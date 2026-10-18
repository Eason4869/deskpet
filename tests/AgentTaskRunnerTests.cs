using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentTaskRunnerTests
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        sealed class Api:IAgentConversationApi
        {
            internal TaskCompletionSource<string> Release=new TaskCompletionSource<string>();internal ConnectionSettings Seen;internal IList<ChatMessage> Messages;internal Action<string> Delta;
            public async Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> m,AgentWorkspace w,Action<string> d,Action<string> r,Action<AgentToolEvent> t,bool stream,CancellationToken c){Seen=s;Messages=m;Delta=d;using(c.Register(()=>Release.TrySetCanceled()))return await Release.Task;}
        }
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent task runner tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);using(var w=new AgentWorkspace(root)){
            w.BeginTask(new AgentTaskState{Workspace=root,Goal="fixture"});var api=new Api();int deltas=0;var request=new AgentRunRequest{Model=new ConnectionSettings{Model="original"},Messages=new List<ChatMessage>{new ChatMessage("user","original input")},Workspace=w,Text=d=>deltas++};var running=new AgentTaskRunner(api).RunAsync(request,CancellationToken.None);request.Model.Model="changed";request.Messages.Clear();Check(api.Seen.Model=="original"&&api.Messages.Count==1&&api.Messages[0].Content=="original input","runner freezes model and history before awaiting");api.Release.SetResult("done");var result=await running;Check(result.Status=="completed"&&w.TaskState.Status=="complete","runner owns actual terminal state without a window");api.Delta("late");Check(deltas==0,"late callbacks after completed run are ignored");
            api=new Api();using(var cancel=new CancellationTokenSource()){var stopped=new AgentTaskRunner(api).RunAsync(new AgentRunRequest{Model=new ConnectionSettings(),Messages=new[]{new ChatMessage("user","cancel")},Workspace=w,Text=d=>deltas++},cancel.Token);cancel.Cancel();result=await stopped;Check(result.Status=="cancelled"&&w.TaskState.Status=="cancelled","runner cancellation reaches model and task state");api.Delta("late cancelled");Check(deltas==0,"cancelled event sink cannot update a later run");}
        }}
    }
}
