using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Dafeiyu.Tests
{
    public static class UnifiedPreviewTabsTests
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        sealed class OwnedView:Border,IDisposable
        {
            internal readonly TextBox Draft=new TextBox{Text="保存原始草稿"};internal int Disposals;internal bool ThrowOnDispose;
            internal OwnedView(){Child=Draft;}
            public void Dispose(){Disposals++;if(ThrowOnDispose)throw new InvalidOperationException("fixture release failure");}
        }
        static void SelectHeader(Border header){header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent});}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PreviewWorkspace area=null;
            try
            {
                int empty=0;var conversation=new OwnedView();var first=new OwnedView();var second=new OwnedView();area=new PreviewWorkspace(()=>empty++,delegate{});var host=new Window{Content=area,Width=900,Height=600};host.Show();
                area.AddPage("文件甲","file",first);area.AddPage("终端乙","terminal",second);area.AttachConversation(conversation,"当前会话");host.UpdateLayout();
                area.Width=748;area.SetToolbarInset(114);host.UpdateLayout();Check(All<ScrollViewer>(area).Single(v=>v.Name=="PreviewTabScroll").ScrollableWidth<1,"three tabs and the inline add action fit without an avoidable horizontal scrollbar");area.Width=Double.NaN;host.UpdateLayout();
                var pinned=All<Border>(area).Single(b=>b.Name=="ConversationPreviewTab");var strip=(Panel)pinned.Parent;
                Check(area.HasConversation&&area.Count==3&&area.ToolCount==2&&strip.Children.IndexOf(pinned)==0&&(string)pinned.Tag=="conversation","conversation is inserted as the stable first pinned tab while tool count remains separate");
                Check(!All<Button>(pinned).Any(b=>b.Name=="ClosePreviewTab"),"pinned conversation header has no close action");
                Check(ReferenceEquals(area.Selected,second)&&conversation.Visibility==Visibility.Collapsed&&ReferenceEquals(second.Parent,first.Parent),"attaching the conversation preserves the selected tool and existing tool controls");
                string title=new string('长',100)+"会话";area.UpdateConversationTitle(title);host.UpdateLayout();var label=All<TextBlock>(pinned).Single(t=>t.Name=="ConversationPreviewTitle");Check(label.Text==title&&label.TextTrimming==TextTrimming.CharacterEllipsis&&label.MaxWidth==165&&(string)pinned.ToolTip==title&&(string)label.ToolTip==title&&ReferenceEquals(area.Selected,second),"title updates preserve ellipsis tooltip and active tool selection");
                area.SelectConversation();Check(ReferenceEquals(area.Selected,conversation)&&conversation.IsVisible&&!first.IsVisible&&!second.IsVisible,"selecting the pinned tab reveals the original live conversation view");
                conversation.Draft.Text="切换后仍保留的草稿";var tools=All<Border>(area).Where(b=>b.Name=="PreviewTab").ToArray();SelectHeader(tools[0]);Check(ReferenceEquals(area.Selected,first)&&conversation.Draft.Text=="切换后仍保留的草稿","real header selection switches tabs without recreating the main conversation draft");
                area.AttachConversation(conversation,"更新标题");Check(area.Count==3&&ReferenceEquals(area.Selected,first)&&ReferenceEquals(All<Border>(area).Single(b=>b.Name=="ConversationPreviewTab"),pinned),"reattaching the same conversation only updates its existing pinned header");
                area.Close(conversation);Check(area.HasConversation&&conversation.Disposals==0&&area.Count==3,"programmatic close also protects the pinned conversation");
                var returned=area.DetachConversation();Check(ReferenceEquals(returned,conversation)&&conversation.Parent==null&&conversation.Visibility==Visibility.Visible&&conversation.Disposals==0&&!area.HasConversation&&area.Count==2&&area.ToolCount==2&&ReferenceEquals(area.Selected,first)&&empty==0,"detach returns the same visible conversation without disposal selection loss or empty notification");
                area.AttachConversation(conversation,"再次挂载");area.SelectConversation();returned=area.DetachConversation();Check(ReferenceEquals(returned,conversation)&&ReferenceEquals(area.Selected,first)&&second.Disposals==0&&empty==0,"detaching the selected conversation chooses an existing tool without releasing sibling state");
                area.AttachConversation(conversation,"保留会话");area.Close(first);area.Close(second);Check(area.Count==1&&area.ToolCount==0&&area.HasConversation&&ReferenceEquals(area.Selected,conversation)&&first.Disposals==1&&second.Disposals==1&&empty==0,"closing all tools leaves the pinned conversation active and never reports an empty workspace");
                returned=area.DetachConversation();Check(area.Count==0&&area.Selected==null&&empty==0&&ReferenceEquals(returned,conversation)&&conversation.Disposals==0,"detaching the only pinned tab still does not trigger empty or dispose the conversation");
                area.AttachConversation(conversation,"原主聊天");var replacement=new OwnedView();area.AttachConversation(replacement,"新主聊天");Check(area.Count==1&&ReferenceEquals(area.Selected,replacement)&&conversation.Parent==null&&conversation.Disposals==0&&conversation.Draft.Text=="切换后仍保留的草稿","updating the pinned conversation detaches the old view intact without reconstructing either draft");
                var afterFailure=new OwnedView();var throwing=new OwnedView{ThrowOnDispose=true};area.AddPage("清理异常","file",throwing);area.AddPage("后续工具","file",afterFailure);area.Dispose();Check(replacement.Disposals==0&&replacement.Parent==null&&replacement.Visibility==Visibility.Visible&&throwing.Disposals==1&&afterFailure.Disposals==1&&area.Count==0&&area.Selected==null,"workspace disposal protects the main view and continues releasing later tools after one cleanup failure");area.Dispose();Check(throwing.Disposals==1&&afterFailure.Disposals==1,"workspace disposal is idempotent for tool resources");
                int legacyEmpty=0;using(var legacy=new PreviewWorkspace(()=>legacyEmpty++,delegate{})){var only=new OwnedView();legacy.AddPage("普通工具","file",only);legacy.Close(only);Check(legacyEmpty==1&&only.Disposals==1&&legacy.Count==0,"a workspace without a pinned conversation retains the legacy last-tool empty callback");}
                using(var warningThrows=new PreviewWorkspace(delegate{},delegate{},message=>{throw new Exception("warning callback");})){var broken=new OwnedView{ThrowOnDispose=true};var survivor=new OwnedView();var protectedChat=new OwnedView();warningThrows.AttachConversation(protectedChat,"对话");warningThrows.AddPage("异常","file",broken);warningThrows.AddPage("继续释放","file",survivor);warningThrows.Dispose();Check(protectedChat.Disposals==0&&broken.Disposals==1&&survivor.Disposals==1,"a failing cleanup warning cannot stop disposal of remaining tool tabs");}
                Console.WriteLine("Unified preview tabs tests: "+checks+" passed");host.Close();return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(area!=null)area.Dispose();foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();app.Shutdown();}
        }
    }
}
