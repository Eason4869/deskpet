using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    public static class ConversationViewportTests
    {
        static Application app;static PetWindow pet;static ChatWindow full;static int checks;
        static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static object Field(string name){return typeof(ChatWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(full);}
        static async Task Settle(){await Task.Delay(150);full.UpdateLayout();}
        static void Click(string name){All<Button>(full).Single(b=>b.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static double Left(FrameworkElement element){return element.TranslatePoint(new Point(0,0),full).X;}
        static double Right(FrameworkElement element){return element.TranslatePoint(new Point(element.ActualWidth,0),full).X;}
        static double Center(FrameworkElement element){return element.TranslatePoint(new Point(element.ActualWidth/2,0),full).X;}
        static Rect Bounds(FrameworkElement element){return new Rect(element.TranslatePoint(new Point(0,0),full),new Size(element.ActualWidth,element.ActualHeight));}
        static bool Overlaps(FrameworkElement first,FrameworkElement second){var overlap=Rect.Intersect(Bounds(first),Bounds(second));return !overlap.IsEmpty&&overlap.Width>1&&overlap.Height>1;}
        static void CheckToolbar(string label,bool split)
        {
            var summary=All<Button>(full).Single(b=>b.Name=="ToggleWorkspaceSummary");var expand=All<Button>(full).Single(b=>b.Name=="ToggleFullTabView");var hide=All<Button>(full).Single(b=>b.Name=="ToggleSideTabs");
            Check(summary.IsVisible&&expand.IsVisible&&hide.IsVisible,label+" keeps summary, full-view and hide controls accessible");
            Check(!Overlaps(summary,expand)&&!Overlaps(summary,hide)&&!Overlaps(expand,hide),label+" keeps main summary and tool-view controls from overlapping");
            if(split){var divider=All<Thumb>(full).Single(t=>t.Name=="PreviewDivider");double gap=Left(divider)-Right(summary);Check(divider.IsVisible&&gap>=-1&&gap<=48,label+" anchors summary control at the main region right edge before the tool divider (gap "+gap.ToString("F1")+"px)");Check(Left(expand)>=Left(divider)&&Left(hide)>=Left(divider),label+" keeps full-view and hide controls in the right tool region");}
        }
        static ScrollBar VerticalBar(ScrollViewer history)
        {
            var bar=history.Template.FindName("PART_VerticalScrollBar",history) as ScrollBar;
            return bar??All<ScrollBar>(history).First(b=>b.Orientation==Orientation.Vertical&&b.IsVisible);
        }
        static void CheckRightEdge(ScrollViewer history,string label)
        {
            var divider=All<Thumb>(full).Single(t=>t.Name=="PreviewDivider");var bar=VerticalBar(history);double boundary=Left(divider),viewerGap=boundary-Right(history),barGap=boundary-Right(bar);
            Check(divider.IsVisible&&history.IsVisible&&bar.IsVisible,label+" uses the actual visible history viewer and vertical scrollbar");
            Check(Math.Abs(viewerGap)<8,label+" history viewer reaches the preview divider (gap "+viewerGap.ToString("F1")+"px)");
            Check(Math.Abs(barGap)<8,label+" actual vertical scrollbar reaches the preview divider (gap "+barGap.ToString("F1")+"px)");
        }
        static async Task CheckJump(ScrollViewer history,FrameworkElement composer,MarkdownEditor editor,string draft,string label)
        {
            var jump=All<Button>(full).Single(b=>b.Name=="JumpToLatestMessage");history.ScrollToEnd();await Settle();
            Check(history.ScrollableHeight-history.VerticalOffset<2&&!jump.IsVisible,label+" hides jump-to-latest at the latest reply");
            history.ScrollToVerticalOffset(Math.Max(0,history.ScrollableHeight-450));await Settle();
            Check(jump.IsVisible&&history.ScrollableHeight-history.VerticalOffset>120,label+" shows jump-to-latest when reading older history");
            Check(Math.Abs(Center(jump)-Center(composer))<=8,label+" centers jump-to-latest over the readable composer column");
            jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
            Check(history.ScrollableHeight-history.VerticalOffset<2&&!jump.IsVisible,label+" jump button scrolls to the latest reply and hides");
            Check(editor.Text==draft,label+" jump button preserves the unsent draft");
        }
        static void Capture(string name)
        {
            full.UpdateLayout();var content=(FrameworkElement)full.Content;var bounds=new Rect(0,0,content.ActualWidth,content.ActualHeight);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(Brushes.White,null,bounds);drawing.DrawRectangle(new VisualBrush(content),null,bounds);}var bitmap=new RenderTargetBitmap((int)Math.Ceiling(bounds.Width),(int)Math.Ceiling(bounds.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine("work","permissions",name)))encoder.Save(file);
        }
        static void CheckRetained(ScrollViewer history,ConversationSession session,MarkdownEditor editor,string draft,int count)
        {
            Check(ReferenceEquals(Field("scroll"),history)&&ReferenceEquals(Field("session"),session)&&ReferenceEquals(Field("input"),editor),"viewport changes retain the original history, session and editor instances");
            Check(editor.Text==draft&&session.History.Messages.Count==count&&!session.IsBusy,"viewport changes preserve the draft and local history without submitting requests");
        }
        static async Task Run()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","viewport-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"README.md");File.WriteAllText(path,"# 文件预览\n\n右侧工具与主聊天保留在同一窗口。\n\n- 聊天滚动条应位于工具分隔线旁。\n- 空间不足时，摘要自动收起。\n\n```csharp\nvar view = \"原始聊天控件\";\n```\n");
            var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=root,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"data","settings.json")),null);
            var project=new ProjectDefinition{Id="viewport-fixture",Name="视口布局",Workspace=root};var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="检查滚动条、阅读宽度与摘要",Mode="work"};var workspace=pet.Workspaces.CreateWorkspace(project,definition);var session=workspace.Session;
            for(int i=1;i<=12;i++)session.History.Commit("第 "+i+" 个问题：整理今天的工作。","这是第 "+i+" 条本地历史回复。正文保持独立的阅读宽度，滚动条位于整个主区域最右侧。\n\n即使摘要面板显示，滚动条也应靠近右侧工具分隔线；靠近底部的按钮可随时返回最新回复。\n\n```csharp\nvar message = "+i+";\nConsole.WriteLine(message);\n```");
            int historyCount=session.History.Messages.Count;full=new ChatWindow(pet,workspace){Width=1780,Height=920};full.Show();await Settle();full.OpenFilePreview(path);await Settle();
            var history=All<ScrollViewer>(full).Single(s=>s.Name=="ChatHistoryScroll");var inspector=All<Border>(full).Single(b=>b.Name=="AgentWorkspaceInspector");var editor=(MarkdownEditor)Field("input");var composer=(FrameworkElement)Field("composer");var area=(PreviewWorkspace)Field("filePreview");var markdown=(FilePreviewPane)area.Selected;await markdown.Ready;const string draft="这是保留在主聊天里的未发送草稿。";editor.Text=draft;
            Check(historyCount==24&&history.ScrollableHeight>1000,"fixture displays real local messages and an actually scrollable history");Check(!inspector.IsVisible,"1780px split layout prioritizes readable chat and hides the fixed summary");CheckRightEdge(history,"1780px split");
            CheckToolbar("1780px auto-collapsed summary",true);
            var tools=area.Background as SolidColorBrush;var left=All<Border>(full).Single(b=>b.Name=="AgentWorkspaceSidebar").Background as SolidColorBrush;Check(tools!=null&&tools.Color==Color.FromRgb(0xF8,0xF9,0xFB)&&left!=null&&tools.Color==left.Color,"right tools use the same F8F9FB background as the left sidebar");
            await CheckJump(history,composer,editor,draft,"split view");history.ScrollToVerticalOffset(260);await Settle();Capture("viewport-split.png");
            full.Width=2400;await Settle();All<Thumb>(full).Single(t=>t.Name=="PreviewDivider").RaiseEvent(new DragDeltaEventArgs(160,0){RoutedEvent=Thumb.DragDeltaEvent});await Settle();Check(inspector.IsVisible,"2400px layout restores the fixed summary when the readable column has room");Console.WriteLine("INFO viewport dimensions: window="+full.ActualWidth+" surface="+((FrameworkElement)Field("conversationSurface")).ActualWidth+" history="+history.ActualWidth+" messages="+((FrameworkElement)Field("messages")).ActualWidth+" margin="+((FrameworkElement)Field("messages")).Margin);Check(((FrameworkElement)Field("messages")).ActualWidth>=679,"wide summary layout retains at least 680px of readable message width");CheckRightEdge(history,"2400px summary layout");Check(Right(VerticalBar(history))>Right(inspector)+1,"actual history scrollbar stays to the right of the fixed summary card");
            CheckToolbar("2400px fixed summary",true);
            await CheckJump(history,composer,editor,draft,"wide summary view");history.ScrollToVerticalOffset(260);await Settle();Capture("viewport-wide.png");
            Click("ToggleWorkspaceSummary");await Settle();Check(!inspector.IsVisible,"explicit summary hide removes the fixed card");full.Width=2460;await Settle();Check(!inspector.IsVisible,"resizing does not override an explicitly hidden summary");CheckRightEdge(history,"explicitly hidden summary");
            Click("ToggleSideTabs");await Settle();Check(!((ContentControl)Field("filePreviewDock")).IsVisible&&ReferenceEquals(Field("filePreview"),area)&&ReferenceEquals(area.Selected,markdown),"hiding tool pane retains its workspace and selected Markdown view");CheckToolbar("hidden tool pane",false);var hideControl=All<Button>(full).Single(b=>b.Name=="ToggleSideTabs");double hiddenGap=Right((FrameworkElement)Field("conversationSurface"))-Right(hideControl);Check(hiddenGap>=-1&&hiddenGap<=30,"hidden-pane restore control stays at the visible main region right edge (gap "+hiddenGap.ToString("F1")+"px)");Click("ToggleSideTabs");await Settle();Check(((ContentControl)Field("filePreviewDock")).IsVisible&&ReferenceEquals(Field("filePreview"),area)&&ReferenceEquals(area.Selected,markdown),"visible hide control restores the same tool pane and view");CheckToolbar("restored tool pane",true);
            double offset=history.VerticalOffset;full.ToggleFullTabView();await Settle();area.SelectConversation();await Settle();Check(area.HasConversation&&ReferenceEquals(area.Selected,Field("conversationSurface")),"full tabs show the existing pinned main conversation");Check(Math.Abs(history.VerticalOffset-offset)<2,"full tab entry preserves the history reading position");CheckRetained(history,session,editor,draft,historyCount);await CheckJump(history,composer,editor,draft,"full tab view");
            CheckToolbar("full tab view",false);
            history.ScrollToVerticalOffset(offset);await Settle();Click("ToggleFullTabView");await Settle();Check(!area.HasConversation&&ReferenceEquals(area.Selected,markdown),"returning from full tabs restores the original split tool");Check(Math.Abs(history.VerticalOffset-offset)<2,"returning to split preserves the history reading position");CheckRightEdge(history,"split restored from full tabs");
            full.Width=850;await Settle();area.SelectConversation();await Settle();Check(area.HasConversation&&!full.OwnedWindows.OfType<Window>().Any(),"narrow viewport keeps main conversation and tools in the same window");CheckRetained(history,session,editor,draft,historyCount);
            full.Width=1780;await Settle();Check(!area.HasConversation&&!inspector.IsVisible&&ReferenceEquals(area.Selected,markdown),"recovering width restores split while respecting the hidden-summary preference");CheckRightEdge(history,"split restored after resize");CheckRetained(history,session,editor,draft,historyCount);
            full.Close();full=null;Console.WriteLine("Conversation viewport tests: "+checks+" passed; screenshots: viewport-split.png, viewport-wide.png");
        }
        [STAThread]public static int Main(){app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{if(full!=null)full.Close();if(pet!=null)pet.Close();app.Shutdown();}}));app.Run();return result;}
    }
}
