using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                if(text=="blocked request"){Started.TrySetResult(true);await Release.Task.ConfigureAwait(false);}
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
                CheckQueuedWorkOptions(root,project).GetAwaiter().GetResult();
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
            }
            using(var manager=new ConversationWorkspaceManager(root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true))
            {
                var draft=manager.CreateWorkspace(project,new ConversationDefinition{Id="restored-draft"});
                draft.IsDraft=true;
                manager.BindDraft(draft,"pet");
                Check(draft.EffectiveSettings().AgentGoal=="persistent draft goal","an unsent work goal survives draft restart");
                draft.FinishDraftWorkOptions();
                Check(draft.EffectiveSettings().AgentGoal=="persistent draft goal","finishing draft settings preserves the promoted work options");
                var next=manager.CreateWorkspace(project,new ConversationDefinition{Id="next-draft"});
                next.IsDraft=true;
                manager.BindDraft(next,"pet");
                Check(next.Definition.WorkOptions==null,"a promoted draft goal does not leak into the next draft");
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
    }
}
