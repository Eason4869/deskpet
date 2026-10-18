using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class MarkdownView : RichTextBox
    {
        string text = "";
        internal bool FitContentWidth;
        double naturalWidth = Double.NaN;
        internal bool HasFormatting { get; private set; }
        public string Text { get { return text; } set { value=value??"";if(text==value)return;text=value;naturalWidth=Double.NaN;bool formatted;Document=MarkdownDocument.Create(value,FontFamily,Foreground,false,out formatted);HasFormatting=formatted; } }
        protected override Size MeasureOverride(Size constraint)
        {
            if (!FitContentWidth) return base.MeasureOverride(constraint);
            if (Double.IsNaN(naturalWidth)) naturalWidth = NaturalWidth(Document.Blocks);
            double width = Math.Min(constraint.Width, Math.Max(1, Math.Ceiling(naturalWidth) + 12));
            return base.MeasureOverride(new Size(width, constraint.Height));
        }
        static double NaturalWidth(BlockCollection blocks)
        {
            double width = 0;
            foreach (Block block in blocks)
            {
                double current; var paragraph = block as Paragraph; var section = block as Section;var list=block as System.Windows.Documents.List;var table=block as Table;
                if (paragraph != null)
                {
                    var measure = new TextBlock { FontFamily = paragraph.FontFamily, FontSize = paragraph.FontSize, FontWeight = paragraph.FontWeight, FontStyle = paragraph.FontStyle, TextWrapping = TextWrapping.NoWrap };
                    foreach (Inline inline in paragraph.Inlines) measure.Inlines.Add(MeasureInline(inline));
                    measure.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity)); current = measure.DesiredSize.Width;
                }
                else if (section != null) current = NaturalWidth(section.Blocks);
                else if(list!=null){current=0;foreach(var item in list.ListItems)current=Math.Max(current,NaturalWidth(item.Blocks)+24);}
                else if(table!=null)
                {
                    var columns=new double[table.Columns.Count];
                    foreach(var group in table.RowGroups)foreach(var row in group.Rows)
                    {
                        int column=0;foreach(var cell in row.Cells){if(column<columns.Length)columns[column]=Math.Max(columns[column],NaturalWidth(cell.Blocks)+Horizontal(cell.Padding)+Horizontal(cell.BorderThickness));column++;}
                    }
                    current=table.CellSpacing*(columns.Length+1);foreach(double column in columns)current+=column;
                }
                else return Double.PositiveInfinity;
                width = Math.Max(width, current + Horizontal(block.Padding)+Horizontal(block.Margin)+Horizontal(block.BorderThickness));
            }
            return width;
        }
        static double Horizontal(Thickness value){return (Double.IsNaN(value.Left)?0:value.Left)+(Double.IsNaN(value.Right)?0:value.Right);}
        static Inline MeasureInline(Inline inline)
        {
            Inline result; var run = inline as Run; var span = inline as Span; var element = inline as InlineUIContainer;
            if (run != null) result = new Run(run.Text);
            else if (span != null) { var copy = new Span(); foreach (Inline child in span.Inlines) copy.Inlines.Add(MeasureInline(child)); result = copy; }
            else if (element != null && element.Child != null) { element.Child.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity)); result = new InlineUIContainer(new Border { Width = element.Child.DesiredSize.Width, Height = element.Child.DesiredSize.Height }); }
            else result = new LineBreak();
            result.FontFamily = inline.FontFamily; result.FontSize = inline.FontSize; result.FontWeight = inline.FontWeight; result.FontStyle = inline.FontStyle; result.FontStretch = inline.FontStretch; return result;
        }
        public MarkdownView()
        { IsReadOnly = true; IsDocumentEnabled = true; BorderThickness = new Thickness(0); Background = Brushes.Transparent; Foreground = Ui.Ink; Padding = new Thickness(0); VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; ContextMenu = ChatUi.EditorMenu(this, true); }
    }
}
