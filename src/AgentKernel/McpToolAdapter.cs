using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class McpToolDefinition { public string Name,Description,ApprovalResource;public Dictionary<string,object> InputSchema; }
    public interface IMcpTransport
    {
        Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel);
        Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel);
    }
    public sealed class McpHttpTransport : IMcpTransport,IDisposable
    {
        readonly Uri endpoint;readonly HttpClient http;readonly string token;
        string sessionId="";
        public async Task InitializeAsync(CancellationToken cancel)
        {
            await CallRpcAsync("initialize",new Dictionary<string,object>{{"protocolVersion","2025-03-26"},{"capabilities",new Dictionary<string,object>()},{"clientInfo",new{name="DeskPet",version="1"}}},cancel).ConfigureAwait(false);
            var payload=new Dictionary<string,object>{{"jsonrpc","2.0"},{"method","notifications/initialized"}};
            // Notification responses need only their status; never buffer an unbounded body.
            using(var request=CreateRequest(payload))using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false)){if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Get("mcpToolAdapter.initializeAsync.text"));}
        }
        HttpRequestMessage CreateRequest(object payload)
        {var request=new HttpRequestMessage(HttpMethod.Post,endpoint);request.Content=new StringContent(Serialize(payload),Encoding.UTF8,"application/json");request.Headers.Accept.ParseAdd("application/json");request.Headers.Accept.ParseAdd("text/event-stream");if(sessionId.Length>0)request.Headers.Add("Mcp-Session-Id",sessionId);if(!String.IsNullOrEmpty(token))request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);return request;}
        public void Dispose(){http.Dispose();}
        public McpHttpTransport(Uri endpoint,HttpMessageHandler handler,string token)
        {
            if(endpoint==null)throw new ArgumentNullException("endpoint");this.endpoint=endpoint;this.token=token??"";
            http=new HttpClient(handler??new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        }
        public async Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel)
        {
            string json=await CallRpcAsync("tools/list",new Dictionary<string,object>(),cancel).ConfigureAwait(false);var result=GetMap(Deserialize(json),"result");var tools=GetList(result,"tools");var output=new List<McpToolDefinition>();
            foreach(var item in tools){var map=item as Dictionary<string,object>;if(map==null)continue;output.Add(new McpToolDefinition{Name=GetString(map,"name",""),Description=GetString(map,"description",""),InputSchema=ReadInputSchema(map)});}
            return output.ToArray();
        }
        public async Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel)
        {
            string json=await CallRpcAsync("tools/call",new Dictionary<string,object>{{"name",name},{"arguments",args??new Dictionary<string,object>()}},cancel).ConfigureAwait(false);return ParseToolResult(json);
        }
        async Task<string> CallRpcAsync(string method,Dictionary<string,object> parameters,CancellationToken cancel)
        {
            var payload=new Dictionary<string,object>{{"jsonrpc","2.0"},{"id",Guid.NewGuid().ToString("N")},{"method",method},{"params",parameters}};
            using(var request=CreateRequest(payload))using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false)){
                if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Get("mcpToolAdapter.callRpcAsync.text"));
                if(response.Content.Headers.ContentType!=null&&response.Content.Headers.ContentType.MediaType=="text/event-stream")throw new ApiException(TextResources.Get("mcpToolAdapter.callRpcAsync.text.alternativeA"));
                IEnumerable<string> ids;if(response.Headers.TryGetValues("Mcp-Session-Id",out ids))sessionId=ids.First();
                using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()){byte[] buffer=new byte[8192];int read;while((read=await stream.ReadAsync(buffer,0,buffer.Length,cancel).ConfigureAwait(false))>0){if(output.Length+read>2*1024*1024)throw new ApiException(TextResources.Get("mcpToolAdapter.callRpcAsync.text.alternativeB"));output.Write(buffer,0,read);}string json=Encoding.UTF8.GetString(output.ToArray());if(GetMap(Deserialize(json),"error")!=null&&method!="tools/call")throw new ApiException(TextResources.Get("mcpToolAdapter.callRpcAsync.text.alternativeC"));return json;}}
        }
        internal static string ParseToolResult(string json)
        {
            var root=Deserialize(json);var error=GetMap(root,"error");if(error!=null)return Failure(TextResources.Get("mcpToolAdapter.parseToolResult.failure"),error);var result=GetMap(root,"result");if(result==null)throw new ApiException(TextResources.Get("mcpToolAdapter.parseToolResult.text"));var content=GetList(result,"content");
            object failed;if(result.TryGetValue("isError",out failed)&&failed is bool&&(bool)failed)return Failure(TextResources.Get("mcpToolAdapter.parseToolResult.failure.alternativeA"),result);
            if(content.Count==1 && (result==null||!result.ContainsKey("structuredContent"))){var map=content[0] as Dictionary<string,object>;if(map!=null && GetString(map,"type","")=="text")return GetString(map,"text","");}
            return Serialize(result??root);
        }
        static string Failure(string message,object payload){string details=Serialize(payload);return TextResources.Get("mcpToolAdapter.failure.text")+message+"。\n"+(details.Length>22000?TextResources.Format("mcpToolAdapter.failure.template",details.Substring(0,22000)):details);}
        internal static Dictionary<string,object> ReadInputSchema(Dictionary<string,object> tool)
        {
            object raw;if(!tool.TryGetValue("inputSchema",out raw))return new Dictionary<string,object>();
            var schema=raw as Dictionary<string,object>;if(schema==null)throw new ApiException(TextResources.Get("mcpToolAdapter.readInputSchema.text")+GetString(tool,"name",TextResources.Get("mcpToolAdapter.readInputSchema.getString"))+TextResources.Get("mcpToolAdapter.readInputSchema.text.alternativeA"));return schema;
        }
        internal static Dictionary<string,object> Deserialize(string json){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.DeserializeObject(json??"") as Dictionary<string,object>??new Dictionary<string,object>();}
        internal static string Serialize(object value){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Serialize(value);}
        internal static Dictionary<string,object> GetMap(Dictionary<string,object> map,string key){object value;return map!=null&&map.TryGetValue(key,out value)?value as Dictionary<string,object>:null;}
        internal static List<object> GetList(Dictionary<string,object> map,string key){object value;if(map==null||!map.TryGetValue(key,out value))return new List<object>();var list=value as System.Collections.IEnumerable;if(list==null||value is string)return new List<object>();return list.Cast<object>().ToList();}
        internal static string GetString(Dictionary<string,object> map,string key,string fallback){object value;return map!=null&&map.TryGetValue(key,out value)&&value is string?(string)value:fallback;}
    }
    public sealed class McpStdioProtocol : IMcpTransport
    {
        readonly TextReader reader;readonly TextWriter writer;readonly SemaphoreSlim gate=new SemaphoreSlim(1,1),initGate=new SemaphoreSlim(1,1);int nextId;bool initialized;
        public McpStdioProtocol(TextReader reader,TextWriter writer){if(reader==null)throw new ArgumentNullException("reader");if(writer==null)throw new ArgumentNullException("writer");this.reader=reader;this.writer=writer;}
        public async Task InitializeAsync(CancellationToken cancel)
        {
            if(initialized)return;await initGate.WaitAsync(cancel).ConfigureAwait(false);try{if(initialized)return;await RequestAsync("initialize",new Dictionary<string,object>{{"protocolVersion","2025-03-26"},{"capabilities",new Dictionary<string,object>()},{"clientInfo",new Dictionary<string,object>{{"name","DeskPet"},{"version","1"}}}},cancel).ConfigureAwait(false);await NotifyAsync("notifications/initialized",new Dictionary<string,object>(),cancel).ConfigureAwait(false);initialized=true;}finally{initGate.Release();}
        }
        public async Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel)
        {
            await InitializeAsync(cancel).ConfigureAwait(false);var result=GetMap(await RequestAsync("tools/list",new Dictionary<string,object>(),cancel).ConfigureAwait(false),"result");var output=new List<McpToolDefinition>();
            foreach(var item in GetList(result,"tools")){var map=item as Dictionary<string,object>;if(map==null)continue;output.Add(new McpToolDefinition{Name=GetString(map,"name",""),Description=GetString(map,"description",""),InputSchema=McpHttpTransport.ReadInputSchema(map)});}
            return output.ToArray();
        }
        public Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel)
        {
            if(String.IsNullOrWhiteSpace(name))throw new ArgumentException(TextResources.Get("mcpToolAdapter.callAsync.text"));
            return CallCoreAsync(name,args??new Dictionary<string,object>(),cancel);
        }
        async Task<string> CallCoreAsync(string name,Dictionary<string,object> args,CancellationToken cancel)
        {
            await InitializeAsync(cancel).ConfigureAwait(false);var root=await RequestAsync("tools/call",new Dictionary<string,object>{{"name",name},{"arguments",args}},cancel).ConfigureAwait(false);return McpHttpTransport.ParseToolResult(Serialize(root));
        }
        async Task<Dictionary<string,object>> RequestAsync(string method,Dictionary<string,object> parameters,CancellationToken cancel)
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                int id=Interlocked.Increment(ref nextId);var payload=new Dictionary<string,object>{{"jsonrpc","2.0"},{"id",id},{"method",method},{"params",parameters??new Dictionary<string,object>()}};
                await writer.WriteLineAsync(Serialize(payload)).ConfigureAwait(false);await writer.FlushAsync().ConfigureAwait(false);
                while(true)
                {
                    string line=await ReadLineAsync(cancel).ConfigureAwait(false);if(line==null)throw new ApiException(TextResources.Get("mcpToolAdapter.requestAsync.text"));if(String.IsNullOrWhiteSpace(line))continue;
                    var message=Deserialize(line);object rawId;if(message.TryGetValue("id",out rawId) && String.Equals(Convert.ToString(rawId),id.ToString(),StringComparison.Ordinal))
                    {var error=GetMap(message,"error");if(error!=null&&method!="tools/call")throw new ApiException(TextResources.Get("mcpToolAdapter.requestAsync.text.alternativeA")+GetString(error,"message",TextResources.Get("mcpToolAdapter.requestAsync.getString")));return message;}
                    if(message.ContainsKey("method") && message.ContainsKey("id")){await writer.WriteLineAsync(Serialize(new Dictionary<string,object>{{"jsonrpc","2.0"},{"id",message["id"]},{"result",new Dictionary<string,object>()}})).ConfigureAwait(false);await writer.FlushAsync().ConfigureAwait(false);}
                }
            }finally{gate.Release();}
        }
        async Task NotifyAsync(string method,Dictionary<string,object> parameters,CancellationToken cancel)
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);try{await writer.WriteLineAsync(Serialize(new Dictionary<string,object>{{"jsonrpc","2.0"},{"method",method},{"params",parameters??new Dictionary<string,object>()}})).ConfigureAwait(false);await writer.FlushAsync().ConfigureAwait(false);}finally{gate.Release();}
        }
        async Task<string> ReadLineAsync(CancellationToken cancel)
        {
            var read=reader.ReadLineAsync();if(read==await Task.WhenAny(read,Task.Delay(Timeout.Infinite,cancel)).ConfigureAwait(false))return await read.ConfigureAwait(false);cancel.ThrowIfCancellationRequested();return null;
        }
        static string Serialize(object value){return McpHttpTransport.Serialize(value);}static Dictionary<string,object> Deserialize(string value){return McpHttpTransport.Deserialize(value);}static Dictionary<string,object> GetMap(Dictionary<string,object> value,string key){return McpHttpTransport.GetMap(value,key);}static List<object> GetList(Dictionary<string,object> value,string key){return McpHttpTransport.GetList(value,key);}static string GetString(Dictionary<string,object> value,string key,string fallback){return McpHttpTransport.GetString(value,key,fallback);}
    }
    public sealed class McpStdioTransport : IMcpTransport, IDisposable
    {
        readonly string fileName,arguments,workingDirectory;readonly object gate=new object();WindowsCommandRunner.StdioProcess process;McpStdioProtocol protocol;bool disposed;
        public McpStdioTransport(string fileName,string arguments,string workingDirectory){if(String.IsNullOrWhiteSpace(fileName))throw new ArgumentException(TextResources.Get("mcpToolAdapter.mcpStdioTransport.text"));this.fileName=fileName;this.arguments=arguments??"";this.workingDirectory=workingDirectory??"";}
        public Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel){cancel.ThrowIfCancellationRequested();return EnsureProtocol().ListToolsAsync(cancel);}
        public Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel){cancel.ThrowIfCancellationRequested();return EnsureProtocol().CallAsync(name,args,cancel);}
        McpStdioProtocol EnsureProtocol()
        {
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("McpStdioTransport");if(protocol!=null)return protocol;
                try{process=WindowsCommandRunner.StartStdioRaw(fileName,arguments,workingDirectory);protocol=new McpStdioProtocol(process.Output,process.Input);return protocol;}
                catch(Exception e){throw new ApiException(TextResources.Format("mcpToolAdapter.ensureProtocol.template",e.Message));}
            }
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;if(process!=null){process.Dispose();process=null;}protocol=null;}}
    }
    public sealed class McpToolAdapter
    {
        readonly IMcpTransport transport;public McpToolAdapter(IMcpTransport transport){if(transport==null)throw new ArgumentNullException("transport");this.transport=transport;}
        public AgentToolDefinition[] Register(AgentToolRuntime runtime)
        {
            if(runtime==null)throw new ArgumentNullException("runtime");
            var tools=transport.ListToolsAsync(CancellationToken.None).GetAwaiter().GetResult();var output=new List<AgentToolDefinition>();
            foreach(var tool in tools??new McpToolDefinition[0])
            {
                if(tool==null || !ValidName(tool.Name))continue;
                string name="mcp_"+tool.Name.Replace("-","_");if(runtime.Has(name))continue;var schema=InputSchema(tool);var properties=McpHttpTransport.GetMap(schema,"properties");var required=McpHttpTransport.GetList(schema,"required").Cast<string>().ToArray();var definition=new AgentToolDefinition{Name=name,Description=tool.Description??TextResources.Get("mcpToolAdapter.register.descriptionToolDescription"),ApprovalResource=tool.ApprovalResource,ReadOnly=false,Properties=properties,Required=required,InputSchema=schema};
                runtime.Register(definition,(args,context,cancel)=>transport.CallAsync(tool.Name,args??new Dictionary<string,object>(),cancel));output.Add(definition);
            }
            return output.ToArray();
        }
        static Dictionary<string,object> InputSchema(McpToolDefinition tool)
        {
            var schema=McpHttpTransport.Deserialize(McpHttpTransport.Serialize(tool.InputSchema??new Dictionary<string,object>()));object raw;
            if(schema.TryGetValue("type",out raw)&&!Object.Equals(raw,"object"))throw InvalidSchema(tool,TextResources.Get("mcpToolAdapter.inputSchema.invalidSchema"));
            schema["type"]="object";
            if(schema.TryGetValue("properties",out raw)){if(!(raw is Dictionary<string,object>))throw InvalidSchema(tool,TextResources.Get("mcpToolAdapter.inputSchema.invalidSchema.alternativeA"));}
            else schema["properties"]=new Dictionary<string,object>();
            if(schema.TryGetValue("required",out raw)&&(!(raw is object[])||((object[])raw).Any(value=>!(value is string))))throw InvalidSchema(tool,TextResources.Get("mcpToolAdapter.inputSchema.invalidSchema.alternativeB"));
            return schema;
        }
        static InvalidOperationException InvalidSchema(McpToolDefinition tool,string detail){return new InvalidOperationException(TextResources.Format("mcpToolAdapter.invalidSchema.template",tool.Name,detail));}
        static bool ValidName(string name)
        {
            if(String.IsNullOrWhiteSpace(name) || name.Length>100)return false;foreach(char c in name)if(!(Char.IsLetterOrDigit(c)||c=='_'||c=='-'||c=='.'))return false;return true;
        }
    }
}
