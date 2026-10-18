using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class SidebarActionTests
    {
        static int passed,failed;static string root;
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return(T)node;foreach(object item in LogicalTreeHelper.GetChildren(node)){var child=item as DependencyObject;if(child!=null)foreach(var found in All<T>(child))yield return found;}}
        static void Check(bool value,string text){if(!value)throw new Exception(text);}
        static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        static Button Find(ProjectSidebar sidebar,string name){return All<Button>(sidebar).First(b=>b.Name==name);}
        static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static ProjectCatalogStore Store(){return new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));}
        static ProjectSidebar Sidebar(ProjectCatalog c,ProjectCatalogStore store,Action<ProjectDefinition> create=null,Action<ProjectDefinition,ConversationDefinition> archive=null){return new ProjectSidebar(store,c,delegate{},delegate{},delegate{},delegate{},create??delegate{},null,null,null,null,null,archive);}
        [STAThread]public static int Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sidebar-actions-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            Run("section heading collapses and expands its projects",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);var s=Sidebar(c,Store());Click(Find(s,"ToggleSection_projects"));Check(!All<Button>(s).Any(b=>(string)b.Tag==p.Id),"section did not collapse");Click(Find(s,"ToggleSection_projects"));Check(All<Button>(s).Any(b=>(string)b.Tag==p.Id),"section did not expand");});
            Run("section plus creates a project in that section",delegate{var c=new ProjectCatalog();var section=ProjectCatalogStore.CreateSection(c,"custom");var s=Sidebar(c,Store());Exception failure=null;Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{var dialog=Application.Current.Windows.OfType<NewProjectDialog>().Single();try{All<TextBox>(dialog).Single(t=>t.Name=="NewProjectName").Text="section project";typeof(NewProjectDialog).GetField("workspacePath",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(dialog,root);Click(All<Button>(dialog).Single(b=>b.Name=="CreateProject"));}catch(Exception e){failure=e;dialog.Close();}}));Click(Find(s,"NewProjectInSection_"+section.Id));if(failure!=null)throw failure;Check(c.Projects.Single().SectionId==section.Id,"plus added the project to a different section");});
            Run("project hover actions expose more and compose without toggling",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);int created=0;var s=Sidebar(c,Store(),delegate(ProjectDefinition owner){Check(owner.Id==p.Id,"wrong project for draft");created++;});var compose=Find(s,"NewProjectConversation_"+p.Id);var more=Find(s,"ProjectMore_"+p.Id);Check(compose.Opacity==0&&more.Opacity==0,"project actions are permanently visible");Click(compose);Check(created==1&&All<Button>(s).Any(b=>b.Name=="WorkspaceConversation_"+p.Conversations[0].Id),"compose toggled project expansion");Check(more.ContextMenu!=null&&more.ContextMenu.Items.OfType<MenuItem>().Any(m=>m.Name=="PinProject"),"project menu omits pin");});
            Run("pinning a conversation persists and moves its display to pinned section",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);var chat=p.Conversations[0];var store=Store();var s=Sidebar(c,store);Click(Find(s,"PinConversation_"+chat.Id));Check(chat.Pinned&&p.Conversations.Contains(chat)&&All<Button>(s).Any(b=>b.Name=="PinnedConversation_"+chat.Id),"pin changed ownership or omitted pinned group");Check(!All<Button>(s).Any(b=>b.Name=="WorkspaceConversation_"+chat.Id||b.Name=="RecentConversation_"+chat.Id),"pinned chat remains duplicated in normal groups");Check(store.Load().Projects[0].Conversations[0].Pinned,"pin not persisted");Click(Find(s,"PinConversation_"+chat.Id));Check(!chat.Pinned&&All<Button>(s).Any(b=>b.Name=="WorkspaceConversation_"+chat.Id),"unpin failed to restore original location");});
            Run("archive hover action targets its own conversation",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);var chat=p.Conversations[0];int archived=0;var s=Sidebar(c,Store(),null,delegate(ProjectDefinition owner,ConversationDefinition item){Check(owner.Id==p.Id&&item.Id==chat.Id,"archive targeted a different conversation");item.Archived=true;archived++;});Click(Find(s,"ArchiveConversation_"+chat.Id));Check(archived==1&&!All<Button>(s).Any(b=>b.Name=="WorkspaceConversation_"+chat.Id),"archive did not remove active row");});
            Run("pin save failure preserves previous state",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);var s=Sidebar(c,new ProjectCatalogStore(root));bool denied=false;try{s.ToggleConversationPin(p.Conversations[0]);}catch(Exception){denied=true;}Check(denied&&!p.Conversations[0].Pinned,"pin storage failure changed in-memory state");});
            Run("project pin is reversible and preserves section membership",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);var store=Store();var s=Sidebar(c,store);s.ToggleProjectPin(p);Check(p.Pinned&&p.SectionId=="projects"&&store.Load().Projects[0].Pinned,"pin changed original section");s.ToggleProjectPin(p);Check(!p.Pinned&&p.SectionId=="projects","unpin lost original section");});
            app.Shutdown();Console.WriteLine("Sidebar action tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
    }
}
