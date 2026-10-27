using System.Security.Cryptography;
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
using DeskPet.Desktop;

internal static class FeatureNavigationUiChecks
{
    internal static int Run(string directory,string language,bool channelsOnly=false)
    {
        int checks=0;
        void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
        void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("Native navigation fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name)where T:Control=>root.GetLogicalDescendants().OfType<T>().Distinct().Single(c=>c.Name==name);
        void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());var settingsPath=Path.Combine(directory,"settings.json");new SettingsStore(settingsPath).Save(new PetSettings{UiLanguage=language,AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,AgentWorkspace=Path.Combine(directory,"workspace")});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));owner.Pet.Show();var settings=new SettingsWindow(owner);settings.Show();var chat=new ChatWindow(owner,false);chat.Show();var about=new AboutWindow(owner);
        var captures=Path.GetFullPath(Path.Combine("work","feature-navigation","native-"+language+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(captures);
        void Capture(Window window,string name){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(captures,name+".png"),new PngBitmapEncoderOptions());}
        try{
            if(channelsOnly){
                settings.SelectSection("channels");Dispatcher.UIThread.RunJobs();
                Check(settings.SelectedSection=="channels","channel navigation route");
                Check(Named<TextBox>(settings,"ImSecret").PasswordChar!='\0',"secret is masked");
                var channelName=Named<TextBox>(settings,"ImChannelName");channelName.Text="unsaved channel";
                UiLanguageChange.Apply(new SettingsStore(settingsPath),owner.Settings,language=="zh_CN"?"en_US":"zh_CN");Dispatcher.UIThread.RunJobs();
                Check(channelName.Text=="unsaved channel","live language preserves channel draft");
                Check(Named<ComboBox>(settings,"ImPlatform").ItemCount==5,"five platforms available");
                Click(Named<Button>(settings,"ImSave"));Check(owner.ImChannels.Profiles.Count==1,"channel saved without connecting");
                Click(Named<Button>(settings,"ImDelete"));Check(owner.ImChannels.Profiles.Count==0&&Named<ComboBox>(settings,"ImChannelList").ItemCount==0,"deletion removes stale profile and credentials from list");
                UiLanguageChange.Apply(new SettingsStore(settingsPath),owner.Settings,language);Dispatcher.UIThread.RunJobs();Check(Named<Button>(settings,"ImOpenPlatform")!=null,"platform browser entry exists");
                Capture(settings,"channels");settings.Width=600;Dispatcher.UIThread.RunJobs();Capture(settings,"channels-narrow");
                Console.WriteLine("Channels UI captures="+captures);return checks;
            }
            Check(TextResources.CurrentLanguage==language,"native frozen startup language matches fixture");
            var draft=Named<TextBox>(chat,"ChatDraft");draft.Text="S3 preserved native composer";var session=chat.BoundSession;
            settings.SelectSection("image");Dispatcher.UIThread.RunJobs();var provider=Named<TextBox>(settings,"ImageProviderName");provider.Text="S3 native unsaved provider";
            var sentinel=Path.Combine(directory,"history","s3-navigation-sentinel.json");Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);File.WriteAllText(sentinel,"owned history survives navigation");Wait(owner.FlushDeferredSettingsAsync());string historyHash=Hash(sentinel),settingsHash=Hash(settingsPath);
            settings.SelectSection("behavior");Dispatcher.UIThread.RunJobs();var general=Named<Button>(settings,"GeneralDataManagement");Check((string)general.Content! == TextResources.Get("dataManagement.title"),"native General exposes current-language data entry");general.Focus();Check(general.IsFocused,"native General data entry accepts real keyboard focus");Click(general);
            Check(settings.SelectedSection=="storage"&&!settings.GetLogicalDescendants().OfType<TextBox>().Any(c=>c.Name=="SettingsCategorySearch"),"native real General route reaches existing storage without global search");
            var pageHost=Named<ContentControl>(settings,"SettingsPageHost");var cachedData=pageHost.Content;
            foreach(string action in new[]{"ClearAllHistory","RestoreDefaultPreferences"}){
                var button=Named<Button>(settings,action);button.Focus();Check(button.IsFocused,"native actual data action accepts keyboard focus "+action);Check(!settings.OwnedWindows.OfType<DataActionConfirmationWindow>().Any(),"native focus does not execute "+action);
                Click(button);var confirmation=settings.OwnedWindows.OfType<DataActionConfirmationWindow>().Single();Check(confirmation.Owner==settings,"native explicit action owns separate confirmation "+action);confirmation.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Check(!confirmation.IsVisible&&owner.Pet.IsVisible,"native Escape cancels without closing pet "+action);
            }
            var attachments=Named<Button>(settings,"OpenPreservedAttachments");Click(attachments);var attachmentDeadline=DateTime.UtcNow.AddSeconds(15);while(!attachments.IsEnabled){if(DateTime.UtcNow>attachmentDeadline)throw new TimeoutException("Native attachment entry did not complete");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Check(Named<TextBlock>(settings,"DataManagementStatus").Text==TextResources.Get("dataManagement.attachmentsEmpty"),"native attachment entry gives actual empty state");
            settings.SelectSection("image");Dispatcher.UIThread.RunJobs();Check(ReferenceEquals(provider,Named<TextBox>(settings,"ImageProviderName"))&&provider.Text=="S3 native unsaved provider","native return preserves same provider editor draft");settings.SelectSection("storage");Dispatcher.UIThread.RunJobs();Check(ReferenceEquals(cachedData,pageHost.Content),"native storage route reuses existing page");
            about.Show();Click(Named<Button>(about,"ClearCache"));var cache=about.OwnedWindows.OfType<CacheMaintenanceWindow>().Single();Wait(cache.PendingOperation);Check(cache.Owner==about,"native About cache entry opens owned scan dialog");Click(Named<Button>(cache,"CancelCache"));Check(!cache.IsVisible&&owner.Pet.IsVisible,"native cache cancel keeps pet and settings alive");about.Close();
            chat.Activate();var avatar=Named<Button>(chat,"ShellAvatar");Click(avatar);var entry=avatar.ContextMenu!.Items.OfType<MenuItem>().Single(c=>c.Name=="ProfileCompanionHome");entry.Focus();Check(entry.IsFocused,"native profile home accepts real keyboard focus");entry.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Dispatcher.UIThread.RunJobs();
            var home=chat.GetLogicalDescendants().OfType<CompanionHomePage>().Single();Wait(home.RefreshAsync());Check(chat.CurrentPage=="companion"&&!avatar.ContextMenu.IsOpen,"native keyboard profile route opens existing home and closes menu");
            var relationship=Named<Expander>(home,"CompanionRelationshipSettings");relationship.IsExpanded=true;Check(Named<ToggleSwitch>(home,"RelationshipGrowthEnabled").IsVisible&&home.PendingSettings.IsCompleted,"native settings group reveals real controls without saving");
            foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);settings.Width=570;settings.Height=760;Capture(settings,"storage-narrow-"+theme);chat.Width=570;chat.Height=800;Capture(chat,"home-narrow-"+theme);Check(Named<Button>(settings,"ClearAllHistory").IsVisible&&Named<Button>(settings,"OpenPreservedAttachments").IsVisible,"native narrow data and attachment entries remain reachable "+theme);}
            chat.NavigateHistory(-1);Dispatcher.UIThread.RunJobs();Check(chat.CurrentPage=="chat"&&ReferenceEquals(session,chat.BoundSession)&&ReferenceEquals(draft,Named<TextBox>(chat,"ChatDraft"))&&draft.Text=="S3 preserved native composer","native history returns to same session and composer draft");
            Check(Hash(sentinel)==historyHash&&Hash(settingsPath)==settingsHash,"native navigation and cancelled dialogs preserve settings/history bytes");
            Console.WriteLine("Feature navigation native: "+language+" "+checks+" passed; captures="+captures);return checks;
        }finally{about.Close();settings.Close();chat.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}
    }
    private sealed class FixtureRuntime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("Fixture has no audio");public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("Fixture has no audio");
    }
}
