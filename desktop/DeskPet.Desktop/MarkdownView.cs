using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Dafeiyu;

namespace DeskPet.Desktop;

// Native, selectable Markdown. Links use the platform launcher; HTML is never executed.
internal sealed class MarkdownView : StackPanel
{
    private static readonly IBrush Muted = DesktopTheme.Brush("#687282");
    private static readonly IBrush CodeBackground = DesktopTheme.Brush("#F3F5F8");
    private static readonly FontFamily Mono = new("Menlo,Consolas,monospace");
    private string? markdown;
    public MarkdownView() { Name = "MarkdownBody"; Spacing = 9; }
    public void Update(string value)
    {
        if (markdown == value) return;
        markdown = value; Children.Clear();
        foreach (var node in MarkdownContent.Parse(value)) Children.Add(Block(node));
    }
    private Control Block(MarkdownNode node)
    {
        switch (node.Kind)
        {
            case "heading":
                var heading = Text(node.Children); heading.Name = "MarkdownHeading";
                heading.FontSize = node.Level switch { 1 => 26, 2 => 22, 3 => 19, _ => 16 };
                heading.FontWeight = FontWeight.Bold; heading.Margin = new Thickness(0, 6, 0, 2); return heading;
            case "codeblock":
                var code = new SelectableTextBlock { Name = "MarkdownCode", Text = node.Text, FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.NoWrap };
                Ui.ReadOnlyMenu(code);
                var contents = new StackPanel { Spacing = 6 };
                if (!string.IsNullOrWhiteSpace(node.Info)) contents.Children.Add(new TextBlock { Text = node.Info, Foreground = Muted, FontSize = 11 });
                contents.Children.Add(new ScrollViewer { Content = code, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
                return new Border { Background = CodeBackground, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = contents };
            case "quote":
                return new Border { Name = "MarkdownQuote", BorderBrush = DesktopTheme.Brush("#C9D2DE"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 3, 0, 3), Child = Blocks(node.Children) };
            case "list": case "ordered":
                var list = new StackPanel { Name = "MarkdownList", Spacing = 5 }; int index = node.Start;
                foreach (var item in node.Children)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                    row.Children.Add(new TextBlock { Text = node.Kind == "ordered" ? (index++) + "." : "•", MinWidth = 21, Margin = new Thickness(2, 0, 7, 0), VerticalAlignment = VerticalAlignment.Top });
                    var body = Blocks(item.Children); Grid.SetColumn(body, 1); row.Children.Add(body); list.Children.Add(row);
                }
                return list;
            case "table": return Table(node);
            case "rule": return new Border { Height = 1, Background = DesktopTheme.Brush("#DDE3EA"), Margin = new Thickness(0, 6) };
            case "group": case "item": case "cell": return Blocks(node.Children);
            default: return Text(node.Children);
        }
    }
    private StackPanel Blocks(IEnumerable<MarkdownNode> nodes)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var node in nodes) panel.Children.Add(Block(node));
        return panel;
    }
    private Control Table(MarkdownNode table)
    {
        var grid = new Grid { Name = "MarkdownTable" };
        int columns = table.Children.Select(r => r.Children.Count).DefaultIfEmpty(0).Max();
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < table.Children.Count; r++)
        {
            var row = table.Children[r]; grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < row.Children.Count; c++)
            {
                var content = Blocks(row.Children[c].Children); content.MinWidth = 64; content.MaxWidth = 280;
                if (row.Header) foreach (var text in content.Children.OfType<SelectableTextBlock>()) text.FontWeight = FontWeight.Bold;
                var cell = new Border { Padding = new Thickness(9, 6), BorderBrush = DesktopTheme.Brush("#DDE3EA"), BorderThickness = new Thickness(c == 0 ? 1 : 0, r == 0 ? 1 : 0, 1, 1), Background = row.Header ? CodeBackground : Brushes.Transparent, Child = content };
                Grid.SetColumn(cell, c); Grid.SetRow(cell, r); grid.Children.Add(cell);
            }
        }
        return new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    private SelectableTextBlock Text(IEnumerable<MarkdownNode> nodes)
    {
        var text = new SelectableTextBlock { Name = "MarkdownParagraph", TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        Ui.ReadOnlyMenu(text);
        foreach (var node in nodes) text.Inlines!.Add(Inline(node));
        return text;
    }
    private Inline Inline(MarkdownNode node)
    {
        if (node.Kind == "break") return new LineBreak();
        if (node.Kind == "code") return new Run(node.Text) { FontFamily = Mono, Background = CodeBackground };
        if (node.Kind is "link" or "image")
        {
            var label = MarkdownContent.PlainText(node.Children).Trim();
            if (string.IsNullOrEmpty(label)) label = string.IsNullOrEmpty(node.Text) ? node.Url : node.Text;
            if (node.Kind == "image") label = "图片：" + label;
            if (MarkdownContent.TryLink(node.Url, out var uri))
            {
                var link = new Button { Name = "MarkdownLink", Content = label, Padding = new Thickness(0), Margin = new Thickness(0), MinHeight = 0, MinWidth = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = DesktopTheme.Brush("#246ED8"), Cursor = new Cursor(StandardCursorType.Hand) };
                ToolTip.SetTip(link, node.Url);
                link.Click += async (_, _) => { if (TopLevel.GetTopLevel(this) is { } top) await top.Launcher.LaunchUriAsync(uri); };
                return new InlineUIContainer(link);
            }
            return new Run(label);
        }
        if (node.Children.Count == 0) return new Run(node.Text);
        var span = new Span();
        if (node.Kind == "bold") span.FontWeight = FontWeight.Bold;
        if (node.Kind == "italic") span.FontStyle = FontStyle.Italic;
        if (node.Kind == "strike") span.TextDecorations = TextDecorations.Strikethrough;
        foreach (var child in node.Children) span.Inlines.Add(Inline(child));
        return span;
    }
}
