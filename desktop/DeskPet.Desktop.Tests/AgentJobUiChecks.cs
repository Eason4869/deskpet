using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Desktop;

internal static class AgentJobUiChecks
{
    internal sealed class Clock:IAgentJobClock{readonly Stopwatch elapsed=Stopwatch.StartNew();public DateTime UtcNow=>DateTime.UtcNow;public long ElapsedMilliseconds=>elapsed.ElapsedMilliseconds;}
    private sealed class Lease:IDisposable{public void Dispose(){}}
    private sealed class Approval:IAgentAuthorizationService{public Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken token)=>Task.FromResult(new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow});}
    internal sealed class Process(Action<AgentJobOutputChunk> output):IAgentJobProcess
    {
        internal bool Clean;private AgentJobProcessExit status=new(false,null,false,false,1);
        private readonly TaskCompletionSource<AgentJobProcessExit> done=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentJobProcessExit Status=>status;public Task<AgentJobProcessExit> Completion=>done.Task;
        internal void Emit(string text){var bytes=System.Text.Encoding.UTF8.GetBytes(text);output(new AgentJobOutputChunk("stdout",bytes,bytes.Length,DateTime.UtcNow));}
        public void Resume()=>Emit("<script>plain text</script> start_job({fake:true}) http://user:secret@localhost:12345/fixture?token=secret#part\n"+string.Join("\n",Enumerable.Range(0,90).Select(value=>"fixture line "+value))+"\n");
        public Task<AgentJobCleanupResult> StopAsync(CancellationToken token){if(Clean){status=new(true,0,true,true,0);done.TrySetResult(status);}return Task.FromResult(new AgentJobCleanupResult(Clean,Clean?null:"held"));}
        public void Dispose(){}
    }
    internal sealed class Factory:IAgentJobProcessFactory
    {internal readonly List<Process> Processes=new();public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token){var process=new Process(output);Processes.Add(process);return Task.FromResult<IAgentJobProcess>(process);}}
    internal static Task<AgentJobSnapshot> Start(AgentJobManager manager,string root,string project,string conversation,string reply,string command="fixture plain text")
    {
        var owner=new AgentJobOwner(project,conversation,"primary");var invocation=new AgentToolInvocation("start_job",new Dictionary<string,object>{{"command",command},{"title","fixture"},{"working_directory",root},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId=project,AgentId="primary",TaskId="task-"+reply,CallId="call-"+reply});
        var context=new AgentJobStartContext(owner,invocation,new Approval(),()=>new AgentJobPolicySnapshot(root,"fixture-directory",1,1800,true,false,false,AgentApprovalMode.Full,null),()=>new Lease(),()=>{}){ReplyId=reply};return manager.StartAsync(context,new AgentJobSpec(command,"fixture",root,1800),CancellationToken.None);
    }
    internal static IEnumerable<T> All<T>(Control root) where T:class=>root.GetLogicalDescendants().OfType<T>().Concat(root.GetVisualDescendants().OfType<T>()).Distinct();
    internal static int Run(string directory,string? language=null)
    {
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("Avalonia jobs: "+label);checks++;}
        void Until(Func<bool> ready,int limit=6000)
        {
            if(ready()){Dispatcher.UIThread.RunJobs();return;}var watch=Stopwatch.StartNew();var frame=new DispatcherFrame();bool timedOut=false;
            using(var pulse=new System.Threading.Timer(_=>Dispatcher.UIThread.Post(()=>{if(ready())frame.Continue=false;else if(watch.ElapsedMilliseconds>limit){timedOut=true;frame.Continue=false;}}),null,0,10))Dispatcher.UIThread.PushFrame(frame);
            if(timedOut)throw new TimeoutException("Avalonia job view");Dispatcher.UIThread.RunJobs();
        }
        void Complete(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        Directory.CreateDirectory(directory);var factory=new Factory();int creations=0;
        if(language!=null){if(!UiLanguages.Choices.Any(choice=>choice.Code==language))throw new ArgumentException("Unsupported task UI language fixture");new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false});}
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory),jobsFactory:root=>new AgentJobApplicationService(root,clock=>{creations++;return factory;},new Clock()));
        var jobs=owner.WorkBindings.Jobs;int completions=0;jobs.TerminalCompleted+=_=>Interlocked.Increment(ref completions);Complete(jobs.InitializeAsync());Check(creations==0,"metadata recovery constructs zero native executors");
        var projectTask=owner.WorkBindings.CreateProjectAsync("UI fixture",directory);Complete(projectTask);var project=projectTask.Result;var conversation=project.Conversations.Single();
        var panel=new DesktopAgentJobsPanel(jobs,project.Id);var window=new Window{Content=panel,Width=720,Height=650,MinWidth=390};window.Show();
        try
        {
            var start=Start(jobs.Manager,directory,project.Id,conversation.Id,"reply");Complete(start);var job=start.Result;Until(()=>All<DesktopAgentJobCard>(panel).Any());
            Check(creations==1&&All<TextBlock>(panel).Any(text=>text.Name=="AgentJobOwner"&&text.Text!.Contains(conversation.Id)),"task card renders its immutable persistent owner");
            All<Expander>(panel).Single(value=>value.Name=="AgentJobLogExpander").IsExpanded=true;
            try{Until(()=>All<TextBox>(panel).Any(text=>text.Name=="AgentJobLog"&&text.Text!.Contains("<script>")));}
            catch{Console.WriteLine("panel attached="+panel.Attached+" refresh="+panel.RefreshCount);foreach(var card in All<DesktopAgentJobCard>(panel)){var row=(DesktopAgentJobsPanel.Row)typeof(DesktopAgentJobCard).GetField("row",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(card)!;Console.WriteLine("card visible="+card.IsEffectivelyVisible+" view="+ReferenceEquals(row.View,card)+" loading="+row.Loading+" log="+row.Log.Length+" cursor="+row.Cursor);}foreach(var text in All<TextBlock>(panel))Console.WriteLine("TEXT "+text.Text);throw;}
            var log=All<TextBox>(panel).Single(text=>text.Name=="AgentJobLog");Check(log.IsReadOnly&&log.Text!.Contains("start_job({fake:true})")&&factory.Processes.Count==1,"HTML and forged tool instructions stay read-only text");
            Until(()=>All<Button>(panel).Any(button=>button.Name=="AgentJobPreview"));var preview=All<Button>(panel).Single(button=>button.Name=="AgentJobPreview");
            Check((ToolTip.GetTip(preview)?.ToString()??"")=="http://localhost:12345/fixture","preview address removes credentials, query and fragment without probing the server");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();var scroll=log.GetVisualDescendants().OfType<ScrollViewer>().First();Check(scroll.Extent.Height>scroll.Viewport.Height&&log.Bounds.Height>0,"log layout really overflows its visible viewport");
            var bars=All<ScrollBar>(log).Where(bar=>bar.Orientation==Avalonia.Layout.Orientation.Vertical&&bar.IsEffectivelyVisible&&bar.Bounds.Width>0).ToArray();Check(bars.Length>0&&bars.All(bar=>Math.Abs(bar.Bounds.Width-12)<1),"task logs reuse the application twelve-DIP scrollbar style");
            scroll.Offset=new Vector(0,0);log.SelectionStart=0;log.SelectionEnd=6;factory.Processes[0].Emit("later appended output\n");Until(()=>log.Text!.Contains("later appended output"));AvaloniaHeadlessPlatform.ForceRenderTimerTick();Check(log.SelectionStart==0&&log.SelectionEnd==6&&scroll.Offset.Y<2,"incremental logs preserve the user's selection and scroll position");
            int before=panel.RefreshCount;var sample=Stopwatch.StartNew();Until(()=>sample.ElapsedMilliseconds>=1100,2000);Check(panel.RefreshCount-before>=3&&panel.RefreshCount-before<=5,"real dispatcher timer refreshes at four hertz without spinning");
            var responsive=Stopwatch.StartNew();var ping=Dispatcher.UIThread.InvokeAsync(()=>true,Avalonia.Threading.DispatcherPriority.Background);Complete(ping.GetTask());Check(responsive.ElapsedMilliseconds<500&&!factory.Processes[0].Status.TreeExited,"running tasks leave the UI dispatcher responsive within five hundred milliseconds");window.Hide();int hidden=panel.RefreshCount;Thread.Sleep(400);Dispatcher.UIThread.RunJobs();Check(!panel.Attached&&panel.RefreshCount==hidden&&!factory.Processes[0].Status.TreeExited,"hidden view releases refresh ownership while the task continues");window.Show();Until(()=>panel.Attached);
            Window? settingsHost=null;WorkSettingsView? settingsView=null;int originalLimit=project.BackgroundJobTimeoutSeconds;
            if(OperatingSystem.IsWindows()){
                settingsHost=new Window();settingsView=new WorkSettingsView(owner,settingsHost,project.Id);settingsHost.Content=settingsView;settingsHost.Show();
                All<NumericUpDown>(settingsView).Single(value=>value.Name=="AgentBackgroundJobTimeoutSeconds").Value=999;
                Check(owner.WorkBindings.ProjectSettings(project.Id).AgentBackgroundJobTimeoutSeconds==originalLimit,"active jobs reject task-policy edits without changing the project");
            }
            var stop=All<Button>(panel).Single(button=>button.Name=="AgentJobStop");stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>stop.IsEnabled&&Equals(stop.Content,TextResources.Get("agent.jobs.ui.retryStop")));Check(stop.IsEnabled,"unconfirmed cleanup retains a retry action");factory.Processes[0].Clean=true;stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!stop.IsEnabled&&jobs.Manager.ListForProject(project.Id).Single().CleanupConfirmed);Check(jobs.Manager.ListForProject(project.Id).Single().CleanupConfirmed,"human stop confirms the original process owner");
            Until(()=>completions==1);var completionWatch=Stopwatch.StartNew();Until(()=>completionWatch.ElapsedMilliseconds>=350,1000);Check(completions==1,"multiple refreshes emit only one application completion notification");
            if(settingsView!=null){All<NumericUpDown>(settingsView).Single(value=>value.Name=="AgentContextTokenLimit").Value=65536;Check(owner.WorkBindings.ProjectSettings(project.Id).AgentBackgroundJobTimeoutSeconds==originalLimit,"a later unrelated save never applies a task-policy edit refused while busy");settingsHost!.Close();}
            string captures=Path.GetFullPath(Path.Combine("work","agent-jobs-ui","avalonia",TextResources.CurrentLanguage));Directory.CreateDirectory(captures);
            foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{390,980})
            {
                DesktopTheme.Apply(theme);window.Width=width;AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
                Check(Math.Abs(window.Bounds.Width-width)<2,"job window actually measures "+width+" DIP");
                Check(All<DesktopAgentJobCard>(panel).All(card=>card.Bounds.Width>0&&card.Bounds.Width<=panel.Bounds.Width+1),"task cards fit "+theme+" "+width+" DIP");
                using(var capture=window.CaptureRenderedFrame()){Check(capture!=null,"task panel renders "+theme+" "+width);capture!.Save(Path.Combine(captures,theme+"-"+width+".png"),new PngBitmapEncoderOptions());}
            }
            DesktopTheme.Apply("light");
            var filter=All<ComboBox>(panel).Single(combo=>combo.Name=="AgentJobsFilter");filter.SelectedIndex=1;Check(All<ListBox>(panel).Single().ItemCount==0,"active filter excludes confirmed stopped tasks");filter.SelectedIndex=2;Check(All<ListBox>(panel).Single().ItemCount==1,"finished filter retains its record");
            var workspace=owner.WorkBindings.Manager.CreateWorkspace(project,conversation);workspace.FollowProject(new ProjectDefinition{Id="moved-project",Name="moved",Workspace=directory});
            var reply=new ChatMessage("assistant","fixture reply");typeof(ChatMessage).GetProperty("Id")!.SetValue(reply,"reply");reply.SetTools(new[]{new AgentToolEvent{Id="call-reply",Name="start_job",Status="complete"}});
            var replyView=new ChatMessageView(reply,owner,window,workspace.Session,workspace.Queue,workspace.EffectiveSettings);var replyWindow=new Window{Content=replyView,Width=650,Height=650};replyWindow.Show();Until(()=>All<DesktopAgentJobCard>(replyView).Any());Check(All<TextBlock>(replyView).Any(text=>text.Name=="AgentJobOwner"&&text.Text!.Contains(project.Id)),"original reply card retains the original task owner after moving the conversation");replyView.DisposeImages();replyWindow.Close();
            panel.Dispose();Check(!panel.Attached,"disposing the view releases its refresh lifetime");
        }
        finally{panel.Dispose();window.Close();foreach(var process in factory.Processes)process.Clean=true;owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
        return checks;
    }
}
