using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;

namespace Dafeiyu.Tests
{
    public static class MarkdownTests
    {
        [STAThread] public static int Main()
        {
            try
            {
                string input="# 标题\n\n**粗体**、*斜体*、~~删除~~、`code` 和 [链接](https://example.com)。\n\n> 引用\n\n1. 第一项\n2. 第二项\n\n- [x] 已完成\n\n| A | B |\n| --- | --- |\n| 一 | 二 |\n\n```csharp\nvar x = 1;\n```\n\n<script>alert('test')</script>";
                var nodes=Flatten(MarkdownContent.Parse(input)).ToList();
                foreach(string kind in new[]{"heading","bold","italic","strike","code","link","quote","ordered","list","table","codeblock"})Check(nodes.Any(n=>n.Kind==kind),"AST "+kind);
                var view=new MarkdownView{Text=input};Check(view.Document.Blocks.OfType<Table>().Count()==1,"table is a real WPF table");Check(view.Document.Blocks.OfType<List>().Count()==2,"ordered and task lists are real WPF lists");
                var visible=new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text;Check(visible.Contains("标题") && visible.Contains("粗体") && !visible.Contains("**粗体**") && !visible.Contains("```"),"Markdown markers are rendered, not printed");
                Check(visible.Contains("<script>"),"HTML remains inert text");Check(view.Document.Blocks.OfType<Paragraph>().Any(p=>p.Inlines.OfType<Hyperlink>().Any(l=>l.Inlines.OfType<InlineUIContainer>().Any(i=>((System.Windows.FrameworkElement)i.Child).Name=="MarkdownGlobeIcon"))),"links render as a globe plus clickable address");
                string plain=MarkdownContent.PlainText(input);Check(plain.Contains("标题") && plain.Contains("已完成") && !plain.Contains("**"),"plain-text copy and speech omit syntax");
                view.Text="```python\nprint('流式未闭合')";Check(new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text.Contains("流式未闭合"),"unclosed streaming code fence renders");
                Uri uri;Check(!MarkdownContent.TryLink("javascript:alert(1)",out uri) && !MarkdownContent.TryLink("file:///C:/Windows/a",out uri),"active/file links are not executable");
                Console.WriteLine("Markdown tests: 18 passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
        static IEnumerable<MarkdownNode> Flatten(IEnumerable<MarkdownNode> nodes){foreach(var node in nodes){yield return node;foreach(var child in Flatten(node.Children))yield return child;}}
    }
}
