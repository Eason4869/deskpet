using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ConversationArchiveRegressionTests
    {
        static int checks;
        static readonly List<Exception> dispatcherErrors=new List<Exception>();
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static void Pump()
        {
            var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
            timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
            if(dispatcherErrors.Count>0)throw new AggregateException(dispatcherErrors);
        }
        static void Until(Func<bool> done){var watch=Stopwatch.StartNew();while(!done()&&watch.Elapsed.TotalSeconds<5)Pump();Check(done(),"conversation navigation completes without a dispatcher error");Pump();}
        static void ArchiveButton(ChatWindow chat,ConversationDefinition conversation){All<Button>(chat).First(b=>b.Name=="ArchiveConversation_"+conversation.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject
        {
            if(root is T)yield return (T)root;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;
        }
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs args){dispatcherErrors.Add(args.Exception);args.Handled=true;};
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"archive-regression-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var settings=new PetSettings{AgentEnabled=true,AgentWorkspace=root,AutoUpdate=false,SaveChatHistory=true,GreetingEnabled=false,ShowPredictedMessages=false,ReadReplies=false,OnboardingState="completed"};
                foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                settings.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1",Model="fixture",StreamReplies=false};
                var pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);
                var project=ProjectCatalogStore.CreateProject(pet.Projects,"archive fixture",root);var first=project.Conversations[0];
                pet.ProjectStore.Save(pet.Projects);
                pet.OpenProjectConversation(project,first);Pump();var chat=app.Windows.OfType<ChatWindow>().Single();chat.UpdateLayout();Pump();
                var firstWorkspace=pet.Workspaces.OpenWorkspaces.Single(w=>w.Id==first.Id);
                firstWorkspace.Session.History.Commit("fixture archive history","retained reply");
                ArchiveButton(chat,first);
                Check(first.Archived&&chat.ConversationId!=first.Id&&pet.Workspaces.OpenWorkspaces.Any(w=>w.Id==chat.ConversationId&&w.IsDraft),"archiving last sidebar conversation switches to a live draft");
                Check(firstWorkspace.IsDisposed&&!pet.Workspaces.OpenWorkspaces.Any(w=>w.Id==first.Id),"archived current workspace closes and leaves a usable view");
                Check(!All<Button>(chat).Any(b=>b.Name=="ArchiveConversation_"+first.Id),"archived conversation disappears from sidebar");
                Check(Directory.Exists(firstWorkspace.DataDirectory),"archive preserves existing conversation directory");
                Check(pet.ProjectStore.Load().Projects.Single(p=>p.Id==project.Id).Conversations.Single(c=>c.Id==first.Id).Archived,"archive is persisted");
                pet.RestoreConversation(project,first);pet.OpenProjectConversation(project,first);Until(()=>chat.ConversationId==first.Id);
                var restored=pet.Workspaces.OpenWorkspaces.Single(w=>w.Id==first.Id);
                Check(restored.Session.History.Messages.Any(m=>m.Content=="fixture archive history")&&restored.Session.History.Messages.Any(m=>m.Content=="retained reply"),"restore preserves both saved messages");
                var second=ProjectCatalogStore.CreateConversation(pet.Projects,project.Id,"second");pet.ProjectStore.Save(pet.Projects);pet.OpenProjectConversation(project,second);Until(()=>chat.ConversationId==second.Id);
                var secondWorkspace=pet.Workspaces.OpenWorkspaces.Single(w=>w.Id==second.Id);pet.OpenProjectConversation(project,first);Until(()=>chat.ConversationId==first.Id);
                var input=All<MarkdownEditor>(chat).First(b=>b.Name=="MessageInput");input.Text="keep active draft";
                var background=All<Button>(chat).First(b=>b.Name=="WorkspaceConversation_"+second.Id);
                background.ContextMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header=="归档").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
                Check(second.Archived&&secondWorkspace.IsDisposed&&chat.ConversationId==first.Id&&input.Text=="keep active draft","archiving a background conversation preserves the current conversation and draft");
                pet.RestoreConversation(project,second);ArchiveButton(chat,first);Until(()=>chat.ConversationId==second.Id);
                Check(first.Archived&&restored.IsDisposed&&!second.Archived,"archiving current conversation selects another live conversation");
                var active=pet.Workspaces.OpenWorkspaces.Single(w=>w.Id==second.Id);pet.ArchiveProject(project);Pump();
                Check(project.Archived&&active.IsDisposed&&chat.ConversationId!=second.Id&&pet.Workspaces.OpenWorkspaces.Any(w=>w.Id==chat.ConversationId&&w.IsDraft),"archiving the current project switches to a live draft");
                chat.SwitchEntry(false);Pump();Check(!chat.WorkEntry&&pet.Workspaces.OpenWorkspaces.Any(w=>w.Id==chat.ConversationId&&w.Session!=null),"chat entry after archive uses a live workspace");
                chat.SwitchEntry(true);Pump();Check(chat.WorkEntry&&pet.Workspaces.OpenWorkspaces.Any(w=>w.Id==chat.ConversationId&&w.Session!=null),"work entry after archive does not revisit a disposed workspace");
                chat.Close();Pump();pet.Close();Pump();
                Check(dispatcherErrors.Count==0,"closing the conversation window after archives does not throw a second exception");
                Console.WriteLine("Conversation archive regression: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{foreach(var window in app.Windows.OfType<Window>().ToArray())try{window.Close();}catch(Exception error){Console.Error.WriteLine("Close failure: "+error);}app.Shutdown();}
        }
    }
}
