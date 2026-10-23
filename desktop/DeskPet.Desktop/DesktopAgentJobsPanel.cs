using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class DesktopAgentJobsPanel:UserControl,IDisposable
{
    internal sealed class Row
    {
        internal required AgentJobSnapshot Job;internal required AgentJobManager Manager;
        internal DesktopAgentJobCard? View;internal required Func<Uri,Task> OpenPreview;
        internal readonly AgentJobPreviewCandidates Candidates=new();
        internal string Cursor="",Log="",Gap="";internal bool Loading,Stopping;
    }
    private readonly AgentJobApplicationService services;
    private readonly string projectId;private readonly string? conversationId,replyId;
    private readonly Func<Uri,Task> openPreview;
    private readonly ListBox list=new(){Name="AgentJobsList"};
    private readonly TextBlock notice=Ui.Text("");
    private readonly ComboBox filter=new(){Name="AgentJobsFilter",MinWidth=110},sort=new(){Name="AgentJobsSort",MinWidth=110};
    private readonly Dictionary<string,Row> rows=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private AgentJobManager? manager;private CancellationTokenSource? reading;
    private Window? host;private bool disposed,refreshing;private int generation;private string order="";
    internal int RefreshCount {get;private set;}internal bool Attached=>manager!=null;
    internal DesktopAgentJobsPanel(AgentJobApplicationService services,string projectId,string? conversationId=null,string? replyId=null,bool compact=false,Func<Uri,Task>? openPreview=null)
    {
        this.services=services;this.projectId=projectId;this.conversationId=conversationId;this.replyId=replyId;
        this.openPreview=openPreview??(async uri=>{if(TopLevel.GetTopLevel(this) is { } top)await top.Launcher.LaunchUriAsync(uri);});
        Name="AgentJobsPanel";Background=DesktopTheme.Surface;
        filter.ItemsSource=new[]{TextResources.Get("agent.jobs.ui.all"),TextResources.Get("agent.jobs.ui.active"),TextResources.Get("agent.jobs.ui.finished")};
        sort.ItemsSource=new[]{TextResources.Get("agent.jobs.ui.latest"),TextResources.Get("agent.jobs.ui.oldest")};filter.SelectedIndex=0;sort.SelectedIndex=0;
        var controls=new WrapPanel{IsVisible=!compact,Margin=new Thickness(0,0,0,8)};controls.Children.Add(filter);controls.Children.Add(sort);
        var root=new DockPanel();var header=Ui.Column(controls,notice);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);root.Children.Add(list);Content=root;
        list.ItemsPanel=new FuncTemplate<Panel?>(()=>new VirtualizingStackPanel());
        list.ItemTemplate=new FuncDataTemplate<Row>((row,_)=>row==null?null:new DesktopAgentJobCard(row));
        list.Styles.Add(new Style(selector=>selector.OfType<ListBoxItem>()){Setters={new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch),new Setter(ListBoxItem.PaddingProperty,new Thickness(0))}});
        ScrollViewer.SetHorizontalScrollBarVisibility(list,Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);if(compact)list.MaxHeight=1000;
        filter.SelectionChanged+=(_,_)=>RefreshRows();sort.SelectionChanged+=(_,_)=>RefreshRows();
        timer.Tick+=async (_,_)=>
        {
            if(refreshing||manager==null||reading==null)return;refreshing=true;
            try{var token=reading.Token;RefreshRows();foreach(var row in rows.Values.ToArray())if(row.View!=null)await row.View.ReadLogAsync(token);}
            catch(OperationCanceledException){}finally{refreshing=false;}
        };
        AttachedToVisualTree+=(_,_)=>{host=TopLevel.GetTopLevel(this) as Window;if(host!=null)host.PropertyChanged+=HostChanged;Attach();};
        DetachedFromVisualTree+=(_,_)=>{DetachHost();Detach();};
        PropertyChanged+=(_,change)=>{if(change.Property==IsVisibleProperty){if(IsVisible)Attach();else Detach();}};
    }
    private void HostChanged(object? sender,AvaloniaPropertyChangedEventArgs change)
    {if(change.Property==Window.IsVisibleProperty){if(host?.IsVisible==true)Attach();else Detach();}}
    private void DetachHost(){if(host!=null)host.PropertyChanged-=HostChanged;host=null;}
    private async void Attach()
    {
        if(disposed||manager!=null||!IsEffectivelyVisible||host?.IsVisible==false)return;
        int current=++generation;notice.Text=TextResources.Get("agent.jobs.ui.recovering");
        bool ready=await services.InitializeAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(()=>
        {
            if(disposed||current!=generation||!IsEffectivelyVisible||host?.IsVisible==false)return;
            if(!ready||services.Manager==null){notice.Text=TextResources.Get("agent.jobs.ui.unavailable");return;}
            manager=services.Manager;reading=new CancellationTokenSource();RefreshRows();timer.Start();
        });
    }
    private void Detach(){generation++;timer.Stop();manager=null;reading?.Cancel();reading?.Dispose();reading=null;}
    private void RefreshRows()
    {
        if(disposed||manager==null)return;RefreshCount++;
        var source=replyId==null?manager.ListForProject(projectId):manager.ListForConversation(conversationId).Where(job=>job.SourceReplyId==replyId).ToList();
        var jobs=replyId==null?AgentJobViewState.Select(source,projectId,conversationId,null,filter.SelectedIndex==1?"active":filter.SelectedIndex==2?"finished":"all",sort.SelectedIndex==0):source.OrderBy(job=>job.CreatedUtc).ThenBy(job=>job.Id,StringComparer.Ordinal).ToList();
        var visible=new List<Row>();foreach(var job in jobs){if(!rows.TryGetValue(job.Id,out var row)){row=new Row{Job=job,Manager=manager,OpenPreview=openPreview};rows.Add(job.Id,row);}row.Job=job;row.View?.Update();visible.Add(row);}
        foreach(string id in rows.Keys.Where(id=>!jobs.Any(job=>job.Id==id)).ToArray())rows.Remove(id);
        string next=string.Join("|",jobs.Select(job=>job.Id));if(next!=order||list.ItemsSource==null){order=next;list.ItemsSource=visible;}
        notice.Text=jobs.Count==0?TextResources.Get("agent.jobs.ui.empty"):"";
    }
    internal async Task RetryAsync(){Detach();await services.RetryInitializationAsync();Attach();}
    public void Dispose(){if(disposed)return;disposed=true;DetachHost();Detach();list.ItemsSource=null;rows.Clear();}
}

internal sealed class DesktopAgentJobCard:Border
{
    private readonly DesktopAgentJobsPanel.Row row;
    private readonly TextBlock title=Ui.Text("",15),status=Ui.Text("",12),owner=Ui.Text("",11),directory=Ui.Text("",11),notice=Ui.Text("",12),gap=Ui.Text("",11);
    private readonly TextBox command=Ui.Input(),log=Ui.Input();
    private readonly Button stop;private readonly Expander details=new(){Name="AgentJobLogExpander",Header=TextResources.Get("agent.jobs.ui.openLog"),HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly WrapPanel previews=new();private string previewSignature="";
    internal DesktopAgentJobCard(DesktopAgentJobsPanel.Row row)
    {
        this.row=row;row.View=this;Name="AgentJobCard";Padding=new Thickness(12);Margin=new Thickness(0,0,0,10);CornerRadius=new CornerRadius(10);BorderThickness=new Thickness(1);BorderBrush=DesktopTheme.Brush("#D8E5F9");Background=DesktopTheme.Surface;
        title.Name="AgentJobTitle";status.Name="AgentJobStatus";owner.Name="AgentJobOwner";directory.Name="AgentJobDirectory";
        command.Name="AgentJobCommand";command.IsReadOnly=true;command.AcceptsReturn=true;command.TextWrapping=TextWrapping.Wrap;command.MinWidth=0;command.MaxHeight=100;
        log.Name="AgentJobLog";log.IsReadOnly=true;log.AcceptsReturn=true;log.TextWrapping=TextWrapping.Wrap;log.MinWidth=0;log.MaxHeight=240;log.FontFamily=FontFamily.Parse("Consolas, Menlo, monospace");log.Text=row.Log;
        stop=Ui.Button(TextResources.Get("agent.jobs.ui.stop"),()=>_ =StopAsync());stop.Name="AgentJobStop";
        details.Content=Ui.Column(gap,log,Ui.Text(TextResources.Get("agent.jobs.ui.preview"),12),Ui.Text(TextResources.Get("agent.jobs.ui.previewUnchecked"),11),previews);ProcessExpander.Apply(details);
        Child=Ui.Column(title,status,Ui.Text(TextResources.Get("agent.jobs.ui.command"),11),command,directory,owner,stop,notice,details);
        AttachedToVisualTree+=(_,_)=>{row.View=this;Update();};DetachedFromVisualTree+=(_,_)=>{if(ReferenceEquals(row.View,this))row.View=null;};Update();
    }
    internal void Update()
    {
        var job=row.Job;title.Text=job.Spec.Title;status.Text=AgentJobUiText.State(job.State)+" / "+TextResources.Format("agent.jobs.ui.timeout",job.Spec.TimeoutSeconds)+(job.RootExitCode.HasValue?" / "+TextResources.Format("agent.jobs.ui.exitCode",job.RootExitCode.Value):"");
        if(command.Text!=job.Spec.Command)command.Text=job.Spec.Command;directory.Text=TextResources.Get("agent.jobs.ui.directory")+": "+job.Spec.WorkingDirectory;owner.Text=TextResources.Get("agent.jobs.ui.owner")+": "+job.Owner.ProjectId+" / "+job.Owner.ConversationId;
        stop.Content=job.State==AgentJobState.CleanupFailed?TextResources.Get("agent.jobs.ui.retryStop"):TextResources.Get("agent.jobs.ui.stop");stop.IsEnabled=!row.Stopping&&AgentJobUiText.IsActive(job)&&job.State!=AgentJobState.Stopping;
    }
    private async Task StopAsync()
    {
        if(row.Stopping)return;row.Stopping=true;Update();
        try
        {
            var stopped=await row.Manager.StopAsync(row.Job.Owner,row.Job.Id,AgentJobStopReason.User,CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(()=>{row.Job=stopped;notice.Text=stopped.CleanupConfirmed?"":TextResources.Get("agent.jobs.ui.stopError");});
        }
        catch(Exception error){await Dispatcher.UIThread.InvokeAsync(()=>notice.Text=AgentJobUiText.Error(error));}
        finally{await Dispatcher.UIThread.InvokeAsync(()=>{row.Stopping=false;Update();});}
    }
    internal async Task ReadLogAsync(CancellationToken token)
    {
        if(row.Loading||!details.IsExpanded||!IsEffectivelyVisible)return;row.Loading=true;
        try
        {
            var page=await row.Manager.GetAsync(row.Job.Owner,row.Job.Id,row.Cursor,16384,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
            string added=string.Concat(page.Records.Select(record=>record.Text));
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(token.IsCancellationRequested||!IsEffectivelyVisible)return;
                row.Cursor=page.NextCursor;row.Candidates.Add(added);row.Log+=added;if(page.Gap!=null)row.Gap=TextResources.Format("agent.jobs.ui.logGap",page.Gap.DroppedBytes);
                if(row.Log.Length>131072){int removed=row.Log.Length-131072;row.Log=row.Log.Substring(removed);row.Gap=TextResources.Format("agent.jobs.ui.logGap",removed);}
                var scroll=log.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();var offset=scroll?.Offset??default;bool follow=scroll==null||scroll.Extent.Height-scroll.Viewport.Height-offset.Y<20;int start=log.SelectionStart,end=log.SelectionEnd;
                log.Text=row.Log.Length==0?TextResources.Get("agent.jobs.ui.logEmpty"):row.Log;log.SelectionStart=Math.Min(start,log.Text.Length);log.SelectionEnd=Math.Min(end,log.Text.Length);gap.Text=row.Gap;
                if(scroll!=null){if(follow)scroll.ScrollToEnd();else scroll.Offset=offset;}RefreshPreviews();
            });
        }
        catch(OperationCanceledException){}
        catch(Exception){await Dispatcher.UIThread.InvokeAsync(()=>{if(IsEffectivelyVisible)notice.Text=TextResources.Get("agent.jobs.ui.logError");});}
        finally{row.Loading=false;}
    }
    private void RefreshPreviews()
    {
        var candidates=row.Candidates.Candidates;string signature=string.Join("|",candidates.Select(uri=>uri.AbsoluteUri));if(signature==previewSignature)return;previewSignature=signature;previews.Children.Clear();
        foreach(var uri in candidates){var target=uri;var button=Ui.AsyncButton(TextResources.Get("agent.jobs.ui.openPreview"),()=>row.OpenPreview(target),error=>notice.Text=AgentJobUiText.Error(error));button.Name="AgentJobPreview";ToolTip.SetTip(button,target.AbsoluteUri);previews.Children.Add(button);}
    }
}
