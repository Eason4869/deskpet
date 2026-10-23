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
        readonly object gate=new object();
        readonly string dataDirectory;
        readonly Func<IAgentJobClock,IAgentJobProcessFactory> createFactory;
        readonly IAgentJobClock clock;
        Task<bool> initialization;
        Task storeClosing;
        AgentJobStore store;
        AgentJobManager manager;
        readonly HashSet<Task> mutations=new HashSet<Task>();
        bool exiting,cleanupConfirmed,disposed;
        string failureCode;
        public AgentJobApplicationService(string dataDirectory,Func<IAgentJobClock,IAgentJobProcessFactory> createFactory,IAgentJobClock clock)
        {
            if(String.IsNullOrWhiteSpace(dataDirectory)||!Path.IsPathRooted(dataDirectory))throw new ArgumentException("dataDirectory");
            if(createFactory==null||clock==null)throw new ArgumentNullException("services");
            this.dataDirectory=dataDirectory;this.createFactory=createFactory;this.clock=clock;
        }
        public AgentJobManager Manager {get{lock(gate)return manager;}}
        public string FailureCode {get{lock(gate)return failureCode;}}
        public Task<bool> InitializeAsync()
        {
            lock(gate)
            {
                if(exiting||disposed)return Task.FromResult(false);
                return initialization??(initialization=Task.Run((Func<Task<bool>>)InitializeCoreAsync));
            }
        }
        async Task<bool> InitializeCoreAsync()
        {
            AgentJobStore opened=null;
            try
            {
                // Acquire the exclusive lease before constructing any platform executor.
                opened=new AgentJobStore(dataDirectory,clock);
                var recovered=new AgentJobManager(opened,createFactory(clock),clock);
                await recovered.RecoverAsync(CancellationToken.None).ConfigureAwait(false);
                lock(gate){store=opened;manager=recovered;}
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
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock(gate)
            {
                if(exiting||disposed)throw new InvalidOperationException("jobs_exiting");
                mutations.Add(completion.Task);
            }
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
                finally{lock(gate)mutations.Remove(completion.Task);}
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
        {
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            AgentJobManager owned;
            lock(gate){EnsureWritable();owned=manager;mutations.Add(completion.Task);}
            try{return mutation(owned);}
            finally{completion.TrySetResult(true);lock(gate)mutations.Remove(completion.Task);}
        }
        public async Task<AgentJobExitReport> StopAllForExitAsync(CancellationToken token)
        {
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
                    Task closing;
                    lock(gate)
                    {
                        if(store!=null&&(storeClosing==null||storeClosing.IsFaulted||storeClosing.IsCanceled))
                        {var ownedStore=store;storeClosing=Task.Run(()=>ownedStore.Dispose());}
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
        }
        public void Dispose()
        {
            lock(gate)
            {
                if(disposed)return;
                if(!cleanupConfirmed)throw new InvalidOperationException("job_cleanup_unconfirmed");
                // Native, catalog and storage cleanup has already joined off the UI thread.
                disposed=true;store=null;manager=null;
            }
        }
    }
}
