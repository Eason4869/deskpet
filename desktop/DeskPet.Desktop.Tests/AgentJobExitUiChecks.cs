using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class AgentJobExitUiChecks
{
    private static string mode="",root="";
    private sealed class HeldFactory(IAgentJobClock clock,string release):IAgentJobProcessFactory
    {
        private readonly WindowsAgentJobProcessFactory native=new(clock);
        public async Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)=>new HeldProcess(await native.PrepareAsync(spec,policy,output,token),release);
    }
    private sealed class HeldProcess(IAgentJobProcess native,string release):IAgentJobProcess
    {
        private readonly TaskCompletionSource<AgentJobCleanupResult> held=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentJobProcessExit Status=>native.Status;public Task<AgentJobProcessExit> Completion=>native.Completion;
        public void Resume()=>native.Resume();
        public Task<AgentJobCleanupResult> StopAsync(CancellationToken token)=>File.Exists(release)?native.StopAsync(token):held.Task;
        public void Dispose(){held.TrySetResult(new AgentJobCleanupResult(false,"fixture_disposed"));native.Dispose();}
    }
    private sealed class HeldSpeech(string release):IReplySpeech
    {
        public Task SpeakAsync(string text,PetSettings settings,CancellationToken token)=>Task.CompletedTask;
        public void Stop(){}public void SetVolume(int volume){}
        public void Dispose(){while(!File.Exists(release))Thread.Sleep(20);}
    }
    private sealed class ExitApplication:Application
    {
        public ExitApplication(){}
        public override void Initialize(){Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());DesktopAppearance.Install(this);}
        public override void OnFrameworkInitializationCompleted()
        {
            var lifetime=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;lifetime.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Dispatcher.UIThread.Post(async()=>
            {
                DesktopController? owner=null;
                try
                {
                    owner=new DesktopController(lifetime,new TestRuntime(root),jobsFactory:directory=>new AgentJobApplicationService(directory,clock=>mode=="held"?new HeldFactory(clock,Path.Combine(root,"cleanup.allow")):new WindowsAgentJobProcessFactory(clock),new AgentJobUiChecks.Clock()));
                    var project=await owner.WorkBindings.CreateProjectAsync("Native exit fixture",root);var conversation=project.Conversations.Single();
                    var window=new ChatWindow(owner,false);lifetime.MainWindow=window;window.Show();await window.BindWorkAsync(project.Id,conversation.Id);
                    var handle=window.TryGetPlatformHandle();if(handle==null||handle.Handle==IntPtr.Zero||handle.HandleDescriptor!="HWND")throw new Exception("Exit fixture did not create an actual native Windows window");
                    File.WriteAllText(Path.Combine(root,"host.native"),handle.HandleDescriptor+":"+handle.Handle.ToString("x"));
                    if(mode=="writer")typeof(ConversationSession).GetField("speech",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window.BoundSession,new HeldSpeech(Path.Combine(root,"cleanup.allow")));
                    var owned=owner;lifetime.Exit+=(_,_)=>
                    {
                        if(owned.WorkBindings.Jobs.Manager!=null)File.AppendAllText(Path.Combine(root,"fixture.error"),Environment.NewLine+"Native lifetime exited before confirming job/storage ownership");
                        else File.WriteAllText(Path.Combine(root,"exit.complete"),"confirmed");
                        owned.Dispose();
                    };
                    if(mode=="preview"){await AgentJobNativeUiChecks.RunInHostAsync(owner,window,root,project,conversation);await owner.ExitAsync();return;}
                    string pids=Path.Combine(root,"owned.pids");string command="$child=Start-Process -FilePath "+Quote(AgentCommandExecutor.PowerShellPath())+" -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 120' -WindowStyle Hidden -PassThru; [IO.File]::WriteAllText("+Quote(pids)+",([string]$PID+[Environment]::NewLine+[string]$child.Id)); Start-Sleep -Seconds 120";
                    var job=await AgentJobUiChecks.Start(owner.WorkBindings.Jobs.Manager,root,project.Id,conversation.Id,"native-exit",command);
                    await WaitAsync(()=>File.Exists(pids)&&File.ReadAllLines(pids).Length==2,7000,"native process tree readiness");File.WriteAllText(Path.Combine(root,"job.id"),job.Id);
                    File.WriteAllText(Path.Combine(root,"ready.tmp"),File.ReadAllText(pids));File.Move(Path.Combine(root,"ready.tmp"),Path.Combine(root,"host.ready"));
                    await WaitAsync(()=>File.Exists(Path.Combine(root,"exit.request")),30000,"parent exit request");
                    if(mode=="normal")
                    {if(lifetime.TryShutdown())throw new Exception("Native OS quit bypassed asynchronous admission");return;}
                    if(mode=="direct")
                    {
                        var previous=SynchronizationContext.Current;Task exit;SynchronizationContext.SetSynchronizationContext(null);
                        try{exit=owner.ExitAsync();}finally{SynchronizationContext.SetSynchronizationContext(previous);}await exit;return;
                    }
                    if(mode=="held"||mode=="writer")
                    {
                        bool refused=false;try{await owner.ExitAsync();}catch(InvalidOperationException){refused=true;}
                        if(!refused||!window.IsVisible||owner.WorkBindings.Jobs.Manager==null)throw new Exception("Unconfirmed cleanup discarded the native window or lease owner");
                        File.WriteAllText(Path.Combine(root,"exit.held"),"retained");await WaitAsync(()=>File.Exists(Path.Combine(root,"cleanup.allow")),18000,"parent cleanup retry");await owner.ExitAsync();return;
                    }
                    throw new Exception("Unexpected native fixture mode: "+mode);
                }
                catch(Exception error){File.WriteAllText(Path.Combine(root,"fixture.error"),error.ToString());owner?.Dispose();lifetime.Shutdown(1);}
            });
            base.OnFrameworkInitializationCompleted();
        }
    }
    private static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
    private static async Task WaitAsync(Func<bool> ready,int milliseconds,string description)
    {var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>milliseconds)throw new TimeoutException(description);await Task.Delay(25);}}
    internal static int RunNativeHost(string fixtureMode,string directory)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native exit fixtures require Windows");mode=fixtureMode;root=directory;int result=1;
        var thread=new Thread(()=>{try{result=AppBuilder.Configure<ExitApplication>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(Array.Empty<string>(),ShutdownMode.OnExplicitShutdown);}catch(Exception error){File.WriteAllText(Path.Combine(root,"fixture.error"),error.ToString());}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();return result;
    }
    internal static int RunPreview(string directory)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native preview acceptance requires Windows");Directory.CreateDirectory(directory);
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach(string argument in new[]{"--agent-job-exit-fixture","preview",directory})info.ArgumentList.Add(argument);using var host=System.Diagnostics.Process.Start(info)!;
        if(!host.WaitForExit(60000)){host.Kill();host.WaitForExit(5000);throw new TimeoutException("Native Avalonia preview host");}
        if(host.ExitCode!=0||!File.Exists(Path.Combine(directory,"exit.complete")))throw new Exception("Native Avalonia preview failed: "+(File.Exists(Path.Combine(directory,"fixture.error"))?File.ReadAllText(Path.Combine(directory,"fixture.error")):"exit="+host.ExitCode));
        using var report=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"preview.report.json")));return report.RootElement.GetProperty("checks").GetInt32();
    }
    internal static int Run(string directory,string? only=null)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native exit acceptance requires Windows");int checks=0;
        void Check(bool value,string description){if(!value)throw new Exception("Avalonia native exit: "+description);checks++;Console.WriteLine("PASS "+description);}
        void Wait(Func<bool> ready,int milliseconds,string description){var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>milliseconds)throw new TimeoutException(description);Thread.Sleep(25);}}
        foreach(string fixture in new[]{"normal","held","writer","kill","direct"}.Where(value=>only==null||value==only))
        {
            string data=Path.Combine(directory,fixture);Directory.CreateDirectory(data);var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach(string argument in new[]{"--agent-job-exit-fixture",fixture,data})info.ArgumentList.Add(argument);
            using var host=System.Diagnostics.Process.Start(info)!;var owned=new List<System.Diagnostics.Process>();Exception? failure=null;
            try
            {
                Wait(()=>File.Exists(Path.Combine(data,"host.ready"))||host.HasExited,25000,fixture+" actual native host readiness");Check(!host.HasExited&&File.ReadAllText(Path.Combine(data,"host.native")).StartsWith("HWND:"),fixture+" independent host creates a real native Windows UI");
                foreach(string pid in File.ReadAllLines(Path.Combine(data,"host.ready"))){var process=System.Diagnostics.Process.GetProcessById(int.Parse(pid));_ =process.Handle;owned.Add(process);}
                Check(owned.Count==2&&owned.All(process=>!process.HasExited),fixture+" actual PowerShell root and child are alive");var elapsed=Stopwatch.StartNew();
                if(fixture=="kill")host.Kill();else File.WriteAllText(Path.Combine(data,"exit.request"),"exit");
                if(fixture is "held" or "writer")
                {
                    Wait(()=>File.Exists(Path.Combine(data,"exit.held"))||host.HasExited,fixture=="writer"?12500:7000,fixture+" cleanup refusal");
                    Check(elapsed.ElapsedMilliseconds>=(fixture=="writer"?9000:4000)&&!host.HasExited,fixture+" cleanup deadline retains the native host");
                    Check(fixture=="writer"?owned.All(process=>process.HasExited):owned.All(process=>!process.HasExited),fixture+" distinguishes native task cleanup from held view finalization");
                    bool locked=false;try{using var denied=new AgentJobStore(data,new AgentJobUiChecks.Clock());}catch(IOException){locked=true;}Check(locked,fixture+" retains the data lease until retry confirms every owner");File.WriteAllText(Path.Combine(data,"cleanup.allow"),"retry");elapsed.Restart();
                }
                Wait(()=>host.HasExited&&owned.All(process=>process.HasExited),fixture=="kill"?5000:10000,fixture+" native host/tree exit");Check(elapsed.ElapsedMilliseconds<=(fixture=="kill"?5000:10000),fixture+" native host and tree satisfy the exit budget");
                if(fixture!="kill")Check(host.ExitCode==0&&File.Exists(Path.Combine(data,"exit.complete")),fixture+" lifetime exits only after final ownership confirmation");
                string id=File.ReadAllText(Path.Combine(data,"job.id"));using(var store=new AgentJobStore(data,new AgentJobUiChecks.Clock())){var jobs=store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();Check(jobs.Count==1&&jobs[0].Id==id&&jobs[0].State==(fixture=="kill"?AgentJobState.Interrupted:AgentJobState.Stopped),fixture+" recovery preserves task identity without rerunning the command");}
            }
            catch(Exception error){failure=new Exception(fixture+" host failure"+(File.Exists(Path.Combine(data,"fixture.error"))?": "+File.ReadAllText(Path.Combine(data,"fixture.error")):""),error);}
            if(!host.HasExited){File.WriteAllText(Path.Combine(data,"cleanup.allow"),"fixture cleanup");host.Kill();Wait(()=>host.HasExited,5000,"fixture host cleanup");}
            string evidence=Path.GetFullPath(Path.Combine("work","agent-jobs-ui","avalonia-exit",fixture));Directory.CreateDirectory(evidence);
            foreach(string file in Directory.GetFiles(data).Where(file=>new[]{".ready",".native",".complete",".held",".error",".id"}.Any(extension=>file.EndsWith(extension,StringComparison.Ordinal))))File.Copy(file,Path.Combine(evidence,Path.GetFileName(file)),true);
            Wait(()=>owned.All(process=>process.HasExited),5000,"fixture owned tree cleanup");foreach(var process in owned)process.Dispose();if(failure!=null)throw failure;
        }
        return checks;
    }
}
