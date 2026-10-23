using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentJobIntegrationTests
    {
        static int checks;
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        static Dictionary<string,object> Json(string value){return (Dictionary<string,object>)new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(value);}
        sealed class Process : IAgentJobProcess
        {
            internal int Resumes,Stops;internal Action<AgentJobOutputChunk> Output;
            public AgentJobProcessExit Status {get;internal set;}
            readonly TaskCompletionSource<AgentJobProcessExit> done=new TaskCompletionSource<AgentJobProcessExit>();
            internal Process(){Status=new AgentJobProcessExit(false,null,false,false,1);}
            public Task<AgentJobProcessExit> Completion {get{return done.Task;}}
            public void Resume(){Resumes++;}
            public Task<AgentJobCleanupResult> StopAsync(CancellationToken token){Stops++;Status=new AgentJobProcessExit(true,null,true,true,0);done.TrySetResult(Status);return Task.FromResult(new AgentJobCleanupResult(true,null));}
            public void Dispose(){}
        }
        sealed class Factory : IAgentJobProcessFactory
        {
            internal readonly List<Process> Processes=new List<Process>();
            public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)
            {var process=new Process{Output=output};Processes.Add(process);return Task.FromResult<IAgentJobProcess>(process);}
        }
        sealed class Approval : IAgentApprovalProvider
        {
            internal int Calls;internal AgentApprovalRequest Last;internal Action BeforeAllow;
            public Task<bool> ApproveAsync(AgentApprovalRequest request){Interlocked.Increment(ref Calls);Last=request;if(BeforeAllow!=null)BeforeAllow();return Task.FromResult(true);}
        }
        sealed class Fixture : IDisposable
        {
            internal readonly AgentJobTestSupport.TemporaryDirectory Root=new AgentJobTestSupport.TemporaryDirectory();
            internal readonly AgentJobTestSupport.Clock Clock=new AgentJobTestSupport.Clock();
            internal readonly Factory Factory=new Factory();
            internal readonly AgentJobStore Store;internal readonly AgentJobManager Manager;
            internal readonly AgentJobOwner Owner=new AgentJobOwner("persistent-project","persistent-conversation","primary");
            internal Fixture(){Store=new AgentJobStore(Path.Combine(Root.Path,"data"),Clock);Manager=new AgentJobManager(Store,Factory,Clock);Manager.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();}
            internal AgentWorkspace Workspace(bool bind=true,AgentJobOwner owner=null,Approval approval=null,AgentApprovalMode mode=AgentApprovalMode.Full)
            {
                owner=owner??Owner;var workspace=new AgentWorkspace(Root.Path);workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,approval,null,"persisted-task",new AgentSessionModel(Guid.NewGuid().ToString("N"),owner.ProjectId,owner.ConversationId));
                var settings=new PetSettings{AgentWorkspace=Root.Path,AgentApprovalMode=mode,AgentSandboxEnabled=mode==AgentApprovalMode.Auto};
                workspace.ConfigureAccessPolicy(settings);workspace.ConfigureTerminal(AgentTerminalPolicy.ConfirmEach);workspace.ConfigureDesktopSession();workspace.BeginTask(new AgentTaskState{Id="persisted-task",Workspace=Root.Path});
                if(bind)
                {
                    var configure=typeof(AgentWorkspace).GetMethod("ConfigureJobs",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(AgentJobManager),typeof(AgentJobOwner),typeof(int)},null);
                    if(configure==null){workspace.Dispose();throw new Exception("Trusted ConfigureJobs integration is missing");}configure.Invoke(workspace,new object[]{Manager,owner,1800});
                }
                return workspace;
            }
            public void Dispose(){Manager.StopAllForExitAsync(CancellationToken.None).GetAwaiter().GetResult();Store.Dispose();Root.Dispose();}
        }
        static Dictionary<string,object> Execute(AgentWorkspace workspace,string name,string args,string call)
        {return Json(workspace.ExecuteAsync(name,args,call,CancellationToken.None).GetAwaiter().GetResult());}
        static string Error(Dictionary<string,object> value){return (string)((Dictionary<string,object>)value["error"])["code"];}
        static void UnboundAndMalformedCallsReturnProtocolErrors()
        {
            using(var f=new Fixture())using(var workspace=f.Workspace(false))
            {
                Check(!workspace.AvailableToolNames.Split(',').Any(name=>name.Trim()=="start_job"),"ordinary workspace does not expose background job tools");
                Check(Error(Execute(workspace,"start_job","{\"command\":\"echo x\"}","unbound"))=="workspace_required"&&f.Factory.Processes.Count==0,"forged direct start in an unbound workspace returns complete JSON and launches nothing");
            }
            using(var f=new Fixture())using(var workspace=f.Workspace())
            {
                Check(Error(Execute(workspace,"start_job","{broken","malformed"))=="invalid_arguments","malformed job JSON stays inside the protocol error envelope");
                Check(Error(Execute(workspace,"start_job","{\"command\":\"echo x\",\"timeout_seconds\":true}","boolean"))=="invalid_arguments","direct tool route rejects a boolean timeout without numeric conversion");
            }
        }
        static void CommittedJobSurvivesWorkspaceDisposeAndStableOwnerReplay()
        {
            using(var f=new Fixture())
            {
                string id;
                using(var workspace=f.Workspace())
                {
                    Check(workspace.AvailableToolNames.Contains("start_job")&&workspace.AvailableToolNames.Contains("get_job"),"trusted persistent binding exposes the background job catalog");
                    var response=Execute(workspace,"start_job","{\"command\":\"echo fixture\"}","stable-start");id=(string)((Dictionary<string,object>)response["job"])["id"];
                    Check((bool)response["ok"]&&!(bool)response["replayed"]&&f.Factory.Processes.Count==1,"first tool call hands one process to the application manager");
                }
                Check(f.Factory.Processes[0].Stops==0,"disposing the first tool workspace does not stop its committed job");
                using(var workspace=f.Workspace())
                {
                    var replay=Execute(workspace,"start_job","{\"command\":\"echo fixture\"}","stable-start");
                    Check((bool)replay["ok"]&&(bool)replay["replayed"]&&(string)((Dictionary<string,object>)replay["job"])["id"]==id&&f.Factory.Processes.Count==1,"recreated workspace with a new transient session replays the persisted owner/call instead of rerunning");
                    workspace.PlanMode=true;
                    Check(workspace.AvailableToolNames.Contains("get_job")&&!workspace.AvailableToolNames.Contains("start_job"),"Plan schema retains read-only get but removes start");
                    Check((bool)Execute(workspace,"get_job","{\"id\":\""+id+"\"}","plan-read")["ok"],"original owner reads its job after switching to Plan");
                    Check(Error(Execute(workspace,"start_job","{\"command\":\"echo denied\"}","plan-start"))=="permission_denied"&&f.Factory.Processes.Count==1,"direct Plan start is denied without a side effect");
                    workspace.PlanMode=false;workspace.ConfigureTerminal(AgentTerminalPolicy.Disabled);
                    Check(workspace.AvailableToolNames.Contains("get_job")&&!workspace.AvailableToolNames.Contains("start_job")&&Error(Execute(workspace,"start_job","{\"command\":\"echo denied\"}","disabled-start"))=="permission_denied","disabled terminal removes start schema and blocks forged direct starts");
                    workspace.ConfigureAgentProfile(new AgentProfile{Id="explore",Mode="subagent",ReadOnly=true});
                    Check(!workspace.AvailableToolNames.Contains("get_job")&&Error(Execute(workspace,"get_job","{\"id\":\""+id+"\"}","worker-read"))=="permission_denied","a child worker cannot inherit the primary conversation's job capability");
                }
            }
        }
        static void LargeLivePageAndForeignOwner()
        {
            using(var f=new Fixture())using(var workspace=f.Workspace())
            {
                var response=Execute(workspace,"start_job","{\"command\":\"echo fixture\"}","logs-start");string id=(string)((Dictionary<string,object>)response["job"])["id"];
                byte[] bytes=Encoding.UTF8.GetBytes(new string('\n',8192));for(int part=0;part<8;part++){f.Factory.Processes[0].Output(new AgentJobOutputChunk("stdout",bytes,bytes.Length,f.Clock.UtcNow));f.Store.OpenLog(id).FlushAsync(CancellationToken.None).GetAwaiter().GetResult();}
                var page=Execute(workspace,"get_job","{\"id\":\""+id+"\",\"limit_bytes\":65536}","large-read");
                Check((bool)page["ok"]&&((object[])page["records"]).Sum(record=>((string)((Dictionary<string,object>)record)["text"]).Length)==65536,"workspace returns a complete untruncated 64-KiB escaped log page larger than the old journal ceiling");
                f.Factory.Processes[0].Status=new AgentJobProcessExit(true,7,false,false,1);
                var fresh=Execute(workspace,"get_job","{\"id\":\""+id+"\"}","fresh-read");Check((bool)((Dictionary<string,object>)fresh["job"])["root_exited"],"a new read call observes live root status rather than a cached previous page");
                using(var foreign=f.Workspace(true,new AgentJobOwner("other-project","other-conversation","primary")))
                {
                    var hidden=Execute(foreign,"get_job","{\"id\":\""+id+"\"}","foreign-read");var missing=Execute(foreign,"get_job","{\"id\":\""+new string('0',32)+"\"}","missing-read");
                    Check(Error(hidden)=="job_not_found"&&new JavaScriptSerializer().Serialize(hidden)==new JavaScriptSerializer().Serialize(missing),"foreign and unknown jobs return indistinguishable protocol errors");
                }
                var stop=Execute(workspace,"stop_job","{\"id\":\""+id+"\"}","user-stop");Check((bool)stop["ok"]&&(string)((Dictionary<string,object>)stop["job"])["stop_reason"]=="user"&&f.Factory.Processes[0].Stops==1,"tool stop fixes its reason to user and cleans only the owned process");
            }
        }
        static void AutoShellApprovalIsExactAndOnce()
        {
            using(var f=new Fixture())
            {
                var approval=new Approval();using(var workspace=f.Workspace(true,null,approval,AgentApprovalMode.Auto))
                {
                    var calls=Enumerable.Range(0,10).Select(index=>workspace.ExecuteAsync("start_job","{\"command\":\"echo fixture\"}","one-approved-call",CancellationToken.None)).ToArray();Task.WaitAll(calls);
                    Check(approval.Calls==1&&f.Factory.Processes.Count==1&&calls.All(call=>(bool)Json(call.Result)["ok"]),"ten direct Auto sandbox starts obtain one real manual approval and one process");
                    var shown=Json(approval.Last.Arguments);
                    Check(approval.Last.Action=="shell"&&shown.Count==4&&(string)shown["working_directory"]==f.Root.Path&&(int)shown["timeout_seconds"]==1800&&approval.Last.ProjectId==f.Owner.ProjectId,"manual approval contains the exact normalized command, directory, timeout and trusted project");
                    var conflict=Execute(workspace,"start_job","{\"command\":\"echo changed\"}","one-approved-call");
                    Check(Error(conflict)=="idempotency_conflict"&&approval.Calls==1&&f.Factory.Processes.Count==1,"a changed payload cannot reuse an approved tool call");
                }
            }
        }
        static void OversizedEscapedStartAndStopStayValidInTheJournal()
        {
            using(var f=new Fixture())
            {
                string command="echo fixture; #"+new string('"',15000),args=new JavaScriptSerializer().Serialize(new{command,title="large-command"}),id;
                using(var workspace=f.Workspace())
                {
                    var start=Execute(workspace,"start_job",args,"large-command");if(!(bool)start["ok"])throw new Exception("Large start failed: "+new JavaScriptSerializer().Serialize(start));id=(string)((Dictionary<string,object>)start["job"])["id"];
                    Check(new JavaScriptSerializer().Serialize(start).Length>24000&&((string)((Dictionary<string,object>)start["job"])["command"]).Length==command.Length,"large escaped start retains the complete command in valid JSON");
                    Check((bool)Execute(workspace,"stop_job","{\"id\":\""+id+"\"}","large-stop")["ok"],"large escaped stop snapshot remains valid JSON");
                }
                using(var workspace=f.Workspace())
                {
                    var replay=Execute(workspace,"start_job",args,"large-command");var stop=Execute(workspace,"stop_job","{\"id\":\""+id+"\"}","large-stop");
                    Check((bool)replay["replayed"]&&(bool)stop["replayed"]&&((string)((Dictionary<string,object>)stop["job"])["command"])==command&&f.Factory.Processes.Count==1,"durable start and stop replay bypass the old 24000-character journal truncation");
                }
            }
        }
        static void RetiredMetadataCannotBeResurrectedByStartReplay()
        {
            using(var f=new Fixture())
            {
                string id;
                using(var workspace=f.Workspace())
                {
                    var start=Execute(workspace,"start_job","{\"command\":\"echo fixture\"}","retired-call");id=(string)((Dictionary<string,object>)start["job"])["id"];
                    Execute(workspace,"stop_job","{\"id\":\""+id+"\"}","retire-stop");f.Clock.Advance(TimeSpan.FromDays(8));
                    Execute(workspace,"start_job","{\"command\":\"echo fresh\"}","fresh-call");
                }
                Check(!File.Exists(f.Store.MetadataPath(id))&&f.Manager.ListForProject(f.Owner.ProjectId).Count==1,"retiring expired metadata also retires the application manager's old terminal snapshot");
                using(var workspace=f.Workspace())
                {
                    var replay=Execute(workspace,"start_job","{\"command\":\"echo fixture\"}","retired-call");
                    Check(Error(replay)=="execution_reconciliation_required"&&f.Factory.Processes.Count==2,"permanent host journal blocks a retired start call from creating or resurrecting a job");
                }
            }
        }
        static void DirectoryReplacementDuringApprovalCannotUseTheOldConsent()
        {
            using(var f=new Fixture())
            {
                string child=Path.Combine(f.Root.Path,"approved-directory"),old=Path.Combine(f.Root.Path,"old-directory");Directory.CreateDirectory(child);
                var approval=new Approval{BeforeAllow=()=>{Directory.Move(child,old);Directory.CreateDirectory(child);}};
                using(var workspace=f.Workspace(true,null,approval,AgentApprovalMode.Auto))
                {
                    var response=Execute(workspace,"start_job",new JavaScriptSerializer().Serialize(new{command="echo fixture",working_directory=child}),"directory-replaced");
                    Check(!(bool)response["ok"]&&Error(response)=="directory_changed"&&f.Factory.Processes.Count==0,"replacing the actual approved working directory cannot reuse consent even inside the same project root");
                }
            }
        }
        static void ReplacingPermissionsAtTheSameVersionInvalidatesApproval()
        {
            using(var f=new Fixture())
            {
                var approval=new Approval();using(var workspace=f.Workspace(true,null,approval,AgentApprovalMode.Auto))
                {
                    approval.BeforeAllow=()=>workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,approval,null,"persisted-task",new AgentSessionModel("changed-runtime",f.Owner.ProjectId,f.Owner.ConversationId));
                    var response=Execute(workspace,"start_job","{\"command\":\"echo fixture\"}","permission-replaced");
                    Check(!(bool)response["ok"]&&Error(response)=="policy_changed"&&approval.Calls==1&&f.Factory.Processes.Count==0,"replacing a permission runtime at the same numeric version cannot reuse a stale approval");
                }
            }
        }
        public static int Main()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return 0;
            checks=0;int failures=0;foreach(var test in new Action[]{UnboundAndMalformedCallsReturnProtocolErrors,CommittedJobSurvivesWorkspaceDisposeAndStableOwnerReplay,LargeLivePageAndForeignOwner,AutoShellApprovalIsExactAndOnce,OversizedEscapedStartAndStopStayValidInTheJournal,RetiredMetadataCannotBeResurrectedByStartReplay,DirectoryReplacementDuringApprovalCannotUseTheOldConsent,ReplacingPermissionsAtTheSameVersionInvalidatesApproval})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobIntegrationTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
