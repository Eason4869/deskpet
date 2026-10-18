using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    // Additive task details: existing chat/composer layout and colours stay intact.
    internal sealed class AgentTaskPanel : Expander,IDisposable
    {
        readonly PetWindow pet;readonly ConversationSession session;readonly ConversationQueue queue;readonly StackPanel body=new StackPanel();string taskWorkspace="";
        readonly ScrollViewer detailScroll;
        readonly TextBlock heading=Ui.Text("任务进度",12,Ui.Muted);readonly TextBlock notice=Ui.Text("",11,Ui.Muted);
        bool disposed,queued;string signature="";
        Action<string> openFileRequested;Action<string,FrameworkElement> openDetailRequested;
        internal Action<string> OpenFileRequested{get{return openFileRequested;}set{if(ReferenceEquals(openFileRequested,value))return;openFileRequested=value;signature="";if(!disposed)Refresh();}}
        internal Action<string,FrameworkElement> OpenDetailRequested{get{return openDetailRequested;}set{if(ReferenceEquals(openDetailRequested,value))return;openDetailRequested=value;signature="";if(!disposed)Refresh();}}
        public AgentTaskPanel(PetWindow companion,ConversationSession sessionOverride=null,ConversationQueue queueOverride=null)
        {
            pet=companion;session=sessionOverride??pet.Conversation;queue=queueOverride??pet.InputQueue;Name="AgentTaskDetails";Header=heading;Margin=new Thickness(0,5,0,7);Visibility=Visibility.Collapsed;
            ChatUi.ProcessExpander(this);detailScroll=Ui.Scroll(body);detailScroll.MaxHeight=220;Content=detailScroll;
            session.AgentTaskChanged+=Changed;session.StateChanged+=Changed;session.HistoryChanged+=Changed;
            session.LoadAgentTask(TaskSettings);Refresh();
        }
        internal void SetWorkspaceLayout(bool wide){detailScroll.MaxHeight=wide?Double.PositiveInfinity:220;Margin=wide?new Thickness(14,0,14,14):new Thickness(0,5,0,7);}
        // Task records are keyed by the project workspace, so every lookup must
        // resolve against the open project rather than the global setting.
        internal void SetTaskWorkspace(string path)
        {
            string next=path??"";
            if(String.Equals(next,taskWorkspace,StringComparison.OrdinalIgnoreCase))return;
            taskWorkspace=next;
            // The task store is keyed by workspace, so a change must re-resolve the
            // record; otherwise the panel shows another project's (or no) task.
            if(!disposed)session.LoadAgentTask(TaskSettings);
            Refresh();
        }
        PetSettings TaskSettings
        {
            get
            {
                var cfg=pet.ConversationSettings(session);
                if(!String.IsNullOrEmpty(taskWorkspace))try{cfg.AgentWorkspace=System.IO.Path.GetFullPath(taskWorkspace);}catch(ArgumentException){}
                return cfg;
            }
        }
        void Changed(){if(disposed || queued)return;queued=true;Dispatcher.BeginInvoke(new Action(delegate{queued=false;if(!disposed)Refresh();}));}
        void Refresh()
        {
            if(disposed)return;if(!Dispatcher.CheckAccess()){Changed();return;}
            var task=session.CurrentAgentTask;if(task==null){Visibility=Visibility.Collapsed;return;}Visibility=Visibility.Visible;
            bool busy=session.IsBusy;string running=task.Tools.LastOrDefault(t=>t.Status=="running")==null?"处理中":"执行工具中";
            string status=task.Status=="complete"?"已完成":task.Status=="running"?running:task.Status=="cancelled"?"已停止":task.Status=="interrupted"?"已中断":"需要处理";
            heading.Text="任务 · "+status+"   "+task.Plan.Count(p=>p.Status=="complete")+"/"+task.Plan.Count+" 步   "+task.Changes.Count+" 个文件";
            string next=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(task)+"|"+busy+"|"+pet.Settings.AgentEnabled+"|"+session.AgentTaskWarning;
            if(next==signature)return;signature=next;body.Children.Clear();
            var goal=Ui.Text(task.Goal,12);goal.Margin=new Thickness(0,3,0,7);body.Children.Add(goal);
            string context=task.ContextStatus=="compacting"?"正在整理上下文…":task.ContextStatus=="limited"?"当前输入超过上下文预算":task.ContextCompactions>0?"上下文已自动压缩 "+task.ContextCompactions+" 次":"上下文自动压缩已启用";
            body.Children.Add(Ui.Text(context+(task.EstimatedContextTokens>0?" · 约 "+task.EstimatedContextTokens.ToString("N0")+" tokens":""),11,Ui.Muted));
            body.Children.Add(Ui.Text("累计用量估算：父任务 "+task.EstimatedTotalTokens+" + 子任务 "+task.ChildEstimatedTotalTokens+" Token（非计费数据）",11,Ui.Muted));
            if(task.Guidance!=null && task.Guidance.Count>0)body.Children.Add(Ui.Text("已接入 "+task.Guidance.Count+" 条引导",11,Ui.Muted));
            if(task.Children!=null && task.Children.Count>0){body.Children.Add(Section("独立只读子任务"));foreach(var child in task.Children){string childText=child.Status+" · "+child.Input+"\n"+(child.Result??"")+"\n来源："+child.Source+" · 实际工具调用 "+child.ToolCalls+" · 当前上下文约 "+child.EstimatedTokens+" Token · 累计估算 "+child.EstimatedTotalTokens+" Token / "+child.ModelRequests+" 次请求";var line=Ui.Text(childText,11,Ui.Muted);line.TextWrapping=TextWrapping.Wrap;line.Margin=new Thickness(0,3,0,7);body.Children.Add(line);var openChild=Small("在标签页查看","OpenAgentChildTab");openChild.IsEnabled=OpenDetailRequested!=null;openChild.Click+=delegate{var navigate=OpenDetailRequested;if(navigate!=null)navigate("子任务 · "+child.Id,ReadOnly(childText));};body.Children.Add(openChild);}}
            if(task.Plan.Count>0){var plan=new StackPanel{Name="AgentTaskPlan"};foreach(var step in task.Plan){var line=Ui.Text((step.Status=="complete"?"✓ ":step.Status=="in_progress"?"• ":"○ ")+step.Text,12,step.Status=="in_progress"?Ui.Ink:Ui.Muted);line.Margin=new Thickness(0,2,0,2);plan.Children.Add(line);}body.Children.Add(plan);}
            if(task.Changes.Count>0){body.Children.Add(Section("文件变更"));var files=new StackPanel{Name="AgentTaskChanges"};foreach(var change in task.Changes){
                var row=new DockPanel{Margin=new Thickness(0,2,0,2)};var tools=new StackPanel{Orientation=Orientation.Horizontal};
                var diff=Small("查看差异","ShowAgentDiff");diff.IsEnabled=change.Status!="restored";diff.Click+=async delegate{await ShowDiff(change.Path);};tools.Children.Add(diff);
                var restore=Small("恢复","RestoreAgentFile");restore.IsEnabled=!busy && change.Status!="restored" && !String.IsNullOrEmpty(change.BackupPath);restore.Click+=async delegate{try{string result=await session.RestoreAgentFileAsync(change.Path,TaskSettings);await UpdateUiAsync(delegate{notice.Text=result;Refresh();});}catch(Exception e){Error(e);}};tools.Children.Add(restore);
                DockPanel.SetDock(tools,Dock.Right);row.Children.Add(tools);var file=FileLink(Relative(task.Workspace,change.Path)+(change.Status=="restored"?"（已恢复）":""));file.ToolTip=change.Path;file.IsEnabled=OpenFileRequested!=null;file.Click+=delegate{var navigate=OpenFileRequested;if(navigate!=null)navigate(change.Path);};row.Children.Add(file);files.Children.Add(row);
            }body.Children.Add(files);}
            var commands=new StackPanel{Name="AgentTaskCommands"};if(task.Commands.Count==0)commands.Children.Add(Ui.Text("尚未运行构建或测试验证。",11,Ui.Muted));
            foreach(var command in task.Commands){string label=command.CommandId+" · "+(command.Cancelled?"已停止":command.TimedOut?"超时":"退出码 "+command.ExitCode)+" · "+(command.ElapsedMilliseconds/1000.0).ToString("0.0")+" 秒";
                string commandText=(String.IsNullOrEmpty(command.CommandText)?"":command.CommandText+"\n"+command.Workspace+"\n"+command.IsolationDescription+"\n\n")+(command.Output??"");var output=ReadOnly(commandText);output.MaxHeight=130;var commandBody=new StackPanel();commandBody.Children.Add(output);var open=Small("在标签页查看","OpenAgentCommandTab");open.IsEnabled=OpenDetailRequested!=null;open.Margin=new Thickness(0,5,0,0);open.HorizontalAlignment=HorizontalAlignment.Left;open.Click+=delegate{var navigate=OpenDetailRequested;if(navigate!=null)navigate("命令输出 · "+command.CommandId,ReadOnly(commandText));};commandBody.Children.Add(open);var details=new Expander{Header=Ui.Text(label,11,command.ExitCode==0 && !command.TimedOut && !command.Cancelled?Ui.Muted:Ui.Brush("#A54A37")),Content=commandBody,Margin=new Thickness(0,3,0,3)};ChatUi.ProcessExpander(details);commands.Children.Add(details);
            }body.Children.Add(Section("命令与验证"));body.Children.Add(commands);
            if(!String.IsNullOrEmpty(task.Notice))body.Children.Add(Ui.Text(task.Notice,11,Ui.Muted));
            var facts=Small("项目事实","OpenAgentProjectFacts");facts.Click+=delegate{try{new AgentFactsWindow(task,Window.GetWindow(this)).Show();}catch(Exception e){Error(e);}};body.Children.Add(facts);
            if(task.Status=="needs_reconciliation"){var acknowledge=Small("已核对执行记录","AcknowledgeAgentExecutions");acknowledge.IsEnabled=!busy;acknowledge.Click+=async delegate{try{await session.AcknowledgeAgentExecutionsAsync(TaskSettings);await UpdateUiAsync(Refresh);}catch(Exception e){Error(e);}};body.Children.Add(acknowledge);}
            var actions=new WrapPanel{Margin=new Thickness(0,7,0,2)};var resume=Small("继续任务","ContinueAgentTask");resume.IsEnabled=!busy && pet.Settings.AgentEnabled && task.Status!="running";resume.Click+=async delegate{bool resumeQueue=!queue.IsPaused;queue.Hold();try{await session.ResumeAgentTaskAsync(TaskSettings,pet.Skin.Name);}catch(Exception e){Error(e);}finally{if(resumeQueue)queue.Resume();}};actions.Children.Add(resume);
            var settings=Small("工作设置","OpenAgentWorkSettings");settings.Click+=delegate{pet.ShowWorkSettings();};actions.Children.Add(settings);
            var clear=Small("清除任务记录","ClearAgentCheckpoint");clear.IsEnabled=!busy;clear.Click+=async delegate{try{await session.ClearAgentTaskAsync(TaskSettings);}catch(Exception e){Error(e);}};actions.Children.Add(clear);if(!String.IsNullOrEmpty(session.AgentTaskWarning)){var retry=Small("重试保存","RetryAgentCheckpoint");retry.IsEnabled=!busy;retry.Click+=async delegate{try{await session.RetryAgentTaskSaveAsync();await UpdateUiAsync(delegate{notice.Text=String.IsNullOrEmpty(session.AgentTaskWarning)?"任务记录已保存。":session.AgentTaskWarning;Refresh();});}catch(Exception e){Error(e);}};actions.Children.Add(retry);}body.Children.Add(actions);body.Children.Add(notice);
        }
        internal async Task ShowDiff(string path)
        {
            try
            {
                var navigate=OpenDetailRequested;var preview=await session.PreviewAgentChangeAsync(path,TaskSettings);
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    if(disposed)return;var content=AgentChangePreviewContent.Create(preview);
                    if(navigate!=null){navigate("文件差异 · "+Path.GetFileName(path),content);return;}
                    var host=new Window{Name="AgentChangePreview"};Ui.Window(host,"文件变更 · "+Path.GetFileName(path),900,650);host.MinWidth=540;host.MinHeight=360;host.Content=content;host.Show();
                }));
            }
            catch(Exception error){Dispatcher.BeginInvoke(new Action(delegate{if(!disposed)Error(error);}));}
        }
        static TextBlock Section(string text){var t=Ui.Text(text,11,Ui.Muted);t.Margin=new Thickness(0,9,0,3);return t;}
        static Button Small(string label,string name){var b=Ui.Button(label);b.Name=name;b.FontSize=11;b.MinHeight=25;b.Padding=new Thickness(7,3,7,3);b.Margin=new Thickness(3,0,0,0);return b;}
        static ControlTemplate fileLinkTemplate;
        static Button FileLink(string label)
        {
            if(fileLinkTemplate==null)fileLinkTemplate=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Left' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.75'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Opacity' Value='0.75'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            return new Button{Name="OpenAgentChangedFile",Content=Ui.Text(label,11,Ui.Blue),Template=fileLinkTemplate,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0,3,3,3),HorizontalContentAlignment=HorizontalAlignment.Left,Cursor=System.Windows.Input.Cursors.Hand,FocusVisualStyle=null};
        }
        static TextBox ReadOnly(string value){var box=new TextBox{Text=value,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas, Microsoft YaHei UI"),FontSize=12,Background=Ui.Brush("#FAFAFA"),BorderBrush=Ui.Line,Padding=new Thickness(8)};box.ContextMenu=ChatUi.EditorMenu(box,true);return box;}
        static string Relative(string root,string path){return path.StartsWith(root.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?path.Substring(root.TrimEnd('\\','/').Length+1):path;}
        // Button events can be raised before a DispatcherSynchronizationContext
        // exists (and file I/O completes on workers), so dispatch UI explicitly.
        Task UpdateUiAsync(Action update){return Dispatcher.InvokeAsync(new Action(delegate{if(!disposed)update();})).Task;}
        void Error(Exception e){if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(delegate{Error(e);}));return;}notice.Text=e is ArgumentException || e is InvalidOperationException || e is ApiException?e.Message:"操作未完成，请检查项目文件、备份和数据目录。";}
        public void Dispose(){disposed=true;session.AgentTaskChanged-=Changed;session.StateChanged-=Changed;session.HistoryChanged-=Changed;}
    }
}
