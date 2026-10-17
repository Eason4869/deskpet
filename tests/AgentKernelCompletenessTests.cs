using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentKernelCompletenessTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                var claims=new ExecutionClaimStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"complete-claims-"+Guid.NewGuid().ToString("N")));
                var manager=new AgentSubagentManager(claims);var scheduler=new AgentSubagentScheduler(manager,1);var profile=new AgentProfile{Id="explore",Mode="subagent",ReadOnly=true};
                var first=scheduler.Schedule("parent-1",profile,"first");var second=scheduler.Schedule("parent-1",profile,"second");var third=scheduler.Schedule("parent-1",profile,"third");
                Check(first.Status=="running" && second.Status=="queued" && third.Status=="queued","scheduler bounds concurrent children and queues the rest");
                string queuedId=second.Id;Check(scheduler.Complete(first.Id,"done") && second.Status=="running" && second.Id==queuedId && third.Status=="queued","completing a child starts the next queued child with its stable ID");
                Check(scheduler.Interrupt(third.Id) && scheduler.List("parent-1").Count(s=>s.Id==third.Id)==0,"interrupting a queued child removes it without starting work");
                Check(scheduler.Fail(second.Id,"failed") && scheduler.List("parent-1").Single(s=>s.Id==second.Id).Status=="failed","scheduler records child failure");
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"complete-workspace-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var workspace=new AgentWorkspace(root);var audit=new AgentAuditLog(root);workspace.ConfigureKernel(new AgentPermissionRuntime(),audit);
                workspace.ConfigureProvider("unknown","model");Check(workspace.OpenAITools.Length==0 && workspace.AnthropicTools.Length==0,"unsupported provider advertises no tools");
                workspace.ConfigureProvider("mimo","mimo-v2.5-flash");Check(workspace.OpenAITools.Length>0 && workspace.AnthropicTools.Length>0,"supported provider advertises protocol tool schemas");
                var mcpPermissions=new AgentPermissionRuntime();mcpPermissions.Add(new AgentPermissionRule{Action="mcp",Resource="**",Effect=AgentPermissionEffect.Allow});workspace.ConfigureKernel(mcpPermissions,new AgentAuditLog(Path.Combine(root,"mcp")));workspace.ConfigureProvider("mimo","mimo-v2.5-flash");workspace.ConfigureMcp(new FakeMcpTransport());workspace.ConfigureMcp(new FakeMcpTransport("second:"));
                var schemaJson=new JavaScriptSerializer().Serialize(workspace.OpenAITools);Check(schemaJson.Contains("mcp_echo"),"configured MCP tools enter the advertised protocol schema");var schemaRuntime=new AgentToolRuntime(new AgentPermissionRuntime(),new AgentAuditLog(Path.Combine(root,"schema")));var schemaTools=new McpToolAdapter(new FakeMcpTransport()).Register(schemaRuntime);Check(schemaTools[0].Properties.ContainsKey("value") && schemaTools[0].Required.SequenceEqual(new[]{"value"}),"MCP schema maps JSON Schema properties and required fields into the tool contract");
                string mcpResult=workspace.ExecuteAsync("mcp_echo",new JavaScriptSerializer().Serialize(new{value="hi"}),"call-mcp-1",CancellationToken.None).GetAwaiter().GetResult();Check(mcpResult.Contains("hi"),"workspace dispatches MCP tools through the unified runtime");
                workspace.PlanMode=true;Check(!new JavaScriptSerializer().Serialize(workspace.OpenAITools).Contains("mcp_echo"),"plan mode does not advertise non-readonly MCP tools");workspace.PlanMode=false;
                workspace.ConfigureKernel(new AgentPermissionRuntime(),audit);workspace.BeginTask(new AgentTaskState{Id="task-stable",Workspace=root,Goal="read"});
                string result=workspace.ExecuteAsync("read_file",new JavaScriptSerializer().Serialize(new{path="missing.txt"}),"call-fixed-123",CancellationToken.None).GetAwaiter().GetResult();
                Check(result.StartsWith("错误：") && audit.Events.Last().CallId=="call-fixed-123","real tool calls retain the model CallId in audit");workspace.FinishTask("complete","done");
                var input=new StringReader("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"echo\",\"description\":\"echo\",\"inputSchema\":{\"type\":\"object\"}}]}}\n{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}\n");
                var output=new StringWriter();var protocol=new McpStdioProtocol(input,output);
                protocol.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();var tools=protocol.ListToolsAsync(CancellationToken.None).GetAwaiter().GetResult();string called=protocol.CallAsync("echo",new Dictionary<string,object>{{"value","hi"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(tools.Length==1 && tools[0].Name=="echo" && called=="ok","MCP stdio protocol initializes, lists and calls newline-delimited JSON-RPC");
                string wire=output.ToString();Check(wire.Contains("\"method\":\"initialize\"") && wire.Contains("\"method\":\"notifications/initialized\"") && wire.Contains("\"method\":\"tools/list\"") && wire.Contains("\"method\":\"tools/call\""),"MCP stdio protocol writes the required handshake and requests");
                Console.WriteLine("Agent kernel completeness tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        sealed class FakeMcpTransport:IMcpTransport
        {
            readonly string prefix;public FakeMcpTransport(){}public FakeMcpTransport(string prefix){this.prefix=prefix??"";}
            public Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel){var schema=new Dictionary<string,object>{{"type","object"},{"properties",new Dictionary<string,object>{{"value",new Dictionary<string,object>{{"type","string"}}}}},{"required",new[]{"value"}}};return Task.FromResult(new[]{new McpToolDefinition{Name="echo",Description="echo",InputSchema=schema}});}
            public Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel){return Task.FromResult(prefix+"mcp:"+args["value"]);}
        }
    }
}
