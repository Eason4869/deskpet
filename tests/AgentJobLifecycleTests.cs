using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentJobLifecycleTests
    {
        static int checks;
        static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        sealed class Factory : IAgentJobProcessFactory
        {
            internal bool AllowStarts;internal Process Last;
            public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)
            {if(!AllowStarts)throw new Exception("Recovery must never start a process");Last=new Process();return Task.FromResult((IAgentJobProcess)Last);}
        }
        sealed class Process : IAgentJobProcess
        {
            internal bool ConfirmCleanup=true;
            volatile AgentJobProcessExit status=new AgentJobProcessExit(false,null,false,false,1);
            readonly TaskCompletionSource<AgentJobProcessExit> done=new TaskCompletionSource<AgentJobProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
            public AgentJobProcessExit Status {get{return status;}}
            public Task<AgentJobProcessExit> Completion {get{return done.Task;}}
            public void Resume(){}
            public Task<AgentJobCleanupResult> StopAsync(CancellationToken token)
            {if(ConfirmCleanup){status=new AgentJobProcessExit(true,null,true,true,0);done.TrySetResult(status);}return Task.FromResult(new AgentJobCleanupResult(ConfirmCleanup,ConfirmCleanup?null:"held"));}
            public void Dispose(){}
        }
        sealed class Authorization : IAgentAuthorizationService
        {
            public Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken token)
            {return Task.FromResult(new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow});}
        }
        sealed class Speech : IReplySpeech
        {public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
        sealed class Model : IConversationApi,IAgentConversationApi
        {
            internal bool UsedJobs;internal int Calls;
            public async Task<string> ChatAgentAsync(ConnectionSettings settings,System.Collections.Generic.IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken token)
            {
                Calls++;
                string result=await workspace.ExecuteAsync("start_job","{\"command\":\"echo fixture\"}","provider-start",token).ConfigureAwait(false);
                UsedJobs=result.Contains("\"ok\":true");if(!UsedJobs)throw new Exception("Provider did not expose the task service: "+result);return "started";
            }
            public Task<string> ChatAsync(ConnectionSettings settings,System.Collections.Generic.IList<ChatMessage> messages,CancellationToken token){throw new Exception("Unexpected plain model call");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] bytes,CancellationToken token){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token){throw new NotSupportedException();}
            public void Dispose(){}
        }
        sealed class Lease : IDisposable {public void Dispose(){}}
        static Task<AgentJobSnapshot> Start(Service service,string directory)
        {
            service.Initialize().GetAwaiter().GetResult();service.ProcessFactory.AllowStarts=true;
            var owner=new AgentJobOwner("project","conversation","primary");
            var invocation=new AgentToolInvocation("start_job",new System.Collections.Generic.Dictionary<string,object>{{"command","echo fixture"},{"title","echo fixture"},{"working_directory",directory},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId="project",AgentId="primary",TaskId="persistent-task",CallId=Guid.NewGuid().ToString("N")});
            var context=new AgentJobStartContext(owner,invocation,new Authorization(),()=>new AgentJobPolicySnapshot(directory,"directory",1,1800,true,false,false,AgentApprovalMode.Ask,null),()=>new Lease(),()=>{});
            return (Task<AgentJobSnapshot>)typeof(AgentJobManager).GetMethod("StartAsync",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(service.Manager,new object[]{context,new AgentJobSpec("echo fixture",null,directory,1800),CancellationToken.None});
        }
        sealed class Service : IDisposable
        {
            readonly object instance;readonly Type type;
            internal int Factories;
            internal bool HoldFactory;
            internal Factory ProcessFactory;
            internal readonly ManualResetEventSlim FactoryEntered=new ManualResetEventSlim(false),FactoryRelease=new ManualResetEventSlim(false);
            internal Service(string directory,IAgentJobClock clock)
            {
                type=typeof(AgentJobManager).Assembly.GetType("Dafeiyu.AgentJobApplicationService");
                if(type==null)throw new Exception("Application-owned background job service is missing");
                Func<IAgentJobClock,IAgentJobProcessFactory> create=unused=>{Interlocked.Increment(ref Factories);FactoryEntered.Set();if(HoldFactory)FactoryRelease.Wait();return ProcessFactory=new Factory();};
                instance=Activator.CreateInstance(type,directory,create,clock);
            }
            internal T Invoke<T>(string method,params object[] args)
            {try{return (T)type.GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(instance,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
            internal AgentJobManager Manager {get{return (AgentJobManager)type.GetProperty("Manager").GetValue(instance,null);}}
            internal object Instance {get{return instance;}}
            internal void InjectIO(AgentJobStorageIO io)
            {
                Initialize().GetAwaiter().GetResult();
                var store=type.GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);
                typeof(AgentJobStore).GetField("io",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(store,io);
            }
            internal Task Mutate(string[] projects,Func<Task> mutation,bool stopActive=false,string conversationId=null)
            {
                var guardType=type.Assembly.GetType("Dafeiyu.AgentJobCatalogGuard");
                if(guardType==null)throw new Exception("Application catalog guard is missing");
                var guard=Activator.CreateInstance(guardType,instance);
                try{return (Task)guardType.GetMethod("RunAsync").Invoke(guard,new object[]{projects,conversationId,stopActive,mutation,CancellationToken.None});}
                catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
            }
            internal Task<bool> Initialize(){return Invoke<Task<bool>>("InitializeAsync");}
            public void Dispose()
            {
                var report=Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult();
                if(!report.CleanupConfirmed)throw new Exception("Fixture cannot release unconfirmed jobs");
                ((IDisposable)instance).Dispose();
                FactoryEntered.Dispose();FactoryRelease.Dispose();
            }
        }
        static void ConcurrentInitializationAndRecovery()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string id=new string('a',32);
                using(var seed=new AgentJobTestSupport.Store(root.Path,clock))seed.Save(AgentJobTestSupport.Snapshot(root.Path,id,AgentJobState.Running,clock));
                using(var service=new Service(root.Path,clock))
                {
                    var callers=Enumerable.Range(0,8).Select(unused=>Task.Run(()=>service.Initialize())).ToArray();Task.WaitAll(callers);
                    Check(callers.All(task=>task.Result)&&service.Factories==1,"concurrent application initialization acquires one lease and constructs one process factory");
                    var manager=service.Manager;
                    var list=(System.Collections.Generic.IList<AgentJobSnapshot>)typeof(AgentJobManager).GetMethod("ListForProject",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,new object[]{"project"});
                    Check(list.Count==1&&list[0].Id==id&&list[0].State==AgentJobState.Interrupted,"application recovers a prior running task as Interrupted without executing it");
                    service.Initialize().GetAwaiter().GetResult();Check(Object.ReferenceEquals(manager,service.Manager),"reopening a panel retains the application manager");
                }
                using(var store=new AgentJobTestSupport.Store(root.Path,clock))Check(store.Load().Count==1,"confirmed application exit releases the data-directory lease");
            }
        }
        static void SecondHostCannotWriteBindings()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();
                using(var first=new Service(root.Path,clock))
                {
                    Check(first.Initialize().GetAwaiter().GetResult(),"first application obtains its task-store lease");
                    using(var second=new Service(root.Path,clock))
                    {
                        Check(!second.Initialize().GetAwaiter().GetResult()&&second.Manager==null&&second.Factories==0,"second application remains unavailable without constructing a native factory");
                        bool denied=false;try{second.Invoke<Task>("EnsureWritableAsync",CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){denied=true;}
                        Check(denied,"a host without the lease cannot mutate persistent work bindings");
                    }
                }
            }
        }
        static void ExitBlocksFutureInitializationAndWrites()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            {
                var report=service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult();
                Check(report.CleanupConfirmed&&service.Factories==0,"exit before first use does not initialize a process factory");
                Check(!service.Initialize().GetAwaiter().GetResult()&&service.Manager==null,"an exiting application never admits a new manager");
                bool denied=false;try{service.Invoke<Task>("EnsureWritableAsync",CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException error){denied=error.Message=="jobs_exiting";}
                Check(denied,"exit barrier rejects new binding writes");
            }
        }
        static void ExitRetainsAnInitializingLeaseUntilRetry()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            {
                service.HoldFactory=true;var initialize=service.Initialize();
                try
                {
                    Check(service.FactoryEntered.Wait(3000),"initialization fixture reaches the owned-lease boundary");
                    using(var deadline=new CancellationTokenSource(100))
                    {
                        var report=service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",deadline.Token).GetAwaiter().GetResult();
                        Check(!report.CleanupConfirmed,"cancelled exit retains a still-initializing lease for retry");
                    }
                    bool denied=false;try{service.Invoke<object>("Dispose");}catch(InvalidOperationException error){denied=error.Message=="job_cleanup_unconfirmed";}
                    Check(denied,"disposing an unconfirmed service cannot release its writer lease");
                }
                finally{service.FactoryRelease.Set();initialize.GetAwaiter().GetResult();}
                var retry=service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult();
                Check(retry.CleanupConfirmed,"exit retry joins late initialization and confirms cleanup");
            }
        }
        static void CatalogGateOrdersProjectsAndReleasesAfterFailure()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            using(var entered=new ManualResetEventSlim(false))using(var secondEntered=new ManualResetEventSlim(false))
            {
                var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var first=service.Mutate(new[]{"project-b","project-a"},async delegate{entered.Set();await release.Task;});
                Task second=null;
                try
                {
                    Check(entered.Wait(3000),"multi-project catalog mutation enters its protected body");
                    second=service.Mutate(new[]{"project-a","project-b","project-a"},delegate{secondEntered.Set();return Task.FromResult(0);});
                    Check(!secondEntered.Wait(100),"overlapping catalog mutations share ordered project gates");
                }
                finally{release.TrySetResult(true);first.GetAwaiter().GetResult();if(second!=null)second.GetAwaiter().GetResult();}
                Check(secondEntered.IsSet,"opposite project order and duplicate IDs complete without deadlock");
                bool failed=false;try{service.Mutate(new[]{"project-a"},delegate{throw new IOException("fixture");}).GetAwaiter().GetResult();}catch(IOException){failed=true;}
                int writes=0;service.Mutate(new[]{"project-a"},delegate{writes++;return Task.FromResult(0);}).GetAwaiter().GetResult();
                Check(failed&&writes==1,"a failed catalog mutation releases its admission gate");
            }
        }
        static void ExitJoinsCatalogMutationAndLeaseFailurePreventsWrites()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            using(var entered=new ManualResetEventSlim(false))
            {
                var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var mutation=service.Mutate(new[]{"project"},async delegate{entered.Set();await release.Task;});
                Task<AgentJobExitReport> exit=null;
                try
                {
                    Check(entered.Wait(3000),"exit fixture enters a catalog write");
                    using(var second=new Service(root.Path,new AgentJobTestSupport.Clock()))
                    {
                        bool denied=false,wrote=false;
                        try{second.Mutate(new[]{"project"},delegate{wrote=true;return Task.FromResult(0);}).GetAwaiter().GetResult();}catch(IOException){denied=true;}
                        Check(denied&&!wrote,"lease failure rejects the mutation before changing bindings");
                    }
                    exit=service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None);
                    Check(!exit.Wait(100),"application exit waits for an admitted catalog write before releasing the lease");
                }
                finally{release.TrySetResult(true);mutation.GetAwaiter().GetResult();}
                Check(exit.GetAwaiter().GetResult().CleanupConfirmed,"exit confirms both catalog drain and process cleanup");
                bool refused=false;try{service.Mutate(new[]{"project"},delegate{throw new Exception("must not enter");}).GetAwaiter().GetResult();}catch(InvalidOperationException error){refused=error.Message=="jobs_exiting";}
                Check(refused,"exiting service rejects subsequent catalog mutations");
            }
        }
        static void FailedCleanupKeepsLeaseAndCatalogUnchanged()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            {
                Start(service,root.Path).GetAwaiter().GetResult();bool wrote=false,refused=false;
                Func<Task> write=delegate{wrote=true;return Task.FromResult(0);};
                try{service.Mutate(new[]{"project"},write).GetAwaiter().GetResult();}catch(InvalidOperationException error){refused=error.Message=="job_owner_busy";}
                Check(refused&&!wrote,"root and ownership changes reject active jobs even without an open workspace");
                service.ProcessFactory.Last.ConfirmCleanup=false;refused=false;
                try{service.Mutate(new[]{"project"},write,true,"conversation").GetAwaiter().GetResult();}catch(InvalidOperationException error){refused=error.Message=="job_cleanup_unconfirmed";}
                Check(refused&&!wrote,"deletion never changes bindings when owned-tree cleanup is unconfirmed");
                try
                {
                    var report=service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult();
                    Check(!report.CleanupConfirmed&&report.Jobs.Count==1,"application exit reports an unconfirmed owned tree");
                    using(var second=new Service(root.Path,new AgentJobTestSupport.Clock()))Check(!second.Initialize().GetAwaiter().GetResult(),"an unconfirmed exit retains its lease against another host");
                }
                finally{service.ProcessFactory.Last.ConfirmCleanup=true;}
                Check(service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult().CleanupConfirmed,"retry confirms cleanup before releasing the host");
            }
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            {
                Start(service,root.Path).GetAwaiter().GetResult();bool wrote=false;
                service.Mutate(new[]{"project"},delegate{wrote=true;return Task.FromResult(0);},true,"conversation").GetAwaiter().GetResult();
                Check(wrote&&service.ProcessFactory.Last.Status.TreeExited,"confirmed cleanup precedes the catalog mutation body");
            }
        }
        static void PersistentWorkspaceReceivesTheApplicationManager()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            using(var workspaces=new ConversationWorkspaceManager(root.Path,"e-bao",()=>new ApiClient(),()=>new Speech(),()=>null,()=>new PetSettings(),()=>true))
            {
                try
                {
                var property=typeof(ConversationWorkspaceManager).GetProperty("JobServices");
                Check(property!=null,"workspace manager exposes application job services");property.SetValue(workspaces,service.Instance,null);
                var resolver=typeof(ConversationWorkspace).GetMethod("GetJobManagerAsync",BindingFlags.Instance|BindingFlags.NonPublic);
                Check(resolver!=null,"workspace resolves a task manager at the request boundary");
                var project=new ProjectDefinition{Id="persistent",Name="fixture",Workspace=root.Path};
                var definition=new ConversationDefinition{Id="work-owner",Name="fixture",Mode="work"};
                var workspace=workspaces.CreateWorkspace(project,definition);
                Func<ConversationWorkspace,AgentJobManager> resolve=owner=>((Task<AgentJobManager>)resolver.Invoke(owner,new object[]{CancellationToken.None})).GetAwaiter().GetResult();
                Check(Object.ReferenceEquals(resolve(workspace),service.Manager),"persistent work sessions resolve the single recovered manager");
                var draft=typeof(ConversationWorkspace).GetField("IsDraft",BindingFlags.Instance|BindingFlags.NonPublic);draft.SetValue(workspace,true);
                Check(resolve(workspace)==null,"unsaved drafts never gain background job tools");draft.SetValue(workspace,false);
                var chat=workspaces.CreateWorkspace(project,new ConversationDefinition{Id="chat-owner",Mode="chat"});
                Check(resolve(chat)==null,"ordinary conversations never gain background job tools");
                var unassigned=workspaces.CreateWorkspace(new ProjectDefinition{Id=ProjectCatalogStore.UnassignedProjectId,Workspace=root.Path},new ConversationDefinition{Id="unassigned-owner",Mode="work"});
                Check(resolve(unassigned)==null,"unassigned work sessions never gain background job tools");
                workspaces.CloseWorkspaceAsync(workspace.Id).GetAwaiter().GetResult();
                var reopened=workspaces.CreateWorkspace(project,definition);
                Check(Object.ReferenceEquals(resolve(reopened),service.Manager),"closing and reopening a conversation preserves application process ownership");
                }
                finally{foreach(var owned in workspaces.OpenWorkspaces.ToArray())workspaces.CloseWorkspaceAsync(owned.Id).GetAwaiter().GetResult();}
            }
        }
        static void RealConversationUsesTheInjectedApplicationService()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.WriteLine("SKIP Windows conversation tool boundary; shared lifecycle checks still run");return;}
            foreach(bool useProvider in new[]{true,false})
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            {
                service.Initialize().GetAwaiter().GetResult();service.ProcessFactory.AllowStarts=true;
                var model=new Model();var settings=new PetSettings{AgentEnabled=true,AgentWorkspace=root.Path,AgentApprovalMode=AgentApprovalMode.Full,AgentSandboxEnabled=false,AgentTerminalPolicy=AgentTerminalPolicy.ConfirmEach,ReadReplies=false,SaveChatHistory=true};
                settings.Text.BaseUrl="https://fixture.invalid/v1";settings.Text.ApiKey="fixture-only";settings.Text.Model="fixture";settings.Text.ToolSupport=CapabilitySupport.Supported;
                using(var workspaces=new ConversationWorkspaceManager(root.Path,"e-bao",()=>model,()=>new Speech(),()=>null,()=>settings,()=>true))
                {
                    if(useProvider)typeof(ConversationWorkspaceManager).GetProperty("JobServices").SetValue(workspaces,service.Instance,null);
                    var project=new ProjectDefinition{Id="provider-project",Name="fixture",Workspace=root.Path,ApprovalMode=AgentApprovalMode.Full,SandboxEnabled=false,TerminalPolicy=AgentTerminalPolicy.ConfirmEach};
                    var workspace=workspaces.CreateWorkspace(project,new ConversationDefinition{Id="provider-conversation",Mode="work"});
                    if(!useProvider)typeof(ConversationWorkspace).GetMethod("ConfigureJobs",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(workspace,new object[]{service.Manager});
                    try
                    {
                        workspace.Session.SendTextAsync("start fixture",workspace.EffectiveSettings(),"fixture").GetAwaiter().GetResult();
                        Check(model.UsedJobs&&model.Calls==1&&service.ProcessFactory.Last!=null,useProvider?"real conversation requests use the application manager without manual ConfigureJobs":"explicitly configured legacy sessions retain their task manager");
                    }
                    finally{workspaces.CloseWorkspaceAsync(workspace.Id).GetAwaiter().GetResult();}
                    Check(!service.ProcessFactory.Last.Status.TreeExited,"closing the real conversation leaves its handed-off task running");
                }
            }
        }
        static void StalledStorageKeepsTheExitAsyncAndRetainsTheLease()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO())
            using(var service=new Service(root.Path,new AgentJobTestSupport.Clock()))
            {
                service.InjectIO(io);Start(service,root.Path).GetAwaiter().GetResult();io.HoldMetadataCommit=true;
                try
                {
                    var elapsed=System.Diagnostics.Stopwatch.StartNew();
                    var exit=service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None);
                    Check(elapsed.ElapsedMilliseconds<500,"stalled storage never synchronously blocks the application exit caller");
                    var report=exit.GetAwaiter().GetResult();
                    Check(!report.CleanupConfirmed&&elapsed.ElapsedMilliseconds<11000,"exit reports unconfirmed storage drain within the application deadline");
                    using(var second=new Service(root.Path,new AgentJobTestSupport.Clock()))Check(!second.Initialize().GetAwaiter().GetResult(),"stalled metadata retains the task-store lease after native cleanup");
                }
                finally{io.HoldMetadataCommit=false;io.CommitRelease.Set();}
                Check(service.Invoke<Task<AgentJobExitReport>>("StopAllForExitAsync",CancellationToken.None).GetAwaiter().GetResult().CleanupConfirmed,"retry joins completed storage cleanup before releasing the application");
            }
        }
        public static int Main()
        {
            checks=0;int failures=0;
            foreach(var test in new Action[]{ConcurrentInitializationAndRecovery,SecondHostCannotWriteBindings,ExitBlocksFutureInitializationAndWrites,ExitRetainsAnInitializingLeaseUntilRetry,CatalogGateOrdersProjectsAndReleasesAfterFailure,ExitJoinsCatalogMutationAndLeaseFailurePreventsWrites,FailedCleanupKeepsLeaseAndCatalogUnchanged,PersistentWorkspaceReceivesTheApplicationManager,RealConversationUsesTheInjectedApplicationService,StalledStorageKeepsTheExitAsyncAndRetainsTheLease})
                try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobLifecycleTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
