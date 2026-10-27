using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class SettingsLazyChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TemporaryInstructions(DesktopController owner,out ProviderEditor editor)
    {string text=new string('x',100000);var weak=new WeakReference(text);owner.Settings.AgentGlobalInstructions=text;editor=new ProviderEditor(owner,"text");owner.Settings.AgentGlobalInstructions="";return weak;}
    internal static int Run(string directory)
    {
        int checks=0;
        void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        void Wait(Task task){var limit=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted){if(DateTime.UtcNow>limit)throw new TimeoutException("Settings lifecycle fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name) where T:Control=>root.GetLogicalDescendants().OfType<T>().Distinct().Single(c=>c.Name==name);
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,Wander=false,FollowCursor=false,SaveChatHistory=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));
        var window=new SettingsWindow(owner);window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var pages=(IList)typeof(SettingsWindow).GetField("pages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            Check(pages.Count==10&&pages.Cast<object?>().Count(x=>x!=null)==1,"only initial partner page is materialized");
            Wait(owner.RestoreDefaultPreferencesAsync());
            typeof(SettingsWindow).GetMethod("RefreshPreferences",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            Check(pages[1]==null&&pages[8]==null,"restoring preferences does not create unvisited behavior or storage");
            window.SelectSection("image");Dispatcher.UIThread.RunJobs();var provider=Named<TextBox>(window,"ImageProviderName");provider.Text="unsaved image provider";var models=pages[2];
            var editors=(IList)typeof(SettingsWindow).GetField("providers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            Check(editors.Count==0,"direct native image route does not create text editor");
            window.SelectSection("partner");window.SelectSection("image");Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(models,pages[2])&&ReferenceEquals(provider,Named<TextBox>(window,"ImageProviderName"))&&provider.Text=="unsaved image provider","model page and draft are reused");
            window.SelectSection("text");Dispatcher.UIThread.RunJobs();Check(editors.Count==1,"text editor is created once on first text visit");
            window.SelectSection("channels");Dispatcher.UIThread.RunJobs();var channel=Named<TextBox>(window,"ImChannelName");channel.Text="unsaved native channel";var channels=pages[9];
            window.SelectSection("partner");window.SelectSection("channels");Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(channels,pages[9])&&channel.Text=="unsaved native channel","channel view and draft are reused");
            window.SelectSection("storage");Dispatcher.UIThread.RunJobs();Check(Named<Button>(window,"ClearAllHistory")!=null,"direct storage route is reachable");
            window.SelectSection("behavior");Dispatcher.UIThread.RunJobs();Check(Named<Button>(window,"GeneralDataManagement")!=null,"general route is reachable");
            foreach(string section in new[]{"asr","speech","live","work","voice"}){window.SelectSection(section);Dispatcher.UIThread.RunJobs();Check(window.SelectedSection==section,"lazy page route "+section);}
            Check(pages.Cast<object?>().All(x=>x!=null),"every page can be materialized");
            var unrelated=TemporaryInstructions(owner,out var memoryEditor);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            Check(!unrelated.IsAlive,"native provider draft does not retain unrelated global instruction buffer");GC.KeepAlive(memoryEditor);memoryEditor.Dispose();
            return checks;
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
