using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Dafeiyu;

namespace DeskPet.Desktop;

// A viewport entry is distinct from the changing transparent outline of a frame.
internal sealed class HolidayHoverEntry
{
    private bool consumed;
    internal bool TryEnter(bool insideViewport, bool hitsCharacter)
    {
        if (!insideViewport) { Leave(); return false; }
        if (!hitsCharacter || consumed) return false;
        consumed = true; return true;
    }
    internal void Leave() => consumed = false;
}

internal sealed partial class PetWindow
{
    private OutfitDefinition? committedOutfit;
    private readonly HolidayPlayback holidayPlayback = new();
    private readonly HolidayTransition holidayTransition = new();
    private readonly HolidayHoverEntry hoverEntry = new();
    private HolidayCloudView clouds = null!;
    private double holidayProbeAt;
    private string? rejectedOutfit;
    private bool refreshingHoliday, disposedHoliday, rejectingHoliday;
    private bool sessionInteractive = true;
    internal bool KeepsakeInteractive=>sessionInteractive&&!owner.IsExiting;
    private ulong lifecycleRevision;
    private int observedTerminalRevision;
    internal Func<DateTime> HolidayLocalNow { get; set; } = () => DateTime.Now;
    internal Func<double>? HolidayMonotonicNow { get; set; }
    internal Func<double>? HolidayHoverRandom { get; set; }
    private double HolidaySeconds => HolidayMonotonicNow?.Invoke() ?? clock.Elapsed.TotalSeconds;
    private bool HolidaySafe => sessionInteractive && IsVisible && !fullscreenHidden && !Paused && !dragging && !drop.Active && !owner.Settings.QuietMode && !owner.Settings.ClickThrough && !owner.HasOpenPanel && !owner.HolidayBusinessBusy && action == "idle";
    private string ResourceVersion => owner.Skin.PackVersion ?? "builtin-1";
    private static string? OutfitKey(OutfitDefinition? outfit) => outfit == null ? null : outfit.CharacterId + ":" + outfit.OutfitId + ":" + outfit.Version + ":" + outfit.SourceKey;

    private void InitializeHoliday()
    {
        clouds = new HolidayCloudView(holidayTransition) { IsHitTestVisible = false };
        var layers = new Grid(); layers.Children.Add(sprite); layers.Children.Add(clouds); Content = layers;
        owner.Holidays.Changed += HolidayChanged;
        owner.Changed += HolidayBusinessChanged;
        sprite.FrameRendered += HolidayFrameRendered;
        sprite.HolidayFrameFailed += RejectRenderedHolidayFrame;
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) { CancelHoliday(); DismissGreeting(); } };
    }
    private void HolidayChanged(object? sender, EventArgs args)
    {
        if (disposedHoliday || rejectingHoliday) return;
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { Avalonia.Threading.Dispatcher.UIThread.Post(() => HolidayChanged(sender, args)); return; }
        SynchronizeHolidayAppearance();
    }
    private void HolidayBusinessChanged()
    {
        if (disposedHoliday) return;
        ApplyHolidayBusinessAction();
        if (!HolidaySafe) CancelHoliday();
        RefreshHoliday(true);
    }
    private void ApplyHolidayBusinessAction()
    {
        if (dragging || drop.Active) return;
        if (owner.Session.IsBusy)
        {
            var requested = owner.Session.RuntimeAction;
            if (AnimationPlayer.States.Contains(requested) && action != requested) { CancelHoliday(); Play(requested, false); }
        }
        else if (observedTerminalRevision != owner.Session.TerminalRevision)
        {
            observedTerminalRevision = owner.Session.TerminalRevision;
            if (owner.Session.RuntimeAction is "review" or "failed") { CancelHoliday(); Play(owner.Session.RuntimeAction, false); }
        }
    }
    private void RefreshHoliday(bool force = false)
    {
        if (disposedHoliday || refreshingHoliday) return;
        if ((holidayPlayback.Active || holidayTransition.Active) && !HolidaySafe) CancelHoliday();
        var now = HolidaySeconds;
        if (!force && now < holidayProbeAt) return;
        holidayProbeAt = now + 1;
        refreshingHoliday = true;
        try
        {
            owner.Holidays.Refresh(new HolidayContext {
                PersonaId = owner.Skin.Id, CharacterId = owner.Skin.BuiltinCharacterId,
                LocalTime = HolidayLocalNow(), MonotonicSeconds = now, ResourceVersion = ResourceVersion,
                CanPlayEasterEgg = HolidaySafe && OutfitKey(committedOutfit) == OutfitKey(owner.Holidays.EffectiveOutfit) && committedOutfit != null && rejectedOutfit != OutfitKey(committedOutfit),
                CanAnimateTransition = HolidaySafe
            });
            SynchronizeHolidayAppearance();
            if (HolidaySafe)
            {
                var notice = owner.Holidays.TakeUnlockNotice();
                if (!string.IsNullOrEmpty(notice))
                {
                    var bubble = LocalizedUi.Window(()=>(TextResources.Get("petHoliday.refreshHoliday.window")), Ui.Text(notice), 350, 150);
                    bubble.ShowActivated = false; bubble.ShowInTaskbar = false; bubble.Topmost = owner.Settings.Topmost; bubble.Show();
                    var dismiss = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                    dismiss.Tick += (_, _) => { dismiss.Stop(); bubble.Close(); }; bubble.Closed += (_, _) => dismiss.Stop(); dismiss.Start();
                }
            }
        }
        finally { refreshingHoliday = false; }
    }
    private void SynchronizeHolidayAppearance()
    {
        var desired = owner.Holidays.EffectiveOutfit;
        if (holidayPlayback.Active && (holidayPlayback.Ticket.Revision != owner.Holidays.Revision || !HolidaySafe)) CancelHolidayPlayback();
        if (OutfitKey(desired) == OutfitKey(committedOutfit))
        {
            if (holidayTransition.Active && holidayTransition.TargetOutfitId != desired?.OutfitId) { holidayTransition.FinishImmediately(); clouds.InvalidateVisual(); }
            return;
        }
        if (owner.Holidays.TransitionRequested && HolidaySafe)
        {
            if (!holidayTransition.Active || holidayTransition.TargetOutfitId != desired?.OutfitId) holidayTransition.Start(desired?.OutfitId);
            owner.Holidays.AcknowledgeTransition();
        }
        else if (!holidayTransition.Active || !HolidaySafe)
        {
            holidayTransition.FinishImmediately(); owner.Holidays.AcknowledgeTransition(); CommitHolidayAppearance();
        }
    }
    private void CommitHolidayAppearance()
    {
        var desired = owner.Holidays.EffectiveOutfit;
        try
        {
            // Decode the next committed frame before changing either drawing or hit data.
            var clip = assets.Resolve(owner.Skin, action, original, desired);
            assets.PrepareFrame(owner.Skin, clip, Math.Min(playback.Frame, clip.Frames - 1));
            committedOutfit = desired;
            playback.RefreshAppearance();
            PublishHolidayFrame(playback.Clip, playback.Frame, OriginalMirror()); sprite.InvalidateVisual();
        }
        catch (Exception e) when (desired != null && IsHolidayDecodeFailure(e))
        {
            RejectHolidayOutfit(desired);
            playback.RefreshAppearance(); PublishHolidayFrame(playback.Clip, playback.Frame, OriginalMirror()); sprite.InvalidateVisual();
        }
    }
    private static bool IsHolidayDecodeFailure(Exception error) => error is InvalidDataException or IOException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException;
    private bool OriginalMirror() => committedOutfit == null && owner.Skin.BuiltIn && (owner.Skin.OfficialCharacterId ?? owner.Skin.Id) is "dafeiyu" or "marfu" && action == "running-left";
    private void RejectHolidayOutfit(OutfitDefinition outfit)
    {
        if (rejectingHoliday) return;
        rejectingHoliday = true;
        try
        {
            rejectedOutfit = OutfitKey(outfit); committedOutfit = null;
            owner.Holidays.CancelHover(); holidayPlayback.Cancel(); holidayTransition.FinishImmediately();
            DismissGreeting(); owner.Holidays.ReportUnavailable(outfit); clouds.InvalidateVisual();
        }
        finally { rejectingHoliday = false; }
    }
    private ClipSpec ResolveHolidayAction(string name, bool useOriginal)
    {
        var outfit = committedOutfit;
        try
        {
            var clip = assets.Resolve(owner.Skin, name, useOriginal, outfit);
            assets.PrepareFrame(owner.Skin, clip, 0); return clip;
        }
        catch (Exception error) when (outfit != null && IsHolidayDecodeFailure(error))
        {
            RejectHolidayOutfit(outfit); return assets.Resolve(owner.Skin, name, useOriginal);
        }
    }
    private void PublishHolidayFrame(ClipSpec clip, int frame, bool mirror, bool gazeFrame = false)
    {
        var outfit = committedOutfit;
        try { assets.PrepareFrame(owner.Skin, clip, frame); }
        catch (Exception error) when (outfit != null && SpriteAssets.IsHolidayClip(clip) && IsHolidayDecodeFailure(error))
        {
            RejectHolidayOutfit(outfit);
            playback.RefreshAppearance();
            clip = gazeFrame ? assets.Gaze(owner.Skin) : playback.Clip;
            frame = gazeFrame ? Math.Min(frame, clip.Frames - 1) : playback.Frame;
            mirror = gazeFrame ? false : OriginalMirror();
            assets.PrepareFrame(owner.Skin, clip, frame);
        }
        // Single UI-thread commit after decode and alpha validation succeed.
        sprite.Clip = clip; sprite.Frame = frame; sprite.MirrorX = mirror;
    }
    private void RejectRenderedHolidayFrame(Exception error)
    {
        var outfit = committedOutfit;
        if (outfit == null) return;
        RejectHolidayOutfit(outfit); playback.RefreshAppearance();
        PublishHolidayFrame(playback.Clip, playback.Frame, OriginalMirror()); sprite.InvalidateVisual();
    }
    private bool TryHolidayHover()
    {
        RefreshHoliday(true);
        if (!HolidaySafe || holidayTransition.Active || committedOutfit == null || rejectedOutfit == OutfitKey(committedOutfit)) return false;
        var ticket = owner.Holidays.TryBeginHover(HolidayHoverRandom?.Invoke() ?? random.NextDouble());
        if (ticket == null) return false;
        try
        {
            var clip = SpriteAssets.OutfitClip(committedOutfit, ticket.Clip);
            assets.PrepareFrame(owner.Skin, clip, 0);
            holidayPlayback.Start(ticket); PublishHolidayFrame(clip, 0, false);
            sprite.InvalidateVisual(); return true;
        }
        catch (Exception e) when (IsHolidayDecodeFailure(e)) { var outfit = committedOutfit; if (outfit != null) RejectHolidayOutfit(outfit); CancelHolidayPlayback(); return false; }
    }
    private void HolidayFrameRendered()
    {
        _=owner.ObserveHolidayEncounter(IsPetVisible&&sprite.Clip!=null,HolidayEncounterKind.VisibleBuddy);
        if (!holidayPlayback.Active || holidayPlayback.FirstFrameRendered || sprite.Frame != 0 || committedOutfit == null || sprite.Clip?.File != SpriteAssets.OutfitClip(committedOutfit, holidayPlayback.Ticket.Clip).File) return;
        holidayPlayback.MarkFirstFrameRendered(); owner.Holidays.MarkHoverFirstFrame(holidayPlayback.Ticket);
    }
    private void PublishHolidayGaze(int frame) => PublishHolidayFrame(assets.Gaze(owner.Skin, committedOutfit), frame, false, true);
    private void AdvanceHoliday(double dt)
    {
        if (holidayTransition.Active)
        {
            holidayTransition.Advance(dt);
            if (holidayTransition.TakeCommit()) CommitHolidayAppearance();
            clouds.InvalidateVisual();
        }
        if (!holidayPlayback.Active) return;
        holidayPlayback.Advance(dt);
        if (holidayPlayback.Completed)
        {
            var ticket = holidayPlayback.Ticket;
            // Date/busy/identity may have changed within the normal one-second probe.
            // Keep the original ticket: refresh can cancel and clear the player.
            RefreshHoliday(true);
            owner.Holidays.CompleteHover(ticket); CancelHolidayPlayback();
        }
        else
        {
            PublishHolidayFrame(SpriteAssets.OutfitClip(committedOutfit!, holidayPlayback.Ticket.Clip), holidayPlayback.Frame, false);
        }
    }
    private void CancelHolidayPlayback()
    {
        owner.Holidays.CancelHover(); holidayPlayback.Cancel();
        if (committedOutfit == null && SpriteAssets.IsHolidayClip(playback.Clip)) playback.RefreshAppearance();
        PublishHolidayFrame(playback.Clip, playback.Frame, OriginalMirror());
    }
    private void CancelHoliday()
    {
        CancelHolidayPlayback();
        holidayTransition.FinishImmediately();
        if (clouds != null) { CommitHolidayAppearance(); clouds.InvalidateVisual(); }
    }
    internal void CancelHolidayForSkinChange() { CancelHoliday(); DismissGreeting(); }
    internal void ProbeHolidayForTest() => RefreshHoliday(true);
    internal OutfitDefinition? CommittedHolidayOutfit => committedOutfit;
    private void LeaveHolidayHover() { hoverEntry.Leave(); owner.Holidays.LeaveHover(); }
    private void DisposeHoliday()
    {
        disposedHoliday = true; CancelHoliday();
        owner.Holidays.Changed -= HolidayChanged; owner.Changed -= HolidayBusinessChanged; sprite.FrameRendered -= HolidayFrameRendered; sprite.HolidayFrameFailed -= RejectRenderedHolidayFrame;
    }
}

internal sealed class HolidayCloudView(HolidayTransition transition) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!transition.Active) return;
        foreach (var cloud in transition.Clouds)
        {
            var brush = new RadialGradientBrush { GradientStops = new GradientStops { new(Colors.White, 0), new(Color.FromArgb(225, 221, 231, 244), .65), new(Colors.Transparent, 1) } };
            using var opacity = context.PushOpacity(cloud.Opacity);
            context.DrawEllipse(brush, null, new Point(cloud.X * Bounds.Width, cloud.Y * Bounds.Height), cloud.Radius * Bounds.Width, cloud.Radius * Bounds.Height);
        }
    }
}
