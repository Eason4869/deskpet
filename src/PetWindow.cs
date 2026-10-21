using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace Dafeiyu
{
    public sealed partial class PetWindow : Window
    {
        private SpriteAtlas atlas;
        private InteractionAtlas interactionAtlas;
        private readonly InteractionAnimation interaction=new InteractionAnimation();
        private readonly AdditionalInteractions additionalInteractions=new AdditionalInteractions();
        private DropReaction dropReaction;
        private bool dragDisplaced;
        private IdleSleepSchedule idleSleep;
        private int interactionRevision;
        private bool originalInteraction;
        private bool originalAction;
        private readonly DropMotion dropMotion=new DropMotion();
        public IHolidayHoverActionProvider HolidayHoverActions {get;set;}
        private readonly DispatcherTimer clickTimer;
        private bool pendingHeadClick;
        private string fallbackInteraction="";
        private readonly Image image;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch watch=Stopwatch.StartNew();
        private readonly EventWaitHandle wake;
        private readonly Random random=new Random();
        private readonly Forms.NotifyIcon tray;
        private readonly System.Drawing.Icon trayIcon;
        private readonly TrayContextMenu trayMenu;
        private SettingsPage preferences;
        private OnboardingWindow guide;
        string guideInteraction="";bool guidePaused,guideWheelOpened;
        GuideFocusScope guideMenuScope;
        internal event Action<string> GuideInteractionCompleted;
        bool GuideActive {get{return guide!=null && guide.IsVisible;}}
        internal void SetGuideInteraction(string action)
        {
            if(guideInteraction=="double-click" && quick!=null){quick.SetGuidePreview(false);quick.Hide();}
            if(guideInteraction.Length>0){if(ContextMenu!=null)ContextMenu.IsOpen=false;if(palette!=null)palette.Close();SetPaused(guidePaused);if(handle!=IntPtr.Zero)DesktopInterop.SetClickThrough(handle,Settings.ClickThrough || !petDisplayed);}
            guideInteraction=action??"";guideWheelOpened=false;
            if(guideInteraction.Length>0){guidePaused=Paused;SetPaused(false);RestorePet();if(handle!=IntPtr.Zero)DesktopInterop.SetClickThrough(handle,false);}
        }
        internal void NotifyGuideInteraction(string action)
        {
            if(!GuideActive)return;if(action=="wheel-open"){guideWheelOpened=true;return;}
            if(guideInteraction==action && (action!="wheel" || guideWheelOpened) && GuideInteractionCompleted!=null)GuideInteractionCompleted(action);
        }
        internal void SetGuideLock(bool active){trayMenu.Enabled=!active;if(active && quick!=null)quick.Hide();}
        internal bool GuideWorkspacePractice;
        internal bool GuideIsRunning {get{return GuideActive;}}
        bool ReturnToGuide(){if(!GuideActive||GuideWorkspacePractice)return false;if(guide.WindowState==WindowState.Minimized)guide.WindowState=WindowState.Normal;guide.Activate();return true;}

        private ActionPaletteWindow palette;
        private ChatWindow chat;
        private LiveCallView call;
        private ConversationSession conversation;
        private ConversationQueue inputQueue;private ProjectCatalog projectCatalog;private ProjectDefinition editingProject;private ProjectCatalogStore projectCatalogStore;private ConversationWorkspaceManager workspaces;private readonly HashSet<string> titleGenerationStarted=new HashSet<string>();private ConversationWorkspace quickWorkspace;internal event Action ProjectsChanged;
        private DictationSession dictation;
        public DictationSession Dictation{get{return dictation ?? (dictation=new DictationSession(new ApiClient(),()=>new AudioRecorder(),()=>Settings));}}
        public bool DictationActive{get{return dictation!=null && dictation.IsBusy;}}
        public ConversationQueue InputQueue
        {
            get
            {
                if(inputQueue==null){inputQueue=new ConversationQueue(Conversation,()=>Settings,()=>Skin.Name,Path.Combine(Store.DirectoryPath,"queued-messages.json"));inputQueue.Changed+=delegate{if(!closing)Dispatcher.BeginInvoke(new Action(ConversationChanged));};}
                return inputQueue;
            }
        }
        bool HasQueuedMessages {get{return ((inputQueue!=null || File.Exists(Path.Combine(Store.DirectoryPath,"queued-messages.json"))) && InputQueue.HasPending) || (workspaces!=null&&workspaces.OpenWorkspaces.Any(w=>w.Queue.HasPending));}}
        private QuickChatWindow quick;
        private GreetingBubbleWindow greeting;
        private GreetingSchedule greetingSchedule;
        private double lastGreetingProbe;
        private CancellationTokenSource greetingRequest;
        private int greetingGeneration;
        private bool greetingPreview;
        private CompanionMemory companionMemory;
        private readonly Dictionary<string,List<string>> recentGreetings=new Dictionary<string,List<string>>();
        internal GreetingService GreetingGenerator=new GreetingService();
        private bool sessionLocked;
        private UpdateCoordinator updates;
        private UpdateNoticeWindow updateNotice;
        public bool IsUpdating {get{return updates!=null && updates.IsApplying;}}
        IEnumerable<ConversationSession> ConversationSessions {get{if(conversation!=null)yield return conversation;if(workspaces!=null)foreach(var workspace in workspaces.OpenWorkspaces){var session=workspace.Session;if(session!=null&&!Object.ReferenceEquals(session,conversation))yield return session;}}}
        public bool HasHistorySaveWarning {get{return ConversationSessions.Any(session=>(Settings.SaveChatHistory&&!String.IsNullOrEmpty(session.HistoryWarning))||!String.IsNullOrEmpty(session.AgentTaskWarning));}}
        public string PendingSaveWarning {get{var session=ConversationSessions.FirstOrDefault(s=>!String.IsNullOrEmpty(s.AgentTaskWarning));return session!=null?session.AgentTaskWarning:"聊天记录尚未保存，请先导出或处理保存问题后再更新。";}}
        public UpdateCoordinator Updates {get{return updates ?? (updates=new UpdateCoordinator(this));}}
        public bool CanApplyUpdate(bool manual)
        {
            return !closing && (mcpConnections==null||!mcpConnections.Status.Any(s=>s.State=="connecting")) && !dragging && !DictationActive && (call==null || !call.IsVisible) && !LiveActive && !HasHistorySaveWarning && !HasQueuedMessages && (inputQueue==null || !inputQueue.IsRunning) && (conversation==null || !conversation.IsBusy) && (workspaces==null || !workspaces.Busy) && !sessionLocked && (ContextMenu==null || !ContextMenu.IsOpen) && (trayMenu==null || !trayMenu.Visible) &&
                (chat==null || (!chat.HasDraft && !chat.HasUnsavedMcpEdits && (manual || !chat.IsVisible))) && (quick==null || (!quick.HasDraft && (manual || !quick.IsVisible))) &&
                (preferences==null || (!preferences.HasUnsavedEdits && (manual || !preferences.IsVisible))) && (guide==null || !guide.IsVisible) && (palette==null || !palette.IsVisible) && !DesktopInterop.ForegroundIsFullScreen();
        }
        public async Task PrepareUpdateAsync()
        {
            if(conversation!=null)await conversation.FlushHistoryAsync();if(workspaces!=null)await workspaces.SaveAllAsync();
            if(HasHistorySaveWarning)throw new InvalidOperationException(PendingSaveWarning);
            SavePosition();
            try{Store.Save(Settings);}
            catch(Exception error){if(!(error is IOException || error is UnauthorizedAccessException))throw;throw new InvalidOperationException("设置尚未保存，请检查目录权限后重试更新。",error);}
        }
        private GeminiLiveSession live;
        private string liveSkinId;

        private readonly VisibilityPolicy visibility=new VisibilityPolicy();
        private HotkeyRegistry hotkeys;
        private MenuItem quietItem,clickThroughItem;
        private bool exitPrepared;
        public string HotkeyWarning {get;private set;}
        public bool UserVisible {get{return visibility.UserVisible;}}
        public bool AutoHidden {get{return visibility.AutoHidden;}}
        private bool petDisplayed=true;
        public bool IsPetVisible {get{return IsVisible && petDisplayed;}}
        private MenuItem visibilityItem;
        private IntPtr handle;
        private bool dragging,moved,closing,walking;
        private double manualActionUntil;
        private Point dragStart,windowStart,lastCursor;
        private double previousTick,lastCursorMove,nextWalk=90,walkTarget;
        private double nextRender;
        private readonly GazeController gaze=new GazeController();
        private readonly HoverInteraction hover=new HoverInteraction();
        private bool gazeActive;
        private int shownRow=-1,shownCol=-1;
        private bool shownMirrored;
        private bool shownSupplemental;
        private Point shownOffset;
        private SmoothMotion motion;
        private string motionSkin;
        private MotionClip shownMotion;
        private int shownSample=-1;
        private double shownWidth,shownHeight;
        public AnimationPlayer Animation { get; private set; }
        public PetSettings Settings { get; private set; }
        public SettingsStore Store { get; private set; }
        public SkinCatalog Catalog { get; private set; }
        public SkinDefinition Skin { get; private set; }
        public bool Paused { get; private set; }
        public bool IsSleeping {get{return interaction.IsSleeping;}}
        public string Notice { get; private set; }
        public ProjectDefinition CurrentEditingProject {get{return editingProject;}}
        public ProjectCatalog Projects
        {
            get
            {
                if(projectCatalog==null)
                {
                    projectCatalogStore=new ProjectCatalogStore(Path.Combine(Store.DirectoryPath,"projects.json"));
                    try{
                        projectCatalog=projectCatalogStore.Load();var upgraded=ProjectCatalogStore.UpgradeOwnership(projectCatalog,Store.DirectoryPath,Catalog.Skins.Select(s=>s.Id).Concat(Settings.PersonaOverrides.Keys).Concat(Settings.VoiceProfiles.Keys),Skin.Id);
                        if(!Object.ReferenceEquals(upgraded,projectCatalog)){projectCatalogStore.Save(upgraded);projectCatalog=upgraded;}
                    }catch(Exception){projectCatalogStore.PreserveAfterReadFailure();if(projectCatalog==null)projectCatalog=new ProjectCatalog();Notice="项目记录读取或归属升级未完成，原文件与加密历史已保留；请检查本地存储后重新打开。";}
                }
                return projectCatalog;
            }
        }
        public ProjectCatalogStore ProjectStore {get{if(projectCatalogStore==null){var ignored=Projects;}return projectCatalogStore;}}
        public ConversationWorkspaceManager Workspaces
        {
            get
            {
                if(workspaces==null){workspaces=new ConversationWorkspaceManager(Store.DirectoryPath,Skin.Id,delegate{return new ApiClient();},delegate{return new SpeechOutput(new ApiClient(),Path.Combine(Store.DirectoryPath,"audio-cache"),Dispatcher);},delegate{return new AudioRecorder();},delegate{return Settings;},delegate{return Settings.SaveChatHistory;});workspaces.ApprovalProvider=new AgentApprovalController(this);workspaces.QuestionProvider=QuestionController;workspaces.ImageStore=ImageStore;workspaces.ImageClient=ImageClient;workspaces.McpConnections=McpConnections;workspaces.StateChanged+=ConversationChanged;}
                return workspaces;
            }
        }
        public ConversationSession Conversation
        {
            get
            {
                if(conversation==null)
                {
                    var api=new ApiClient();var voice=new SpeechOutput(api,Path.Combine(Store.DirectoryPath,"audio-cache"),Dispatcher);
                    conversation=new ConversationSession(api,voice,delegate {return new AudioRecorder();},new HistoryStore(Path.Combine(Store.DirectoryPath,"history")),delegate {return Settings.SaveChatHistory;});conversation.ApprovalProvider=new AgentApprovalController(this);ConfigureImageServices(conversation);conversation.McpConnections=McpConnections;conversation.SelectPersona(Skin.Id);conversation.StateChanged+=ConversationChanged;conversation.HistoryChanged+=ConversationMemoryChanged;
                }
                return conversation;
            }
        }
        public bool LiveActive {get{return live!=null && live.IsActive;}}
        public bool DialogueBusy {get{return IsUpdating || LiveActive || (inputQueue!=null && inputQueue.IsRunning) || (conversation!=null && conversation.IsBusy) || (workspaces!=null && workspaces.Busy);}}
        public GeminiLiveSession Live
        {
            get
            {
                if(live==null)
                {
                    live=new GeminiLiveSession();live.StateChanged+=delegate {if(!closing)Dispatcher.BeginInvoke(new Action(ConversationChanged));};
                    live.TurnCommitted+=delegate(string user,string reply,string status){string id=liveSkinId;Action commit=delegate {if(!exitPrepared)Conversation.AddLiveTurn(id,user,reply,status);};if(Dispatcher.CheckAccess())commit();else if(!Dispatcher.HasShutdownStarted)Dispatcher.Invoke(commit);};
                }
                return live;
            }
        }
        public void ShowLiveCall(){ShowChat();ShowLiveCall(chat);}
        public void ShowLiveCall(Window origin)
        {
            if(closing || IsUpdating || ReturnToGuide())return;
            if(call!=null){call.Activate();return;}
            if(origin==null)return;
            if(DictationActive){Notice="请先结束或取消听写，再开始通话。";return;}
            if(DialogueBusy && !LiveActive){Notice="请先结束当前对话，再开始 Live。";return;}
            if(origin is ChatWindow && quick!=null)quick.Hide();
            CancelInteraction();call=new LiveCallView(this,origin);call.Closed+=delegate {call=null;};call.Show();
        }
        public async Task StartLiveAsync()
        {if(DialogueBusy)throw new InvalidOperationException("请先停止当前对话再开始 Live。");liveSkinId=Skin.Id;await Live.StartAsync(Settings,Settings.SystemPrompt.Replace("{pet_name}",Skin.Name));}
        public async Task StopDialogueAsync(){if(inputQueue!=null)await inputQueue.PauseAsync();if(live!=null)await live.StopAsync();if(conversation!=null)await conversation.StopAsync();}

        public PetWindow(SpriteAtlas source,PetSettings settings,SettingsStore store,EventWaitHandle wakeSignal)
        {
            atlas=source; Settings=settings; Store=store; wake=wakeSignal; Settings.Normalize();ThemeManager.Apply(Settings.ThemeMode);ManagedSkillLibrary.ActiveDirectory=System.IO.Path.Combine(Store.DirectoryPath,"skills");InitializeUsageTracking();
            idleSleep=new IdleSleepSchedule(watch.Elapsed.TotalSeconds,random.NextDouble);
            nextWalk=WanderSchedule.Next(watch.Elapsed.TotalSeconds,random.NextDouble());
            Catalog=new SkinCatalog(store.DirectoryPath); Skin=Catalog.Find(Settings.SkinId);
            try { atlas=Skin.Load(); }
            catch { atlas=source ?? new SpriteAtlas(); Skin=Catalog.Find("dafeiyu"); Notice="之前导入的外观无法读取，已切回大肥鱼。"; }
            interactionAtlas=InteractionAtlas.LoadForSkin(Skin);interaction.Configure(Skin);
            PersonaProfiles.Activate(Settings,Skin); Animation=Skin.CreateAnimation(atlas.FrameMultiplier);
            ResetGreetingSchedule();
            Microsoft.Win32.SystemEvents.SessionSwitch+=SessionChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged+=PowerChanged;
            Title="桌面伙伴 · "+Skin.Name;Icon=Ui.ApplicationIcon(); AllowsTransparency=true; WindowStyle=WindowStyle.None;
            ResizeMode=ResizeMode.NoResize; Background=Brushes.Transparent; ShowInTaskbar=false;
            ShowActivated=false; Focusable=false; Topmost=Settings.Topmost;
            Width=192*Settings.Scale; Height=208*Settings.Scale;
            image=new Image { Stretch=Stretch.Fill,Cursor=Cursors.Hand };
            clickTimer=new DispatcherTimer(DispatcherPriority.Input) {Interval=TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime)};
            clickTimer.Tick+=delegate {clickTimer.Stop();if(!closing && IsPetVisible && !Paused && !dragging && !IsUpdating){PlayInteraction(pendingHeadClick ? "petting" : "poked");NotifyGuideInteraction("click");}};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality); Content=image;
            image.MouseLeftButtonDown+=BeginDrag; image.MouseMove+=Drag; image.MouseLeftButtonUp+=EndDrag;
            image.PreviewMouseDown+=delegate(object sender,MouseButtonEventArgs args){if(args.ChangedButton==MouseButton.Middle){args.Handled=true;if(!dragging)ShowActionPalette();}};
            image.LostMouseCapture+=delegate { if(dragging) { dragging=false; walking=false; CancelInteraction(); SavePosition(); UpdateFrame(); } };
            ContextMenu=CreateMenu();
            ContextMenuOpening+=delegate(object sender,ContextMenuEventArgs args)
            {
                if(!GuideActive)return;
                if(guideInteraction!="menu"){args.Handled=true;return;}
                // Establish the guide's foreground ownership before WPF captures
                // the mouse. Activating either window after opening drops capture.
                guide.Activate();
            };
            ContextMenu.Opened+=delegate{if(GuideActive){guideMenuScope=GuideFocusScope.BlockMenuActions(ContextMenu);NotifyGuideInteraction("menu");}};
            ContextMenu.Closed+=delegate{if(guideMenuScope!=null){guideMenuScope.Dispose();guideMenuScope=null;}};
            LocationChanged+=delegate {if(quick!=null && quick.IsVisible)quick.FollowPet();if(greeting!=null && greeting.IsVisible)greeting.FollowPet();if(updateNotice!=null && updateNotice.IsVisible)updateNotice.FollowPet();};
            SourceInitialized+=delegate
            {
                handle=new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(handle).AddHook(WindowMessage);
                DesktopInterop.MakeToolWindow(handle);DesktopInterop.SetClickThrough(handle,Settings.ClickThrough || !petDisplayed);
                hotkeys=new HotkeyRegistry(handle);
                foreach(var pair in Settings.Hotkeys.Where(p=>ShortcutCatalog.All.Any(d=>d.Global&&d.Id==p.Key))){string error;if(!hotkeys.TryBind(pair.Key,pair.Value,out error))HotkeyWarning=Notice=error+"（"+pair.Value+"）";}
            };
            Loaded+=delegate
            {
                if(Settings.X.HasValue && Settings.Y.HasValue) DesktopInterop.Move(handle,new Point(Settings.X.Value,Settings.Y.Value));
                else ReturnHome();
                DesktopInterop.ClampWindow(handle); lastCursor=DesktopInterop.Cursor(); previousTick=watch.Elapsed.TotalSeconds;
                UpdateFrame(); timer.Start();
            };
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Dafeiyu.Icon")) trayIcon=(System.Drawing.Icon)new System.Drawing.Icon(stream).Clone();
            trayMenu=new TrayContextMenu();
            visibilityItem=new MenuItem{Header="显示 / 隐藏伙伴",Name="TrayVisibility",Icon=UiIcons.Image("pet-visible",Ui.Muted),IsCheckable=true,IsChecked=true};visibilityItem.Click+=delegate {Dispatcher.BeginInvoke(new Action(delegate {if(visibility.UserVisible)HidePet();else RestorePet();}));};trayMenu.Items.Add(visibilityItem);
            quietItem=new MenuItem{Header="免打扰",Name="TrayQuietMode",Icon=UiIcons.Image("quiet",Ui.Muted),IsCheckable=true,IsChecked=Settings.QuietMode};quietItem.Click+=delegate {Dispatcher.BeginInvoke(new Action(delegate {SetQuietMode(!Settings.QuietMode);}));};trayMenu.Items.Add(quietItem);
            clickThroughItem=new MenuItem{Header="鼠标穿透",Name="TrayClickThrough",Icon=UiIcons.Image("pointer",Ui.Muted),IsCheckable=true,IsChecked=Settings.ClickThrough};clickThroughItem.Click+=delegate {Dispatcher.BeginInvoke(new Action(delegate {SetClickThrough(!Settings.ClickThrough);}));};trayMenu.Items.Add(clickThroughItem);
            trayMenu.Opening+=delegate {visibilityItem.IsChecked=visibility.UserVisible;quietItem.IsChecked=Settings.QuietMode;clickThroughItem.IsChecked=Settings.ClickThrough;};
            AddTray("快速对话",ShowQuickChat,"TrayQuickChat","chat");AddTray("打开完整对话",ShowChat,"TrayFullChat","full-view");AddTray("设置",ShowSettings,"TraySettings","settings");
            AddTray("回到屏幕角落",ReturnHome,"TrayReturnHome","back");
            trayMenu.Items.Add(new Separator());AddTray("退出桌宠",Close,"TrayExit","close");
            tray=new Forms.NotifyIcon { Icon=trayIcon,Text="桌面伙伴 · "+Skin.Name,Visible=true };
            tray.MouseUp+=delegate(object sender,Forms.MouseEventArgs args){if(args.Button==Forms.MouseButtons.Right)Dispatcher.BeginInvoke(new Action(ShowTrayMenu));};
            tray.MouseDoubleClick+=delegate(object sender,Forms.MouseEventArgs args){if(args.Button==Forms.MouseButtons.Left)HandleTrayDoubleClick();};
            timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(250) };
            timer.Tick+=Tick; Closing+=OnClosing; UpdateFrame();
            IsVisibleChanged+=delegate { RefreshRendering(); };
        }
        private void RefreshRendering()
        {
            CompositionTarget.Rendering-=RenderFrame;
            if(IsPetVisible && !closing) { previousTick=watch.Elapsed.TotalSeconds;nextRender=previousTick;CompositionTarget.Rendering+=RenderFrame; }
            else CancelInteraction();
        }
        private void AddTray(string text,Action action,string name,string icon)
        {var item=new MenuItem{Header=text,Name=name,Icon=UiIcons.Image(icon,Ui.Muted)};item.Click+=delegate {Dispatcher.BeginInvoke(action);};trayMenu.Items.Add(item);}
        internal void ShowTrayMenu(){if(!closing&&trayMenu.Enabled)trayMenu.ShowMenu();}
        internal void HandleTrayDoubleClick()
        {Dispatcher.BeginInvoke(new Action(delegate{if(closing||IsUpdating||!trayMenu.Enabled||ReturnToGuide())return;RestorePet();ShowChat();}));}
        private ContextMenu CreateMenu()
        {
            var menu=ChatUi.Menu();
            // When a settings/chat/guide window already owns foreground focus,
            // activating the pet-owned popup makes Windows restore that window
            // and WPF drops mouse capture. Keep the existing foreground owner;
            // normal popup capture still dismisses the menu on an outside click.
            menu.StaysOpen=false;menu.Opened+=delegate {Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate {if(menu.IsOpen && !GuideActive && !DesktopInterop.ForegroundBelongsToPet())DesktopInterop.ForegroundMenu(menu);}));};
            AddMenu(menu,"快速对话…",ShowQuickChat);AddMenu(menu,"打开完整对话…",ShowChat); var appearance=new MenuItem{Header="外观",Name="QuickAppearanceMenu"};menu.Items.Add(appearance);
            menu.Opened+=delegate{RefreshAppearanceMenu(appearance);};RefreshAppearanceMenu(appearance);
            AddMenu(menu,"设置",ShowSettings); menu.Items.Add(new Separator());
            var actions=new MenuItem { Header="做个动作" };
            for(int i=0;i<AnimationPlayer.States.Length;i++)
            {
                string state=AnimationPlayer.States[i]; var item=new MenuItem { Header=AnimationPlayer.Labels[i] };
                item.Click+=delegate { PlayAction(state,state=="jumping" ? .7 : 2); }; actions.Items.Add(item);
            }
            menu.Items.Add(actions);
            var interactions=new MenuItem {Header="互动"};
            AddInteractionMenu(interactions,"摸摸头","petting");AddInteractionMenu(interactions,"戳一戳","poked");AddInteractionMenu(interactions,"喂零食","feeding");
            var sleep=new MenuItem {Header="小憩"};sleep.Click+=delegate {PlayInteraction(interaction.IsSleeping ? "wake" : "sleeping");};interactions.Items.Add(sleep);
            var firstSkill=new MenuItem{Header=Skin.SpecialAction};firstSkill.Click+=delegate{PlayInteraction("special");};interactions.Items.Add(firstSkill);
            var extraSkill=new MenuItem();extraSkill.Click+=delegate{PlayInteraction("special2");};interactions.Items.Add(extraSkill);menu.Items.Add(interactions);
            menu.Opened+=delegate{RecordPetInteraction();firstSkill.Header=Skin.SpecialAction;extraSkill.Header=AdditionalInteractions.SpecialName(Skin);extraSkill.Visibility=AdditionalInteractions.Has(Skin,"special2")?Visibility.Visible:Visibility.Collapsed;};
            menu.Opened+=delegate {CancelPendingClick();sleep.Header=interaction.IsSleeping ? "叫醒" : "小憩";interactions.IsEnabled=!IsUpdating;};
            var pause=new MenuItem { Header="暂停动画",IsCheckable=true };
            pause.Click+=delegate { SetPaused(pause.IsChecked); }; menu.Items.Add(pause);
            var follow=new MenuItem { Header="视线跟随鼠标",IsCheckable=true };
            follow.Click+=delegate { Settings.FollowCursor=follow.IsChecked; ApplySettings(); }; menu.Items.Add(follow);
            var wander=new MenuItem { Header="偶尔散步",IsCheckable=true };
            wander.Click+=delegate { Settings.Wander=wander.IsChecked; ApplySettings(); }; menu.Items.Add(wander);
            var top=new MenuItem { Header="保持置顶",IsCheckable=true };
            top.Click+=delegate { Settings.Topmost=top.IsChecked; ApplySettings(); }; menu.Items.Add(top);
            menu.Opened+=delegate { pause.IsChecked=Paused; follow.IsChecked=Settings.FollowCursor; wander.IsChecked=Settings.Wander; top.IsChecked=Settings.Topmost; };
            menu.Items.Add(new Separator()); AddMenu(menu,"回到屏幕角落",ReturnHome); AddMenu(menu,"隐藏到托盘",HidePet); AddMenu(menu,"退出桌宠",Close);
            return menu;
        }
        private void RefreshAppearanceMenu(MenuItem menu)
        {
            menu.Items.Clear();
            foreach(var skin in Catalog.Skins)
            {
                SkinDefinition selected=skin;
                var item=new MenuItem{Header=skin.Name,IsCheckable=true,IsChecked=skin.Id==Skin.Id,IsEnabled=!DialogueBusy && (preferences==null || !preferences.HasUnsavedEdits),Tag=skin.Id};
                item.Icon=new Image{Source=SkinCatalog.GetAvatar(skin),Width=28,Height=28,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,28,28),5.6,5.6)};
                item.Click+=delegate{SelectSkin(selected);foreach(MenuItem entry in menu.Items)entry.IsChecked=(string)entry.Tag==Skin.Id;};menu.Items.Add(item);
            }
        }
        private static void AddMenu(ContextMenu menu,string text,Action action)
        { var item=new MenuItem { Header=text }; item.Click+=delegate { action(); }; menu.Items.Add(item); }
        private void AddInteractionMenu(MenuItem menu,string text,string action)
        {var item=new MenuItem {Header=text};item.Click+=delegate {PlayInteraction(action);};menu.Items.Add(item);}
        private void Tick(object sender,EventArgs args)
        {
            if(closing) return;
            if(wake!=null && wake.WaitOne(0)) { RestorePet(); ShowSettings(); }
            double now=watch.Elapsed.TotalSeconds;
            PollVisibility();
            ConversationChanged();
            if(now-lastGreetingProbe>=1)
            {
                lastGreetingProbe=now;
                ProbeGreetings(DateTime.UtcNow,DesktopInterop.ForegroundIsFullScreen(handle));
                ProbeAutoSleep(now,DesktopInterop.UserIdleSeconds());
            }
        }
        private void ProbeGreetings(DateTime now,bool fullscreen)
        {
            bool allowed=GreetingAllowed(fullscreen,false);
            if(greetingRequest!=null && !GreetingAllowed(fullscreen,greetingPreview))CancelPendingGreeting();
            if(fullscreen && greeting!=null && greeting.IsVisible)greeting.Hide();
            if(greetingSchedule.TryTake(now,allowed && greetingRequest==null))ShowGreeting();
        }
        private void RecordPetInteraction(){if(idleSleep!=null)idleSleep.Touch(watch.Elapsed.TotalSeconds);}
        internal bool ProbeAutoSleep(double now,double systemIdle)
        {
            bool allowed=!closing && IsPetVisible && !Paused && !dragging && !walking && !interaction.Active && Animation.State=="idle" &&
                !DialogueBusy && !DictationActive && !HasQueuedMessages && !GuideActive && !sessionLocked &&
                !clickTimer.IsEnabled && (ContextMenu==null || !ContextMenu.IsOpen) && !trayMenu.Visible && (palette==null || !palette.IsVisible) &&
                !WindowInUse(quick) && !WindowInUse(chat) && !WindowInUse(preferences==null?null:preferences.HostWindow) && (updates==null || !updates.HasRestartCountdown) && greetingRequest==null;
            if(!idleSleep.TrySleep(now,systemIdle,allowed,Settings.AutoSleepEnabled))return false;
            StartInteraction("sleeping");return true;
        }
        private bool GreetingAllowed(bool fullscreen,bool preview)
        {
            return !closing && !Settings.QuietMode && (preview || Settings.GreetingEnabled) && IsPetVisible && !Paused && !dragging && !walking && !interaction.Active && !clickTimer.IsEnabled && !sessionLocked && !DialogueBusy && !DictationActive && !HasQueuedMessages && (!GuideActive || (preview && guide.TargetName=="GreetingEnabled")) && (updates==null || !updates.HasRestartCountdown) &&
                (ContextMenu==null || !ContextMenu.IsOpen) && !trayMenu.Visible && (palette==null || !palette.IsVisible) &&
                !WindowInUse(quick) && (!WindowInUse(chat) || (preview && preferences!=null && Object.ReferenceEquals(preferences.HostWindow,chat))) && (preview || !WindowInUse(preferences==null?null:preferences.HostWindow)) && !fullscreen;
        }
        private static bool WindowInUse(Window window){return window!=null && window.IsVisible && window.WindowState!=WindowState.Minimized;}
        private void RenderFrame(object sender,EventArgs args)
        {
            if(closing || !IsPetVisible) return;
            double now=watch.Elapsed.TotalSeconds;
            if(now+.002<nextRender) return;
            // Fixed deadlines avoid falling to 48 Hz on a 144 Hz monitor.
            nextRender+=(Math.Floor(Math.Max(0,now-nextRender)*60)+1)/60;
            double delta=Math.Min(.2,Math.Max(0,now-previousTick)); previousTick=now;
            if(Paused) return;
            if(quick!=null && quick.IsVisible)quick.FollowPet();
            Point cursor=DesktopInterop.Cursor(); if((cursor-lastCursor).Length>2) { lastCursor=cursor; lastCursorMove=now; }
            int beforeHover=interactionRevision;
            UpdateHover(cursor);
            bool manuallyRunning=AdvanceManualRun(delta);
            if(!manuallyRunning && !dragging && !interaction.Active && guideInteraction.Length==0) Wander(delta,now,cursor);
            if(dropMotion.Active)
            {
                dropMotion.Advance(Math.Min(delta,.125));var dropBounds=DesktopInterop.Bounds(handle);
                DesktopInterop.Move(handle,new Point(dropBounds.Left,dropMotion.Position));
                if(!dropMotion.Active)SavePosition();
            }
            bool interacting=interaction.Active;
            // New eight-pose actions must show every pose even if a UI frame is late.
            interaction.Advance(beforeHover!=interactionRevision?0:AdditionalInteractions.Has(Skin,interaction.Name)?Math.Min(delta,.125):delta);
            if(interacting && !interaction.Active){fallbackInteraction="";Animation.Play("idle",0);ConversationChanged();nextWalk=WanderSchedule.Next(now,random.NextDouble());}
            Animation.Advance(beforeHover!=interactionRevision?0:AdditionalInteractions.Has(Skin,Animation.State)?Math.Min(delta,.125):delta); UpdateFrame(delta);
        }
        internal void UpdateHover(Point cursor)
        {
            if(handle==IntPtr.Zero || shownRow<0 || shownCol<0)return;
            Rect bounds=DesktopInterop.Bounds(handle);bool inside=bounds.Contains(cursor),solid=false;
            if(inside && bounds.Width>0 && bounds.Height>0)
            {
                int width=shownMotion!=null?shownMotion.Width:shownSupplemental?interactionAtlas.FrameWidth:atlas.CellWidth,height=shownMotion!=null?shownMotion.Height:shownSupplemental?interactionAtlas.FrameHeight:atlas.CellHeight;
                int x=(int)Math.Floor((cursor.X-bounds.Left)/bounds.Width*width-shownOffset.X*width/480),y=(int)Math.Floor((cursor.Y-bounds.Top)/bounds.Height*height-shownOffset.Y*height/520);if(shownMirrored)x=width-1-x;
                solid=(shownMotion!=null?shownMotion.AlphaAt(shownSample,x,y):shownSupplemental?interactionAtlas.AlphaAt(shownRow,shownCol,x,y):atlas.AlphaAt(shownRow,shownCol,x,y))>=24;
            }
            bool allowed=!Paused && !dragging && !walking && !interaction.Active && !IsUpdating && !GuideActive && !Settings.ClickThrough && !clickTimer.IsEnabled && (Animation.State=="idle" || DialogueBusy) && (ContextMenu==null || !ContextMenu.IsOpen) && (palette==null || !palette.IsVisible);
            if(hover.Update(solid && DesktopInterop.IsWindowAtPoint(handle,cursor),inside,allowed))
            {
                var choices=HoverActions.Choices(Skin,HolidayHoverActions,DateTime.Now);
                choices[random.Next(choices.Count)].Play(this);nextWalk=WanderSchedule.Next(watch.Elapsed.TotalSeconds,random.NextDouble());
            }
        }
        private void Wander(double delta,double now,Point cursor)
        {
            if(interaction.Active)return;
            if(Settings.QuietMode || Settings.LockPosition){if(walking){walking=false;Animation.Play("idle",0);SavePosition();}return;}
            if(DialogueBusy || (quick!=null && quick.IsVisible)) return;
            if(!Settings.Wander && walking) { walking=false; Animation.Play("idle",0); SavePosition(); }
            if(walking)
            {
                Rect bounds=DesktopInterop.Bounds(handle),area=DesktopInterop.WorkArea(handle);
                walkTarget=PetMath.ClampPosition(walkTarget,bounds.Top,bounds.Width,bounds.Height,area).X;
                double dx=walkTarget-bounds.Left;
                double step=70*delta*Settings.Scale;
                if(Math.Abs(dx)<=step) { DesktopInterop.Move(handle,new Point(walkTarget,bounds.Top)); walking=false; Animation.Play("idle",0); SavePosition(); nextWalk=WanderSchedule.Next(now,random.NextDouble()); }
                else DesktopInterop.Move(handle,new Point(bounds.Left+Math.Sign(dx)*step,bounds.Top));
                return;
            }
            if(Settings.Wander && Animation.State=="idle" && now>=nextWalk && !IsMouseOver && (preferences==null || !preferences.IsVisible))
            {
                Rect bounds=DesktopInterop.Bounds(handle),area=DesktopInterop.WorkArea(handle);
                double desired=bounds.Left+(random.Next(2)==0 ? -1 : 1)*(80+random.Next(120));
                walkTarget=PetMath.ClampPosition(desired,bounds.Top,bounds.Width,bounds.Height,area).X;
                if(Math.Abs(walkTarget-bounds.Left)>10) {DismissGreeting();walking=true; Animation.Play(walkTarget>bounds.Left ? "running-right" : "running-left",0); }
                nextWalk=WanderSchedule.Next(now,random.NextDouble());
            }
        }
        private void UpdateFrame(double delta=0)
        {
            var extra=additionalInteractions.Get(Skin,interaction.Active?interaction.Name+(originalInteraction?"-original":""):Animation.State+(originalAction && AdditionalInteractions.HasOriginal(Skin,Animation.State)?"-original":""));
            if(extra!=null)
            {
                int sample=extra.Sample(interaction.Active?interaction.Elapsed:Animation.Elapsed);
                if(shownMotion==extra && shownSample==sample && shownWidth==Width && shownHeight==Height)return;
                shownMotion=extra;shownSample=sample;shownRow=interaction.Active?interaction.Row:Animation.Row;shownCol=sample;shownSupplemental=interaction.Active;
                shownMirrored=false;shownOffset=new Point();shownWidth=Width;shownHeight=Height;gazeActive=false;
                image.RenderTransform=Transform.Identity;image.Source=extra.Frame(sample);return;
            }
            bool supplemental=interaction.Active && (interactionAtlas!=null || Skin.Illustrated);
            if(interaction.Active && !supplemental && fallbackInteraction!=interaction.Name)
            {
                fallbackInteraction=interaction.Name;
                string fallback=fallbackInteraction=="poked" ? "failed" : fallbackInteraction=="happy" || fallbackInteraction=="special" || fallbackInteraction=="landing" ? "jumping" : fallbackInteraction=="lifted" || fallbackInteraction=="sleeping" ? "waiting" : "waving";
                Animation.Play(fallback,0);
            }
            int row=Animation.Row,col=Animation.Frame;
            if(supplemental){row=interaction.Row;col=interaction.Frame;gazeActive=false;}
            else if(!interaction.Active && !Paused && !dragging && !walking && !hover.Active && Settings.FollowCursor && Animation.State=="idle" && handle!=IntPtr.Zero && watch.Elapsed.TotalSeconds-lastCursorMove<1.4)
            {
                Rect bounds=DesktopInterop.Bounds(handle); Point cursor=DesktopInterop.Cursor();
                double dx=cursor.X-(bounds.X+bounds.Width*.5),dy=cursor.Y-(bounds.Y+bounds.Height*.4);
                if(!gazeActive) { gaze.Reset(8); gazeActive=true; }
                int exact=PetMath.LookDirection(dx,dy);if(exact>=0)gaze.Reset(exact);
                int direction=gaze.Frame(atlas.FrameMultiplier),columns=8*atlas.FrameMultiplier;
                row=9+direction/columns; col=direction%columns;
            }
            else gazeActive=false;
            // Built-in companions use the original v0.5.2 atlases and cadence.
            // Imported illustrated packs retain their own manifest frame timing.
            if(Skin.Illustrated && row<9) {
                if(motion==null || motionSkin!=Skin.Id){motion=new SmoothMotion(Skin);motionSkin=Skin.Id;}
                var clip=motion.Get(supplemental?11+row:row);
                if(clip!=null) {
                    int sample=clip.Sample(supplemental?interaction.Elapsed:Animation.Elapsed);
                    if(shownMotion==clip && shownSample==sample && shownWidth==Width && shownHeight==Height)return;
                    shownMotion=clip;shownSample=sample;shownRow=row;shownCol=col;shownSupplemental=supplemental;
                    shownMirrored=false;shownOffset=new Point();shownWidth=Width;shownHeight=Height;
                    image.RenderTransform=Transform.Identity;image.Source=clip.Frame(sample);return;
                }
            }
            bool wasMotion=shownMotion!=null;shownMotion=null;shownSample=-1;
            bool mirrored=!supplemental && Skin.MirrorsAction(row);
            if(!supplemental){col=Skin.ActionFrame(row,col);row=Skin.ActionRow(row);}
            if(!wasMotion && row==shownRow && col==shownCol && mirrored==shownMirrored && supplemental==shownSupplemental && shownWidth==Width && shownHeight==Height) return;
            shownRow=row; shownCol=col;shownMirrored=mirrored;shownSupplemental=supplemental;shownWidth=Width;shownHeight=Height;
            shownOffset=!supplemental && Skin.Embedded ? SpriteAlignment.Offset(Skin.Id,row,col,mirrored) : new Point();
            image.RenderTransformOrigin=new Point(.5,.5);
            if(!mirrored && shownOffset.X==0 && shownOffset.Y==0)image.RenderTransform=Transform.Identity;
            else
            {
                var transform=new TransformGroup();
                if(mirrored)transform.Children.Add(new ScaleTransform(-1,1));
                if(shownOffset.X!=0 || shownOffset.Y!=0)transform.Children.Add(new TranslateTransform(shownOffset.X*Width/480,shownOffset.Y*Height/520));
                transform.Freeze();image.RenderTransform=transform;
            }
            image.Source=supplemental ? interactionAtlas.GetFrame(row,col) : atlas.GetFrame(row,col);
        }
        private void BeginDrag(object sender,MouseButtonEventArgs args)
        {
            if(closing || IsUpdating || (GuideActive && guideInteraction!="drag" && guideInteraction!="click" && guideInteraction!="double-click")){args.Handled=true;return;}
            if(args.ClickCount==2) {CancelInteraction();UpdateFrame();ShowQuickChat();if(quick!=null && quick.IsVisible && call==null)NotifyGuideInteraction("double-click");args.Handled=true;return;}
            if(dropMotion.Active){dropMotion.Cancel();SavePosition();}
            RecordPetInteraction();
            CancelPendingClick();DismissGreeting();
            StopManualRun();walking=false; dragStart=DesktopInterop.Cursor(); Rect bounds=DesktopInterop.Bounds(handle); windowStart=bounds.TopLeft;
            pendingHeadClick=dragStart.Y-bounds.Top<bounds.Height*.55;
            moved=false;dragDisplaced=false;dragging=true;image.CaptureMouse();args.Handled=true;
        }
        private void Drag(object sender,MouseEventArgs args)
        {
            if(closing || IsUpdating || (GuideActive && guideInteraction!="drag") || (Settings.LockPosition && guideInteraction!="drag") || !dragging || args.LeftButton!=MouseButtonState.Pressed) return;
            Point cursor=DesktopInterop.Cursor(); Vector delta=cursor-dragStart;
            if(!moved && Math.Abs(delta.X)<Forms.SystemInformation.DragSize.Width/2d && Math.Abs(delta.Y)<Forms.SystemInformation.DragSize.Height/2d) return;
            if(!moved){moved=true;if(!Paused)StartInteraction("lifted");}
            Rect bounds=DesktopInterop.Bounds(handle); Rect area=DesktopInterop.WorkArea(cursor);
            DesktopInterop.Move(handle,PetMath.ClampPosition(windowStart.X+delta.X,windowStart.Y+delta.Y,bounds.Width,bounds.Height,area));
            if((DesktopInterop.Bounds(handle).TopLeft-bounds.TopLeft).Length>0)dragDisplaced=true;
            if(quick!=null && quick.IsVisible)quick.FollowPet();
        }
        private void EndDrag(object sender,MouseButtonEventArgs args)
        {
            if(!dragging) return; bool originalDrop=originalInteraction;dragging=false; image.ReleaseMouseCapture(); SavePosition();
            if(moved)
            {
                if(dropReaction==null)dropReaction=new DropReaction(random.NextDouble);
                bool stumble=dropReaction.TryStumble(dragDisplaced,!Paused && Settings.StumbleEnabled && !IsUpdating && !GuideActive,AdditionalInteractions.Supports(Skin),watch.Elapsed.TotalSeconds);
                if(!Paused)
                {
                    PlayInteraction(stumble?"stumble":"landing",originalDrop);
                    var dropBounds=DesktopInterop.Bounds(handle);var dropArea=DesktopInterop.WorkArea(handle);
                    dropMotion.Start(dropBounds.Top,dropBounds.Height,dropArea.Bottom);
                }
                if((DesktopInterop.Bounds(handle).TopLeft-windowStart).Length>2)NotifyGuideInteraction("drag");
            }
            else if(!closing && !Paused && !IsUpdating && (!GuideActive || guideInteraction=="click")){clickTimer.Interval=TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime);clickTimer.Start();}
            args.Handled=true;
        }
        private IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(message==0x312 && hotkeys!=null){string action=hotkeys.ActionForId(wParam.ToInt32());if(action!=null){handled=true;Dispatcher.BeginInvoke(new Action(delegate {if(closing || IsUpdating || ReturnToGuide())return;if(action=="quick")ShowQuickChat();else if(action=="hide"){if(visibility.UserVisible)HidePet();else RestorePet();}else if(action=="quiet")SetQuietMode(!Settings.QuietMode);else if(action=="passthrough")SetClickThrough(!Settings.ClickThrough);}));return IntPtr.Zero;}}
            if(message==0x21) { handled=true; return new IntPtr(3); }
            if(message==0x84 && !dragging)
            {
                if(!IsPetVisible){handled=true;return new IntPtr(-1);}
                long p=lParam.ToInt64(); int x=(short)(p&0xffff),y=(short)((p>>16)&0xffff); Rect b=DesktopInterop.Bounds(hwnd);
                if(b.Width<=0 || b.Height<=0)return IntPtr.Zero;
                int width=shownMotion!=null?shownMotion.Width:shownSupplemental ? interactionAtlas.FrameWidth : atlas.CellWidth,height=shownMotion!=null?shownMotion.Height:shownSupplemental ? interactionAtlas.FrameHeight : atlas.CellHeight;
                int sx=(int)Math.Floor((x-b.Left)/b.Width*width-shownOffset.X*width/480),sy=(int)Math.Floor((y-b.Top)/b.Height*height-shownOffset.Y*height/520);
                if(shownMirrored)sx=width-1-sx;
                byte alpha=shownMotion!=null?shownMotion.AlphaAt(shownSample,sx,sy):shownSupplemental ? interactionAtlas.AlphaAt(Math.Max(0,shownRow),Math.Max(0,shownCol),sx,sy) : atlas.AlphaAt(Math.Max(0,shownRow),Math.Max(0,shownCol),sx,sy);
                if(alpha<12) { handled=true; return new IntPtr(-1); }
            }
            if(message==0x7e || message==0x2e0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate { if(!closing) DesktopInterop.ClampWindow(handle); }));
            return IntPtr.Zero;
        }
        public void PlayAction(string state,double duration)
        { if(closing || IsUpdating)return;RecordPetInteraction();CancelInteraction();originalAction=AdditionalInteractions.HasOriginal(Skin,state) && random.Next(2)==0;manualActionUntil=watch.Elapsed.TotalSeconds+Math.Max(3,duration);interactionRevision++;walking=false; Paused=false;nextWalk=WanderSchedule.Next(watch.Elapsed.TotalSeconds,random.NextDouble()); Animation.Play(state,duration);StartManualRun(state,duration);previousTick=watch.Elapsed.TotalSeconds; UpdateFrame(); if(preferences!=null) preferences.RefreshState(); }
        public void PlayInteraction(string name)
        { PlayInteraction(name,null); }
        private void PlayInteraction(string name,bool? originalStyle)
        {
            if(closing || IsUpdating)return;
            RecordPetInteraction();
            if((name=="stumble" || name=="special2") && !AdditionalInteractions.Has(Skin,name)){if(name=="special2")return;name="landing";}
            CancelInteraction();StartInteraction(name,originalStyle);
        }
        public ImageSource GetActionPreview(string action,bool isInteraction)
        {
            var extra=additionalInteractions.Get(Skin,action+(isInteraction && interaction.Name==action && originalInteraction?"-original":""));
            if(extra!=null)return extra.Frame(3);
            int motionRow=Array.IndexOf(isInteraction?new[]{"lifted","landing","petting","poked","feeding","happy","sleeping","special"}:AnimationPlayer.States,action);
            if(motionRow>=0 && Skin.Illustrated) {
                if(motion==null || motionSkin!=Skin.Id){motion=new SmoothMotion(Skin);motionSkin=Skin.Id;}
                var clip=motion.Get(isInteraction?11+motionRow:motionRow);
                if(clip!=null)return clip.Frame(clip.Count/2);
            }
            if(isInteraction)
            {
                int row=Array.IndexOf(new[]{"lifted","landing","petting","poked","feeding","happy","sleeping","special"},action);
                if(row<0)return null;
                if(interactionAtlas!=null)return interactionAtlas.GetFrame(row,1);
                action=action=="poked"?"failed":action=="happy" || action=="special" || action=="landing"?"jumping":action=="lifted" || action=="sleeping"?"waiting":"waving";
            }
            int baseRow=Array.IndexOf(AnimationPlayer.States,action);if(baseRow<0)return null;
            int col=Skin.ActionFrame(baseRow,baseRow==1 || baseRow==2 || baseRow==4?2:0);bool mirrored=Skin.MirrorsAction(baseRow);baseRow=Skin.ActionRow(baseRow);
            BitmapSource frame=atlas.GetFrame(baseRow,col);
            if(mirrored){var flipped=new TransformedBitmap(frame,new ScaleTransform(-1,1));flipped.Freeze();return flipped;}
            return frame;
        }
        public void ShowActionPalette()
        {
            if(closing || IsUpdating || dragging || (GuideActive && guideInteraction!="wheel"))return;CancelPendingClick();DismissGreeting();
            RecordPetInteraction();
            if(palette==null){palette=new ActionPaletteWindow(this);palette.Closed+=delegate {palette=null;};}
            palette.ShowForPet();NotifyGuideInteraction("wheel-open");
        }
        private void StartInteraction(string name,bool? originalStyle=null)
        {
            interactionRevision++;
            originalInteraction=AdditionalInteractions.HasOriginal(Skin,name) && (originalStyle??random.Next(2)==0);
            interaction.Configure(Skin,originalInteraction);
            CancelPendingClick();DismissGreeting();walking=false;Paused=false;fallbackInteraction="";
            Animation.Play("idle",0);interaction.Start(name);nextWalk=WanderSchedule.Next(watch.Elapsed.TotalSeconds,random.NextDouble());UpdateFrame();
            if(AdditionalInteractions.Has(Skin,name))previousTick=watch.Elapsed.TotalSeconds;
            if(preferences!=null)preferences.RefreshState();
        }
        private void CancelPendingClick(){if(clickTimer!=null)clickTimer.Stop();}
        private void CancelInteraction()
        {
            StopManualRun();
            if(dropMotion.Active){dropMotion.Cancel();SavePosition();}
            CancelPendingClick();bool active=interaction.Active;interaction.Cancel();fallbackInteraction="";originalInteraction=false;
            dragging=false;moved=false;dragDisplaced=false;if(image!=null && image.IsMouseCaptured)image.ReleaseMouseCapture();
            if(active && Animation!=null)Animation.Play("idle",0);
        }
        public void SetPaused(bool value)
        { if(Paused!=value){RecordPetInteraction();DismissGreeting(true);}if(value)CancelInteraction();Paused=value; if(value && walking) { walking=false; Animation.Play("idle",0); SavePosition(); }UpdateFrame(); if(preferences!=null) preferences.RefreshState(); }
        public void ApplySettings()
        {
            StopManualRun();
            RecordPetInteraction();
            Settings.Normalize();if((Settings.LockPosition || Settings.ClickThrough) && dragging){CancelInteraction();SavePosition();}if(handle!=IntPtr.Zero)DesktopInterop.SetClickThrough(handle,Settings.ClickThrough || !petDisplayed);if((Settings.QuietMode || Settings.LockPosition) && walking){walking=false;Animation.Play("idle",0);}PollVisibility(); Rect before=handle==IntPtr.Zero ? Rect.Empty : DesktopInterop.Bounds(handle);
            Width=192*Settings.Scale; Height=208*Settings.Scale; Topmost=Settings.Topmost;
            if(!Settings.Wander && walking) { walking=false; Animation.Play("idle",0); }
            if(handle!=IntPtr.Zero) Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate
            {
                if(closing) return; Rect after=DesktopInterop.Bounds(handle);
                DesktopInterop.Move(handle,new Point(before.Left,before.Bottom-after.Height)); DesktopInterop.ClampWindow(handle); SavePosition();
            }));
            UpdateFrame(); SaveSettings(); if(preferences!=null) preferences.RefreshState();
        }
        public bool SelectSkin(SkinDefinition skin)
        {
            RecordPetInteraction();
            if(Object.ReferenceEquals(skin,Skin))return true;
            if(preferences!=null && preferences.HasUnsavedEdits){Notice="请先保存当前设置，再切换伙伴。";preferences.ShowNotice(Notice);return false;}
            if(DialogueBusy || DictationActive){Notice="请先停止对话、听写或结束 Live，再切换伙伴。";return false;}
            try
            {
                var replacement=skin.Load();var replacementInteractions=InteractionAtlas.LoadForSkin(skin);
                if(palette!=null)palette.Close();if(call!=null)call.Close();CancelInteraction();atlas=replacement;interactionAtlas=replacementInteractions;Skin=skin;interaction.Configure(Skin);PersonaProfiles.Activate(Settings,skin);if(conversation!=null)conversation.SelectPersona(skin.Id);
                DismissGreeting(true);
                motion=null;motionSkin=null;shownRow=shownCol=-1; walking=false; gazeActive=false; Animation=Skin.CreateAnimation(atlas.FrameMultiplier); UpdateFrame();
                Title="桌面伙伴 · "+skin.Name; tray.Text=Title;
                if(quick!=null){var wasVisible=quick.IsVisible;quick.Close();if(wasVisible)ShowQuickChat();}
                if(chat!=null&&chat.IsPersonalConversation){Settings.AgentEnabled=false;var personal=Projects.QuickConversations.Concat(Projects.UnassignedConversations.Where(c=>c.Mode=="chat")).Where(c=>!c.Archived&&c.PetId==skin.Id).OrderByDescending(c=>c.LastOpenedUtcTicks).FirstOrDefault();if(personal==null)BeginDraftConversation(null);else OpenProjectConversation(Projects.QuickConversations.Contains(personal)?ProjectCatalogStore.QuickProject(Projects,Settings):ProjectCatalogStore.UnassignedProject(Projects,Settings),personal);}else if(chat!=null)chat.ReloadHistory();RefreshChatAppearance(); SaveSettings();if(preferences!=null)preferences.RefreshState(); return true;
            }
            catch(Exception e)
            {
                if(!(e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is NotSupportedException || e is System.IO.FileFormatException)) throw;
                Notice="外观无法载入，请检查 PNG 精灵图。"; return false;
            }
        }
        public void ShowSettings()
        { ShowSettings(false); }
        public void ShowSettings(bool connections)
        {
            if(closing || IsUpdating || ReturnToGuide())return;RecordPetInteraction();DismissGreeting();
            var window=EnsureAppWindow();if(preferences==null){preferences=new SettingsPage(this);preferences.GuideRequested+=ShowOnboarding;}
            preferences.RefreshState();window.ShowSettingsPage(preferences);ShowAppWindow(window);if(connections)preferences.SelectConnections();
        }
        internal PetSettings ConversationSettings(ConversationSession session)
        {
            var workspace=Workspaces.OpenWorkspaces.FirstOrDefault(w=>Object.ReferenceEquals(w.Session,session));var cfg=workspace==null?Settings.Copy():workspace.EffectiveSettings();if(workspace==null&&session.PermissionOverride!=null){cfg.AgentApprovalMode=session.PermissionOverride.Mode;cfg.AgentSandboxEnabled=session.PermissionOverride.Sandbox;}return cfg;
        }
        internal void SetConversationPlanMode(ConversationSession session,bool enabled){UpdateConversationSettings(session,delegate(PetSettings cfg){cfg.AgentPlanMode=enabled;});}
        internal void SetConversationGoal(ConversationSession session,string goal){UpdateConversationSettings(session,delegate(PetSettings cfg){cfg.AgentGoal=goal;});}
        void UpdateConversationSettings(ConversationSession session,Action<PetSettings> update)
        {
            var workspace=Workspaces.OpenWorkspaces.FirstOrDefault(w=>Object.ReferenceEquals(w.Session,session));
            if(workspace==null || workspace.ProjectId==ProjectCatalogStore.UnassignedProjectId){update(Settings);if(workspace!=null){var globalSettings=Settings.Copy();update(globalSettings);workspace.Project.PlanMode=globalSettings.AgentPlanMode;workspace.Project.Goal=globalSettings.AgentGoal;}SaveSettings();return;}
            var cfg=workspace.EffectiveSettings();update(cfg);var project=workspace.Project;bool oldPlan=project.PlanMode;string oldGoal=project.Goal;
            try{project.PlanMode=cfg.AgentPlanMode;project.Goal=cfg.AgentGoal;ProjectStore.Save(Projects);}
            catch{project.PlanMode=oldPlan;project.Goal=oldGoal;throw;}
            RefreshChatAppearance();
        }
        public void ShowWorkSettings(){if(preferences!=null&&!preferences.CanChangeWorkSettingsProject(null)){ShowSettings();preferences.SelectWorkSettings();return;}editingProject=null;ShowWorkSettingsCore();}
        void ShowWorkSettingsCore(){if(ReturnToGuide())return;ShowSettings();if(preferences!=null)preferences.SelectWorkSettings();}

        public void EditProject(ProjectDefinition project)
        {
            if(project==null)return;var dialog=new NewProjectDialog(project);bool accepted=dialog.ShowDialog()==true;if(dialog.RemoveRequested){DeleteProject(project);return;}if(dialog.WorkSettingsRequested){if(preferences!=null&&!preferences.CanChangeWorkSettingsProject(project)){ShowSettings();preferences.SelectWorkSettings();return;}editingProject=project;ShowWorkSettingsCore();return;}if(!accepted)return;
            if(Workspaces.OpenWorkspaces.Any(w=>w.ProjectId==project.Id&&(w.Session.IsBusy||w.Queue.HasPending))){MessageBox.Show("请先结束项目中正在运行或排队的任务，再编辑项目。","项目正在使用");return;}
            string name=project.Name,path=project.Workspace;try{project.Name=dialog.ProjectName;project.Workspace=dialog.Workspace;ProjectStore.Save(Projects);}catch(Exception error){project.Name=name;project.Workspace=path;ShowProjectSaveError(error);return;}foreach(var workspace in Workspaces.OpenWorkspaces.Where(w=>w.ProjectId==project.Id))workspace.FollowProject(project);NotifyProjectsChanged();RefreshChatAppearance();
        }
        public void ArchiveProject(ProjectDefinition project)
        {
            if(project==null)return;bool wasCurrent=chat!=null && project.Conversations.Any(c=>c.Id==chat.ConversationId);project.Archived=true;foreach(var conversation in project.Conversations)conversation.Status="idle";SaveProjectsAfterChange(project.Id);CloseProjectWorkspaces(project.Id);if(wasCurrent)SwitchToAvailableConversation(null);
        }
        public void RestoreProject(ProjectDefinition project)
        {
            if(project==null)return;project.Archived=false;SaveProjectsAfterChange(null);if(chat!=null)chat.RefreshAppearance();
        }
        public void ArchiveConversation(ProjectDefinition project,ConversationDefinition conversation)
        {
            if(project==null || conversation==null)return;bool wasCurrent=chat!=null && chat.ConversationId==conversation.Id;conversation.Archived=true;conversation.Status="idle";SaveProjectsAfterChange(null);CloseConversationWorkspace(conversation.Id);if(wasCurrent)SwitchToAvailableConversation(null);
        }
        public void RestoreConversation(ProjectDefinition project,ConversationDefinition conversation)
        {
            if(project==null || conversation==null)return;conversation.Archived=false;SaveProjectsAfterChange(null);if(chat!=null)chat.RefreshAppearance();
        }
        public void DeleteProject(ProjectDefinition project)
        {
            if(project==null || MessageBox.Show("确定永久删除项目“"+project.Name+"”吗？\n只会删除 DeskPet 的项目记录和任务缓存，不会删除工作目录中的源文件。","永久删除项目",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            bool wasCurrent=chat!=null && project.Conversations.Any(c=>c.Id==chat.ConversationId);CloseProjectWorkspaces(project.Id);Projects.Projects.Remove(project);if(editingProject!=null && editingProject.Id==project.Id)editingProject=null;SaveProjectsAfterChange(project.Id);DeleteProjectData(project);if(wasCurrent)SwitchToAvailableConversation(null);
        }
        public async void DeleteConversation(ProjectDefinition project,ConversationDefinition conversation)
        {
            if(project==null || conversation==null)return;
            var dialog=new DeleteConversationDialog(conversation.Name);if(dialog.ShowDialog()!=true)return;
            int index=project.Conversations.IndexOf(conversation);if(index<0)return;
            bool wasCurrent=chat!=null && chat.ConversationId==conversation.Id;project.Conversations.RemoveAt(index);
            try{ProjectStore.Save(Projects);}catch(Exception error){project.Conversations.Insert(index,conversation);new DeleteConversationDialog(conversation.Name,"聊天记录尚未删除，请检查本地存储权限或空间。\n"+error.Message).ShowDialog();return;}
            if(wasCurrent)SwitchToAvailableConversation(null);
            try{await Workspaces.CloseWorkspaceAsync(conversation.Id);}
            catch(Exception error){project.Conversations.Insert(Math.Min(index,project.Conversations.Count),conversation);try{ProjectStore.Save(Projects);}catch(Exception){}new DeleteConversationDialog(conversation.Name,"聊天记录未能关闭，原文件已保留。\n"+error.Message).ShowDialog();return;}
            if(quickWorkspace!=null && quickWorkspace.Id==conversation.Id && quick!=null)quick.Close();
            DeleteConversationData(project.Id,conversation.Id);NotifyProjectsChanged();
        }
        void SaveProjectsAfterChange(string removedProjectId)
        {
            if(!String.IsNullOrEmpty(removedProjectId) && Projects.ActiveProjectId==removedProjectId){var nextProject=Projects.Projects.FirstOrDefault(p=>!p.Archived);Projects.ActiveProjectId=nextProject==null?"":nextProject.Id;}try{ProjectStore.Save(Projects);}catch(Exception error){ShowProjectSaveError(error);}
        }
        void CloseProjectWorkspaces(string projectId)
        {
            foreach(var workspace in Workspaces.OpenWorkspaces.Where(w=>w.ProjectId==projectId).ToArray())CloseConversationWorkspace(workspace.Id);
        }
        void CloseConversationWorkspace(string conversationId)
        {
            var workspace=Workspaces.OpenWorkspaces.FirstOrDefault(w=>w.Id==conversationId);if(workspace!=null)try{Workspaces.CloseWorkspaceAsync(workspace.Id).GetAwaiter().GetResult();}catch(Exception){}
        }
        void DeleteProjectData(ProjectDefinition project)
        {
            foreach(var conversation in project.Conversations)DeleteConversationData(project.Id,conversation.Id);
            DeleteDataDirectory(Path.Combine(Store.DirectoryPath,"projects",project.Id));
        }
        void DeleteConversationData(string projectId,string conversationId)
        {
            DeleteDataDirectory(Path.Combine(Store.DirectoryPath,"projects",projectId,"chats",conversationId));
            DeleteDataDirectory(Path.Combine(Store.DirectoryPath,"conversations",conversationId));
        }
        void DeleteDataDirectory(string path)
        {
            try{string root=Path.GetFullPath(Path.Combine(Store.DirectoryPath,"projects"));string target=Path.GetFullPath(path);string conversations=Path.GetFullPath(Path.Combine(Store.DirectoryPath,"conversations"));if((target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || target.StartsWith(conversations+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) && Directory.Exists(target))Directory.Delete(target,true);}catch(Exception){}
        }
        void SwitchToAvailableConversation(string removedId)
        {
            if(chat==null)return;var project=Projects.Projects.FirstOrDefault(p=>!p.Archived && p.Conversations.Any(c=>!c.Archived));if(project!=null){var conversation=project.Conversations.FirstOrDefault(c=>!c.Archived);if(conversation!=null){OpenProjectConversation(project,conversation);return;}}if(chat!=null)BeginDraftConversation(null);
        }        public void RenameProject(ProjectDefinition project)
        {
            if(project==null)return;var dialog=new ConversationRenameDialog(project.Name,"重命名项目","项目名称");if(dialog.ShowDialog()!=true)return;var name=dialog.ConversationName;if(name.Length<1 || name.Length>80)return;project.Name=name;try{ProjectStore.Save(Projects);}catch(Exception error){ShowProjectSaveError(error);return;}if(chat!=null)chat.RefreshAppearance();
        }
        public void RenameConversation(ProjectDefinition project,ConversationDefinition conversation)
        {
            if(project==null || conversation==null)return;var dialog=new ConversationRenameDialog(conversation.Name);if(dialog.ShowDialog()!=true)return;var name=dialog.ConversationName;if(name.Length<1 || name.Length>80)return;
            conversation.Name=name;conversation.NameEdited=true;conversation.LastOpenedUtcTicks=DateTime.UtcNow.Ticks;try{ProjectStore.Save(Projects);}catch(Exception error){ShowProjectSaveError(error);return;}if(chat!=null)chat.RefreshAppearance();
        }
        void WatchConversationTitle(ProjectDefinition project,ConversationDefinition conversation,ConversationWorkspace workspace)
        {
            if(workspace.UiObserved)return;workspace.UiObserved=true;string lastComplete=workspace.Session.History.Messages.LastOrDefault(m=>m.Role=="assistant"&&m.Status=="complete")==null?"":workspace.Session.History.Messages.Last(m=>m.Role=="assistant"&&m.Status=="complete").Id;bool wasBusy=false;Action refresh=delegate{if(workspace.Session==null || workspace.Queue==null || closing)return;Action update=delegate{if(workspace.Session==null || workspace.Queue==null || closing)return;var current=ProjectCatalogStore.AllConversations(Projects).FirstOrDefault(c=>c.Id==workspace.Id);if(current==null&&!workspace.IsDraft)return;conversation=current??conversation;QueueConversationTitle(project,conversation,workspace);bool busy=workspace.Session.IsBusy||workspace.Queue.IsRunning;string status=busy?"running":"idle";var reply=workspace.Session.History.Messages.LastOrDefault(m=>m.Role=="assistant"&&m.Status=="complete");bool changed=conversation.Status!=status;conversation.Status=status;if(wasBusy&&!busy&&reply!=null&&reply.Id!=lastComplete){lastComplete=reply.Id;conversation.HasUnreadCompletion=!IsConversationVisible(conversation.Id);changed=true;}wasBusy=busy;if(changed){try{ProjectStore.Save(Projects);}catch(Exception){Notice="项目状态尚未保存，请检查本地存储。";}NotifyProjectsChanged();}};if(Dispatcher.CheckAccess())update();else Dispatcher.BeginInvoke(update);};workspace.Session.HistoryChanged+=refresh;workspace.Session.StateChanged+=refresh;workspace.Queue.Changed+=refresh;
        }
        async void QueueConversationTitle(ProjectDefinition project,ConversationDefinition conversation,ConversationWorkspace workspace)
        {
            if(workspace.Session==null || conversation==null || conversation.NameEdited || conversation.Name!="新对话" || titleGenerationStarted.Contains(conversation.Id) || workspace.Session.IsBusy)return;
            if(!workspace.Session.History.Messages.Any(m=>m.Role=="assistant" && m.Status=="complete" && !String.IsNullOrWhiteSpace(m.Content)))return;
            titleGenerationStarted.Add(conversation.Id);var snapshot=Settings.Copy();try
            {
                string title=await workspace.Session.GenerateTitleAsync(snapshot,CancellationToken.None).ConfigureAwait(false);if(String.IsNullOrWhiteSpace(title))return;
                Dispatcher.Invoke(new Action(delegate{var current=ProjectCatalogStore.AllConversations(Projects).FirstOrDefault(c=>c.Id==conversation.Id);if(current==null || current.NameEdited)return;current.Name=title;current.NameEdited=false;try{ProjectStore.Save(Projects);}catch(Exception){Notice="对话标题尚未保存，请检查本地存储。";}if(chat!=null)chat.RefreshAppearance();}));
            }
            catch(Exception){titleGenerationStarted.Remove(conversation.Id);}
        }

        public void ShowLiveSettings(){if(ReturnToGuide())return;ShowSettings(true);if(preferences!=null)preferences.SelectAiSection(3);}
        public void ShowOnboarding()
        {
            if(closing || IsUpdating)return;
            if(guide!=null && guide.IsVisible){guide.Activate();return;}
            if(DialogueBusy || DictationActive || LiveActive){ShowSettings();preferences.ShowNotice("请先结束当前对话、听写或通话，再打开配置引导。");return;}
            ShowSettings();guide=new OnboardingWindow(this,preferences);guide.Closed+=delegate {DismissGreeting();guide=null;};guide.Show();
        }
        public void OpenProjectConversation(ProjectDefinition project,ConversationDefinition conversation)
        {
            if(closing || IsUpdating || ReturnToGuide())return;
            if(!ProjectCatalogStore.VisibleForPet(conversation,Skin.Id))return;if(project.Id==ProjectCatalogStore.UnassignedProjectId||project.Id==ProjectCatalogStore.QuickProjectId)project=ProjectCatalogStore.FindProject(Projects,project.Id,Settings);if(!String.IsNullOrEmpty(conversation.Mode)){Settings.AgentEnabled=conversation.Mode=="work";SaveSettings();}
            Projects.ActiveProjectId=project.Id==ProjectCatalogStore.UnassignedProjectId||project.Id==ProjectCatalogStore.QuickProjectId?"":project.Id;conversation.HasUnreadCompletion=false;conversation.LastOpenedUtcTicks=DateTime.UtcNow.Ticks;try{ProjectStore.Save(Projects);}catch(Exception error){ShowProjectSaveError(error);}var workspace=Workspaces.OpenWorkspaces.FirstOrDefault(w=>w.Id==conversation.Id)??Workspaces.CreateWorkspace(project,conversation);
            WatchConversationTitle(project,conversation,workspace);if(chat==null){chat=(Application.Current==null?null:Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w=>w.IsVisible))??new ChatWindow(this,workspace);chat.Closed+=delegate{chat=null;var ignored=CloseConversationWorkspacesAsync();};}else chat.SwitchWorkspace(workspace);if(!chat.IsVisible)chat.Show();if(chat.WindowState==WindowState.Minimized)chat.WindowState=WindowState.Normal;chat.Activate();
        }
        async Task CloseConversationWorkspacesAsync()
        {
            var open=Workspaces.OpenWorkspaces.Where(w=>!Object.ReferenceEquals(w,quickWorkspace)).ToArray();foreach(var workspace in open){foreach(var project in Projects.Projects){var conversation=project.Conversations.FirstOrDefault(c=>c.Id==workspace.Id);if(conversation!=null){conversation.Status="idle";break;}}}try{ProjectStore.Save(Projects);}catch(Exception){ }foreach(var workspace in open)await Workspaces.CloseWorkspaceAsync(workspace.Id).ConfigureAwait(false);
        }
        // New chats remain drafts until the first submission; their project is chosen in the composer.
        public void NewActiveConversation(){BeginDraftConversation(null,chat==null?Settings.AgentEnabled:chat.WorkEntry);}
        public void NewProjectConversation(ProjectDefinition project){BeginDraftConversation(project,true);}
        public void NewChatConversation(string sectionId){BeginDraftConversation(null,false,sectionId);}
        internal ConversationWorkspace CreateDraftConversation(ProjectDefinition project,bool work,string sectionId=null)
        {
            project=work?(project??ProjectCatalogStore.UnassignedProject(Projects,Settings)):ProjectCatalogStore.UnassignedProject(Projects,Settings);
            string targetSection=work?"":sectionId??ProjectCatalogStore.DefaultChatSectionId;
            var pending=Workspaces.OpenWorkspaces.FirstOrDefault(w=>w.IsDraft&&w.Session!=null&&w.ProjectId==project.Id&&w.Definition.Mode==(work?"work":"chat")&&(work||w.Definition.PetId==Skin.Id&&w.Definition.SectionId==targetSection));
            if(pending!=null)return pending;
            var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="新对话",Mode=work?"work":"chat",PetId=work?"":Skin.Id,SectionId=work?"":sectionId??ProjectCatalogStore.DefaultChatSectionId,CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
            var workspace=Workspaces.CreateWorkspace(project,definition);workspace.IsDraft=true;WatchConversationTitle(project,definition,workspace);return workspace;
        }
        void BeginDraftConversation(ProjectDefinition project,bool? workEntry=null,string sectionId=null)
        {
            if(closing || IsUpdating || ReturnToGuide())return;
            var workspace=CreateDraftConversation(project,workEntry??Settings.AgentEnabled,sectionId);
            if(chat==null){chat=(Application.Current==null?null:Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w=>w.IsVisible))??new ChatWindow(this,workspace);chat.SwitchWorkspace(workspace);chat.Closed+=delegate{chat=null;var ignored=CloseConversationWorkspacesAsync();};}else chat.SwitchWorkspace(workspace);
            if(!chat.IsVisible)chat.Show();chat.Activate();
        }
        internal Action PrepareConversationSubmission(ConversationWorkspace workspace)
        {
            if(workspace==null || !workspace.IsDraft)return null;
            var owner=ProjectCatalogStore.FindProject(Projects,workspace.ProjectId,Settings);if(owner==null || owner.Archived)throw new InvalidOperationException("所选项目已归档或删除，请重新选择。");
            var definition=workspace.Definition;string previousActive=Projects.ActiveProjectId;
            if(owner.Conversations.Any(c=>c.Id==definition.Id))return null;
            owner.Conversations.Add(definition);Projects.ActiveProjectId=owner.Id==ProjectCatalogStore.UnassignedProjectId||owner.Id==ProjectCatalogStore.QuickProjectId?"":owner.Id;
            try{ProjectStore.Save(Projects);}catch{owner.Conversations.Remove(definition);Projects.ActiveProjectId=previousActive;throw;}
            return delegate{owner.Conversations.Remove(definition);Projects.ActiveProjectId=previousActive;ProjectStore.Save(Projects);};
        }
        internal void FinishConversationSubmission(ConversationWorkspace workspace){if(workspace!=null&&workspace.IsDraft){workspace.IsDraft=false;NotifyProjectsChanged();}}
        internal ProjectDefinition CreateProjectForConversation()
        {
            var dialog=new NewProjectDialog();if(dialog.ShowDialog()!=true)return null;var previous=Projects.Copy();try{var project=ProjectCatalogStore.CreateProject(Projects,dialog.ProjectName,dialog.Workspace);project.Conversations.Clear();ProjectStore.Save(Projects);NotifyProjectsChanged();return project;}catch{RestoreProjectCatalog(previous);throw;}
        }
        public void ActivateProject(ProjectDefinition project)
        {
            if(project==null)return;var previous=Projects.Copy();
            try{Projects.ActiveProjectId=project.Id;ProjectStore.Save(Projects);}catch(Exception error){RestoreProjectCatalog(previous);ShowProjectSaveError(error);return;}
            if(project.Conversations.Count>0)OpenProjectConversation(project,project.Conversations[0]);
        }
        public bool MoveConversation(ProjectDefinition from,ProjectDefinition to,ConversationDefinition conversation)
        {
            if(from==null || to==null || conversation==null)return false;var open=Workspaces.OpenWorkspaces.FirstOrDefault(w=>w.Id==conversation.Id);if(open!=null && (open.Session.IsBusy || open.Queue.IsRunning)){Notice="请先停止当前任务，再移动对话。";return false;}var previous=Projects.Copy();
            try{ConversationWorkspace.ResolveDataDirectory(Store.DirectoryPath,conversation.Id);ProjectCatalogStore.MoveConversation(Projects,from.Id,to.Id,conversation.Id);ProjectStore.Save(Projects);}
            catch(Exception error){RestoreProjectCatalog(previous);ShowProjectSaveError(error);return false;}
            // An open workspace keeps the conversation Id but must follow the new
            // project so its agent tools and task records use the new directory.
            if(open!=null)open.FollowProject(to);
            if(chat!=null)chat.RefreshAppearance();return true;
        }
        void RestoreProjectCatalog(ProjectCatalog previous){Projects.Projects.Clear();Projects.Projects.AddRange(previous.Projects);Projects.ActiveProjectId=previous.ActiveProjectId;Projects.Sections=previous.Sections;Projects.UnassignedConversations=previous.UnassignedConversations;Projects.QuickConversations=previous.QuickConversations;Projects.OwnershipVersion=previous.OwnershipVersion;Projects.LegacyRootImported=previous.LegacyRootImported;Projects.RecentIncludeProjects=previous.RecentIncludeProjects;foreach(var workspace in Workspaces.OpenWorkspaces){var project=Projects.Projects.FirstOrDefault(p=>p.Conversations.Any(c=>c.Id==workspace.Id))??(Projects.UnassignedConversations.Any(c=>c.Id==workspace.Id)?ProjectCatalogStore.UnassignedProject(Projects,Settings):Projects.QuickConversations.Any(c=>c.Id==workspace.Id)?ProjectCatalogStore.QuickProject(Projects,Settings):null);if(project!=null)workspace.FollowProject(project);}}
        void ShowProjectSaveError(Exception error){MessageBox.Show(error.Message,"项目记录未保存",MessageBoxButton.OK,MessageBoxImage.Warning);}
        public void OpenProjectWorkspace(ProjectDefinition project)
        {
            if(project==null || !Directory.Exists(project.Workspace))return;Process.Start(new ProcessStartInfo("explorer.exe","\""+project.Workspace+"\""){UseShellExecute=true});
        }
        public void ShowNewProjectDialog()
        {
            var dialog=new NewProjectDialog();if(dialog.ShowDialog()!=true)return;var previous=Projects.Copy();ProjectDefinition project;try{project=ProjectCatalogStore.CreateProject(Projects,dialog.ProjectName,dialog.Workspace);ProjectStore.Save(Projects);}catch(Exception error){RestoreProjectCatalog(previous);ShowProjectSaveError(error);return;}ActivateProject(project);
        }
        internal void OpenChatBrowserLink(Uri uri){ShowChat();if(chat!=null&&chat.IsVisible)chat.OpenBrowserLink(uri);}
        internal void OpenChatProjectFacts(AgentTaskState task){ShowChat();if(chat!=null&&chat.IsVisible)chat.OpenProjectFacts(task);}
        public void ShowChat()
        {if(closing || IsUpdating || ReturnToGuide())return;RecordPetInteraction();if(call!=null){call.Activate();return;}if(quickWorkspace!=null && quick!=null && quick.IsVisible){var project=ProjectCatalogStore.FindProject(Projects,quickWorkspace.ProjectId,Settings);var definition=project==null?null:quickWorkspace.Definition;quick.Hide();if(definition!=null){OpenProjectConversation(project,definition);return;}}if(quick!=null)quick.Hide();DismissGreeting();if(chat!=null){chat.ShowHomePage();if(!chat.IsVisible)chat.Show();if(chat.WindowState==WindowState.Minimized)chat.WindowState=WindowState.Normal;chat.Activate();return;}if(chat==null){chat=(Application.Current==null?null:Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w=>w.IsVisible))??new ChatWindow(this,CreateDraftConversation(null,Settings.AgentEnabled));chat.Closed+=delegate {chat=null;var ignored=CloseConversationWorkspacesAsync();};}chat.ShowHomePage();if(!chat.IsVisible)chat.Show();if(chat.WindowState==WindowState.Minimized)chat.WindowState=WindowState.Normal;chat.Activate();}
        bool IsConversationVisible(string id){return (chat!=null&&chat.IsVisible&&chat.ConversationId==id)||(quick!=null&&quick.IsVisible&&quickWorkspace!=null&&quickWorkspace.Id==id);}
        void NotifyProjectsChanged(){var changed=ProjectsChanged;if(changed!=null)changed();}
        async Task ReleaseQuickWorkspaceAsync(ConversationWorkspace workspace)
        {
            try{if(workspace.Session==null)return;await workspace.Session.WaitForIdleAsync();if(workspace.Queue!=null)await workspace.Queue.FlushAsync();if(!closing && (chat==null || chat.ConversationId!=workspace.Id)&&(quickWorkspace==null||quickWorkspace.Id!=workspace.Id))await Workspaces.CloseWorkspaceAsync(workspace.Id);}catch(Exception){Notice="快速对话保存未完成，请检查本地存储。";}
        }
        public void ShowQuickChat()
        {
            RecordPetInteraction();
            bool preview=GuideActive && guideInteraction=="double-click";
            if(closing || IsUpdating || (!preview && ReturnToGuide()))return;if(call!=null){call.Activate();return;}
            CancelInteraction();UpdateFrame();DismissGreeting();visibility.OverrideFullscreen=true;visibility.UserVisible=true;ApplyVisibility();
            if(quick==null)
            {
                var project=ProjectCatalogStore.EnsureQuickChatProject(Projects,Settings);
                var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="新对话",Mode="quick",PetId=Skin.Id,CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
                quickWorkspace=Workspaces.CreateWorkspace(project,definition);
                quickWorkspace.IsDraft=true;
                WatchConversationTitle(project,definition,quickWorkspace);
                var current=quickWorkspace;quick=new QuickChatWindow(this,current);
                quick.Closed+=delegate
                {
                    quick=null;quickWorkspace=null;visibility.OverrideFullscreen=false;PollVisibility();
                    var ignored=ReleaseQuickWorkspaceAsync(current);
                };
                NotifyProjectsChanged();
            }
            if(!quick.IsVisible)quick.Show();quick.FollowPet();quick.SetGuidePreview(preview);quick.Activate();if(!preview)quick.FocusInput();
        }
        internal void UpdateStateChanged(UpdateCoordinator controller)
        {
            if(closing)return;
            if(controller.IsApplying){CancelInteraction();UpdateFrame();}
            if(controller.IsApplying && palette!=null)palette.Close();
            IsEnabled=!controller.IsApplying;trayMenu.Enabled=!controller.IsApplying && !GuideActive;if(preferences!=null)preferences.IsEnabled=!controller.IsApplying;
            if(chat!=null)chat.IsEnabled=!controller.IsApplying;if(quick!=null)quick.IsEnabled=!controller.IsApplying;
            if(controller.HasRestartCountdown || controller.IsApplying)
            {
                DismissGreeting();
                if(updateNotice==null)updateNotice=new UpdateNoticeWindow(this,controller);updateNotice.Refresh();
            }
            else if(updateNotice!=null)updateNotice.Hide();
        }
        public void ConfigureGreetings(){Settings.Normalize();DismissGreeting(true);SaveSettings();}
        private void ResetGreetingSchedule(){greetingSchedule=new GreetingSchedule(DateTime.UtcNow,Settings.GreetingMinMinutes,Settings.GreetingMaxMinutes,random.NextDouble);}
        private void CancelPendingGreeting(bool retry=true)
        {greetingGeneration++;var pending=greetingRequest;greetingRequest=null;if(pending!=null){pending.Cancel();if(retry && !greetingPreview && !closing && greetingSchedule!=null)greetingSchedule.Defer(DateTime.UtcNow);}}
        private void DismissGreeting(bool restartTimer=false){CancelPendingGreeting(!restartTimer);if(greeting!=null)greeting.Hide();if(restartTimer)ResetGreetingSchedule();}
        public void PreviewGreeting(){if(!closing && IsPetVisible)RunGreeting(true);}
        private void ShowGreeting(){RunGreeting(false);}
        private async void RunGreeting(bool preview)
        {
            if(!GreetingAllowed(DesktopInterop.ForegroundIsFullScreen(handle),preview))return;
            CancelPendingGreeting();var cancellation=new CancellationTokenSource();greetingRequest=cancellation;greetingPreview=preview;int generation=greetingGeneration;
            string skinId=Skin.Id;var snapshot=Settings.Copy();List<string> recent;
            if(!recentGreetings.TryGetValue(skinId,out recent)){recent=new List<string>();recentGreetings[skinId]=recent;}
            try {
                IList<ChatMessage> memory=null;bool memoryFailed=false;
                if(snapshot.AiGreetingEnabled)try {if(companionMemory==null)companionMemory=new CompanionMemory(()=>Conversation);memory=companionMemory.Capture(skinId);}
                catch(Exception error){if(!(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException || error is System.Runtime.Serialization.SerializationException))throw;memoryFailed=true;}
                int phrase=random.Next(GreetingPhrases.Count);
                string line=memoryFailed ? GreetingService.Fallback(skinId,Skin.Name,DateTime.Now,phrase,recent) : await GreetingGenerator.GenerateAsync(snapshot,skinId,Skin.Name,memory,DateTime.Now,phrase,recent,cancellation.Token);
                if(cancellation.IsCancellationRequested || generation!=greetingGeneration || Skin.Id!=skinId)return;
                if(snapshot.GreetingEnabled!=Settings.GreetingEnabled || snapshot.AiGreetingEnabled!=Settings.AiGreetingEnabled || snapshot.SystemPrompt!=Settings.SystemPrompt || !SameGreetingConnection(snapshot.Text,Settings.Text) || !GreetingAllowed(DesktopInterop.ForegroundIsFullScreen(handle),preview)){CancelPendingGreeting();return;}
                greetingRequest=null;
                recent.Add(line);while(recent.Count>8)recent.RemoveAt(0);
                if(greeting==null)greeting=new GreetingBubbleWindow(this);
                greeting.ShowGreeting(line);if(!Paused)PlayAction("waving",1.2);
            }
            catch(OperationCanceledException){}
            finally {if(Object.ReferenceEquals(greetingRequest,cancellation))greetingRequest=null;cancellation.Dispose();}
        }
        private static bool SameGreetingConnection(ConnectionSettings first,ConnectionSettings second)
        {return first!=null && second!=null && first.BaseUrl==second.BaseUrl && first.Model==second.Model && first.EncryptedKey==second.EncryptedKey && first.Protocol==second.Protocol && first.UseProxy==second.UseProxy && first.ProxyAddress==second.ProxyAddress;}
        private void SessionChanged(object sender,Microsoft.Win32.SessionSwitchEventArgs args)
        {
            if(closing)return;Dispatcher.BeginInvoke(new Action(delegate
            {
                if(closing)return;if(args.Reason==Microsoft.Win32.SessionSwitchReason.SessionLock)sessionLocked=true;
                if(args.Reason==Microsoft.Win32.SessionSwitchReason.SessionUnlock)sessionLocked=false;DismissGreeting(true);
            }));
        }
        private void PowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs args)
        {if(!closing && args.Mode==Microsoft.Win32.PowerModes.Resume)Dispatcher.BeginInvoke(new Action(delegate {if(!closing)DismissGreeting(true);}));}
        public void RefreshChatAppearance(){CancelPendingGreeting();if(chat!=null)chat.RefreshAppearance();if(quick!=null)quick.RefreshPersona();if(preferences!=null)preferences.RefreshTextProviders();}
        public void ReloadConversation(){DismissGreeting();if(chat!=null)chat.ReloadHistory();if(quick!=null)quick.RefreshPersona();}
        private void ConversationMemoryChanged()
        {if(closing)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(ConversationMemoryChanged));return;}DismissGreeting();}
        public void UpdateVoiceVolume(int volume){foreach(var session in ConversationSessions)session.SetVolume(volume);if(live!=null)live.SetVolume(volume);}
        public void StopReplySpeech(){foreach(var session in ConversationSessions)session.StopSpeech();}
        public event Action ReplySpeechChanged;
        public void SetReplySpeech(bool enabled)
        {
            if(Settings.ReadReplies==enabled)return;
            Settings.ReadReplies=enabled;if(!enabled)StopReplySpeech();SaveSettings();
            if(ReplySpeechChanged!=null)ReplySpeechChanged();
        }
        public void HidePet() { if(ContextMenu!=null)ContextMenu.IsOpen=false;if(palette!=null)palette.Close();if(call!=null)call.Close();CancelInteraction();visibility.UserVisible=false;visibility.OverrideFullscreen=false;DismissGreeting();walking=false;Animation.Play("idle",0);SavePosition();if(quick!=null)quick.Hide();ApplyVisibility(); }
        public void RestorePet() {RecordPetInteraction();visibility.UserVisible=true;PollVisibility();DesktopInterop.ClampWindow(handle);previousTick=watch.Elapsed.TotalSeconds;}
        private void PollVisibility()
        {
            if(handle==IntPtr.Zero || closing)return;
            if(visibility.OverrideFullscreen && quick!=null && !quick.IsVisible)visibility.OverrideFullscreen=false;
            // Keep the last fullscreen state while a companion-owned panel has focus.
            if(!DesktopInterop.ForegroundBelongsToPet())visibility.Fullscreen=Settings.HideOnFullscreen && DesktopInterop.ForegroundIsFullScreen(handle);
            else if(!Settings.HideOnFullscreen)visibility.Fullscreen=false;
            ApplyVisibility();
        }
        private void ApplyVisibility()
        {
            // Keep the per-pixel surface attached. On Windows 11, hiding and
            // showing this layered HWND can leave its native alpha surface empty.
            // Zero opacity plus native pass-through hides it without that cycle.
            if(visibility.Visible)
            {
                if(!IsVisible)Show();
                if(!petDisplayed)
                {
                    ResetGreetingSchedule();petDisplayed=true;IsHitTestVisible=true;
                    Opacity=1;DesktopInterop.SetClickThrough(handle,Settings.ClickThrough);
                    UpdateFrame();RefreshRendering();
                }
            }
            else if(petDisplayed)
            {
                if(ContextMenu!=null)ContextMenu.IsOpen=false;
                if(palette!=null)palette.Close();if(call!=null)call.Close();CancelInteraction();DismissGreeting();
                walking=false;Animation.Play("idle",0);petDisplayed=false;IsHitTestVisible=false;
                Opacity=0;if(handle!=IntPtr.Zero)DesktopInterop.SetClickThrough(handle,true);RefreshRendering();
            }
            if(visibilityItem!=null)visibilityItem.IsChecked=visibility.UserVisible;
        }
        public void SetQuietMode(bool enabled)
        {Settings.QuietMode=enabled;DismissGreeting(true);if(enabled){StopReplySpeech();walking=false;if(!interaction.Active)Animation.Play("idle",0);}SaveSettings();if(quietItem!=null)quietItem.IsChecked=enabled;if(preferences!=null)preferences.RefreshState();}
        public void SetClickThrough(bool enabled)
        {RecordPetInteraction();Settings.ClickThrough=enabled;if(enabled){CancelInteraction();UpdateFrame();}if(handle!=IntPtr.Zero)DesktopInterop.SetClickThrough(handle,enabled || !petDisplayed);SaveSettings();if(clickThroughItem!=null)clickThroughItem.IsChecked=enabled;if(preferences!=null)preferences.RefreshState();}
        public bool UpdateHotkey(string action,string value,out string error)
        {
            error="";if(!ShortcutCatalog.Validate(Settings,action,value,out error))return false;
            var definition=ShortcutCatalog.All.First(d=>d.Id==action);string normalized=String.IsNullOrWhiteSpace(value)?"":ShortcutCatalog.Normalize(value);
            if(definition.Global){if(hotkeys!=null&&!hotkeys.TryBind(action,normalized,out error))return false;Settings.Hotkeys[action]=normalized;}
            else Settings.LocalShortcuts[action]=normalized;
            SaveSettings();return true;
        }
        public void ReturnHome()
        {
            RecordPetInteraction();
            CancelInteraction();if(handle==IntPtr.Zero) return; walking=false; Animation.Play("idle",0); UpdateFrame();
            Rect bounds=DesktopInterop.Bounds(handle),area=DesktopInterop.WorkArea(handle);
            DesktopInterop.Move(handle,PetMath.ClampPosition(area.Right-bounds.Width-28,area.Bottom-bounds.Height-16,bounds.Width,bounds.Height,area)); SavePosition();
        }
        public void SaveSettings()
        {
            try { Store.Save(Settings); Notice=Store.Warning; }
            catch(Exception e) { if(!(e is IOException || e is UnauthorizedAccessException)) throw; Notice="设置暂时无法保存，请将桌宠放在可写入的文件夹。"; }
            if(preferences!=null) preferences.ShowNotice(Notice);
        }
        private void SavePosition()
        { if(handle==IntPtr.Zero) return; Rect bounds=DesktopInterop.Bounds(handle); Settings.X=bounds.Left; Settings.Y=bounds.Top; SaveSettings(); }
        private async void OnClosing(object sender,System.ComponentModel.CancelEventArgs args)
        {
            if(!exitPrepared)
            {
                args.Cancel=true;if(closing)return;closing=true;DismissGreeting();if(guide!=null)guide.Close();if(palette!=null)palette.Close();if(call!=null)await call.CloseAsync();CancelInteraction();
                timer.Stop();CompositionTarget.Rendering-=RenderFrame;if(hotkeys!=null)hotkeys.Dispose();if(updates!=null)updates.Dispose();
                try{if(dictation!=null)await dictation.CancelAsync();if(inputQueue!=null)await inputQueue.PauseAsync();if(workspaces!=null)await workspaces.SaveAllAsync();if(live!=null)await live.StopAsync();if(conversation!=null){await conversation.StopAsync();await conversation.FlushHistoryAsync();}}
                catch(OperationCanceledException){}finally{exitPrepared=true;DispatcherOperation closingDispatch=Dispatcher.BeginInvoke(new Action(Close));}return;
            }
            SavePosition();if(quick!=null)quick.Close();if(chat!=null)chat.Close();if(mcpConnections!=null)mcpConnections.Dispose();if(workspaces!=null)workspaces.Dispose();if(dictation!=null)dictation.Dispose();if(inputQueue!=null)inputQueue.Dispose();if(live!=null)live.Dispose();if(conversation!=null){conversation.StateChanged-=ConversationChanged;conversation.HistoryChanged-=ConversationMemoryChanged;conversation.Dispose();}if(preferences!=null)preferences.Dispose();
            Microsoft.Win32.SystemEvents.SessionSwitch-=SessionChanged;Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerChanged;if(greeting!=null)greeting.Close();if(updateNotice!=null)updateNotice.Close();
            DisposeImageServices();tray.Visible=false;tray.Dispose();trayMenu.Dispose();trayIcon.Dispose();
        }
    }
}
