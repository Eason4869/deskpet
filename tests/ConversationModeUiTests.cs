using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ConversationModeUiTests
    {
        static int passed,failed;
        static readonly BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(root,i)))yield return x;}
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static void Run(string name,Action<PetWindow> test,bool work)
        {
            var cfg=new PetSettings{AgentEnabled=work,ReadReplies=false,GreetingEnabled=false,AutoUpdate=false,OnboardingState="completed"};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mode-ui-"+Guid.NewGuid().ToString("N"),"settings.json")),null);
            try{test(pet);passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}finally{foreach(var chat in Application.Current.Windows.OfType<ChatWindow>().ToArray())chat.Close();pet.Close();Pump();}
        }
        static ChatWindow Open(PetWindow pet){pet.ShowChat();Pump();return Application.Current.Windows.OfType<ChatWindow>().Single();}
        static bool Picker(ChatWindow chat){return All<Button>(chat).Any(b=>b.Name=="ConversationProjectPicker"&&b.IsVisible&&b.IsEnabled);}
        static void Mode(ChatWindow chat,bool work){chat.SwitchEntry(work);Pump();}
        static void PublishPlan(ConversationSession session, ChatMessage plan)
        {
            var subscribers=(Action<ChatMessage>)typeof(ConversationSession).GetField("MessageUpdated",hidden).GetValue(session);
            if(subscribers!=null)subscribers(plan);
            Pump();
        }
        static ChatMessage FinishPlan(ConversationSession session, string text, AgentTaskState task=null)
        {
            ChatMessage user;
            var message=session.History.BeginTurn("Fixture plan request","text",out user);
            if(task!=null)
            {
                var tasks=(Dictionary<string,AgentTaskState>)typeof(ConversationSession).GetField("agentTasks",hidden).GetValue(session);
                tasks[session.PersonaId]=task;
                session.History.LinkAgentTask(message.Id,task.Id);
            }
            var plan=session.History.Update(message.Id,text,"complete",null,0,null,null,true);
            PublishPlan(session,plan);
            return plan;
        }
        static void ClickPlanOffer(ChatWindow chat, string button)
        {
            All<Button>(chat).Single(item=>item.Name==button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        static QueuedMessage QueueAndInspectPlan(ChatWindow chat, ConversationWorkspace workspace)
        {
            var previousContext=SynchronizationContext.Current;
            try
            {
                // Hold dispatcher-bound queue delivery until the saved request has been inspected.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                ClickPlanOffer(chat,"AcceptPlanExecution");
                workspace.Queue.Hold();
                ClickPlanOffer(chat,"AcceptPlanExecution");
                var entry=workspace.Queue.Items.Single();
                workspace.Queue.Remove(entry.Id);
                return entry;
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            Run("full window personal draft is registered outside the quick section",delegate(PetWindow pet){var draft=pet.CreateDraftConversation(null,false);Check(draft.ProjectId==ProjectCatalogStore.UnassignedProjectId&&draft.Definition.Mode=="chat","full window draft uses quick owner");pet.PrepareConversationSubmission(draft);pet.FinishConversationSubmission(draft);Check(pet.Projects.UnassignedConversations.Any(c=>c.Id==draft.Id)&&!pet.Projects.QuickConversations.Any(c=>c.Id==draft.Id),"submitted full window chat was saved as quick chat");},false);
            Run("quick window registers only in the quick section on submission",delegate(PetWindow pet){pet.ShowQuickChat();Pump();var draft=(ConversationWorkspace)typeof(PetWindow).GetField("quickWorkspace",hidden).GetValue(pet);Check(draft.IsDraft&&draft.Definition.Mode=="quick"&&pet.Projects.QuickConversations.Count==0,"opening quick chat registered an empty record");pet.PrepareConversationSubmission(draft);pet.FinishConversationSubmission(draft);Check(pet.Projects.QuickConversations.Count==1&&pet.Projects.QuickConversations[0].Mode=="quick"&&!pet.Projects.UnassignedConversations.Any(),"submitted quick window did not use a quick record");foreach(var quick in Application.Current.Windows.OfType<QuickChatWindow>().ToArray())quick.Close();},false);
            Run("chat section plus creates a draft in that section",delegate(PetWindow pet){var chat=Open(pet);var sidebar=All<ProjectSidebar>(chat).Single();var section=sidebar.CreateSection("生活");All<Button>(sidebar).Single(b=>b.Name=="NewChatInSection_"+section.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();var draft=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);Check(draft.IsDraft&&draft.Definition.Mode=="chat"&&draft.Definition.SectionId==section.Id&&draft.ProjectId==ProjectCatalogStore.UnassignedProjectId,"chat plus did not create a draft under its section");pet.PrepareConversationSubmission(draft);pet.FinishConversationSubmission(draft);Check(pet.ProjectStore.Load().UnassignedConversations.Single(c=>c.Id==draft.Id).SectionId==section.Id,"submitted chat lost the chosen section");},false);
            Run("first full-window open in work mode exposes project selection",delegate(PetWindow pet){var chat=Open(pet);Check(Picker(chat),"initial work window has no enabled project picker");Check(chat.CanChooseProject,"initial work window cannot choose project");Check(!String.IsNullOrEmpty(chat.ConversationId),"initial open did not create a draft workspace");Check(!ProjectCatalogStore.AllConversations(pet.Projects).Any(),"opening an empty window saved a conversation");},true);
            Run("first full-window open in chat mode hides project selection",delegate(PetWindow pet){var chat=Open(pet);Check(!All<Button>(chat).Any(b=>b.Name=="ConversationProjectPicker"),"chat mode exposes project selection");Check(!chat.CanChooseProject,"chat mode allows programmatic project selection");},false);
            Run("Gmail search stays local outside Work mode", delegate(PetWindow pet)
            {
                var chat = Open(pet);
                var composer = typeof(ChatWindow).GetField("composer", hidden).GetValue(chat);
                var openGmail = typeof(ConversationComposer).GetMethod("TryOpenGmailCommand", hidden);
                Check((bool)openGmail.Invoke(composer, new object[] { "/GMAIL from:sender@example.invalid" }),
                    "reserved Gmail search would fall through to the ordinary model queue");
                Check(!(bool)openGmail.Invoke(composer, new object[] { "/gmailing" }), "unrelated text was claimed as a Gmail command");
                var input = All<MarkdownEditor>(chat).Single(editor => editor.Name == "MessageInput");
                var workspace = (ConversationWorkspace)typeof(ChatWindow).GetField("workspace", hidden).GetValue(chat);
                string query = "/gmail from:sender@example.invalid";
                input.Text = query;
                typeof(ConversationComposer).GetMethod("Submit", hidden).Invoke(composer, new object[] { false });
                var force = (System.Threading.Tasks.Task)typeof(ConversationComposer).GetMethod("InterruptDraft", hidden).Invoke(composer, new object[] { true });
                Check(force.IsCompleted && input.Text == query && !workspace.Queue.HasPending && !workspace.Queue.IsRunning && workspace.Session.History.Messages.Count == 0,
                    "ordinary send and force-send kept the query in the local draft without creating a conversation turn");
            }, false);
            Run("goal and plan controls share the work composer and stay out of Chat", delegate(PetWindow pet)
            {
                var chat=Open(pet);
                var composer=(ConversationComposer)typeof(ChatWindow).GetField("composer",hidden).GetValue(chat);
                var workspace=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);
                var input=All<MarkdownEditor>(chat).Single(editor=>editor.Name=="MessageInput");
                var placeholder=ChatUi.GetComposerPlaceholder(input);
                var plus=All<Button>(chat).Single(button=>button.Name=="AddAttachment");
                plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                var menu=plus.ContextMenu;
                var plan=menu.Items.OfType<MenuItem>().Single(item=>item.Name=="AgentPlanMode");
                plan.IsChecked=true;plan.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;Pump();
                var chip=All<Button>(chat).Single(button=>button.Name=="ComposerWorkMode");
                Check(chip.IsVisible&&workspace.EffectiveSettings().AgentPlanMode&&
                    placeholder.Text==TextResources.Get("conversationComposer.workMode.planPlaceholder"),
                    "plan entry must show a mode chip and the plan prompt");
                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(!chip.IsVisible&&!workspace.EffectiveSettings().AgentPlanMode&&placeholder.Text==TextResources.Get("chatUi.composer.text"),
                    "closing plan mode restores the normal placeholder and execution settings");
                typeof(ConversationComposer).GetMethod("BuildAtItems",hidden).Invoke(composer,null);
                var items=(System.Collections.IList)typeof(ConversationComposer).GetField("triggerItems",hidden).GetValue(composer);
                var goalItem=items.Cast<object>().Single(item=>(string)item.GetType().GetProperty("Title").GetValue(item)==
                    TextResources.Get("conversationComposer.buildAtItems.title.alternativeA"));
                ((Action)goalItem.GetType().GetField("Action").GetValue(goalItem))();Pump();
                Check(chip.IsVisible&&placeholder.Text==TextResources.Get("conversationComposer.workMode.goalPlaceholder")&&
                    pet.ConversationWorkMode(workspace.Session)=="goal","the @ entry switches to goal mode and its prompt");
                input.Text="整理使用手册并给出可运行示例";
                Check((bool)typeof(ConversationComposer).GetMethod("SaveInitialComposerGoal",hidden).Invoke(composer,null)&&
                    workspace.EffectiveSettings().AgentGoal==input.Text,"the first Work goal is saved on the current conversation");
                input.Clear();Pump();
                Check(placeholder.Text==TextResources.Get("conversationComposer.workMode.goalFollowUpPlaceholder"),
                    "a saved goal changes the hint to continue pursuing it");
                Mode(chat,false);
                var personal=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);
                Check(!chip.IsVisible&&personal.EffectiveSettings().AgentGoal==""&&!personal.EffectiveSettings().AgentPlanMode&&
                    placeholder.Text==TextResources.Get("chatUi.composer.text"),"Chat cannot see or apply work modes");
                Mode(chat,true);
                Check(chip.IsVisible&&pet.ConversationGoal(workspace.Session)=="整理使用手册并给出可运行示例",
                    "returning to Work restores the same conversation goal");
                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(!chip.IsVisible&&workspace.EffectiveSettings().AgentGoal==""&&pet.ConversationGoal(workspace.Session).Length>0,
                    "closing goal mode stops applying the goal while preserving its saved text");
            }, true);
            Run("workspace goal controls pause, edit, resume and delete a saved draft goal", delegate(PetWindow pet)
            {
                var chat=Open(pet);
                var workspace=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);
                pet.SetConversationWorkMode(workspace.Session,"goal");
                pet.SetConversationGoal(workspace.Session,"Write the first draft");
                typeof(ChatWindow).GetMethod("OpenWorkspacePlanGoal",hidden).Invoke(chat,new object[]{false});
                chat.UpdateLayout();Pump();
                var area=(PreviewWorkspace)typeof(ChatWindow).GetField("filePreview",hidden).GetValue(chat);
                var pane=(WorkspacePlanGoalPane)area.Selected;
                var toggle=All<Button>(pane).Single(button=>button.Name=="PauseWorkspaceGoal");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(pet.ConversationGoal(workspace.Session)=="Write the first draft"&&workspace.EffectiveSettings().AgentGoal=="",
                    "pausing hides the goal from requests but keeps the saved draft goal");

                var composer=(ConversationComposer)typeof(ChatWindow).GetField("composer",hidden).GetValue(chat);
                typeof(ConversationComposer).GetMethod("ChangeComposerWorkMode",hidden).Invoke(composer,new object[]{"plan"});Pump();
                Check(workspace.EffectiveSettings().AgentPlanMode&&
                    All<TextBlock>(pane).Any(text=>text.IsVisible&&text.Text==TextResources.Get("workspaceGoal.paused")),
                    "switching the composer to Plan keeps the outstanding goal explicitly paused");

                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate
                {
                    var modal=Application.Current.Windows.OfType<Window>().Single(window=>window.Name=="EditWorkspaceGoalDialog");
                    All<TextBox>(modal).Single(field=>field.Name=="WorkspaceGoalInput").Text="Review the finished draft";
                    All<Button>(modal).Single(button=>button.Name=="SaveWorkspaceGoal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                All<Button>(pane).Single(button=>button.Name=="EditWorkspaceGoal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(pet.ConversationGoal(workspace.Session)=="Review the finished draft"&&workspace.EffectiveSettings().AgentGoal==""&&
                    workspace.EffectiveSettings().AgentPlanMode,"editing a paused goal keeps Plan Mode active");
                typeof(ConversationComposer).GetMethod("SelectGoalMode",hidden).Invoke(composer,null);Pump();
                Check(workspace.EffectiveSettings().AgentPlanMode&&workspace.EffectiveSettings().AgentGoal=="",
                    "selecting Goal opens the paused goal pane without resuming it");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(workspace.EffectiveSettings().AgentGoal=="Review the finished draft","resuming uses the edited goal");

                var delete=All<Button>(pane).Single(button=>button.Name=="DeleteWorkspaceGoal");
                Action<string> closeConfirmation=buttonName=>Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate
                {
                    var modal=Application.Current.Windows.OfType<Window>().Single(window=>window.Name=="DeleteWorkspaceGoalDialog");
                    All<Button>(modal).Single(button=>button.Name==buttonName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                closeConfirmation("KeepWorkspaceGoal");
                delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(pet.ConversationGoal(workspace.Session)=="Review the finished draft","canceling confirmation keeps the goal");
                closeConfirmation("ConfirmDeleteWorkspaceGoal");
                delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(pet.ConversationGoal(workspace.Session)==""&&workspace.EffectiveSettings().AgentGoal==""&&!toggle.IsVisible&&
                    All<FlowDocumentScrollViewer>(pane).Single().Document==null,"confirmed deletion clears saved goal and the open pane");
            }, true);
            Run("plan completion offers one explicit execution and retains the exact plan through a Work rebind", delegate(PetWindow pet)
            {
                pet.Settings.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9",Model="plan-fixture"};
                var chat=Open(pet);
                var workspace=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);
                var card=All<Border>(chat).Single(item=>item.Name=="PlanExecutionOffer");
                pet.SetConversationWorkMode(workspace.Session,"plan");
                Check(!card.IsVisible,"entering Plan Mode without a new reply must not offer execution");

                var declined=FinishPlan(workspace.Session,"A declined plan");
                Check(card.IsVisible&&All<Button>(card).Single(b=>b.Name=="AcceptPlanExecution").IsEnabled,"a live final plan opens the execution offer");
                ClickPlanOffer(chat,"DeclinePlanExecution");
                PublishPlan(workspace.Session,declined);
                Check(!card.IsVisible&&!workspace.Queue.HasPending&&pet.ConversationWorkMode(workspace.Session)=="plan",
                    "declining neither executes nor reopens the same offer");

                Mode(chat,false);
                const string content="  # The offered plan\r\n\r\n1. Verify the current files.\r\n2. Keep the exact wording.  ";
                FinishPlan(workspace.Session,content);
                Check(!card.IsVisible&&!workspace.Queue.HasPending,"a plan that finishes away from Work cannot execute automatically");
                Mode(chat,true);
                Check(ReferenceEquals(workspace,typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat))&&card.IsVisible,
                    "returning to the same Work conversation shows the plan that finished in the background");
                var accept=All<Button>(card).Single(b=>b.Name=="AcceptPlanExecution");
                Mode(chat,false);
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!workspace.Queue.HasPending&&pet.ConversationWorkMode(workspace.Session)=="plan","a stale button cannot execute or alter the previous workspace");
                Mode(chat,true);
                Check(card.IsVisible,"switching views keeps an unanswered offer");

                var previousOptions=workspace.Definition.WorkOptions;
                pet.Settings.Text.BaseUrl="invalid-fixture-url";
                ClickPlanOffer(chat,"AcceptPlanExecution");
                Check(card.IsVisible&&!workspace.Queue.HasPending&&ReferenceEquals(workspace.Definition.WorkOptions,previousOptions),
                    "a rejected submission restores its original Plan Mode settings and remains available for retry");
                pet.Settings.Text.BaseUrl="http://127.0.0.1:9";

                var draft=All<MarkdownEditor>(chat).Single(editor=>editor.Name=="MessageInput");
                draft.Text="Keep my unsent draft";
                var entry=QueueAndInspectPlan(chat,workspace);
                Check(!card.IsVisible&&!workspace.EffectiveSettings().AgentPlanMode&&!entry.ConversationOnly&&
                    entry.Text==TextResources.Get("conversationComposer.planExecution.request")&&
                    String.Concat(entry.Attachments.Select(file=>file.Text))==content&&draft.Text=="Keep my unsent draft",
                    "Yes exits Plan Mode and enqueues the exact offered plan once without consuming an existing draft");
                Pump();

                pet.SetConversationWorkMode(workspace.Session,"plan");
                var structured=new AgentTaskState{PersonaId=workspace.Session.PersonaId,Workspace=workspace.ProjectWorkspace,Status="complete",
                    Plan=new List<AgentPlanStep>{new AgentPlanStep{Text="Keep the exact first step.",Status="pending"},new AgentPlanStep{Text="Keep the exact second step.",Status="in_progress"}}};
                const string summary="The plan is ready for execution.";
                FinishPlan(workspace.Session,summary,structured);
                Check(card.IsVisible,"a completed step-based plan opens the execution offer");
                structured.Plan[0].Text="Changed after the offer was shown";
                var structuredEntry=QueueAndInspectPlan(chat,workspace);
                Check(String.Concat(structuredEntry.Attachments.Select(file=>file.Text))==
                    "[pending] Keep the exact first step.\n[in_progress] Keep the exact second step.\n\n"+summary,
                    "the captured matching steps precede the exact final reply, even if the task state changes after the offer");
                Pump();

                pet.SetConversationWorkMode(workspace.Session,"plan");
                var closedOffer=FinishPlan(workspace.Session,"Close this plan without executing");
                ClickPlanOffer(chat,"ClosePlanExecutionOffer");
                PublishPlan(workspace.Session,closedOffer);
                Check(!card.IsVisible&&!workspace.Queue.HasPending&&pet.ConversationWorkMode(workspace.Session)=="plan",
                    "closing an offer preserves planning and prevents duplicate execution prompts");

                string extended=new string('a',99999)+"\uD83D\uDE00\r\n"+new string('b',80);
                var attachments=ConversationComposer.CreatePlanExecutionAttachments(extended);
                Check(attachments.Count==2&&String.Concat(attachments.Select(file=>file.Text))==extended&&
                    !Char.IsHighSurrogate(attachments[0].Text.Last()),"long plans preserve full Unicode text across ordered attachments");
            }, true);
            Run("mode switching refreshes project header without losing an unsent draft",delegate(PetWindow pet){var chat=Open(pet);var input=All<MarkdownEditor>(chat).Single(t=>t.Name=="MessageInput");input.Text="保留这条未发送的草稿";for(int i=0;i<3;i++){Mode(chat,false);Check(!All<Button>(chat).Any(b=>b.Name=="ConversationProjectPicker")&&!chat.CanChooseProject,"chat mode retained project header");Mode(chat,true);Check(Picker(chat)&&chat.CanChooseProject,"work mode failed to restore project header");Check(input.Text=="保留这条未发送的草稿","switching lost draft text");}Check(!ProjectCatalogStore.AllConversations(pet.Projects).Any(),"mode switch saved empty conversations");},true);
            Run("reopening the full window in work mode initializes the picker again",delegate(PetWindow pet){for(int repeat=0;repeat<20;repeat++){Open(pet).Close();Pump();Check(Picker(Open(pet)),"reopened work window has no picker at cycle "+repeat);}},true);
            Run("entry switching separates chat and restores the work project",delegate(PetWindow pet){var chat=Open(pet);var project=ProjectCatalogStore.CreateProject(pet.Projects,"previous project",pet.Store.DirectoryPath);var old=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);old.FollowProject(project);chat.RefreshAppearance();chat.UpdateLayout();Pump();Check(All<TextBlock>(chat).Any(t=>t.Text==project.Name),"selected project was not rendered");Mode(chat,false);var personal=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);Check(personal.ProjectId==ProjectCatalogStore.UnassignedProjectId&&!personal.EffectiveSettings().AgentEnabled,"chat draft retained the work project");Mode(chat,true);var work=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);Check(work.ProjectId==project.Id&&Picker(chat),"returning to work failed to restore the selected project");},true);
            Run("saved work records display their project header even when the global preference is chat",delegate(PetWindow pet){var project=ProjectCatalogStore.CreateProject(pet.Projects,"saved work",pet.Store.DirectoryPath);pet.OpenProjectConversation(project,project.Conversations[0]);Pump();var chat=Application.Current.Windows.OfType<ChatWindow>().Single();Check(All<Button>(chat).Any(b=>b.Name=="ConversationProjectPicker"&&b.IsVisible),"saved work record has no project header");Check(chat.WorkEntry&&All<Button>(chat).Any(b=>b.Name=="AgentPermissionPicker"),"saved work entry has no permission control");Check(!chat.CanChooseProject,"saved conversation incorrectly allows changing project");},false);
            app.Shutdown();Console.WriteLine("Conversation mode UI tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
    }
}
