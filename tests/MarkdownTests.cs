using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Media;

namespace Dafeiyu.Tests
{
    public static class MarkdownTests
    {
        static int checks;
        [STAThread] public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            try
            {
                if(args.Contains("--link-scroll")){CheckLinkScroll();return 0;}
                if(args.Contains("--link-sidebar")){CheckLinkNavigation();return 0;}
                string input="# 标题\n\n**粗体**、*斜体*、~~删除~~、`code` 和 [链接](https://example.com)。\n\n> 引用\n\n1. 第一项\n2. 第二项\n\n- [x] 已完成\n\n| A | B |\n| --- | --- |\n| 一 | 二 |\n\n```csharp\nvar x = 1;\n```\n\n<script>alert('test')</script>";
                var nodes=Flatten(MarkdownContent.Parse(input)).ToList();
                foreach(string kind in new[]{"heading","bold","italic","strike","code","link","quote","ordered","list","table","codeblock"})Check(nodes.Any(n=>n.Kind==kind),"AST "+kind);
                var view=new MarkdownView{Text=input};Check(view.Document.Blocks.OfType<Table>().Count()==1,"table is a real WPF table");Check(view.Document.Blocks.OfType<List>().Count()==2,"ordered and task lists are real WPF lists");
                var visible=new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text;Check(visible.Contains("标题") && visible.Contains("粗体") && !visible.Contains("**粗体**") && !visible.Contains("```"),"Markdown markers are rendered, not printed");
                Check(visible.Contains("<script>"),"HTML remains inert text");Check(view.Document.Blocks.OfType<Paragraph>().Any(p=>p.Inlines.OfType<Hyperlink>().Any(l=>l.Inlines.OfType<InlineUIContainer>().Any(i=>((System.Windows.FrameworkElement)i.Child).Name=="MarkdownGlobeIcon"))),"links render as a globe plus clickable address");
                string plain=MarkdownContent.PlainText(input);Check(plain.Contains("标题") && plain.Contains("已完成") && !plain.Contains("**"),"plain-text copy and speech omit syntax");
                var links=new MarkdownView{Text="[仓库](https://github.com/Eason4869/deskpet) 和 https://example.com/path?q=1#part"};
                var rendered=links.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Hyperlink>()).ToArray();
                Check(rendered.Length==2&&rendered.All(l=>l.ContextMenu!=null),"named and automatic links both expose a right-click menu");
                foreach(var link in rendered)
                {
                    var menu=link.ContextMenu;Check(menu.Items.OfType<MenuItem>().Any(i=>i.Name=="OpenLinkInSideTab")&&menu.Items.OfType<MenuItem>().Any(i=>i.Name=="OpenLinkExternally"),"link menu offers side tab and default browser actions");
                    menu.Items.OfType<MenuItem>().Single(i=>i.Name=="CopyLinkAddress").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(Clipboard.GetText()==link.NavigateUri.AbsoluteUri,"copy link copies the full target rather than the visible label");
                }
                var email=new MarkdownView{Text="[邮箱](mailto:test@example.com)"};var mail=email.Document.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Hyperlink>().Single();
                Check(!mail.ContextMenu.Items.OfType<MenuItem>().Single(i=>i.Name=="OpenLinkInSideTab").IsEnabled,"email addresses cannot open in the web pane");
                ThemeManager.Apply("dark");Check(((System.Windows.Media.SolidColorBrush)rendered[0].Foreground).Color.R>130,"existing message links gain readable blue text in dark mode");ThemeManager.Apply("light");
                view.Text="```python\nprint('流式未闭合')";Check(new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text.Contains("流式未闭合"),"unclosed streaming code fence renders");
                Uri uri;Check(!MarkdownContent.TryLink("javascript:alert(1)",out uri) && !MarkdownContent.TryLink("file:///C:/Windows/a",out uri),"active/file links are not executable");
                CheckLinkScroll();CheckLinkNavigation();
                Console.WriteLine("Markdown tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{foreach(Window window in app.Windows.Cast<Window>().ToArray())window.Close();app.Shutdown();}
        }
        [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
        static void Pump(){for(int i=0;i<12;i++){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));System.Threading.Thread.Sleep(10);}}
        static void CheckLinkScroll()
        {
            var body=new MarkdownView{Text=String.Join("\n\n",Enumerable.Repeat("Long answer paragraph with enough text to scroll.",80))+"\n\n来源：\n- [Source](https://example.com/news)"};
            var input=new TextBox{Height=30};var layout=new DockPanel();DockPanel.SetDock(input,Dock.Bottom);layout.Children.Add(input);var history=new ScrollViewer{Content=body};layout.Children.Add(history);
            var host=new Window{Width=650,Height=420,Content=layout,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};var cursor=DesktopInterop.Cursor();
            try
            {
                host.Show();host.Activate();Pump();input.Focus();history.ScrollToEnd();Pump();
                var link=body.Document.Blocks.OfType<List>().Single().ListItems.Single().Blocks.OfType<Paragraph>().Single().Inlines.OfType<Hyperlink>().Single();
                var text=link.Inlines.OfType<Run>().Single();var rect=text.ContentStart.GetCharacterRect(LogicalDirection.Forward);var point=body.PointToScreen(new Point(rect.X+4,rect.Y+rect.Height/2));double before=history.VerticalOffset;
                Check(before>1000,"citation fixture scrolls through a long answer");
                Check(SetCursorPos((int)point.X,(int)point.Y),"native pointer reaches the citation");Pump();
                Check(DesktopInterop.IsWindowAtPoint(new System.Windows.Interop.WindowInteropHelper(host).Handle,point),"native citation target belongs to the fixture window");
                mouse_event(2,0,0,0,UIntPtr.Zero);Pump();
                Console.WriteLine("INFO citation mouse-down offset: "+before+" -> "+history.VerticalOffset);
                Check(Math.Abs(history.VerticalOffset-before)<2,"pressing a bottom citation preserves the answer reading position");
                var blank=input.PointToScreen(new Point(4,4));SetCursorPos((int)blank.X,(int)blank.Y);mouse_event(4,0,0,0,UIntPtr.Zero);Pump();
                body.Focus();body.Selection.Select(body.Document.ContentStart,body.Document.ContentStart);history.ScrollToTop();Pump();
                EditingCommands.SelectToDocumentEnd.Execute(null,body);Pump();
                Check(body.Selection.Text.Contains("Source")&&history.VerticalOffset>1000,"keyboard selection to the document end still reveals its endpoint");
            }
            finally{SetCursorPos((int)cursor.X,(int)cursor.Y);mouse_event(4,0,0,0,UIntPtr.Zero);host.Close();}
        }
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject
        {if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static IEnumerable<Hyperlink> Links(DependencyObject root)
        {if(root is Hyperlink)yield return(Hyperlink)root;foreach(object child in LogicalTreeHelper.GetChildren(root)){var item=child as DependencyObject;if(item!=null)foreach(var link in Links(item))yield return link;}}
        static void CheckLinkNavigation()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","link-navigation-"+Guid.NewGuid().ToString("N"));
            var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);ChatWindow host=null;
            try
            {
                var workspace=pet.Workspaces.CreateWorkspace(new ProjectDefinition{Id="link-fixture",Name="Links",Workspace=root},new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="Links",Mode="chat"});
                workspace.Session.History.Commit("Question",String.Join("\n\n",Enumerable.Repeat("Long answer paragraph.",80))+"\n\n[Named](http://127.0.0.1:9/named)\n\nhttp://127.0.0.1:9/auto\n\n> [Quote](http://127.0.0.1:9/quote)\n\n| Source |\n| --- |\n| [Table](http://127.0.0.1:9/table) |\n\n来源：\n- [Citation](http://127.0.0.1:9/citation)");
                host=new ChatWindow(pet,workspace){Width=1600,Height=800};host.Show();Pump();
                var body=All<MarkdownView>(host).Single(v=>v.Text.Contains("[Named]"));var links=Links(body.Document).ToArray();Check(links.Length==5,"named, automatic, quoted, table and citation links render");
                var history=All<ScrollViewer>(host).Single(s=>s.Name=="ChatHistoryScroll");All<MarkdownEditor>(host).Single(e=>e.Name=="MessageInput").Focus();history.ScrollToEnd();Pump();
                var citation=links.Last();var run=citation.Inlines.OfType<Run>().Single();var rect=run.ContentStart.GetCharacterRect(LogicalDirection.Forward);var point=body.PointToScreen(new Point(rect.X+4,rect.Y+rect.Height/2));var cursor=DesktopInterop.Cursor();
                try
                {
                    host.Topmost=true;host.Activate();Pump();Check(SetCursorPos((int)point.X,(int)point.Y),"native citation click moves pointer");Pump();
                    Check(DesktopInterop.IsWindowAtPoint(new System.Windows.Interop.WindowInteropHelper(host).Handle,point),"native chat click belongs to the fixture window");
                    mouse_event(2,0,0,0,UIntPtr.Zero);Pump();mouse_event(4,0,0,0,UIntPtr.Zero);Pump();
                    var pane=All<PreviewWorkspace>(host).SingleOrDefault();Check(pane!=null&&pane.Selected is BrowserPane&&All<TextBox>(pane.Selected).Single(t=>t.Name=="BrowserAddress").Text=="http://127.0.0.1:9/citation","one complete mouse click opens a bottom citation in the sidebar");
                }
                finally{mouse_event(4,0,0,0,UIntPtr.Zero);SetCursorPos((int)cursor.X,(int)cursor.Y);host.Topmost=false;}
                foreach(var link in links)
                {
                    typeof(Hyperlink).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(link,null);Pump();
                    var area=All<PreviewWorkspace>(host).SingleOrDefault();var pane=area==null?null:area.Selected as BrowserPane;
                    Check(pane!=null&&All<TextBox>(pane).Single(t=>t.Name=="BrowserAddress").Text==link.NavigateUri.AbsoluteUri,"left activation opens the exact URL in a sidebar: "+link.NavigateUri.AbsolutePath);
                    Check(link.ContextMenu.Items.OfType<MenuItem>().Single(i=>i.Name=="OpenLinkExternally").IsEnabled,"right-click external browser option stays available");
                }
                host.OpenSideChat();Pump();var side=(SideChatPane)All<PreviewWorkspace>(host).Single().Selected;
                side.Session.AddLiveTurn(side.Session.PersonaId,"Side question","[Side link](http://127.0.0.1:9/side)");Pump();
                var sideLink=Links(All<MarkdownView>(side).Single(v=>v.Text.Contains("[Side link]")).Document).Single();
                typeof(Hyperlink).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(sideLink,null);Pump();
                var selected=All<PreviewWorkspace>(host).Single().Selected;
                Check(selected is BrowserPane&&All<TextBox>(selected).Single(t=>t.Name=="BrowserAddress").Text=="http://127.0.0.1:9/side","temporary side conversation links open the exact URL in a browser sidebar");
                workspace.Session.AddLiveTurn(workspace.Session.PersonaId,"Quick question","[Quick link](http://127.0.0.1:9/quick)");
                var quick=new QuickChatWindow(pet,workspace);
                try
                {
                    quick.Show();Pump();var quickLink=Links(All<MarkdownView>(quick).Single(v=>v.Text.Contains("[Quick link]")).Document).Single();
                    typeof(Hyperlink).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(quickLink,null);Pump();
                    Check(Application.Current.Windows.OfType<ChatWindow>().SelectMany(w=>All<PreviewWorkspace>(w)).Any(p=>p.Selected is BrowserPane&&All<TextBox>(p.Selected).Any(t=>t.Name=="BrowserAddress"&&t.Text=="http://127.0.0.1:9/quick")),"quick conversation links open the exact URL in the main chat browser sidebar");
                }
                finally{quick.Close();}
            }
            finally{if(host!=null)host.Close();pet.Close();Pump();}
        }
        static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        static IEnumerable<MarkdownNode> Flatten(IEnumerable<MarkdownNode> nodes){foreach(var node in nodes){yield return node;foreach(var child in Flatten(node.Children))yield return child;}}
    }
}
