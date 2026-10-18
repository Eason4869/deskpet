using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed partial class ChatMessageView
    {
        readonly ContentControl changeSummaryHost=new ContentControl{Name="ReplyChangeSummary"};
        readonly StackPanel outputFiles=new StackPanel{Name="ReplyOutputFiles",Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,2)};
        string outputContent,outputAttachments,outputRoot;
        internal void SetChangeSummary(AgentChangeSummaryCard card){changeSummaryHost.Content=card;}
        AgentChangeSummaryCard ownChangeSummary;AgentTaskState replyChangeTask;bool summaryUndoEnabled;string summarySignature;
        Action<AgentTaskState,string> showRecordedChange;Action<AgentTaskState,AgentChange> restoreRecordedChange;
        internal void RefreshChangeSummary(AgentTaskState snapshot,Action<AgentTaskState,string> show,Action<AgentTaskState,AgentChange> restore)
        {
            replyChangeTask=snapshot==null?null:AgentChangeSummaryData.CopyTask(snapshot);showRecordedChange=show;restoreRecordedChange=restore;
            bool undo=restore!=null;
            if(snapshot==null&&ownChangeSummary==null)return;
            string signature=snapshot==null?"":snapshot.Id+"|"+snapshot.Workspace+"|"+snapshot.Status+"|"+undo+"|"+String.Join(";",(snapshot.Changes??new System.Collections.Generic.List<AgentChange>()).Select(c=>c.Path+"|"+c.BeforeHash+"|"+c.AfterHash+"|"+c.Status+"|"+c.BackupPath+"|"+c.AddedLines+"|"+c.RemovedLines));
            if(summarySignature==signature)return;summarySignature=signature;
            if(ownChangeSummary==null||summaryUndoEnabled!=undo){summaryUndoEnabled=undo;ownChangeSummary=new AgentChangeSummaryCard(path=>{if(replyChangeTask!=null&&showRecordedChange!=null)showRecordedChange(AgentChangeSummaryData.CopyTask(replyChangeTask),path);},undo?(Action<AgentChange>)(change=>{if(replyChangeTask!=null&&restoreRecordedChange!=null)restoreRecordedChange(AgentChangeSummaryData.CopyTask(replyChangeTask),change);}):null);changeSummaryHost.Content=ownChangeSummary;}
            ownChangeSummary.Refresh(replyChangeTask);if(current!=null)UpdateOutputFiles(current);
        }
        internal void RefreshOutputFiles(){if(current!=null)UpdateOutputFiles(current);}
        void UpdateOutputFiles(ChatMessage message)
        {
            if(user)return;if(message.Status!="complete"){outputFiles.Children.Clear();outputFiles.Visibility=Visibility.Collapsed;outputContent=null;return;}
            var task=session.CurrentAgentTask;string root=message.ChangeSummary!=null&&message.ChangeSummary.TaskId==message.AgentTaskId?message.ChangeSummary.Workspace:replyChangeTask!=null&&replyChangeTask.Id==message.AgentTaskId?replyChangeTask.Workspace:task!=null&&task.Id==message.AgentTaskId?task.Workspace:null;
            string attachments=String.Join("|",(message.Attachments??new System.Collections.Generic.List<ChatAttachment>()).Select(f=>f.Name+"\n"+f.SourcePath+"\n"+(f.Data??"").GetHashCode()+"\n"+(f.Text??"").GetHashCode()));if(outputContent==message.Content&&outputAttachments==attachments&&outputRoot==root)return;outputContent=message.Content;outputAttachments=attachments;outputRoot=root;outputFiles.Children.Clear();
            var paths=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in message.Attachments??new System.Collections.Generic.List<ChatAttachment>()){if(!String.IsNullOrEmpty(file.SourcePath))paths.Add(file.SourcePath);if(file.IsImage){var picture=AttachmentCard(file);picture.Name="OutputFileCard";picture.HorizontalAlignment=HorizontalAlignment.Left;picture.Margin=new Thickness(0,0,0,8);outputFiles.Children.Add(picture);}else outputFiles.Children.Add(OutputCard(file.Name,file.MediaType??"文件",file.SourcePath,AttachmentMenu(file),()=>OutputAction(()=>FilePreviewLauncher.Show(host,AttachmentPath(file),pet)),false));}
            foreach(var file in ReplyFileReference.Parse(message.Content,root))if(paths.Add(file.Path))outputFiles.Children.Add(OutputCard(file.Name,!file.Available?"文件已移动或删除":file.Web?"网页":System.IO.Path.GetExtension(file.Path).Equals(".zip",StringComparison.OrdinalIgnoreCase)?"压缩包":"文件",file.Path,OutputMenu(file),()=>OutputAction(()=>FilePreviewLauncher.Show(host,file.Path,pet)),file.Web));
            outputFiles.Visibility=outputFiles.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
        }
        Border OutputCard(string name,string detail,string path,ContextMenu menu,Action open,bool web)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});
            var icon=UiIcons.Image(web?"globe":"file",web?Ui.Blue:AgentUi.Muted);icon.Width=icon.Height=22;row.Children.Add(new Border{Width=36,Height=36,Background=Ui.Brush(web?"#E8EEFB":"#EDEEF0"),CornerRadius=new CornerRadius(10),Child=icon,HorizontalAlignment=HorizontalAlignment.Left});
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var title=Ui.Text(name,13,AgentUi.Ink);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(title);var type=Ui.Text(detail,11,AgentUi.Muted);type.TextWrapping=TextWrapping.NoWrap;type.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(type);Grid.SetColumn(text,1);row.Children.Add(text);var arrow=UiIcons.Image("expand",AgentUi.Muted);arrow.Width=arrow.Height=15;Grid.SetColumn(arrow,2);row.Children.Add(arrow);
            var card=new Border{Name="OutputFileCard",Child=row,Background=Ui.Brush("#F7F7F6"),BorderBrush=Ui.Brush("#ECEBE8"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,0,0,8),ToolTip=path??name,ContextMenu=menu,Cursor=Cursors.Hand,Focusable=true};card.MouseLeftButtonUp+=delegate{open();};card.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter||e.Key==Key.Space){e.Handled=true;open();}};return card;
        }
        static void OpenOutput(ReplyFileReference file){if(!file.Available)throw new FileNotFoundException("交付文件已移动或删除。",file.Path);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file.Path){UseShellExecute=true});}
        ContextMenu OutputMenu(ReplyFileReference file)
        {
            var menu=FileActionsMenu.Create(()=>OutputAction(()=>OpenOutput(file)),()=>OutputAction(()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe","shell32.dll,OpenAs_RunDLL \""+file.Path+"\""){UseShellExecute=true})),()=>OutputAction(()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+file.Path+"\""){UseShellExecute=true})),()=>OutputAction(()=>Clipboard.SetText(file.Path)),()=>OutputAction(()=>Clipboard.SetText(file.ReadText())),()=>{var picker=new Microsoft.Win32.SaveFileDialog{FileName=file.Name,Filter="所有文件|*.*"};if(picker.ShowDialog(host)==true)OutputAction(()=>{file.SaveTo(picker.FileName);session.ReportNotice("文件已保存。");});},file.Available,file.TextFile);
            menu.Opened+=delegate{foreach(MenuItem item in menu.Items){string title=item.Header as string;item.IsEnabled=title=="复制路径"||file.Available&&(title!="复制文件内容"||file.TextFile);}};return menu;
        }
        void OutputAction(Action action){try{action();}catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException||e is InvalidOperationException||e is ArgumentException||e is System.ComponentModel.Win32Exception||e is System.Runtime.InteropServices.ExternalException))throw;session.ReportNotice("文件操作未完成："+e.Message);}}
    }
}
