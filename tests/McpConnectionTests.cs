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
        sealed class SchemaTransport:IMcpTransport
        {
            internal readonly Dictionary<string,object> Schema;
            internal SchemaTransport(string json){Schema=McpHttpTransport.Deserialize(json);}
            public Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel){return Task.FromResult(new[]{new McpToolDefinition{Name="schema_probe",InputSchema=Schema}});}
            public Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel){return Task.FromResult("unused");}
        }
        sealed class FaultHandler:HttpMessageHandler
        {
            internal string Mode;internal int ToolCalls;internal bool Released;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
            {
                var input=McpHttpTransport.Deserialize(await request.Content.ReadAsStringAsync());string method=McpHttpTransport.GetString(input,"method","");object id;input.TryGetValue("id",out id);
                if(method=="tools/call"&&++ToolCalls==1)
                {
                    if(Mode=="cancel")await Task.Delay(Timeout.Infinite,cancel);
                    if(Mode=="transport")throw new HttpRequestException("fixture connection lost");
                    if(Mode=="protocol")return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{malformed",Encoding.UTF8,"application/json")};
                    if(Mode=="rpc")return Response(new{jsonrpc="2.0",id,error=new{code=-32602,message="invalid fixture path",data=new{path="missing.txt"}}});
                    if(Mode=="business")return Response(new{jsonrpc="2.0",id,result=new{isError=true,content=new[]{new{type="text",text="fixture file not found: missing.txt"}},structuredContent=new{retry="existing.txt"}}});
                }
                object result=method=="initialize"?(object)new{protocolVersion="2025-03-26",capabilities=new{},serverInfo=new{name="fixture",version="1"}}:method=="tools/list"?new{tools=new[]{new{name="retry",inputSchema=new{type="object"}}}}:(object)new{content=new[]{new{type="text",text="recovered"}}};
                return Response(new{jsonrpc="2.0",id,result});
            }
            static HttpResponseMessage Response(object value){return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(McpHttpTransport.Serialize(value),Encoding.UTF8,"application/json")};}
            protected override void Dispose(bool disposing){Released=true;base.Dispose(disposing);}
        }
        public static int Main(string[] args){if(args.Length>0&&args[0]=="--server")return Server(args);try{if(args.Contains("--schema-regression"))SchemaRegression();else if(args.Contains("--error-regression"))ErrorRegression().GetAwaiter().GetResult();else{SchemaRegression();ErrorRegression().GetAwaiter().GetResult();Run().GetAwaiter().GetResult();}Console.WriteLine("MCP connection tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        internal static int PortableRegression(){try{SchemaRegression();ErrorRegression(true).GetAwaiter().GetResult();Console.WriteLine("MCP portable tests: "+count+" passed");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static Dictionary<string,object> SchemaFor(AgentToolDefinition[] definitions,bool anthropic)
        {
            object[] openai,anthropicTools;AgentToolCatalog.Schemas(false,null,definitions,out openai,out anthropicTools);
            var tools=(object[])new JavaScriptSerializer().DeserializeObject(new JavaScriptSerializer().Serialize(anthropic?anthropicTools:openai));
            var tool=tools.Cast<Dictionary<string,object>>().Single(item=>anthropic?McpHttpTransport.GetString(item,"name","")=="mcp_schema_probe":McpHttpTransport.GetString(McpHttpTransport.GetMap(item,"function"),"name","")=="mcp_schema_probe");
            return anthropic?McpHttpTransport.GetMap(tool,"input_schema"):McpHttpTransport.GetMap(McpHttpTransport.GetMap(tool,"function"),"parameters");
        }
        static void SchemaRegression()
        {
            foreach(string json in new[]{"{\"type\":\"object\"}","{}","{\"type\":\"object\",\"properties\":{\"value\":{\"$ref\":\"#/$defs/Value\"}},\"$defs\":{\"Value\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"value\"],\"allOf\":[{\"minProperties\":1}],\"additionalProperties\":true}"})
            {
                var transport=new SchemaTransport(json);
                {
                    var definitions=new McpToolAdapter(transport).Register(new AgentToolRuntime(AgentPermissionRuntime.DefaultWorkspacePolicy(),null));
                    foreach(bool anthropic in new[]{false,true})
                    {
                        var schema=SchemaFor(definitions,anthropic);var properties=McpHttpTransport.GetMap(schema,"properties");
                        Check(McpHttpTransport.GetString(schema,"type","")=="object"&&properties!=null&&!properties.ContainsKey("type"),"empty MCP input remains an object with an empty property map: "+anthropic);
                        if(json.Contains("$defs"))
                        {
                            Check(McpHttpTransport.Serialize(schema)==McpHttpTransport.Serialize(transport.Schema),"complete root schema, references and composition reach the final protocol payload: "+anthropic);
                        }
                    }
                    transport.Schema["type"]="array";
                    Check(McpHttpTransport.GetString(SchemaFor(definitions,false),"type","")=="object","registered MCP schema is an immutable snapshot of the remote catalogue");
                    if(json.Contains("$defs"))
                    {
                        McpHttpTransport.GetMap(McpHttpTransport.GetMap(transport.Schema,"$defs"),"Value")["type"]="integer";
                        Check(McpHttpTransport.GetString(McpHttpTransport.GetMap(McpHttpTransport.GetMap(SchemaFor(definitions,false),"$defs"),"Value"),"type","")=="string","remote catalogue mutation cannot alter a nested definition after registration");
                    }
                }
            }
            foreach(string json in new[]{"{\"type\":\"array\"}","{\"type\":\"object\",\"properties\":[]}","{\"type\":\"object\",\"required\":\"value\"}"})
            {
                bool rejected=false;try{new McpToolAdapter(new SchemaTransport(json)).Register(new AgentToolRuntime(AgentPermissionRuntime.DefaultWorkspacePolicy(),null));}catch(InvalidOperationException error){rejected=error.Message.Contains("schema_probe")&&error.Message.Contains("schema");}Check(rejected,"unsupported root schema fails locally with tool-specific diagnostics");
            }
        }
        static async Task ErrorRegression(bool portable=false)
        {
            foreach(string mode in new[]{"business","rpc","cancel","protocol","transport"})
            {
                var handler=new FaultHandler{Mode=mode};using(var manager=new McpConnectionManager(config=>new McpHttpTransport(new Uri("http://127.0.0.1/mcp"),handler,"")))
                {
                    await manager.ConnectAsync(new McpServerConfiguration{Id="retry",Enabled=true},CancellationToken.None);var bound=manager.ConnectedTransports().Single();string tool=(await bound.ListToolsAsync(CancellationToken.None)).Single().Name;
                    if(mode=="business"||mode=="rpc")
                    {
                        string result=await bound.CallAsync(tool,new Dictionary<string,object>(),CancellationToken.None);
                        Check(result.StartsWith("错误：")&&result.Contains(mode=="business"?"fixture file not found":"invalid fixture path")&&result.Contains("missing.txt"),"MCP failure preserves its bounded error details: "+mode);
                        Check(manager.Status.Single().State=="connected"&&!handler.Released,"tool/request error retains the connection: "+mode);
                        Check(await bound.CallAsync(tool,new Dictionary<string,object>(),CancellationToken.None)=="recovered","corrected MCP call succeeds on the same transport: "+mode);
                        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mcp-failure-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                        var runtimePermissions=AgentPermissionRuntime.DefaultWorkspacePolicy();runtimePermissions.Add(new AgentPermissionRule{Action="mcp",Resource="**",Effect=AgentPermissionEffect.Ask});
                        var runtimeAudit=new AgentAuditLog(root);var runtime=new AgentToolRuntime(runtimePermissions,runtimeAudit,new Approval());new McpToolAdapter(bound).Register(runtime);handler.ToolCalls=0;
                        string runtimeFailure=await runtime.ExecuteAsync("mcp_"+tool,new Dictionary<string,object>(),new AgentToolExecutionContext{CallId="bad-arguments",TaskId="recovery"},CancellationToken.None);
                        Check(runtimeFailure.StartsWith("错误：")&&runtimeAudit.Events.Any(entry=>entry.Result=="error")&&manager.Status.Single().State=="connected","shared runtime records the failed tool without losing its MCP session: "+mode);
                        Check(await runtime.ExecuteAsync("mcp_"+tool,new Dictionary<string,object>(),new AgentToolExecutionContext{CallId="corrected-arguments",TaskId="recovery"},CancellationToken.None)=="recovered","shared runtime retries corrected arguments on the same transport: "+mode);
                        if(!portable)
                        using(var workspace=new AgentWorkspace(root))
                        {
                            var audit=new AgentAuditLog(root);workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),audit,new Approval(),null,"recovery");workspace.ConfigureProvider("openai","gpt-fixture");workspace.ConfigureMcp(bound);handler.ToolCalls=0;
                            string failed=await workspace.ExecuteAsync("mcp_"+tool,"{}","bad-arguments",CancellationToken.None);
                            Check(failed.StartsWith("错误：")&&audit.Events.Any(entry=>entry.Result=="error")&&manager.Status.Single().State=="connected","workspace records the failed tool honestly without losing its MCP session: "+mode);
                            Check(await workspace.ExecuteAsync("mcp_"+tool,"{}","corrected-arguments",CancellationToken.None)=="recovered","agent can correct arguments and retry after receiving error details: "+mode);
                        }
                    }
                    else if(mode=="cancel")
                    {
                        bool canceled=false;using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(50);try{await bound.CallAsync(tool,new Dictionary<string,object>(),cancel.Token);}catch(OperationCanceledException){canceled=true;}}
                        Check(canceled&&manager.Status.Single().State=="connected"&&!handler.Released,"HTTP request cancellation does not discard a valid session");
                        Check(await bound.CallAsync(tool,new Dictionary<string,object>(),CancellationToken.None)=="recovered","HTTP connection remains usable after user cancellation");
                    }
                    else
                    {
                        bool failed=false;try{await bound.CallAsync(tool,new Dictionary<string,object>(),CancellationToken.None);}catch(Exception){failed=true;}
                        Check(failed&&manager.ConnectedTransports().Length==0&&handler.Released,"invalid protocol or lost transport disconnects and releases ownership: "+mode);
                    }
                }
            }
            string oversized=McpHttpTransport.Serialize(new{result=new{isError=true,content=new[]{new{type="text",text=new string('x',50000)}}}});
            string boundedError=McpHttpTransport.ParseToolResult(oversized);Check(boundedError.StartsWith("错误：")&&boundedError.Length<=24000&&boundedError.Contains("截断"),"business error details remain explicitly bounded");
            var input=new StringReader("{\"id\":1,\"result\":{}}\n{\"id\":2,\"result\":{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"stdio missing file\"}]}}\n{\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"stdio retry ok\"}]}}\n");var protocol=new McpStdioProtocol(input,new StringWriter());
            Check((await protocol.CallAsync("read",new Dictionary<string,object>(),CancellationToken.None)).Contains("stdio missing file"),"stdio preserves business failure content");
            Check(await protocol.CallAsync("read",new Dictionary<string,object>(),CancellationToken.None)=="stdio retry ok","stdio protocol stays synchronized for a corrected call");
        }
        static int Server(string[] args)
        {
            Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);if(args.Length>1){var info=new ProcessStartInfo(AgentCommandExecutor.PowerShellPath(),"-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60"){UseShellExecute=false,CreateNoWindow=true};var child=Process.Start(info);File.WriteAllText(args[1],child.Id.ToString());}
            int toolCalls=0;string line;while((line=Console.ReadLine())!=null){var input=McpHttpTransport.Deserialize(line);object id;if(!input.TryGetValue("id",out id))continue;string method=McpHttpTransport.GetString(input,"method","");if(method=="tools/call"&&args.Contains("--stall-tools"))Thread.Sleep(10000);object result=method=="initialize"?(object)new{protocolVersion="2025-03-26",capabilities=new{},serverInfo=new{name="stdio-fixture",version="1"}}:method=="tools/list"?new{tools=new[]{new{name="read",description="fixture",inputSchema=new{type="object",properties=new{}}}}}:args.Contains("--business-errors")&&++toolCalls==1?(object)new{isError=true,content=new[]{new{type="text",text="stdio missing.txt"}}}:(object)new{content=new[]{new{type="text",text="stdio-result"}}};Console.WriteLine(new JavaScriptSerializer().Serialize(new{jsonrpc="2.0",id,result}));Console.Out.Flush();}return 0;
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
            foreach(string mode in new[]{"--business-errors","--stall-tools"})using(var manager=new McpConnectionManager())
            {
                var config=new McpServerConfiguration{Id="stdio-retry",Enabled=true,Transport="stdio",Endpoint=Process.GetCurrentProcess().MainModule.FileName,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,Arguments=new[]{"--server",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mcp-child-"+Guid.NewGuid().ToString("N")+".txt"),mode}};
                await manager.ConnectAsync(config,CancellationToken.None);var connection=manager.ConnectedTransports().Single();string tool=(await connection.ListToolsAsync(CancellationToken.None)).Single().Name;
                if(mode=="--business-errors")
                {
                    Check((await connection.CallAsync(tool,new Dictionary<string,object>(),CancellationToken.None)).Contains("stdio missing.txt")&&manager.Status.Single().State=="connected","real stdio business error keeps its process and connected tool catalogue");
                    Check(await connection.CallAsync(tool,new Dictionary<string,object>(),CancellationToken.None)=="stdio-result","real stdio process handles the corrected request after a business failure");
                }
                else
                {
                    bool canceled=false;using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(100);try{await connection.CallAsync(tool,new Dictionary<string,object>(),cancel.Token);}catch(OperationCanceledException){canceled=true;}}
                    Check(canceled&&manager.ConnectedTransports().Length==0,"canceled stdio closes its desynchronized stream rather than reusing an abandoned read");
                }
            }
        }
    }
}
