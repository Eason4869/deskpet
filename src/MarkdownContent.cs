using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;

namespace Dafeiyu
{
    public sealed class MarkdownNode
    {
        public string Kind = "", Text = "", Url = "", Info = ""; public int Level, Start = 1; public bool Header,AutoLink;
        public readonly List<MarkdownNode> Children = new List<MarkdownNode>();
    }
    // The neutral render tree is shared by WPF and Avalonia. No HTML is executed.
    public static class MarkdownContent
    {
        public static List<MarkdownNode> Parse(string text)
        {
#if !PORTABLE
            MarkdownDependencies.Ensure();
#endif
            return ParseCore(text ?? "");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<MarkdownNode> ParseCore(string text)
        {
            return Blocks(Markdown.Parse(text, PipelineCache.Instance));
        }
        // Initialise only after embedded dependencies have been registered.
        static class PipelineCache
        {
            internal static readonly MarkdownPipeline Instance = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough).UseTaskLists().UseAutoLinks().DisableHtml().Build();
        }
        private static List<MarkdownNode> Blocks(ContainerBlock blocks)
        {
            var result = new List<MarkdownNode>();
            foreach (var block in blocks)
            {
                var node = new MarkdownNode();
                var code = block as CodeBlock; var heading = block as HeadingBlock; var list = block as ListBlock; var table = block as Table;
                if (code != null) { node.Kind = "codeblock"; node.Text = code.Lines.ToString(); var fenced = code as FencedCodeBlock; if (fenced != null) node.Info = fenced.Info ?? ""; }
                else if (heading != null) { node.Kind = "heading"; node.Level = heading.Level; node.Children.AddRange(Inlines(heading.Inline)); }
                else if (table != null) { node.Kind = "table"; foreach (TableRow row in table) { var rowNode = new MarkdownNode { Kind = "row", Header = row.IsHeader }; foreach (TableCell cell in row) { var cellNode = new MarkdownNode { Kind = "cell" }; cellNode.Children.AddRange(Blocks(cell)); rowNode.Children.Add(cellNode); } node.Children.Add(rowNode); } }
                else if (list != null) { node.Kind = list.IsOrdered ? "ordered" : "list"; int start; if (Int32.TryParse(list.OrderedStart, out start)) node.Start = Math.Max(1, start); node.Children.AddRange(Blocks(list)); }
                else if (block is QuoteBlock) { node.Kind = "quote"; node.Children.AddRange(Blocks((ContainerBlock)block)); }
                else if (block is ListItemBlock) { node.Kind = "item"; node.Children.AddRange(Blocks((ContainerBlock)block)); }
                else if (block is ThematicBreakBlock) node.Kind = "rule";
                else if (block is LeafBlock) { node.Kind = "paragraph"; node.Children.AddRange(Inlines(((LeafBlock)block).Inline)); }
                else if (block is ContainerBlock) { node.Kind = "group"; node.Children.AddRange(Blocks((ContainerBlock)block)); }
                result.Add(node);
            }
            return result;
        }
        private static List<MarkdownNode> Inlines(ContainerInline container)
        {
            var result = new List<MarkdownNode>(); if (container == null) return result;
            foreach (var inline in container)
            {
                var node = new MarkdownNode { Kind = "text" }; var literal = inline as LiteralInline; var emphasis = inline as EmphasisInline; var code = inline as CodeInline; var link = inline as LinkInline; var auto = inline as AutolinkInline; var task = inline as Markdig.Extensions.TaskLists.TaskList;
                if (literal != null) node.Text = literal.Content.ToString();
                else if (code != null) { node.Kind = "code"; node.Text = code.Content; }
                else if (emphasis != null) { node.Kind = emphasis.DelimiterChar == '~' ? "strike" : emphasis.DelimiterCount >= 2 ? "bold" : "italic"; node.Children.AddRange(Inlines(emphasis)); }
                else if (link != null) { node.Kind = link.IsImage ? "image" : "link"; node.AutoLink=link.IsAutoLink; node.Url = link.Url ?? ""; node.Children.AddRange(Inlines(link)); }
                else if (auto != null) { node.Kind = "link"; node.AutoLink=true; node.Url = auto.IsEmail ? "mailto:" + auto.Url : auto.Url; node.Text = auto.Url; }
                else if (inline is LineBreakInline) { node.Kind = "break"; node.Text = "\n"; }
                else if (task != null) node.Text = task.Checked ? "☑ " : "☐ ";
                else if (inline is ContainerInline) node.Children.AddRange(Inlines((ContainerInline)inline));
                result.Add(node);
            }
            return result;
        }
        public static string PlainText(string markdown) { return PlainText(Parse(markdown)); }
        public static string PlainText(IEnumerable<MarkdownNode> nodes)
        {
            var text = new StringBuilder(); foreach (var node in nodes) { text.Append(node.Text); if (node.Children.Count > 0) text.Append(PlainText(node.Children)); if (node.Kind == "paragraph" || node.Kind == "heading" || node.Kind == "codeblock" || node.Kind == "item" || node.Kind == "row") text.AppendLine(); else if (node.Kind == "cell") text.Append(" "); }
            return text.ToString();
        }
        public static bool TryLink(string url, out Uri uri) { return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http" || uri.Scheme == "mailto"); }
    }
#if !PORTABLE
    internal static class MarkdownDependencies
    {
        static bool loaded; static readonly object gate = new object(); static readonly Dictionary<string, System.Reflection.Assembly> assemblies = new Dictionary<string, System.Reflection.Assembly>();
        public static void Ensure()
        {
            lock (gate)
            {
                if (loaded) return;
                AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs args) { System.Reflection.Assembly found; return assemblies.TryGetValue(new System.Reflection.AssemblyName(args.Name).Name, out found) ? found : null; };
                foreach (string name in new[] { "System.Buffers", "System.Numerics.Vectors", "System.Runtime.CompilerServices.Unsafe", "System.Memory", "Markdig" })
                    using (var stream = typeof(MarkdownDependencies).Assembly.GetManifestResourceStream("Dafeiyu.Markdown." + name + ".dll"))
                    using (var buffer = new System.IO.MemoryStream()) { if (stream == null) throw new System.IO.FileNotFoundException("Missing embedded Markdown dependency: " + name); stream.CopyTo(buffer); assemblies[name] = System.Reflection.Assembly.Load(buffer.ToArray()); }
                loaded = true;
            }
        }
    }
#endif
}
