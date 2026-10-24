using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using Dafeiyu.Maintenance;
using DeskPet.Desktop;

internal static class CacheMaintenanceUiChecks
{
    internal static int Run(string directory)
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(20);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("Native cache UI");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name)where T:Control=>root.GetLogicalDescendants().OfType<T>().Single(x=>x.Name==name);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{AutoUpdate=false,OnboardingState="completed",UiLanguage="en_US"});
        var lifetime=new ClassicDesktopStyleApplicationLifetime();using var owner=new DesktopController(lifetime,new FixtureRuntime(directory));owner.Pet.Show();var about=new AboutWindow(owner);about.Show();
        try{
            Click(Named<Button>(about,"ClearCache"));Dispatcher.UIThread.RunJobs();var opened=about.OwnedWindows.OfType<CacheMaintenanceWindow>().Single();Wait(opened.PendingOperation);
            Check(opened.Owner==about&&!Named<Button>(opened,"CleanCache").IsEnabled,"native About opens owned zero-space cache dialog");opened.Close();Check(owner.Pet.IsVisible,"native close keeps pet alive");
            string path=Path.Combine(directory,"updates",new string('a',32));using(CacheResourceLease.Acquire(directory,path))File.WriteAllBytes(Path.Combine(path,"package.zip"),new byte[123]);File.SetLastWriteTimeUtc(Path.Combine(path,"package.zip"),DateTime.UtcNow.AddDays(-8));Directory.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-8));
            var cache=new CacheMaintenanceWindow(directory);cache.Show(about);Wait(cache.PendingOperation);Check(cache.Scan!.TotalBytes==123&&Named<Button>(cache,"CleanCache").IsEnabled,"native scan shows actual bytes");Click(Named<Button>(cache,"CleanCache"));Wait(cache.PendingOperation);Check(cache.LastResult!.FreedBytes==123&&cache.Scan!.TotalBytes==0&&!Named<Button>(cache,"CleanCache").IsEnabled,"native actual clean results and zero rescan");
            var captures=Path.GetFullPath(Path.Combine("work","cache-ui-native"));Directory.CreateDirectory(captures);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var image=cache.CaptureRenderedFrame())image!.Save(Path.Combine(captures,"cache-result.png"),new PngBitmapEncoderOptions());Click(Named<Button>(cache,"CancelCache"));Check(!cache.IsVisible&&owner.Pet.IsVisible,"native cancel keeps pet alive");
            var escape=new CacheMaintenanceWindow(directory);escape.Show(about);Wait(escape.PendingOperation);escape.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Check(!escape.IsVisible&&owner.Pet.IsVisible,"native Esc keeps pet alive");
            return checks;
        }finally{about.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}
    }
    private sealed class FixtureRuntime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();
        public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);
        public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("Fixture has no audio");
        public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("Fixture has no audio");
    }
}
