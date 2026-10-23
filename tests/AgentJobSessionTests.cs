using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    // Scripted model + real conversation + real Windows process. No live model API.
    public static class AgentJobSessionTests
    {
        static int checks;
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        static Dictionary<string,object> Json(string value){return (Dictionary<string,object>)new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(value);}
        sealed class Clock : IAgentJobClock
        {readonly Stopwatch elapsed=Stopwatch.StartNew();public DateTime UtcNow {get{return DateTime.UtcNow;}}public long ElapsedMilliseconds {get{return elapsed.ElapsedMilliseconds;}}}
        sealed class Speech : IReplySpeech
        {public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int value){}public void Stop(){}public void Dispose(){}}
        sealed class Model : IConversationApi,IAgentConversationApi
        {
            internal string Command,JobId;internal int Calls;internal long StartMilliseconds;
            internal bool LoseFirstResponse,StopNext;internal string LastResult;internal AgentWorkspace FirstWorkspace;
            public async Task<string> ChatAgentAsync(ConnectionSettings settings,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken token)
            {
                Calls++;string name,args,call;
                if(Calls==1||LoseFirstResponse&&Calls==2)
                {name="start_job";args=new JavaScriptSerializer().Serialize(new{command=Command,title="session-fixture"});call="persisted-start-call";if(FirstWorkspace==null)FirstWorkspace=workspace;}
                else{name=StopNext?"stop_job":"get_job";args=new JavaScriptSerializer().Serialize(new{id=JobId});call="fresh-"+name+"-"+Calls;}
                var elapsed=Stopwatch.StartNew();LastResult=await workspace.ExecuteAsync(name,args,call,token).ConfigureAwait(false);if(Calls==1)StartMilliseconds=elapsed.ElapsedMilliseconds;
                var response=Json(LastResult);if(!(bool)response["ok"])throw new Exception("scripted tool failed: "+LastResult);
                if(name=="start_job")JobId=(string)((Dictionary<string,object>)response["job"])["id"];
                if(LoseFirstResponse&&Calls==1)throw new IOException("Injected lost model response after committed tool result");
                return "scripted reply "+Calls;
            }
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){throw new Exception("Unexpected plain/model call");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] bytes,CancellationToken token){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token){throw new NotSupportedException();}
            public void Dispose(){}
        }
        static void Configure(ConversationWorkspace workspace,AgentJobManager manager)
        {
            var method=typeof(ConversationWorkspace).GetMethod("ConfigureJobs",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(AgentJobManager)},null);
            if(method==null)throw new Exception("Persistent conversation ConfigureJobs is missing");method.Invoke(workspace,new object[]{manager});
        }
        static PetSettings Settings(string root)
        {var settings=new PetSettings{AgentEnabled=true,AgentWorkspace=root,AgentApprovalMode=AgentApprovalMode.Full,AgentSandboxEnabled=false,AgentTerminalPolicy=AgentTerminalPolicy.ConfirmEach,ReadReplies=false,SaveChatHistory=true};settings.Text.BaseUrl="https://fixture.invalid/v1";settings.Text.ApiKey="fixture-only";settings.Text.Model="fixture";settings.Text.ToolSupport=CapabilitySupport.Supported;return settings;}
        static void RealConversationCrossesTurnsWithoutOwningTheProcess()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new Clock();using(var store=new AgentJobStore(Path.Combine(root.Path,"jobs"),clock))
                {
                    var manager=new AgentJobManager(store,new WindowsAgentJobProcessFactory(clock),clock);manager.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();
                    string marker=Path.Combine(root.Path,"ready.txt"),finish=Path.Combine(root.Path,"finish.txt"),nonce=Guid.NewGuid().ToString("N");
                    var model=new Model{Command="[IO.File]::WriteAllText('"+marker.Replace("'","''")+"','"+nonce+"'); while(-not [IO.File]::Exists('"+finish.Replace("'","''")+"')){ Start-Sleep -Milliseconds 25 }; [Console]::Out.Write('finished'); exit 7"};
                    var project=new ProjectDefinition{Id="session-project",Name="fixture",Workspace=root.Path,ApprovalMode=AgentApprovalMode.Full,SandboxEnabled=false,TerminalPolicy=AgentTerminalPolicy.ConfirmEach};var definition=new ConversationDefinition{Id="session-conversation",Mode="work"};var settings=Settings(root.Path);
                    try
                    {
                        using(var workspace=ConversationWorkspace.Create(project,definition,root.Path,"pet",()=>model,()=>new Speech(),()=>null,()=>settings,()=>true))
                        {
                            Configure(workspace,manager);workspace.Session.SendTextAsync("start the fixture",workspace.EffectiveSettings(),"pet").GetAwaiter().GetResult();
                            var owner=new AgentJobOwner(project.Id,definition.Id,"primary");AgentJobSnapshot snapshot;
                            Check(manager.TrySnapshot(owner,model.JobId,out snapshot)&&snapshot.State==AgentJobState.Running&&model.FirstWorkspace.AvailableToolNames.IndexOf("start_job",StringComparison.Ordinal)<0,"first real conversation turn disposes its tool workspace while the real process remains running");
                            var ready=Stopwatch.StartNew();while(!File.Exists(marker)&&ready.ElapsedMilliseconds<10000)Thread.Sleep(10);
                            Check(File.Exists(marker)&&File.ReadAllText(marker)==nonce,"real background command signals readiness with the fixture nonce");
                            Check(model.StartMilliseconds<=2000,"real unsandboxed long-lived background start returns within two seconds (actual "+model.StartMilliseconds+" ms)");
                            project.PlanMode=true;workspace.Session.SendTextAsync("inspect the running fixture",workspace.EffectiveSettings(),"pet").GetAwaiter().GetResult();
                            Check(model.Calls==2&&(string)((Dictionary<string,object>)Json(model.LastResult)["job"])["agent_id"]=="primary","second real model turn in Plan reads the same stable primary owner");
                            int before=workspace.Session.History.Messages.Count;string reply=workspace.Session.History.Messages.Last().Content;
                            using(var finished=new ManualResetEventSlim(false))
                            {
                                Action changed=()=>{AgentJobSnapshot current;if(manager.TrySnapshot(owner,model.JobId,out current)&&AgentJobStore.IsTerminal(current.State))finished.Set();};manager.Changed+=changed;
                                try{File.WriteAllText(finish,nonce);Check(finished.Wait(10000),"application manager observes actual process completion after both conversation turns ended");}
                                finally{manager.Changed-=changed;}
                            }
                            Check(manager.TrySnapshot(owner,model.JobId,out snapshot)&&snapshot.State==AgentJobState.Failed&&snapshot.RootExitCode==7&&snapshot.CleanupConfirmed,"real completion preserves exit 7 and confirms native cleanup");
                            Check(model.Calls==2&&workspace.Session.History.Messages.Count==before&&workspace.Session.History.Messages.Last().Content==reply,"job completion makes zero additional model calls and does not append output to chat history");
                        }
                    }
                    finally{manager.StopAllForExitAsync(CancellationToken.None).GetAwaiter().GetResult();}
                }
            }
        }
        static void LostResponseResumeDoesNotRerunTheRealCommand()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new Clock();using(var store=new AgentJobStore(Path.Combine(root.Path,"jobs"),clock))
                {
                    var manager=new AgentJobManager(store,new WindowsAgentJobProcessFactory(clock),clock);manager.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();
                    string once=Path.Combine(root.Path,"once.txt"),nonce=Guid.NewGuid().ToString("N");
                    var model=new Model{LoseFirstResponse=true,Command="[IO.File]::AppendAllText('"+once.Replace("'","''")+"','"+nonce+"'+[Environment]::NewLine); Start-Sleep -Seconds 30"};
                    var project=new ProjectDefinition{Id="response-project",Workspace=root.Path,ApprovalMode=AgentApprovalMode.Full,SandboxEnabled=false,TerminalPolicy=AgentTerminalPolicy.ConfirmEach};var settings=Settings(root.Path);
                    try
                    {
                        using(var workspace=ConversationWorkspace.Create(project,new ConversationDefinition{Id="response-conversation",Mode="work"},root.Path,"pet",()=>model,()=>new Speech(),()=>null,()=>settings,()=>true))
                        {
                            Configure(workspace,manager);bool lost=false;try{workspace.Session.SendTextAsync("start once",workspace.EffectiveSettings(),"pet").GetAwaiter().GetResult();}catch(ApiException){lost=true;}
                            Check(lost&&model.JobId!=null&&manager.ListForProject(project.Id).Count==1,"lost scripted model response leaves the real committed task in application ownership");
                            Check(model.StartMilliseconds<=2000,"actual 30-second command returns its start result within two seconds (actual "+model.StartMilliseconds+" ms)");
                            workspace.Session.ResumeAgentTaskAsync(workspace.EffectiveSettings(),"pet").GetAwaiter().GetResult();
                            Check((bool)Json(model.LastResult)["replayed"]&&manager.ListForProject(project.Id).Count==1,"resuming the persisted task replays its original start call without a second job");
                            var ready=Stopwatch.StartNew();while(!File.Exists(once)&&ready.ElapsedMilliseconds<10000)Thread.Sleep(10);
                            Check(File.Exists(once)&&File.ReadAllLines(once).SequenceEqual(new[]{nonce}),"actual command side effect occurs exactly once across lost response and resume");
                            project.TerminalPolicy=AgentTerminalPolicy.Disabled;workspace.Session.SendTextAsync("inspect after disabling terminal",workspace.EffectiveSettings(),"pet").GetAwaiter().GetResult();
                            Check((bool)Json(model.LastResult)["ok"],"real conversation still reads an existing job after terminal execution is disabled");
                            var stopped=manager.StopForUserAsync(model.JobId,AgentJobStopReason.User,CancellationToken.None).GetAwaiter().GetResult();
                            Check(stopped.CleanupConfirmed&&stopped.State==AgentJobState.Stopped,"trusted user stop remains available with terminal execution disabled");
                        }
                    }
                    finally{manager.StopAllForExitAsync(CancellationToken.None).GetAwaiter().GetResult();}
                }
            }
        }
        static void BuildConversationStopsItsOwnedRealJob()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new Clock();using(var store=new AgentJobStore(Path.Combine(root.Path,"jobs"),clock))
                {
                    var manager=new AgentJobManager(store,new WindowsAgentJobProcessFactory(clock),clock);manager.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();
                    var model=new Model{Command="Start-Sleep -Seconds 30"};var settings=Settings(root.Path);
                    var project=new ProjectDefinition{Id="stop-project",Workspace=root.Path,ApprovalMode=AgentApprovalMode.Full,SandboxEnabled=false,TerminalPolicy=AgentTerminalPolicy.ConfirmEach};
                    try
                    {
                        using(var workspace=ConversationWorkspace.Create(project,new ConversationDefinition{Id="stop-conversation",Mode="work"},root.Path,"pet",()=>model,()=>new Speech(),()=>null,()=>settings,()=>true))
                        {
                            Configure(workspace,manager);workspace.Session.SendTextAsync("start the fixture",workspace.EffectiveSettings(),"pet").GetAwaiter().GetResult();
                            model.StopNext=true;workspace.Session.SendTextAsync("stop the owned fixture",workspace.EffectiveSettings(),"pet").GetAwaiter().GetResult();
                            var response=Json(model.LastResult);var job=(Dictionary<string,object>)response["job"];
                            Check(model.Calls==2&&(bool)response["ok"]&&(string)job["id"]==model.JobId&&(string)job["state"]=="stopped"&&(bool)job["cleanup_confirmed"],"second real Build conversation turn stops only its owned native job using job_stop permission");
                        }
                    }
                    finally{manager.StopAllForExitAsync(CancellationToken.None).GetAwaiter().GetResult();}
                }
            }
        }
        static void OnlyPersistentProjectWorkConversationsCanReceiveJobs()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new Clock();using(var store=new AgentJobStore(Path.Combine(root.Path,"jobs"),clock))
                {
                    var manager=new AgentJobManager(store,new WindowsAgentJobProcessFactory(clock),clock);manager.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();var settings=Settings(root.Path);
                    try
                    {
                        foreach(var candidate in new[]{new{Project="project",Mode="chat",Draft=false},new{Project=ProjectCatalogStore.UnassignedProjectId,Mode="work",Draft=false},new{Project=ProjectCatalogStore.QuickProjectId,Mode="quick",Draft=false},new{Project="project",Mode="work",Draft=true}})
                        {
                            using(var workspace=ConversationWorkspace.Create(new ProjectDefinition{Id=candidate.Project,Workspace=root.Path},new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Mode=candidate.Mode},root.Path,"pet",()=>new Model(),()=>new Speech(),()=>null,()=>settings,()=>true))
                            {
                                workspace.IsDraft=candidate.Draft;bool denied=false;try{Configure(workspace,manager);}catch(TargetInvocationException error){denied=error.InnerException is InvalidOperationException;}
                                Check(denied,"conversation binding denies background jobs for "+candidate.Project+"/"+candidate.Mode+"/draft="+candidate.Draft);
                            }
                        }
                    }
                    finally{manager.StopAllForExitAsync(CancellationToken.None).GetAwaiter().GetResult();}
                }
            }
        }
        public static int Main()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return 2;
            checks=0;int failed=0;foreach(var test in new Action[]{RealConversationCrossesTurnsWithoutOwningTheProcess,LostResponseResumeDoesNotRerunTheRealCommand,BuildConversationStopsItsOwnedRealJob,OnlyPersistentProjectWorkConversationsCanReceiveJobs})try{test();}catch(Exception error){failed++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobSessionTests: "+checks+" passed");return failed==0?0:1;
        }
    }
}
