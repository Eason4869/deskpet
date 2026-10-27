using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu
{
    internal sealed class AgentJobsPanel : UserControl,IDisposable
    {
        internal sealed class Row
        {
            internal AgentJobSnapshot Job;internal AgentJobManager Manager;internal AgentJobCard View;internal Action<Uri> OpenPreview;
            internal readonly AgentJobPreviewCandidates Candidates=new AgentJobPreviewCandidates();
            internal string Cursor="",Log="",Gap="";internal bool Loading,Stopping;
        }
        readonly AgentJobApplicationService services;readonly string projectId,conversationId,replyId;readonly bool compact;
        readonly Action<Uri> openPreview;readonly ListBox list=new ListBox{Name="AgentJobsList",BorderThickness=new Thickness(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        readonly TextBlock notice=Ui.Text("",12,Ui.Muted);readonly Dictionary<string,Row> rows=new Dictionary<string,Row>();
        readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        readonly ComboBox filter=new ComboBox{Name="AgentJobsFilter",MinWidth=110},sort=new ComboBox{Name="AgentJobsSort",MinWidth=110};
        AgentJobManager manager;CancellationTokenSource reading;bool disposed,refreshing;int generation;string order="";
        internal int RefreshCount;internal bool Attached{get{return manager!=null;}}
        internal AgentJobsPanel(AgentJobApplicationService services,string projectId,string conversationId=null,string replyId=null,bool compact=false,Action<Uri> openPreview=null)
        {
            this.services=services;this.projectId=projectId;this.conversationId=conversationId;this.replyId=replyId;this.compact=compact;this.openPreview=openPreview??OpenBrowser;
            Name="AgentJobsPanel";Background=ThemeManager.Surface;var root=new Grid{Background=ThemeManager.Surface};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());Content=root;
            var controls=new WrapPanel{Margin=new Thickness(0,0,0,10)};filter.Items.Add(TextResources.Get("agent.jobs.ui.all"));filter.Items.Add(TextResources.Get("agent.jobs.ui.active"));filter.Items.Add(TextResources.Get("agent.jobs.ui.finished"));sort.Items.Add(TextResources.Get("agent.jobs.ui.latest"));sort.Items.Add(TextResources.Get("agent.jobs.ui.oldest"));filter.SelectedIndex=0;sort.SelectedIndex=0;controls.Children.Add(filter);sort.Margin=new Thickness(8,0,0,0);controls.Children.Add(sort);controls.Visibility=compact?Visibility.Collapsed:Visibility.Visible;root.Children.Add(controls);
            Grid.SetRow(notice,1);root.Children.Add(notice);Grid.SetRow(list,2);root.Children.Add(list);if(compact)list.MaxHeight=1000;
            VirtualizingStackPanel.SetIsVirtualizing(list,true);VirtualizingStackPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(list,true);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
            var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));list.ItemContainerStyle=itemStyle;
            var factory=new FrameworkElementFactory(typeof(AgentJobCard));list.ItemTemplate=new DataTemplate{VisualTree=factory};
            filter.SelectionChanged+=delegate{RefreshRows();};sort.SelectionChanged+=delegate{RefreshRows();};
            timer.Tick+=async delegate{if(refreshing||manager==null)return;refreshing=true;try{var token=reading.Token;RefreshRows();foreach(var row in rows.Values.ToArray())if(row.View!=null)await row.View.ReadLogAsync(token);}catch(OperationCanceledException){}finally{refreshing=false;}};
            Loaded+=delegate{Attach();};Unloaded+=delegate{Detach();};IsVisibleChanged+=delegate{if(IsVisible&&IsLoaded)Attach();else Detach();};
        }
        static void OpenBrowser(Uri uri){Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}
        async void Attach()
        {
            if(disposed||manager!=null||!IsVisible)return;int current=++generation;LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("agent.jobs.ui.recovering")));
            if(!await services.InitializeAsync()){if(current==generation&&!disposed)LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("agent.jobs.ui.unavailable")));return;}
            if(current!=generation||disposed||!IsLoaded||!IsVisible)return;manager=services.Manager;if(manager==null)return;
            reading=new CancellationTokenSource();RefreshRows();timer.Start();
        }
        void Detach()
        {generation++;timer.Stop();manager=null;if(reading!=null){reading.Cancel();reading.Dispose();reading=null;}}
        void RefreshRows()
        {
            if(disposed||manager==null)return;RefreshCount++;
            var source=replyId==null?manager.ListForProject(projectId):manager.ListForConversation(conversationId).Where(job=>job.SourceReplyId==replyId).ToList();
            var jobs=replyId==null?AgentJobViewState.Select(source,projectId,conversationId,null,filter.SelectedIndex==1?"active":filter.SelectedIndex==2?"finished":"all",sort.SelectedIndex==0):source.OrderBy(job=>job.CreatedUtc).ThenBy(job=>job.Id,StringComparer.Ordinal).ToList();
            var visible=new List<Row>();foreach(var job in jobs)
            {Row row;if(!rows.TryGetValue(job.Id,out row)){row=new Row{Job=job,Manager=manager,OpenPreview=openPreview};rows.Add(job.Id,row);}row.Job=job;if(row.View!=null)row.View.Update();visible.Add(row);}
            foreach(string id in rows.Keys.Where(id=>!jobs.Any(job=>job.Id==id)).ToArray())rows.Remove(id);
            string next=String.Join("|",jobs.Select(job=>job.Id));if(next!=order||list.ItemsSource==null){order=next;list.ItemsSource=visible;}
            LocalizedUi.Set(notice,"Text",()=>(jobs.Count==0?TextResources.Get("agent.jobs.ui.empty"):""));
        }
        internal void Retry(){Detach();services.RetryInitializationAsync();Attach();}
        public void Dispose(){if(disposed)return;disposed=true;Detach();list.ItemsSource=null;rows.Clear();}
    }
    internal sealed class AgentJobCard : Border
    {
        AgentJobsPanel.Row row;
        readonly TextBlock title=Ui.Text("",14),status=Ui.Text("",12,Ui.Muted),owner=Ui.Text("",11,Ui.Muted),directory=Ui.Text("",11,Ui.Muted),notice=Ui.Text("",12,Ui.Muted),gap=Ui.Text("",11,Ui.Muted);
        readonly TextBox command=Ui.Field("",true),log=Ui.Field("",true);readonly Button stop=LocalizedUi.Button(()=>(TextResources.Get("agent.jobs.ui.stop")));
        readonly Expander logExpander=LocalizedUi.With(new Expander{Header=TextResources.Get("agent.jobs.ui.openLog"),Name="AgentJobLogExpander"},"Header",()=>(TextResources.Get("agent.jobs.ui.openLog")));readonly WrapPanel previews=new WrapPanel();readonly TextBlock previewNotice=LocalizedUi.Text(()=>(TextResources.Get("agent.jobs.ui.previewUnchecked")),11,Ui.Muted);string previewSignature="";
        public AgentJobCard()
        {
            Name="AgentJobCard";Background=ThemeManager.CardSurface;BorderBrush=Ui.Line;BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(10);Padding=new Thickness(12);Margin=new Thickness(0,0,0,10);
            var body=new StackPanel();Child=body;title.Name="AgentJobTitle";title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);status.Name="AgentJobStatus";body.Children.Add(status);
            command.Name="AgentJobCommand";command.IsReadOnly=true;command.MaxHeight=100;command.FontFamily=new FontFamily("Consolas");command.FontSize=12;body.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("agent.jobs.ui.command")),11,Ui.Muted));body.Children.Add(command);
            directory.Name="AgentJobDirectory";directory.Margin=new Thickness(0,5,0,0);body.Children.Add(directory);owner.Name="AgentJobOwner";body.Children.Add(owner);
            var actions=new WrapPanel{Margin=new Thickness(0,8,0,6)};stop.Name="AgentJobStop";actions.Children.Add(stop);body.Children.Add(actions);body.Children.Add(notice);
            var details=new StackPanel();log.Name="AgentJobLog";log.IsReadOnly=true;log.MaxHeight=240;log.FontFamily=new FontFamily("Consolas");log.FontSize=12;log.TextWrapping=TextWrapping.Wrap;details.Children.Add(gap);details.Children.Add(log);details.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("agent.jobs.ui.preview")),12,Ui.Muted));details.Children.Add(previewNotice);details.Children.Add(previews);logExpander.Content=details;ChatUi.ProcessExpander(logExpander);body.Children.Add(logExpander);
            DataContextChanged+=delegate{Bind();};Loaded+=delegate{Bind();};Unloaded+=delegate{if(row!=null&&ReferenceEquals(row.View,this))row.View=null;};
            stop.Click+=async delegate
            {
                if(row==null||row.Stopping)return;var current=row;current.Stopping=true;stop.IsEnabled=false;notice.Text="";
                try{var stopped=await current.Manager.StopAsync(current.Job.Owner,current.Job.Id,AgentJobStopReason.User,CancellationToken.None);current.Job=stopped;if(ReferenceEquals(current,row)){LocalizedUi.Set(notice,"Text",()=>(stopped.CleanupConfirmed?"":TextResources.Get("agent.jobs.ui.stopError")));Update();}}
                catch(Exception){if(ReferenceEquals(current,row))LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("agent.jobs.ui.stopError")));}
                finally{current.Stopping=false;if(ReferenceEquals(current,row))Update();}
            };
        }
        void Bind(){var next=DataContext as AgentJobsPanel.Row;if(row!=null&&ReferenceEquals(row.View,this))row.View=null;row=next;notice.Text="";if(row==null)return;row.View=this;Update();log.Text=row.Log;gap.Text=row.Gap;RefreshPreviews();}
        internal void Update()
        {
            if(row==null)return;var job=row.Job;LocalizedUi.Set(title,"Text",()=>(job.Spec.Title));status.Text=AgentJobUiText.State(job.State)+" / "+TextResources.Format("agent.jobs.ui.timeout",job.Spec.TimeoutSeconds)+(job.RootExitCode.HasValue?" / "+TextResources.Format("agent.jobs.ui.exitCode",job.RootExitCode.Value):"");
            if(command.Text!=job.Spec.Command)command.Text=job.Spec.Command;LocalizedUi.Set(directory,"Text",()=>(TextResources.Get("agent.jobs.ui.directory")+": "+job.Spec.WorkingDirectory));LocalizedUi.Set(owner,"Text",()=>(TextResources.Get("agent.jobs.ui.owner")+": "+job.Owner.ProjectId+" / "+job.Owner.ConversationId));
            LocalizedUi.Set(stop,"Content",()=>((job.State==AgentJobState.CleanupFailed?TextResources.Get("agent.jobs.ui.retryStop"):TextResources.Get("agent.jobs.ui.stop"))));stop.IsEnabled=!row.Stopping&&AgentJobUiText.IsActive(job)&&job.State!=AgentJobState.Stopping;
        }
        internal async Task ReadLogAsync(CancellationToken token)
        {
            if(row==null||row.Loading||!logExpander.IsExpanded||!IsVisible)return;var current=row;current.Loading=true;
            try
            {
                var page=await current.Manager.GetAsync(current.Job.Owner,current.Job.Id,current.Cursor,16384,token);token.ThrowIfCancellationRequested();current.Cursor=page.NextCursor;
                if(page.Gap!=null)current.Gap=TextResources.Format("agent.jobs.ui.logGap",page.Gap.DroppedBytes);
                var text=String.Concat(page.Records.Select(record=>record.Text));current.Candidates.Add(text);current.Log+=text;if(current.Log.Length>131072){int removed=current.Log.Length-131072;current.Log=current.Log.Substring(removed);current.Gap=TextResources.Format("agent.jobs.ui.logGap",removed);}
                if(!ReferenceEquals(current,row)||!IsVisible)return;double offset=log.VerticalOffset;bool follow=log.ExtentHeight-log.ViewportHeight-offset<20;int selected=log.SelectionStart,length=log.SelectionLength;
                LocalizedUi.Set(log,"Text",()=>(current.Log.Length==0?TextResources.Get("agent.jobs.ui.logEmpty"):current.Log));log.Select(Math.Min(selected,log.Text.Length),Math.Min(length,Math.Max(0,log.Text.Length-selected)));if(follow)log.ScrollToEnd();else log.ScrollToVerticalOffset(offset);gap.Text=current.Gap;RefreshPreviews();
            }
            catch(OperationCanceledException){}
            catch(Exception){if(ReferenceEquals(current,row)&&IsVisible)LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("agent.jobs.ui.logError")));}
            finally{current.Loading=false;}
        }
        void RefreshPreviews()
        {
            if(row==null)return;var candidates=row.Candidates.Candidates;string signature=String.Join("|",candidates.Select(uri=>uri.AbsoluteUri));if(previewSignature==signature)return;previewSignature=signature;previews.Children.Clear();
            foreach(var uri in candidates){var target=uri;var button=LocalizedUi.Button(()=>(TextResources.Get("agent.jobs.ui.openPreview")));button.ToolTip=target.AbsoluteUri;button.Margin=new Thickness(0,5,8,0);button.Click+=delegate{try{row.OpenPreview(target);}catch(Exception error){notice.Text=error.Message;}};previews.Children.Add(button);}
        }
    }
}
