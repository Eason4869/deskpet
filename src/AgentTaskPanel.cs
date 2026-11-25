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
        StackPanel childTasks;
        readonly TextBlock heading=LocalizedUi.Text(()=>(TextResources.Get("agentTaskPanel.heading.text")),12,Ui.Muted);readonly TextBlock notice=Ui.Text("",11,Ui.Muted);
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
            var task=session.CurrentAgentTask;if(task==null){Visibility=Visibility.Collapsed;signature="";childTasks=null;return;}Visibility=Visibility.Visible;
            bool busy=session.IsBusy;
            LocalizedUi.Set(heading,"Text",()=>TextResources.Format("agentTaskPanel.refresh.taskPlanCount",AgentTaskProgressText.Status(task),task.Plan.Count(p=>p.Status=="complete"),task.Plan.Count,task.Changes.Count));
            string next=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(task)+"|"+busy+"|"+pet.Settings.AgentEnabled+"|"+session.AgentTaskWarning;
            if(next==signature)return;signature=next;body.Children.Clear();childTasks=null;
            var goal=Ui.Text(task.Goal,12);goal.Margin=new Thickness(0,3,0,7);body.Children.Add(goal);
            var requests=LocalizedUi.Text(()=>AgentTaskProgressText.RequestCount(task),11,Ui.Muted);
            requests.Name="AgentTaskRequestCount";body.Children.Add(requests);
            var estimate=LocalizedUi.Text(()=>AgentTaskProgressText.TokenEstimate(task),11,Ui.Muted);
            estimate.Name="AgentTaskTokenEstimate";body.Children.Add(estimate);
            if(!String.IsNullOrEmpty(AgentTaskProgressText.Notice(task)))
            {
                var taskNotice=LocalizedUi.Text(()=>AgentTaskProgressText.Notice(task),11,Ui.Muted);
                taskNotice.Name="AgentTaskNotice";body.Children.Add(taskNotice);
            }
            var executionActions=new WrapPanel{Margin=new Thickness(0,7,0,2)};
            if(busy && task.Status=="running")
            {
                var stop=Small(()=>TextResources.Get("agent.jobs.ui.stop"),"StopAgentTask");
                stop.Click+=async delegate{try{await queue.PauseAsync();}catch(Exception e){Error(e);}};
                executionActions.Children.Add(stop);
            }
            if(task.Status=="needs_reconciliation")
            {
                var acknowledge=Small(()=>TextResources.Get("agentTaskPanel.refresh.small.alternativeD"),"AcknowledgeAgentExecutions");
                acknowledge.IsEnabled=!busy;
                acknowledge.Click+=async delegate{try{await session.AcknowledgeAgentExecutionsAsync(TaskSettings);await UpdateUiAsync(Refresh);}catch(Exception e){Error(e);}};
                executionActions.Children.Add(acknowledge);
            }
            var resume=Small(()=>TextResources.Get("agentTaskPanel.refresh.small.alternativeE"),"ContinueAgentTask");
            resume.IsEnabled=!busy && pet.Settings.AgentEnabled && task.Status!="running" && task.Status!="needs_reconciliation";
            resume.Click+=async delegate{bool resumeQueue=!queue.IsPaused;queue.Hold();try{await session.ResumeAgentTaskAsync(TaskSettings,pet.Skin.Name);}catch(Exception e){Error(e);}finally{if(resumeQueue)queue.Resume();}};
            executionActions.Children.Add(resume);body.Children.Add(executionActions);
            string context=task.ContextStatus=="compacting"?TextResources.Get("agentTaskPanel.refresh.contextCompactionsTaskContextCompactions.alternativeA"):task.ContextStatus=="limited"?TextResources.Get("agentTaskPanel.refresh.contextCompactionsTaskContextCompactions.alternativeB"):task.ContextCompactions>0?TextResources.Format("agentTaskPanel.refresh.contextCompactionsTaskContextCompactions",task.ContextCompactions):TextResources.Get("agentTaskPanel.refresh.contextCompactionsTaskContextCompactions.alternativeC");
            body.Children.Add(LocalizedUi.Text(()=>(context+(task.EstimatedContextTokens>0?TextResources.Format("agentTaskPanel.refresh.template",task.EstimatedContextTokens.ToString("N0")):"")),11,Ui.Muted));
            if(task.Guidance!=null && task.Guidance.Count>0)body.Children.Add(LocalizedUi.Text(()=>(TextResources.Format("agentTaskPanel.refresh.template.alternativeB",task.Guidance.Count)),11,Ui.Muted));
            if(task.Children!=null && task.Children.Count>0)
            {
                childTasks=new StackPanel{Name="AgentTaskChildren"};
                childTasks.Children.Add(Section(()=>TextResources.Get("agentTaskPanel.refresh.section")));
                foreach(var child in task.Children)
                {
                    string childText=TextResources.Format("agentTaskPanel.refresh.statusChildInput",child.Status,child.Input,child.Result??"",child.Source,child.ToolCalls,child.EstimatedTokens,child.EstimatedTotalTokens,child.ModelRequests);
                    var line=Ui.Text(childText,11,Ui.Muted);line.Margin=new Thickness(0,3,0,7);childTasks.Children.Add(line);
                    var openChild=Small(()=>TextResources.Get("agentTaskPanel.refresh.small"),"OpenAgentChildTab");openChild.IsEnabled=OpenDetailRequested!=null;
                    openChild.Click+=delegate{var navigate=OpenDetailRequested;if(navigate!=null)navigate(TextResources.Format("agentTaskPanel.refresh.navigate",child.Id),ReadOnly(childText));};
                    childTasks.Children.Add(openChild);
                }
                body.Children.Add(childTasks);
            }
            if(task.Plan.Count>0){var plan=new StackPanel{Name="AgentTaskPlan"};foreach(var step in task.Plan){var line=Ui.Text((step.Status=="complete"?"✓ ":step.Status=="in_progress"?"• ":"○ ")+step.Text,12,step.Status=="in_progress"?Ui.Ink:Ui.Muted);line.Margin=new Thickness(0,2,0,2);plan.Children.Add(line);}body.Children.Add(plan);}
            if(task.Changes.Count>0){body.Children.Add(Section(()=>(TextResources.Get("agentTaskPanel.refresh.section.alternativeA"))));var files=new StackPanel{Name="AgentTaskChanges"};foreach(var change in task.Changes){
                var row=new DockPanel{Margin=new Thickness(0,2,0,2)};var tools=new StackPanel{Orientation=Orientation.Horizontal};
                var diff=Small(()=>(TextResources.Get("agentTaskPanel.refresh.small.alternativeA")),"ShowAgentDiff");diff.IsEnabled=change.Status!="restored";diff.Click+=async delegate{await ShowDiff(change.Path);};tools.Children.Add(diff);
                var restore=Small(()=>(TextResources.Get("agentTaskPanel.refresh.small.alternativeB")),"RestoreAgentFile");restore.IsEnabled=!busy && change.Status!="restored" && !String.IsNullOrEmpty(change.BackupPath);restore.Click+=async delegate{try{string result=await session.RestoreAgentFileAsync(change.Path,TaskSettings);await UpdateUiAsync(delegate{notice.Text=result;Refresh();});}catch(Exception e){Error(e);}};tools.Children.Add(restore);
                DockPanel.SetDock(tools,Dock.Right);row.Children.Add(tools);var file=FileLink(()=>(Relative(task.Workspace,change.Path)+(change.Status=="restored"?TextResources.Get("agentTaskPanel.refresh.fileLink"):"")));file.ToolTip=change.Path;file.IsEnabled=OpenFileRequested!=null;file.Click+=delegate{var navigate=OpenFileRequested;if(navigate!=null)navigate(change.Path);};row.Children.Add(file);files.Children.Add(row);
            }body.Children.Add(files);}
            var commands=new StackPanel{Name="AgentTaskCommands"};if(task.Commands.Count==0)commands.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("agentTaskPanel.refresh.text")),11,Ui.Muted));
            foreach(var command in task.Commands){string label=command.CommandId+" · "+(command.Cancelled?TextResources.Get("agentTaskPanel.refresh.labelCommandCommandId.alternativeA"):command.TimedOut?TextResources.Get("agentTaskPanel.refresh.labelCommandCommandId.alternativeB"):TextResources.Format("agentTaskPanel.refresh.labelCommandCommandId",command.ExitCode))+" · "+(command.ElapsedMilliseconds/1000.0).ToString("0.0")+TextResources.Get("agentTaskPanel.refresh.labelCommandCommandId.alternativeC");
                string commandText=(String.IsNullOrEmpty(command.CommandText)?"":command.CommandText+"\n"+command.Workspace+"\n"+command.IsolationDescription+"\n\n")+(command.Output??"");var output=ReadOnly(commandText);output.MaxHeight=130;var commandBody=new StackPanel();commandBody.Children.Add(output);var open=Small(()=>(TextResources.Get("agentTaskPanel.refresh.small")),"OpenAgentCommandTab");open.IsEnabled=OpenDetailRequested!=null;open.Margin=new Thickness(0,5,0,0);open.HorizontalAlignment=HorizontalAlignment.Left;open.Click+=delegate{var navigate=OpenDetailRequested;if(navigate!=null)navigate(TextResources.Format("agentTaskPanel.refresh.navigate.alternativeA",command.CommandId),ReadOnly(commandText));};commandBody.Children.Add(open);var details=new Expander{Header=Ui.Text(label,11,command.ExitCode==0 && !command.TimedOut && !command.Cancelled?Ui.Muted:Ui.Brush("#A54A37")),Content=commandBody,Margin=new Thickness(0,3,0,3)};ChatUi.ProcessExpander(details);commands.Children.Add(details);
            }body.Children.Add(Section(()=>(TextResources.Get("agentTaskPanel.refresh.section.alternativeB"))));body.Children.Add(commands);
            var facts=Small(()=>(TextResources.Get("agentTaskPanel.refresh.small.alternativeC")),"OpenAgentProjectFacts");facts.Click+=delegate{try{if(openDetailRequested!=null)openDetailRequested(TextResources.Get("agentTaskPanel.refresh.openDetailRequested"),new AgentFactsPane(task));else if(Window.GetWindow(this) is ChatWindow)((ChatWindow)Window.GetWindow(this)).OpenProjectFacts(task);else pet.OpenChatProjectFacts(task);}catch(Exception e){Error(e);}};body.Children.Add(facts);
            var actions=new WrapPanel{Margin=new Thickness(0,7,0,2)};
            var settings=Small(()=>(TextResources.Get("agentTaskPanel.refresh.small.alternativeF")),"OpenAgentWorkSettings");settings.Click+=delegate{pet.ShowWorkSettings();};actions.Children.Add(settings);
            var clear=Small(()=>(TextResources.Get("agentTaskPanel.refresh.small.alternativeG")),"ClearAgentCheckpoint");clear.IsEnabled=!busy;clear.Click+=async delegate{try{await session.ClearAgentTaskAsync(TaskSettings);}catch(Exception e){Error(e);}};actions.Children.Add(clear);if(!String.IsNullOrEmpty(session.AgentTaskWarning)){var retry=Small(()=>(TextResources.Get("agentTaskPanel.refresh.small.alternativeH")),"RetryAgentCheckpoint");retry.IsEnabled=!busy;retry.Click+=async delegate{try{await session.RetryAgentTaskSaveAsync();await UpdateUiAsync(delegate{notice.Text=String.IsNullOrEmpty(session.AgentTaskWarning)?TextResources.Get("agentTaskPanel.refresh.noticeStringIsNullOrEmpty"):session.AgentTaskWarning;Refresh();});}catch(Exception e){Error(e);}};actions.Children.Add(retry);}body.Children.Add(actions);body.Children.Add(notice);
        }
        internal void ShowChildren()
        {
            if(disposed)return;
            Refresh();
            if(childTasks==null)return;
            IsExpanded=true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(delegate{if(!disposed&&childTasks!=null)childTasks.BringIntoView();}));
        }

        internal async Task ShowDiff(string path)
        {
            try
            {
                var navigate=OpenDetailRequested;var preview=await session.PreviewAgentChangeAsync(path,TaskSettings);
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    if(disposed)return;var content=AgentChangePreviewContent.Create(preview);
                    if(navigate!=null){navigate(TextResources.Format("agentTaskPanel.showDiff.navigate",Path.GetFileName(path)),content);return;}
                    var host=new Window{Name="AgentChangePreview"};Ui.Window(host,TextResources.Format("agentTaskPanel.showDiff.window",Path.GetFileName(path)),900,650);host.MinWidth=540;host.MinHeight=360;DialogUi.ApplyTitled(host,content,true);host.Show();
                }));
            }
            catch(Exception error){var errorDispatch=Dispatcher.BeginInvoke(new Action(delegate{if(!disposed)Error(error);}));}
        }
        static TextBlock Section(string text){var t=Ui.Text(text,11,Ui.Muted);t.Margin=new Thickness(0,9,0,3);return t;}
        static Button Small(string label,string name){var b=Ui.Button(label);b.Name=name;b.FontSize=11;b.MinHeight=25;b.Padding=new Thickness(7,3,7,3);b.Margin=new Thickness(3,0,0,0);return b;}
        static ControlTemplate fileLinkTemplate;
        static Button FileLink(string label)
        {
            if(fileLinkTemplate==null)fileLinkTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Left' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.75'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Opacity' Value='0.75'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            return new Button{Name="OpenAgentChangedFile",Content=Ui.Text(label,11,Ui.Blue),Template=fileLinkTemplate,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0,3,3,3),HorizontalContentAlignment=HorizontalAlignment.Left,Cursor=System.Windows.Input.Cursors.Hand,FocusVisualStyle=null};
        }
        static TextBox ReadOnly(string value){var box=new TextBox{Text=value,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas, Microsoft YaHei UI"),FontSize=12,Background=Ui.Brush("#FAFAFA"),BorderBrush=Ui.Line,Padding=new Thickness(8)};box.ContextMenu=ChatUi.EditorMenu(box,true);return box;}
        static string Relative(string root,string path){return path.StartsWith(root.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?path.Substring(root.TrimEnd('\\','/').Length+1):path;}
        // Button events can be raised before a DispatcherSynchronizationContext
        // exists (and file I/O completes on workers), so dispatch UI explicitly.
        Task UpdateUiAsync(Action update){return Dispatcher.InvokeAsync(new Action(delegate{if(!disposed)update();})).Task;}
        void Error(Exception e){if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(delegate{Error(e);}));return;}LocalizedUi.Set(notice,"Text",()=>(e is ArgumentException || e is InvalidOperationException || e is ApiException?e.Message:TextResources.Get("agentTaskPanel.error.apiExceptionEMessage")));}
        public void Dispose(){disposed=true;session.AgentTaskChanged-=Changed;session.StateChanged-=Changed;session.HistoryChanged-=Changed;}
static TextBlock Section(System.Func<string> text){return LocalizedUi.With(Section(text()),"Text",text);}
        static Button Small(System.Func<string> label,string name){return LocalizedUi.With(Small(label(),name),"Content",label);}
        static Button FileLink(System.Func<string> label){return LocalizedUi.With(FileLink(label()),"Content",label);}    }
}
