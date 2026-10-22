using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    // Both live and completed summaries describe the last recorded state of
    // each path. Restored and byte-identical originals remain in the audit list
    // while their net change contributes neither files nor lines to the totals.
    internal sealed class AgentChangeSummaryData
    {
        internal IList<AgentChange> Files{get;private set;}
        internal IList<AgentChange> ActiveFiles{get;private set;}
        internal int FileCount{get{return ActiveFiles.Count;}}
        internal int RecordedFileCount{get{return Files.Count;}}
        internal long? AddedLines{get;private set;}
        internal long? RemovedLines{get;private set;}
        internal static AgentChangeSummaryData FromTask(AgentTaskState task)
        {
            var files=((task==null?null:task.Changes)??new List<AgentChange>()).Where(c=>c!=null&&!String.IsNullOrWhiteSpace(c.Path)).GroupBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.Last().Copy()).ToList();
            var active=files.Where(IsActive).ToList();bool known=active.All(c=>c.AddedLines.HasValue&&c.RemovedLines.HasValue&&c.AddedLines.Value>=0&&c.RemovedLines.Value>=0);
            return new AgentChangeSummaryData{Files=files.AsReadOnly(),ActiveFiles=active.AsReadOnly(),AddedLines=known?(long?)active.Sum(c=>(long)c.AddedLines.Value):null,RemovedLines=known?(long?)active.Sum(c=>(long)c.RemovedLines.Value):null};
        }
        internal static bool IsActive(AgentChange change){return change.Status!="restored"&&(String.IsNullOrEmpty(change.BeforeHash)||String.IsNullOrEmpty(change.AfterHash)||!String.Equals(change.BeforeHash,change.AfterHash,StringComparison.OrdinalIgnoreCase));}
        internal static AgentTaskState CopyTask(AgentTaskState task)
        {
            if(task==null)return null;
            return new AgentTaskState{Id=task.Id,PersonaId=task.PersonaId,Workspace=task.Workspace,Status=task.Status,Changes=(task.Changes??new List<AgentChange>()).Where(c=>c!=null).Select(c=>c.Copy()).ToList()};
        }
    }
    // Only the matching reply receives the latest task's actual change record.
    internal sealed class AgentChangeSummaryCard : Border
    {
        readonly StackPanel body=new StackPanel();readonly Action<string> preview;readonly Action<AgentChange> restore;AgentTaskState task;bool expanded,allowRunning;
        internal AgentChangeSummaryCard(Action<string> show,Action<AgentChange> restore=null){preview=show;this.restore=restore;Name="AgentChangeSummary";Child=body;Background=ThemeManager.Surface;BorderBrush=AgentUi.Line;BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(13);Padding=new Thickness(15);Margin=new Thickness(0,12,0,0);Visibility=Visibility.Collapsed;}
        internal void Refresh(AgentTaskState snapshot,bool includeRunning=false)
        {
            if(task==null || snapshot==null || task.Id!=snapshot.Id)expanded=false;task=AgentChangeSummaryData.CopyTask(snapshot);allowRunning=includeRunning;body.Children.Clear();var summary=AgentChangeSummaryData.FromTask(task);
            if(task==null || !includeRunning&&task.Status=="running" || summary.RecordedFileCount==0){Visibility=Visibility.Collapsed;return;}Visibility=Visibility.Visible;
            var files=summary.Files;var active=summary.ActiveFiles;var header=new DockPanel();
            var details=AgentUi.Nav("查看差异","OpenChangeSummary");details.FontSize=11;details.Click+=delegate{if(active.Count>0&&preview!=null)preview(active[0].Path);};details.IsEnabled=active.Count>0&&preview!=null;DockPanel.SetDock(details,Dock.Right);header.Children.Add(details);
            var title=new StackPanel();var text=Ui.Text(summary.FileCount>0?"已编辑 "+summary.FileCount+" 个文件":"没有未恢复的净变更",14,AgentUi.Ink);text.Name="ChangeSummaryFileCount";text.FontWeight=FontWeights.SemiBold;title.Children.Add(text);
            if(active.Count>0 && summary.AddedLines.HasValue&&summary.RemovedLines.HasValue)title.Children.Add(Counts(summary.AddedLines.Value,summary.RemovedLines.Value));else title.Children.Add(Ui.Text(active.Count==0?files.All(c=>c.Status=="restored")?"所有文件均已恢复":"原文件内容保持不变":"部分文件未提供行数统计",11,AgentUi.Muted));header.Children.Add(title);body.Children.Add(header);
            body.Children.Add(new Separator{Margin=new Thickness(0,10,0,8),Background=AgentUi.Line});
            foreach(var change in files.Take(expanded?256:3)){
                var row=new DockPanel{Margin=new Thickness(0,3,0,3)};
                if(restore!=null){var undo=AgentUi.Nav("撤销","RestoreSummaryFile");undo.FontSize=11;undo.Margin=new Thickness(10,0,0,0);undo.IsEnabled=task.Status!="running"&&AgentChangeSummaryData.IsActive(change)&&!String.IsNullOrEmpty(change.BeforeHash)&&!String.IsNullOrEmpty(change.BackupPath);undo.Click+=delegate{restore(change.Copy());};DockPanel.SetDock(undo,Dock.Right);row.Children.Add(undo);}
                if(change.AddedLines.HasValue && change.RemovedLines.HasValue && change.AddedLines.Value>=0 && change.RemovedLines.Value>=0 && AgentChangeSummaryData.IsActive(change)){var counts=Counts(change.AddedLines.Value,change.RemovedLines.Value);DockPanel.SetDock(counts,Dock.Right);row.Children.Add(counts);}
                string root=(task.Workspace??"").TrimEnd('\\','/')+Path.DirectorySeparatorChar;string name=change.Path.StartsWith(root,StringComparison.OrdinalIgnoreCase)?change.Path.Substring(root.Length):change.Path;
                var file=AgentUi.Nav(name+(change.Status=="restored"?"（已恢复）":""),"SummaryChangedFile");file.FontSize=12;file.MinHeight=27;file.Padding=new Thickness(0,4,8,4);file.ToolTip=name;file.IsEnabled=change.Status!="restored"&&preview!=null;string path=change.Path;file.Click+=delegate{if(preview!=null)preview(path);};row.Children.Add(file);body.Children.Add(row);
            }
            if(files.Count>3){var more=AgentUi.Nav(expanded?"收起文件列表":"再显示 "+(files.Count-3)+" 个文件","ExpandChangeSummary");more.FontSize=12;more.Click+=delegate{expanded=!expanded;Refresh(task,allowRunning);};body.Children.Add(more);}
        }
        static StackPanel Counts(long added,long removed){var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(Ui.Text("+"+added,12,Ui.Brush("#159A5A")));var minus=Ui.Text("−"+removed,12,Ui.Brush("#D65151"));minus.Margin=new Thickness(9,0,0,0);row.Children.Add(minus);return row;}
    }
}
