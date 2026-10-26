using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed partial class PetWindow : Window
{
    private readonly DesktopController owner;
    private readonly SpriteAssets assets;
    private readonly SpriteView sprite;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Random random = new();
    private readonly DropMotion drop = new();
    private readonly DropReaction reaction;
    private readonly IdleSleepSchedule sleep;
    private readonly GazeController gaze = new();
    private readonly InteractionPlayback playback;
    private string action => playback.Action;
    private bool original => playback.Original;
    private bool dragging, moved, nativeIgnored, fullscreenHidden;
    private bool feedbackQuiet,feedbackFullscreen,feedbackInteractive=true,feedbackSessionObserved;
    private ulong feedbackSessionRevision;
    private void ObserveFeedbackQuiet()
    {if(feedbackQuiet!=owner.Settings.QuietMode){feedbackQuiet=owner.Settings.QuietMode;owner.Relationships.InvalidateFeedback();}}
    private void ObserveFeedbackSession()
    {
        if(NativeWindowHandle==IntPtr.Zero)return;
        bool interactive=MacServices.IsSessionInteractive;ulong revision=MacServices.SessionLifecycleRevision;
        bool fullscreen=MacServices.IsFrontApplicationFullscreen();
        if(feedbackSessionObserved&&(interactive!=feedbackInteractive||revision!=feedbackSessionRevision||fullscreen!=feedbackFullscreen))owner.Relationships.InvalidateFeedback();
        feedbackInteractive=interactive;feedbackSessionRevision=revision;feedbackFullscreen=fullscreen;feedbackSessionObserved=true;
    }
    internal bool RelationshipFeedbackAllowed{get{ObserveFeedbackSession();return IsPetVisible&&sessionInteractive&&feedbackInteractive&&owner.Relationships.FeedbackEnabled;}}
    internal void CelebrateRelationship(string name){if(!RelationshipFeedbackAllowed||owner.Settings.QuietMode||owner.HolidayBusinessBusy||owner.WorkBindings.Manager.Busy||owner.WorkBindings.Jobs.Manager?.HasActiveJobs==true||owner.GuideLocked||Paused||dragging||drop.Active||holidayPlayback.Active||holidayTransition.Active||action!="idle")return;Interact(name);}
    public bool IsPetVisible=>IsVisible && !fullscreenHidden && Opacity>0;
    private Point press;
    private PixelPoint start;
    private double lastTime, wanderAt, runUntil, policyAt,keepsakeFrameAt;
    private double? walkTarget;
    private double motionX;
    private int direction = 1;
    private GreetingSchedule greetingSchedule;
    private CancellationTokenSource? greetingRequest;
    private Window? greetingBubble;
    private readonly List<string> recentGreetings = new();
    private bool paused;
    internal bool CompanionSourceRefreshSafe => !dragging && !drop.Active && !holidayPlayback.Active && !holidayTransition.Active && action == "idle";
    public bool Paused { get => paused; set { if(paused!=value)DismissGreeting();paused = value; if (value) CancelHoliday(); RefreshHoliday(true); } }
    public PetWindow(DesktopController controller, SpriteAssets? spriteAssets = null)
    {
        owner = controller;
        assets = spriteAssets ?? new SpriteAssets();
        reaction = new DropReaction(random.NextDouble);
        sleep = new IdleSleepSchedule(0, random.NextDouble);
        greetingSchedule = new GreetingSchedule(DateTime.UtcNow, owner.Settings.GreetingMinMinutes, owner.Settings.GreetingMaxMinutes, random.NextDouble);
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent; CanResize = false; ShowInTaskbar = false;
        LocalizedUi.Set(this,"Title",()=>(TextResources.Get("petWindow.petWindow.title")));
        sprite = new SpriteView { Assets = assets, Skin = owner.Skin };
        playback = new InteractionPlayback(ResolveHolidayAction);
        InitializeHoliday();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += (_, _) => { if (dragging) FinishDrag(); };
        PointerExited += (_, _) => LeaveHolidayHover();
        PropertyChanged += (_, e) => {if(e.Property==IsVisibleProperty)owner.Relationships.InvalidateFeedback();};
        Screens.Changed += ScreensChanged;
        DoubleTapped += (_, _) => owner.ShowChat(true);
        ContextMenu = BuildMenu();
        Opened += (_, _) => { ObserveFeedbackSession();owner.Relationships.EnqueueVisit(owner.RelationshipCompanionId,IsPetVisible);ApplySettings(); Clamp(); ConfigureNative(false); };
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        Closed += async (_, _) => { owner.Relationships.InvalidateFeedback();timer.Stop(); Screens.Changed -= ScreensChanged; await owner.HolidayKeepsakes.FlushAsync();DisposeHoliday(); DismissGreeting(); assets.Dispose(); };
        wanderAt = WanderSchedule.Next(0, random.NextDouble());
        ApplySettings();
        if (owner.Settings.X == null || owner.Settings.Y == null)
        {
            var area = WorkArea(); Position = new PixelPoint(area.Right - (int)(Width * RenderScaling) - 24, area.Bottom - (int)(Height * RenderScaling) - 24); Clamp();
        }
        Play("idle", false);
    }
    public void ApplySettings()
    {
        relationshipContentEpoch++;
        ObserveFeedbackQuiet();
        CancelHoliday();
        Width = 192 * owner.Settings.Scale; Height = 208 * owner.Settings.Scale;
        Topmost = owner.Settings.Topmost;
        if (!owner.Settings.Wander && !owner.Settings.FollowCursor) { runUntil = 0; walkTarget = null; if (action is "running-left" or "running-right") Play("idle", false); }
        if (owner.Settings.X is double x && owner.Settings.Y is double y && !IsVisible) Position = new PixelPoint((int)x, (int)y);
        Clamp(); ConfigureNative(false);
        RefreshHoliday(true);
    }
    public void SelectSkin()
    {
        // DesktopController already cancelled using the old skin before changing
        // its identity. Clear ownership here without publishing the old clip
        // through the new skin's resource folder.
        owner.Holidays.CancelHover(); holidayPlayback.Cancel(); holidayTransition.FinishImmediately();
        committedOutfit = null; LeaveHolidayHover(); clouds.InvalidateVisual();
        var previousCompanion=RelationshipIdentity.Resolve(owner.DataDirectory,sprite.Skin.PickerKey,sprite.Skin.OfficialCharacterId,sprite.Skin.Id);
        if(previousCompanion!=owner.RelationshipCompanionId)owner.Relationships.InvalidateFeedback();
        drop.Cancel(); sprite.ResetFrames(); assets.Dispose(); sprite.Skin = owner.Skin; Play("idle", false); ContextMenu = BuildMenu();
        if(previousCompanion!=owner.RelationshipCompanionId)owner.Relationships.EnqueueVisit(owner.RelationshipCompanionId,IsPetVisible);
        RefreshHoliday(true);
    }
    // Headless windows can expose an IPlatformHandle with a zero Handle even on
    // macOS. Only an attached native window can use desktop input and policies.
    private IntPtr NativeWindowHandle => OperatingSystem.IsMacOS() ? TryGetPlatformHandle()?.Handle ?? IntPtr.Zero : IntPtr.Zero;
    private void ConfigureNative(bool ignored)
    {
        if (!IsVisible) return;
        var handle = NativeWindowHandle;
        if (handle == IntPtr.Zero) return;
        MacServices.ConfigureWindow(handle, owner.Settings.Topmost, ignored, true); nativeIgnored = ignored;
    }
    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu(); var items = new List<object>();
        void Add(System.Func<string> label, Action command) { var item = LocalizedUi.With(new MenuItem { Header = label() },"Header",()=> (label())); item.Click += (_, _) => command(); items.Add(item); }
        Add(()=> (TextResources.Get("petWindow.buildMenu.text")), () => owner.ShowChat(true)); Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeA")), () => owner.ShowChat(false));
        Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeB")), ShowPalette); Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeC")), () => UserInteract("feeding")); Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeD")), () => UserInteract("petting"));
        Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeE")), () => { if (action == "sleeping") {Play("idle", false);RecordRelationshipInteraction(true,true);} else UserInteract("sleeping"); });
        Add(()=> (owner.Skin.SpecialAction), () => UserInteract("special")); if (SpriteAssets.HasExtra(owner.Skin,"special2")) Add(()=> (owner.Skin.SpecialAction2), () => UserInteract("special2"));
        Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeF")), () => Paused = !Paused); Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeG")), Hide);
        items.Add(new Separator());
        foreach (var skin in owner.Skins)
        {
            var item = LocalizedUi.With(new MenuItem { Header = TextResources.Format("petWindow.buildMenu.headerSkinName",skin.DisplayName), Icon = new AvatarView { Assets = assets, Skin = skin, Width = 24, Height = 24 } },"Header",()=>(TextResources.Format("petWindow.buildMenu.headerSkinName",skin.DisplayName)));
            item.Click += (_, _) => owner.Run(() => owner.SelectSkinAsync(skin, CompanionSelectionOrigin.PetMenu)); items.Add(item);
        }
        Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeH")), owner.ShowSettings); Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeI")), owner.ShowGuide); Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeJ")), owner.ShowAbout); Add(()=> (TextResources.Get("petWindow.buildMenu.text.alternativeK")), () => owner.Run(owner.ExitAsync));
        menu.ItemsSource = items; return menu;
    }
    public void ShowPalette()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*") };
        var choices = new (string, Action)[] { (TextResources.Get("petWindow.showPalette.choices"), () => owner.ShowChat(true)), (TextResources.Get("petWindow.showPalette.choices.alternativeA"), () => UserInteract("feeding")), (TextResources.Get("petWindow.showPalette.choices.alternativeB"), () => UserInteract("sleeping")), (TextResources.Get("petWindow.showPalette.choices.alternativeC"), () => UserInteract(SpriteAssets.HasExtra(owner.Skin,"special2") && random.Next(2) == 0 ? "special2" : "special")) };
        var window = LocalizedUi.Window(()=>(TextResources.Get("petWindow.showPalette.window")), grid, 290, 280); window.Topmost = true;
        for (int i = 0; i < choices.Length; i++) { var c = choices[i]; var b = Ui.Button(c.Item1, () => { c.Item2(); window.Close(); }); b.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; b.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch; b.Margin = new Thickness(5); Grid.SetColumn(b, i % 2); Grid.SetRow(b, i / 2); grid.Children.Add(b); }
        window.Show();
    }
    public void UserInteract(string name)=>Interact(name,true);
    public void Interact(string name,bool userOrigin=false)
    {
        if (name == "holiday" || name.StartsWith("holiday-", StringComparison.Ordinal)) return;
        if (owner.GuideLocked || dragging) return;
        if (!AnimationPlayer.States.Contains(name) && !SpriteAssets.Interactions.Contains(name) && !SpriteAssets.HasExtra(owner.Skin,name)) return;
        CancelHoliday();
        DismissGreeting();
        sleep.Touch(clock.Elapsed.TotalSeconds); wanderAt = WanderSchedule.Next(clock.Elapsed.TotalSeconds, random.NextDouble()); runUntil = 0; walkTarget = null; drop.Cancel();
        if (!owner.Skin.BuiltIn && !owner.Skin.Clips.ContainsKey(name) && !SpriteAssets.HasExtra(owner.Skin,name) && !SpriteAssets.Interactions.Contains(name)) return;
        Play(name, (owner.Skin.BuiltIn || owner.Skin.Alternates.ContainsKey(name)) && random.Next(2) == 0);
        RecordRelationshipInteraction(userOrigin,true);
    }
    private void Play(string name, bool useOriginal)
    {
        relationshipActionRevision++;playback.Play(name, useOriginal); PublishHolidayFrame(playback.Clip, playback.Frame, OriginalMirror());
        sprite.InvalidateVisual();
    }
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (owner.GuideLocked || !sprite.Hits(e.GetPosition(sprite))) return;
        CancelHoliday();
        DismissGreeting();
        sleep.Touch(clock.Elapsed.TotalSeconds); wanderAt = WanderSchedule.Next(clock.Elapsed.TotalSeconds, random.NextDouble());
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || owner.Settings.LockPosition) return;
        dragging = true; moved = false; press = e.GetPosition(this); start = Position; drop.Cancel();
        Play("lifted", (owner.Skin.BuiltIn || owner.Skin.Alternates.ContainsKey("lifted")) && random.Next(2) == 0);
        e.Pointer.Capture(this); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (!dragging)
        {
            var position = e.GetPosition(sprite);
            var inside = position.X >= 0 && position.Y >= 0 && position.X < sprite.Bounds.Width && position.Y < sprite.Bounds.Height;
            if (!inside) LeaveHolidayHover();
            else if (hoverEntry.TryEnter(true, sprite.Hits(position)) && action == "idle" && !owner.Session.IsBusy && !owner.GuideLocked)
            {
                if (!TryHolidayHover()) Interact("petting");
            }
            return;
        }
        var local = e.GetPosition(this); var scale = RenderScaling;
        var dx = (local.X - press.X) * scale; var dy = (local.Y - press.Y) * scale;
        if (Math.Abs(dx) + Math.Abs(dy) > 3) moved = true;
        Position = new PixelPoint(Position.X + (int)Math.Round(dx), Position.Y + (int)Math.Round(dy)); Clamp();
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging) return;
        var clicked=!moved;FinishDrag();if(clicked)RecordRelationshipInteraction(true,true); e.Pointer.Capture(null); e.Handled = true;
    }
    private void FinishDrag()
    {
        dragging = false;
        if (!moved) { Play("poked", false); return; }
        var stumble = reaction.TryStumble(true, owner.Settings.StumbleEnabled, SpriteAssets.HasExtra(owner.Skin,"stumble"), clock.Elapsed.TotalSeconds);
        playback.Land(stumble); PublishHolidayFrame(playback.Clip, playback.Frame, OriginalMirror());
        var area = WorkArea();
        drop.Start(Position.Y, Height * RenderScaling, area.Bottom);
        SavePosition();
    }
    private PixelRect WorkArea() => (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
    private void ScreensChanged(object? sender, EventArgs e) { drop.Cancel(); runUntil = 0; Clamp(); SavePosition(); }
    public void Clamp()
    {
        var area = WorkArea(); var w = (int)(Width * RenderScaling); var h = (int)(Height * RenderScaling);
        var next = new PixelPoint(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - w)), Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - h)));
        if (Position != next) Position = next;
    }
    private void SavePosition() { owner.Settings.X = Position.X; owner.Settings.Y = Position.Y; owner.Save(); }
    private void Tick()
    {
        var now = clock.Elapsed.TotalSeconds; var gap = now - lastTime; var dt = Math.Clamp(gap, 0, .125); lastTime = now;
        if(IsPetVisible&&now>=keepsakeFrameAt){keepsakeFrameAt=now+1;sprite.InvalidateVisual();}
        var nativeWindow = NativeWindowHandle;
        var hasNativeWindow = nativeWindow != IntPtr.Zero;
        var wasInteractive = sessionInteractive;
        bool lifecycleChanged = false;
        if (hasNativeWindow)
        {
            sessionInteractive = MacServices.IsSessionInteractive;
            var latestLifecycle = MacServices.SessionLifecycleRevision;
            lifecycleChanged = latestLifecycle != lifecycleRevision; lifecycleRevision = latestLifecycle;
            ObserveFeedbackSession();
        }
        if (gap > 1 || !sessionInteractive || lifecycleChanged) { CancelHoliday(); DismissGreeting(); }
        RefreshHoliday(gap > 1 || lifecycleChanged || wasInteractive != sessionInteractive);
        if (hasNativeWindow && IsVisible && now >= policyAt)
        {
            policyAt = now + 1;
            ApplyFullscreenVisibility(owner.Settings.HideOnFullscreen && MacServices.IsFrontApplicationFullscreen());
        }
        if (now >= greetingProbeAt) { greetingProbeAt = now + 1; ProbeGreeting(); }
        if (fullscreenHidden || !sessionInteractive) { if (!nativeIgnored) ConfigureNative(true); return; }
        if (!IsVisible) return;
        if (Paused)
        {
            if (hasNativeWindow) { var ignored = owner.Settings.ClickThrough || !sprite.Hits(LocalPointer(nativeWindow)); if (ignored != nativeIgnored) ConfigureNative(ignored); }
            return;
        }
        ApplyHolidayBusinessAction();
        if (drop.Active) { drop.Advance(dt); Position = new PixelPoint(Position.X, (int)Math.Round(drop.Position)); if (!drop.Active) SavePosition(); }
        if (!dragging)
        {
            var idle = hasNativeWindow ? MacServices.GetIdleTime().TotalSeconds : 0;
            if (sleep.TrySleep(now, idle, !owner.Session.IsBusy && !owner.Live.IsActive && !owner.Dictation.IsBusy && action == "idle" && !owner.HasOpenPanel, owner.Settings.AutoSleepEnabled)) Interact("sleeping");
        }
        if (!dragging && !drop.Active && action is "idle" or "running-right" or "running-left" && !owner.Session.IsBusy && !owner.Live.IsActive && !owner.Dictation.IsBusy && !owner.HasOpenPanel && !owner.Settings.LockPosition && !owner.Settings.QuietMode)
        {
            if (owner.Settings.FollowCursor && hasNativeWindow)
            {
                var pointer = LocalPointer(nativeWindow); var dx = pointer.X - Width / 2;
                if (Math.Abs(dx) > Width) { direction = Math.Sign(dx); runUntil = now + .2; walkTarget = null; }
            }
            if (owner.Settings.Wander && now >= wanderAt && now >= runUntil)
            {
                direction = random.Next(2) == 0 ? -1 : 1;
                var area = WorkArea();
                walkTarget = Math.Clamp(Position.X + direction * (80 + random.Next(120)) * RenderScaling, area.X, Math.Max(area.X, area.Right - Width * RenderScaling));
                if (Math.Abs(walkTarget.Value - Position.X) > 10 * RenderScaling) { runUntil = double.PositiveInfinity; motionX = Position.X; }
                wanderAt = WanderSchedule.Next(now, random.NextDouble());
            }
            if (now < runUntil)
            {
                var desired = direction < 0 ? "running-left" : "running-right";
                if (action != desired) { Play(desired, false); motionX = Position.X; }
                var step = 70 * owner.Settings.Scale * dt * RenderScaling;
                if (walkTarget is double target && Math.Abs(target - motionX) <= step) { motionX = target; runUntil = now; walkTarget = null; wanderAt = WanderSchedule.Next(now, random.NextDouble()); }
                else motionX += direction * step;
                Position = new PixelPoint((int)Math.Round(motionX), Position.Y); var before = Position; Clamp();
                if (Position != before) { motionX = Position.X; runUntil = now; }
            }
            else if (action != "idle") { Play("idle", false); SavePosition(); }
        }
        else if (action is "running-right" or "running-left") { runUntil = 0; walkTarget = null; Play("idle", false); SavePosition(); }
        playback.Advance(dt);
        var visibleFrame = committedOutfit == null && owner.Skin.BuiltIn && (owner.Skin.OfficialCharacterId ?? owner.Skin.Id) == "marfu" && action is "running-right" or "running-left" ? new[] { 0, 2, 3, 2, 4, 2, 6, 2 }[playback.Frame % 8] : playback.Frame;
        PublishHolidayFrame(playback.Clip, visibleFrame, OriginalMirror());
        AdvanceHoliday(dt);
        owner.ApplyPendingCompanionAppearance();
        if (hasNativeWindow)
        {
            var local = LocalPointer(nativeWindow);
            if (!holidayPlayback.Active && action == "idle" && !owner.Settings.Wander && !owner.Settings.FollowCursor)
            {
                gaze.Advance(local.X - Width / 2, local.Y - Height / 2, dt);
                PublishHolidayGaze(gaze.Frame(1));
            }
            else if (action == "idle" && sprite.Clip?.Frames == 16) Play("idle", false);
            var ignored = !dragging && (owner.Settings.ClickThrough || !sprite.Hits(local));
            if (ignored != nativeIgnored) ConfigureNative(ignored);
        }
        sprite.InvalidateVisual();
    }
    private static Point LocalPointer(IntPtr handle)
    {
        var point = MacServices.GetPointerRelativeToWindow(handle);
        return new Point(point.X, point.Y);
    }
    private double greetingProbeAt;
    private void ApplyFullscreenVisibility(bool hidden)
    {
        var wasHidden=fullscreenHidden;
        if(hidden!=wasHidden)owner.Relationships.InvalidateFeedback();
        if(hidden&&!wasHidden)DismissGreeting();
        fullscreenHidden=hidden;Opacity=hidden?0:1;
        if(wasHidden&&IsPetVisible)owner.Relationships.EnqueueVisit(owner.RelationshipCompanionId,true);
        if(fullscreenHidden)CancelHoliday();
    }
    public void ConfigureGreetings()
    {
        ObserveFeedbackQuiet();
        greetingSchedule = new GreetingSchedule(DateTime.UtcNow, owner.Settings.GreetingMinMinutes, owner.Settings.GreetingMaxMinutes, random.NextDouble);
        DismissGreeting();
    }
    private bool GreetingAllowed => sessionInteractive && IsVisible && !fullscreenHidden && !Paused && !dragging && !holidayPlayback.Active && !holidayTransition.Active && action == "idle" && owner.Settings.GreetingEnabled && !owner.Settings.QuietMode && !owner.HasOpenPanel && !owner.HolidayBusinessBusy&&!owner.DataMaintenance.IsMaintenanceActive&&!owner.RelationshipNoticeVisible;
    private void ProbeGreeting()
    {
        if (!GreetingAllowed) { DismissGreeting(false); greetingSchedule.TryTake(DateTime.UtcNow, false); return; }
        if (greetingRequest == null && greetingBubble == null && greetingSchedule.TryTake(DateTime.UtcNow, true)) owner.Run(ShowGreetingAsync);
    }
    private async Task ShowGreetingAsync()
    {
        if(!GreetingAllowed)return;
        var cancellation = new CancellationTokenSource(); greetingRequest = cancellation;
        var id = owner.Skin.Id; var name = owner.Skin.Name;
        var capturedSkin=owner.Skin;var relationshipId=owner.RelationshipCompanionId;var capturedPack=owner.RelationshipPack;int epoch=relationshipContentEpoch,settingsVersion=owner.Relationships.SettingsVersion;var settings=owner.Settings.Copy();
        RefreshHoliday(true);
        var holiday = owner.Holidays.CreateGreetingContext(random.NextDouble());
        var revision = owner.Holidays.Revision; var date = HolidayLocalNow().Date;
        var aiEnabled = owner.Settings.AiGreetingEnabled;
        try
        {
            var service = new GreetingService();
            var stage=await new RelationshipExperience(owner.Relationships).ReadAsync(relationshipId,capturedPack,TextResources.CurrentLanguage);var stageWelcome=RelationshipWelcome(stage);
            string text;
            var phrase = random.Next(GreetingPhrases.Count);
            IList<ChatMessage>? memory = null;
            try { memory = new CompanionMemory(() => owner.Session).Capture(id); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            if (memory == null) text = GreetingService.Fallback(id, name, HolidayLocalNow(), phrase, recentGreetings.ToList(), holiday,stageWelcome);
            else text = await service.GenerateAsync(settings, id, name, memory, HolidayLocalNow(), phrase, recentGreetings.ToList(), cancellation.Token, holiday,stageWelcome,stage.AiContext);
            RefreshHoliday(true);
            if (cancellation.IsCancellationRequested || epoch!=relationshipContentEpoch||!ReferenceEquals(capturedSkin,owner.Skin)||relationshipId!=owner.RelationshipCompanionId||settingsVersion!=owner.Relationships.SettingsVersion||settings.SystemPrompt!=owner.Settings.SystemPrompt||!SameGreetingConnection(settings.Text,owner.Settings.Text)||owner.Skin.Id != id || !GreetingAllowed || HolidayLocalNow().Date != date || owner.Holidays.Revision != revision || owner.Settings.AiGreetingEnabled != aiEnabled || holiday != null && !owner.Holidays.IsGreetingCurrent(holiday)) return;
            recentGreetings.Add(text); if (recentGreetings.Count > 6) recentGreetings.RemoveAt(0);
            var message=Ui.Text(text);message.Name="GreetingMessage";var bubble = LocalizedUi.With(new Window { Width = 320, Height = 115, CanResize = false, ShowActivated=false,ShowInTaskbar = false, Topmost = owner.Settings.Topmost, WindowDecorations = WindowDecorations.None, Title = TextResources.Format("petWindow.showGreetingAsync.titleName",name), Content = new Border { Padding = new Thickness(12), Background = Brushes.DimGray, Child = Ui.Column(message, LocalizedUi.Button(()=>(TextResources.Get("petWindow.showGreetingAsync.text")), () => { DismissGreeting(); owner.ShowChat(true); })) } },"Title",()=>(TextResources.Format("petWindow.showGreetingAsync.titleName",name)));
            greetingBubble = bubble;
            var area = WorkArea(); bubble.Position = new PixelPoint(Math.Clamp(Position.X - 50, area.X, Math.Max(area.X, area.Right - (int)(320 * RenderScaling))), Math.Max(area.Y, Position.Y - (int)(120 * RenderScaling)));
            bubble.Show();
            if(bubble.IsVisible)RememberRelationshipWelcome(stage,text);
            if (holiday != null) owner.Holidays.MarkGreetingShown(holiday);
            var dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) }; dismiss.Tick += (_, _) => { dismiss.Stop(); if (ReferenceEquals(greetingBubble, bubble)) DismissGreeting(); };
            bubble.Closed += (_, _) => dismiss.Stop(); dismiss.Start();
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(greetingRequest, cancellation)) greetingRequest = null; cancellation.Dispose(); }
    }
    private void DismissGreeting(bool invalidateRelationship=true) { if(invalidateRelationship)relationshipContentEpoch++;greetingRequest?.Cancel(); var bubble = greetingBubble; greetingBubble = null; bubble?.Close(); }
    private static bool SameGreetingConnection(ConnectionSettings a,ConnectionSettings b)=>a.BaseUrl==b.BaseUrl&&a.Model==b.Model&&a.EncryptedKey==b.EncryptedKey&&a.Protocol==b.Protocol&&a.UseProxy==b.UseProxy&&a.ProxyAddress==b.ProxyAddress;
}
