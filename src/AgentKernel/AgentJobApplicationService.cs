using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    // The lease and native process owner outlive every conversation and task panel.
    public sealed class AgentJobApplicationService : IDisposable
    {
        public Dafeiyu.Maintenance.DataMaintenanceGate Maintenance {get;private set;}
        readonly object gate=new object();
        readonly string dataDirectory;
        readonly Func<IAgentJobClock,IAgentJobProcessFactory> createFactory;
        readonly IAgentJobClock clock;
        readonly AgentJobStorageIO storageIO;
        Task<bool> initialization;
        Task storeClosing;
        AgentJobStore store;
        AgentJobManager manager;
        readonly HashSet<Task> mutations=new HashSet<Task>();
        bool exiting,cleanupConfirmed,disposed;
        string failureCode;
        AgentJobCompletionTracker completions;
        public event Action<AgentJobSnapshot> TerminalCompleted;
        void JobsChanged()
        {
            AgentJobManager owned;AgentJobCompletionTracker tracker;lock(gate){owned=manager;tracker=completions;}
            if(owned==null||tracker==null)return;
            foreach(var job in tracker.Observe(owned.ListAll()))
            {var handler=TerminalCompleted;if(handler!=null)foreach(Action<AgentJobSnapshot> callback in handler.GetInvocationList())try{callback(job);}catch(Exception error){Debug.WriteLine(error);}}
        }
        public AgentJobApplicationService(string dataDirectory,Func<IAgentJobClock,IAgentJobProcessFactory> createFactory,IAgentJobClock clock):this(dataDirectory,createFactory,clock,null,null){}
        public AgentJobApplicationService(string dataDirectory,Func<IAgentJobClock,IAgentJobProcessFactory> createFactory,IAgentJobClock clock,Dafeiyu.Maintenance.DataMaintenanceGate maintenance):this(dataDirectory,createFactory,clock,null,maintenance){}
        internal AgentJobApplicationService(string dataDirectory,Func<IAgentJobClock,IAgentJobProcessFactory> createFactory,IAgentJobClock clock,AgentJobStorageIO storageIO):this(dataDirectory,createFactory,clock,storageIO,null){}
        internal AgentJobApplicationService(string dataDirectory,Func<IAgentJobClock,IAgentJobProcessFactory> createFactory,IAgentJobClock clock,AgentJobStorageIO storageIO,Dafeiyu.Maintenance.DataMaintenanceGate maintenance)
        {
            if(String.IsNullOrWhiteSpace(dataDirectory)||!Path.IsPathRooted(dataDirectory))throw new ArgumentException("dataDirectory");
            if(createFactory==null||clock==null)throw new ArgumentNullException("services");
            Maintenance=maintenance??new Dafeiyu.Maintenance.DataMaintenanceGate();this.dataDirectory=dataDirectory;this.createFactory=createFactory;this.clock=clock;this.storageIO=storageIO;
        }
        public void ConfigureMaintenance(Dafeiyu.Maintenance.DataMaintenanceGate maintenance)
        {if(maintenance==null)throw new ArgumentNullException("maintenance");lock(gate){if(initialization!=null||mutations.Count!=0)throw new InvalidOperationException("jobs_already_initialized");Maintenance=maintenance;}}
        public AgentJobManager Manager {get{lock(gate)return manager;}}
        public string FailureCode {get{lock(gate)return failureCode;}}
        public Task<bool> InitializeAsync()
        {
            lock(gate)
            {
                if(exiting||disposed)return Task.FromResult(false);
                if(initialization!=null)return initialization;var admission=Maintenance.EnterActivity();
                return initialization=Task.Run(async()=>{try{return await InitializeCoreAsync().ConfigureAwait(false);}finally{admission.Dispose();}});
            }
        }
        public Task<bool> RetryInitializationAsync()
        {
            lock(gate)
            {
                if(!exiting&&!disposed&&manager==null&&initialization!=null&&initialization.IsCompleted){initialization=null;failureCode=null;}
                return InitializeAsync();
            }
        }
        sealed class DeferredFactory : IAgentJobProcessFactory
        {
            readonly Lazy<IAgentJobProcessFactory> native;
            internal DeferredFactory(Func<IAgentJobClock,IAgentJobProcessFactory> create,IAgentJobClock clock)
            {native=new Lazy<IAgentJobProcessFactory>(()=>create(clock),true);}
            public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)
            {return Task.Run(delegate{token.ThrowIfCancellationRequested();return native.Value.PrepareAsync(spec,policy,output,token);},token);}
        }
        async Task<bool> InitializeCoreAsync()
        {
            AgentJobStore opened=null;
            try
            {
                // Acquire the exclusive lease before constructing any platform executor.
                opened=storageIO==null?new AgentJobStore(dataDirectory,clock):new AgentJobStore(dataDirectory,clock,storageIO);
                var recovered=new AgentJobManager(opened,new DeferredFactory(createFactory,clock),clock,Maintenance);
                await recovered.RecoverAsync(CancellationToken.None).ConfigureAwait(false);
                lock(gate){store=opened;manager=recovered;completions=new AgentJobCompletionTracker(recovered.ListAll());recovered.Changed+=JobsChanged;}
                return true;
            }
            catch(Exception error)
            {
                if(opened!=null)opened.Dispose();
                lock(gate)failureCode="job_storage_unavailable";
                Debug.WriteLine(error);return false;
            }
        }
        public async Task EnsureWritableAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await AgentAuthorizationService.AwaitCancellation(InitializeAsync(),token).ConfigureAwait(false);
            lock(gate)
            {
                if(exiting||disposed)throw new InvalidOperationException("jobs_exiting");
                if(manager==null)throw new IOException("job_storage_unavailable");
            }
        }
        internal async Task<AgentJobManager> GetManagerAsync(CancellationToken token)
        {
            await AgentAuthorizationService.AwaitCancellation(InitializeAsync(),token).ConfigureAwait(false);
            lock(gate)return exiting||disposed?null:manager;
        }
        internal Task RunMutationAsync(Func<AgentJobManager,Task> mutation,CancellationToken token)
        {
            var admission=Maintenance.EnterActivity();
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try{lock(gate)
            {
                if(exiting||disposed)throw new InvalidOperationException("jobs_exiting");
                mutations.Add(completion.Task);
            }}catch{admission.Dispose();throw;}
            Task.Run(async delegate
            {
                Exception failure=null;bool cancelled=false;
                try
                {
                    await EnsureWritableAsync(token).ConfigureAwait(false);
                    AgentJobManager owned;lock(gate)owned=manager;
                    await mutation(owned).ConfigureAwait(false);
                }
                catch(OperationCanceledException){cancelled=true;}
                catch(Exception error){failure=error;}
                finally{lock(gate)mutations.Remove(completion.Task);admission.Dispose();}
                if(cancelled)completion.TrySetCanceled();
                else if(failure!=null)completion.TrySetException(failure);
                else completion.TrySetResult(true);
            });
            return completion.Task;
        }
        internal void EnsureWritable()
        {
            lock(gate)
            {
                if(exiting||disposed)throw new InvalidOperationException("jobs_exiting");
                if(initialization==null||!initialization.IsCompleted)throw new InvalidOperationException("jobs_recovering");
                if(manager==null)throw new IOException("job_storage_unavailable");
            }
        }
        internal bool TryRunMutation(Func<AgentJobManager,bool> mutation)
        {using(Maintenance.EnterActivity()){
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            AgentJobManager owned;
            lock(gate){EnsureWritable();owned=manager;mutations.Add(completion.Task);}
            try{return mutation(owned);}
            finally{completion.TrySetResult(true);lock(gate)mutations.Remove(completion.Task);}
        }}
        public Task<AgentJobExitReport> StopAllForExitAsync(CancellationToken token){return StopForExitAsync(token,true);}
        public Task<AgentJobExitReport> PrepareForExitAsync(CancellationToken token){return StopForExitAsync(token,false);}
        async Task<AgentJobExitReport> StopForExitAsync(CancellationToken token,bool releaseStore)
        {using(Maintenance.EnterActivity()){
            var elapsed=Stopwatch.StartNew();Task<bool> pending;Task[] admitted;
            AgentJobExitReport completed=null;
            lock(gate){exiting=true;cleanupConfirmed=false;pending=initialization;admitted=mutations.ToArray();}
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(10000);
                try
                {
                    if(pending!=null)await AgentAuthorizationService.AwaitCancellation(pending,deadline.Token).ConfigureAwait(false);
                    AgentJobManager owned;lock(gate)owned=manager;
                    var report=owned==null?new AgentJobExitReport(true,new AgentJobSnapshot[0],elapsed.ElapsedMilliseconds):await owned.StopAllForExitAsync(deadline.Token).ConfigureAwait(false);completed=report;
                    // A failed mutation still needs joining; it may have performed rollback.
                    try{await AgentAuthorizationService.AwaitCancellation(Task.WhenAll(admitted).ContinueWith(task=>{task.GetAwaiter().GetResult();return true;},TaskScheduler.Default),deadline.Token).ConfigureAwait(false);}
                    catch(OperationCanceledException){deadline.Token.ThrowIfCancellationRequested();}
                    catch(Exception error){Debug.WriteLine(error);}
                    if(!report.CleanupConfirmed)return new AgentJobExitReport(false,report.Jobs,elapsed.ElapsedMilliseconds);
                    if(!releaseStore)return new AgentJobExitReport(true,report.Jobs,elapsed.ElapsedMilliseconds);
                    Task closing;
                    lock(gate)
                    {
                        if(store!=null&&(storeClosing==null||storeClosing.IsFaulted||storeClosing.IsCanceled))
                        {var ownedStore=store;storeClosing=ownedStore.TrackMaintenance(Task.Run(()=>ownedStore.Dispose()));}
                        closing=storeClosing;
                    }
                    if(closing!=null)await AgentAuthorizationService.AwaitCancellation(closing.ContinueWith(task=>{task.GetAwaiter().GetResult();return true;},TaskScheduler.Default),deadline.Token).ConfigureAwait(false);
                    lock(gate)cleanupConfirmed=report.CleanupConfirmed;
                    return new AgentJobExitReport(report.CleanupConfirmed,report.Jobs,elapsed.ElapsedMilliseconds);
                }
                catch(OperationCanceledException)
                {
                    // Initialization may still own the lease; retain the service for retry.
                    return new AgentJobExitReport(false,completed==null?new AgentJobSnapshot[0]:completed.Jobs,elapsed.ElapsedMilliseconds);
                }
                catch(Exception error){Debug.WriteLine(error);return new AgentJobExitReport(false,completed==null?new AgentJobSnapshot[0]:completed.Jobs,elapsed.ElapsedMilliseconds);}
            }
        }}
        public void Dispose()
        {
            lock(gate)
            {
                if(disposed)return;
                if(!cleanupConfirmed)throw new InvalidOperationException("job_cleanup_unconfirmed");
                // Native, catalog and storage cleanup has already joined off the UI thread.
                if(manager!=null){manager.Changed-=JobsChanged;manager.RetireMaintenance();}disposed=true;store=null;manager=null;completions=null;
            }
        }
    }
}
