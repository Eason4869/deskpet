using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ConversationWorkspaceTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class Api:IConversationApi
        {
            internal readonly List<string> Prompts=new List<string>();
            internal readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> Release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c)
            {
                Prompts.Add(m[0].Content);
                var text=m[m.Count-1].Content;
                if(text=="blocked request")
                {
                    Started.TrySetResult(true);
                    var cancelled=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using(c.Register(()=>cancelled.TrySetResult(true)))
                        await Task.WhenAny(Release.Task,cancelled.Task).ConfigureAwait(false);
                    c.ThrowIfCancellationRequested();
                }
                return "reply-"+text;
            }
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        static PetSettings Settings(){var s=new PetSettings{ReadReplies=false,SaveChatHistory=true};s.Text.BaseUrl="http://127.0.0.1:1";s.Text.Model="fixture";return s;}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"workspaces-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var project=new ProjectDefinition{Id="p",Name="project",Workspace=root};var chatA=new ConversationDefinition{Id="a",Name="A"};var chatB=new ConversationDefinition{Id="b",Name="B"};
                using(var a=ConversationWorkspace.Create(project,chatA,root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true))
                using(var b=ConversationWorkspace.Create(project,chatB,root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true))
                {
                    project.PlanMode=true;project.Goal="old project goal";
                    Check(b.EffectiveSettings().AgentPlanMode&&b.EffectiveSettings().AgentGoal=="","legacy project plan defaults do not adopt the previous project goal");
                    chatA.WorkOptions=new ConversationWorkOptions("goal","alpha goal");
                    Check(a.EffectiveSettings().AgentGoal=="alpha goal"&&!a.EffectiveSettings().AgentPlanMode&&b.EffectiveSettings().AgentPlanMode,"active goal overrides the plan for only its own conversation");
                    var frozen=a.EffectiveSettings();
                    chatA.WorkOptions=new ConversationWorkOptions("off",chatA.WorkOptions.Goal);
                    Check(a.EffectiveSettings().AgentGoal==""&&!a.EffectiveSettings().AgentPlanMode&&chatA.WorkOptions.Goal=="alpha goal"&&frozen.AgentGoal=="alpha goal","leaving the mode suppresses prompts without modifying the saved goal or previous snapshot");
                    project.Conversations.Add(chatA);project.Conversations.Add(chatB);
                    var catalog=new ProjectCatalog();catalog.Projects.Add(project);
                    var store=new ProjectCatalogStore(Path.Combine(root,"catalog.json"));store.Save(catalog);
                    store.SaveConversationWorkOptions(chatA.Id,new ConversationWorkOptions("plan",chatA.WorkOptions.Goal));
                    var reloaded=store.Load().Projects[0].Conversations;
                    Check(reloaded[0].WorkOptions.Mode=="plan"&&reloaded[0].WorkOptions.Goal=="alpha goal"&&reloaded[1].WorkOptions==null,"named work options survive reload independently of sibling conversations");
                    store.SaveConversationWorkOptions(chatA.Id,new ConversationWorkOptions("plan","alpha goal","revision-a",true));
                    reloaded=store.Load().Projects[0].Conversations;
                    Check(reloaded[0].WorkOptions.Mode=="plan"&&reloaded[0].WorkOptions.GoalRevision=="revision-a"&&
                        reloaded[0].WorkOptions.GoalPaused&&reloaded[1].WorkOptions==null,
                        "persisted Plan mode retains the conversation's paused goal independently of siblings");
                    a.Session.SendTextAsync("alpha",Settings(),"pet").GetAwaiter().GetResult();b.Session.SendTextAsync("beta",Settings(),"pet").GetAwaiter().GetResult();
                    Check(a.Session.History.Messages.Count==2 && b.Session.History.Messages.Count==2,"two workspaces keep independent history");
                    Check(a.Session.History.Messages[0].Content=="alpha" && b.Session.History.Messages[0].Content=="beta","workspace history cannot leak across conversations");
                    Check(a.Queue.Items.Count==0 && b.Queue.Items.Count==0,"workspace queues are separate");
                    Check(a.DataDirectory!=b.DataDirectory && File.Exists(Path.Combine(a.DataDirectory,"history"))==false,"workspace storage is namespaced by conversation");
                }
                var temporary=new ConversationDefinition{Id="transient",Name="reply",Mode="chat",PetId="pet"};
                using(var draft=ConversationWorkspace.Create(project,temporary,root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true,transient:true))
                {
                    temporary.WorkOptions=new ConversationWorkOptions("goal","personal hidden");
                    Check(!draft.EffectiveSettings().AgentPlanMode&&draft.EffectiveSettings().AgentGoal=="","personal sessions suppress even preexisting work options");
                    draft.Session.AttachProactive(new ProactiveEvent("event","pet","Hello",null,DateTime.UtcNow,null,null));
                    Check(draft.Session.History.Messages.Count==1,"proactive event appears in transient history");
                    Check(!Directory.Exists(draft.DataDirectory),"unanswered proactive workspace creates no directory");
                    bool rejected=false;try{draft.Queue.Enqueue("unsent");}catch(InvalidOperationException){rejected=true;}
                    Check(rejected,"unadmitted queue rejects persistence and model work");
                    Check(!Directory.Exists(draft.DataDirectory),"unadmitted queue creates no files");
                    draft.CloseAsync().GetAwaiter().GetResult();
                    Check(!Directory.Exists(draft.DataDirectory),"closing unanswered workspace leaves no artifacts");
                }
                var protectedDefinition=new ConversationDefinition{Id="preexisting",Name="reply",Mode="chat",PetId="pet"};string protectedDirectory=Path.Combine(root,"conversations","preexisting","kernel-sessions");Directory.CreateDirectory(protectedDirectory);string protectedFile=Path.Combine(protectedDirectory,"existing.session");File.WriteAllText(protectedFile,"preserve");
                bool collision=false;try{using(var transient=ConversationWorkspace.Create(project,protectedDefinition,root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true,transient:true)){} }catch(IOException){collision=true;}Check(collision && File.Exists(protectedFile),"transient creation rejects preexisting storage without reading or deleting it");
                CheckDraftOptions(root,project);
                CheckLegacyPause();
                CheckQueuedWorkOptions(root,project).GetAwaiter().GetResult();
                CheckGoalControlsQueue(root,project).GetAwaiter().GetResult();
                CheckStopActiveGoal(root,project).GetAwaiter().GetResult();
                Console.WriteLine("Conversation workspace tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }

        static void CheckDraftOptions(string root,ProjectDefinition project)
        {
            var other=new ProjectDefinition{Id="other-project",Name="other project",Workspace=root};
            using(var manager=new ConversationWorkspaceManager(root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true))
            {
                var draft=manager.CreateWorkspace(project,new ConversationDefinition{Id="first-draft"});
                draft.IsDraft=true;
                manager.BindDraft(draft,"pet");
                draft.SaveDraftWorkOptions(new ConversationWorkOptions("goal","persistent draft goal"));
                draft.FollowProject(other);
                Check(draft.Definition.WorkOptions==null,"draft work settings are isolated from another project scope");
                draft.FollowProject(project);
                Check(draft.WorkMode=="goal"&&draft.EffectiveSettings().AgentGoal=="persistent draft goal","switching back restores the original draft work settings");
                draft.SaveDraftWorkOptions(draft.Definition.WorkOptions.WithGoalPaused(true).WithMode("plan"));
            }
            using(var manager=new ConversationWorkspaceManager(root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true))
            {
                var draft=manager.CreateWorkspace(project,new ConversationDefinition{Id="restored-draft"});
                draft.IsDraft=true;
                manager.BindDraft(draft,"pet");
                Check(draft.Definition.WorkOptions.Goal=="persistent draft goal"&&draft.Definition.WorkOptions.GoalPaused&&
                    draft.EffectiveSettings().AgentGoal==""&&draft.EffectiveSettings().AgentPlanMode,
                    "a paused draft goal and its active Plan mode survive restart independently");
                draft.SaveDraftWorkOptions(draft.Definition.WorkOptions.WithMode("goal").WithGoalPaused(false));
                draft.FinishDraftWorkOptions();
                Check(draft.EffectiveSettings().AgentGoal=="persistent draft goal","finishing draft settings preserves the promoted work options");
                var next=manager.CreateWorkspace(project,new ConversationDefinition{Id="next-draft"});
                next.IsDraft=true;
                manager.BindDraft(next,"pet");
                Check(next.Definition.WorkOptions==null,"a promoted draft goal does not leak into the next draft");
            }
        }

        static void CheckLegacyPause()
        {
            using(var input=new MemoryStream(Encoding.UTF8.GetBytes("{\"Mode\":\"goal-paused\",\"Goal\":\"legacy pending goal\",\"GoalRevision\":\"legacy-revision\"}")))
            {
                var old=(ConversationWorkOptions)new DataContractJsonSerializer(typeof(ConversationWorkOptions)).ReadObject(input);
                var plan=old.WithMode("plan");
                var settings=Settings();
                plan.Apply(settings,true);
                Check(old.Mode=="off"&&plan.GoalPaused&&plan.Goal=="legacy pending goal"&&plan.GoalRevision=="legacy-revision"&&
                    settings.AgentPlanMode&&settings.AgentGoal=="","a stored legacy pause migrates before switching modes");
            }
        }

        static async Task CheckQueuedWorkOptions(string root,ProjectDefinition project)
        {
            var api=new Api();
            var definition=new ConversationDefinition{Id="queued-work-settings",WorkOptions=new ConversationWorkOptions("goal","purpose for first queued request")};
            using(var workspace=ConversationWorkspace.Create(project,definition,root,"pet",()=>api,()=>new Speech(),()=>null,Settings,()=>true))
            {
                var active=workspace.Session.SendTextAsync("blocked request",Settings(),"pet");
                try
                {
                    if(await Task.WhenAny(api.Started.Task,Task.Delay(5000))!=api.Started.Task)throw new Exception("fixture request did not block");
                    workspace.Queue.Enqueue("goal request");
                    definition.WorkOptions=new ConversationWorkOptions("off",definition.WorkOptions.Goal);
                    workspace.Queue.Enqueue("regular request");
                    definition.WorkOptions=new ConversationWorkOptions("plan",definition.WorkOptions.Goal);
                    Check(workspace.Queue.Items.Select(item=>item.WorkOptions.Mode).SequenceEqual(new[]{"goal","off"}),"queued rows capture their separate work options at submission");
                }
                finally { api.Release.TrySetResult(true); }
                await active;
                await workspace.Queue.FlushAsync();
                Check(api.Prompts.Count==3&&api.Prompts[1].Contains("purpose for first queued request")&&!api.Prompts[2].Contains("purpose for first queued request")&&!api.Prompts[2].Contains("当前为计划模式"),"queued prompts retain submitted modes when the composer mode changes");
            }
        }

        static async Task CheckGoalControlsQueue(string root,ProjectDefinition project)
        {
            var api=new Api();
            var original=new ConversationWorkOptions("goal","Original goal");
            var definition=new ConversationDefinition{Id="goal-management-queue",WorkOptions=original};
            using(var workspace=ConversationWorkspace.Create(project,definition,root,"pet",()=>api,()=>new Speech(),()=>null,Settings,()=>true))
            {
                var active=workspace.Session.SendTextAsync("blocked request",Settings(),"pet");
                try
                {
                    if(await Task.WhenAny(api.Started.Task,Task.Delay(5000))!=api.Started.Task)throw new Exception("fixture request did not block");
                    workspace.Queue.Enqueue("Queued instruction for the original goal");
                    definition.WorkOptions=original.WithGoalPaused(true).WithMode("plan");
                    await workspace.Session.StopGoalAsync();
                    Check(workspace.Session.IsBusy,"pausing a goal leaves an unrelated active request running");
                }
                finally{api.Release.TrySetResult(true);}
                await active;
                await workspace.Queue.FlushAsync();
                Check(workspace.Queue.IsPaused&&workspace.Queue.Items.Count==1&&api.Prompts.Count==1,
                    "switching to Plan keeps an explicitly paused goal request queued");
                workspace.Queue.Resume();
                await workspace.Queue.FlushAsync();
                Check(workspace.Queue.IsPaused&&workspace.Queue.Items.Count==1&&api.Prompts.Count==1,
                    "resuming the message queue cannot restart the independently paused goal");

                definition.WorkOptions=original.WithGoal("").WithGoal("Original goal");
                workspace.Queue.Resume();
                await workspace.Queue.FlushAsync();
                Check(workspace.Queue.IsPaused&&workspace.Queue.Items.Count==1&&api.Prompts.Count==1,
                    "deleting and recreating identical goal text never restarts an obsolete queued request");

                definition.WorkOptions=definition.WorkOptions.WithGoal("Revised goal");
                workspace.Queue.Edit(workspace.Queue.Items.Single().Id,"Replacement instruction");
                workspace.Queue.Resume();
                await workspace.Queue.FlushAsync();
                Check(api.Prompts.Count==2&&api.Prompts[1].Contains("Revised goal")&&!api.Prompts[1].Contains("Original goal"),
                    "explicitly editing a blocked queued message uses the new goal");
            }
        }

        static async Task CheckStopActiveGoal(string root,ProjectDefinition project)
        {
            var api=new Api();
            var definition=new ConversationDefinition{Id="goal-management-active",WorkOptions=new ConversationWorkOptions("goal","Prepare the report")};
            using(var workspace=ConversationWorkspace.Create(project,definition,root,"pet",()=>api,()=>new Speech(),()=>null,Settings,()=>true))
            {
                var active=workspace.Session.SendTextAsync("blocked request",workspace.EffectiveSettings(),"pet");
                try
                {
                    if(await Task.WhenAny(api.Started.Task,Task.Delay(5000))!=api.Started.Task)throw new Exception("fixture goal did not start");
                    var checkpoint=new AgentTaskState{ConversationGoal="Prepare the report",Workspace=root,Goal="blocked request"};
                    definition.WorkOptions=new ConversationWorkOptions(ConversationWorkOptions.PausedGoalMode,checkpoint.ConversationGoal);
                    await workspace.Session.StopGoalAsync();
                    bool cancelled=false;
                    try{await active;}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled&&!workspace.Session.IsBusy,"pausing a running goal cancels the active model request");
                    Check(!workspace.Session.CanResumeAgentTask(checkpoint),"the paused goal's checkpoint cannot be resumed");
                    definition.WorkOptions=definition.WorkOptions.WithGoal("An updated objective");
                    definition.WorkOptions=new ConversationWorkOptions("goal",definition.WorkOptions.Goal,definition.WorkOptions.GoalRevision);
                    bool rejected=false;
                    try{await workspace.Session.SendTextAsync("Continue",workspace.EffectiveSettings(),"pet",resumeTask:checkpoint);}
                    catch(InvalidOperationException){rejected=true;}
                    Check(rejected&&api.Prompts.Count==1&&!workspace.Session.IsBusy,
                        "a checkpoint from the previous goal is rejected before it can issue a new request");
                }
                finally{api.Release.TrySetResult(true);}
            }
        }
    }
}
