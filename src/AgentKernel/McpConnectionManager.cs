using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    [DataContract] public sealed class McpServerConfiguration
    {
        [DataMember]public string Id=Guid.NewGuid().ToString("N"),Name="MCP",Transport="http-json",Endpoint="",WorkingDirectory="";
        [DataMember]public string[] Arguments=new string[0];[DataMember]public bool Enabled;
        [DataMember]public ConnectionSettings Credentials=new ConnectionSettings();
        public override string ToString(){return Name;}
        public McpServerConfiguration Copy(){var copy=(McpServerConfiguration)MemberwiseClone();copy.Arguments=(string[])(Arguments??new string[0]).Clone();copy.Credentials=(Credentials??new ConnectionSettings()).Copy();return copy;}
    }
    public interface IMcpConnectionState {bool Connected {get;}}
    public sealed class McpConnectionStatus {public string Id,State,Error;public int Tools;}
    public sealed class McpConnectionManager:IDisposable
    {
        sealed class Entry{internal string Id,Display="",State="disconnected",Error="";internal int Generation;internal IMcpTransport Transport;internal CancellationTokenSource Cancel;internal McpToolDefinition[] Tools=new McpToolDefinition[0];}
        readonly object gate=new object();readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();readonly Func<McpServerConfiguration,IMcpTransport> factory;bool disposed;
        public event Action Changed;
        public McpConnectionManager(Func<McpServerConfiguration,IMcpTransport> factory=null){this.factory=factory??Create;}
        static string DisplayEndpoint(McpServerConfiguration config){Uri uri;return config.Transport=="http-json"&&Uri.TryCreate(config.Endpoint,UriKind.Absolute,out uri)?uri.GetLeftPart(UriPartial.Path):config.Endpoint;}
        static IMcpTransport Create(McpServerConfiguration configuration)
        {
            if(configuration.Transport=="http-json"){Uri uri;if(!Uri.TryCreate(configuration.Endpoint,UriKind.Absolute,out uri)||uri.UserInfo.Length>0||(uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback)))throw new ArgumentException("MCP HTTP 需使用 HTTPS 或本机 fixture 地址。");return new McpHttpTransport(uri,null,configuration.Credentials.ApiKey);}
            if(configuration.Transport!="stdio"||!Path.IsPathRooted(configuration.Endpoint)||!File.Exists(configuration.Endpoint))throw new ArgumentException("stdio 需要已有可执行文件的绝对路径。");
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("stdio 连接当前仅在 Windows 验证。");
            string args=String.Join(" ",configuration.Arguments.Select(WindowsCommandRunner.Quote));return new McpStdioTransport(configuration.Endpoint,args,configuration.WorkingDirectory);
        }
        public McpConnectionStatus[] Status {get{lock(gate)return entries.Values.Select(e=>new McpConnectionStatus{Id=e.Id,State=e.State,Error=e.Error,Tools=e.Tools.Length}).ToArray();}}
        public async Task ConnectAsync(McpServerConfiguration configuration,CancellationToken cancel)
        {
            if(configuration==null||!configuration.Enabled||String.IsNullOrWhiteSpace(configuration.Id))throw new InvalidOperationException("请先保存并启用该服务器。");var config=configuration.Copy();Entry entry;int generation;CancellationTokenSource connectionCancel;
            lock(gate){if(disposed)throw new ObjectDisposedException("McpConnectionManager");if(!entries.TryGetValue(config.Id,out entry)){if(entries.Count>=16)throw new InvalidOperationException("最多连接 16 个服务器。");entry=new Entry{Id=config.Id};entries.Add(config.Id,entry);}if(entry.State=="connected"||entry.State=="connecting")return;entry.State="connecting";entry.Error="";generation=++entry.Generation;entry.Cancel=CancellationTokenSource.CreateLinkedTokenSource(cancel);connectionCancel=entry.Cancel;connectionCancel.CancelAfter(TimeSpan.FromSeconds(30));entry.Display=config.Name+" / "+DisplayEndpoint(config);}
            Notify();IMcpTransport transport=null;
            try{transport=factory(config);var http=transport as McpHttpTransport;if(http!=null)await http.InitializeAsync(connectionCancel.Token).ConfigureAwait(false);var tools=await transport.ListToolsAsync(connectionCancel.Token).ConfigureAwait(false);if(tools==null||tools.Length>128)throw new ApiException("MCP 工具目录超过容量。");
                lock(gate){if(disposed||entry.Generation!=generation||connectionCancel.IsCancellationRequested)throw new OperationCanceledException();entry.Transport=transport;entry.Tools=tools;entry.State="connected";connectionCancel.CancelAfter(Timeout.Infinite);transport=null;}
            }catch(Exception){lock(gate)if(entry.Generation==generation){entry.State="failed";entry.Error="连接未完成，请核对服务器配置与协议；当前支持 2025-03-26 stdio 或 HTTP JSON 子集。";entry.Tools=new McpToolDefinition[0];}throw;}
            finally{var cleanup=transport as IDisposable;if(cleanup!=null)cleanup.Dispose();Notify();}
        }
        public void Disconnect(string id)
        {Disconnect(id,0);}
        void Disconnect(string id,int expectedGeneration)
        {
            IMcpTransport transport;CancellationTokenSource cancel;lock(gate){Entry entry;if(!entries.TryGetValue(id??"",out entry)||(expectedGeneration!=0&&entry.Generation!=expectedGeneration))return;entry.Generation++;entry.State="disconnected";entry.Tools=new McpToolDefinition[0];transport=entry.Transport;entry.Transport=null;cancel=entry.Cancel;entry.Cancel=null;}
            if(cancel!=null)cancel.Cancel();var owned=transport as IDisposable;if(owned!=null)owned.Dispose();Notify();
        }
        public IMcpTransport[] ConnectedTransports(){lock(gate)return entries.Values.Where(e=>e.State=="connected").Select(e=>(IMcpTransport)new BoundTransport(this,e.Id,e.Generation,e.Tools,e.Display)).ToArray();}
        bool IsConnected(string id,int generation){lock(gate){Entry entry;return !disposed&&entries.TryGetValue(id,out entry)&&entry.Generation==generation&&entry.State=="connected";}}
        async Task<string> CallAsync(string id,int generation,string name,Dictionary<string,object> args,CancellationToken cancel)
        {
            Entry entry;IMcpTransport transport;CancellationToken lifetime;lock(gate){if(!entries.TryGetValue(id,out entry)||!IsConnected(id,generation))throw new InvalidOperationException("MCP 连接已失效，请重新连接。");transport=entry.Transport;lifetime=entry.Cancel.Token;}
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(cancel,lifetime))try{return await transport.CallAsync(name,args,linked.Token).ConfigureAwait(false);}catch(Exception){Disconnect(id,generation);throw;}
        }
        sealed class BoundTransport:IMcpTransport,IMcpConnectionState
        {
            readonly McpConnectionManager manager;readonly string id,display;readonly int generation;readonly Dictionary<string,McpToolDefinition> names=new Dictionary<string,McpToolDefinition>();
            internal BoundTransport(McpConnectionManager manager,string id,int generation,McpToolDefinition[] tools,string display){this.display=display;this.manager=manager;this.id=id;this.generation=generation;foreach(var tool in tools){if(tool==null||String.IsNullOrWhiteSpace(tool.Name))continue;string alias="s"+Hash(id)+"_t"+Hash(tool.Name);if(names.ContainsKey(alias))throw new InvalidOperationException("MCP 工具 ID 冲突。");names.Add(alias,tool);}}
            static string Hash(string value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,24).ToLowerInvariant();}
            public bool Connected {get{return manager.IsConnected(id,generation);}}
            public Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel){cancel.ThrowIfCancellationRequested();return Task.FromResult(!Connected?new McpToolDefinition[0]:names.Select(p=>new McpToolDefinition{Name=p.Key,Description="服务器 "+display+" / "+p.Value.Name+"："+p.Value.Description,ApprovalResource=display+" / 操作 "+p.Value.Name,InputSchema=p.Value.InputSchema}).ToArray());}
            public Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel){McpToolDefinition original;if(!names.TryGetValue(name,out original))throw new ArgumentException("未知 MCP 工具。");return manager.CallAsync(id,generation,original.Name,args,cancel);}
        }
        void Notify(){var changed=Changed;if(!disposed&&changed!=null)changed();}
        public void Dispose(){string[] ids;lock(gate){if(disposed)return;disposed=true;ids=entries.Keys.ToArray();}foreach(string id in ids)Disconnect(id);}
    }
}
