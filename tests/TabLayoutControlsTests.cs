using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Automation;

namespace Dafeiyu.Tests
{
    public static class TabLayoutControlsTests
    {
        sealed class RetainedTool : Border,IDisposable {internal int Disposals;public void Dispose(){Disposals++;}}
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static string GeometryOf(FrameworkElement image){var view=image as Viewbox;Check(view!=null && view.Child is Canvas,"toolbar icon uses the shared canvas renderer");var canvas=(Canvas)view.Child;Check(canvas.Width==24 && canvas.Height==24,"toolbar icon has a 24-unit canvas");return ((System.Windows.Shapes.Path)canvas.Children[0]).Data.ToString();}
        static object Field(ChatWindow window,string name){return typeof(ChatWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        [STAThread]public static int Main(string[] args)
        {
            Application app=null;PetWindow pet=null;ChatWindow window=null;
            try
            {
                string split=GeometryOf(ShellIcons.Image("split-panels")),expand=GeometryOf(ShellIcons.Image("expand-tabs")),collapse=GeometryOf(ShellIcons.Image("collapse-tools"));
                Check(split!=collapse && split!=expand && expand!=collapse,"split, full-view and hide-tools actions have distinct icon shapes");
                if(args.Contains("--icons-only"))return 0;
                app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};string root=System.IO.Path.Combine(Directory.GetCurrentDirectory(),"work","tab-layout-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(System.IO.Path.Combine(root,"settings.json")),null);var project=new ProjectDefinition{Id="layout-fixture",Name="布局测试",Workspace=root};var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="布局测试",Mode="work"};window=new ChatWindow(pet,pet.Workspaces.CreateWorkspace(project,definition)){Width=1600,Height=850};window.Show();Pump();window.OpenBlankTab();Pump();
                var area=(PreviewWorkspace)Field(window,"filePreview");var tool=new RetainedTool{Child=new TextBlock{Text="retained process fixture"}};area.AddPage("retained tool","terminal",tool);Pump();
                var full=(Button)Field(window,"fullTabToggle");var hide=(Button)Field(window,"sideTabsToggle");var dock=(ContentControl)Field(window,"filePreviewDock");var surface=(Grid)Field(window,"conversationSurface");var editor=(MarkdownEditor)Field(window,"input");var shell=(Grid)Field(window,"workspaceShell");editor.Text="retained layout draft";int count=area.ToolCount;
                Check(!area.HasConversation && surface.IsVisible && dock.IsVisible,"initial split displays both main chat and right tools");
                Click(full);Check(area.HasConversation,"layout button enters full tabs");Check(GeometryOf((FrameworkElement)full.Content)!=GeometryOf((FrameworkElement)hide.Content),"return-to-split and hide-tools buttons show different icons");
                Check(AutomationProperties.GetName(full)==(string)full.ToolTip && AutomationProperties.GetName(hide)==(string)hide.ToolTip,"button accessibility names reflect the current actions");
                Click(full);Check(!area.HasConversation && surface.IsVisible && dock.IsVisible && Object.ReferenceEquals(surface.Parent,shell) && Grid.GetColumn(surface)==2,"return-to-split restores main chat alongside visible tools");
                Check(Object.ReferenceEquals(area.Selected,tool) && editor.Text=="retained layout draft","layout switching preserves tool selection and draft");
                Click(hide);Check(!dock.IsVisible && surface.IsVisible && shell.ColumnDefinitions[6].ActualWidth<1 && tool.Disposals==0,"hide-tools collapses the entire right area without disposing its content");
                Check(((FrameworkElement)hide.Content).RenderTransform is ScaleTransform && ((ScaleTransform)((FrameworkElement)hide.Content).RenderTransform).ScaleX==-1,"show-tools action points back into the workspace");
                Click(hide);Check(dock.IsVisible && !area.HasConversation && Object.ReferenceEquals(area.Selected,tool) && area.ToolCount==count,"show-tools restores split layout and existing selection");
                Click(full);area.SelectConversation();Click(hide);Click(hide);Check(area.HasConversation && Object.ReferenceEquals(area.Selected,surface) && editor.Text=="retained layout draft" && tool.Disposals==0,"hide and show preserve full layout and selected main tab");
                Click(hide);Click(full);Check(dock.IsVisible && !area.HasConversation && surface.IsVisible,"return-to-split while tools are hidden restores both panes");
                Click(full);Check(Object.ReferenceEquals(area.Selected,tool),"entering full tabs after an explicit split return retains the selected tool");Click(full);
                window.Width=850;Pump();Check(!full.IsEnabled && !((string)full.ToolTip).Contains("返回分屏"),"narrow full-layout control explains its disabled action");
                bool before=(bool)Field(window,"fullTabView");window.ToggleFullTabView();Pump();Check((bool)Field(window,"fullTabView")==before,"narrow layout command cannot bypass disabled control");
                // The existing narrow back action changes previewVisible without changing tabsHidden.
                typeof(ChatWindow).GetField("previewVisible",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,false);typeof(ChatWindow).GetMethod("RefreshWorkspaceLayout",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);Pump();Check(!dock.IsVisible,"narrow main-chat return hides the tools");Click(hide);Check(dock.IsVisible,"show-tools uses actual visibility after narrow main-chat return");
                Check(tool.Disposals==0 && editor.Text=="retained layout draft","all toolbar actions preserve live tool instances and the draft");
                Console.WriteLine("Tab layout controls tests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}
            finally{if(window!=null)window.Close();if(pet!=null)pet.Close();if(app!=null)app.Shutdown();}
        }
    }
}
