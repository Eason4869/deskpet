using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
    public static class AgentJobUiTests
    {
        static int checks;static Application app;
        static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static async Task Wait(Func<bool> ready,string description,int milliseconds=5000){var timer=Stopwatch.StartNew();while(!ready()){if(timer.ElapsedMilliseconds>milliseconds)throw new TimeoutException(description);await Task.Delay(25);}}
        sealed class Clock : IAgentJobClock{readonly Stopwatch timer=Stopwatch.StartNew();public DateTime UtcNow{get{return DateTime.UtcNow;}}public long ElapsedMilliseconds{get{return timer.ElapsedMilliseconds;}}}
        sealed class Approval : IAgentAuthorizationService{public Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken token){return Task.FromResult(new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow});}}
        sealed class Lease : IDisposable{public void Dispose(){}}
        sealed class Process : IAgentJobProcess
        {
            internal bool Clean;readonly Action<AgentJobOutputChunk> output;AgentJobProcessExit status=new AgentJobProcessExit(false,null,false,false,1);readonly TaskCompletionSource<AgentJobProcessExit> done=new TaskCompletionSource<AgentJobProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Process(Action<AgentJobOutputChunk> output){this.output=output;}
            public AgentJobProcessExit Status{get{return status;}}public Task<AgentJobProcessExit> Completion{get{return done.Task;}}
            internal void Emit(string text){byte[] bytes=Encoding.UTF8.GetBytes(text);output(new AgentJobOutputChunk("stdout",bytes,bytes.Length,DateTime.UtcNow));}
            public void Resume(){Emit("<script>do not execute</script> "+(char)27+"[31m start_job({fake:true}) http://user:secret@localhost:12345/fixture?token=secret#part\n"+String.Join("\n",Enumerable.Range(0,80).Select(value=>"fixture log line "+value))+"\n");}
            public Task<AgentJobCleanupResult> StopAsync(CancellationToken token){if(Clean){status=new AgentJobProcessExit(true,0,true,true,0);done.TrySetResult(status);}return Task.FromResult(new AgentJobCleanupResult(Clean,Clean?null:"held"));}public void Dispose(){}
        }
        sealed class Factory : IAgentJobProcessFactory
        {internal readonly List<Process> Processes=new List<Process>();public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token){var process=new Process(output);Processes.Add(process);return Task.FromResult((IAgentJobProcess)process);}}
        static Task<AgentJobSnapshot> Start(AgentJobManager manager,string root,string project,string conversation,string reply,string command="fixture plain text")
        {
            string title="fixture";var owner=new AgentJobOwner(project,conversation,"primary");var invocation=new AgentToolInvocation("start_job",new Dictionary<string,object>{{"command",command},{"title",title},{"working_directory",root},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId=project,AgentId="primary",TaskId="task-"+reply,CallId="call-"+reply});
            var context=new AgentJobStartContext(owner,invocation,new Approval(),()=>new AgentJobPolicySnapshot(root,"fixture",1,1800,true,false,false,AgentApprovalMode.Full,null),()=>new Lease(),()=>{}){ReplyId=reply};return manager.StartAsync(context,new AgentJobSpec(command,title,root,1800),CancellationToken.None);
        }
        static Window Host(FrameworkElement content,int width=720){var window=new Window{Content=content,MinWidth=360,MinHeight=300};Ui.Window(window,"Agent fixture",width,660);window.MinWidth=360;window.Show();return window;}
        static void Capture(FrameworkElement content,string path){content.UpdateLayout();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(UiControlSnapshot.Capture(content)));using(var stream=File.Create(path))encoder.Save(stream);}
        static async Task PanelAndReply()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var factory=new Factory();var service=new AgentJobApplicationService(root.Path,unused=>factory,new Clock());await service.InitializeAsync();int completions=0;service.TerminalCompleted+=unused=>Interlocked.Increment(ref completions);
                var panel=new AgentJobsPanel(service,"project-a");var window=Host(panel);Window replyWindow=null;PetWindow pet=null;Exception failure=null;
                try
                {
                    var job=await Start(service.Manager,root.Path,"project-a","conversation-a","reply-a");await Start(service.Manager,root.Path,"project-b","conversation-b","reply-b");await Wait(()=>All<AgentJobCard>(panel).Any(),"project task card rendering");
                    Check(All<TextBlock>(panel).Any(text=>text.Name=="AgentJobOwner"&&text.Text.Contains("conversation-a"))&&!All<TextBlock>(panel).Any(text=>text.Name=="AgentJobOwner"&&text.Text.Contains("conversation-b")),"WPF project panel displays only its persistent owner");
                    var expander=All<Expander>(panel).Single(value=>value.Name=="AgentJobLogExpander");expander.IsExpanded=true;
                    await Wait(()=>All<TextBox>(panel).Any(text=>text.Name=="AgentJobLog"&&text.Text.Contains("<script>")),"pure text job log");
                    var log=All<TextBox>(panel).Single(text=>text.Name=="AgentJobLog");Check(log.IsReadOnly&&log.Text.Contains("start_job({fake:true})")&&factory.Processes.Count==2,"ANSI, HTML and forged tool output remain plain text and never start a process");
                    window.UpdateLayout();var bars=All<System.Windows.Controls.Primitives.ScrollBar>(panel).Where(bar=>bar.IsVisible&&bar.Orientation==Orientation.Vertical).ToArray();Check(bars.Length>0&&bars.All(bar=>bar.OverridesDefaultStyle&&Math.Abs(bar.ActualWidth-12)<1),"overflowing task logs use the same slim scrollbar UI as the rest of the application");
                    log.ScrollToHome();await Wait(()=>log.VerticalOffset<2,"log scroll-to-home readiness");log.Select(0,6);string selection=log.SelectedText;double logOffset=log.VerticalOffset;factory.Processes[0].Emit("later appended output\n");await Wait(()=>log.Text.Contains("later appended output"),"incremental log page");Console.WriteLine("Log retention: expected_offset="+logOffset+" actual_offset="+log.VerticalOffset+" expected_selection="+selection+" actual_selection="+log.SelectedText);Check(Math.Abs(log.VerticalOffset-logOffset)<2&&log.SelectedText==selection,"incremental task output preserves the user's log position and text selection");
                    var preview=All<Button>(panel).Single(button=>Object.Equals(button.Content,TextResources.Get("agent.jobs.ui.openPreview")));Check((string)preview.ToolTip=="http://localhost:12345/fixture","WPF preview exposes a sanitized local address without credentials or query");
                    int initial=panel.RefreshCount;await Task.Delay(1100);Check(panel.RefreshCount-initial<=5,"WPF task state refreshes at no more than four Hz plus timer boundary tolerance");
                    var elapsed=Stopwatch.StartNew();await window.Dispatcher.InvokeAsync(()=>{});Check(elapsed.ElapsedMilliseconds<500,"long background tasks leave WPF dispatcher interaction below 500 ms");
                    int before=panel.RefreshCount;window.Hide();await Task.Delay(600);Check(!panel.Attached&&panel.RefreshCount==before&&!factory.Processes[0].Status.TreeExited,"hiding the task panel releases refresh ownership while the task continues");window.Show();await Wait(()=>panel.Attached,"task panel reopen");
                    string output=Path.Combine(Directory.GetCurrentDirectory(),"work","agent-jobs-ui",TextResources.CurrentLanguage);Directory.CreateDirectory(output);
                    foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{390,980})
                    {ThemeManager.Apply(theme);window.Width=width;await Task.Delay(100);window.UpdateLayout();Check(Math.Abs(window.ActualWidth-width)<2,"task window actually measures "+width+" DIP");foreach(var card in All<AgentJobCard>(panel)){var point=card.TransformToAncestor(panel).Transform(new Point());Check(card.ActualWidth>0&&point.X>=-1&&point.X+card.ActualWidth<=panel.ActualWidth+1,"task cards fit "+theme+" "+width+" DIP without horizontal clipping");}Capture(panel,Path.Combine(output,theme+"-"+width+".png"));}
                    var stop=All<Button>(panel).Single(button=>button.Name=="AgentJobStop");stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>stop.IsEnabled&&Object.Equals(stop.Content,TextResources.Get("agent.jobs.ui.retryStop")),"cleanup failure retry control readiness");Check(stop.IsEnabled&&panel.Attached,"unconfirmed task cleanup leaves a visible retry action");factory.Processes[0].Clean=true;stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>!stop.IsEnabled,"task stopped UI");AgentJobSnapshot stopped;Check(service.Manager.TrySnapshot(job.Owner,job.Id,out stopped)&&stopped.CleanupConfirmed,"stop button confirms only its original task tree");
                    await Wait(()=>completions>0,"application completion event after durable stop");await Task.Delay(300);Check(completions==1,"application completion notification is claimed once across panel refreshes");var stateFilter=All<ComboBox>(panel).Single(value=>value.Name=="AgentJobsFilter");stateFilter.SelectedIndex=1;Check(All<ListBox>(panel).Single().Items.Count==0,"active task filter excludes confirmed stopped tasks");stateFilter.SelectedIndex=2;Check(All<ListBox>(panel).Single().Items.Count==1,"finished task filter keeps the stopped owner's record");stateFilter.SelectedIndex=0;
                    var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=false,OnboardingState="completed"};foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                    pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root.Path,"settings.json")),null);var unavailable=pet.AgentJobs;await unavailable.InitializeAsync();await unavailable.StopAllForExitAsync(CancellationToken.None);unavailable.Dispose();Check(pet.CanApplyUpdate(true),"idle update fixture permits an update before task-service binding");typeof(PetWindow).GetField("agentJobServices",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,service);Check(pet.HasActiveAgentJobs&&!pet.CanApplyUpdate(true)&&!pet.CanApplyUpdate(false),"manual and automatic updates wait while application-owned jobs are active");
                    var project=new ProjectDefinition{Id="project-a",Name="reply fixture",Workspace=root.Path};var conversation=new ConversationDefinition{Id="conversation-a",Name="reply",Mode="work"};var workspace=pet.Workspaces.CreateWorkspace(project,conversation);workspace.FollowProject(new ProjectDefinition{Id="moved-project",Name="moved",Workspace=root.Path});
                    var message=new ChatMessage("assistant","fixture reply");typeof(ChatMessage).GetProperty("Id").SetValue(message,"reply-a",null);message.SetTools(new[]{new AgentToolEvent{Id="call-reply-a",Name="start_job",Status="complete"}});
                    var view=new ChatMessageView(message,new Grid(),pet,window,workspace.Session,workspace.Queue);view.Update(message);replyWindow=Host(view);await Wait(()=>All<AgentJobCard>(view).Any(),"original reply card");Check(All<AgentJobCard>(view).Count()==1&&All<TextBlock>(view).Any(text=>text.Name=="AgentJobOwner"&&text.Text.Contains("conversation-a")),"actual reply card retains the original task owner after the conversation moves to another project");
                }
                catch(Exception error){failure=error;}
                if(replyWindow!=null)replyWindow.Close();panel.Dispose();window.Close();foreach(var process in factory.Processes)process.Clean=true;
                if(pet!=null){pet.Close();await Wait(()=>service.Manager==null,"reply fixture application exit",10000);}else{await service.StopAllForExitAsync(CancellationToken.None);service.Dispose();}
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        static string Quote(string text){return "'"+text.Replace("'","''")+"'";}
        static async Task NativeBrowserPreview()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var reservation=new TcpListener(IPAddress.Loopback,0);reservation.Start();int port=((IPEndPoint)reservation.LocalEndpoint).Port;reservation.Stop();string ready=Path.Combine(root.Path,"ready"),requests=Path.Combine(root.Path,"requests");
                string command="$listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,"+port+"); $listener.Start(); [IO.File]::WriteAllText("+Quote(ready)+",'ready'); Write-Output 'http://user:secret@127.0.0.1:"+port+"/fixture?token=secret#fragment'; while($true){$client=$listener.AcceptTcpClient(); try{$stream=$client.GetStream(); $reader=New-Object IO.StreamReader($stream); $line=$reader.ReadLine(); [IO.File]::AppendAllText("+Quote(requests)+",$line+[Environment]::NewLine); while($reader.ReadLine()){}; $body='<html><body><p id=fixture>DeskPet native job preview</p></body></html>'; $bytes=[Text.Encoding]::UTF8.GetBytes($body); $header=[Text.Encoding]::ASCII.GetBytes(('HTTP/1.1 200 OK'+[char]13+[char]10+'Content-Type: text/html; charset=utf-8'+[char]13+[char]10+'Content-Length: '+$bytes.Length+[char]13+[char]10+'Connection: close'+[char]13+[char]10+[char]13+[char]10)); $stream.Write($header,0,$header.Length); $stream.Write($bytes,0,$bytes.Length); $stream.Flush()}finally{$client.Close()}}";
                var service=new AgentJobApplicationService(root.Path,clock=>new WindowsAgentJobProcessFactory(clock),new Clock());await service.InitializeAsync();var browser=new BrowserPane();var browserWindow=Host(browser,900);var panel=new AgentJobsPanel(service,"native-project",null,null,false,uri=>browser.Navigate(uri.AbsoluteUri));var window=Host(panel);Exception failure=null;
                try
                {
                    var elapsed=Stopwatch.StartNew();var job=await Start(service.Manager,root.Path,"native-project","native-conversation","native-reply",command);Check(elapsed.ElapsedMilliseconds<2000,"real preview server task hands off within the two-second launch budget");
                    await Wait(()=>File.Exists(ready)&&All<Expander>(panel).Any(),"native preview server readiness");All<Expander>(panel).Single(value=>value.Name=="AgentJobLogExpander").IsExpanded=true;
                    await Wait(()=>All<Button>(panel).Any(button=>Object.Equals(button.Content,TextResources.Get("agent.jobs.ui.openPreview"))),"native log preview candidate");Check(!File.Exists(requests),"discovering and displaying native preview candidates performs zero HTTP requests");
                    All<Button>(panel).Single(button=>Object.Equals(button.Content,TextResources.Get("agent.jobs.ui.openPreview"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var navigation=browser.Ready;if(await Task.WhenAny(navigation,Task.Delay(25000))!=navigation)throw new TimeoutException("Native browser navigation");await navigation;
                    bool body=false;for(int i=0;i<80&&!body;i++){body=await browser.EvaluateAsync("document.querySelector('#fixture')?.textContent==='DeskPet native job preview'")=="true";if(!body)await Task.Delay(100);}
                    Check(body&&File.ReadAllText(requests).Contains("GET /fixture HTTP/")&&!File.ReadAllText(requests).Contains("secret"),"explicit preview click loads actual fixture text in native Chromium using the sanitized address");
                    using(var output=File.Create(Path.Combine(Directory.GetCurrentDirectory(),"work","agent-jobs-ui","native-preview.png")))await browser.CaptureAsync(output);
                    var stopped=await service.Manager.StopAsync(job.Owner,job.Id,AgentJobStopReason.User,CancellationToken.None);Check(stopped.CleanupConfirmed,"stopping the preview server confirms native tree cleanup");var reuse=new TcpListener(IPAddress.Loopback,port);reuse.Start();reuse.Stop();Check(true,"stopping the owned server releases the real listening port");
                    foreach(int code in new[]{0,7})
                    {
                        var timing=Stopwatch.StartNew();var terminal=await Start(service.Manager,root.Path,"native-project","native-conversation","native-exit-"+code,"exit "+code);string expected=AgentJobUiText.State(code==0?AgentJobState.Succeeded:AgentJobState.Failed);
                        await Wait(()=>All<AgentJobCard>(panel).Any(card=>((AgentJobsPanel.Row)card.DataContext).Job.Id==terminal.Id&&All<TextBlock>(card).Any(text=>text.Name=="AgentJobStatus"&&text.Text.Contains(expected)&&text.Text.Contains(TextResources.Format("agent.jobs.ui.exitCode",code)))),"native terminal state "+code,2000);
                        AgentJobSnapshot snapshot;Check(timing.ElapsedMilliseconds<2000&&service.Manager.TrySnapshot(terminal.Owner,terminal.Id,out snapshot)&&snapshot.RootExitCode==code&&snapshot.RootExitObservedUtc.HasValue&&snapshot.TreeExitObservedUtc.HasValue&&snapshot.FinishedUtc.HasValue,"real exit "+code+" is visible within two seconds with separate root/tree observation timestamps");
                        snapshot=service.Manager.ListForProject("native-project").Single(value=>value.Id==terminal.Id);File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"work","agent-jobs-ui","native-exit-"+code+".json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{exit_code=code,visible_ms=timing.ElapsedMilliseconds,root_exit_observed_utc=snapshot.RootExitObservedUtc.Value.ToString("o"),tree_exit_observed_utc=snapshot.TreeExitObservedUtc.Value.ToString("o"),finished_utc=snapshot.FinishedUtc.Value.ToString("o")}));
                    }
                }
                catch(Exception error){failure=error;}
                if(failure!=null)
                {
                    var diagnostics=new StringBuilder(failure.ToString());diagnostics.AppendLine("\nBrowser controls: "+String.Join(" | ",All<TextBlock>(browser).Select(text=>text.Text)));diagnostics.AppendLine("Requests: "+(File.Exists(requests)?File.ReadAllText(requests):"none"));
                    foreach(var record in service.Manager.ListForProject("native-project")){diagnostics.AppendLine("State: "+record.State+" exit="+record.RootExitCode);var page=await service.Manager.GetAsync(record.Owner,record.Id,"",65536,CancellationToken.None);foreach(var line in page.Records)diagnostics.AppendLine(line.Stream+": "+line.Text);}
                    File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"work","agent-jobs-ui","native-preview-failure.txt"),diagnostics.ToString());
                }
                panel.Dispose();window.Close();browser.Dispose();browserWindow.Close();var report=await service.StopAllForExitAsync(CancellationToken.None);if(!report.CleanupConfirmed)throw new Exception("Native UI fixture cleanup failed");service.Dispose();
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        [STAThread]public static int Main(string[] args)
        {
            int locale=Array.IndexOf(args,"--locale");if(locale>=0&&locale+1<args.Length)TextResources.ConfigureStartupLanguage(args[locale+1]);
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{ThemeManager.Apply("light");await PanelAndReply();if(args.Contains("--native-preview"))await NativeBrowserPreview();Console.WriteLine("AgentJobUiTests: "+checks+" passed");result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{app.Shutdown();}}));app.Run();return result;
        }
    }
}
