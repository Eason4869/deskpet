using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentIntegrationFinalTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class Handler:HttpMessageHandler
        {
protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                string body=request.Content==null?"":request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string json=body.Contains("tools/list")?"{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[{\"name\":\"echo\",\"description\":\"echo\",\"inputSchema\":{}}]}}":"{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"mcp-http\"}]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        public static int Main()
        {
            try
            {
                var mcp=new McpHttpTransport(new Uri("https://mcp.example.test"),new Handler(),"secret");
                var tools=mcp.ListToolsAsync(CancellationToken.None).GetAwaiter().GetResult();
                Check(tools.Length==1 && tools[0].Name=="echo","MCP HTTP transport lists remote tools");
                string output=mcp.CallAsync("echo",new Dictionary<string,object>{{"value","x"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(output=="mcp-http","MCP HTTP transport calls remote tools");
                var caps=new AgentProviderRegistry().Resolve("unknown","unknown-model");
                Check(!caps.SupportsTools && !caps.CanUseTool("write_file"),"provider capability gate blocks unsupported tools");
                var claims=new ExecutionClaimStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"scheduler-"+Guid.NewGuid().ToString("N")));
                var scheduler=new AgentSubagentScheduler(new AgentSubagentManager(claims),1);
                var profile=new AgentProfile{Id="explore",Mode="subagent",ReadOnly=true};
                var a=scheduler.Schedule("parent",profile,"a");var b=scheduler.Schedule("parent",profile,"b");
                Check(a.Status=="running" && b.Status=="queued","subagent scheduler enforces concurrency limit");
                Check(scheduler.Complete(a.Id,"done") && scheduler.List("parent")[1].Status=="running","subagent scheduler promotes queued child after completion");
                Console.WriteLine("Agent integration final tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
