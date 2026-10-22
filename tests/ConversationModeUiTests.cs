using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
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
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            Run("full window personal draft is registered outside the quick section",delegate(PetWindow pet){var draft=pet.CreateDraftConversation(null,false);Check(draft.ProjectId==ProjectCatalogStore.UnassignedProjectId&&draft.Definition.Mode=="chat","full window draft uses quick owner");pet.PrepareConversationSubmission(draft);pet.FinishConversationSubmission(draft);Check(pet.Projects.UnassignedConversations.Any(c=>c.Id==draft.Id)&&!pet.Projects.QuickConversations.Any(c=>c.Id==draft.Id),"submitted full window chat was saved as quick chat");},false);
            Run("quick window registers only in the quick section on submission",delegate(PetWindow pet){pet.ShowQuickChat();Pump();var draft=(ConversationWorkspace)typeof(PetWindow).GetField("quickWorkspace",hidden).GetValue(pet);Check(draft.IsDraft&&draft.Definition.Mode=="quick"&&pet.Projects.QuickConversations.Count==0,"opening quick chat registered an empty record");pet.PrepareConversationSubmission(draft);pet.FinishConversationSubmission(draft);Check(pet.Projects.QuickConversations.Count==1&&pet.Projects.QuickConversations[0].Mode=="quick"&&!pet.Projects.UnassignedConversations.Any(),"submitted quick window did not use a quick record");foreach(var quick in Application.Current.Windows.OfType<QuickChatWindow>().ToArray())quick.Close();},false);
            Run("chat section plus creates a draft in that section",delegate(PetWindow pet){var chat=Open(pet);var sidebar=All<ProjectSidebar>(chat).Single();var section=sidebar.CreateSection("生活");All<Button>(sidebar).Single(b=>b.Name=="NewChatInSection_"+section.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();var draft=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);Check(draft.IsDraft&&draft.Definition.Mode=="chat"&&draft.Definition.SectionId==section.Id&&draft.ProjectId==ProjectCatalogStore.UnassignedProjectId,"chat plus did not create a draft under its section");pet.PrepareConversationSubmission(draft);pet.FinishConversationSubmission(draft);Check(pet.ProjectStore.Load().UnassignedConversations.Single(c=>c.Id==draft.Id).SectionId==section.Id,"submitted chat lost the chosen section");},false);
            Run("first full-window open in work mode exposes project selection",delegate(PetWindow pet){var chat=Open(pet);Check(Picker(chat),"initial work window has no enabled project picker");Check(chat.CanChooseProject,"initial work window cannot choose project");Check(!String.IsNullOrEmpty(chat.ConversationId),"initial open did not create a draft workspace");Check(!ProjectCatalogStore.AllConversations(pet.Projects).Any(),"opening an empty window saved a conversation");},true);
            Run("first full-window open in chat mode hides project selection",delegate(PetWindow pet){var chat=Open(pet);Check(!All<Button>(chat).Any(b=>b.Name=="ConversationProjectPicker"),"chat mode exposes project selection");Check(!chat.CanChooseProject,"chat mode allows programmatic project selection");},false);
            Run("mode switching refreshes project header without losing an unsent draft",delegate(PetWindow pet){var chat=Open(pet);var input=All<MarkdownEditor>(chat).Single(t=>t.Name=="MessageInput");input.Text="保留这条未发送的草稿";for(int i=0;i<3;i++){Mode(chat,false);Check(!All<Button>(chat).Any(b=>b.Name=="ConversationProjectPicker")&&!chat.CanChooseProject,"chat mode retained project header");Mode(chat,true);Check(Picker(chat)&&chat.CanChooseProject,"work mode failed to restore project header");Check(input.Text=="保留这条未发送的草稿","switching lost draft text");}Check(!ProjectCatalogStore.AllConversations(pet.Projects).Any(),"mode switch saved empty conversations");},true);
            Run("reopening the full window in work mode initializes the picker again",delegate(PetWindow pet){for(int repeat=0;repeat<20;repeat++){Open(pet).Close();Pump();Check(Picker(Open(pet)),"reopened work window has no picker at cycle "+repeat);}},true);
            Run("entry switching separates chat and restores the work project",delegate(PetWindow pet){var chat=Open(pet);var project=ProjectCatalogStore.CreateProject(pet.Projects,"previous project",pet.Store.DirectoryPath);var old=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);old.FollowProject(project);chat.RefreshAppearance();chat.UpdateLayout();Pump();Check(All<TextBlock>(chat).Any(t=>t.Text==project.Name),"selected project was not rendered");Mode(chat,false);var personal=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);Check(personal.ProjectId==ProjectCatalogStore.UnassignedProjectId&&!personal.EffectiveSettings().AgentEnabled,"chat draft retained the work project");Mode(chat,true);var work=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",hidden).GetValue(chat);Check(work.ProjectId==project.Id&&Picker(chat),"returning to work failed to restore the selected project");},true);
            Run("saved work records display their project header even when the global preference is chat",delegate(PetWindow pet){var project=ProjectCatalogStore.CreateProject(pet.Projects,"saved work",pet.Store.DirectoryPath);pet.OpenProjectConversation(project,project.Conversations[0]);Pump();var chat=Application.Current.Windows.OfType<ChatWindow>().Single();Check(All<Button>(chat).Any(b=>b.Name=="ConversationProjectPicker"&&b.IsVisible),"saved work record has no project header");Check(chat.WorkEntry&&All<Button>(chat).Any(b=>b.Name=="AgentPermissionPicker"),"saved work entry has no permission control");Check(!chat.CanChooseProject,"saved conversation incorrectly allows changing project");},false);
            app.Shutdown();Console.WriteLine("Conversation mode UI tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
    }
}
