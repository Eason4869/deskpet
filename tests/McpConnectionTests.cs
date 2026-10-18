using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class McpConnectionTests
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        sealed class UnreadableNotificationContent:HttpContent
        {
            internal bool ReadAttempted,Released;
            protected override Task SerializeToStreamAsync(Stream stream,TransportContext context){ReadAttempted=true;throw new InvalidOperationException("Initialized notification response body must not be buffered.");}
            protected override bool TryComputeLength(out long length){length=0;return false;}
            protected override void Dispose(bool disposing){Released=true;base.Dispose(disposing);}
        }
        sealed class Handler:HttpMessageHandler
        {
            internal int Calls;internal bool SessionPreserved=true,RejectNotificationBody;internal UnreadableNotificationContent NotificationBody;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){Calls++;var input=McpHttpTransport.Deserialize(await request.Content.ReadAsStringAsync());string method=McpHttpTransport.GetString(input,"method","");if(method!="initialize")SessionPreserved&=request.Headers.Contains("Mcp-Session-Id");object result=method=="initialize"?(object)new{protocolVersion="2025-03-26",capabilities=new{},serverInfo=new{name="fixture",version="1"}}:method=="tools/list"?new{tools=new[]{new{name="same-tool",description="fixture",inputSchema=new{type="object",properties=new{}}}}}:(object)new{content=new object[]{new{type="text",text="first"},new{type="text",text="second"},new{type="image",data="fixture",mimeType="image/png"}},structuredContent=new{answer=7}};object id;input.TryGetValue("id",out id);var response=new HttpResponseMessage(method=="notifications/initialized"?HttpStatusCode.Accepted:HttpStatusCode.OK){Content=RejectNotificationBody&&method=="notifications/initialized"?(HttpContent)(NotificationBody=new UnreadableNotificationContent()):new StringContent(new JavaScriptSerializer().Serialize(new{jsonrpc="2.0",id,result}),Encoding.UTF8,"application/json")};response.Headers.Add("Mcp-Session-Id","fixture-session");return response;}
        }
        sealed class Approval:IAgentApprovalProvider{public int Calls;public AgentApprovalRequest Request;public Task<bool> ApproveAsync(AgentApprovalRequest request){Request=request;Calls++;return Task.FromResult(true);}}
        public static int Main(string[] args){if(args.Length>0&&args[0]=="--server")return Server(args);try{Run().GetAwaiter().GetResult();Console.WriteLine("MCP connection tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static int Server(string[] args)
        {
            Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);if(args.Length>1){var info=new ProcessStartInfo(AgentCommandExecutor.PowerShellPath(),"-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60"){UseShellExecute=false,CreateNoWindow=true};var child=Process.Start(info);File.WriteAllText(args[1],child.Id.ToString());}
            string line;while((line=Console.ReadLine())!=null){var input=McpHttpTransport.Deserialize(line);object id;if(!input.TryGetValue("id",out id))continue;string method=McpHttpTransport.GetString(input,"method","");object result=method=="initialize"?(object)new{protocolVersion="2025-03-26",capabilities=new{},serverInfo=new{name="stdio-fixture",version="1"}}:method=="tools/list"?new{tools=new[]{new{name="read",description="fixture",inputSchema=new{type="object",properties=new{}}}}}:(object)new{content=new[]{new{type="text",text="stdio-result"}}};Console.WriteLine(new JavaScriptSerializer().Serialize(new{jsonrpc="2.0",id,result}));Console.Out.Flush();}return 0;
        }
        static async Task Run()
        {
            var notificationHandler=new Handler{RejectNotificationBody=true};using(var transport=new McpHttpTransport(new Uri("http://127.0.0.1/mcp"),notificationHandler,"fixture-key")){
                await transport.InitializeAsync(CancellationToken.None);
                Check(notificationHandler.NotificationBody!=null&&!notificationHandler.NotificationBody.ReadAttempted&&notificationHandler.NotificationBody.Released,"HTTP initialized notification accepts headers and disposes an unread response body without buffering");
                Check((await transport.ListToolsAsync(CancellationToken.None)).Length==1&&notificationHandler.SessionPreserved,"skipping notification body preserves HTTP initialization session and subsequent tool discovery");
            }
            var handlers=new List<Handler>();using(var manager=new McpConnectionManager(c=>{var handler=new Handler();handlers.Add(handler);return new McpHttpTransport(new Uri("http://127.0.0.1/mcp"),handler,"fixture-key");})){
                Check(manager.ConnectedTransports().Length==0&&handlers.Count==0,"creating manager does not start or connect servers");var a=new McpServerConfiguration{Id="server-a",Name="Evidence server A",Endpoint="http://127.0.0.1/mcp",Enabled=true};var b=new McpServerConfiguration{Id="server-b",Enabled=true};await manager.ConnectAsync(a,CancellationToken.None);await manager.ConnectAsync(a,CancellationToken.None);Check(handlers.Count==1,"repeated connect owns only one transport");await manager.ConnectAsync(b,CancellationToken.None);
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mcp-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var approval=new Approval();using(var workspace=new AgentWorkspace(root)){workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,approval,null,"task");foreach(var t in manager.ConnectedTransports())workspace.ConfigureMcp(t);workspace.ConfigureDesktopSession();var tools=workspace.AvailableToolNames.Split(new[]{", "},StringSplitOptions.RemoveEmptyEntries).Where(n=>n.StartsWith("mcp_")).ToArray();Check(tools.Length==2&&tools[0]!=tools[1],"same remote tool name has distinct stable server identities");string result=await workspace.ExecuteAsync(tools[0],"{}","call",CancellationToken.None);Check(approval.Calls==1&&result.Contains("first")&&result.Contains("second")&&result.Contains("image")&&result.Contains("answer"),"connected MCP preserves multiblock/structured output and requires approval");Check(approval.Request.Resource.Contains("Evidence server A")&&approval.Request.Resource.Contains("same-tool")&&approval.Request.Resource.Contains("http://127.0.0.1/mcp"),"approval identifies bound server, endpoint and original operation");Check(handlers.All(h=>h.SessionPreserved),"HTTP initialization session is retained");manager.Disconnect("server-a");manager.Disconnect("server-b");Check(!new JavaScriptSerializer().Serialize(workspace.OpenAITools).Contains("mcp_"),"disconnect immediately removes tools from request schemas");Check((await workspace.ExecuteAsync(tools[0],"{}","late",CancellationToken.None)).StartsWith("错误："),"disconnected tool cannot execute through a saved entry");}
            }
            string pidFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mcp-child-"+Guid.NewGuid().ToString("N")+".txt");using(var manager=new McpConnectionManager()){
                var config=new McpServerConfiguration{Id="stdio",Enabled=true,Transport="stdio",Endpoint=Process.GetCurrentProcess().MainModule.FileName,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,Arguments=new[]{"--server",pidFile}};await manager.ConnectAsync(config,CancellationToken.None);var connection=manager.ConnectedTransports().Single();var tools=await connection.ListToolsAsync(CancellationToken.None);Check((await connection.CallAsync(tools[0].Name,new Dictionary<string,object>(),CancellationToken.None))=="stdio-result","real stdio process completes initialize/list/call");int pid=Int32.Parse(File.ReadAllText(pidFile));manager.Disconnect(config.Id);bool stopped=false;try{stopped=Process.GetProcessById(pid).WaitForExit(3000);}catch(ArgumentException){stopped=true;}Check(stopped,"disconnect cleans only the owned stdio process tree");}
        }
    }
}
