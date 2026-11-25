using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed class LiquidThemeReveal : IDisposable
{
    private readonly Window window;
    private readonly Grid host;
    private readonly Control content;
    private readonly ThemeTitleSwitch? themeSwitch;
    private readonly Action<string> commit;
    private readonly Func<bool> reducedMotion;
    private readonly TimeSpan duration;
    private readonly Func<string> motion;
    private readonly LiquidThemeSoundBinding sound;
    private readonly Stopwatch clock = new();
    private SnapshotLayer? layer;
    private ThemeRevealPaths? paths;
    private TimeSpan activeDuration;
    private string activeEffect = ThemeTransitionMotion.Flow;
    private string? targetMode;
    private Control? settingsOrigin;
    private bool targetDark;
    private bool saveOnFinish;
    private bool systemAppearancePrepared;
    private int generation;
    internal Action? BeforeCapture { get; set; }

    internal LiquidThemeReveal(Window window, Grid host, Control content, ThemeTitleSwitch? themeSwitch,
        Action<string> commit, TimeSpan duration, Func<string> motion, Func<bool>? reducedMotion = null,
        BeginLiquidThemeSound? beginSound = null)
    {
        this.window = window;
        this.host = host;
        this.content = content;
        this.themeSwitch = themeSwitch;
        this.commit = commit;
        this.duration = duration;
        this.motion = motion;
        this.reducedMotion = reducedMotion ?? (() => MacServices.PrefersReducedMotion);
        sound = new LiquidThemeSoundBinding(beginSound);
        if (themeSwitch != null)
        {
            themeSwitch.Selected += ChangeTheme;
            themeSwitch.GettingFocus += PreservePointerFocus;
        }
        host.SizeChanged += Interrupt;
        window.ScalingChanged += Interrupt;
        window.Deactivated += Interrupt;
        window.PropertyChanged += WindowChanged;
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        window.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, handledEventsToo: true);
        window.Closed += Closed;
        DesktopTheme.ModeChanged += RefreshSwitch;
        DesktopTheme.SystemAppearanceChanging += PrepareSystemAppearance;
        DesktopTheme.AppearanceChanged += OnAppearanceChanged;
        ApplyWindowAppearance();
        themeSwitch?.Refresh(DesktopTheme.Mode, DesktopTheme.IsDark, false);
    }

    private void RefreshSwitch() => themeSwitch?.Refresh(DesktopTheme.Mode, DesktopTheme.IsDark,
        !reducedMotion());

    internal void ChangeTheme(string next, Control origin)
    {
        if (next == DesktopTheme.Mode) return;
        if (targetMode != null || systemAppearancePrepared) Finish();
        if (themeSwitch == null) settingsOrigin = origin;
        sound.Cancel();
        bool wasDark = DesktopTheme.IsDark;
        bool reduceMotion = reducedMotion();
        string effect = ThemeTransitionMotion.Resolve(motion());
        bool couldChangePalette = next == "system" || (next == "dark") != wasDark;
        if (couldChangePalette && !reduceMotion) Prepare(effect, origin);
        DesktopTheme.Apply(next);
        if (DesktopTheme.IsDark != wasDark && StartPrepared(next, true)) return;

        ClearLayer();
        commit(next);
        if (wasDark != DesktopTheme.IsDark) sound.Start(DesktopTheme.IsDark, duration, true, effect);
    }

    // Keep this window's theme resources on the old variant until the old snapshot has been taken.
    private void ApplyWindowAppearance() =>
        window.RequestedThemeVariant = DesktopTheme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

    private void OnAppearanceChanged()
    {
        ApplyWindowAppearance();
        RefreshSwitch();
        if (systemAppearancePrepared)
            Dispatcher.UIThread.Post(RevealSystemAppearance, DispatcherPriority.Render);
    }

    private void PrepareSystemAppearance()
    {
        Finish();
        if (reducedMotion()) return;
        Prepare(ThemeTransitionMotion.Resolve(motion()), themeSwitch?.SystemButton ?? settingsOrigin);
        systemAppearancePrepared = layer != null;
    }

    private void RevealSystemAppearance()
    {
        if (!systemAppearancePrepared) return;
        systemAppearancePrepared = false;
        if (DesktopTheme.Mode != "system" || !StartPrepared("system", false)) ClearLayer();
    }

    private void Prepare(string effect, Control? anchor)
    {
        if (effect == ThemeTransitionMotion.Off || duration <= TimeSpan.Zero || !content.IsEffectivelyVisible || window.WindowState == WindowState.Minimized ||
            content.Bounds.Width < 1 || content.Bounds.Height < 1 ||
            HasUncapturableSurface(content)) return;
        BeforeCapture?.Invoke();
        var origin = new Point(Math.Max(0, content.Bounds.Width - 42), 22);
        if (anchor is { IsEffectivelyVisible: true })
        {
            double originY = effect == ThemeTransitionMotion.Flow ? anchor.Bounds.Height - 2 : anchor.Bounds.Height / 2;
            origin = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, originY), content) ?? origin;
        }
        var before = Capture(window, content);
        if (before == null) return;
        paths = ThemeRevealPaths.Create(effect, content.Bounds.Width, content.Bounds.Height, origin.X, origin.Y);
        layer = new SnapshotLayer(before, content.Bounds.Size, paths);
        if (themeSwitch?.Capsule is { IsEffectivelyVisible: true } capsule && capsule.Bounds.Width > 0 && capsule.Bounds.Height > 0 &&
            capsule.TranslatePoint(default, content) is Point capsuleOrigin &&
            capsule.TranslatePoint(new Point(capsule.Bounds.Width, capsule.Bounds.Height), content) is Point capsuleEnd)
        {
            var capsuleBounds = new Rect(capsuleOrigin, capsuleEnd);
            double radius = capsule.CornerRadius.TopLeft;
            layer.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(content.Bounds.Size)),
                new RectangleGeometry(capsuleBounds, radius * capsuleBounds.Width / capsule.Bounds.Width,
                    radius * capsuleBounds.Height / capsule.Bounds.Height));
        }
        activeDuration = ThemeTransitionMotion.Duration(effect, duration);
        activeEffect = effect;
        host.Children.Add(layer);
    }

    private bool StartPrepared(string next, bool persist)
    {
        if (layer == null) return false;
        content.UpdateLayout();
        var after = Capture(window, content);
        if (after == null) return false;
        if (!layer.SetAfter(after))
        {
            after.Dispose();
            return false;
        }
        targetMode = next;
        targetDark = DesktopTheme.IsDark;
        saveOnFinish = persist;
        clock.Restart();
        if (persist) sound.Start(targetDark, activeDuration, false, activeEffect);
        int current = ++generation;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            if (current == generation && RenderFrame()) window.RequestAnimationFrame(frame!);
        };
        window.RequestAnimationFrame(frame);
        return true;
    }

    internal static bool HasUncapturableSurface(Control content) =>
        content.GetVisualDescendants().Any(v => v.IsEffectivelyVisible &&
            (v is NativeControlHost or Popup { IsOpen: true } or ComboBox { IsDropDownOpen: true } ||
             v is Control { ContextMenu.IsOpen: true }));

    internal static RenderTargetBitmap? Capture(Window window, Control content)
    {
        RenderTargetBitmap? bitmap = null;
        try
        {
            double width = content.Bounds.Width, height = content.Bounds.Height;
            double scale = Math.Min(window.RenderScaling, Math.Sqrt(8500000 / (width * height)));
            bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)),
                new Vector(96 * scale, 96 * scale));
            bitmap.Render(content);
            return bitmap;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException or OutOfMemoryException)
        {
            bitmap?.Dispose();
            return null;
        }
    }

    internal void InterruptForCompanion()
    {
        Finish();
        ClearLayer();
    }

    private bool RenderFrame()
    {
        double progress = clock.Elapsed.TotalMilliseconds / activeDuration.TotalMilliseconds;
        if (DesktopTheme.Mode != targetMode || DesktopTheme.IsDark != targetDark)
        {
            Finish();
            return false;
        }
        if (progress >= 1)
        {
            Finish(true);
            return false;
        }

        try
        {
            layer!.SetFrame(paths!, progress);
            sound.Advance(progress);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            Finish();
            return false;
        }
    }

    private static StreamGeometry ClipAt(Action<double, ILiquidThemeOutline> write, double progress)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.SetFillRule(FillRule.NonZero);
            write(progress, new ClipOutline(context));
        }
        return geometry;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.X != 0 || args.OffsetDelta.Y != 0) Finish();
    }
    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (themeSwitch?.IsPointerOver != true) Finish();
    }
    private void PreservePointerFocus(object? sender, FocusChangingEventArgs args)
    {
        if (args.NavigationMethod == NavigationMethod.Pointer && args.OldFocusedElement is TextBox input &&
            TopLevel.GetTopLevel(input) == window) args.TryCancel();
    }
    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (themeSwitch?.IsKeyboardFocusWithin != true || args.Key is not (Key.Space or Key.Enter)) Finish();
    }
    private void Interrupt(object? sender, EventArgs args) => Finish();
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if ((args.Property == Visual.IsVisibleProperty && !window.IsVisible) ||
            (args.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)) Finish();
    }

    private void Finish(bool completedNormally = false)
    {
        if (completedNormally) sound.Complete();
        else sound.Cancel();
        if (systemAppearancePrepared)
        {
            systemAppearancePrepared = false;
            ClearLayer();
        }
        if (targetMode == null) return;
        string completed = targetMode;
        targetMode = null;
        generation++;
        clock.Stop();
        ClearLayer();
        if (saveOnFinish && DesktopTheme.Mode == completed) commit(completed);
    }

    private void ClearLayer()
    {
        if (layer == null) return;
        host.Children.Remove(layer);
        layer.Dispose();
        layer = null;
        paths = null;
    }

    private void Closed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        Finish();
        if (themeSwitch != null)
        {
            themeSwitch.Selected -= ChangeTheme;
            themeSwitch.GettingFocus -= PreservePointerFocus;
        }
        host.SizeChanged -= Interrupt;
        window.ScalingChanged -= Interrupt;
        window.Deactivated -= Interrupt;
        window.PropertyChanged -= WindowChanged;
        window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        window.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        window.Closed -= Closed;
        DesktopTheme.ModeChanged -= RefreshSwitch;
        DesktopTheme.SystemAppearanceChanging -= PrepareSystemAppearance;
        DesktopTheme.AppearanceChanged -= OnAppearanceChanged;
    }

    private sealed class ClipOutline(StreamGeometryContext context) : ILiquidThemeOutline
    {
        public void MoveTo(double x, double y) => context.BeginFigure(new Point(x, y), true);
        public void LineTo(double x, double y) => context.LineTo(new Point(x, y));
        public void CurveTo(double x1, double y1, double x2, double y2, double x, double y) =>
            context.CubicBezierTo(new Point(x1, y1), new Point(x2, y2), new Point(x, y));
        public void Close() => context.EndFigure(true);
    }

    internal sealed class SnapshotLayer : Grid, IDisposable
    {
        private readonly RenderTargetBitmap before;
        private readonly Rect viewport;
        private readonly Control[]? refractionLayers;
        private readonly ZipperDecoration? zipper;
        private readonly Border[]? rings;
        private RenderTargetBitmap? after;
        private SnapshotImage? revealedImage;

        internal SnapshotLayer(RenderTargetBitmap before, Size size, ThemeRevealPaths paths, string name = "LiquidThemeOverlay")
        {
            this.before = before;
            viewport = new Rect(size);
            if (paths.Refraction != null) refractionLayers = new Control[RefractionThemeShape.Layers.Length];
            if (paths.Zipper != null) zipper = new ZipperDecoration();
            if (paths.CompanionRipple != null)
                rings = Enumerable.Range(0, CompanionRevealPaths.RingCount).Select(i => new Border
                { Background = new SolidColorBrush(Color.Parse(i == 0 ? "#BDF5FF" : "#62CCFF")), Clip = new StreamGeometry() }).ToArray();
            Name = name;
            Avalonia.Automation.AutomationProperties.SetAccessibilityView(this,Avalonia.Automation.AccessibilityView.Raw);
            IsHitTestVisible = false;
            Focusable = false;
            ClipToBounds = true;
            Children.Add(new SnapshotImage(before, viewport));
        }

        internal void SetFrame(ThemeRevealPaths paths, double progress)
        {
            if (revealedImage != null) revealedImage.Clip = ClipAt(paths.Reveal, progress);
            if (rings != null)
            {
                for (int i = 0; i < rings.Length; i++)
                {
                    var ring = new StreamGeometry();
                    using (var context = ring.Open())
                    {
                        context.SetFillRule(FillRule.NonZero);
                        paths.CompanionRipple.WriteRing(progress, i, new ClipOutline(context));
                    }
                    rings[i].Clip = ring;
                    rings[i].Opacity = CompanionRevealPaths.RingOpacity(progress, i, DesktopTheme.IsDark);
                }
            }
            if (refractionLayers != null)
            {
                for (int i = 0; i < refractionLayers.Length; i++)
                {
                    var band = new StreamGeometry();
                    using (var context = band.Open())
                    {
                        context.SetFillRule(FillRule.NonZero);
                        paths.Refraction!.WriteRefraction(progress, i, new ClipOutline(context));
                    }
                    refractionLayers[i].Clip = band;
                }
            }
            zipper?.SetFrame(paths.Zipper!, progress);
        }

        internal bool SetAfter(RenderTargetBitmap bitmap)
        {
            if (bitmap.PixelSize != before.PixelSize) return false;
            revealedImage = new SnapshotImage(bitmap, viewport) { Clip = new StreamGeometry() };
            Children.Add(revealedImage);
            if (refractionLayers != null)
            {
                for (int i = 0; i < refractionLayers.Length; i++)
                {
                    var optics = RefractionThemeShape.Layers[i];
                    Control band;
                    if (optics.IsTint)
                    {
                        var tint = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
                        };
                        for (int stop = 0; stop < RefractionThemeShape.TintStops.Length; stop++)
                            tint.GradientStops.Add(new GradientStop(Color.FromUInt32(optics.TintAt(stop)), RefractionThemeShape.TintStops[stop]));
                        band = new Border { Background = tint };
                    }
                    else
                    {
                        var shifted = new Rect(optics.OffsetX, optics.OffsetY, viewport.Width, viewport.Height);
                        band = new SnapshotImage(bitmap, shifted);
                    }
                    band.Clip = new StreamGeometry();
                    band.Opacity = optics.Opacity;
                    refractionLayers[i] = band;
                    Children.Add(band);
                }
            }
            if (zipper != null) Children.Add(zipper);
            if (rings != null) foreach (var ring in rings) Children.Add(ring);
            after = bitmap;
            return true;
        }

        private sealed class SnapshotImage(RenderTargetBitmap bitmap, Rect rect) : Control
        {
            public override void Render(DrawingContext context) => context.DrawImage(bitmap, rect);
        }

        private sealed class ZipperDecoration : Control
        {
            private static readonly IBrush[] Brushes =
            {
                new SolidColorBrush(ColorOf(ThemeRevealPaths.ZipperLining)),
                new SolidColorBrush(ColorOf(ThemeRevealPaths.ZipperRim)),
                new SolidColorBrush(ColorOf(ThemeRevealPaths.ZipperTeeth)),
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new(ColorOf(ThemeRevealPaths.ZipperPullLight), 0),
                        new(ColorOf(ThemeRevealPaths.ZipperPullMiddle), .53),
                        new(ColorOf(ThemeRevealPaths.ZipperPullDark), 1)
                    }
                }
            };
            private readonly Geometry?[] parts = new Geometry?[ThemeRevealPaths.ZipperParts.Length];

            internal void SetFrame(ZipperThemeShape shape, double progress)
            {
                Opacity = ZipperThemeShape.HardwareOpacity(progress);
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = new StreamGeometry();
                    using (var context = part.Open())
                    {
                        context.SetFillRule(FillRule.NonZero);
                        shape.WritePart(progress, ThemeRevealPaths.ZipperParts[i], new ClipOutline(context));
                    }
                    parts[i] = part;
                }
                InvalidateVisual();
            }

            public override void Render(DrawingContext context)
            {
                for (int i = 0; i < parts.Length; i++)
                    if (parts[i] is Geometry part) context.DrawGeometry(Brushes[i], null, part);
            }

            private static Color ColorOf(uint value) => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        public void Dispose()
        {
            Children.Clear();
            revealedImage = null;
            if (refractionLayers != null) Array.Clear(refractionLayers);
            before.Dispose();
            after?.Dispose();
            after = null;
        }
    }
}
