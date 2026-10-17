using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class MarkdownView : RichTextBox
    {
        string text="";
        public string Text {get{return text;}set{value=value??"";if(text==value)return;text=value;var doc=new FlowDocument{PagePadding=new Thickness(0),FontFamily=FontFamily,FontSize=14,Foreground=Foreground,ColumnWidth=Double.PositiveInfinity};foreach(var node in MarkdownContent.Parse(value))doc.Blocks.Add(Block(node));Document=doc;}}
        public MarkdownView()
        {IsReadOnly=true;IsDocumentEnabled=true;BorderThickness=new Thickness(0);Background=Brushes.Transparent;Foreground=Ui.Ink;Padding=new Thickness(0);VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;ContextMenu=ChatUi.EditorMenu(this,true);}
        static Block Block(MarkdownNode node)
        {
            if(node.Kind=="codeblock")
            {
                var section=new Section{Background=Ui.Brush("#F4F5F7"),Padding=new Thickness(12),Margin=new Thickness(0,5,0,12)};
                if(node.Info.Length>0)section.Blocks.Add(new Paragraph(new Run(node.Info)){Foreground=Ui.Muted,FontSize=11,Margin=new Thickness(0,0,0,6)});
                section.Blocks.Add(new Paragraph(new Run(node.Text)){FontFamily=new FontFamily("Consolas"),FontSize=12,Margin=new Thickness(0)});return section;
            }
            if(node.Kind=="list" || node.Kind=="ordered")
            {
                var list=new List{MarkerStyle=node.Kind=="ordered"?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,StartIndex=node.Start,Padding=new Thickness(22,0,0,0),Margin=new Thickness(0,0,0,9)};
                foreach(var item in node.Children){var row=new ListItem{Margin=new Thickness(0,2,0,2)};foreach(var child in item.Children)row.Blocks.Add(Block(child));list.ListItems.Add(row);}return list;
            }
            if(node.Kind=="table")
            {
                var table=new Table{CellSpacing=0,Margin=new Thickness(0,6,0,12)};var group=new TableRowGroup();table.RowGroups.Add(group);
                int columns=node.Children.Count==0?0:node.Children[0].Children.Count;for(int i=0;i<columns;i++)table.Columns.Add(new TableColumn{Width=new GridLength(1,GridUnitType.Star)});
                foreach(var row in node.Children){var rendered=new TableRow();if(row.Header){rendered.Background=Ui.Brush("#F2F4F7");rendered.FontWeight=FontWeights.SemiBold;}foreach(var cell in row.Children){var content=new TableCell{BorderBrush=Ui.Brush("#DDE1E7"),BorderThickness=new Thickness(.5),Padding=new Thickness(7,5,7,5)};foreach(var child in cell.Children)content.Blocks.Add(Block(child));rendered.Cells.Add(content);}group.Rows.Add(rendered);}return table;
            }
            if(node.Kind=="quote" || node.Kind=="group" || node.Kind=="item")
            {
                var section=new Section{Margin=new Thickness(0,3,0,9)};if(node.Kind=="quote"){section.BorderBrush=Ui.Brush("#CDD3DD");section.BorderThickness=new Thickness(3,0,0,0);section.Padding=new Thickness(12,2,0,2);section.Foreground=Ui.Muted;}foreach(var child in node.Children)section.Blocks.Add(Block(child));return section;
            }
            var paragraph=new Paragraph{Margin=new Thickness(0,0,0,8)};
            if(node.Kind=="rule"){paragraph.BorderBrush=Ui.Brush("#E2E5EA");paragraph.BorderThickness=new Thickness(0,0,0,1);paragraph.Margin=new Thickness(0,8,0,12);}
            else{Inline(paragraph.Inlines,node.Children);if(node.Kind=="heading"){paragraph.FontSize=node.Level==1?23:node.Level==2?20:node.Level==3?17:15;paragraph.FontWeight=FontWeights.SemiBold;paragraph.Margin=new Thickness(0,8,0,9);}}
            return paragraph;
        }
        static void Inline(InlineCollection target,IEnumerable<MarkdownNode> nodes)
        {
            foreach(var node in nodes)
            {
                if(node.Kind=="break"){target.Add(new LineBreak());continue;}
                if(node.Kind=="bold" || node.Kind=="italic" || node.Kind=="strike")
                {Span span=node.Kind=="bold"?(Span)new Bold():node.Kind=="italic"?new Italic():new Span{TextDecorations=TextDecorations.Strikethrough};Inline(span.Inlines,node.Children);target.Add(span);continue;}
                if(node.Kind=="link" || node.Kind=="image")
                {
                    Uri uri;if(MarkdownContent.TryLink(node.Url,out uri))
                    {
                        string label=node.Text.Length>0?node.Text:(node.Kind=="image"?"图片":uri.Host);var chip=new Border{Background=Ui.Brush("#F3F7FF"),BorderBrush=Ui.Brush("#B8C8F4"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(8,3,8,3),Margin=new Thickness(1,0,1,0),Cursor=System.Windows.Input.Cursors.Hand,ToolTip=node.Url,Tag=uri};
                        var content=new StackPanel{Orientation=Orientation.Horizontal};var icon=UiIcons.Image("link",Ui.Blue);icon.Width=icon.Height=14;icon.Margin=new Thickness(0,0,6,0);content.Children.Add(icon);var text=Ui.Text(label,12,Ui.Blue);text.TextWrapping=TextWrapping.Wrap;content.Children.Add(text);chip.Child=content;
                        chip.MouseLeftButtonUp+=delegate{try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}};target.Add(new InlineUIContainer(chip));Inline(target,node.Children);continue;
                    }
                    Inline(target,node.Children);continue;
                }                if(node.Text.Length>0){var run=new Run(node.Text);if(node.Kind=="code"){run.FontFamily=new FontFamily("Consolas");run.Background=Ui.Brush("#EFF1F5");run.FontSize=12;}target.Add(run);}if(node.Children.Count>0)Inline(target,node.Children);
            }
        }
    }
}
