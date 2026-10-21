using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed class DesktopController : IDisposable
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
    private Task applicationExitDrain=Task.CompletedTask;
    private bool exiting,exitPrepared;
    private ImageTaskCompletionTracker imageCompletions=null!;
    private Window? imageNotification;
    private TextBlock? imageNotificationText;
    private string? imageNotificationId;
    private readonly DispatcherTimer imageNoticeTimer=new(){Interval=TimeSpan.FromSeconds(8)};
    private readonly bool showAutomaticGuide;
    internal bool ShouldShowAutomaticGuide => showAutomaticGuide;
    public Dictionary<string, string> Drafts { get; } = new();
    public PetSettings Settings { get; }
    public List<PetSkin> Skins { get; }
    public PetSkin Skin { get; private set; }
    public ConversationSession Session { get; }
    public ConversationQueue Queue { get; }
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
    public string DataDirectory { get; }
    public bool GuideLocked => guide != null;
    public bool HasOpenPanel => GuideLocked || quickChat?.IsVisible == true || fullChat?.IsVisible == true || settingsWindow?.IsVisible == true || liveWindow?.IsVisible == true;
    public event Action? Changed;
    public DesktopController(IClassicDesktopStyleApplicationLifetime desktop, DesktopRuntime? runtime = null, Func<GeneratedImageStore, ImageGenerationClient>? imageClientFactory = null)
    {
        lifetime = desktop;
        runtime ??= new MacRuntime();
        this.runtime = runtime;
        DataDirectory = runtime.DataDirectory;
        Directory.CreateDirectory(DataDirectory);
        runtime.InitializeProtection();
        var settingsPath = Path.Combine(DataDirectory, "settings.json");
        bool firstLaunch = !File.Exists(settingsPath);
        store = new SettingsStore(settingsPath); Settings = store.Load();
        DesktopTheme.Apply(Settings.ThemeMode);
        // The Avalonia client starts in chat; Mac has no local file Agent.
        // Shared settings also serve the WPF client, whose default is work.
        if (firstLaunch || !OperatingSystem.IsWindows()) Settings.AgentEnabled = false;
        var onboarding = OnboardingUpgrade.Prepare(Settings, ReleaseInfo.DisplayVersion);
        if (onboarding.Changed) store.Save(Settings);
        showAutomaticGuide = onboarding.ShowGuide;
        Skins = SpriteAssets.LoadSkins(DataDirectory); Skin = Skins.FirstOrDefault(s => s.Id == Settings.SkinId || s.PackageId == Settings.SkinId) ?? Skins[0];
        PersonaProfiles.Activate(Settings, Skin.Id, Skin.Prompt, Skin.Voices);
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
        Pet = new PetWindow(this);
        lifetime.ShutdownRequested+=ShutdownRequested;
        if (!runtime.ShowTray) return;
        using var icon = SpriteAssets.OpenAsset("pet.ico");
        tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "DeskPet · " + Skin.Name, IsVisible = true };
        tray.Clicked += (_, _) => ShowPet();
        var menu = new NativeMenu();
        void Add(string text, Action action) { var item = new NativeMenuItem(text); item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("显示伙伴", ShowPet); Add("快速对话", () => ShowChat(true)); Add("完整聊天", () => ShowChat(false));
        Add("互动轮盘", () => Pet.ShowPalette()); Add("设置", ShowSettings); Add("使用引导", ShowGuide); Add("关于与更新", ShowAbout); Add("退出", () => Run(ExitAsync));
        tray.Menu = menu;
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
    }
    public void Start()
    {
        RegisterHotkeys();
        if (showAutomaticGuide) Dispatcher.UIThread.Post(ShowGuide);
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
                imageNotification=Ui.Window("图片任务",Ui.Column(imageNotificationText,Ui.Row(Ui.Button("查看任务",()=>{imageNotification?.Hide();imageNoticeTimer.Stop();if(imageNotificationId!=null){ShowChat(false);fullChat!.ShowImageTask(imageNotificationId);}}),Ui.Button("关闭",()=>{imageNotification?.Hide();imageNoticeTimer.Stop();}))),400,230);
                imageNotification.Name="ImageTaskCompletionNotification";imageNotification.ShowActivated=false;imageNotification.ShowInTaskbar=false;
                imageNotification.Closed+=(_,_)=>{imageNotification=null;imageNoticeTimer.Stop();};
            }
            imageNotificationText!.Text=ImageGenerationMotion.Label(task)+"。可在任务中心查看结果和处理详情。";
            imageNotification.Show();imageNoticeTimer.Stop();imageNoticeTimer.Start();
        });
    }
    private void Notify() => Dispatcher.UIThread.Post(() => Changed?.Invoke());
    public void Save() { store.Save(Settings); ImageTasks.Concurrency=Settings.ImageGenerationConcurrency; Notify(); }
    public async Task SelectSkinAsync(PetSkin skin)
    {
        await Queue.PauseAsync(); await Dictation.CancelAsync(); await Live.StopAsync(); await Session.StopAsync();
        PersonaProfiles.SaveVoices(Settings); Skin = skin;
        PersonaProfiles.Activate(Settings, skin.Id, skin.Prompt, skin.Voices); Session.SelectPersona(skin.Id);
        Pet.SelectSkin(); Save(); if (tray != null) tray.ToolTipText = "DeskPet · " + skin.Name;
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
    internal Task RepairImageDeliveryAsync(string id) => ImageTaskCenterState.RepairExistingAsync(ImageTasks,ImageStore,ImageUsage,new[]{Session},null,DataDirectory,id,Settings.SaveChatHistory,CancellationToken.None);
    public void ShowImages(GeneratedImage? image = null) { ShowChat(false); fullChat!.ShowImages(image); }
    internal void RegisterChatHost(ChatWindow host) => chatHosts.Add(host);
    internal void ChatHostClosed(ChatWindow host)
    {
        chatHosts.Remove(host);
        var session = host.QuestionSessionId;
        if (!chatHosts.Any(other => other.IsVisible && other.QuestionSessionId == session)) Questions.CancelSession(session);
    }
    public void ShowLive()
    {
        if (liveWindow == null) { liveWindow = new LiveWindow(this); liveWindow.Closed += (_, _) => liveWindow = null; }
        liveWindow.Show(); liveWindow.Activate();
    }
    public void ShowGuide()
    {
        if (guide != null) { guide.Activate(); return; }
        Run(async () =>
        {
            await Queue.PauseAsync(); await Dictation.CancelAsync(); await Live.StopAsync(); await Session.StopAsync();
            if (guide != null) { guide.Activate(); return; }
            guide = new GuideWindow(this); guide.Closed += (_, _) => { guide = null; Notify(); }; guide.Show();
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
            catch (Exception e) { Error(new InvalidOperationException("快捷键注册失败（" + binding.Id + "）：" + e.Message)); }
        }
    }
    public void Run(Func<Task> action) => _ = RunSafe(action);
    public Task<bool> RequestMicrophoneAccessAsync() => runtime.RequestMicrophoneAccessAsync();
    public async Task StopAsync() { await Dictation.CancelAsync(); await Queue.PauseAsync(); await Live.StopAsync(); }
    private async Task RunSafe(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Error(e); } }
    public void Error(Exception e)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Error(e)); return; }
        var text = e is ApiException or InvalidOperationException or InvalidDataException or PlatformNotSupportedException ? e.Message : "操作失败（" + e.GetType().Name + "）。请检查网络、权限和数据目录。";
        var window = Ui.Window("DeskPet · 操作提示", Ui.Text(text), 460, 250); window.Show();
    }
    private void ShutdownRequested(object? sender,ShutdownRequestedEventArgs args) { if(exitPrepared)return;args.Cancel=true;if(!exiting)Run(ExitAsync); }
    public async Task ExitAsync()
    {
        if (exiting) return; exiting = true;
        try { await ImageTaskCenterState.BeginExitDrain(DrainApplicationForExitAsync,out applicationExitDrain); exitPrepared=true;lifetime.Shutdown(); }
        catch { exiting = false; throw; }
    }
    private async Task DrainApplicationForExitAsync()
    {
        await Task.WhenAll(Task.Run(()=>ImageTasks.StopAsync(CancellationToken.None)),Task.Run(()=>Session.StopAsync()),StopAsync());
        Save();await Session.FlushHistoryAsync();
    }
    public void Dispose()
    {
        lifetime.ShutdownRequested-=ShutdownRequested;imageNoticeTimer.Stop();imageNotification?.Close();ImageTasks.Changed-=ImageTaskCompleted;tray?.Dispose(); foreach (var binding in hotkeys) binding.Dispose();
        Queue.Dispose(); Dictation.Dispose(); Live.Dispose(); Session.Dispose(); Questions.Dispose(); ImageServicesShutdown = Task.Run(async () => { try{await applicationExitDrain;}catch(Exception){} await ImageTasks.StopAsync(CancellationToken.None); await Session.WaitForIdleAsync(); ImageClient.Dispose(); ImageStore.Dispose(); });
    }
}
