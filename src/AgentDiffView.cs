using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed class AgentDiffView : UserControl
    {
        internal AgentDiffView(IEnumerable<AgentDiffLine> lines,bool unknown,bool truncated)
        {
            Name="AgentDiffView";var body=new StackPanel();
            if(unknown)body.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("agentDiffView.agentDiffView.text")),11,AgentUi.Muted));
            if(truncated)body.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("agentDiffView.agentDiffView.text.alternativeA")),11,AgentUi.Muted));
            foreach(var line in (lines??new List<AgentDiffLine>()))
            {
                var row=new Grid{Name=line.Kind=="added"?"AgentDiffAdded":line.Kind=="removed"?"AgentDiffRemoved":"AgentDiffContext",Margin=new Thickness(0,1,0,1)};
                row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Background=line.Kind=="added"?Ui.Brush("#E8F7ED"):line.Kind=="removed"?Ui.Brush("#FDECEC"):Brushes.Transparent;
                string number=line.BeforeLine.HasValue?line.BeforeLine.Value.ToString():line.AfterLine.HasValue?line.AfterLine.Value.ToString():"";var label=Ui.Text(number,11,AgentUi.Muted);label.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(label,0);row.Children.Add(label);
                var text=Ui.Text((line.Kind=="added"?"+":line.Kind=="removed"?"-":" ")+(line.Text??""),12,line.Kind=="added"?Ui.Brush("#157A45"):line.Kind=="removed"?Ui.Brush("#B4413E"):AgentUi.Ink);text.Name="AgentDiffLineText";text.FontFamily=new FontFamily("Consolas, Microsoft YaHei UI");text.TextWrapping=TextWrapping.NoWrap;Grid.SetColumn(text,1);row.Children.Add(text);
                body.Children.Add(row);
            }
            Content=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=body};
        }
    }
}
