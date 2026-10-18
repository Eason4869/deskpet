using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentApprovalTests
    {
        static int count;static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
        sealed class Approval:IAgentApprovalProvider
        {
            public AgentApprovalRequest Request;public TaskCompletionSource<bool> Decision=new TaskCompletionSource<bool>();public int Calls;
            public Task<bool> ApproveAsync(AgentApprovalRequest request){Calls++;Request=request;return Decision.Task;}
        }
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent approval tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run()
        {
            var policy=new AgentPermissionRuntime();policy.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Ask});var approval=new Approval();var runtime=new AgentToolRuntime(policy,null,approval);int effects=0;
            runtime.Register(new AgentToolDefinition{Name="write_file",ReadOnly=false},(a,c,t)=>{effects++;return Task.FromResult("done");});
            var context=new AgentToolExecutionContext{SessionId="session",TaskId="task",CallId="call"};var args=new Dictionary<string,object>{{"path","fixture.txt"},{"content","one"}};
            var first=runtime.ExecuteAsync("write_file",args,context,CancellationToken.None);Check(!first.IsCompleted&&effects==0,"pending approval performs no effect");
            Check(approval.Request.Resource=="fixture.txt" && !String.IsNullOrWhiteSpace(approval.Request.PayloadHash),"approval binds resource and exact payload hash");
            var duplicate=runtime.ExecuteAsync("write_file",args,context,CancellationToken.None);approval.Decision.SetResult(true);await Task.WhenAll(first,duplicate);Check(effects==1&&approval.Calls==1,"duplicate call executes once");
            bool altered=false;try{await runtime.ExecuteAsync("write_file",new Dictionary<string,object>{{"path","fixture.txt"},{"content","two"}},context,CancellationToken.None);}catch(InvalidOperationException){altered=true;}Check(altered&&effects==1,"changed payload cannot reuse call approval");
            approval=new Approval();runtime=new AgentToolRuntime(policy,null,approval);runtime.Register(new AgentToolDefinition{Name="write_file"},(a,c,t)=>{effects++;return Task.FromResult("bad");});
            using(var cancel=new CancellationTokenSource()){var stopped=runtime.ExecuteAsync("write_file",args,new AgentToolExecutionContext{CallId="stop",TaskId="task"},cancel.Token);cancel.Cancel();Check(await Task.WhenAny(stopped,Task.Delay(1000))==stopped,"stop cancels approval wait promptly");try{await stopped;}catch(OperationCanceledException){}approval.Decision.TrySetResult(true);Check(effects==1,"late approval after stop performs no effect");}
            var deny=new Approval();deny.Decision.SetResult(false);runtime=new AgentToolRuntime(policy,null,deny);runtime.Register(new AgentToolDefinition{Name="write_file"},(a,c,t)=>{effects++;return Task.FromResult("bad");});bool rejected=false;try{await runtime.ExecuteAsync("write_file",args,new AgentToolExecutionContext{CallId="deny"},CancellationToken.None);}catch(UnauthorizedAccessException){rejected=true;}Check(rejected&&effects==1,"rejection performs no effect");
        }
    }
}
