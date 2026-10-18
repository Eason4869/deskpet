using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ConversationSingleWindowSmoke
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static T[] All<T>(DependencyObject root)where T:DependencyObject{var output=new System.Collections.Generic.List<T>();foreach(var item in Walk(root,new System.Collections.Generic.HashSet<DependencyObject>()))if(item is T)output.Add((T)item);return output.ToArray();}static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root,System.Collections.Generic.HashSet<DependencyObject> seen){if(root==null||!seen.Add(root))yield break;yield return root;if(root is Visual){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Walk(VisualTreeHelper.GetChild(root,i),seen))yield return item;}foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in Walk(child,seen))yield return item;}
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"single-window-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var cfg=new PetSettings{AgentEnabled=false,SaveChatHistory=false,GreetingEnabled=false,HideOnFullscreen=false,ReadReplies=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                cfg.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9876/v1",Model="fixture",StreamReplies=false};
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();Pump();
                string projectPath=Path.Combine(root,"project");Directory.CreateDirectory(projectPath);var project=ProjectCatalogStore.CreateProject(pet.Projects,"project",projectPath);pet.ProjectStore.Save(pet.Projects);
                pet.OpenProjectConversation(project,project.Conversations[0]);Pump();
                var first=Application.Current.Windows.OfType<ChatWindow>().SingleOrDefault(w=>w.IsVisible);Check(first!=null && first.ConversationId==project.Conversations[0].Id,"first project conversation opens in the shared chat window");var firstInput=All<MarkdownEditor>(first).Single(b=>b.Name=="MessageInput");firstInput.Text="first draft";
                var second=ProjectCatalogStore.CreateConversation(pet.Projects,project.Id,"second");pet.ProjectStore.Save(pet.Projects);pet.OpenProjectConversation(project,second);Pump();
                var visible=Application.Current.Windows.OfType<ChatWindow>().Where(w=>w.IsVisible).ToArray();Check(visible.Length==1 && Object.ReferenceEquals(first,visible[0]) && visible[0].ConversationId==second.Id && All<MarkdownEditor>(visible[0]).Single(b=>b.Name=="MessageInput").Text=="","switching conversations reuses one visible chat window and isolates drafts");pet.OpenProjectConversation(project,project.Conversations[0]);Pump();Check(Object.ReferenceEquals(first,Application.Current.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible)) && firstInput.Text=="first draft","switching back restores the previous conversation draft in the same window");
                Check(pet.Workspaces.OpenCount==2,"switching labels preserves both conversation workspaces for concurrent work");
                Check(All<Button>(visible[0]).Any(b=>b.Name=="WorkspaceConversation_"+second.Id),"sidebar retains the previous conversation as a selectable label");
                Check(All<Button>(visible[0]).Any(b=>b.Name=="ProjectConversation"),"active conversation label remains in the same sidebar");string secondProjectPath=Path.Combine(root,"second-project");Directory.CreateDirectory(secondProjectPath);var secondProject=ProjectCatalogStore.CreateProject(pet.Projects,"second-project",secondProjectPath);pet.ProjectStore.Save(pet.Projects);pet.OpenProjectConversation(secondProject,secondProject.Conversations[0]);Pump();var projectVisible=Application.Current.Windows.OfType<ChatWindow>().Where(w=>w.IsVisible).ToArray();Check(projectVisible.Length==1 && Object.ReferenceEquals(first,projectVisible[0]) && projectVisible[0].ConversationId==secondProject.Conversations[0].Id && pet.Workspaces.OpenCount==3,"switching projects reuses the same window and preserves all open workspaces");
                foreach(var window in Application.Current.Windows.OfType<Window>().ToArray())window.Close();Pump();
                Console.WriteLine("Conversation single-window smoke: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}finally{if(pet!=null)pet.Close();}
        }
    }
}
