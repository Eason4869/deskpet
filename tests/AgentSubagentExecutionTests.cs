using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentSubagentExecutionTests
    {
        static int count,models;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        sealed class Model:IAgentConversationApi,IDisposable
        {
            readonly int id=Interlocked.Increment(ref models);
            public async Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> messages,AgentWorkspace w,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel){await Task.Delay(60,cancel);Check(messages.Count==2,"worker gets independent minimal history");string path=messages.Last().Content;Check(!w.AvailableToolNames.Contains("exec_command")&&!w.AvailableToolNames.Contains("spawn_subagent")&&!w.AvailableToolNames.Contains("write_file"),"worker tool set remains read-only with no recursive delegation");if(tool!=null)tool(new AgentToolEvent{Id="read-"+id,Name="read_file",Status="running"});string text=await w.ExecuteAsync("read_file",new JavaScriptSerializer().Serialize(new{path}),"read-"+id,cancel);if(tool!=null)tool(new AgentToolEvent{Id="read-"+id,Name="read_file",Status="complete",Summary="Read "+path});return "model-"+id+" "+text;}public void Dispose(){}
        }
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent subagent execution tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"children-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"a.txt"),"first evidence");File.WriteAllText(Path.Combine(root,"b.txt"),"second evidence");using(var workspace=new AgentWorkspace(root)){
            workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,null,new ExecutionClaimStore(root),"parent");workspace.BeginTask(new AgentTaskState{Id="parent",Workspace=root,Goal="explore"});workspace.ConfigureWorkers(()=>new Model(),new ConnectionSettings{Model="fixture"});workspace.ConfigureDesktopSession();
            var json=new JavaScriptSerializer();string a=await workspace.ExecuteAsync("spawn_subagent","{\"profile\":\"explore\",\"input\":\"a.txt\"}","spawn-a",CancellationToken.None),b=await workspace.ExecuteAsync("spawn_subagent","{\"profile\":\"explore\",\"input\":\"b.txt\"}","spawn-b",CancellationToken.None);var ids=new[]{(string)((Dictionary<string,object>)json.DeserializeObject(a))["Id"],(string)((Dictionary<string,object>)json.DeserializeObject(b))["Id"]};string result=await workspace.ExecuteAsync("wait_subagents",json.Serialize(new{ids,timeout_seconds=5}),"wait",CancellationToken.None);
            Check(models==2&&result.Contains("first evidence")&&result.Contains("second evidence")&&result.Contains("model-1")&&result.Contains("model-2"),"two independent worker model calls return different real file evidence");Check(workspace.TaskState.Children.Count==2&&workspace.TaskState.Children.All(c=>c.Status=="completed"&&c.ToolCalls==1),"parent task stores actual worker results and tool counts");Check((await workspace.ExecuteAsync("complete_subagent","{\"id\":\"fake\",\"result\":\"done\"}","forge",CancellationToken.None)).StartsWith("错误："),"parent cannot forge child completion");
            var finished=workspace.TaskState;
            Check(finished.Children.All(c=>c.StartedUtc.HasValue&&c.FinishedUtc>=c.StartedUtc&&c.Model=="fixture"&&c.ProfileId=="explore"),"runtime records actual start finish model and profile");
            Check(finished.Children.All(c=>c.Process.Length==1&&c.Process[0].Tool=="read_file"&&c.Process[0].Status=="complete"&&c.Process[0].Summary=="Read "+c.Input),"process shows the last real public event per tool invocation");
            Check(!result.Contains("StartedUtc")&&!result.Contains("Process"),"parent tool result retains the existing wire contract");
            await CheckHistory(root,finished);
        }
            await CheckStopTimeout(root);
        }

        static async Task CheckStopTimeout(string root)
        {
            var model=new BlockingWorker();
            using(var workspace=new AgentWorkspace(root))
            {
                workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,null,new ExecutionClaimStore(root),"cancel-parent");
                workspace.BeginTask(new AgentTaskState{Id="cancel-parent",Workspace=root});
                workspace.ConfigureWorkers(()=>model,new ConnectionSettings{Model="fixture"});
                workspace.ConfigureDesktopSession();
                try
                {
                    await workspace.ExecuteAsync("spawn_subagent","{\"profile\":\"explore\",\"input\":\"read\"}","cancel-child",CancellationToken.None);
                    if(await Task.WhenAny(model.Started.Task,Task.Delay(5000))!=model.Started.Task)throw new Exception("worker did not start");
                    bool timedOut=false;
                    try { await workspace.StopChildWorkersAsync(); }
                    catch(ApiException error) { timedOut=error.Message==TextResources.Get("agentSubagentRunner.stopAsync.text"); }
                    var child=workspace.TaskState.Children.Single();
                    Check(timedOut&&child.Status=="cancelled"&&child.FinishedUtc==null,"a stop timeout publishes cancellation without claiming the worker finished");
                    Check(child.Process.Single().Status=="interrupted","worker cancellation clears an unfinished process step");
                }
                finally
                {
                    model.Release.TrySetResult("released");
                    workspace.StopChildWorkersAsync().GetAwaiter().GetResult();
                }
            }
        }

        sealed class BlockingWorker:IAgentConversationApi
        {
            internal readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<string> Release=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<string> ChatAgentAsync(ConnectionSettings settings,IList<ChatMessage> messages,AgentWorkspace workspace,
                Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel)
            {
                tool(new AgentToolEvent{Id="blocked-read",Name="read_file",Status="running"});
                Started.TrySetResult(true);
                return Release.Task;
            }
        }

        static async Task CheckHistory(string root,AgentTaskState finished)
        {
            var store=new HistoryStore(Path.Combine(root,"history"));
            using(var session=new ConversationSession(new ApiClient(),new Speech(),()=>null,store))
            {
                session.SelectPersona("one");
                session.History.Commit("first question","first reply");
                session.History.LinkAgentTask(session.History.Messages.Last().Id,finished.Id);
                session.History.UpdateAgentTaskSummary(finished);
                Check(session.Subagents.Count==2,"completed workers stay visible after their parent finishes");
                var delayed=finished.Copy();
                delayed.Children[0].Status="running";
                delayed.Children[0].FinishedUtc=null;
                session.History.UpdateAgentTaskSummary(delayed);
                Check(session.Subagents.All(c=>c.Status=="completed"&&c.FinishedUtc.HasValue),"a delayed progress snapshot cannot replace completed work");
                var next=new AgentTaskState{Id="later-task",Workspace=root};
                next.Children.Add(new AgentChildResult{Id="later-child",Input="next",Status="running"});
                session.History.Commit("next question","next reply");
                session.History.LinkAgentTask(session.History.Messages.Last().Id,next.Id);
                session.History.UpdateAgentTaskSummary(next);
                Check(session.Subagents.Count==3&&session.Subagents[0].Id=="later-child","a later task shows live workers without hiding earlier results");
                Check(session.History.ForRequest("system","next").All(m=>m.AgentChildren==null),"ordinary model history excludes worker display metadata");
                await store.SaveAsync("one",session.History.Messages);
                session.SelectPersona("two");
                Check(session.Subagents.Count==0,"another conversation owner cannot see the previous worker history");
            }
            using(var restarted=new ConversationSession(new ApiClient(),new Speech(),()=>null,store))
            {
                restarted.SelectPersona("one");
                var restored=restarted.Subagents;
                Check(restored.Count==3&&restored[0].Status=="interrupted"&&restored[0].FinishedUtc==null&&restored.Skip(1).All(c=>c.Status=="completed"&&c.Process.Length==1),"history reload retains completed details and normalizes interrupted runs without a false time");
                restored[1].Process[0].Summary="mutated copy";
                Check(restarted.Subagents[1].Process[0].Summary!="mutated copy","worker rows and typed process summaries are independent snapshots");
                restarted.Clear();
                Check(restarted.Subagents.Count==0,"clearing conversation history clears its worker rows");
            }
            await store.FlushAsync();
        }

        sealed class Speech:IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}
            public void SetVolume(int volume){}
            public void Stop(){}
            public void Dispose(){}
        }
    }
}
