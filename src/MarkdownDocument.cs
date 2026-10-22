using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace Dafeiyu
{
    // Shared renderer for messages and the editable composer; no second preview tree.
    internal sealed class MarkdownDocument
    {
        readonly bool editable;
        MarkdownDocument(bool edit){editable=edit;}
        internal static FlowDocument Create(string text,FontFamily family,Brush foreground,bool editable,out bool formatted)
        {
            var doc=new FlowDocument{PagePadding=new Thickness(0),FontFamily=family,FontSize=14,Foreground=foreground,ColumnWidth=Double.PositiveInfinity};
            var renderer=new MarkdownDocument(editable);formatted=false;
            foreach(var node in MarkdownContent.Parse(text)){formatted|=Formatted(node);doc.Blocks.Add(renderer.Block(node));}
            if(doc.Blocks.Count==0)doc.Blocks.Add(new Paragraph{Margin=new Thickness(0)});
            // Markdown parsers discard terminal blank lines; an editable draft must
            // retain them so the caret can actually stay on the user's new line.
            var last=doc.Blocks.LastBlock as Paragraph;
            if(editable){int trailing=0,existing=0;string normalized=(text??"").Replace("\r\n","\n").Replace('\r','\n');for(int i=normalized.Length-1;i>=0&&normalized[i]=='\n';i--)trailing++;
                if(trailing>0 && last==null){last=new Paragraph{Tag="draft-tail",Margin=new Thickness(0)};doc.Blocks.Add(last);existing=1;}
                if(last!=null){for(Inline inline=last.Inlines.LastInline;inline is LineBreak;inline=inline.PreviousInline)existing++;for(int i=existing;i<trailing;i++)last.Inlines.Add(new LineBreak());}}
            return doc;
        }
        static bool Formatted(MarkdownNode node){if(node.Kind!="paragraph" && node.Kind!="text" && node.Kind!="break")return true;foreach(var child in node.Children)if(Formatted(child))return true;return false;}
        internal Block Block(MarkdownNode node)
        {
            if (node.Kind == "codeblock")
            {
                var section = new Section { Tag = "code:"+node.Info, Background = Ui.Brush("#F4F5F7"), Padding = new Thickness(12), Margin = new Thickness(0, 5, 0, 12) };
                if (!editable && node.Info.Length > 0) section.Blocks.Add(new Paragraph(new Run(node.Info)) { Foreground = Ui.Muted, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
                section.Blocks.Add(new Paragraph(new Run(node.Text)) { FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0) }); return section;
            }
            if (node.Kind == "list" || node.Kind == "ordered")
            {
                var list = new List { MarkerStyle = node.Kind == "ordered" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, StartIndex = node.Start, Padding = new Thickness(22, 0, 0, 0), Margin = new Thickness(0, 0, 0, 9) };
                foreach (var item in node.Children) { var row = new ListItem { Margin = new Thickness(0, 2, 0, 2) }; foreach (var child in item.Children) row.Blocks.Add(Block(child)); list.ListItems.Add(row); }
                return list;
            }
            if (node.Kind == "table")
            {
                var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 12) }; var group = new TableRowGroup(); table.RowGroups.Add(group);
                int columns = node.Children.Count == 0 ? 0 : node.Children[0].Children.Count; for (int i = 0; i < columns; i++) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                foreach (var row in node.Children) { var rendered = new TableRow(); if (row.Header) { rendered.Background = Ui.Brush("#F2F4F7"); rendered.FontWeight = FontWeights.SemiBold; } foreach (var cell in row.Children) { var content = new TableCell { BorderBrush = Ui.Brush("#DDE1E7"), BorderThickness = new Thickness(.5), Padding = new Thickness(7, 5, 7, 5) }; foreach (var child in cell.Children) content.Blocks.Add(Block(child)); rendered.Cells.Add(content); } group.Rows.Add(rendered); }
                return table;
            }
            if (node.Kind == "quote" || node.Kind == "group" || node.Kind == "item")
            {
                var section = new Section { Tag=node.Kind, Margin = new Thickness(0, 3, 0, 9) }; if (node.Kind == "quote") { section.BorderBrush = Ui.Brush("#CDD3DD"); section.BorderThickness = new Thickness(3, 0, 0, 0); section.Padding = new Thickness(12, 2, 0, 2); section.Foreground = Ui.Muted; }
                foreach (var child in node.Children) section.Blocks.Add(Block(child)); return section;
            }
            var paragraph = new Paragraph { Tag=node.Kind=="heading"?"heading:"+node.Level:node.Kind, Margin = new Thickness(0, 0, 0, 8) };
            if (node.Kind == "rule") { paragraph.BorderBrush = Ui.Brush("#E2E5EA"); paragraph.BorderThickness = new Thickness(0, 0, 0, 1); paragraph.Margin = new Thickness(0, 8, 0, 12); }
            else { Inline(paragraph.Inlines, node.Children); if (node.Kind == "heading") { paragraph.FontSize = node.Level == 1 ? 23 : node.Level == 2 ? 20 : node.Level == 3 ? 17 : 15; paragraph.FontWeight = FontWeights.SemiBold; paragraph.Margin = new Thickness(0, 8, 0, 9); } }
            return paragraph;
        }
        static void OpenLink(Uri uri)
        { try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { } }
        void Inline(InlineCollection target, IEnumerable<MarkdownNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Kind == "break") { target.Add(new LineBreak()); continue; }
                if (node.Kind == "bold" || node.Kind == "italic" || node.Kind == "strike")
                { Span span = node.Kind == "bold" ? (Span)new Bold() : node.Kind == "italic" ? new Italic() : new Span { TextDecorations = TextDecorations.Strikethrough }; Inline(span.Inlines, node.Children); target.Add(span); continue; }
                if (node.Kind == "link" || node.Kind == "image")
                {
                    Uri uri; if (MarkdownContent.TryLink(node.Url, out uri))
                    {
                        var linkInk=ThemeManager.Brush("#456DE6","#91B5FF");
                        var link = new Hyperlink { Tag=node.AutoLink?"autolink":node.Kind, NavigateUri = uri, Foreground = linkInk, TextDecorations = null, ToolTip = editable?"Ctrl + 单击打开链接\n"+uri.AbsoluteUri:uri.AbsoluteUri };
                        if(!editable)link.ContextMenu=MarkdownLinkMenu.Create(link);
                        bool previewImage=node.Kind=="image"&&!editable&&(uri.Scheme=="http"||uri.Scheme=="https");
                        if(previewImage)link.ToolTip="点击预览图片（仅点击时加载）\n"+uri.AbsoluteUri;
                        link.RequestNavigate += delegate (object sender, System.Windows.Navigation.RequestNavigateEventArgs e) { if(previewImage){var host=Window.GetWindow(link);if(host!=null)ChatImagePreview.OpenRemote(host,e.Uri,new System.Windows.Documents.TextRange(link.ContentStart,link.ContentEnd).Text);}else if(!editable || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control)!=0)OpenLink(e.Uri); e.Handled = true; };
                        string iconName = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ? "github" : "globe";
                        var icon = UiIcons.Image(iconName, linkInk); icon.Name = iconName == "github" ? "MarkdownGitHubIcon" : "MarkdownGlobeIcon"; icon.Width = icon.Height = 14; icon.Margin = new Thickness(0, 0, 5, 0); icon.IsHitTestVisible=false;icon.Focusable=false;link.Inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center,Focusable=false });
                        if (node.Children.Count > 0) Inline(link.Inlines, node.Children); else link.Inlines.Add(new Run(node.Text.Length > 0 ? node.Text : uri.AbsoluteUri));
                        target.Add(link); continue;
                    }
                    Inline(target, node.Children); continue;
                }
                if (node.Text.Length > 0) { var run = new Run(node.Text); if (node.Kind == "code") { run.Tag="code"; run.FontFamily = new FontFamily("Consolas"); run.Background = Ui.Brush("#EFF1F5"); run.FontSize = 12; } target.Add(run); }
                if (node.Children.Count > 0) Inline(target, node.Children);
            }
        }
    }
}
