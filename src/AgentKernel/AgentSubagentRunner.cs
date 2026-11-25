using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    [DataContract]
    public sealed class AgentChildToolSummary
    {
        [DataMember] public string Tool,Status,Summary;
        internal string EventId;
        internal AgentChildToolSummary Copy(){return (AgentChildToolSummary)MemberwiseClone();}
    }

    [DataContract] public sealed class AgentChildResult
    {
        [DataMember]public string Id,Status,Input,Result,Source="independent-worker";
        [DataMember]public int ToolCalls,EstimatedTokens,ModelRequests;[DataMember]public long EstimatedTotalTokens;
        [DataMember(EmitDefaultValue=false)] public DateTime? CreatedUtc,StartedUtc,FinishedUtc;
        [DataMember(EmitDefaultValue=false)] public string Model,ProfileId,DisplayName;
        [DataMember(EmitDefaultValue=false)] public AgentChildToolSummary[] Process;
        internal bool IsInProgress {get{return Status=="running"||Status=="queued";}}

        public AgentChildResult Copy()
        {
            var copy=(AgentChildResult)MemberwiseClone();
            copy.Process=Process==null?null:Process.Where(step=>step!=null).Select(step=>step.Copy()).ToArray();
            return copy;
        }

        internal AgentChildResult ArchiveCopy()
        {
            var copy=Copy();
            copy.Input=Limit(Input,800);
            copy.Result=Limit(Result,2200);
            if(copy.Process!=null)copy.Process=copy.Process.Skip(Math.Max(0,copy.Process.Length-6)).ToArray();
            return copy;
        }

        internal void RecoverInterrupted()
        {
            if(IsInProgress)
            {
                Status="interrupted";
                Result=TextResources.Get("agentTaskStore.recovered.childResult");
            }
            InterruptUnfinishedProcess();
        }

        internal void InterruptUnfinishedProcess()
        {
            if(Status!="cancelled"&&Status!="failed"&&Status!="interrupted")return;
            if(Process!=null)foreach(var step in Process)
                if(step!=null&&step.Status=="running")step.Status="interrupted";
        }

        internal static string Limit(string value,int maximum)
        {
            if(value==null||value.Length<=maximum)return value;
            int length=maximum-1;
            if(Char.IsHighSurrogate(value[length-1]))length--;
            return value.Substring(0,length)+"…";
        }
    }
    public interface IAgentSubagentRunner {Task<AgentChildResult[]> WaitAsync(string[] ids,int seconds,CancellationToken cancel);}
    public sealed class AgentSubagentRunner:IAgentSubagentRunner,IDisposable
    {
        readonly object gate=new object();readonly AgentSubagentScheduler scheduler;readonly Func<IAgentConversationApi> factory;readonly AgentWorkspace parent;readonly ConnectionSettings model;
        readonly AgentPermissionRuntime permissions;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();readonly Dictionary<string,AgentChildResult> results=new Dictionary<string,AgentChildResult>();readonly Dictionary<string,TaskCompletionSource<bool>> done=new Dictionary<string,TaskCompletionSource<bool>>();readonly Dictionary<string,CancellationTokenSource> cancellations=new Dictionary<string,CancellationTokenSource>();
        public event Action Changed;bool disposed;
        internal AgentSubagentRunner(AgentWorkspace parent,AgentSubagentScheduler scheduler,Func<IAgentConversationApi> factory,ConnectionSettings model){this.parent=parent;this.scheduler=scheduler;this.factory=factory;this.model=model.Copy();permissions=parent.FreezePermissions();scheduler.Started+=Start;}
        public AgentChildResult[] List(){lock(gate)return results.Values.Select(r=>r.Copy()).ToArray();}
        void Ensure(AgentSubagentState child)
        {
            lock(gate)if(!results.ContainsKey(child.Id))
            {
                results.Add(child.Id,new AgentChildResult{Id=child.Id,Input=child.Input,Status=child.Status,CreatedUtc=child.CreatedUtc,
                    ProfileId=child.ProfileId,Model=AgentChildResult.Limit(model.Model,250)});
                done.Add(child.Id,new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            }
        }
        public AgentChildResult Spawn(string parentId,string input)
        {
            if(disposed)throw new ObjectDisposedException("AgentSubagentRunner");if(String.IsNullOrWhiteSpace(input)||input.Length>12000)throw new ArgumentException(TextResources.Get("agentSubagentRunner.spawn.text"));lock(gate)if(results.Count>=64)throw new InvalidOperationException(TextResources.Get("agentSubagentRunner.spawn.text.alternativeA"));
            var child=scheduler.Schedule(parentId,new AgentProfileRegistry().Get("explore"),input);Ensure(child);Notify();lock(gate)return results[child.Id].Copy();
        }
        void Start(AgentSubagentState child)
        {
            Ensure(child);CancellationTokenSource cancel;
            lock(gate)
            {
                var state=results[child.Id];
                if(disposed||lifetime.IsCancellationRequested||state.Status=="cancelled")
                {
                    state.Status="cancelled";
                    if(!state.FinishedUtc.HasValue)state.FinishedUtc=DateTime.UtcNow;
                    done[child.Id].TrySetResult(true);
                    scheduler.Interrupt(child.Id);
                    return;
                }
                cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                cancel.CancelAfter(TimeSpan.FromMinutes(2));
                cancellations.Add(child.Id,cancel);
                state.Status="running";
                state.StartedUtc=DateTime.UtcNow;
            }
            Task.Run(()=>RunAsync(child,cancel));
        }
        async Task RunAsync(AgentSubagentState child,CancellationTokenSource cancel)
        {
            using(UsageTracking.BeginScope("subagent"))
            {
            Notify();IAgentConversationApi api=null;
            try{
                cancel.Token.ThrowIfCancellationRequested();api=factory();if(api==null)throw new InvalidOperationException(TextResources.Get("agentSubagentRunner.runAsync.text"));
                using(var workspace=new AgentWorkspace(parent.Root,parent.RestrictToRoot){ReadOnly=true,ContextTokenLimit=parent.ContextTokenLimit,ChildToolBudget=32,CumulativeTokenLimit=Math.Min(262144,Math.Max(4096,parent.ContextTokenLimit)*4)}){
                    workspace.ConfigureKernel(permissions.Copy(),null,null,null,child.Id);workspace.InheritInstructions(parent);
                    workspace.ConfigureCapabilities(new AgentCapabilitySnapshot(CapabilitySupport.Supported,model.ImageSupport,true,false,parent.ContextTokenLimit,"inherited","parent-subset"));workspace.ConfigureDesktopSession();
                    workspace.TaskChanged=snapshot=>{lock(gate){var state=results[child.Id];state.EstimatedTokens=snapshot.EstimatedContextTokens;state.EstimatedTotalTokens=snapshot.EstimatedTotalTokens;state.ModelRequests=snapshot.ModelRequests;}Notify();};
                    workspace.BeginTask(new AgentTaskState{Id=child.Id,Workspace=parent.Root,Goal=child.Input,PersonaId="explore"});
                    var result=await new AgentTaskRunner(api).RunAsync(new AgentRunRequest{Model=model,Workspace=workspace,Messages=new[]{new ChatMessage("system",ReplyLanguagePolicy.Apply("你是独立只读探索 worker。仅探索和验证资料，禁止写入、终端、扩权和递归委派。结果列出实际读取来源及未验证部分。")),new ChatMessage("user",child.Input)},Tool=tool=>RecordProcess(child.Id,tool),Stream=false},cancel.Token).ConfigureAwait(false);
                    cancel.Token.ThrowIfCancellationRequested();lock(gate){var state=results[child.Id];if(state.Status=="cancelled")throw new OperationCanceledException();state.Status=result.Status;state.Result=(result.Status=="completed"?result.Text:result.Error)??TextResources.Get("agentSubagentRunner.runAsync.stateResult");if(state.Result.Length>12000)state.Result=TextResources.Format("agentSubagentRunner.runAsync.stateResultSubstring",state.Result.Substring(0,12000));state.ToolCalls=result.ToolCalls;state.EstimatedTokens=workspace.TaskState.EstimatedContextTokens;state.EstimatedTotalTokens=workspace.TaskState.EstimatedTotalTokens;state.ModelRequests=workspace.TaskState.ModelRequests;}
                    if(result.Status=="completed")scheduler.Complete(child.Id,result.Text);else if(result.Status=="cancelled")scheduler.Interrupt(child.Id);else scheduler.Fail(child.Id,result.Error);
                }
            }catch(OperationCanceledException){lock(gate){results[child.Id].Status="cancelled";results[child.Id].Result=TextResources.Get("agentSubagentRunner.runAsync.childIdResult");}scheduler.Interrupt(child.Id);}
            catch(Exception){bool cancelled=cancel.IsCancellationRequested;lock(gate){results[child.Id].Status=cancelled?"cancelled":"failed";results[child.Id].Result=cancelled?TextResources.Get("agentSubagentRunner.runAsync.idResultCancelled"):TextResources.Get("agentSubagentRunner.runAsync.idResultCancelled.alternativeA");}if(cancelled)scheduler.Interrupt(child.Id);else scheduler.Fail(child.Id,"worker failed");}
            finally
            {
                var disposable=api as IDisposable;
                if(disposable!=null)disposable.Dispose();
                lock(gate)
                {
                    var state=results[child.Id];
                    state.InterruptUnfinishedProcess();
                    state.FinishedUtc=DateTime.UtcNow;
                    cancellations.Remove(child.Id);
                    done[child.Id].TrySetResult(true);
                }
                cancel.Dispose();
                Notify();
            }

            }
        }

        void RecordProcess(string childId,AgentToolEvent tool)
        {
            if(tool==null||String.IsNullOrEmpty(tool.Name))return;
            lock(gate)
            {
                var child=results[childId];
                if(child.Status!="running")return;
                var steps=new List<AgentChildToolSummary>(child.Process??new AgentChildToolSummary[0]);
                var summary=new AgentChildToolSummary{EventId=tool.Id,Tool=AgentChildResult.Limit(tool.Name,80),
                    Status=AgentChildResult.Limit(tool.Status,24),Summary=AgentChildResult.Limit(tool.Summary,180)};
                int matching=String.IsNullOrEmpty(tool.Id)?-1:steps.FindIndex(step=>step.EventId==tool.Id);
                if(matching>=0)steps[matching]=summary;
                else
                {
                    if(steps.Count==12)steps.RemoveAt(0);
                    steps.Add(summary);
                }
                child.Process=steps.ToArray();
            }
            Notify();
        }
        public async Task<AgentChildResult[]> WaitAsync(string[] ids,int seconds,CancellationToken cancel)
        {
            if(ids==null||ids.Length<1||ids.Length>64||seconds<0||seconds>30)throw new ArgumentException(TextResources.Get("agentSubagentRunner.waitAsync.text"));Task[] tasks;lock(gate){if(ids.Any(id=>!done.ContainsKey(id)))throw new UnauthorizedAccessException(TextResources.Get("agentSubagentRunner.waitAsync.text.alternativeA"));tasks=ids.Select(id=>(Task)done[id].Task).ToArray();}
            await Task.WhenAny(Task.WhenAll(tasks),Task.Delay(seconds*1000,cancel)).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();lock(gate)return ids.Select(id=>results[id].Copy()).ToArray();
        }
        public bool Interrupt(string id)
        {
            CancellationTokenSource cancel;bool queued;
            lock(gate)
            {
                AgentChildResult child;
                if(!results.TryGetValue(id,out child)||!child.IsInProgress)return false;
                queued=!cancellations.TryGetValue(id,out cancel);
                child.Status="cancelled";
                child.Result=TextResources.Get("agentSubagentRunner.interrupt.childResult");
                child.InterruptUnfinishedProcess();
                if(queued)
                {
                    child.FinishedUtc=DateTime.UtcNow;
                    done[id].TrySetResult(true);
                }
            }
            if(cancel!=null)try{cancel.Cancel();}catch(ObjectDisposedException){}
            if(queued)scheduler.Interrupt(id);return true;
        }
        internal async Task StopAsync()
        {
            lifetime.Cancel();foreach(var child in List())Interrupt(child.Id);Task[] tasks;lock(gate)tasks=done.Values.Select(v=>(Task)v.Task).ToArray();
            if(tasks.Length>0){var all=Task.WhenAll(tasks);if(await Task.WhenAny(all,Task.Delay(3000)).ConfigureAwait(false)!=all)throw new ApiException(TextResources.Get("agentSubagentRunner.stopAsync.text"));await all.ConfigureAwait(false);}
        }
        void Notify(){var changed=Changed;if(!disposed&&changed!=null)changed();}
        public void Dispose(){if(disposed)return;disposed=true;scheduler.Started-=Start;lifetime.Cancel();foreach(var child in List())Interrupt(child.Id);}
    }
}
