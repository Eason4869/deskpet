using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentJobManagerTests
    {
        static int checks;
        static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        sealed class Authorization : IAgentAuthorizationService
        {
            internal int Calls;
            internal TaskCompletionSource<bool> Barrier;
            public async Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken token)
            {Interlocked.Increment(ref Calls);if(invocation.Action!="shell")throw new Exception("background start must request shell authorization");if(Barrier!=null)await AgentAuthorizationService.AwaitCancellation(Barrier.Task,token);return new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow};}
        }
        sealed class Process : IAgentJobProcess
        {
            internal int Resumes,Stops;
            internal bool ConfirmCleanup=true;
            internal readonly TaskCompletionSource<AgentJobProcessExit> Done=new TaskCompletionSource<AgentJobProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
            public AgentJobProcessExit Status {get;internal set;}
            public Task<AgentJobProcessExit> Completion {get{return Done.Task;}}
            internal Process(){Status=new AgentJobProcessExit(false,null,false,false,1);}
            public void Resume(){if(Stops>0)throw new Exception("Process resumed after cleanup");Interlocked.Increment(ref Resumes);}
            public Task<AgentJobCleanupResult> StopAsync(CancellationToken token){Interlocked.Increment(ref Stops);if(ConfirmCleanup){Status=new AgentJobProcessExit(true,null,true,true,0);Done.TrySetResult(Status);}return Task.FromResult(new AgentJobCleanupResult(ConfirmCleanup,ConfirmCleanup?null:"held"));}
            public void Dispose(){}
        }
        sealed class Factory : IAgentJobProcessFactory
        {
            internal readonly List<Process> Processes=new List<Process>();
            internal Action<Process> BeforeReturn;
            public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)
            {var process=new Process();lock(Processes)Processes.Add(process);if(BeforeReturn!=null)BeforeReturn(process);return Task.FromResult<IAgentJobProcess>(process);}
        }
        sealed class Lease : IDisposable {public void Dispose(){}}
        sealed class Fixture : IDisposable
        {
            internal readonly AgentJobTestSupport.TemporaryDirectory Temporary=new AgentJobTestSupport.TemporaryDirectory();
            internal readonly AgentJobTestSupport.Clock Clock=new AgentJobTestSupport.Clock();
            internal readonly Factory Factory=new Factory();
            internal readonly Authorization Authorization=new Authorization();
            internal readonly AgentJobStore Store;
            internal readonly AgentJobTestSupport.ControlledIO IO=new AgentJobTestSupport.ControlledIO();
            internal readonly object Manager;
            internal readonly Type ManagerType;
            internal Fixture()
            {
                Store=new AgentJobStore(Temporary.Path,Clock,IO);
                ManagerType=typeof(AgentJobStore).Assembly.GetType("Dafeiyu.AgentJobManager");
                if(ManagerType==null){Store.Dispose();Temporary.Dispose();throw new Exception("AgentJobManager is missing");}
                Manager=Activator.CreateInstance(ManagerType,Store,Factory,Clock);
                Invoke<Task>("RecoverAsync",CancellationToken.None).GetAwaiter().GetResult();
            }
            internal T Invoke<T>(string name,params object[] arguments)
            {try{return (T)ManagerType.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(Manager,arguments);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
            internal AgentJobStartContext Context(string call,string project="project",string conversation="conversation")
            {
                var owner=new AgentJobOwner(project,conversation,"primary");
                var invocation=new AgentToolInvocation("start_job",new Dictionary<string,object>{{"command","echo fixture"},{"title","echo fixture"},{"working_directory",Temporary.Path},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId=project,AgentId="primary",TaskId="persisted-task",CallId=call});
                return new AgentJobStartContext(owner,invocation,Authorization,()=>new AgentJobPolicySnapshot(Temporary.Path,"directory",1,1800,true,false,false,AgentApprovalMode.Ask,null),()=>new Lease(),()=>{});
            }
            internal Task<AgentJobSnapshot> Start(string call,string project="project",string conversation="conversation",string command="echo fixture")
            {return Invoke<Task<AgentJobSnapshot>>("StartAsync",Context(call,project,conversation),new AgentJobSpec(command,null,Temporary.Path,1800),CancellationToken.None);}
            public void Dispose(){IO.FailCommit=false;try{Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult();}finally{Store.Dispose();IO.Dispose();Temporary.Dispose();}}
        }
        static void TenConcurrentStartsLaunchOnce()
        {
            using(var f=new Fixture())
            {
                f.Authorization.Barrier=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var calls=Enumerable.Range(0,10).Select(i=>f.Start("same-call")).ToArray();f.Authorization.Barrier.SetResult(true);Task.WaitAll(calls);
                Check(calls.Select(call=>call.Result.Id).Distinct().Count()==1&&f.Factory.Processes.Count==1&&f.Factory.Processes[0].Resumes==1,"ten concurrent calls launch exactly one process");
                Check(f.Authorization.Calls==1,"idempotent concurrent starts ask authorization once");
                bool conflict=false;try{f.Start("same-call",command:"echo changed").GetAwaiter().GetResult();}catch(InvalidOperationException error){conflict=error.Message=="job_idempotency_conflict";}Check(conflict&&f.Factory.Processes.Count==1,"same key with changed payload cannot launch again");
                var second=f.Start("second").GetAwaiter().GetResult();bool full=false;try{f.Start("third").GetAwaiter().GetResult();}catch(InvalidOperationException error){full=error.Message=="job_project_capacity";}Check(full&&f.Factory.Processes.Count==2,"each project reserves at most two live jobs");
                Check(f.Start("same-call").Result.Id==calls[0].Result.Id,"replay succeeds before capacity is checked");
            }
        }
        static void OwnershipAndCleanupFailure()
        {
            using(var f=new Fixture())
            {
                var first=f.Start("a").Result;f.Factory.Processes[0].ConfirmCleanup=false;
                var failed=f.Invoke<Task<AgentJobSnapshot>>("StopAsync",first.Owner,first.Id,AgentJobStopReason.User,CancellationToken.None).Result;
                Check(failed.State==AgentJobState.CleanupFailed&&!failed.CleanupConfirmed,"unconfirmed cleanup keeps its active reservation");
                bool hidden=false;try{f.Invoke<Task<AgentJobPage>>("GetAsync",new AgentJobOwner("project","other","primary"),first.Id,"",1024,CancellationToken.None).GetAwaiter().GetResult();}catch(KeyNotFoundException error){hidden=error.Message=="job_not_found";}Check(hidden,"foreign conversation cannot inspect a job");
                f.Factory.Processes[0].ConfirmCleanup=true;
                var stopped=f.Invoke<Task<AgentJobSnapshot>>("StopAsync",first.Owner,first.Id,AgentJobStopReason.User,CancellationToken.None).Result;
                Check(stopped.State==AgentJobState.Stopped&&stopped.CleanupConfirmed,"retry confirms cleanup and releases the slot");
            }
        }
        static void MetadataFailureCannotPreventStopping()
        {
            using(var f=new Fixture())
            {
                var started=f.Start("metadata-failure").Result;f.IO.FailCommit=true;
                try{f.Invoke<Task<AgentJobSnapshot>>("StopAsync",started.Owner,started.Id,AgentJobStopReason.User,CancellationToken.None).GetAwaiter().GetResult();}catch(System.IO.IOException){}
                Check(f.Factory.Processes[0].Stops==1,"metadata write failure cannot prevent terminating an owned process");
                f.IO.FailCommit=false;
            }
        }
        static void RootExitAndCancellationAfterHandoff()
        {
            using(var f=new Fixture())using(var request=new CancellationTokenSource())
            {
                var started=f.Invoke<Task<AgentJobSnapshot>>("StartAsync",f.Context("handoff"),new AgentJobSpec("echo fixture",null,f.Temporary.Path,1800),request.Token).Result;
                request.Cancel();var process=f.Factory.Processes[0];process.Status=new AgentJobProcessExit(true,0,false,false,1);
                var running=f.Invoke<Task<AgentJobPage>>("GetAsync",started.Owner,started.Id,"",1024,CancellationToken.None).Result.Snapshot;
                Check(running.State==AgentJobState.Running&&running.RootExited&&running.RootExitCode==0&&process.Stops==0,"root exit and request cancellation preserve a committed job with live children");
                process.Status=new AgentJobProcessExit(true,0,true,true,0);process.Done.TrySetResult(process.Status);
                var done=f.Invoke<Task<AgentJobPage>>("GetAsync",started.Owner,started.Id,"",1024,CancellationToken.None).Result.Snapshot;
                Check(done.State==AgentJobState.Succeeded&&done.CleanupConfirmed,"whole-tree and stream completion produce a successful terminal snapshot");
            }
        }
        static void CancellationBeforeResumeAndRunningSaveFailure()
        {
            using(var f=new Fixture())using(var request=new CancellationTokenSource())
            {
                f.Factory.BeforeReturn=process=>request.Cancel();bool cancelled=false;
                try{f.Invoke<Task<AgentJobSnapshot>>("StartAsync",f.Context("cancel"),new AgentJobSpec("echo fixture",null,f.Temporary.Path,1800),request.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled&&f.Factory.Processes[0].Resumes==0&&f.Factory.Processes[0].Stops==1,"cancellation after suspended preparation cleans the tree without resuming it");
            }
            using(var f=new Fixture())
            {
                f.Factory.BeforeReturn=process=>f.IO.FailCommit=true;bool failed=false;
                try{f.Start("running-save").GetAwaiter().GetResult();}catch(System.IO.IOException){failed=true;}
                Check(failed&&f.Factory.Processes[0].Resumes==1&&f.Factory.Processes[0].Stops==1,"running-save failure after resume cleans up the possibly executed tree");
                f.IO.FailCommit=false;
                bool replayFailed=false;try{f.Start("running-save").GetAwaiter().GetResult();}catch(System.IO.IOException){replayFailed=true;}
                Check(replayFailed&&f.Factory.Processes.Count==1,"failed handoff replay cannot create another process");
            }
        }
        static void ApplicationCapacityAndExitBarrier()
        {
            using(var f=new Fixture())
            {
                f.Start("a","p1").GetAwaiter().GetResult();f.Start("b","p1").GetAwaiter().GetResult();f.Start("c","p2").GetAwaiter().GetResult();f.Start("d","p2").GetAwaiter().GetResult();
                bool full=false;try{f.Start("e","p3").GetAwaiter().GetResult();}catch(InvalidOperationException error){full=error.Message=="job_application_capacity";}
                Check(full&&f.Factory.Processes.Count==4,"application refuses a fifth live job without queuing or spawning");
                var report=f.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).Result;
                Check(report.CleanupConfirmed&&f.Factory.Processes.All(process=>process.Stops==1),"application exit cleans each owned tree exactly once");
                bool exiting=false;try{f.Start("new","p3").GetAwaiter().GetResult();}catch(InvalidOperationException error){exiting=error.Message=="jobs_exiting";}
                Check(exiting,"exit barrier refuses new job starts");
            }
        }
        static void CapacityAndPolicyAreRecheckedAfterApproval()
        {
            using(var f=new Fixture())
            {
                var approval=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);f.Authorization.Barrier=approval;
                var pending=f.Start("waiting");f.Authorization.Barrier=null;
                f.Start("other-a").GetAwaiter().GetResult();f.Start("other-b").GetAwaiter().GetResult();approval.SetResult(true);
                bool refused=false;try{pending.GetAwaiter().GetResult();}catch(InvalidOperationException error){refused=error.Message=="job_project_capacity";}
                Check(refused&&f.Factory.Processes.Count==2,"approval does not reserve stale capacity or bypass the project limit");
            }
            using(var f=new Fixture())
            {
                var basis=f.Context("policy");int checks=0;
                var context=new AgentJobStartContext(basis.Owner,basis.Invocation,basis.Authorization,()=>new AgentJobPolicySnapshot(f.Temporary.Path,"directory",++checks,1800,true,false,false,AgentApprovalMode.Ask,null),()=>new Lease(),()=>{});
                bool refused=false;try{f.Invoke<Task<AgentJobSnapshot>>("StartAsync",context,new AgentJobSpec("echo fixture",null,f.Temporary.Path,1800),CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException error){refused=error.Message=="job_policy_changed";}
                Check(refused&&f.Factory.Processes.Count==0,"a policy version change after authorization prevents process creation");
            }
        }
        static void StopExitRaceProducesOneTerminalNotification()
        {
            using(var f=new Fixture())
            {
                var job=f.Start("race").Result;int terminalEvents=0;
                Action handler=delegate{var list=f.Invoke<IList<AgentJobSnapshot>>("ListForProject",job.Owner.ProjectId);if(list.Any(item=>item.Id==job.Id&&AgentJobStore.IsTerminal(item.State)))Interlocked.Increment(ref terminalEvents);};
                f.ManagerType.GetEvent("Changed").AddEventHandler(f.Manager,handler);
                var stop=Task.Run(()=>f.Invoke<Task<AgentJobSnapshot>>("StopAsync",job.Owner,job.Id,AgentJobStopReason.User,CancellationToken.None).GetAwaiter().GetResult());
                var exit=Task.Run(()=>f.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult());Task.WaitAll(stop,exit);
                Check(terminalEvents==1&&f.Factory.Processes[0].Stops==1,"stop and application-exit race produces one cleanup and one terminal notification");
                f.ManagerType.GetEvent("Changed").RemoveEventHandler(f.Manager,handler);
            }
        }
        static void TimeoutUsesApplicationLifetime()
        {
            using(var f=new Fixture())using(var timedOut=new ManualResetEventSlim(false))
            {
                var job=f.Start("timeout").Result;
                Action handler=delegate{if(f.Invoke<IList<AgentJobSnapshot>>("ListForProject",job.Owner.ProjectId).Any(item=>item.Id==job.Id&&item.State==AgentJobState.TimedOut))timedOut.Set();};
                f.ManagerType.GetEvent("Changed").AddEventHandler(f.Manager,handler);f.Clock.Advance(TimeSpan.FromSeconds(1801));
                Check(timedOut.Wait(3000)&&f.Factory.Processes[0].Stops==1,"application clock expiry stops a committed job without an active request");
                f.ManagerType.GetEvent("Changed").RemoveEventHandler(f.Manager,handler);
            }
        }
        static void StartArgumentsMustMatchApprovedPayload()
        {
            using(var f=new Fixture())
            {
                bool denied=false;try{f.Invoke<Task<AgentJobSnapshot>>("StartAsync",f.Context("mismatch"),new AgentJobSpec("echo unapproved",null,f.Temporary.Path,1800),CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException error){denied=error.Message=="job_invocation_mismatch";}
                Check(denied&&f.Factory.Processes.Count==0&&f.Authorization.Calls==0,"a substituted command cannot execute under another approved payload");
            }
        }
        static void OwnerMutationRequiresConfirmedCleanup()
        {
            using(var f=new Fixture())
            {
                var job=f.Start("owner-mutation").Result;int mutations=0;Func<Task> mutate=()=>{Interlocked.Increment(ref mutations);return Task.FromResult(true);};
                bool busy=false;try{f.Invoke<Task>("RunOwnerMutationAsync",job.Owner.ProjectId,job.Owner.ConversationId,false,mutate,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException error){busy=error.Message=="job_owner_busy";}
                Check(busy&&mutations==0&&f.Factory.Processes[0].Stops==0,"moving an active owner is denied before changing its binding");
                f.Factory.Processes[0].ConfirmCleanup=false;bool unconfirmed=false;
                try{f.Invoke<Task>("RunOwnerMutationAsync",job.Owner.ProjectId,job.Owner.ConversationId,true,mutate,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException error){unconfirmed=error.Message=="job_cleanup_unconfirmed";}
                Check(unconfirmed&&mutations==0,"deleting an owner preserves its binding when process cleanup is unconfirmed");
                f.Factory.Processes[0].ConfirmCleanup=true;
                f.Invoke<Task>("RunOwnerMutationAsync",job.Owner.ProjectId,job.Owner.ConversationId,true,mutate,CancellationToken.None).GetAwaiter().GetResult();
                Check(mutations==1,"owner mutation runs exactly once after confirmed cleanup");
            }
        }
        public static int Main()
        {
            checks=0;int failures=0;foreach(var test in new Action[]{TenConcurrentStartsLaunchOnce,OwnershipAndCleanupFailure,MetadataFailureCannotPreventStopping,RootExitAndCancellationAfterHandoff,CancellationBeforeResumeAndRunningSaveFailure,ApplicationCapacityAndExitBarrier,CapacityAndPolicyAreRecheckedAfterApproval,StopExitRaceProducesOneTerminalNotification,TimeoutUsesApplicationLifetime,StartArgumentsMustMatchApprovedPayload,OwnerMutationRequiresConfirmedCleanup})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobManagerTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
