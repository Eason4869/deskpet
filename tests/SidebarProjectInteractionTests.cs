using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class SidebarProjectInteractionTests
    {
        static int passed,failed;static string root;
        [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
        [DllImport("user32.dll",SetLastError=true)]static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")]static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
        static void Check(bool value,string text){if(!value)throw new Exception(text);}
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return(T)node;foreach(object item in LogicalTreeHelper.GetChildren(node)){var child=item as DependencyObject;if(child!=null)foreach(var found in All<T>(child))yield return found;}}
        static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        static Button Project(ProjectSidebar sidebar,string id){return All<Button>(sidebar).Single(b=>(string)b.Tag==id);}
        static ProjectSidebar Sidebar(ProjectCatalog catalog,Action<ProjectDefinition,ConversationDefinition> open=null,Action<ProjectDefinition> activate=null){return new ProjectSidebar(new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json")),catalog,activate??delegate{},open??delegate{},delegate{},delegate{},delegate{});}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
        static string[] OrderedTitles(ProjectSidebar sidebar){return All<Button>(sidebar).Select(b=>b.Content as TextBlock).Where(t=>t!=null&&t.Text.StartsWith("order fixture ")).Select(t=>t.Text).ToArray();}
        static void OpeningKeepsOrder(string section)
        {
            var catalog=new ProjectCatalog();string owner=section=="quick"?ProjectCatalogStore.QuickProjectId:ProjectCatalogStore.UnassignedProjectId;
            if(section=="project"||section=="pinned"){var project=ProjectCatalogStore.CreateProject(catalog,"order project",root);project.Conversations.Clear();owner=project.Id;catalog.SidebarSectionExpansion["recent"]=false;}
            var older=ProjectCatalogStore.CreateConversation(catalog,owner,"order fixture older");older.CreatedUtcTicks=10;older.LastOpenedUtcTicks=10;
            var newer=ProjectCatalogStore.CreateConversation(catalog,owner,"order fixture newer");newer.CreatedUtcTicks=20;newer.LastOpenedUtcTicks=20;
            if(section=="pinned"){older.Pinned=true;newer.Pinned=true;}
            var expected=section=="project"?new[]{older.Name,newer.Name}:new[]{newer.Name,older.Name};
            var store=new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));ProjectSidebar sidebar=null;
            sidebar=new ProjectSidebar(store,catalog,delegate{},delegate(ProjectDefinition p,ConversationDefinition c){c.LastOpenedUtcTicks=100;sidebar.SetActiveConversation(c.Id);sidebar.Refresh();},delegate{},delegate{},delegate{});
            try{Check(OrderedTitles(sidebar).SequenceEqual(expected),"initial fixed order is incorrect");var clicked=All<Button>(sidebar).First(b=>b.Content is TextBlock&&((TextBlock)b.Content).Text==older.Name);clicked.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(older.LastOpenedUtcTicks==100,"opening was not exercised");Check(OrderedTitles(sidebar).SequenceEqual(expected),"opening moved the conversation");store.Save(catalog);var loaded=store.Load();if(section=="project"||section=="pinned")loaded.SidebarSectionExpansion["recent"]=false;using(var reloaded=Sidebar(loaded))Check(OrderedTitles(reloaded).SequenceEqual(expected),"reopening the sidebar changed its order");}finally{sidebar.Dispose();}
        }
        static void ManualOrderPersists(string section,bool blocked=false)
        {
            var catalog=new ProjectCatalog();string owner=section=="quick"?ProjectCatalogStore.QuickProjectId:ProjectCatalogStore.UnassignedProjectId;
            if(section=="project"||section=="pinned"){var project=ProjectCatalogStore.CreateProject(catalog,"order project",root);project.Conversations.Clear();owner=project.Id;catalog.SidebarSectionExpansion["recent"]=false;}
            var a=ProjectCatalogStore.CreateConversation(catalog,owner,"order fixture first");a.CreatedUtcTicks=10;var b=ProjectCatalogStore.CreateConversation(catalog,owner,"order fixture second");b.CreatedUtcTicks=20;if(section=="pinned"){a.Pinned=true;b.Pinned=true;}
            string file=blocked?root:Path.Combine(root,Guid.NewGuid().ToString("N")+".json");var store=new ProjectCatalogStore(file);
            using(var sidebar=new ProjectSidebar(store,catalog,delegate{},delegate{},delegate{},delegate{},delegate{})){
                string group=section=="project"?"project/"+owner:section;var before=OrderedTitles(sidebar);var first=before[0]==a.Name?a:b;var last=first==a?b:a;
                var method=typeof(ProjectSidebar).GetMethod("ReorderConversation",BindingFlags.Instance|BindingFlags.NonPublic);Check(method!=null,"manual conversation ordering is unavailable");
                bool denied=false;try{method.Invoke(sidebar,new object[]{group,last.Id,first.Id,false});}catch(TargetInvocationException){denied=true;}
                if(blocked){Check(denied&&OrderedTitles(sidebar).SequenceEqual(before),"failed save changed visible ordering");sidebar.Refresh();Check(OrderedTitles(sidebar).SequenceEqual(before),"failed save retained unsaved ordering");return;}
                Check(!denied&&OrderedTitles(sidebar).SequenceEqual(before.Reverse()),"drag order did not change");first.LastOpenedUtcTicks=long.MaxValue;sidebar.SetActiveConversation(first.Id);sidebar.Refresh();Check(OrderedTitles(sidebar).SequenceEqual(before.Reverse()),"opening changed manual ordering");
                var loaded=store.Load();if(section=="project"||section=="pinned")loaded.SidebarSectionExpansion["recent"]=false;using(var reloaded=Sidebar(loaded))Check(OrderedTitles(reloaded).SequenceEqual(before.Reverse()),"manual order was not persisted");
            }
        }
        static DragEventArgs DragEvent(IDataObject data,DependencyObject target,Point point,RoutedEvent routed)
        {
            var constructor=typeof(DragEventArgs).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).First(c=>c.GetParameters().Any(p=>p.ParameterType==typeof(IDataObject)));
            var args=constructor.GetParameters().Select(p=>p.ParameterType==typeof(IDataObject)?(object)data:p.ParameterType==typeof(DragDropKeyStates)?DragDropKeyStates.LeftMouseButton:p.ParameterType==typeof(DragDropEffects)?DragDropEffects.Move:p.ParameterType==typeof(DependencyObject)?target:p.ParameterType==typeof(Point)?(object)point:null).ToArray();var result=(DragEventArgs)constructor.Invoke(args);result.RoutedEvent=routed;return result;
        }
        static void CrossProjectDrop()
        {
            var catalog=new ProjectCatalog();var source=ProjectCatalogStore.CreateProject(catalog,"source",root);var target=ProjectCatalogStore.CreateProject(catalog,"target",root);var item=source.Conversations[0];var store=new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));
            using(var sidebar=new ProjectSidebar(store,catalog,delegate{},delegate{},delegate{},delegate{},delegate{})){
                var header=(FrameworkElement)Project(sidebar,target.Id).Parent;var data=new DataObject(ProjectSidebar.DragFormat,source.Id+"/"+item.Id);header.RaiseEvent(DragEvent(data,header,new Point(5,5),System.Windows.DragDrop.DropEvent));
                Check(!source.Conversations.Contains(item)&&target.Conversations.Contains(item),"dropping on another project did not move ownership");var loaded=store.Load();Check(loaded.Projects.Single(p=>p.Id==target.Id).Conversations.Any(c=>c.Id==item.Id),"cross-project move was not saved");
            }
        }
        static void ConversationRowDrop(bool after)
        {
            var catalog=new ProjectCatalog();var a=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"order fixture first");a.CreatedUtcTicks=10;var b=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"order fixture second");b.CreatedUtcTicks=20;
            using(var sidebar=Sidebar(catalog)){
                sidebar.Measure(new Size(320,800));sidebar.Arrange(new Rect(0,0,320,800));sidebar.UpdateLayout();var anchor=after?a:b;var moving=after?b:a;
                var button=All<Button>(sidebar).Single(x=>x.Name=="QuickConversation_"+anchor.Id);var row=(Grid)button.Parent;var data=new DataObject(ProjectSidebar.DragFormat,ProjectCatalogStore.QuickProjectId+"/"+moving.Id);data.SetData("DeskPet.ConversationOrderGroup","quick");var point=new Point(5,after?row.ActualHeight-1:1);
                var over=DragEvent(data,row,point,System.Windows.DragDrop.DragOverEvent);row.RaiseEvent(over);var indicator=All<Border>(row).Single(x=>x.Name=="ConversationDropIndicator");Check(over.Handled&&over.Effects==DragDropEffects.Move&&indicator.Visibility==Visibility.Visible,"row does not accept a conversation drag or show its insertion line");Check(indicator.VerticalAlignment==(after?VerticalAlignment.Bottom:VerticalAlignment.Top),"insertion indicator does not match drop position");
                var drop=DragEvent(data,row,point,System.Windows.DragDrop.DropEvent);row.RaiseEvent(drop);Check(drop.Handled&&OrderedTitles(sidebar).SequenceEqual(new[]{a.Name,b.Name}),"row drop did not insert at the indicated position");
            }
        }
        static void OtherPersonaOrderSurvives()
        {
            var catalog=new ProjectCatalog();var records=Enumerable.Range(1,4).Select(i=>{var c=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"order fixture "+i);c.CreatedUtcTicks=i;c.PetId=i<=2?"one":"two";return c;}).ToArray();catalog.SidebarConversationOrder["quick"]=new List<string>{records[1].Id,records[0].Id,records[2].Id,records[3].Id};
            using(var sidebar=Sidebar(catalog)){sidebar.SetPersona("one");sidebar.Refresh();sidebar.ReorderConversation("quick",records[0].Id,records[1].Id,false);sidebar.SetPersona("two");sidebar.Refresh();Check(OrderedTitles(sidebar).SequenceEqual(new[]{records[2].Name,records[3].Name}),"sorting one persona erased the other persona's manual order");}
        }
        static void NewRecentRemainsVisible()
        {
            var catalog=new ProjectCatalog();var records=Enumerable.Range(1,35).Select(i=>{var c=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.UnassignedProjectId,"order fixture "+i);c.CreatedUtcTicks=i;return c;}).ToArray();
            using(var sidebar=Sidebar(catalog)){sidebar.ReorderConversation("recent",records[33].Id,records[34].Id,false);var created=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.UnassignedProjectId,"order fixture new");created.CreatedUtcTicks=100;sidebar.Refresh();Check(OrderedTitles(sidebar).First()==created.Name,"new conversation disappeared below the recent list limit after manual ordering");}
        }
        static void ChatSections()
        {
            var catalog=new ProjectCatalog();ProjectCatalogStore.CreateProject(catalog,"work folder",root);using(var sidebar=Sidebar(catalog)){
                sidebar.SetEntry(false);Check(All<Button>(sidebar).Any(b=>b.Name=="ToggleSection_default_chat"),"chat mode has no default section");Check(!All<Button>(sidebar).Any(b=>b.Name.StartsWith("WorkspaceProject")||b.Name=="NewProject"),"chat mode exposes project folders");
                var section=sidebar.CreateSection("生活");Check(All<Button>(sidebar).Any(b=>b.Name=="ToggleSection_"+section.Id),"created chat section is invisible");sidebar.RenameSection(section.Id,"随想");Check(All<TextBlock>(sidebar).Any(t=>t.Name=="SidebarSectionTitle"&&t.Text=="随想"),"chat section rename was not rendered");Check(All<Button>(sidebar).Any(b=>b.Name=="NewChatInSection_"+section.Id),"chat section cannot create a conversation");
                sidebar.SetEntry(true);Check(!All<Button>(sidebar).Any(b=>b.Name=="ToggleSection_"+section.Id),"chat section leaked into work mode");var work=sidebar.CreateSection("研发");sidebar.RenameSection(work.Id,"代码");Check(All<TextBlock>(sidebar).Any(t=>t.Name=="SidebarSectionTitle"&&t.Text=="代码"),"work section rename failed");
            }
        }
        static void ChatRecentAndMigration()
        {
            var catalog=new ProjectCatalog();var full=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"full window chat");full.Mode="chat";full.PetId="one";var quick=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"quick window chat");quick.Mode="quick";quick.PetId="one";var foreign=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"other persona");foreign.Mode="chat";foreign.PetId="two";
            var store=new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));store.Save(catalog);var loaded=store.Load();Check(loaded.UnassignedConversations.Any(c=>c.Id==full.Id)&&!loaded.QuickConversations.Any(c=>c.Id==full.Id),"full window chat remained in quick conversations");Check(loaded.QuickConversations.Any(c=>c.Id==quick.Id),"quick window chat lost its quick owner");
            using(var sidebar=Sidebar(loaded)){sidebar.SetPersona("one");sidebar.SetEntry(false);Check(All<Button>(sidebar).Any(b=>b.Name=="RecentConversation_"+full.Id)&&All<Button>(sidebar).Any(b=>b.Name=="RecentConversation_"+quick.Id),"chat recent list omits current persona records");Check(!All<TextBlock>(sidebar).Any(t=>t.Text==foreign.Name),"chat list exposed another persona's record");Check(All<Button>(sidebar).Any(b=>b.Name=="ChatConversation_"+full.Id)&&All<Button>(sidebar).Any(b=>b.Name=="QuickConversation_"+quick.Id),"chat and quick sections did not separate their rows");}
        }
        static void BuiltinSectionMenus()
        {
            var catalog=new ProjectCatalog();var store=new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));using(var sidebar=new ProjectSidebar(store,catalog,delegate{},delegate{},delegate{},delegate{},delegate{})){
                foreach(bool work in new[]{false,true}){sidebar.SetEntry(work);var title=All<Button>(sidebar).Single(b=>b.Name=="ToggleSection_recent");Check(title.ContextMenu!=null&&title.ContextMenu.Items.OfType<MenuItem>().Any(m=>(string)m.Header=="重命名分区")&&title.ContextMenu.Items.OfType<MenuItem>().Any(m=>m.Name=="NewSidebarSection"),"built-in section omits rename/create actions");var rename=typeof(ProjectSidebar).GetMethod("RenameFixedSection",BindingFlags.Instance|BindingFlags.NonPublic);Check(rename!=null,"built-in section rename is unavailable");rename.Invoke(sidebar,new object[]{"recent",work?"工作记录":"聊天记录"});}
            }using(var loaded=Sidebar(store.Load())){loaded.SetEntry(false);Check(All<TextBlock>(loaded).Any(t=>t.Name=="SidebarSectionTitle"&&t.Text=="聊天记录"),"chat section label did not persist");loaded.SetEntry(true);Check(All<TextBlock>(loaded).Any(t=>t.Name=="SidebarSectionTitle"&&t.Text=="工作记录"),"work section label did not persist independently");}
        }
        static void MoveBetweenChatSections()
        {
            var catalog=new ProjectCatalog();var target=ProjectCatalogStore.CreateSection(catalog,"生活","chat");var full=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.UnassignedProjectId,"full chat");full.Mode="chat";full.SectionId=ProjectCatalogStore.DefaultChatSectionId;var quick=ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"quick chat");var store=new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));
            using(var sidebar=new ProjectSidebar(store,catalog,delegate{},delegate{},delegate{},delegate{},delegate{})){sidebar.SetEntry(false);var header=(FrameworkElement)All<Button>(sidebar).Single(b=>b.Name=="ToggleSection_"+target.Id).Parent;var data=new DataObject(ProjectSidebar.DragFormat,ProjectCatalogStore.UnassignedProjectId+"/"+full.Id);header.RaiseEvent(DragEvent(data,header,new Point(3,3),System.Windows.DragDrop.DropEvent));Check(full.SectionId==target.Id&&store.Load().UnassignedConversations.Single(c=>c.Id==full.Id).SectionId==target.Id,"chat section move was not persisted");var quickData=new DataObject(ProjectSidebar.DragFormat,ProjectCatalogStore.QuickProjectId+"/"+quick.Id);header=(FrameworkElement)All<Button>(sidebar).Single(b=>b.Name=="ToggleSection_"+target.Id).Parent;var over=DragEvent(quickData,header,new Point(3,3),System.Windows.DragDrop.DragOverEvent);header.RaiseEvent(over);Check(over.Effects==DragDropEffects.None&&catalog.QuickConversations.Contains(quick),"quick-window chat was allowed into a full-window section");}
        }
        static void WorkProjectsStayInWorkSections()
        {
            var catalog=new ProjectCatalog();var project=ProjectCatalogStore.CreateProject(catalog,"project",root);var chat=ProjectCatalogStore.CreateSection(catalog,"生活","chat");var store=new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));using(var sidebar=new ProjectSidebar(store,catalog,delegate{},delegate{},delegate{},delegate{},delegate{})){sidebar.SetEntry(true);var menu=Project(sidebar,project.Id).ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Name=="MoveProjectSection");Check(!menu.Items.OfType<MenuItem>().Any(m=>Equals(m.Header,chat.Name)),"project move menu exposes chat sections");bool rejected=false;try{sidebar.MoveProjectToSection(project,chat.Id);}catch(ArgumentException){rejected=true;}Check(rejected&&project.SectionId=="projects","project can disappear into a chat section");project.SectionId=chat.Id;store.Save(catalog);Check(store.Load().Projects.Single(p=>p.Id==project.Id).SectionId=="projects","invalid historical project section was not repaired");}
        }
        static void NativeDragGesture(string scenario)
        {
            bool work=scenario=="work"||scenario=="project move",chat=scenario=="chat"||scenario=="section move";var catalog=new ProjectCatalog();ProjectDefinition project=null,targetProject=null;SidebarSection targetSection=null;
            string owner=ProjectCatalogStore.QuickProjectId;if(work){project=ProjectCatalogStore.CreateProject(catalog,"source",root);project.Conversations.Clear();owner=project.Id;}else if(chat)owner=ProjectCatalogStore.UnassignedProjectId;
            if(scenario=="project move")targetProject=ProjectCatalogStore.CreateProject(catalog,"target",root);if(scenario=="section move")targetSection=ProjectCatalogStore.CreateSection(catalog,"生活","chat");
            var records=Enumerable.Range(0,3).Select(i=>{var c=ProjectCatalogStore.CreateConversation(catalog,owner,"drag fixture "+i);c.CreatedUtcTicks=i+1;if(chat){c.Mode="chat";c.SectionId=ProjectCatalogStore.DefaultChatSectionId;}return c;}).ToArray();var moving=records[work?2:0];var anchor=records[work?0:2];
            var store=new ProjectCatalogStore(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"));using(var sidebar=new ProjectSidebar(store,catalog,delegate{},delegate{},delegate{},delegate{},delegate{})){
                sidebar.SetEntry(work);var window=new Window{Content=sidebar,Width=360,Height=740,Left=70,Top=70,Topmost=true,ShowInTaskbar=false,UseLayoutRounding=true};NativePoint original;GetCursorPos(out original);
                try{window.Show();window.Activate();PumpNative(150);string prefix=work?"WorkspaceConversation_":chat?"ChatConversation_":"QuickConversation_";var source=All<Button>(sidebar).Single(b=>b.Name==prefix+moving.Id);Button target=targetProject!=null?Project(sidebar,targetProject.Id):targetSection!=null?All<Button>(sidebar).Single(b=>b.Name=="ToggleSection_"+targetSection.Id):All<Button>(sidebar).Single(b=>b.Name==prefix+anchor.Id);
                    int previewMoves=0,dragOvers=0;source.AddHandler(UIElement.PreviewMouseMoveEvent,new MouseEventHandler(delegate(object sender,MouseEventArgs e){if(e.LeftButton==MouseButtonState.Pressed)previewMoves++;}),true);string lastTrace="";sidebar.AddHandler(System.Windows.DragDrop.DragOverEvent,new DragEventHandler(delegate(object sender,DragEventArgs e){dragOvers++;var node=e.OriginalSource as DependencyObject;string names="";for(int depth=0;node!=null&&depth<8;depth++){var fe=node as FrameworkElement;if(fe!=null&&!String.IsNullOrEmpty(fe.Name))names+="/"+fe.Name;node=VisualTreeHelper.GetParent(node);}string trace=e.Effects+" target="+names+" origin="+e.Data.GetData("DeskPet.ConversationOrderGroup");if(trace!=lastTrace){lastTrace=trace;Console.WriteLine("NATIVE OVER "+trace);}}),true);sidebar.AddHandler(System.Windows.DragDrop.DropEvent,new DragEventHandler(delegate(object sender,DragEventArgs e){Console.WriteLine("NATIVE DROP handled="+e.Handled+" effect="+e.Effects);}),true);var start=source.PointToScreen(new Point(40,source.ActualHeight/2));var finish=target.PointToScreen(new Point(40,targetProject!=null||targetSection!=null?target.ActualHeight/2:2));
                    var input=Task.Run(delegate{if(!SetCursorPos((int)start.X,(int)start.Y))throw new Exception("native pointer denied: "+Marshal.GetLastWin32Error());System.Threading.Thread.Sleep(150);SetCursorPos((int)start.X,(int)start.Y);mouse_event(2,0,0,0,UIntPtr.Zero);System.Threading.Thread.Sleep(100);for(int i=1;i<=6;i++){SetCursorPos((int)(start.X+(finish.X-start.X)*i/6),(int)(start.Y+(finish.Y-start.Y)*i/6));System.Threading.Thread.Sleep(100);}System.Threading.Thread.Sleep(150);SetCursorPos((int)finish.X,(int)finish.Y);System.Threading.Thread.Sleep(100);mouse_event(4,0,0,0,UIntPtr.Zero);});PumpNative(2400);input.GetAwaiter().GetResult();PumpNative(150);
                    var saved=store.Load();if(targetProject!=null)Check(saved.Projects.Single(p=>p.Id==targetProject.Id).Conversations.Any(c=>c.Id==moving.Id)&&!saved.Projects.Single(p=>p.Id==project.Id).Conversations.Any(c=>c.Id==moving.Id),"native cross-project drag did not persist ownership");else if(targetSection!=null)Check(saved.UnassignedConversations.Single(c=>c.Id==moving.Id).SectionId==targetSection.Id,"native cross-section drag did not persist");else{string group=work?"project/"+owner:chat?"chat/"+ProjectCatalogStore.DefaultChatSectionId:"quick";List<string> order;Check(saved.SidebarConversationOrder.TryGetValue(group,out order)&&order.First()==moving.Id,"native drag did not persist order; pressed moves="+previewMoves+", drag-over events="+dragOvers+", groups="+String.Join(",",saved.SidebarConversationOrder.Keys));}
                }finally{mouse_event(4,0,0,0,UIntPtr.Zero);window.Close();SetCursorPos(original.X,original.Y);}
            }
        }
        static void PumpNative(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void HoverKeepsListInteractive(bool projectRow)
        {
            var catalog=new ProjectCatalog();var project=ProjectCatalogStore.CreateProject(catalog,"hover fixture",root);int opened=0;
            var sidebar=Sidebar(catalog,delegate{opened++;});var window=new Window{Content=sidebar,Width=340,Height=740,Left=-10000,Top=0,ShowInTaskbar=false,ShowActivated=false};
            try{window.Show();Pump();var button=projectRow?Project(sidebar,project.Id):All<Button>(sidebar).First(b=>b.Name=="WorkspaceConversation_"+project.Conversations[0].Id);
                button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=UIElement.MouseEnterEvent});Pump();var popup=sidebar.HoverDetails;
                Check(popup.IsOpen,"hover card did not open");Check(Mouse.Captured==null,"hover card captured the mouse and blocks the list");
                button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=UIElement.MouseEnterEvent});Pump();Check(ReferenceEquals(popup,sidebar.HoverDetails)&&popup.IsOpen,"re-entering the same row replaced its hover card");
                if(!projectRow){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(opened==1&&!popup.IsOpen,"conversation did not activate once and close its hover card");}
                else{button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=UIElement.MouseLeaveEvent});var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);Check(!popup.IsOpen,"project card remained open after leaving the row");}
            }finally{sidebar.Dispose();window.Close();Pump();}
        }
        static void NativeSectionHover()
        {
            var catalog=new ProjectCatalog();var project=ProjectCatalogStore.CreateProject(catalog,"Native project",root);using(var sidebar=Sidebar(catalog)){sidebar.SetEntry(true);var window=new Window{Content=sidebar,Width=360,Height=740,Left=70,Top=70,Topmost=true,ShowInTaskbar=false};NativePoint original;if(!GetCursorPos(out original))throw new Exception("cannot read native cursor position");
                try{window.Show();window.Activate();PumpNative(250);var title=All<Button>(sidebar).Single(x=>x.Name=="ToggleSection_recent");var arrow=((StackPanel)title.Content).Children.OfType<FrameworkElement>().Single(x=>x.Name=="SidebarSectionArrow");var outside=window.PointToScreen(new Point(window.ActualWidth-15,window.ActualHeight-15));if(!SetCursorPos((int)outside.X,(int)outside.Y))throw new Exception("native pointer denied: "+Marshal.GetLastWin32Error());PumpNative(250);Check(!title.IsMouseOver&&arrow.Opacity==0,"arrow is hidden before hover");
                    var point=title.PointToScreen(new Point(title.ActualWidth/2,title.ActualHeight/2));if(!SetCursorPos((int)point.X,(int)point.Y))throw new Exception("native pointer denied: "+Marshal.GetLastWin32Error());PumpNative(250);Console.WriteLine("hover="+title.IsMouseOver+" opacity="+arrow.Opacity+" point="+point+" title="+title.ActualWidth+"x"+title.ActualHeight);Check(title.IsMouseOver&&arrow.Opacity==1,"actual pointer hover reveals arrow");var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);using(var file=File.Create(Path.Combine(root,"section-hover.png"))){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(file);}Console.WriteLine("Hover capture: "+Path.Combine(root,"section-hover.png"));
                    if(!SetCursorPos((int)outside.X,(int)outside.Y))throw new Exception("native pointer denied");PumpNative(250);Check(!title.IsMouseOver&&arrow.Opacity==0,"actual pointer leaving hides arrow");
                    Check(!All<Button>(sidebar).Any(b=>b.Name=="RecentConversation_"+project.Conversations[0].Id),"project conversation initially hidden from recent");SetCursorPos((int)point.X,(int)point.Y);PumpNative(150);mouse_event(8,0,0,0,UIntPtr.Zero);mouse_event(16,0,0,0,UIntPtr.Zero);PumpNative(250);Check(title.ContextMenu.IsOpen,"right click opens recent menu");var menuItem=title.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Name=="RecentShowProjects");var menuPoint=menuItem.PointToScreen(new Point(menuItem.ActualWidth/2,menuItem.ActualHeight/2));SetCursorPos((int)menuPoint.X,(int)menuPoint.Y);PumpNative(100);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);PumpNative(250);Check(catalog.RecentShowProjectConversations&&All<Button>(sidebar).Any(b=>b.Name=="RecentConversation_"+project.Conversations[0].Id),"actual menu click shows project conversation");
                }finally{window.Close();SetCursorPos(original.X,original.Y);}}
        }
        [STAThread]public static int Main(string[] args){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sidebar-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            if(args.Contains("--native-hover")){Run("native section arrow hover",NativeSectionHover);app.Shutdown();return failed==0?0:1;}
            if(args.Contains("--native-drag")){foreach(string scenario in args.Length>1?new[]{args[1]}:new[]{"quick","chat","work","project move","section move"}){string current=scenario;Run("native drag: "+current,delegate{NativeDragGesture(current);});}app.Shutdown();Console.WriteLine("Native sidebar drag tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;}
            foreach(string section in new[]{"quick","recent","pinned","project"}){string current=section;Run(current+" conversation order survives opening and reloading",delegate{OpeningKeepsOrder(current);});}
            foreach(string section in new[]{"quick","recent","pinned","project"}){string current=section;Run(current+" manual order persists without following activation time",delegate{ManualOrderPersists(current);});}
            Run("failed manual order save preserves the previous visible order",delegate{ManualOrderPersists("quick",true);});
            Run("conversation drop on another project preserves and saves ownership",CrossProjectDrop);
            Run("dropping above a conversation shows and applies the upper insertion line",delegate{ConversationRowDrop(false);});
            Run("dropping below a conversation shows and applies the lower insertion line",delegate{ConversationRowDrop(true);});
            Run("sorting one persona preserves another persona's saved order",OtherPersonaOrderSurvives);
            Run("new conversations stay visible after sorting a full recent list",NewRecentRemainsVisible);
            Run("chat and work modes both create and rename sections without chat project folders",ChatSections);
            Run("chat recent list and legacy migration distinguish full and quick windows",ChatRecentAndMigration);
            Run("built-in section menus support mode-specific renaming and new sections",BuiltinSectionMenus);
            Run("chat sections accept full-window chats and keep quick-window records separate",MoveBetweenChatSections);
            Run("work project menus and persistence reject chat sections",WorkProjectsStayInWorkSections);
            Run("conversation hover keeps mouse input available for switching",delegate{HoverKeepsListInteractive(false);});
            Run("project hover keeps mouse input available and closes after leaving",delegate{HoverKeepsListInteractive(true);});
            Run("project icon and label share one toggle button",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);var sidebar=Sidebar(c);var button=All<Button>(sidebar).Single(b=>b.Name.StartsWith("WorkspaceProject"));Check(All<FrameworkElement>(button).Any(e=>e.Name=="ProjectFolderGlyph")&&All<TextBlock>(button).Any(t=>t.Text=="project"),"project label and folder are separate controls");Check(!All<Button>(sidebar).Any(b=>b.Name=="ToggleProject"),"separate toggle remains");});
            Run("whole project row toggles folder state without navigating",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"project",root);int activated=0;var sidebar=Sidebar(c,null,delegate{activated++;});var first=Project(sidebar,p.Id);string open=All<FrameworkElement>(first).Single(e=>e.Name=="ProjectFolderGlyph").Tag as string;first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var closed=Project(sidebar,p.Id);Check((string)All<FrameworkElement>(closed).Single(e=>e.Name=="ProjectFolderGlyph").Tag!=open&&!All<Button>(sidebar).Any(b=>b.Name.StartsWith("WorkspaceConversation_")),"row did not collapse or change folder icon");closed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(All<Button>(sidebar).Any(b=>b.Name.StartsWith("WorkspaceConversation_"))&&activated==0,"toggle navigated or failed to expand");});
            Run("opening a child and rebuilding navigation preserves expansion",delegate{var c=new ProjectCatalog();var p=ProjectCatalogStore.CreateProject(c,"default",root);ProjectSidebar remounted=null;var sidebar=Sidebar(c,delegate{remounted=Sidebar(c);});All<Button>(sidebar).Single(b=>b.Name=="WorkspaceConversation_"+p.Conversations[0].Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(remounted!=null&&All<Button>(remounted).Any(b=>b.Name=="WorkspaceConversation_"+p.Conversations[0].Id),"opening child collapsed its parent after navigation remount");});
            Run("section creation is inside more menus and header actions start hidden",delegate{var c=new ProjectCatalog();var sidebar=Sidebar(c);Check(!All<Button>(sidebar).Any(b=>b.Name=="NewSidebarSection"),"standalone section creation remains");foreach(var more in All<Button>(sidebar).Where(b=>b.Name.StartsWith("SectionMore_"))){Check(more.Opacity==0&&!more.IsHitTestVisible,"more action remains permanently visible");Check(more.ContextMenu.Items.OfType<MenuItem>().Any(m=>m.Name=="NewSidebarSection"),"section more menu omits new section");}Check(All<Button>(sidebar).Where(b=>b.Name=="NewProject"||b.Name.StartsWith("NewProjectInSection_")).All(b=>b.Opacity==0&&!b.IsHitTestVisible),"add project actions remain permanently visible");});
            Run("section hover reveals and hides its lightweight actions",delegate{var sidebar=Sidebar(new ProjectCatalog());var more=All<Button>(sidebar).First(b=>b.Name.StartsWith("SectionMore_"));var header=(FrameworkElement)more.Parent;header.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=UIElement.MouseEnterEvent});Check(more.Opacity==1&&more.IsHitTestVisible,"hover did not reveal the actions");header.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=UIElement.MouseLeaveEvent});Check(more.Opacity==0&&!more.IsHitTestVisible,"leaving header did not hide its actions");});
            Run("sidebar brand includes the application logo beside DeskPet",delegate{var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);var sample=ProjectCatalogStore.CreateProject(pet.Projects,"deskpet",root);sample.Conversations[0].Name="审查 DeskPet dev 分支";try{using(var navigation=new AgentWorkspaceNavigation(pet,delegate{})){Check(All<Image>(navigation).Any(i=>i.Name=="WorkspaceAppLogo"&&i.Source!=null)&&All<TextBlock>(navigation).Any(t=>t.Text=="DeskPet"),"application logo missing");navigation.Measure(new Size(252,800));navigation.Arrange(new Rect(0,0,252,800));navigation.UpdateLayout();var bitmap=new RenderTargetBitmap(252,800,96,96,PixelFormats.Pbgra32);bitmap.Render(navigation);using(var stream=File.Create(Path.Combine(root,"sidebar.png"))){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(stream);}Console.WriteLine("Sidebar preview: "+Path.Combine(root,"sidebar.png"));}}finally{pet.Close();}});
            app.Shutdown();Console.WriteLine("Sidebar project interaction tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
    }
}
