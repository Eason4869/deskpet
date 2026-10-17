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
    public sealed class PetWindow : Window
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
        private readonly Forms.ContextMenuStrip trayMenu;
        private SettingsWindow preferences;
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
        bool ReturnToGuide(){if(!GuideActive)return false;if(guide.WindowState==WindowState.Minimized)guide.WindowState=WindowState.Normal;guide.Activate();return true;}

        private ActionPaletteWindow palette;
        private ChatWindow chat;
        private LiveCallView call;
        private ConversationSession conversation;
        private ConversationQueue inputQueue;private ProjectCatalog projectCatalog;private ProjectDefinition editingProject;private ProjectCatalogStore projectCatalogStore;private ConversationWorkspaceManager workspaces;private readonly HashSet<string> titleWatchers=new HashSet<string>(),titleGenerationStarted=new HashSet<string>();
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
        bool HasQueuedMessages {get{return (inputQueue!=null || File.Exists(Path.Combine(Store.DirectoryPath,"queued-messages.json"))) && InputQueue.HasPending;}}
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
        public bool HasHistorySaveWarning {get{return conversation!=null && ((Settings.SaveChatHistory && !String.IsNullOrEmpty(conversation.HistoryWarning)) || !String.IsNullOrEmpty(conversation.AgentTaskWarning));}}
        public string PendingSaveWarning {get{return conversation!=null && !String.IsNullOrEmpty(conversation.AgentTaskWarning)?conversation.AgentTaskWarning:"聊天记录尚未保存，请先导出或处理保存问题后再更新。";}}
        public UpdateCoordinator Updates {get{return updates ?? (updates=new UpdateCoordinator(this));}}
        public bool CanApplyUpdate(bool manual)
        {
            return !closing && !dragging && !DictationActive && (call==null || !call.IsVisible) && !LiveActive && !HasHistorySaveWarning && !HasQueuedMessages && (inputQueue==null || !inputQueue.IsRunning) && (conversation==null || !conversation.IsBusy) && (workspaces==null || !workspaces.Busy) && !sessionLocked && (ContextMenu==null || !ContextMenu.IsOpen) && (trayMenu==null || !trayMenu.Visible) &&
                (chat==null || (!chat.HasDraft && (manual || !chat.IsVisible))) && (quick==null || (!quick.HasDraft && (manual || !quick.IsVisible))) &&
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
        private Forms.ToolStripMenuItem quietItem,clickThroughItem;
        private bool exitPrepared;
        public string HotkeyWarning {get;private set;}
        public bool UserVisible {get{return visibility.UserVisible;}}
        public bool AutoHidden {get{return visibility.AutoHidden;}}
        private bool petDisplayed=true;
        public bool IsPetVisible {get{return IsVisible && petDisplayed;}}
        private Forms.ToolStripMenuItem visibilityItem;
        private IntPtr handle;
        private bool dragging,moved,closing,walking;
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
                    try{projectCatalog=projectCatalogStore.Load();}catch(Exception){projectCatalog=new ProjectCatalog();}
                    if(projectCatalog.Projects.Count==0){projectCatalog=ProjectCatalogStore.MigrateLegacy(Settings);try{projectCatalogStore.Save(projectCatalog);}catch(Exception){}}
                }
                return projectCatalog;
            }
        }
        public ProjectCatalogStore ProjectStore {get{if(projectCatalogStore==null){var ignored=Projects;}return projectCatalogStore;}}
        public ConversationWorkspaceManager Workspaces
        {
            get
            {
                if(workspaces==null)workspaces=new ConversationWorkspaceManager(Store.DirectoryPath,Skin.Id,delegate{return new ApiClient();},delegate{return new SpeechOutput(new ApiClient(),Path.Combine(Store.DirectoryPath,"audio-cache"),Dispatcher);},delegate{return new AudioRecorder();},delegate{return Settings;},delegate{return Settings.SaveChatHistory;});
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
                    conversation=new ConversationSession(api,voice,delegate {return new AudioRecorder();},new HistoryStore(Path.Combine(Store.DirectoryPath,"history")),delegate {return Settings.SaveChatHistory;});conversation.SelectPersona(Skin.Id);conversation.StateChanged+=ConversationChanged;conversation.HistoryChanged+=ConversationMemoryChanged;
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
            atlas=source; Settings=settings; Store=store; wake=wakeSignal; Settings.Normalize();
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
            clickTimer.Tick+=delegate {clickTimer.Stop();if(!closing && IsPetVisible && !Paused && !dragging && !DialogueBusy){PlayInteraction(pendingHeadClick ? "petting" : "poked");NotifyGuideInteraction("click");}};
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
                foreach(var pair in Settings.Hotkeys){string error;if(!hotkeys.TryBind(pair.Key,pair.Value,out error))HotkeyWarning=Notice=error+"（"+pair.Value+"）";}
            };
            Loaded+=delegate
            {
                if(Settings.X.HasValue && Settings.Y.HasValue) DesktopInterop.Move(handle,new Point(Settings.X.Value,Settings.Y.Value));
                else ReturnHome();
                DesktopInterop.ClampWindow(handle); lastCursor=DesktopInterop.Cursor(); previousTick=watch.Elapsed.TotalSeconds;
                UpdateFrame(); timer.Start();
            };
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Dafeiyu.Icon")) trayIcon=(System.Drawing.Icon)new System.Drawing.Icon(stream).Clone();
            trayMenu=new Forms.ContextMenuStrip();
            visibilityItem=new Forms.ToolStripMenuItem("显示 / 隐藏伙伴"){Checked=true};visibilityItem.Click+=delegate {Dispatcher.BeginInvoke(new Action(delegate {if(visibility.UserVisible)HidePet();else RestorePet();}));};trayMenu.Items.Add(visibilityItem);
            quietItem=new Forms.ToolStripMenuItem("免打扰"){Checked=Settings.QuietMode};quietItem.Click+=delegate {Dispatcher.BeginInvoke(new Action(delegate {SetQuietMode(!Settings.QuietMode);}));};trayMenu.Items.Add(quietItem);
            clickThroughItem=new Forms.ToolStripMenuItem("鼠标穿透"){Checked=Settings.ClickThrough};clickThroughItem.Click+=delegate {Dispatcher.BeginInvoke(new Action(delegate {SetClickThrough(!Settings.ClickThrough);}));};trayMenu.Items.Add(clickThroughItem);
            trayMenu.Opening+=delegate {visibilityItem.Checked=visibility.UserVisible;quietItem.Checked=Settings.QuietMode;clickThroughItem.Checked=Settings.ClickThrough;};
            AddTray("快速对话",ShowQuickChat);AddTray("打开完整对话",ShowChat); AddTray("设置",ShowSettings);
            AddTray("回到屏幕角落",ReturnHome);
            trayMenu.Items.Add(new Forms.ToolStripSeparator()); AddTray("退出桌宠",Close);
            tray=new Forms.NotifyIcon { Icon=trayIcon,Text="桌面伙伴 · "+Skin.Name,ContextMenuStrip=trayMenu,Visible=true };
            tray.DoubleClick+=delegate { Dispatcher.BeginInvoke(new Action(delegate { RestorePet(); ShowSettings(); })); };
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
        private void AddTray(string text,Action action)
        { trayMenu.Items.Add(text,null,delegate { Dispatcher.BeginInvoke(action); }); }
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
            menu.Opened+=delegate{RecordPetInteraction();firstSkill.Header=Skin.SpecialAction;extraSkill.Header=AdditionalInteractions.SpecialName(Skin);extraSkill.Visibility=AdditionalInteractions.Supports(Skin)?Visibility.Visible:Visibility.Collapsed;};
            menu.Opened+=delegate {CancelPendingClick();sleep.Header=interaction.IsSleeping ? "叫醒" : "小憩";interactions.IsEnabled=!DialogueBusy;};
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
                !WindowInUse(quick) && !WindowInUse(chat) && !WindowInUse(preferences) && (updates==null || !updates.HasRestartCountdown) && greetingRequest==null;
            if(!idleSleep.TrySleep(now,systemIdle,allowed,Settings.AutoSleepEnabled))return false;
            StartInteraction("sleeping");return true;
        }
        private bool GreetingAllowed(bool fullscreen,bool preview)
        {
            return !closing && !Settings.QuietMode && (preview || Settings.GreetingEnabled) && IsPetVisible && !Paused && !dragging && !walking && !interaction.Active && !clickTimer.IsEnabled && !sessionLocked && !DialogueBusy && !DictationActive && !HasQueuedMessages && (!GuideActive || (preview && guide.TargetName=="GreetingEnabled")) && (updates==null || !updates.HasRestartCountdown) &&
                (ContextMenu==null || !ContextMenu.IsOpen) && !trayMenu.Visible && (palette==null || !palette.IsVisible) &&
                !WindowInUse(quick) && !WindowInUse(chat) && (preview || !WindowInUse(preferences)) && !fullscreen;
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
            if(!dragging && !interaction.Active && guideInteraction.Length==0) Wander(delta,now,cursor);
            if(dropMotion.Active)
            {
                dropMotion.Advance(Math.Min(delta,.125));var dropBounds=DesktopInterop.Bounds(handle);
                DesktopInterop.Move(handle,new Point(dropBounds.Left,dropMotion.Position));
                if(!dropMotion.Active)SavePosition();
            }
            bool interacting=interaction.Active;
            // New eight-pose actions must show every pose even if a UI frame is late.
            interaction.Advance(beforeHover!=interactionRevision?0:AdditionalInteractions.Has(Skin,interaction.Name)?Math.Min(delta,.125):delta);
            if(interacting && !interaction.Active){fallbackInteraction="";Animation.Play("idle",0);nextWalk=WanderSchedule.Next(now,random.NextDouble());}
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
            bool allowed=!Paused && !dragging && !walking && !interaction.Active && !DialogueBusy && !GuideActive && !Settings.ClickThrough && !clickTimer.IsEnabled && Animation.State=="idle" && (ContextMenu==null || !ContextMenu.IsOpen) && (palette==null || !palette.IsVisible) && (quick==null || !quick.IsVisible) && (chat==null || !chat.IsVisible);
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
            var extra=additionalInteractions.Get(Skin,interaction.Active?interaction.Name+(originalInteraction?"-original":""):Animation.State);
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
            if(DialogueBusy){args.Handled=true;return;}
            if(dropMotion.Active){dropMotion.Cancel();SavePosition();}
            RecordPetInteraction();
            CancelPendingClick();DismissGreeting();
            walking=false; dragStart=DesktopInterop.Cursor(); Rect bounds=DesktopInterop.Bounds(handle); windowStart=bounds.TopLeft;
            pendingHeadClick=dragStart.Y-bounds.Top<bounds.Height*.55;
            moved=false;dragDisplaced=false;dragging=true;image.CaptureMouse();args.Handled=true;
        }
        private void Drag(object sender,MouseEventArgs args)
        {
            if(closing || DialogueBusy || (GuideActive && guideInteraction!="drag") || (Settings.LockPosition && guideInteraction!="drag") || !dragging || args.LeftButton!=MouseButtonState.Pressed) return;
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
                bool stumble=dropReaction.TryStumble(dragDisplaced,!Paused && Settings.StumbleEnabled && !DialogueBusy && !GuideActive,AdditionalInteractions.Supports(Skin),watch.Elapsed.TotalSeconds);
                if(!Paused)
                {
                    PlayInteraction(stumble?"stumble":"landing",originalDrop);
                    var dropBounds=DesktopInterop.Bounds(handle);var dropArea=DesktopInterop.WorkArea(handle);
                    dropMotion.Start(dropBounds.Top,dropBounds.Height,dropArea.Bottom);
                }
                if((DesktopInterop.Bounds(handle).TopLeft-windowStart).Length>2)NotifyGuideInteraction("drag");
            }
            else if(!closing && !Paused && !DialogueBusy && (!GuideActive || guideInteraction=="click")){clickTimer.Interval=TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime);clickTimer.Start();}
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
        { RecordPetInteraction();CancelInteraction();interactionRevision++;walking=false; Paused=false;nextWalk=WanderSchedule.Next(watch.Elapsed.TotalSeconds,random.NextDouble()); Animation.Play(state,duration);if(AdditionalInteractions.Has(Skin,state))previousTick=watch.Elapsed.TotalSeconds; UpdateFrame(); if(preferences!=null) preferences.RefreshState(); }
        public void PlayInteraction(string name)
        { PlayInteraction(name,null); }
        private void PlayInteraction(string name,bool? originalStyle)
        {
            if(closing || DialogueBusy)return;
            RecordPetInteraction();
            if((name=="stumble" || name=="special2") && !AdditionalInteractions.Supports(Skin)){if(name=="special2")return;name="landing";}
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
            if(closing || DialogueBusy || dragging || (GuideActive && guideInteraction!="wheel"))return;CancelPendingClick();DismissGreeting();
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
            if(dropMotion.Active){dropMotion.Cancel();SavePosition();}
            CancelPendingClick();bool active=interaction.Active;interaction.Cancel();fallbackInteraction="";originalInteraction=false;
            dragging=false;moved=false;dragDisplaced=false;if(image!=null && image.IsMouseCaptured)image.ReleaseMouseCapture();
            if(active && Animation!=null)Animation.Play("idle",0);
        }
        public void SetPaused(bool value)
        { if(Paused!=value){RecordPetInteraction();DismissGreeting(true);}if(value)CancelInteraction();Paused=value; if(value && walking) { walking=false; Animation.Play("idle",0); SavePosition(); }UpdateFrame(); if(preferences!=null) preferences.RefreshState(); }
        public void ApplySettings()
        {
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
            if(skin.Id==Skin.Id)return true;
            if(preferences!=null && preferences.HasUnsavedEdits){Notice="请先保存当前设置，再切换伙伴。";preferences.ShowNotice(Notice);return false;}
            if(DialogueBusy || DictationActive){Notice="请先停止对话、听写或结束 Live，再切换伙伴。";return false;}
            try
            {
                var replacement=skin.Load();var replacementInteractions=InteractionAtlas.LoadForSkin(skin);
                if(palette!=null)palette.Close();if(call!=null)call.Close();CancelInteraction();atlas=replacement;interactionAtlas=replacementInteractions;Skin=skin;interaction.Configure(Skin);PersonaProfiles.Activate(Settings,skin);if(conversation!=null)conversation.SelectPersona(skin.Id);
                DismissGreeting(true);
                shownRow=shownCol=-1; walking=false; gazeActive=false; Animation=Skin.CreateAnimation(atlas.FrameMultiplier); UpdateFrame();
                Title="桌面伙伴 · "+skin.Name; tray.Text=Title;if(chat!=null)chat.ReloadHistory();if(quick!=null)quick.RefreshPersona();RefreshChatAppearance(); SaveSettings();if(preferences!=null)preferences.RefreshState(); return true;
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
            if(closing || IsUpdating || ReturnToGuide())return;
            RecordPetInteraction();
            DismissGreeting();
            if(preferences==null) { preferences=new SettingsWindow(this); preferences.GuideRequested+=ShowOnboarding;preferences.Closed+=delegate {CancelPendingGreeting();preferences=null;}; }
            if(!preferences.IsVisible) preferences.Show(); preferences.Activate(); preferences.RefreshState();
            if(connections) preferences.SelectConnections();
        }
        public void ShowWorkSettings(){editingProject=null;ShowWorkSettingsCore();}
        void ShowWorkSettingsCore(){if(ReturnToGuide())return;ShowSettings();if(preferences!=null)preferences.SelectWorkSettings();}

        public void EditProject(ProjectDefinition project){editingProject=project;ShowWorkSettingsCore();}
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
            bool wasCurrent=chat!=null && project.Conversations.Any(c=>c.Id==chat.ConversationId);CloseProjectWorkspaces(project.Id);Projects.Projects.Remove(project);if(editingProject!=null && editingProject.Id==project.Id)editingProject=null;SaveProjectsAfterChange(project.Id);DeleteProjectData(project.Id);if(wasCurrent)SwitchToAvailableConversation(null);
        }
        public void DeleteConversation(ProjectDefinition project,ConversationDefinition conversation)
        {
            if(project==null || conversation==null || MessageBox.Show("确定永久删除对话“"+conversation.Name+"”吗？\n对话历史和任务缓存会一并删除。","永久删除对话",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            bool wasCurrent=chat!=null && chat.ConversationId==conversation.Id;project.Conversations.Remove(conversation);SaveProjectsAfterChange(null);CloseConversationWorkspace(conversation.Id);DeleteConversationData(project.Id,conversation.Id);if(wasCurrent)SwitchToAvailableConversation(null);
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
        void DeleteProjectData(string projectId)
        {
            DeleteDataDirectory(Path.Combine(Store.DirectoryPath,"projects",projectId));
        }
        void DeleteConversationData(string projectId,string conversationId)
        {
            DeleteDataDirectory(Path.Combine(Store.DirectoryPath,"projects",projectId,"chats",conversationId));
        }
        void DeleteDataDirectory(string path)
        {
            try{string root=Path.GetFullPath(Path.Combine(Store.DirectoryPath,"projects"));string target=Path.GetFullPath(path);if(target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))Directory.Delete(target,true);}catch(Exception){}
        }
        void SwitchToAvailableConversation(string removedId)
        {
            if(chat==null)return;var project=Projects.Projects.FirstOrDefault(p=>!p.Archived && p.Conversations.Any(c=>!c.Archived));if(project!=null){var conversation=project.Conversations.FirstOrDefault(c=>!c.Archived);if(conversation!=null){OpenProjectConversation(project,conversation);return;}}if(chat!=null)chat.SwitchWorkspace(null);
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
            if(!titleWatchers.Add(conversation.Id))return;Action refresh=delegate{QueueConversationTitle(project,conversation,workspace);};workspace.Session.HistoryChanged+=refresh;workspace.Session.StateChanged+=refresh;
        }
        async void QueueConversationTitle(ProjectDefinition project,ConversationDefinition conversation,ConversationWorkspace workspace)
        {
            if(conversation==null || conversation.NameEdited || conversation.Name!="新对话" || titleGenerationStarted.Contains(conversation.Id) || workspace.Session.IsBusy)return;
            if(!workspace.Session.History.Messages.Any(m=>m.Role=="assistant" && m.Status=="complete" && !String.IsNullOrWhiteSpace(m.Content)))return;
            titleGenerationStarted.Add(conversation.Id);var snapshot=Settings.Copy();try
            {
                string title=await workspace.Session.GenerateTitleAsync(snapshot,CancellationToken.None).ConfigureAwait(false);if(String.IsNullOrWhiteSpace(title))return;
                Dispatcher.Invoke(new Action(delegate{var current=Projects.Projects.SelectMany(p=>p.Conversations).FirstOrDefault(c=>c.Id==conversation.Id);if(current==null || current.NameEdited)return;current.Name=title;current.NameEdited=false;try{ProjectStore.Save(Projects);}catch(Exception error){ShowProjectSaveError(error);}if(chat!=null)chat.RefreshAppearance();}));
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
            Projects.ActiveProjectId=project.Id;conversation.Status="active";try{ProjectStore.Save(Projects);}catch(Exception error){ShowProjectSaveError(error);}var workspace=Workspaces.OpenWorkspaces.FirstOrDefault(w=>w.Id==conversation.Id)??Workspaces.CreateWorkspace(project,conversation,delegate{var currentSettings=Settings.Copy();currentSettings.AgentWorkspace=project.Workspace;currentSettings.AgentGoal=project.Goal;currentSettings.AgentFullAccess=project.FullAccess;currentSettings.AgentBackupFiles=project.BackupFiles;currentSettings.AgentPlanMode=project.PlanMode;currentSettings.AgentCommandsEnabled=project.CommandsEnabled;currentSettings.AgentCommandRecipes=project.CommandRecipes;currentSettings.AgentCommandTimeoutSeconds=project.CommandTimeoutSeconds;currentSettings.AgentContextTokenLimit=project.ContextTokenLimit;return currentSettings;});
            WatchConversationTitle(project,conversation,workspace);if(chat==null){chat=(Application.Current==null?null:Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w=>w.IsVisible))??new ChatWindow(this,workspace);chat.Closed+=delegate{chat=null;var ignored=CloseConversationWorkspacesAsync();};}else chat.SwitchWorkspace(workspace);if(!chat.IsVisible)chat.Show();if(chat.WindowState==WindowState.Minimized)chat.WindowState=WindowState.Normal;chat.Activate();
        }
        async Task CloseConversationWorkspacesAsync()
        {
            var open=Workspaces.OpenWorkspaces.ToArray();foreach(var workspace in open){foreach(var project in Projects.Projects){var conversation=project.Conversations.FirstOrDefault(c=>c.Id==workspace.Id);if(conversation!=null){conversation.Status="idle";break;}}}try{ProjectStore.Save(Projects);}catch(Exception){ }foreach(var workspace in open)await Workspaces.CloseWorkspaceAsync(workspace.Id).ConfigureAwait(false);
        }
        // New conversations belong to the project currently selected in the sidebar;
        // fall back to the most recently opened project rather than an arbitrary one.
        public void NewActiveConversation(){
            var projects=Projects.Projects??new System.Collections.Generic.List<ProjectDefinition>();
            var project=projects.FirstOrDefault(p=>p.Id==Projects.ActiveProjectId)
                ??projects.OrderByDescending(p=>p.LastOpenedUtcTicks).FirstOrDefault()
                ??projects.FirstOrDefault();
            if(project==null){ShowNewProjectDialog();return;}
            if(project.Id!=Projects.ActiveProjectId){Projects.ActiveProjectId=project.Id;try{ProjectStore.Save(Projects);}catch(Exception error){ShowProjectSaveError(error);}}
            NewProjectConversation(project);
        }
        public void NewProjectConversation(ProjectDefinition project)
        {
            var previous=Projects.Copy();ConversationDefinition conversation;
            try{conversation=ProjectCatalogStore.CreateConversation(Projects,project.Id,"新对话");ProjectStore.Save(Projects);}catch(Exception error){RestoreProjectCatalog(previous);ShowProjectSaveError(error);return;}
            OpenProjectConversation(project,conversation);
        }
        public void ActivateProject(ProjectDefinition project)
        {
            if(project==null)return;var previous=Projects.Copy();
            try{Projects.ActiveProjectId=project.Id;ProjectStore.Save(Projects);}catch(Exception error){RestoreProjectCatalog(previous);ShowProjectSaveError(error);return;}
            if(project.Conversations.Count>0)OpenProjectConversation(project,project.Conversations[0]);
        }
        public bool MoveConversation(ProjectDefinition from,ProjectDefinition to,ConversationDefinition conversation)
        {
            if(from==null || to==null || conversation==null)return false;var previous=Projects.Copy();
            try{ProjectCatalogStore.MoveConversation(Projects,from.Id,to.Id,conversation.Id);ProjectStore.Save(Projects);}
            catch(Exception error){RestoreProjectCatalog(previous);ShowProjectSaveError(error);return false;}
            // An open workspace keeps the conversation Id but must follow the new
            // project so its agent tools and task records use the new directory.
            var open=Workspaces.OpenWorkspaces.FirstOrDefault(w=>w.Id==conversation.Id);
            if(open!=null)open.FollowProject(to);
            if(chat!=null)chat.RefreshAppearance();return true;
        }
        void RestoreProjectCatalog(ProjectCatalog previous){Projects.Projects.Clear();Projects.Projects.AddRange(previous.Projects);Projects.ActiveProjectId=previous.ActiveProjectId;}
        void ShowProjectSaveError(Exception error){MessageBox.Show(error.Message,"项目记录未保存",MessageBoxButton.OK,MessageBoxImage.Warning);}
        public void OpenProjectWorkspace(ProjectDefinition project)
        {
            if(project==null || !Directory.Exists(project.Workspace))return;Process.Start(new ProcessStartInfo("explorer.exe","\""+project.Workspace+"\""){UseShellExecute=true});
        }
        public void ShowNewProjectDialog()
        {
            var dialog=new NewProjectDialog();if(dialog.ShowDialog()!=true)return;var previous=Projects.Copy();ProjectDefinition project;try{project=ProjectCatalogStore.CreateProject(Projects,dialog.ProjectName,dialog.Workspace);ProjectStore.Save(Projects);}catch(Exception error){RestoreProjectCatalog(previous);ShowProjectSaveError(error);return;}ActivateProject(project);
        }
        public void ShowChat()
        {if(closing || IsUpdating || ReturnToGuide())return;RecordPetInteraction();if(call!=null){call.Activate();return;}if(quick!=null)quick.Hide();DismissGreeting();if(chat!=null){if(!chat.IsVisible)chat.Show();if(chat.WindowState==WindowState.Minimized)chat.WindowState=WindowState.Normal;chat.Activate();return;}if(chat==null){chat=(Application.Current==null?null:Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w=>w.IsVisible))??new ChatWindow(this);chat.Closed+=delegate {chat=null;var ignored=CloseConversationWorkspacesAsync();};}if(!chat.IsVisible)chat.Show();if(chat.WindowState==WindowState.Minimized)chat.WindowState=WindowState.Normal;chat.Activate();}
        public void ShowQuickChat()
        {
            RecordPetInteraction();
            bool preview=GuideActive && guideInteraction=="double-click";
            if(closing || IsUpdating || (!preview && ReturnToGuide()))return;if(call!=null){call.Activate();return;}
            CancelInteraction();UpdateFrame();DismissGreeting();visibility.OverrideFullscreen=true;visibility.UserVisible=true;ApplyVisibility();
            if(quick==null){quick=new QuickChatWindow(this);quick.Closed+=delegate {quick=null;visibility.OverrideFullscreen=false;PollVisibility();};}
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
        public void UpdateVoiceVolume(int volume){if(conversation!=null)conversation.SetVolume(volume);if(live!=null)live.SetVolume(volume);}
        public void StopReplySpeech(){if(conversation!=null)conversation.StopSpeech();}
        public event Action ReplySpeechChanged;
        public void SetReplySpeech(bool enabled)
        {
            if(Settings.ReadReplies==enabled)return;
            Settings.ReadReplies=enabled;if(!enabled)StopReplySpeech();SaveSettings();
            if(ReplySpeechChanged!=null)ReplySpeechChanged();
        }
        private void ConversationChanged()
        {
            if(closing)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(ConversationChanged));return;}
            if(DialogueBusy || DictationActive)DismissGreeting();
            if(DialogueBusy){CancelInteraction();UpdateFrame();}else if(interaction.Active)return;
            walking=false;if(Paused)return;var stage=conversation==null ? ConversationStage.Idle : conversation.Stage;string state=LiveActive ? "waiting" : stage==ConversationStage.Recording?"waiting":stage==ConversationStage.Recognizing?"review":stage==ConversationStage.Thinking?"running":stage==ConversationStage.Speaking?"waving":"idle";Animation.Play(state,0);UpdateFrame();
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
            if(visibilityItem!=null)visibilityItem.Checked=visibility.UserVisible;
        }
        public void SetQuietMode(bool enabled)
        {Settings.QuietMode=enabled;DismissGreeting(true);if(enabled){StopReplySpeech();walking=false;if(!interaction.Active)Animation.Play("idle",0);}SaveSettings();if(quietItem!=null)quietItem.Checked=enabled;if(preferences!=null)preferences.RefreshState();}
        public void SetClickThrough(bool enabled)
        {RecordPetInteraction();Settings.ClickThrough=enabled;if(enabled){CancelInteraction();UpdateFrame();}if(handle!=IntPtr.Zero)DesktopInterop.SetClickThrough(handle,enabled || !petDisplayed);SaveSettings();if(clickThroughItem!=null)clickThroughItem.Checked=enabled;if(preferences!=null)preferences.RefreshState();}
        public bool UpdateHotkey(string action,string value,out string error)
        {error="";if(hotkeys!=null && !hotkeys.TryBind(action,value,out error))return false;Settings.Hotkeys[action]=(value ?? "").Trim();SaveSettings();return true;}
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
            SavePosition();if(quick!=null)quick.Close();if(chat!=null)chat.Close();if(workspaces!=null)workspaces.Dispose();if(dictation!=null)dictation.Dispose();if(inputQueue!=null)inputQueue.Dispose();if(live!=null)live.Dispose();if(conversation!=null){conversation.StateChanged-=ConversationChanged;conversation.HistoryChanged-=ConversationMemoryChanged;conversation.Dispose();}if(preferences!=null)preferences.Close();
            Microsoft.Win32.SystemEvents.SessionSwitch-=SessionChanged;Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerChanged;if(greeting!=null)greeting.Close();if(updateNotice!=null)updateNotice.Close();
            tray.Visible=false;tray.Dispose();trayMenu.Dispose();trayIcon.Dispose();
        }
    }
}
