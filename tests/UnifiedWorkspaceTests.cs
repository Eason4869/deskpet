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
    public static class UnifiedWorkspaceTests
    {
        static Application app;
        static PetWindow pet;
        static ChatWindow full;
        static int checks;

        sealed class RetainedTool:Border,IDisposable
        {
            internal int Releases;
            internal RetainedTool(){Child=Ui.Text("独立工具状态：隐藏和切换布局时保留。",14);Padding=new Thickness(24);}
            public void Dispose(){Releases++;}
        }
        static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject
        {
            if(root is T)yield return(T)root;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
        static object Field(string name){return typeof(ChatWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(full);}
        static void ToggleFull(){typeof(ChatWindow).GetMethod("ToggleFullTabView",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(full,null);}
        static void Click(string name){All<Button>(full).Single(b=>b.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static async Task Settle(){await Task.Delay(130);full.UpdateLayout();}
        static void Capture(string name)
        {
            full.UpdateLayout();var target=(FrameworkElement)full.Content;var bounds=new Rect(0,0,target.ActualWidth,target.ActualHeight);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(Brushes.White,null,bounds);drawing.DrawRectangle(new VisualBrush(target),null,bounds);}var image=new RenderTargetBitmap((int)Math.Ceiling(bounds.Width),(int)Math.Ceiling(bounds.Height),96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine("work","permissions",name)))encoder.Save(file);
        }
        static void CheckMain(Grid surface,MarkdownEditor editor,ConversationSession session,string draft)
        {
            Check(ReferenceEquals(Field("conversationSurface"),surface)&&ReferenceEquals(Field("input"),editor),"layout keeps the original main conversation surface and editor");
            Check(ReferenceEquals(Field("session"),session)&&editor.Text==draft,"layout preserves the main session identity and unsent draft");
        }
        static void CheckPinned(PreviewWorkspace area,Grid surface,string title)
        {
            Check(area.HasConversation,"full tab view includes the pinned main conversation");
            var pinned=All<Border>(area).Single(b=>b.Name=="ConversationPreviewTab");var headers=pinned.Parent as Panel;
            Check(headers!=null&&ReferenceEquals(headers.Children[0],pinned)&&(pinned.Tag as string)=="conversation","main conversation remains the first fixed tab");
            Check(!All<Button>(pinned).Any(b=>b.Name=="ClosePreviewTab"),"pinned main conversation has no tool-tab close button");
            Check(All<TextBlock>(pinned).Single(t=>t.Name=="ConversationPreviewTitle").Text==title,"pinned tab displays the active conversation title");
            area.SelectConversation();
            Check(ReferenceEquals(area.Selected,surface)&&surface.IsVisible,"selecting the main tab reveals the original conversation view");
        }
        static async Task Run()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","unified-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string md=Path.Combine(root,"工作说明.md");File.WriteAllText(md,"# 工作说明\n\n主对话与工具共用同一个窗口。\n\n- 完整标签视图中，主聊天固定在第一项。\n- 返回分屏后，工具与草稿都保持原样。\n\n```csharp\nvar workspace = \"保留同一会话\";\n```\n");
            var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=root,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"data","settings.json")),null);
            var project=new ProjectDefinition{Id="unified-fixture",Name="完整标签工作区",Workspace=root};
            var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="检查完整标签与分屏切换",Mode="work"};
            var workspace=pet.Workspaces.CreateWorkspace(project,definition);
            full=new ChatWindow(pet,workspace){Width=1780,Height=900};double normalMinWidth=full.MinWidth;
            // A small CI desktop caps WPF's requested Width; MinWidth establishes this fixture's actual wide layout.
            full.MinWidth=1780;full.Show();await Settle();
            Check(Math.Abs(full.ActualWidth-1780)<1,"wide fixture has the actual window width required for split-layout checks");
            var shell=(Grid)Field("workspaceShell");var surface=(Grid)Field("conversationSurface");var editor=(MarkdownEditor)Field("input");var session=(ConversationSession)Field("session");const string draft="这份主聊天草稿需要在所有布局之间保留。";editor.Text=draft;
            // Populate the actual conversation renderer without submitting a request.
            for(int i=1;i<=12;i++)session.History.Commit("第 "+i+" 条历史消息：检查布局与滚动记录。","这是第 "+i+" 条本地历史回复。切换标签、隐藏工具和恢复分屏时，需要继续停留在正在阅读的位置。\n\n这里是第二段中文正文，用于产生真实的滚动内容。\n\n```csharp\nvar item = "+i+";\nConsole.WriteLine(item);\n```");
            int historyCount=session.History.Messages.Count;full.ReloadHistory(false);await Settle();var history=(ScrollViewer)Field("scroll");
            Check(historyCount==24&&history.ScrollableHeight>1000,"local fixture renders enough real conversation history to scroll");
            Check(ReferenceEquals(surface.Parent,shell)&&Grid.GetColumn(surface)==2,"initial main conversation occupies its original shell column");
            full.OpenFilePreview(md);await Settle();var area=(PreviewWorkspace)Field("filePreview");var markdown=area.Selected as FilePreviewPane;Check(markdown!=null,"Markdown tool opens beside the main conversation");await markdown.Ready;
            Check(area.ToolCount==1&&!area.HasConversation,"split workspace initially contains one independent tool tab");
            history.ScrollToVerticalOffset(260);await Settle();double readingOffset=history.VerticalOffset;Check(readingOffset>200&&history.ScrollableHeight-readingOffset>500,"history is deliberately positioned away from both start and latest reply (offset="+readingOffset.ToString("F1")+", extent="+history.ScrollableHeight.ToString("F1")+")");
            full.ReloadHistory();history.UpdateLayout();double newerOffset=readingOffset+100;history.ScrollToVerticalOffset(newerOffset);history.UpdateLayout();await Settle();Check(Math.Abs(history.VerticalOffset-newerOffset)<2,"a newer reader position supersedes a pending history follow-to-bottom");readingOffset=history.VerticalOffset;
            ToggleFull();await Settle();CheckPinned(area,surface,definition.Name);CheckMain(surface,editor,session,draft);
            await Settle();Check(Math.Abs(history.VerticalOffset-readingOffset)<2,"entering full tab view preserves the main history scroll offset");
            Check(area.ToolCount==1&&All<FilePreviewPane>(area).Any(p=>ReferenceEquals(p,markdown)),"entering full tabs retains the same Markdown tool instance");
            Check(!ReferenceEquals(surface.Parent,shell)&&All<Grid>(area).Any(g=>ReferenceEquals(g,surface)),"full tabs physically host the existing main view inside the tab workspace");
            definition.Name="更新后的主聊天标题";full.RefreshAppearance();await Settle();CheckPinned(area,surface,definition.Name);
            var dock=(ContentControl)Field("filePreviewDock");area.SelectConversation();await Settle();
            Click("ToggleSideTabs");await Settle();Check(!dock.IsVisible&&ReferenceEquals(Field("filePreview"),area)&&area.ToolCount==1&&ReferenceEquals(surface.Parent,shell),"hiding full tabs restores the borrowed main view without releasing its tools");
            Click("ToggleSideTabs");await Settle();Check(area.HasConversation&&ReferenceEquals(area.Selected,surface)&&surface.IsVisible,"showing full tabs restores the previously selected pinned conversation");
            CheckMain(surface,editor,session,draft);
            Check(!full.OwnedWindows.OfType<Window>().Any(),"full tab mode creates no independent window");Capture("unified-workspace.png");
            Click("ToggleFullTabView");await Settle();
            Check(!area.HasConversation&&ReferenceEquals(surface.Parent,shell)&&Grid.GetColumn(surface)==2,"split toggle returns the original main view to shell column two");
            Check(Math.Abs(history.VerticalOffset-readingOffset)<2,"returning to split preserves the main history scroll offset");
            Check(area.ToolCount==1&&ReferenceEquals(area.Selected,markdown),"returning to split keeps the selected original tool");CheckMain(surface,editor,session,draft);Capture("split-workspace.png");
            var retained=new RetainedTool();area.AddPage("保留状态夹具","file",retained);await Settle();int tools=area.ToolCount;
            ToggleFull();await Settle();Check(area.ToolCount==2&&ReferenceEquals(area.Selected,retained),"entering full tabs preserves selected tool B when tools A and B are open");area.SelectConversation();Click("ToggleFullTabView");await Settle();Check(ReferenceEquals(area.Selected,retained)&&retained.Releases==0,"leaving pinned conversation for split restores the previously selected tool B");
            Click("ToggleSideTabs");await Settle();
            Check(!dock.IsVisible&&ReferenceEquals(Field("filePreview"),area)&&ReferenceEquals(area.Selected,retained)&&retained.Releases==0,"hiding side tabs keeps the workspace, selected tool and its resources alive");
            Click("ToggleSideTabs");await Settle();
            Check(dock.IsVisible&&ReferenceEquals(area.Selected,retained)&&area.ToolCount==tools&&retained.Releases==0,"showing side tabs restores the same selected view without a duplicate tab");
            area.Close(retained);await Settle();Check(retained.Releases==1&&area.ToolCount==1,"explicit tool close releases it exactly once");
            var divider=All<Thumb>(full).Single(t=>t.Name=="PreviewDivider");divider.RaiseEvent(new DragDeltaEventArgs(-5000,0){RoutedEvent=Thumb.DragDeltaEvent});await Settle();
            Check(area.HasConversation,"dragging preview divider far left enters full tab view");CheckPinned(area,surface,definition.Name);CheckMain(surface,editor,session,draft);
            Click("ToggleFullTabView");await Settle();Check(!area.HasConversation&&ReferenceEquals(surface.Parent,shell),"full view entered by dragging can return to split");
            divider.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});double maximumPreview=Math.Max(320,shell.ActualWidth-shell.ColumnDefinitions[0].ActualWidth-430);double toBoundary=shell.ColumnDefinitions[6].ActualWidth-maximumPreview;divider.RaiseEvent(new DragDeltaEventArgs(toBoundary,0){RoutedEvent=Thumb.DragDeltaEvent});await Settle();Check(!area.HasConversation,"dragging to the split boundary alone retains split layout");
            for(int step=0;step<30&&!area.HasConversation;step++){divider.RaiseEvent(new DragDeltaEventArgs(-10,0){RoutedEvent=Thumb.DragDeltaEvent});await Task.Delay(30);full.UpdateLayout();}
            divider.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});await Settle();Check(area.HasConversation,"slow consecutive ten-pixel left drags accumulate beyond the boundary into full tabs");
            area.SelectConversation();CheckMain(surface,editor,session,draft);Click("ToggleFullTabView");await Settle();Check(!area.HasConversation,"incremental drag full mode can return to split");
            full.MinWidth=normalMinWidth;full.Width=850;await Settle();
            Check(Math.Abs(full.ActualWidth-850)<1,"narrow fixture reaches the actual width that selects automatic full tabs");
            CheckPinned(area,surface,definition.Name);Check(!full.OwnedWindows.OfType<Window>().Any(),"narrow width automatically uses the same-window full tab view");CheckMain(surface,editor,session,draft);
            full.MinWidth=1780;full.Width=1780;await Settle();
            Check(Math.Abs(full.ActualWidth-1780)<1,"widening restores the actual fixture width before checking split recovery");
            Check(!area.HasConversation&&ReferenceEquals(surface.Parent,shell)&&Grid.GetColumn(surface)==2,"automatic narrow full view returns to split when width recovers");
            Check(area.ToolCount==1&&ReferenceEquals(area.Selected,markdown)&&ReferenceEquals(Field("filePreview"),area),"automatic resize retains the original tool and workspace");CheckMain(surface,editor,session,draft);
            Check(retained.Releases==1&&session.History.Messages.Count==historyCount&&!session.IsBusy,"layout tests preserve the fixture history and release each closed tool once");
            ToggleFull();await Settle();area.OpenFile(md);Check(ReferenceEquals(area.Selected,markdown),"workspace-navigation fixture starts with a tool selected in full tabs");full.SwitchWorkspace(workspace);await Settle();Check(ReferenceEquals(area.Selected,surface)&&ReferenceEquals(Field("session"),session),"selecting the current workspace from a tool tab returns to its pinned conversation");
            var secondDefinition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="第二个工作会话",Mode="work"};var secondWorkspace=pet.Workspaces.CreateWorkspace(project,secondDefinition);area.OpenFile(md);full.SwitchWorkspace(secondWorkspace);await Settle();
            Check(ReferenceEquals(area.Selected,surface)&&ReferenceEquals(Field("session"),secondWorkspace.Session)&&!ReferenceEquals(Field("session"),session),"switching workspaces from a full tool tab selects the new main session");Check(All<TextBlock>(area).Single(t=>t.Name=="ConversationPreviewTitle").Text==secondDefinition.Name,"pinned title follows the newly selected workspace");Check(area.ToolCount==1&&All<FilePreviewPane>(area).Any(p=>ReferenceEquals(p,markdown)),"workspace navigation preserves existing tool tabs and their view instances");
            editor.Text="第二个会话的独立草稿";full.SwitchWorkspace(workspace);await Settle();CheckMain(surface,editor,session,draft);Check(ReferenceEquals(area.Selected,surface)&&All<TextBlock>(area).Single(t=>t.Name=="ConversationPreviewTitle").Text==definition.Name,"returning to the original workspace restores its pinned title and selects chat");Click("ToggleFullTabView");await Settle();Check(ReferenceEquals(area.Selected,markdown),"workspace round trip preserves the selected split tool");
            ToggleFull();await Settle();area.SelectConversation();area.Close(markdown);await Settle();Check(area.HasConversation&&area.ToolCount==0&&ReferenceEquals(area.Selected,surface),"closing the last tool in full mode retains the pinned main conversation");
            Click("ToggleFullTabView");await Settle();Check(Field("filePreview")==null&&!dock.IsVisible&&dock.Content==null&&ReferenceEquals(surface.Parent,shell)&&Grid.GetColumn(surface)==2,"returning to split after the last tool closes removes the empty tool workspace");CheckMain(surface,editor,session,draft);
            full.MinWidth=normalMinWidth;full.Close();full=null;Console.WriteLine("Unified workspace tests: "+checks+" passed; screenshots: work/permissions/unified-workspace.png, split-workspace.png");
        }
        [STAThread]public static int Main()
        {
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{if(full!=null)full.Close();if(pet!=null)pet.Close();app.Shutdown();}}));app.Run();return result;
        }
    }
}
