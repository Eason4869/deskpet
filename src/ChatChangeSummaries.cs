using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        LiveChangeSummary liveChanges;
        void RefreshChangeSummaries(AgentTaskState currentTask)
        {
            if(liveChanges!=null)liveChanges.Refresh(currentTask,WorkEntry&&session.IsBusy);
            jumpToLatest.Margin=new Thickness(0,0,0,liveChanges!=null&&liveChanges.Visibility==Visibility.Visible?8:0);
            var sourceSession=session;
            foreach(var message in session.History.Messages.Where(m=>m.Role=="assistant"))
            {
                ChatMessageView view;if(!rendered.TryGetValue(message.Id,out view))continue;
                var task=message.ChangeSummary!=null&&message.ChangeSummary.TaskId==message.AgentTaskId?message.ChangeSummary.ToTask():currentTask!=null&&currentTask.Id==message.AgentTaskId?currentTask:null;
                bool undo=task!=null&&currentTask!=null&&task.Id==currentTask.Id&&!session.IsBusy&&currentTask.Status!="running";
                view.RefreshChangeSummary(message.Status=="pending"?null:task,(snapshot,path)=>{var ignored=ShowRecordedChangeAsync(snapshot,path);},undo?(Action<AgentTaskState,AgentChange>)((snapshot,change)=>{var ignored=RestoreSummaryChangeAsync(sourceSession,snapshot,change);}):null);
            }
        }
        void OpenTaskChanges(AgentTaskState task)
        {
            if(task==null||closed)return;
            var snapshot=AgentChangeSummaryData.CopyTask(task);var card=new AgentChangeSummaryCard(path=>{var ignored=ShowRecordedChangeAsync(snapshot,path);});card.Refresh(snapshot,true);OpenDetailTab(TextResources.Get("chatChangeSummaries.openTaskChanges.openDetailTab"),Ui.Scroll(card));
        }
        async Task RestoreSummaryChangeAsync(ConversationSession source,AgentTaskState snapshot,AgentChange change)
        {
            try
            {
                var current=session.CurrentAgentTask;
                if(!ReferenceEquals(source,session)||current==null||current.Id!=snapshot.Id||session.IsBusy)throw new InvalidOperationException(TextResources.Get("chatChangeSummaries.restoreSummaryChangeAsync.text"));
                await source.RestoreAgentFileAsync(change.Path,TaskSettings());
            }
            catch(Exception error){session.ReportNotice(error is InvalidOperationException||error is ArgumentException?error.Message:TextResources.Get("chatChangeSummaries.restoreSummaryChangeAsync.sessionReportNotice"));}
        }
        async Task ShowRecordedChangeAsync(AgentTaskState snapshot,string path)
        {
            var source=session;var settings=TaskSettings();
            try
            {
                if(!String.Equals(Path.GetFullPath(settings.AgentWorkspace).TrimEnd('\\','/'),Path.GetFullPath(snapshot.Workspace).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Get("chatChangeSummaries.showRecordedChangeAsync.text"));
                // Historical summaries retain their own baseline; never substitute the newest task.
                var task=AgentChangeSummaryData.CopyTask(snapshot);string selectedPath=Path.GetFullPath(path);var record=task.Changes.LastOrDefault(c=>String.Equals(Path.GetFullPath(c.Path),selectedPath,StringComparison.OrdinalIgnoreCase));if(record==null)throw new InvalidOperationException(TextResources.Get("chatChangeSummaries.showRecordedChangeAsync.text.alternativeA"));
                task.Changes=new System.Collections.Generic.List<AgentChange>{record};
                string root=Path.GetFullPath(task.Workspace).TrimEnd('\\','/');bool inside=selectedPath.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
                bool restricted=inside&&(String.IsNullOrEmpty(record.BackupPath)||String.Equals(Path.GetDirectoryName(Path.GetFullPath(record.BackupPath)),Path.Combine(root,".deskpet-agent-backups"),StringComparison.OrdinalIgnoreCase));
                var preview=await Task.Run(delegate{using(var files=new AgentWorkspace(task.Workspace,restricted){ReadOnly=true,BackupEnabled=false}){files.BeginTask(task);return files.PreviewChange(selectedPath,CancellationToken.None);}});
                await Dispatcher.InvokeAsync(new Action(delegate{if(!closed&&ReferenceEquals(source,session))OpenDetailTab(TextResources.Format("chatChangeSummaries.showRecordedChangeAsync.openDetailTab",Path.GetFileName(path)),AgentChangePreviewContent.Create(preview));}));
            }
            catch(Exception error){if(!closed)source.ReportNotice(error is InvalidOperationException||error is ArgumentException?error.Message:TextResources.Get("chatChangeSummaries.showRecordedChangeAsync.sourceReportNotice"));}
        }
    }
    internal static class AgentChangePreviewContent
    {
        internal static FrameworkElement Create(AgentChangePreview preview)
        {
            var root=new DockPanel{Margin=new Thickness(18)};var info=LocalizedUi.Text(()=>(preview.Path+(preview.ChangedAgain?TextResources.Get("chatChangeSummaries.create.text"):"")+(preview.Truncated?TextResources.Get("chatChangeSummaries.create.text.alternativeA"):"")),11,Ui.Muted);DockPanel.SetDock(info,Dock.Top);root.Children.Add(info);
            var grid=new Grid{Margin=new Thickness(0,12,0,0),MaxHeight=240};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<2;i++){var column=new DockPanel{Margin=i==0?new Thickness(0,0,8,0):new Thickness(8,0,0,0)};var title=LocalizedUi.Text(()=>(i==0?TextResources.Get("chatChangeSummaries.create.text.alternativeB"):TextResources.Get("chatChangeSummaries.create.text.alternativeC")),12,Ui.Muted);DockPanel.SetDock(title,Dock.Top);column.Children.Add(title);var text=new TextBox{Text=i==0?preview.Before:preview.Current,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new System.Windows.Media.FontFamily("Consolas, Microsoft YaHei UI"),FontSize=12,Padding=new Thickness(8)};text.ContextMenu=ChatUi.EditorMenu(text,true);column.Children.Add(text);Grid.SetColumn(column,i);grid.Children.Add(column);}
            var raw=LocalizedUi.With(new Expander{Header=TextResources.Get("chatChangeSummaries.create.header"),Content=grid,IsExpanded=false,Margin=new Thickness(0,8,0,0)},"Header",()=>(TextResources.Get("chatChangeSummaries.create.header")));ChatUi.ProcessExpander(raw);DockPanel.SetDock(raw,Dock.Bottom);root.Children.Add(raw);root.Children.Add(new AgentDiffView(preview.Diff,preview.Unknown,preview.Truncated));return root;
        }
    }
}
