using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    // This service belongs to the application, never to a disposable chat workspace.
    public sealed class AgentJobManager
    {
        sealed class Entry
        {
            internal AgentJobRecord Record;
            internal volatile AgentJobSnapshot Published;
            internal IAgentJobProcess Process;
            internal volatile AgentJobLogStore Log;
            internal IDisposable DirectoryLease;
            internal bool FinalPersistencePending;
            internal readonly SemaphoreSlim Mutation=new SemaphoreSlim(1,1);
            internal readonly CancellationTokenSource Lifetime=new CancellationTokenSource();
            internal readonly CancellationTokenSource PendingStart=new CancellationTokenSource(),RefreshCancellation=new CancellationTokenSource();
            internal bool HandedOff;
            internal AgentJobStopReason? RequestedStop;
            internal long StartedMilliseconds;
            internal Task<AgentJobSnapshot> Start;
        }
        public Dafeiyu.Maintenance.DataMaintenanceGate Maintenance {get;private set;}
        readonly IDisposable maintenanceRegistration;
        readonly object gate=new object();
        readonly AgentJobStore store;
        readonly IAgentJobProcessFactory factory;
        readonly IAgentJobClock clock;
        readonly Dictionary<string,Entry> jobs=new Dictionary<string,Entry>(StringComparer.Ordinal),invocations=new Dictionary<string,Entry>(StringComparer.Ordinal);
        readonly Dictionary<string,SemaphoreSlim> ownerGates=new Dictionary<string,SemaphoreSlim>(StringComparer.Ordinal);
        readonly CancellationTokenSource exiting=new CancellationTokenSource();
        bool recovered,recovering,exitRequested;
        public event Action Changed;
        public AgentJobManager(AgentJobStore store,IAgentJobProcessFactory factory,IAgentJobClock clock):this(store,factory,clock,null){}
        public AgentJobManager(AgentJobStore store,IAgentJobProcessFactory factory,IAgentJobClock clock,Dafeiyu.Maintenance.DataMaintenanceGate maintenance)
        {if(store==null||factory==null||clock==null)throw new ArgumentNullException("services");this.store=store;this.factory=factory;this.clock=clock;Maintenance=maintenance??new Dafeiyu.Maintenance.DataMaintenanceGate();maintenanceRegistration=Maintenance.Register(store.DrainMaintenanceAsync,()=>HasActiveJobs,this);store.Retired+=Retire;}
        void Retire(string id)
        {
            lock(gate)
            {
                Entry entry;if(!jobs.TryGetValue(id,out entry)||!AgentJobStore.IsTerminal(entry.Record.State))return;
                jobs.Remove(id);Entry invocation;if(invocations.TryGetValue(entry.Record.InvocationKey,out invocation)&&Object.ReferenceEquals(invocation,entry))invocations.Remove(entry.Record.InvocationKey);
            }
        }
        public async Task RecoverAsync(CancellationToken token)
        {using(Maintenance.EnterActivity()){
            lock(gate){if(recovered)return;if(recovering)throw new InvalidOperationException("jobs_recovering");recovering=true;}
            try
            {
                var restored=await store.LoadAsync(token).ConfigureAwait(false);
                lock(gate){foreach(var snapshot in restored){var entry=new Entry{Record=snapshot.ToRecord(),Published=snapshot};jobs.Add(snapshot.Id,entry);invocations.Add(entry.Record.InvocationKey,entry);}recovered=true;}
                Notify();
            }
            finally{lock(gate)recovering=false;}
        }}
        static string Payload(AgentJobSpec spec)
        {
            var json=new JavaScriptSerializer().Serialize(new object[]{spec.Command,spec.Title,spec.WorkingDirectory,spec.TimeoutSeconds});
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();
        }
        static void ValidatePolicy(AgentJobPolicySnapshot policy,AgentJobSpec spec)
        {
            if(policy==null||!policy.TerminalAllowed||policy.ReadOnly)throw new InvalidOperationException("job_permission_denied");
            AgentJobPolicy.ResolveTimeout(spec.TimeoutSeconds,policy.TimeoutCeiling);
            if(String.IsNullOrWhiteSpace(policy.Root)||!Path.IsPathRooted(policy.Root))throw new InvalidOperationException("job_directory_changed");
            var root=Path.GetFullPath(policy.Root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            var path=spec.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            var comparison=Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            if(!String.Equals(path,root,comparison)&&!path.StartsWith(root+Path.DirectorySeparatorChar,comparison))throw new InvalidOperationException("job_directory_denied");
            if(!Directory.Exists(spec.WorkingDirectory))throw new InvalidOperationException("job_directory_changed");
            AgentInstructions.RejectLinks(spec.WorkingDirectory);
        }
        static bool SamePolicy(AgentJobPolicySnapshot a,AgentJobPolicySnapshot b)
        {return a.Root==b.Root&&a.DirectoryIdentity==b.DirectoryIdentity&&a.PermissionVersion==b.PermissionVersion&&a.TimeoutCeiling==b.TimeoutCeiling&&a.TerminalAllowed==b.TerminalAllowed&&a.ReadOnly==b.ReadOnly&&a.Sandbox==b.Sandbox&&a.ApprovalMode==b.ApprovalMode&&a.ReadDirectories.SequenceEqual(b.ReadDirectories);}
        static void RecheckPolicy(AgentJobPolicySnapshot original,AgentJobPolicySnapshot current,AgentJobSpec spec)
        {
            ValidatePolicy(current,spec);
            if(original.Root!=current.Root||original.DirectoryIdentity!=current.DirectoryIdentity)throw new InvalidOperationException("job_directory_changed");
            if(!SamePolicy(original,current))throw new InvalidOperationException("job_policy_changed");
        }
        static void ValidateInvocation(AgentJobStartContext context,AgentJobSpec spec)
        {
            var invocation=context.Invocation;var args=invocation.ReadArguments();object command,title,directory,timeout;
            if(invocation.ToolName!="start_job"||invocation.Action!="shell"||args==null||args.Count!=4||!args.TryGetValue("command",out command)||!args.TryGetValue("title",out title)||!args.TryGetValue("working_directory",out directory)||!args.TryGetValue("timeout_seconds",out timeout)||!(timeout is int)||command as string!=spec.Command||title as string!=spec.Title||directory as string!=spec.WorkingDirectory||(int)timeout!=spec.TimeoutSeconds||invocation.Resource!=spec.WorkingDirectory)
                throw new InvalidOperationException("job_invocation_mismatch");
        }
        public Task<AgentJobSnapshot> StartAsync(AgentJobStartContext context,AgentJobSpec spec,CancellationToken requestToken)
        {bool replayed;return StartForToolAsync(context,spec,requestToken,out replayed);}
        internal Task<AgentJobSnapshot> StartForToolAsync(AgentJobStartContext context,AgentJobSpec spec,CancellationToken requestToken,out bool replayed)
        {
            replayed=false;var admission=Maintenance.EnterActivity();try{
            if(context==null||spec==null)throw new ArgumentNullException("context");
            if(context.Owner.AgentId!="primary")throw new InvalidOperationException("job_owner_denied");
            var key=AgentJobPolicy.IdempotencyKey(context.Owner,context.Invocation);var payload=Payload(spec);Entry entry;
            var result=new TaskCompletionSource<AgentJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock(gate)
            {
                if(!recovered)throw new InvalidOperationException("jobs_recovering");
                if(invocations.TryGetValue(key,out entry))
                {
                    replayed=true;
                    if(entry.Record.PayloadHash!=payload)throw new InvalidOperationException("job_idempotency_conflict");
                    return JoinStartAsync(entry.Start??Task.FromResult(Snapshot(entry)),admission);
                }
                if(exitRequested)throw new InvalidOperationException("jobs_exiting");
                entry=new Entry{Record=new AgentJobRecord{Id=Guid.NewGuid().ToString("N"),InvocationKey=key,PayloadHash=payload,SourceTaskId=context.Invocation.TaskId,SourceCallId=context.Invocation.CallId,SourceReplyId=context.ReplyId,Owner=context.Owner,Spec=spec,State=AgentJobState.Starting,CreatedUtc=clock.UtcNow},Start=result.Task};
                foreach(string phase in new[]{"approval","sandbox_prepare","process_prepare","resume","handoff","stop","eof_drain"})entry.Record.TimingsMs[phase]=null;
                entry.Published=new AgentJobSnapshot(entry.Record);
                invocations.Add(key,entry);
            }
            // StartCore owns all exceptions and never exposes an unobserved async-void task.
            CompleteStartAsync(entry,context,requestToken,result,admission);
            return result.Task;
            }catch{admission.Dispose();throw;}
        }
        static async Task<AgentJobSnapshot> JoinStartAsync(Task<AgentJobSnapshot> pending,IDisposable admission){try{return await pending.ConfigureAwait(false);}finally{admission.Dispose();}}
        internal void RetireMaintenance(){store.DrainMaintenanceAsync().ContinueWith(done=>{var observed=done.Exception;maintenanceRegistration.Dispose();},TaskScheduler.Default);}
        async void CompleteStartAsync(Entry entry,AgentJobStartContext context,CancellationToken requestToken,TaskCompletionSource<AgentJobSnapshot> result,IDisposable admission)
        {
            await entry.Mutation.WaitAsync().ConfigureAwait(false);
            try{result.TrySetResult(await StartCoreAsync(entry,context,requestToken).ConfigureAwait(false));}
            catch(OperationCanceledException){result.TrySetCanceled();}
            catch(Exception error){result.TrySetException(error);}
            finally{entry.Mutation.Release();admission.Dispose();}
        }
        async Task<AgentJobSnapshot> StartCoreAsync(Entry entry,AgentJobStartContext context,CancellationToken requestToken)
        {
            var elapsed=Stopwatch.StartNew();bool reserved=false,saved=false,ownerHeld=false;var ownerGate=OwnerGate(context.Owner.ProjectId);
            try
            {
            using(var cancellation=CancellationTokenSource.CreateLinkedTokenSource(requestToken,exiting.Token,entry.PendingStart.Token))
            {
                var token=cancellation.Token;Exception failure=null;
                try
                {
                    token.ThrowIfCancellationRequested();ValidateInvocation(context,entry.Record.Spec);var original=context.RecheckPolicy();ValidatePolicy(original,entry.Record.Spec);
                    var authorization=Stopwatch.StartNew();var decision=await context.Authorization.AuthorizeAsync(context.Invocation,token).ConfigureAwait(false);
                    entry.Record.TimingsMs["approval"]=authorization.ElapsedMilliseconds;
                    if(decision==null||decision.Effect!=AgentPermissionEffect.Allow)throw new InvalidOperationException("job_permission_denied");
                    await ownerGate.WaitAsync(token).ConfigureAwait(false);ownerHeld=true;
                    token.ThrowIfCancellationRequested();entry.DirectoryLease=context.AcquireDirectory();
                    var current=context.RecheckPolicy();RecheckPolicy(original,current,entry.Record.Spec);
                    lock(gate)
                    {
                        if(exitRequested)throw new InvalidOperationException("jobs_exiting");
                        var active=jobs.Values.Where(job=>!AgentJobStore.IsTerminal(job.Record.State)).ToArray();
                        if(active.Length>=AgentJobPolicy.MaximumActiveJobs)throw new InvalidOperationException("job_application_capacity");
                        if(active.Count(job=>job.Record.Owner.ProjectId==entry.Record.Owner.ProjectId)>=AgentJobPolicy.MaximumProjectJobs)throw new InvalidOperationException("job_project_capacity");
                        jobs.Add(entry.Record.Id,entry);reserved=true;
                    }
                    await PersistAsync(entry,token).ConfigureAwait(false);saved=true;
                    var log=LogFor(entry);
                    log.Faulted+=error=>HandleLogFault(entry);
                    var preparation=Stopwatch.StartNew();entry.Process=await factory.PrepareAsync(entry.Record.Spec,current,chunk=>log.Append(chunk),token).ConfigureAwait(false);
                    long preparationMs=preparation.ElapsedMilliseconds;
                    if(entry.Process==null)throw new InvalidOperationException("job_process_missing");
                    var metrics=entry.Process as IAgentJobPreparationMetrics;
                    entry.Record.TimingsMs["sandbox_prepare"]=metrics==null?0:Math.Max(0,Math.Min(preparationMs,metrics.SandboxPreparationMilliseconds));
                    entry.Record.TimingsMs["process_prepare"]=preparationMs-entry.Record.TimingsMs["sandbox_prepare"].Value;
                    token.ThrowIfCancellationRequested();var final=context.RecheckPolicy();RecheckPolicy(current,final,entry.Record.Spec);
                    if(context.BeforeEffect==null)throw new InvalidOperationException("execution_reconciliation_required");context.BeforeEffect();
                    token.ThrowIfCancellationRequested();entry.Record.MayHaveExecuted=true;var resume=Stopwatch.StartNew();entry.Process.Resume();entry.Record.TimingsMs["resume"]=resume.ElapsedMilliseconds;
                    entry.Record.State=AgentJobState.Running;entry.Record.StartedUtc=clock.UtcNow;entry.StartedMilliseconds=clock.ElapsedMilliseconds;
                    var handoff=Stopwatch.StartNew();
                    await PersistAsync(entry,token).ConfigureAwait(false);
                    lock(gate){token.ThrowIfCancellationRequested();if(exitRequested||entry.RequestedStop.HasValue)throw new OperationCanceledException(token);entry.HandedOff=true;}
                    entry.Record.TimingsMs["handoff"]=handoff.ElapsedMilliseconds;
                    entry.Record.TimingsMs["start_total"]=elapsed.ElapsedMilliseconds;
                    entry.Record.TimingsMs["start_excluding_authorization_and_sandbox"]=Math.Max(0,elapsed.ElapsedMilliseconds-entry.Record.TimingsMs["approval"].Value-entry.Record.TimingsMs["sandbox_prepare"].Value);
                    entry.Published=new AgentJobSnapshot(entry.Record);var started=Snapshot(entry);Notify();MonitorAsync(entry);return started;
                }
                catch(Exception error){failure=error;}
                    if(entry.Process!=null)
                    {
                        bool clean=await CleanupAsync(entry).ConfigureAwait(false);ObserveExit(entry,entry.Process.Status);
                        entry.Record.CleanupConfirmed=clean;entry.Record.State=clean?(entry.RequestedStop.HasValue?AgentJobState.Stopped:AgentJobState.StartFailed):AgentJobState.CleanupFailed;
                    }
                    else{entry.Record.State=entry.RequestedStop.HasValue?AgentJobState.Stopped:AgentJobState.StartFailed;entry.Record.CleanupConfirmed=true;}
                    entry.Record.StopReason=entry.RequestedStop;
                    if(AgentJobStore.IsTerminal(entry.Record.State)){entry.Record.FinishedUtc=clock.UtcNow;Release(entry);}
                    if(saved)try{await PersistAsync(entry,CancellationToken.None).ConfigureAwait(false);}catch(Exception error){Debug.WriteLine(error);}
                    if(!reserved)Release(entry);entry.Published=new AgentJobSnapshot(entry.Record);Notify();System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();throw new InvalidOperationException();
            }
            }
            finally{if(ownerHeld)ownerGate.Release();}
        }
        void HandleLogFault(Entry entry){StopAfterLogFaultAsync(entry);}
        async void StopAfterLogFaultAsync(Entry entry){try{await StopEntryAsync(entry,AgentJobStopReason.LogStorageFailed,CancellationToken.None).ConfigureAwait(false);}catch(Exception error){Debug.WriteLine(error);}}
        async void MonitorAsync(Entry entry)
        {
            Exception failure=null;
            try
            {
                while(!entry.Lifetime.IsCancellationRequested)
                {
                    await RefreshAsync(entry,CancellationToken.None).ConfigureAwait(false);
                    if(entry.Lifetime.IsCancellationRequested||entry.Record.State!=AgentJobState.Running)return;
                    if(clock.ElapsedMilliseconds-entry.StartedMilliseconds>=entry.Record.Spec.TimeoutSeconds*1000L)
                    {await StopEntryAsync(entry,AgentJobStopReason.Timeout,CancellationToken.None).ConfigureAwait(false);return;}
                    await Task.WhenAny(entry.Process.Completion,Task.Delay(100,entry.Lifetime.Token)).ConfigureAwait(false);
                    if(entry.Process.Completion.IsCompleted)
                    {
                        var completed=await entry.Process.Completion.ConfigureAwait(false);
                        if(!completed.TreeExited||!completed.StreamsDrained)throw new IOException("job_stream_drain_timeout");
                    }
                }
            }
            catch(OperationCanceledException){}
            catch(Exception error){failure=error;}
            if(failure!=null){Debug.WriteLine(failure);try{await StopEntryAsync(entry,AgentJobStopReason.LogStorageFailed,CancellationToken.None,failure is IOException&&failure.Message=="job_stream_drain_timeout"?"stream_drain_timeout":"process_monitor_failed").ConfigureAwait(false);}catch(Exception cleanup){Debug.WriteLine(cleanup);}}
        }
        bool ObserveExit(Entry entry,AgentJobProcessExit status)
        {
            bool changed=entry.Record.RootExited!=status.RootExited||entry.Record.RootExitCode!=status.RootExitCode;
            entry.Record.RootExited=status.RootExited;entry.Record.RootExitCode=status.RootExitCode;
            if(status.RootExited&&!entry.Record.RootExitObservedUtc.HasValue){entry.Record.RootExitObservedUtc=clock.UtcNow;changed=true;}
            if(status.TreeExited&&!entry.Record.TreeExitObservedUtc.HasValue){entry.Record.TreeExitObservedUtc=clock.UtcNow;changed=true;}
            return changed;
        }
        async Task RefreshAsync(Entry entry,CancellationToken token)
        {using(Maintenance.EnterActivity()){
            await entry.Mutation.WaitAsync(token).ConfigureAwait(false);
            using(var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,entry.RefreshCancellation.Token))
            {
            try
            {
                if(AgentJobStore.IsTerminal(entry.Record.State)){if(entry.FinalPersistencePending)await PersistTerminalAsync(entry).ConfigureAwait(false);return;}
                if(entry.Process==null||entry.Record.State!=AgentJobState.Running)return;
                var status=entry.Process.Status;bool changed=ObserveExit(entry,status);
                if(status.TreeExited&&status.StreamsDrained)
                {
                    bool clean=await CleanupAsync(entry).ConfigureAwait(false);entry.Record.CleanupConfirmed=clean;
                    if(clean)
                    {
                        // EOF belongs to the application, even when a log-view read is cancelled.
                        // A log failure cannot retain a directory after its native tree is gone.
                        try
                        {
                            LogFor(entry).Complete();
                            using(var flush=CancellationTokenSource.CreateLinkedTokenSource(entry.RefreshCancellation.Token))
                            {
                                flush.CancelAfter(4500);
                                var writing=LogFor(entry).FlushAsync(flush.Token).ContinueWith(task=>{task.GetAwaiter().GetResult();return true;},TaskScheduler.Default);
                                await AgentAuthorizationService.AwaitCancellation(writing,flush.Token).ConfigureAwait(false);
                            }
                        }
                        catch(Exception error){Debug.WriteLine(error);entry.Record.LogWarning="log_storage_failed";}
                        finally
                        {
                            entry.Record.State=status.RootExitCode==0?AgentJobState.Succeeded:AgentJobState.Failed;entry.Record.FinishedUtc=clock.UtcNow;entry.FinalPersistencePending=true;
                            Release(entry);entry.Published=new AgentJobSnapshot(entry.Record);Notify();
                        }
                    }
                    else {entry.Record.State=AgentJobState.CleanupFailed;entry.Record.LogWarning="cleanup_unconfirmed";}
                    changed=true;
                }
                if(changed){entry.Published=new AgentJobSnapshot(entry.Record);if(AgentJobStore.IsTerminal(entry.Record.State))await PersistTerminalAsync(entry).ConfigureAwait(false);else await PersistAsync(entry,cancellation.Token).ConfigureAwait(false);Notify();}
            }
            catch(OperationCanceledException){if(!entry.RefreshCancellation.IsCancellationRequested)throw;}
            finally{entry.Mutation.Release();}
            }
        }}
        static AgentJobSnapshot Snapshot(Entry entry){return entry.Published;}
        internal bool TrySnapshot(AgentJobOwner owner,string id,out AgentJobSnapshot snapshot)
        {lock(gate){Entry entry;if(owner!=null&&jobs.TryGetValue(id??"",out entry)&&owner.Matches(entry.Record.Owner)){snapshot=Snapshot(entry);return true;}snapshot=null;return false;}}
        AgentJobLogStore LogFor(Entry entry){return entry.Log??(entry.Log=store.OpenLog(entry.Record.Id));}
        async Task PersistAsync(Entry entry,CancellationToken token)
        {
            var snapshot=new AgentJobSnapshot(entry.Record);if(AgentJobStore.IsTerminal(snapshot.State))entry.FinalPersistencePending=true;
            await AgentAuthorizationService.AwaitCancellation(store.SaveAsync(snapshot,token).ContinueWith(task=>{task.GetAwaiter().GetResult();return true;},TaskScheduler.Default),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();entry.Published=snapshot;if(AgentJobStore.IsTerminal(snapshot.State))entry.FinalPersistencePending=false;
        }
        async Task PersistTerminalAsync(Entry entry)
        {
            using(var deadline=new CancellationTokenSource(4500))
            {
                try{await PersistAsync(entry,deadline.Token).ConfigureAwait(false);}
                catch(Exception){entry.Record.LogWarning=entry.Record.LogWarning??"metadata_storage_failed";entry.Published=new AgentJobSnapshot(entry.Record);Notify();throw;}
            }
        }
        Entry Find(AgentJobOwner owner,string id)
        {
            lock(gate){Entry entry;if(!jobs.TryGetValue(id??"",out entry)||owner==null||!owner.Matches(entry.Record.Owner))throw new KeyNotFoundException("job_not_found");return entry;}
        }
        // Reading a recovered terminal job can lazily publish its log index.
        public async Task<AgentJobPage> GetAsync(AgentJobOwner owner,string id,string cursor,int limitBytes,CancellationToken token)
        {using(Maintenance.EnterActivity()){
            var entry=Find(owner,id);await RefreshAsync(entry,token).ConfigureAwait(false);
            if(entry.Log==null&&entry.Start!=null)
            {
                if(limitBytes<AgentJobPolicy.MinimumPageBytes||limitBytes>AgentJobPolicy.MaximumPageBytes)throw new ArgumentOutOfRangeException("limit_bytes");
                if(!String.IsNullOrEmpty(cursor))throw new ArgumentException("invalid_cursor");
                return new AgentJobPage(Snapshot(entry),new AgentJobLogRecord[0],"",false,null);
            }
            var page=await LogFor(entry).ReadAsync(cursor,limitBytes,token).ConfigureAwait(false);
            return new AgentJobPage(Snapshot(entry),page.Records,page.NextCursor,page.HasMore,page.Gap);
        }}
        public Task<AgentJobSnapshot> StopAsync(AgentJobOwner owner,string id,AgentJobStopReason reason,CancellationToken token)
        {return StopEntryAsync(Find(owner,id),reason,token);}
        internal Task<AgentJobSnapshot> StopForUserAsync(string id,AgentJobStopReason reason,CancellationToken token)
        {Entry entry;lock(gate){if(!jobs.TryGetValue(id??"",out entry))throw new KeyNotFoundException("job_not_found");}return StopEntryAsync(entry,reason,token);}
        async Task<bool> CleanupAsync(Entry entry,CancellationToken token=default(CancellationToken))
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(4500);
                try{var result=await AgentAuthorizationService.AwaitCancellation(entry.Process.StopAsync(deadline.Token),deadline.Token).ConfigureAwait(false);return result.CleanupConfirmed;}
                catch(Exception error){Debug.WriteLine(error);return false;}
                finally{var metrics=entry.Process as IAgentJobCompletionMetrics;if(metrics!=null)entry.Record.TimingsMs["eof_drain"]=metrics.EofDrainMilliseconds;}
            }
        }
        async Task<AgentJobSnapshot> StopEntryAsync(Entry entry,AgentJobStopReason reason,CancellationToken token,string warning=null)
        {using(Maintenance.EnterActivity()){
            var elapsed=Stopwatch.StartNew();bool pending;
            lock(gate){pending=!entry.HandedOff;if(pending)entry.RequestedStop=entry.RequestedStop??reason;}
            if(pending)entry.PendingStart.Cancel();entry.RefreshCancellation.Cancel();
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
            deadline.CancelAfter(4500);
            try{await entry.Mutation.WaitAsync(deadline.Token).ConfigureAwait(false);}
            catch(OperationCanceledException){token.ThrowIfCancellationRequested();throw new IOException("job_cleanup_unconfirmed");}
            try
            {
                if(AgentJobStore.IsTerminal(entry.Record.State)){if(entry.FinalPersistencePending)await PersistAsync(entry,deadline.Token).ConfigureAwait(false);return Snapshot(entry);}
                // Pending starts are owned by their request until handoff; exit cancels those first.
                if(entry.Process==null)throw new InvalidOperationException("job_starting");
                entry.Record.StopReason=entry.Record.StopReason??reason;entry.Record.State=AgentJobState.Stopping;
                if(reason==AgentJobStopReason.LogStorageFailed)entry.Record.LogWarning=warning??"log_storage_failed";
                Exception storageFailure=null;
                entry.Published=new AgentJobSnapshot(entry.Record);Notify();
                // An unavailable disk must never stand between Stop and tree cleanup.
                bool clean=await CleanupAsync(entry,deadline.Token).ConfigureAwait(false);var status=entry.Process.Status;
                ObserveExit(entry,status);entry.Record.CleanupConfirmed=clean;
                entry.Record.State=clean?(entry.Record.StopReason==AgentJobStopReason.Timeout?AgentJobState.TimedOut:AgentJobState.Stopped):AgentJobState.CleanupFailed;
                entry.Record.TimingsMs["stop"]=elapsed.ElapsedMilliseconds;
                if(clean){entry.Record.FinishedUtc=clock.UtcNow;if(entry.Log!=null)entry.Log.Complete();Release(entry);}
                entry.Published=new AgentJobSnapshot(entry.Record);
                try{await AgentAuthorizationService.AwaitCancellation(PersistAsync(entry,deadline.Token).ContinueWith(task=>{task.GetAwaiter().GetResult();return true;},TaskScheduler.Default),deadline.Token).ConfigureAwait(false);}catch(Exception error){storageFailure=error;entry.Record.LogWarning=entry.Record.LogWarning??"metadata_storage_failed";entry.Published=new AgentJobSnapshot(entry.Record);}
                Notify();if(storageFailure!=null)throw new IOException("job_metadata_failed",storageFailure);return Snapshot(entry);
            }
            finally{entry.Mutation.Release();}
            }
        }}
        void Release(Entry entry)
        {entry.Lifetime.Cancel();if(entry.Process!=null)entry.Process.Dispose();if(entry.DirectoryLease!=null){entry.DirectoryLease.Dispose();entry.DirectoryLease=null;}}
        public bool HasActiveJobs {get{lock(gate)return jobs.Values.Any(entry=>!AgentJobStore.IsTerminal(entry.Record.State));}}
        internal void CheckForUninstall(Dafeiyu.Maintenance.UninstallOwner owner)
        {
            owner.Require(Maintenance);
            lock(gate)if(!recovered||(exitRequested&&!owner.IsClosed)||jobs.Values.Any(e=>!AgentJobStore.IsTerminal(e.Record.State)||e.FinalPersistencePending||!String.IsNullOrEmpty(e.Record.LogWarning)))
                throw new System.IO.IOException(TextResources.Get("dataMaintenance.storageFailure"));
        }
        public IList<AgentJobSnapshot> ListForConversation(string conversationId){lock(gate)return jobs.Values.Where(entry=>entry.Record.Owner.ConversationId==conversationId).Select(Snapshot).ToList().AsReadOnly();}
        internal IList<AgentJobSnapshot> ListAll(){lock(gate)return jobs.Values.Select(Snapshot).ToList().AsReadOnly();}
        public IList<AgentJobSnapshot> ListForProject(string projectId)
        {lock(gate)return jobs.Values.Where(entry=>entry.Record.Owner.ProjectId==projectId).Select(Snapshot).ToList().AsReadOnly();}
        SemaphoreSlim OwnerGate(string projectId)
        {lock(gate){SemaphoreSlim owner;if(!ownerGates.TryGetValue(projectId,out owner)){owner=new SemaphoreSlim(1,1);ownerGates.Add(projectId,owner);}return owner;}}
        internal async Task RunOwnerMutationAsync(string projectId,string conversationId,bool stopActive,Func<Task> mutation,CancellationToken token)
        {
            if(String.IsNullOrWhiteSpace(projectId)||mutation==null)throw new ArgumentException("owner");
            var ownerGate=OwnerGate(projectId);await ownerGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Entry[] active;lock(gate)
                {
                    if(!recovered)throw new InvalidOperationException("jobs_recovering");
                    if(exitRequested)throw new InvalidOperationException("jobs_exiting");
                    active=jobs.Values.Where(entry=>entry.Record.Owner.ProjectId==projectId&&(conversationId==null||entry.Record.Owner.ConversationId==conversationId)&&!AgentJobStore.IsTerminal(entry.Record.State)).ToArray();
                }
                if(active.Length>0&&!stopActive)throw new InvalidOperationException("job_owner_busy");
                foreach(var entry in active)
                {var stopped=await StopEntryAsync(entry,AgentJobStopReason.OwnerDeleted,token).ConfigureAwait(false);if(!stopped.CleanupConfirmed)throw new InvalidOperationException("job_cleanup_unconfirmed");}
                token.ThrowIfCancellationRequested();await mutation().ConfigureAwait(false);
            }
            finally{ownerGate.Release();}
        }
        internal bool TryRunOwnerMutation(string projectId,string conversationId,Action mutation)
        {
            var ownerGate=OwnerGate(projectId);if(!ownerGate.Wait(0))return false;
            try
            {
                lock(gate)
                {
                    if(!recovered)throw new InvalidOperationException("jobs_recovering");
                    if(exitRequested)throw new InvalidOperationException("jobs_exiting");
                    if(jobs.Values.Any(entry=>entry.Record.Owner.ProjectId==projectId&&(conversationId==null||entry.Record.Owner.ConversationId==conversationId)&&!AgentJobStore.IsTerminal(entry.Record.State)))return false;
                }
                mutation();return true;
            }
            finally{ownerGate.Release();}
        }
        public async Task<AgentJobExitReport> StopAllForExitAsync(CancellationToken token)
        {
            var elapsed=Stopwatch.StartNew();Entry[] pending;
            lock(gate){exitRequested=true;pending=invocations.Values.ToArray();}exiting.Cancel();
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(10000);Task<AgentJobSnapshot>[] stops=null;bool joined=false;
                try
                {
                    foreach(var entry in pending)if(entry.Start!=null)try{await AgentAuthorizationService.AwaitCancellation(entry.Start,deadline.Token).ConfigureAwait(false);}catch(OperationCanceledException){deadline.Token.ThrowIfCancellationRequested();}catch(Exception error){Debug.WriteLine(error);}
                    // Terminal publication may precede the final metadata commit: join every entry mutation.
                    Entry[] owned;lock(gate)owned=jobs.Values.ToArray();
                    stops=owned.Select(entry=>StopEntryAsync(entry,AgentJobStopReason.ApplicationExit,deadline.Token)).ToArray();
                    await AgentAuthorizationService.AwaitCancellation(Task.WhenAll(stops),deadline.Token).ConfigureAwait(false);joined=true;
                }
                catch(OperationCanceledException){}
                catch(Exception error){Debug.WriteLine(error);}
                if(stops!=null&&stops.All(task=>task.IsCompleted))joined=true;
                AgentJobSnapshot[] snapshots;lock(gate)snapshots=jobs.Values.Select(Snapshot).ToArray();
                return new AgentJobExitReport(joined&&snapshots.All(job=>AgentJobStore.IsTerminal(job.State)),snapshots,elapsed.ElapsedMilliseconds);
            }
        }
        void Notify(){var changed=Changed;if(changed!=null)foreach(Action callback in changed.GetInvocationList())try{callback();}catch(Exception error){Debug.WriteLine(error);}}
    }
}
