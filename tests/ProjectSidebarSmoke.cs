using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace Dafeiyu.Tests
{
    public static class ProjectSidebarSmoke
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        // Hover close is debounced so the pointer can cross into the card; the
        // test has to let that timer fire before asserting the popup is gone.
        static void Pump(int ms){var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        [STAThread] public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sidebar-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var workspace=Path.Combine(root,"workspace");Directory.CreateDirectory(workspace);
                var store=new ProjectCatalogStore(Path.Combine(root,"projects.json"));var catalog=ProjectCatalogStore.MigrateLegacy(new PetSettings{AgentWorkspace=workspace});store.Save(catalog);
                int renamed=0;var sidebar=new ProjectSidebar(store,catalog,delegate{},delegate{},delegate{},delegate{},delegate{},delegate(ProjectDefinition p,ConversationDefinition c){renamed++;c.Name="Renamed chat";c.NameEdited=true;},delegate(ProjectDefinition p){},delegate(ProjectDefinition p){},delegate(ProjectDefinition p){},delegate(ProjectDefinition p){},delegate(ProjectDefinition p,ConversationDefinition c){},delegate(ProjectDefinition p,ConversationDefinition c){},delegate(ProjectDefinition p,ConversationDefinition c){});sidebar.Measure(new Size(320,800));sidebar.Arrange(new Rect(0,0,320,800));
                var popup=sidebar.HoverDetails;Check(popup!=null && !popup.IsOpen,"ProjectHoverDetails is hidden by default");
                var row=All<Button>(sidebar).First(b=>b.Name=="WorkspaceProject");row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=UIElement.MouseEnterEvent});Check(popup.IsOpen && (popup.Child as FrameworkElement)!=null,"ProjectHoverDetails appears on hover");Check(popup.Child is Border && ((Border)popup.Child).Background==Brushes.White && popup.Placement==PlacementMode.Right,"project hover details use a stable white side popup");
                row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=UIElement.MouseLeaveEvent});Pump(220);Check(!popup.IsOpen,"ProjectHoverDetails hides when hover ends");
                Check(All<Button>(sidebar).Any(b=>b.Name=="NewProject"),"sidebar exposes a new project action");sidebar.SetActiveConversation(catalog.Projects[0].Conversations[0].Id);sidebar.Refresh();Check(All<TextBlock>(sidebar).Any(t=>t.Name=="ProjectConversationCount"&&t.Text=="1") && All<Border>(sidebar).Any(b=>b.Name=="ActiveConversationDot"),"project labels show conversation count and active state");var projectButton=All<Button>(sidebar).Single(b=>b.Name=="WorkspaceProject");Check(projectButton.ContextMenu!=null && projectButton.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="重命名项目") && projectButton.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="归档项目") && projectButton.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="永久删除项目"),"project label exposes rename, archive and delete actions");var chatButton=All<Button>(sidebar).Single(b=>b.Name=="ProjectConversation");Check(chatButton.ContextMenu!=null && chatButton.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="重命名") && chatButton.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="归档") && chatButton.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="永久删除"),"conversation label exposes rename, archive and delete actions");var rename=chatButton.ContextMenu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="重命名");rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));sidebar.Refresh();Check(renamed==1 && catalog.Projects[0].Conversations[0].Name=="Renamed chat" && catalog.Projects[0].Conversations[0].NameEdited,"conversation title is manually editable from the sidebar");var toggle=All<Button>(sidebar).Single(b=>b.Name=="ToggleProject");toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!All<Button>(sidebar).Any(b=>b.Name=="ProjectConversation"),"project label collapses its conversation tabs");toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(All<Button>(sidebar).Any(b=>b.Name=="ProjectConversation"),"project label expands its conversation tabs");
                string second=Path.Combine(root,"second");Directory.CreateDirectory(second);sidebar.CreateProject("second",second);Check(catalog.Projects.Count==2,"project creation API is available for the UI");
                string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);var failing=new ProjectSidebar(new ProjectCatalogStore(blocked),catalog,delegate{},delegate{},delegate{},delegate{},delegate{});bool failed=false;try{failing.CreateProject("must-not-persist",second);}catch(Exception){failed=true;}Check(failed && catalog.Projects.Count==2,"ProjectCreationFailureIsRecoverable keeps the existing catalog unchanged");
                var dialog=new NewProjectDialog();Check(dialog.Content is Grid && dialog.Width==520 && dialog.Height==332,"new project dialog uses the aligned workspace layout");Check(dialog.ProjectName=="" && dialog.Workspace=="","new project dialog starts with empty name and source folder");dialog.Close();Console.WriteLine("Project sidebar smoke: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
