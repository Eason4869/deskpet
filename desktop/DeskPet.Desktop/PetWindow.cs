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
    private Point press;
    private PixelPoint start;
    private double lastTime, wanderAt, runUntil, policyAt;
    private double? walkTarget;
    private double motionX;
    private int direction = 1;
    private GreetingSchedule greetingSchedule;
    private CancellationTokenSource? greetingRequest;
    private Window? greetingBubble;
    private readonly List<string> recentGreetings = new();
    private bool paused;
    internal bool CompanionSourceRefreshSafe => !dragging && !drop.Active && !holidayPlayback.Active && !holidayTransition.Active && action == "idle";
    public bool Paused { get => paused; set { paused = value; if (value) CancelHoliday(); RefreshHoliday(true); } }
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
        Title = "DeskPet";
        sprite = new SpriteView { Assets = assets, Skin = owner.Skin };
        playback = new InteractionPlayback(ResolveHolidayAction);
        InitializeHoliday();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += (_, _) => { if (dragging) FinishDrag(); };
        PointerExited += (_, _) => LeaveHolidayHover();
        Screens.Changed += ScreensChanged;
        DoubleTapped += (_, _) => owner.ShowChat(true);
        ContextMenu = BuildMenu();
        Opened += (_, _) => { ApplySettings(); Clamp(); ConfigureNative(false); };
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        Closed += (_, _) => { timer.Stop(); Screens.Changed -= ScreensChanged; DisposeHoliday(); DismissGreeting(); assets.Dispose(); };
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
        drop.Cancel(); assets.Dispose(); sprite.Skin = owner.Skin; Play("idle", false); ContextMenu = BuildMenu();
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
        void Add(string label, Action command) { var item = new MenuItem { Header = label }; item.Click += (_, _) => command(); items.Add(item); }
        Add("快速对话", () => owner.ShowChat(true)); Add("完整聊天与队列", () => owner.ShowChat(false));
        Add("四格轮盘", ShowPalette); Add("喂零食", () => Interact("feeding")); Add("摸摸", () => Interact("petting"));
        Add("小憩 / 唤醒", () => { if (action == "sleeping") Play("idle", false); else Interact("sleeping"); });
        Add(owner.Skin.SpecialAction, () => Interact("special")); if (SpriteAssets.HasExtra(owner.Skin,"special2")) Add(owner.Skin.SpecialAction2, () => Interact("special2"));
        Add("暂停 / 继续", () => Paused = !Paused); Add("隐藏伙伴", Hide);
        items.Add(new Separator());
        foreach (var skin in owner.Skins)
        {
            var item = new MenuItem { Header = "切换 · " + skin.Name, Icon = new AvatarView { Assets = assets, Skin = skin, Width = 24, Height = 24 } };
            item.Click += (_, _) => owner.Run(() => owner.SelectSkinAsync(skin, CompanionSelectionOrigin.PetMenu)); items.Add(item);
        }
        Add("设置", owner.ShowSettings); Add("使用引导", owner.ShowGuide); Add("关于与更新", owner.ShowAbout); Add("退出", () => owner.Run(owner.ExitAsync));
        menu.ItemsSource = items; return menu;
    }
    public void ShowPalette()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*") };
        var choices = new (string, Action)[] { ("聊天", () => owner.ShowChat(true)), ("喂零食", () => Interact("feeding")), ("小憩", () => Interact("sleeping")), ("招牌动作", () => Interact(SpriteAssets.HasExtra(owner.Skin,"special2") && random.Next(2) == 0 ? "special2" : "special")) };
        var window = Ui.Window("伙伴互动", grid, 290, 280); window.Topmost = true;
        for (int i = 0; i < choices.Length; i++) { var c = choices[i]; var b = Ui.Button(c.Item1, () => { c.Item2(); window.Close(); }); b.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; b.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch; b.Margin = new Thickness(5); Grid.SetColumn(b, i % 2); Grid.SetRow(b, i / 2); grid.Children.Add(b); }
        window.Show();
    }
    public void Interact(string name)
    {
        if (name == "holiday" || name.StartsWith("holiday-", StringComparison.Ordinal)) return;
        if (owner.GuideLocked || dragging) return;
        CancelHoliday();
        DismissGreeting();
        sleep.Touch(clock.Elapsed.TotalSeconds); wanderAt = WanderSchedule.Next(clock.Elapsed.TotalSeconds, random.NextDouble()); runUntil = 0; walkTarget = null; drop.Cancel();
        if (!owner.Skin.BuiltIn && !owner.Skin.Clips.ContainsKey(name) && !SpriteAssets.HasExtra(owner.Skin,name) && !SpriteAssets.Interactions.Contains(name)) return;
        Play(name, (owner.Skin.BuiltIn || owner.Skin.Alternates.ContainsKey(name)) && random.Next(2) == 0);
    }
    private void Play(string name, bool useOriginal)
    {
        playback.Play(name, useOriginal); PublishHolidayFrame(playback.Clip, playback.Frame, OriginalMirror());
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
        FinishDrag(); e.Pointer.Capture(null); e.Handled = true;
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
        var nativeWindow = NativeWindowHandle;
        var hasNativeWindow = nativeWindow != IntPtr.Zero;
        var wasInteractive = sessionInteractive;
        bool lifecycleChanged = false;
        if (hasNativeWindow)
        {
            sessionInteractive = MacServices.IsSessionInteractive;
            var latestLifecycle = MacServices.SessionLifecycleRevision;
            lifecycleChanged = latestLifecycle != lifecycleRevision; lifecycleRevision = latestLifecycle;
        }
        if (gap > 1 || !sessionInteractive || lifecycleChanged) { CancelHoliday(); DismissGreeting(); }
        RefreshHoliday(gap > 1 || lifecycleChanged || wasInteractive != sessionInteractive);
        if (hasNativeWindow && IsVisible && now >= policyAt)
        {
            policyAt = now + 1;
            fullscreenHidden = owner.Settings.HideOnFullscreen && MacServices.IsFrontApplicationFullscreen();
            Opacity = fullscreenHidden ? 0 : 1;
            if (fullscreenHidden) CancelHoliday();
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
    public void ConfigureGreetings()
    {
        greetingSchedule = new GreetingSchedule(DateTime.UtcNow, owner.Settings.GreetingMinMinutes, owner.Settings.GreetingMaxMinutes, random.NextDouble);
        DismissGreeting();
    }
    private bool GreetingAllowed => sessionInteractive && IsVisible && !fullscreenHidden && !Paused && !dragging && !holidayPlayback.Active && !holidayTransition.Active && action == "idle" && owner.Settings.GreetingEnabled && !owner.Settings.QuietMode && !owner.HasOpenPanel && !owner.HolidayBusinessBusy;
    private void ProbeGreeting()
    {
        if (!GreetingAllowed) { DismissGreeting(); greetingSchedule.TryTake(DateTime.UtcNow, false); return; }
        if (greetingRequest == null && greetingBubble == null && greetingSchedule.TryTake(DateTime.UtcNow, true)) owner.Run(ShowGreetingAsync);
    }
    private async Task ShowGreetingAsync()
    {
        var cancellation = new CancellationTokenSource(); greetingRequest = cancellation;
        var id = owner.Skin.Id; var name = owner.Skin.Name;
        RefreshHoliday(true);
        var holiday = owner.Holidays.CreateGreetingContext(random.NextDouble());
        var revision = owner.Holidays.Revision; var date = HolidayLocalNow().Date;
        var aiEnabled = owner.Settings.AiGreetingEnabled;
        try
        {
            var service = new GreetingService();
            string text;
            var phrase = random.Next(GreetingPhrases.Count);
            IList<ChatMessage>? memory = null;
            try { memory = new CompanionMemory(() => owner.Session).Capture(id); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            if (memory == null) text = GreetingService.Fallback(id, name, HolidayLocalNow(), phrase, recentGreetings.ToList(), holiday);
            else text = await service.GenerateAsync(owner.Settings, id, name, memory, HolidayLocalNow(), phrase, recentGreetings.ToList(), cancellation.Token, holiday);
            RefreshHoliday(true);
            if (cancellation.IsCancellationRequested || owner.Skin.Id != id || !GreetingAllowed || HolidayLocalNow().Date != date || owner.Holidays.Revision != revision || owner.Settings.AiGreetingEnabled != aiEnabled || holiday != null && !owner.Holidays.IsGreetingCurrent(holiday)) return;
            recentGreetings.Add(text); if (recentGreetings.Count > 6) recentGreetings.RemoveAt(0);
            var bubble = new Window { Width = 320, Height = 115, CanResize = false, ShowInTaskbar = false, Topmost = owner.Settings.Topmost, WindowDecorations = WindowDecorations.None, Title = name + "的问候", Content = new Border { Padding = new Thickness(12), Background = Brushes.DimGray, Child = Ui.Column(Ui.Text(text), Ui.Button("聊两句", () => { DismissGreeting(); owner.ShowChat(true); })) } };
            greetingBubble = bubble;
            var area = WorkArea(); bubble.Position = new PixelPoint(Math.Clamp(Position.X - 50, area.X, Math.Max(area.X, area.Right - (int)(320 * RenderScaling))), Math.Max(area.Y, Position.Y - (int)(120 * RenderScaling)));
            bubble.Show();
            if (holiday != null) owner.Holidays.MarkGreetingShown(holiday);
            var dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) }; dismiss.Tick += (_, _) => { dismiss.Stop(); if (ReferenceEquals(greetingBubble, bubble)) DismissGreeting(); };
            bubble.Closed += (_, _) => dismiss.Stop(); dismiss.Start();
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(greetingRequest, cancellation)) greetingRequest = null; cancellation.Dispose(); }
    }
    private void DismissGreeting() { greetingRequest?.Cancel(); var bubble = greetingBubble; greetingBubble = null; bubble?.Close(); }
}
