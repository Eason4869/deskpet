using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class PreparationUiChecks
{
    internal static int Run(string directory)
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        void Wait(Task task){var until=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted){if(DateTime.UtcNow>until)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name)where T:Control=>root.GetLogicalDescendants().OfType<T>().Distinct().Single(x=>x.Name==name);
        void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,GreetingEnabled=false,Wander=false,FollowCursor=false,OnboardingState="completed",SaveChatHistory=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));
        IProgress<OfficialCompanionProgress>? reporter=null;int starts=0;bool complete=false;
        owner.PreparationDownloadOverride=(progress,token)=>{starts++;reporter=progress;return complete?Task.CompletedTask:Task.Delay(Timeout.Infinite,token);};
        var window=new CompanionPreparationWindow(owner);window.Show();Dispatcher.UIThread.RunJobs();
        var captures=Path.GetFullPath(Path.Combine("work","preparation-native-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(captures);
        void Capture(string name){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(captures,name+".png"),new PngBitmapEncoderOptions());}
        try
        {
            Check(!window.CanMaximize&&!window.CanResize,"native preparation disables maximize and resizing");
            Check(starts==0,"opening native view does not start transfer");
            var start=Named<Button>(window,"StartOfficialPreparation");var cancel=Named<Button>(window,"CancelOfficialPreparation");
            Check(start.IsEnabled&&!cancel.IsEnabled,"native idle actions");Capture("idle");
            Click(start);Check(starts==1&&!start.IsEnabled&&cancel.IsEnabled,"native running actions");
            reporter!.Report(new OfficialCompanionProgress("e-bao","download","",20*1048576,50*1048576,40));Dispatcher.UIThread.RunJobs();
            Check(Named<ProgressBar>(window,"OfficialPreparationProgressBar").Value==40,"native progress updates");Capture("running");
            window.Close();Check(owner.OfficialPreparation.State==OfficialCompanionPreparationState.Running,"native close preserves transfer");
            window=new CompanionPreparationWindow(owner);window.Show();Dispatcher.UIThread.RunJobs();var running=owner.OfficialPreparation.StartAsync();Click(Named<Button>(window,"CancelOfficialPreparation"));Wait(running);
            Check(owner.OfficialPreparation.State==OfficialCompanionPreparationState.Cancelled,"native stop cancels shared transfer");Capture("cancelled");
            complete=true;Wait(owner.OfficialPreparation.StartAsync());Check(Named<ProgressBar>(window,"OfficialPreparationProgressBar").Value==100,"native completed progress");Capture("complete");
            Console.WriteLine("Native preparation captures="+captures);return checks;
        }
        finally{window.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}
    }
    private sealed class FixtureRuntime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("Fixture has no audio");public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("Fixture has no audio");
    }
}
