using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    // Only the matching reply receives the latest task's actual change record.
    internal sealed class AgentChangeSummaryCard : Border
    {
        readonly StackPanel body=new StackPanel();readonly Action<string> preview;AgentTaskState task;bool expanded;
        internal AgentChangeSummaryCard(Action<string> show){preview=show;Name="AgentChangeSummary";Child=body;Background=System.Windows.Media.Brushes.White;BorderBrush=AgentUi.Line;BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(13);Padding=new Thickness(15);Margin=new Thickness(2,0,2,20);Visibility=Visibility.Collapsed;}
        internal void Refresh(AgentTaskState snapshot)
        {
            if(task==null || snapshot==null || task.Id!=snapshot.Id)expanded=false;task=snapshot;body.Children.Clear();
            if(task==null || task.Status=="running" || task.Changes.Count==0){Visibility=Visibility.Collapsed;return;}Visibility=Visibility.Visible;
            var files=task.Changes.GroupBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToList();var active=files.Where(c=>c.Status!="restored").ToList();var header=new DockPanel();
            var details=AgentUi.Nav("查看差异","OpenChangeSummary");details.FontSize=11;details.Click+=delegate{if(active.Count>0)preview(active[0].Path);};details.IsEnabled=active.Count>0;DockPanel.SetDock(details,Dock.Right);header.Children.Add(details);
            var title=new StackPanel();var text=Ui.Text("已编辑 "+files.Count+" 个文件",14,AgentUi.Ink);text.FontWeight=FontWeights.SemiBold;title.Children.Add(text);
            if(active.Count>0 && active.All(c=>c.AddedLines.HasValue && c.RemovedLines.HasValue))title.Children.Add(Counts(active.Sum(c=>c.AddedLines.Value),active.Sum(c=>c.RemovedLines.Value)));else title.Children.Add(Ui.Text(active.Count==0?"所有文件均已恢复":"部分文件未提供行数统计",11,AgentUi.Muted));header.Children.Add(title);body.Children.Add(header);
            body.Children.Add(new Separator{Margin=new Thickness(0,10,0,8),Background=AgentUi.Line});
            foreach(var change in files.Take(expanded?256:3)){
                var row=new DockPanel{Margin=new Thickness(0,3,0,3)};if(change.AddedLines.HasValue && change.RemovedLines.HasValue && change.Status!="restored"){var counts=Counts(change.AddedLines.Value,change.RemovedLines.Value);DockPanel.SetDock(counts,Dock.Right);row.Children.Add(counts);}
                string root=task.Workspace.TrimEnd('\\','/')+Path.DirectorySeparatorChar;string name=change.Path.StartsWith(root,StringComparison.OrdinalIgnoreCase)?change.Path.Substring(root.Length):change.Path;
                var file=AgentUi.Nav(name+(change.Status=="restored"?"（已恢复）":""),"SummaryChangedFile");file.FontSize=12;file.MinHeight=27;file.Padding=new Thickness(0,4,8,4);file.ToolTip=name;file.IsEnabled=change.Status!="restored";string path=change.Path;file.Click+=delegate{preview(path);};row.Children.Add(file);body.Children.Add(row);
            }
            if(files.Count>3){var more=AgentUi.Nav(expanded?"收起文件列表":"再显示 "+(files.Count-3)+" 个文件","ExpandChangeSummary");more.FontSize=12;more.Click+=delegate{expanded=!expanded;Refresh(task);};body.Children.Add(more);}
        }
        static StackPanel Counts(int added,int removed){var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(Ui.Text("+"+added,12,Ui.Brush("#159A5A")));var minus=Ui.Text("−"+removed,12,Ui.Brush("#D65151"));minus.Margin=new Thickness(9,0,0,0);row.Children.Add(minus);return row;}
    }
}
