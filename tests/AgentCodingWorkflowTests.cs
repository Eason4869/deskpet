using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentCodingWorkflowTests
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        sealed class Approval:IAgentApprovalProvider{public Task<bool> ApproveAsync(AgentApprovalRequest request){return Task.FromResult(true);}}
        sealed class Fixture:IDisposable
        {
            readonly HttpListener listener=new HttpListener();internal string Url;internal readonly List<string> Requests=new List<string>();internal readonly Task Done;
            internal Fixture(string[] replies){var port=new TcpListener(IPAddress.Loopback,0);port.Start();int number=((IPEndPoint)port.LocalEndpoint).Port;port.Stop();Url="http://127.0.0.1:"+number+"/";listener.Prefixes.Add(Url);listener.Start();Done=Task.Run(async delegate{foreach(string reply in replies){var context=await listener.GetContextAsync();using(var reader=new StreamReader(context.Request.InputStream))Requests.Add(await reader.ReadToEndAsync());byte[] bytes=Encoding.UTF8.GetBytes(reply);context.Response.ContentType="application/json";context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes,0,bytes.Length);context.Response.Close();}});}
            public void Dispose(){listener.Close();}
        }
        static string Tool(string id,string name,object args){var json=new JavaScriptSerializer();return json.Serialize(new{choices=new[]{new{message=new{role="assistant",content="",tool_calls=new[]{new{id,type="function",function=new{name,arguments=json.Serialize(args)}}}},finish_reason="tool_calls"}}});}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent coding workflow tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"coding-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"test.ps1"),"Write-Output 'real fixture test'; exit 7",new UTF8Encoding(false));File.WriteAllText(Path.Combine(root,"user.txt"),"user modification must survive");string final=new JavaScriptSerializer().Serialize(new{choices=new[]{new{message=new{content="修正并验证完成"},finish_reason="stop"}}});
            using(var fixture=new Fixture(new[]{Tool("read","read_file",new{path="test.ps1"}),Tool("fail","exec_command",new{command="& './test.ps1'",working_directory=root,timeout_seconds=5}),Tool("fix","edit_file",new{path="test.ps1",old_text="exit 7",new_text="exit 0"}),Tool("pass","exec_command",new{command="& './test.ps1'",working_directory=root,timeout_seconds=5}),final}))using(var api=new ApiClient())using(var workspace=new AgentWorkspace(root)){
                workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,new Approval(),null,"coding-task");workspace.ConfigureTerminal(AgentTerminalPolicy.ConfirmEach);workspace.ConfigureDesktopSession();workspace.BeginTask(new AgentTaskState{Id="coding-task",Workspace=root,Goal="fix the fixture test"});var result=await new AgentTaskRunner(api).RunAsync(new AgentRunRequest{Workspace=workspace,Model=new ConnectionSettings{BaseUrl=fixture.Url,Model="fixture",Protocol="openai",StreamReplies=false,ApiKey="fixture"},Messages=new[]{new ChatMessage("user","定位并修正失败的测试，使用实际退出码核对。")},Stream=false},CancellationToken.None);await fixture.Done;
                Check(result.Status=="completed"&&workspace.TaskState.Status=="complete","non-UI native runner completes a real coding tool loop");Check(workspace.CommandResults.Count==2&&workspace.CommandResults[0].ExitCode==7&&workspace.CommandResults[1].ExitCode==0,"fixture test genuinely fails before the edit and passes afterward: "+String.Join(",",workspace.CommandResults.Select(c=>c.ExitCode.ToString())));
                Check(fixture.Requests[2].Contains("ExitCode")&&fixture.Requests[4].Contains("ExitCode"),"model observes actual terminal results before continuing");Check(File.ReadAllText(Path.Combine(root,"test.ps1")).Contains("exit 0")&&workspace.Changes.Count==1,"real file edit and change evidence retained");Check(File.ReadAllText(Path.Combine(root,"user.txt"))=="user modification must survive","unrelated existing user change preserved");
            }
        }
    }
}
