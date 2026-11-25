using System.Security.Cryptography;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using Dafeiyu.Maintenance;
using DeskPet.Desktop;

internal static class FeatureNavigationUiChecks
{
    internal static int Run(string directory,string language,bool channelsOnly=false)
    {
        int checks=0;
        void Check([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string reason){if(!value)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
        void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("Native navigation fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name)where T:Control=>root.GetLogicalDescendants().OfType<T>().Distinct().Single(c=>c.Name==name);
        void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var protector = new TestProtector();
        Directory.CreateDirectory(directory);SecretProtection.Configure(protector);var settingsPath=Path.Combine(directory,"settings.json");new SettingsStore(settingsPath).Save(new PetSettings{UiLanguage=language,AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,AgentWorkspace=Path.Combine(directory,"workspace")});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory,protector));owner.Pet.Show();var settings=new SettingsWindow(owner);settings.Show();var chat=new ChatWindow(owner,false);chat.Show();var about=new AboutWindow(owner);
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
            owner.Settings.EnsureTextProviders();var textProvider=owner.Settings.TextProviders[0];textProvider.Name="Long provider name for visual inspection of truncation";textProvider.Models.Add("long-vision-model-id-for-testing");textProvider.ModelCapabilities["long-vision-model-id-for-testing"]=new ModelCapabilitySetting{Images=CapabilitySupport.Supported};owner.Settings.ImageDescriptionModel=new ModelReference{ProviderId="deleted-provider",ModelId="deleted-model"};
            settings.SelectSection("text");Dispatcher.UIThread.RunJobs();var imagePicker=Named<ComboBox>(settings,"ImageDescriptionModel");Check(imagePicker!=null,"native language settings expose image description selector");
            foreach(var theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);settings.Width=900;Capture(settings,"image-description-"+theme);settings.Width=600;Capture(settings,"image-description-narrow-"+theme);}DesktopTheme.Apply("light");

            Check(Named<Button>(chat,"ImageDescriptionMenu")!=null,"native composer exposes image description selection");
            var visionChoice=imagePicker.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>i.Tag is ModelReference r&&r.ModelId=="long-vision-model-id-for-testing");Check(visionChoice!=null,"native picker offers explicitly supported candidate");imagePicker.SelectedItem=visionChoice;TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Dispatcher.UIThread.RunJobs();Check(ReferenceEquals(imagePicker.SelectedItem,visionChoice),"native live language change retains fallback selection");TextResources.ApplyLanguage(TextResources.PrepareLanguage(language));Dispatcher.UIThread.RunJobs();owner.Settings.TextProviders[0].Name="newer native provider";string oldModel=owner.Settings.Text.Model;Click(Named<Button>(settings,"SaveTextProvider"));Check(owner.Settings.ImageDescriptionModel?.ModelId=="long-vision-model-id-for-testing"&&owner.Settings.Text.Model==oldModel&&owner.Settings.TextProviders[0].Name=="newer native provider","native fallback save preserves current model and newer provider");

            var persona=owner.Session.PersonaId;owner.WorkBindings.SaveImageDescription(null,persona,new ImageDescriptionOverride{Mode="disabled"});Check(owner.WorkBindings.RootSettings(persona).ImageDescriptionOverride?.Mode=="disabled"&&new ProjectCatalogStore(Path.Combine(directory,"projects.json")).Load().LegacyImageDescriptionOverrides[persona].Mode=="disabled","native root override persists and reaches effective request settings");
            bool failed=false;using(var locked=new FileStream(Path.Combine(directory,"projects.json.write.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){try{owner.WorkBindings.SaveImageDescription(null,persona,new ImageDescriptionOverride{Mode="selected",Model=new ModelReference{ProviderId="missing",ModelId="vision"}});}catch(IOException){failed=true;}}
            Check(failed&&owner.WorkBindings.RootSettings(persona).ImageDescriptionOverride?.Mode=="disabled"&&owner.WorkBindings.ImageDescriptionDraft(null,persona)?.Mode=="selected","native failed save keeps draft without publishing effective override");owner.WorkBindings.SaveImageDescription(null,persona,null);

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
            var imagesNav = Named<Button>(chat, "ImagesNavigation");
            var scheduledNav = Named<Button>(chat, "ScheduledTasksNavigation");
            Check(imagesNav.Parent is Panel topNav && topNav.Children.IndexOf(scheduledNav) == topNav.Children.IndexOf(imagesNav) + 1,
                "native scheduled tasks follow images in the main navigation");
            Click(imagesNav);
            Click(scheduledNav);
            Check(chat.CurrentPage == "scheduledTasks" && scheduledNav.Classes.Contains("selected"),
                "native scheduled tasks route shows the page and selects its navigation button");
            Click(Named<Button>(chat, "PageBack"));
            Check(chat.CurrentPage == "images" && imagesNav.Classes.Contains("selected") && !scheduledNav.Classes.Contains("selected"),
                "native back restores the images route and selection");
            Click(Named<Button>(chat, "PageForward"));
            Check(chat.CurrentPage == "scheduledTasks" && scheduledNav.Classes.Contains("selected"),
                "native forward restores the scheduled tasks route and selection");
            var scheduling = Named<ScheduledTasksPage>(chat, "ScheduledTasksPage");
            var templates = Named<UniformGrid>(scheduling, "ScheduledTaskTemplates");
            DesktopTheme.Apply("light");
            foreach (var columns in new[] { 3, 2 })
            {
                chat.Width = columns == 3 ? 1100 : 520;
                Dispatcher.UIThread.RunJobs();
                chat.UpdateLayout();
                Capture(chat, "scheduled-templates-" + columns);
                Check(templates.Columns == columns && templates.Children.Count == 6,
                    "native empty schedule lays out all six starters in " + columns + " columns");
                Check(templates.Children.OfType<Button>().All(tile => tile.Focusable && tile.Bounds.Width > 80
                    && Math.Abs(tile.Bounds.Height - tile.Bounds.Width) <= 1
                    && tile.GetLogicalDescendants().OfType<Image>().Single().Source is Bitmap { PixelSize.Width: 160 }),
                    "native scheduled starters are square, focusable, and render their embedded art");
            }
            foreach (var suffix in new[] { "Water", "Report", "Weekend", "Plan", "Stretch", "Reading" })
            {
                var button = Named<Button>(scheduling, (suffix == "Plan" ? "Planning" : suffix) + "TaskTemplate");
                var title = TextResources.Get("scheduledTasks.template" + suffix);
                var prompt = TextResources.Get("scheduledTasks.template" + suffix + "Prompt");
                Check(Avalonia.Automation.AutomationProperties.GetName(button) == title
                    && Avalonia.Automation.AutomationProperties.GetHelpText(button) == prompt
                    && ToolTip.GetTip(button) as string == prompt,
                    "native starter exposes its localized title and full prompt " + suffix);
                Click(button);
                var draftInput = Named<TextBox>(scheduling, "ScheduledTaskDraftInput");
                Check(draftInput.Text == prompt && draftInput.IsFocused
                    && !scheduling.GetLogicalDescendants().Contains(templates),
                    "native starter opens the focused prefilled draft " + suffix);
                scheduling.ShowTask(null!);
                Dispatcher.UIThread.RunJobs();
            }
            var createTask = Named<Button>(scheduling, "NewScheduledTask");
            DesktopTheme.Apply("dark");
            Check(createTask.Background is ISolidColorBrush { Color: { R: < 128, G: < 128, B: < 128 } }
                && createTask.Content is TextBlock { Foreground: ISolidColorBrush { Color: { R: > 200, G: > 200, B: > 200 } } },
                "native dark mode uses a dark create-task fill and light caption ink");
            DesktopTheme.Apply("light");
            Click(createTask);
            var editor = chat.OwnedWindows.Single(window => window.Name == "ScheduledTaskEditor");
            Check(editor.Owner == chat && editor.WindowStartupLocation == WindowStartupLocation.CenterOwner
                && !editor.ShowInTaskbar && editor.Content is Border { Name: "ApplicationDialogFrame" },
                "native scheduled task editor uses the centered application dialog frame");
            Check(!Named<Expander>(editor, "ScheduledTaskAdvanced").IsExpanded,
                "native scheduled task editor opens with Advanced collapsed");
            editor.UpdateLayout();
            var taskName = Named<TextBox>(editor, "ScheduledTaskName");
            var taskType = Named<ComboBox>(editor, "ScheduledTaskKind");
            Check(taskName.Bounds.Height > 0 && Math.Abs(taskName.Bounds.Height - taskType.Bounds.Height) <= 1,
                "native task name and type fields have equal rendered heights");
            Click(Named<Button>(editor, "CancelScheduledTask"));
            Check(!editor.IsVisible && chat.IsVisible && owner.ScheduledTasks.List().Count == 0,
                "native task editor cancel returns to the schedule page without saving");
            var quick = new ChatWindow(owner, true);
            quick.Show();
            try { Check(!quick.GetLogicalDescendants().OfType<Button>().Any(button => button.Name == "ScheduledTasksNavigation"), "native quick chat omits scheduled tasks navigation"); }
            finally { quick.Close(); }
            Check(Hash(sentinel)==historyHash&&Hash(settingsPath)==settingsHash,"native navigation and cancelled dialogs preserve settings/history bytes");
            if (language == "zh_CN") CheckRecoveredReminders();
            Console.WriteLine("Feature navigation native: "+language+" "+checks+" passed; captures="+captures);return checks;
        }finally{about.Close();settings.Close();chat.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}

        void CheckRecoveredReminders()
        {
            var isolated = Path.Combine(directory, "scheduled-reminders");
            Directory.CreateDirectory(isolated);
            new SettingsStore(Path.Combine(isolated, "settings.json")).Save(new PetSettings
            {
                UiLanguage = language, AutoUpdate = false, OnboardingState = "completed", GreetingEnabled = false
            });
            var due = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddDays(-1), TimeZoneInfo.Local);
            using (var schedules = new ScheduledTaskService(isolated, new DataMaintenanceGate()))
            {
                foreach (string name in new[] { "Morning reminder", "Afternoon reminder" })
                {
                    schedules.Save(new ScheduledTaskDefinition
                    {
                        Title = name, Content = name + " body",
                        Rule = new ScheduledTaskRule
                        {
                            Frequency = ScheduledFrequency.Once, OnceDateTicks = due.Date.Ticks,
                            MinuteOfDay = due.Hour * 60 + due.Minute
                        }
                    }, due.AddDays(-1).ToUniversalTime(), TimeZoneInfo.Local);
                }
            }
            var desktop = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            typeof(ClassicDesktopStyleApplicationLifetime).GetMethod("SubscribeGlobalEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(desktop, null);
            var fixture = new FixtureRuntime(isolated, protector) { PowerGeneration = 1 };
            using var schedulerHost = new DesktopController(desktop, fixture);
            Window? Popup() => desktop.Windows.SingleOrDefault(window => window.Name == "MissedScheduledTasksDialog");
            Window Notice() => desktop.Windows.Single(window => window.Name == "ScheduledTaskNotification");
            ChatWindow Main() => desktop.Windows.OfType<ChatWindow>().Single();
            try
            {
                Check(schedulerHost.ScheduledTasks.List().Count == 2, "native scheduler restores the two saved reminders");
                schedulerHost.Start();
                schedulerHost.ShowChat(false);
                Main().Close();
                Check(schedulerHost.ScheduledTasks.Pending().Count == 0 && Popup() == null,
                    "native system sleep holds the scheduled runtime while closing the main window");
                fixture.PowerGeneration = 2;
                var deadline = DateTime.UtcNow.AddSeconds(6);
                var frame = new DispatcherFrame();
                // This pumps the OS timer as well as the dispatcher jobs.
                using (var pulse = new System.Threading.Timer(_ => Dispatcher.UIThread.Post(() =>
                {
                    if (Popup() != null || DateTime.UtcNow >= deadline) frame.Continue = false;
                }), null, 0, 20)) Dispatcher.UIThread.PushFrame(frame);
                var popup = Popup() ?? throw new TimeoutException("Scheduled reminders were not recovered after native wake");
                schedulerHost.ShowScheduledMissed();
                var pending = schedulerHost.ScheduledTasks.Pending();
                Check(ReferenceEquals(popup, Popup()) && popup.Owner == null && pending.Count == 2,
                    "native wake groups both reminders in one popup with the main window closed");

                Click(popup.GetLogicalDescendants().OfType<Button>().First(button => button.Name == "RunMissedTask"));
                var firstNotice = Notice();
                Click(Named<Button>(popup, "RunMissedTask"));
                Check(ReferenceEquals(firstNotice, Notice()) && !popup.IsVisible,
                    "native simultaneous reminders queue behind the visible notification");
                Click(Named<Button>(firstNotice, "OpenTrayNotification"));
                var destination = Main();
                var list = Named<ListBox>(destination, "ScheduledTaskList");
                Check(destination.CurrentPage == "scheduledTasks" && (list.SelectedItem as ListBoxItem)?.Tag as string == pending[0].TaskId,
                    "native first reminder notification opens its task in the reopened main window");
                var secondNotice = Notice();
                Click(Named<Button>(secondNotice, "OpenTrayNotification"));
                Check(!ReferenceEquals(firstNotice, secondNotice) && (list.SelectedItem as ListBoxItem)?.Tag as string == pending[1].TaskId,
                    "native second reminder notification opens its own task");
            }
            finally
            {
                foreach (var window in desktop.Windows.ToArray()) window.Close();
                schedulerHost.Pet.Close();
                schedulerHost.Dispose();
                Wait(schedulerHost.ImageServicesShutdown);
            }
        }
    }
    private sealed class FixtureRuntime(string directory, ISecretProtector protector):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        internal ulong? PowerGeneration { get; set; }
        public override ulong? SystemPowerGeneration => PowerGeneration;
        public override void InitializeProtection()=>SecretProtection.Configure(protector);
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("Fixture has no audio");public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("Fixture has no audio");
    }
}
