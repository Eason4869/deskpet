using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu.Tests
{
    public static class AgentJobProjectTests
    {
        static int checks;
        static void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        static bool NoOfficialDownloads(PetWindow pet){return typeof(PetWindow).GetField("officialDownloads",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet)==null;}
        sealed class Clock : IAgentJobClock
        {public DateTime UtcNow{get{return DateTime.UtcNow;}}public long ElapsedMilliseconds{get{return Environment.TickCount&Int32.MaxValue;}}}
        sealed class Approval : IAgentAuthorizationService
        {public Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken token){return Task.FromResult(new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow});}}
        sealed class Lease : IDisposable{public void Dispose(){}}
        sealed class Process : IAgentJobProcess
        {
            internal bool Clean;
            AgentJobProcessExit status=new AgentJobProcessExit(false,null,false,false,1);
            readonly TaskCompletionSource<AgentJobProcessExit> completed=new TaskCompletionSource<AgentJobProcessExit>();
            public AgentJobProcessExit Status{get{return status;}}public Task<AgentJobProcessExit> Completion{get{return completed.Task;}}public void Resume(){}
            public Task<AgentJobCleanupResult> StopAsync(CancellationToken token){if(Clean){status=new AgentJobProcessExit(true,0,true,true,0);completed.TrySetResult(status);}return Task.FromResult(new AgentJobCleanupResult(Clean,Clean?null:"fixture_held"));}
            public void Dispose(){}
        }
        sealed class Factory : IAgentJobProcessFactory
        {
            internal Process Last;
            public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token){Last=new Process();return Task.FromResult((IAgentJobProcess)Last);}
        }
        static Task<AgentJobSnapshot> Start(AgentJobManager manager,string directory,ProjectDefinition project,ConversationDefinition conversation)
        {
            string command="fixture";
            var owner=new AgentJobOwner(project.Id,conversation.Id,"primary");
            var invocation=new AgentToolInvocation("start_job",new Dictionary<string,object>{{"command",command},{"title",command},{"working_directory",directory},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId=project.Id,AgentId="primary",TaskId="project-fixture",CallId=Guid.NewGuid().ToString("N")});
            var context=new AgentJobStartContext(owner,invocation,new Approval(),()=>new AgentJobPolicySnapshot(directory,"fixture",1,1800,true,false,false,AgentApprovalMode.Full,null),()=>new Lease(),()=>{});
            return manager.StartAsync(context,new AgentJobSpec(command,null,directory,1800),CancellationToken.None);
        }
        static async Task Run()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=true,ReadReplies=false,OnboardingState="completed"};foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                var pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root.Path,"settings.json")),null);
                pet.Skin.FixtureEmbedded=true;pet.Show();pet.SetPaused(true);
                var original=pet.AgentJobs;await original.InitializeAsync();await original.StopAllForExitAsync(CancellationToken.None);original.Dispose();
                var factory=new Factory();var service=new AgentJobApplicationService(root.Path,clock=>factory,new Clock());
                typeof(PetWindow).GetField("agentJobServices",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,service);await service.InitializeAsync();
                Exception failure=null;
                try
                {
                    var project=ProjectCatalogStore.CreateProject(pet.Projects,"source",root.Path);var destination=ProjectCatalogStore.CreateProject(pet.Projects,"destination",root.Path);var conversation=project.Conversations[0];pet.ProjectStore.Save(pet.Projects);
                    string directory=ConversationWorkspace.ResolveDataDirectory(root.Path,conversation.Id);Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"sentinel"),"retain");
                    var job=await Start(service.Manager,root.Path,project,conversation);string before=File.ReadAllText(Path.Combine(root.Path,"projects.json"));
                    Check(!pet.Workspaces.OpenWorkspaces.Any(),"active task fixture has no open conversation workspace");
                    Check(!pet.MoveConversation(project,destination,conversation)&&project.Conversations.Contains(conversation)&&!destination.Conversations.Contains(conversation),"real WPF move rejects an unopened owner's active task");
                    bool changed=false;Check(!pet.TryChangeWorkBindings(new[]{project.Id},null,()=>changed=true)&&!changed,"real WPF root edit refuses the active owner");
                    typeof(PetWindow).GetField("editingProject",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,project);
                    var panel=new WorkSettingsPanel(pet);typeof(WorkSettingsPanel).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel);
                    ((TextBox)typeof(WorkSettingsPanel).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel)).Text=Path.GetDirectoryName(root.Path);
                    Check(!panel.Save()&&project.Workspace==root.Path,"work settings cannot change a root while its background task is active");
                    bool denied=false;try{await pet.DeleteConversationAsync(project,conversation);}catch(InvalidOperationException){denied=true;}
                    Check(denied&&project.Conversations.Contains(conversation)&&Directory.Exists(directory)&&File.ReadAllText(Path.Combine(root.Path,"projects.json"))==before,"failed native cleanup leaves WPF conversation model, catalog and files unchanged");
                    factory.Last.Clean=true;await pet.DeleteConversationAsync(project,conversation);
                    Check(!project.Conversations.Contains(conversation)&&!Directory.Exists(directory),"successful cleanup precedes real WPF conversation removal");
                    AgentJobSnapshot stopped;Check(service.Manager.TrySnapshot(job.Owner,job.Id,out stopped)&&stopped.CleanupConfirmed&&stopped.State==AgentJobState.Stopped,"deleted conversation's task retains its original stopped owner record");
                    var next=ProjectCatalogStore.CreateConversation(pet.Projects,project.Id,"archive");pet.ProjectStore.Save(pet.Projects);var active=await Start(service.Manager,root.Path,project,next);
                    pet.ArchiveConversation(project,next);Check(next.Archived&&!factory.Last.Status.TreeExited,"archiving retains the application-owned background task");
                    denied=false;try{await pet.DeleteProjectAsync(project);}catch(InvalidOperationException){denied=true;}
                    Check(denied&&pet.Projects.Projects.Contains(project),"failed project cleanup preserves an archived task owner");
                    factory.Last.Clean=true;await pet.DeleteProjectAsync(project);Check(!pet.Projects.Projects.Contains(project),"project delete joins all tasks before removing its binding");
                    using(var secondRoot=new AgentJobTestSupport.TemporaryDirectory())
                    {
                        var second=new PetWindow(null,settings.Copy(),new SettingsStore(Path.Combine(root.Path,"other-settings.json")),null);
                        second.Skin.FixtureEmbedded=true;
                        var secondClosed=new TaskCompletionSource<bool>();second.Closed+=delegate{secondClosed.TrySetResult(true);};
                        Exception secondFailure=null;
                        try
                        {
                            Check(!await second.AgentJobs.InitializeAsync(),"second WPF host cannot acquire the shared job lease");
                            var catalog=second.Projects;catalog.UnassignedConversations.Add(new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="plain",Mode="chat",PetId=second.Skin.Id});
                            second.ProjectStore.Save(catalog);Check(true,"lease failure still permits ordinary chat catalog writes without changing work bindings");
                            var work=ProjectCatalogStore.CreateProject(catalog,"denied",root.Path);denied=false;try{second.ProjectStore.Save(catalog);}catch(IOException){denied=true;}
                            Check(denied&&!new ProjectCatalogStore(Path.Combine(root.Path,"projects.json")).Load().Projects.Any(p=>p.Id==work.Id),"a second WPF host cannot persist a new work binding");
                            catalog.Projects.Remove(work);catalog.ActiveProjectId="";
                            var fresh=ProjectCatalogStore.CreateProject(pet.Projects,"new owner",root.Path);pet.ProjectStore.Save(pet.Projects);
                            denied=false;try{second.ProjectStore.Save(catalog);}catch(IOException){denied=true;}
                            Check(denied&&new ProjectCatalogStore(Path.Combine(root.Path,"projects.json")).Load().Projects.Any(p=>p.Id==fresh.Id),"ordinary chat writes from a stale second host cannot overwrite newer work bindings");
                            Check(NoOfficialDownloads(second),"second catalog host starts no unrelated official download");
                        }
                        catch(Exception error){secondFailure=error;}
                        finally{second.Close();}
                        await AgentJobExitHost.WaitAsync(()=>secondClosed.Task.IsCompleted,10000,"second catalog fixture closed");
                        await second.ImageServicesShutdown;
                        if(secondFailure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(secondFailure).Throw();
                    }
                    Check(NoOfficialDownloads(pet),"catalog fixture starts no unrelated official download");
                }
                catch(Exception error){failure=error;}
                if(factory.Last!=null)factory.Last.Clean=true;
                var petClosed=new TaskCompletionSource<bool>();pet.Closed+=delegate{petClosed.TrySetResult(true);};pet.Close();
                await AgentJobExitHost.WaitAsync(()=>petClosed.Task.IsCompleted&&service.Manager==null,10000,"catalog fixture exit");
                await pet.ImageServicesShutdown;
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
            app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();Console.WriteLine("AgentJobProjectTests: "+checks+" passed");result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{app.Shutdown();}}));app.Run();return result;
        }
    }
}
