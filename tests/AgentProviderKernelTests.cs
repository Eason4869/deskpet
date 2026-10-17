using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentProviderKernelTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                var providers=new AgentProviderRegistry();
                var caps=providers.Resolve("mimo","mimo-v2.5-flash");
                Check(caps.SupportsTools && caps.SupportsStreaming && caps.ContextWindow>0,"provider capabilities describe tool and streaming support");
                Check(!providers.Resolve("unknown","unknown-model").SupportsTools,"unknown providers fail closed for tool capability");
                string json=providers.Describe("mimo","mimo-v2.5-flash");
                Check(json.Contains("mimo-v2.5-flash") && json.Contains("SupportsTools"),"provider capability description is machine readable");
                var subagents=new AgentSubagentManager(new ExecutionClaimStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"subagents-"+Guid.NewGuid().ToString("N"))));
                var child=subagents.Spawn("parent-1",new AgentProfile{Id="explore",Mode="subagent",ReadOnly=true},"inspect");
                Check(child.Status=="running" && subagents.List("parent-1").Count==1,"subagent spawn creates a running child session");
                Check(subagents.Interrupt(child.Id),"subagent lifecycle supports interruption");
                Check(subagents.Complete(child.Id,"done") && child.Status=="complete","subagent lifecycle completes with result");
                var mcp=new McpToolAdapter(new FakeMcpTransport());
                var permission=new AgentPermissionRuntime();permission.Add(new AgentPermissionRule{Action="mcp",Resource="**",Effect=AgentPermissionEffect.Allow});var runtime=new AgentToolRuntime(permission,new AgentAuditLog(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mcp-audit-"+Guid.NewGuid().ToString("N"))));
                mcp.Register(runtime);
                string result=runtime.ExecuteAsync("mcp_echo",new Dictionary<string,object>{{"value","hi"}},new AgentToolExecutionContext{SessionId="s",AgentId="a",MessageId="m",CallId="c"},CancellationToken.None).GetAwaiter().GetResult();
                Check(result.Contains("hi"),"MCP adapter executes registered remote tools");
                Console.WriteLine("Agent provider/subagent/MCP tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        sealed class FakeMcpTransport:IMcpTransport
        {
            public Task<McpToolDefinition[]> ListToolsAsync(CancellationToken cancel){return Task.FromResult(new[]{new McpToolDefinition{Name="echo",Description="echo",InputSchema=new Dictionary<string, object>()}});}
            public Task<string> CallAsync(string name,Dictionary<string,object> args,CancellationToken cancel){return Task.FromResult("mcp:"+args["value"]);}
        }
    }
}
