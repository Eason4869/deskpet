using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime lifetime;
    private readonly SettingsStore store;
    private readonly DesktopRuntime runtime;
    private readonly TrayIcon? tray;
    private readonly List<IDisposable> hotkeys = new();
    private readonly HashSet<ChatWindow> chatHosts = new();
    private ChatWindow? quickChat, fullChat;
    private SettingsWindow? settingsWindow;
    private LiveWindow? liveWindow;
    private GuideWindow? guide;
    private bool openingGuide;
    private Task applicationExitDrain=Task.CompletedTask;
    private int conversationServicesFinalized,disposeStarted;
    private bool exiting,exitPrepared;
    private ImageTaskCompletionTracker imageCompletions=null!;
    private Window? imageNotification;
    private TextBlock? imageNotificationText;
    private string? imageNotificationId;
    private readonly DispatcherTimer imageNoticeTimer=new(){Interval=TimeSpan.FromSeconds(8)};
    private readonly bool showAutomaticGuide;
    internal bool ShouldShowAutomaticGuide => showAutomaticGuide;
    internal bool IsExiting=>exiting;
    public Dictionary<string, string> Drafts { get; } = new();
    public PetSettings Settings { get; }
    public List<PetSkin> Skins { get; }
    public PetSkin Skin { get; private set; }
    public ConversationSession Session { get; }
    public ConversationQueue Queue { get; }
    internal DesktopWorkspaces WorkBindings { get; }
    public DictationSession Dictation { get; }
    public GeminiLiveSession Live { get; }
    public ApiClient Api { get; }
    public GeneratedImageStore ImageStore { get; }
    public ImageGenerationClient ImageClient { get; }
    public ImageGenerationTaskManager ImageTasks { get; }
    public ImageAssetUsageRegistry ImageUsage { get; }
    public ImageAssetManagementService ImageManagement { get; }
    public Task ImageServicesShutdown { get; private set; } = Task.CompletedTask;
    public AgentQuestionController Questions { get; } = new();
    public PetWindow Pet { get; }
    public HolidayController Holidays { get; }
    internal bool HolidayBusinessBusy => exiting || Session.IsBusy || Session.NeedsExecutionReconciliation ||
        Session.CurrentAgentTask?.Status is "running" or "needs_reconciliation" or "waiting_approval" or "waiting_input" or "recovering" ||
        Queue.IsRunning || Queue.HasPending || Questions.Requests.Count != 0 || Live.IsActive || Dictation.IsBusy;
    public string DataDirectory { get; }
    public bool GuideLocked => guide != null || openingGuide;
    public bool HasOpenPanel => GuideLocked || quickChat?.IsVisible == true || fullChat?.IsVisible == true || settingsWindow?.IsVisible == true || liveWindow?.IsVisible == true;
    public event Action? Changed;
    public DesktopController(IClassicDesktopStyleApplicationLifetime desktop, DesktopRuntime? runtime = null, Func<GeneratedImageStore, ImageGenerationClient>? imageClientFactory = null, HolidayController? holidays = null, Func<SpriteAssets>? spriteAssetsFactory = null, Func<Func<OfficialCompanionInstallation, CancellationToken, Task>, IOfficialCompanionSource>? officialSourceFactory = null,Func<string,AgentJobApplicationService>? jobsFactory=null)
    {
        lifetime = desktop;
        runtime ??= new MacRuntime();
        this.runtime = runtime;
        DataDirectory = runtime.DataDirectory;
        Directory.CreateDirectory(DataDirectory);
        Holidays = holidays ?? new HolidayController(new HolidayCalendar(), new HolidayCatalog(), new HolidayStateStore(Path.Combine(DataDirectory, "holiday-state.json")));
        runtime.InitializeProtection();
        var settingsPath = Path.Combine(DataDirectory, "settings.json");
        bool firstLaunch = !File.Exists(settingsPath);
        store = new SettingsStore(settingsPath); Settings = store.Load();
        TextResources.ConfigureStartupLanguage(Settings.UiLanguage);
        DesktopTheme.Apply(Settings.ThemeMode);
        // The Avalonia client starts in chat; Mac has no local file Agent.
        // Shared settings also serve the WPF client, whose default is work.
        if (firstLaunch || !OperatingSystem.IsWindows()) Settings.AgentEnabled = false;
        var onboarding = OnboardingUpgrade.Prepare(Settings, ReleaseInfo.DisplayVersion);
        if (onboarding.Changed) store.Save(Settings);
        showAutomaticGuide = onboarding.ShowGuide || Environment.GetCommandLineArgs().Contains(ApplicationRestart.ResumeArgument);
        InitializeCompanions(officialSourceFactory);
        Skins = Task.Run(() => SpriteAssets.LoadSkins(DataDirectory)).GetAwaiter().GetResult();
        embeddedEbao = Skins.First(s => s.OfficialCharacterId == "e-bao" && s.BuiltIn && !s.IsPlaceholder);
        var requested = Settings.PendingOfficialCompanionId.Length != 0 ? Skins.FirstOrDefault(s => s.OfficialCharacterId == Settings.PendingOfficialCompanionId) : ResolveSavedCompanion();
        requested ??= embeddedEbao;
        Skin = RestoreCachedStartupCompanion(requested);
        if (Skin.IsPlaceholder) { Settings.PendingOfficialCompanionId = Skin.OfficialCharacterId!; Skin = embeddedEbao; }
        PersonaProfiles.Activate(Settings, Skin.Id, Skin.Prompt, Skin.Voices, Skin.OfficialCharacterId ?? Skin.BuiltinCharacterId);
        Settings.SelectedCompanionKey = Skin.PickerKey; store.Save(Settings);
        Api = new ApiClient();
        ImageUsage = new ImageAssetUsageRegistry();
        ImageStore = new GeneratedImageStore(DataDirectory, ImageUsage);
        ImageClient = imageClientFactory?.Invoke(ImageStore) ?? new ImageGenerationClient(ImageStore);
        Session = new ConversationSession(Api, runtime.CreateSpeech(Api), runtime.CreateRecorder, new HistoryStore(Path.Combine(DataDirectory, "history")), () => Settings.SaveChatHistory);
        ImageTasks = new ImageGenerationTaskManager(new ImageGenerationTaskStore(DataDirectory), ImageStore, ImageClient, ImageUsage);
        ImageTasks.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        ImageTasks.Concurrency=Settings.ImageGenerationConcurrency;
        imageCompletions=new ImageTaskCompletionTracker(ImageTasks.List());ImageTasks.Changed+=ImageTaskCompleted;imageNoticeTimer.Tick+=(_,_)=>{imageNoticeTimer.Stop();imageNotification?.Hide();};
        Session.ImageStore = ImageStore; Session.ImageClient = ImageClient; Session.ImageTasks = ImageTasks; Session.ImageUsage = ImageUsage;
        ImageManagement = new ImageAssetManagementService(ImageStore, ImageUsage, new ImageAssetReferenceResolver(DataDirectory, ImageStore, ImageTasks));
        Session.QuestionProvider = Questions;
        Session.SelectPersona(Skin.Id);
        Dictation = new DictationSession(new ApiClient(), runtime.CreateRecorder, () => Settings);
        Queue = new ConversationQueue(Session, () => Settings, () => Skin.Name, Path.Combine(DataDirectory, "queue.json"));
        Live = new GeminiLiveSession(() => new GeminiLiveSocket(), runtime.CreateMicrophone, runtime.CreateLiveOutput);
        Live.TurnCommitted += (user, reply, status) => Session.AddLiveTurn(Session.PersonaId, user, reply, status);
        Session.StateChanged += Notify; Session.MessageAdded += _ => Notify(); Session.MessageUpdated += _ => Notify(); Session.HistoryChanged += Notify;
        Queue.Changed += Notify; Live.StateChanged += Notify; Dictation.Changed += Notify;
        Questions.Changed += Notify;
        Session.AgentTaskChanged += Notify;
        WorkBindings=new DesktopWorkspaces(this,runtime,jobsFactory);
        Pet = new PetWindow(this, spriteAssetsFactory?.Invoke());
        lifetime.ShutdownRequested+=ShutdownRequested;
        if (!runtime.ShowTray) return;
        using var icon = SpriteAssets.OpenAsset("pet.ico");
        tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "DeskPet · " + Skin.DisplayName, IsVisible = true };
        tray.Clicked += (_, _) => ShowPet();
        var menu = new NativeMenu();
        void Add(string text, Action action) { var item = new NativeMenuItem(text); item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add(TextResources.Get("desktopController.desktopController.text"), ShowPet); Add(TextResources.Get("desktopController.desktopController.text.alternativeA"), () => ShowChat(true)); Add(TextResources.Get("desktopController.desktopController.text.alternativeB"), () => ShowChat(false));
        Add(TextResources.Get("desktopController.desktopController.text.alternativeC"), () => Pet.ShowPalette()); Add(TextResources.Get("desktopController.desktopController.text.alternativeD"), ShowSettings); Add(TextResources.Get("desktopController.desktopController.text.alternativeE"), ShowGuide); Add(TextResources.Get("desktopController.desktopController.text.alternativeF"), ShowAbout); Add(TextResources.Get("desktopController.desktopController.text.alternativeG"), () => Run(ExitAsync));
        tray.Menu = menu;
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
    }
    public void Start()
    {
        RegisterHotkeys();
        if (showAutomaticGuide) Dispatcher.UIThread.Post(ShowGuide);
        Dispatcher.UIThread.Post(() => Run(StartOfficialCompanionsAsync));
        if (!string.IsNullOrWhiteSpace(store.Warning)) Dispatcher.UIThread.Post(() => Error(new InvalidOperationException(store.Warning)));
    }
    private void ImageTaskCompleted(ImageGenerationTaskSnapshot task)
    {
        if(!imageCompletions.Observe(task))return;
        Dispatcher.UIThread.Post(()=>
        {
            if(exiting)return;imageNotificationId=task.Id;
            if(imageNotification==null)
            {
                imageNotificationText=Ui.Text("");
                imageNotification=Ui.Window(TextResources.Get("desktopController.imageTaskCompleted.window"),Ui.Column(imageNotificationText,Ui.Row(Ui.Button(TextResources.Get("desktopController.imageTaskCompleted.text"),()=>{imageNotification?.Hide();imageNoticeTimer.Stop();if(imageNotificationId!=null){ShowChat(false);fullChat!.ShowImageTask(imageNotificationId);}}),Ui.Button(TextResources.Get("common.close"),()=>{imageNotification?.Hide();imageNoticeTimer.Stop();}))),400,230);
                imageNotification.Name="ImageTaskCompletionNotification";imageNotification.ShowActivated=false;imageNotification.ShowInTaskbar=false;
                imageNotification.Closed+=(_,_)=>{imageNotification=null;imageNoticeTimer.Stop();};
            }
            imageNotificationText!.Text=TextResources.Format("desktopController.imageTaskCompleted.imageNotificationTextImageGenerationMotion",ImageGenerationMotion.Label(task));
            imageNotification.Show();imageNoticeTimer.Stop();imageNoticeTimer.Start();
        });
    }
    internal void NotifyWorkChanged()=>Notify();
    internal ChatWindow? WorkApprovalHost(string session)=>chatHosts.FirstOrDefault(host=>host.IsVisible&&host.QuestionSessionId==session);
    private void Notify() => Dispatcher.UIThread.Post(() => { ApplyPendingCompanionAppearance(); Changed?.Invoke(); });
    public void Save() { store.Save(Settings); ImageTasks.Concurrency=Settings.ImageGenerationConcurrency; Notify(); }
    public void SetUiLanguage(string code)
    {
        store.SaveUiLanguage(Settings,code);
        string restart=TextResources.Get("languages.restartRequired");
        if(UiLanguages.Resolve(Settings.UiLanguage)!=TextResources.CurrentLanguage)Session.ReportNotice(restart);
        else if(Session.LastNotice==restart)Session.ReportNotice("");
        Notify();
    }
    public void ShowPet() { if (!Pet.IsVisible) Pet.Show(); Pet.Clamp(); }
    public void ShowChat(bool quick)
    {
        var window = quick ? quickChat : fullChat;
        if (window == null)
        {
            window = new ChatWindow(this, quick);
            if (quick) quickChat = window; else fullChat = window;
            window.Closed += (_, _) => { if (quick) quickChat = null; else fullChat = null; };
        }
        window.Show(); window.Activate();
    }
    public void ShowSettings()
    {
        if (settingsWindow == null) { settingsWindow = new SettingsWindow(this); settingsWindow.Closed += (_, _) => settingsWindow = null; }
        settingsWindow.Show(); settingsWindow.Activate();
    }
    public void ShowSettingsSection(string section) { ShowSettings(); settingsWindow!.SelectSection(section); }
    internal Window? GuideMainWindow => settingsWindow?.IsVisible==true&&settingsWindow.WindowState!=WindowState.Minimized?settingsWindow:fullChat?.IsVisible==true&&fullChat.WindowState!=WindowState.Minimized?fullChat:null;
    internal async Task RestartForOnboardingAsync()
    {
        ApplicationRestart.Requested=true;
        try{await ExitAsync();}catch{ApplicationRestart.Requested=false;throw;}
    }
    internal async Task RepairImageDeliveryAsync(string id)
    {
        if(exiting)throw new OperationCanceledException(TextResources.Get("desktopController.repairImageDeliveryAsync.text"));
        var task=ImageTasks.Get(id);bool persistent=task!=null&&task.Origin.ConversationId!=Session.ImageConversationId;
        if(persistent)await WorkBindings.Jobs.EnsureWritableAsync(CancellationToken.None);
        var sessions=new[]{Session}.Concat(WorkBindings.Manager.OpenWorkspaces.Select(workspace=>workspace.Session));
        await ImageTaskCenterState.RepairExistingAsync(ImageTasks,ImageStore,ImageUsage,sessions,persistent?WorkBindings.Snapshot:null,DataDirectory,id,Settings.SaveChatHistory,CancellationToken.None,persistent?WorkBindings.Manager:null);
    }
    public void ShowImages(GeneratedImage? image = null) { ShowChat(false); fullChat!.ShowImages(image); }
    internal void RegisterChatHost(ChatWindow host) => chatHosts.Add(host);
    internal void ChatHostClosed(ChatWindow host)
    {
        chatHosts.Remove(host);
        // True exit owns question cleanup in the tracked drain. The view must
        // not read PersonaId (the session writer gate) after the exit deadline.
        if(exiting)return;
        var session = host.QuestionSessionId;
        if (!chatHosts.Any(other => other.IsVisible && other.QuestionSessionId == session)) Questions.CancelSession(session);
    }
    internal void WorkViewChanged(ChatWindow host,string oldSession)
    {if(!chatHosts.Any(other=>other!=host&&other.IsVisible&&other.QuestionSessionId==oldSession))Questions.CancelSession(oldSession);Notify();}
    public void ShowLive()
    {
        if (liveWindow == null) { liveWindow = new LiveWindow(this); liveWindow.Closed += (_, _) => liveWindow = null; }
        liveWindow.Show(); liveWindow.Activate();
    }
    public void ShowGuide()
    {
        if (openingGuide) return;
        if (guide != null) { guide.Activate(); return; }
        openingGuide = true;
        Run(async () =>
        {
            try
            {
                await Queue.PauseAsync(); await Dictation.CancelAsync(); await Live.StopAsync(); await Session.StopAsync();
                if (guide != null) { guide.Activate(); return; }
                guide = new GuideWindow(this); guide.Closed += (_, _) => { guide = null; Notify(); Run(StartOfficialCompanionsAsync); }; guide.Show();
            }
            finally { openingGuide = false; }
        });
    }
    public void ShowAbout() => new AboutWindow(this).Show();
    public void RegisterHotkeys()
    {
        foreach (var binding in hotkeys) binding.Dispose(); hotkeys.Clear();
        if (!OperatingSystem.IsMacOS()) return;
        // Carbon key codes: Space, D, H. Defaults remain explicit in Settings UI.
        foreach (var binding in new (string Id, uint Key, Action Action)[] { ("chat", 49, () => ShowChat(true)), ("show", 2, ShowPet), ("hide", 4, Pet.Hide) })
        {
            if (!Settings.Hotkeys.TryGetValue(binding.Id, out var value) || value != "enabled") continue;
            try { hotkeys.Add(MacServices.RegisterHotKey(binding.Key, MacHotKeyModifiers.Command | MacHotKeyModifiers.Shift, () => Dispatcher.UIThread.Post(binding.Action))); }
            catch (Exception e) { Error(new InvalidOperationException(TextResources.Format("desktopController.registerHotkeys.error",binding.Id,e.Message))); }
        }
    }
    public void Run(Func<Task> action) => _ = RunSafe(action);
    public Task<bool> RequestMicrophoneAccessAsync() => runtime.RequestMicrophoneAccessAsync();
    public async Task StopAsync() { await Dictation.CancelAsync(); await Queue.PauseAsync(); await Live.StopAsync(); }
    private async Task RunSafe(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Error(e); } }
    public void Error(Exception e)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Error(e)); return; }
        var localized=AgentJobUiText.Error(e);var text=localized!=e.Message?localized:e is ApiException or InvalidOperationException or InvalidDataException or PlatformNotSupportedException ? e.Message : TextResources.Format("desktopController.error.messageEGetType",e.GetType().Name);
        var window = Ui.Window(TextResources.Get("desktopController.error.window"), Ui.Text(text), 460, 250); window.Show();
    }
    private void ShutdownRequested(object? sender,ShutdownRequestedEventArgs args) { if(exitPrepared)return;args.Cancel=true;if(!exiting)Run(ExitAsync); }
    public async Task ExitAsync()
    {
        if (exiting) return; exiting = true;foreach(var host in chatHosts)host.FreezeForExit();
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ordinary=ImageTaskCenterState.BeginExitDrain(DrainApplicationForExitAsync,out applicationExitDrain);
            await WorkBindings.StopForExitAsync(deadline.Token).WaitAsync(deadline.Token);
            await ordinary;
            await Dispatcher.UIThread.InvokeAsync(()=>{exitPrepared=true;lifetime.Shutdown();});
        }
        catch(OperationCanceledException error){exiting=false;throw new InvalidOperationException(TextResources.Get("agentJobs.exitBlocked"),error);}
        catch { exiting = false; throw; }
    }
    private async Task DrainApplicationForExitAsync()
    {
        await Task.WhenAll(ImageTasks.StopAsync(CancellationToken.None),Task.Run(()=>Session.StopAsync()),StopAsync());
        Save();Holidays.RetrySave();await Session.FlushHistoryAsync();
    }
    private void FinalizeConversationServices()
    {
        if(Interlocked.Exchange(ref conversationServicesFinalized,1)!=0)return;
        Queue.Dispose();
        Dictation.Dispose();
        Live.Dispose();
        Session.Dispose();
        Questions.Dispose();
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposeStarted,1)!=0)return;
        exiting = true;
        companionLifetime.Cancel(); preparation?.Dispose(); companionSelection?.Cancel(); companionRefreshProgress?.Dispose(); officialCompanions.Dispose(); companionNative.Dispose();
        lifetime.ShutdownRequested-=ShutdownRequested;
        CloseAgentJobViews();imageNoticeTimer.Stop();
        imageNotification?.Close();
        ImageTasks.Changed-=ImageTaskCompleted;
        Session.AgentTaskChanged -= Notify;
        Holidays.RetrySave();
        tray?.Dispose();
        foreach(var binding in hotkeys)binding.Dispose();
        ImageServicesShutdown=Task.Run(async()=>
        {
            try{await applicationExitDrain;}catch(Exception){}
            await ImageTasks.StopAsync(CancellationToken.None);
            await WorkBindings.StopForExitAsync();
            FinalizeConversationServices();
            await Session.WaitForIdleAsync();
            await Session.FlushHistoryAsync();
            ImageClient.Dispose();
            ImageStore.Dispose();
        });
    }
}
