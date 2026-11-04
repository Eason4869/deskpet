using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            Check(pages.Count==6&&pages.Cast<object?>().Count(x=>x!=null)==1,"only initial partner page is materialized");
            Wait(owner.RestoreDefaultPreferencesAsync());
            typeof(SettingsWindow).GetMethod("RefreshPreferences",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            Check(pages[5]==null,"restoring preferences does not create unvisited behavior or storage");
            window.SelectSection("image");Dispatcher.UIThread.RunJobs();var provider=Named<TextBox>(window,"ImageProviderName");provider.Text="unsaved image provider";var models=pages[1];
            var editors=(IList)typeof(SettingsWindow).GetField("providers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            Check(editors.Count==0,"direct native image route does not create text editor");
            window.SelectSection("partner");window.SelectSection("image");Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(models,pages[1])&&ReferenceEquals(provider,Named<TextBox>(window,"ImageProviderName"))&&provider.Text=="unsaved image provider","model page and draft are reused");
            window.SelectSection("text");Dispatcher.UIThread.RunJobs();Check(editors.Count==1,"text editor is created once on first text visit");
            window.SelectSection("channels");Dispatcher.UIThread.RunJobs();var channel=Named<TextBox>(window,"ImChannelName");channel.Text="unsaved native channel";var channels=pages[4];
            window.SelectSection("partner");window.SelectSection("channels");Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(channels,pages[4])&&channel.Text=="unsaved native channel","channel view and draft are reused");
            window.SelectSection("storage");Dispatcher.UIThread.RunJobs();Check(Named<Button>(window,"ClearAllHistory")!=null,"direct storage route is reachable");
            Named<Button>(window,"ClearCache").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            var cache=window.OwnedWindows.OfType<CacheMaintenanceWindow>().Single();Wait(cache.PendingOperation);
            Named<Button>(cache,"CancelCache").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(!cache.IsVisible,"general data opens the real owned cache manager and permits cancel");
            window.SelectSection("behavior");Dispatcher.UIThread.RunJobs();Check(Named<Button>(window,"GeneralDataManagement")!=null,"general route is reachable");
            var nav=Named<ListBox>(window,"SettingsNavigation");
            Check(nav.ItemCount==6,"six main settings categories retain compact navigation");
            window.SelectSection("conversation");Dispatcher.UIThread.RunJobs();
            var conversationTabs=Named<TabControl>(window,"ConversationSettingsTabs");
            Check(conversationTabs.ItemCount==2,"conversation has two functioning preference tabs");conversationTabs.SelectedIndex=1;Dispatcher.UIThread.RunJobs();
            var readReplies=Named<CheckBox>(window,"ReadReplies");bool savedRead=owner.Settings.ReadReplies;
            readReplies.IsChecked=!savedRead;
            conversationTabs.SelectedIndex=0;conversationTabs.SelectedIndex=1;Dispatcher.UIThread.RunJobs();
            Check(readReplies.IsChecked==!savedRead,"conversation draft survives its own tabs");
            Check(owner.Settings.ReadReplies==savedRead,"conversation draft does not save on toggle");
            window.SelectSection("work");Dispatcher.UIThread.RunJobs();
            Check(!window.GetLogicalDescendants().OfType<CheckBox>().Any(c=>c.Name=="AgentDetailedProcess"),"work view does not own the global process preference");
            window.SelectSection("conversation");Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(readReplies,Named<CheckBox>(window,"ReadReplies"))&&readReplies.IsChecked==!savedRead,"conversation draft survives category switches");
            owner.Settings.AgentGoal="changed outside conversation";bool currentMode=!owner.Settings.AgentEnabled;owner.Settings.AgentEnabled=currentMode;
            Named<Button>(window,"SaveConversationSettings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Check(owner.Settings.ReadReplies==!savedRead&&owner.Settings.AgentGoal=="changed outside conversation"&&owner.Settings.AgentEnabled==currentMode,"conversation save changes only edited preferences and preserves newer mode");
            readReplies.IsChecked=savedRead;
            Named<Button>(window,"RestoreConversationSettings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Check(readReplies.IsChecked==!savedRead,"restore discards conversation draft without changing saved preference");
            window.SelectSection("tools");Dispatcher.UIThread.RunJobs();Check(Named<CheckBox>(window,"WebSearchEnabled")!=null,"web search is available under tools");
            var sources=Named<CheckBox>(window,"WebSearchSources");bool savedSources=owner.Settings.WebSearch.ShowSources;sources.IsChecked=!savedSources;
            Check(owner.Settings.WebSearch.ShowSources==savedSources,"web search toggle remains an unsaved draft");
            Named<Button>(window,"RestoreWebSearch").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(sources.IsChecked==savedSources,"web search restore discards its pending draft");
            sources.IsChecked=!savedSources;Named<Button>(window,"SaveWebSearch").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(owner.Settings.WebSearch.ShowSources==!savedSources&&owner.Settings.AgentGoal=="changed outside conversation","web search save preserves other settings");
            var searchProvider=Named<ComboBox>(window,"WebSearchProvider");var searchKey=Named<TextBox>(window,"WebSearchKey");var searchAddress=Named<TextBox>(window,"WebSearchAddress");
            searchProvider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"tavily");searchKey.Text="native-fixture-tavily";
            searchProvider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"deepseek");
            Check(searchKey.Text==""&&searchAddress.Text==WebSearchProviders.DefaultBaseUrl("deepseek"),"native provider switch isolates credentials and selects its official endpoint");searchKey.Text="native-fixture-deepseek";
            searchProvider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"tavily");Check(searchKey.Text=="native-fixture-tavily","native search keeps each provider draft");
            Named<Button>(window,"SaveWebSearch").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(owner.Settings.WebSearch.ReadConnection("deepseek").ApiKey=="native-fixture-deepseek"&&owner.Settings.WebSearch.ReadConnection("tavily").ApiKey=="native-fixture-tavily","native explicit save retains both provider configurations");
            searchProvider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"custom");var customFormat=Named<ComboBox>(window,"WebSearchCustomProtocol");
            Check(customFormat.IsVisible&&searchAddress.Text==""&&searchKey.Text=="","native custom search has its own endpoint and compatible format");searchAddress.Text="http://127.0.0.1:9999/custom/search";customFormat.SelectedIndex=Array.IndexOf(WebSearchProviders.SchemaIds,"deepseek");
            Named<Button>(window,"SaveWebSearch").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(owner.Settings.WebSearch.Provider=="custom"&&owner.Settings.WebSearch.CustomProtocol=="deepseek"&&owner.Settings.WebSearch.Connection.BaseUrl==searchAddress.Text,"native custom search saves its full endpoint and format");
            Check(owner.Settings.WebSearch.ReadConnection("tavily").ApiKey=="native-fixture-tavily","native custom search preserves the independent official service key");
            window.SelectSection("text");Dispatcher.UIThread.RunJobs();Check(!window.GetLogicalDescendants().OfType<CheckBox>().Any(c=>c.Name=="WebSearchEnabled"),"model editor no longer embeds web search");
            Check(Named<TabControl>(window,"ModelSettingsTabs").ItemCount==5,"five model and voice tabs remain at the top");
            foreach(string section in new[]{"asr","speech","live","work","voice"}){window.SelectSection(section);Dispatcher.UIThread.RunJobs();Check(window.SelectedSection==section,"lazy page route "+section);}
            var voice=Named<TextBox>(window,"VoiceName");voice.Text="unsaved voice name";
            window.SelectSection("text");window.SelectSection("partner");window.SelectSection("voice");Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(voice,Named<TextBox>(window,"VoiceName"))&&voice.Text=="unsaved voice name","voice draft survives model and category navigation");
            Check(pages.Cast<object?>().All(x=>x!=null),"every page can be materialized");
            var aboutButton=Named<Button>(window,"SettingsAboutAndUpdates");var guideButton=Named<Button>(window,"SettingsGuide");
            Check(guideButton.TranslatePoint(default,window)!.Value.X>aboutButton.TranslatePoint(default,window)!.Value.X,"about and the real configuration guide sit side by side beneath main navigation");
            var captures=Path.GetFullPath(Path.Combine("work","settings-layout-native",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(captures);
            foreach(var theme in new[]{"light","dark"})foreach(var width in new[]{570,960})foreach(var section in new[]{"partner","companion-behavior","greetings","text","conversation","work","tools","channels","behavior"}){
                DesktopTheme.Apply(theme);window.Width=width;window.SelectSection(section);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(captures,section+"-"+theme+"-"+width+".png"),new PngBitmapEncoderOptions());
                if(section=="work"){
                    var directoryInput=Named<TextBox>(window,"AgentWorkspaceInput");var right=directoryInput.TranslatePoint(new Point(directoryInput.Bounds.Width,0),window);
                    Check(right.HasValue&&right.Value.X<=window.Bounds.Width,"long workspace directory remains inside the settings viewport "+theme+" "+width);
                }
                if(section=="tools"){
                    var save=Named<Button>(window,"SaveWebSearch");var point=save.TranslatePoint(default,window);
                    Check(point.HasValue&&point.Value.Y>=0&&point.Value.Y+save.Bounds.Height<=window.Bounds.Height,"web search save remains visible "+theme+" "+width);
                }
                if(section=="conversation"){
                    var tabs=Named<TabControl>(window,"ConversationSettingsTabs");
                    for(int tab=0;tab<2;tab++){tabs.SelectedIndex=tab;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var tabFrame=window.CaptureRenderedFrame();tabFrame!.Save(Path.Combine(captures,section+"-tab"+tab+"-"+theme+"-"+width+".png"),new PngBitmapEncoderOptions());}
                    var save=Named<Button>(window,"SaveConversationSettings");var point=save.TranslatePoint(default,window);
                    Check(point.HasValue&&point.Value.Y>=0&&point.Value.Y+save.Bounds.Height<=window.Bounds.Height,"conversation save remains visible "+theme+" "+width);
                }
            }
            Console.WriteLine("Settings layout captures="+captures);
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
