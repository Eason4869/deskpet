using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu.Tests
{
    public static class MarkdownTests
    {
        static int checks;
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            try
            {
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
                Console.WriteLine("Markdown tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{app.Shutdown();}
        }
        static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        static IEnumerable<MarkdownNode> Flatten(IEnumerable<MarkdownNode> nodes){foreach(var node in nodes){yield return node;foreach(var child in Flatten(node.Children))yield return child;}}
    }
}
