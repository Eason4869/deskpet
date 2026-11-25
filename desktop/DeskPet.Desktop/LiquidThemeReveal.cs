using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed class LiquidThemeReveal : IDisposable
{
    private readonly Window window;
    private readonly Grid host;
    private readonly Control content;
    private readonly Button button;
    private readonly Action<string> commit;
    private readonly Func<bool> reducedMotion;
    private readonly TimeSpan duration;
    private readonly Stopwatch clock = new();
    private SnapshotLayer? layer;
    private LiquidThemeShape? shape;
    private string? targetMode;
    private bool targetDark;
    private int generation;

    internal LiquidThemeReveal(Window window, Grid host, Control content, Button button,
        Action<string> commit, TimeSpan duration, Func<bool>? reducedMotion = null)
    {
        this.window = window;
        this.host = host;
        this.content = content;
        this.button = button;
        this.commit = commit;
        this.duration = duration;
        this.reducedMotion = reducedMotion ?? (() => MacServices.PrefersReducedMotion);
        button.Click += Switch;
        host.SizeChanged += Interrupt;
        window.ScalingChanged += Interrupt;
        window.Deactivated += Interrupt;
        window.PropertyChanged += WindowChanged;
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        window.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, handledEventsToo: true);
        window.Closed += Closed;
        DesktopTheme.ModeChanged += RefreshIcon;
        RefreshIcon();
    }

    private static string Caption() =>
        TextResources.Get("quietPanel.quietPanel.text") + " · " + ThemeSwitch.Label(DesktopTheme.Mode);

    private void RefreshIcon()
    {
        button.Content = new Viewbox
        {
            Width = 18, Height = 18, IsHitTestVisible = false,
            Child = new Canvas
            {
                Width = 24, Height = 24,
                Children =
                {
                    new Avalonia.Controls.Shapes.Path
                    {
                        Data = Geometry.Parse(ThemeSwitch.Icon(DesktopTheme.Mode)),
                        Stroke = DesktopTheme.Brush("#777F89"), StrokeThickness = 1.65,
                        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
                    }
                }
            }
        };
        LocalizedUi.Tooltip(button, Caption);
        LocalizedUi.Automation(button, Caption);
    }

    private void Switch(object? sender, RoutedEventArgs args)
    {
        if (targetMode != null) return;
        string next = ThemeSwitch.Next(DesktopTheme.Mode);
        bool wasDark = DesktopTheme.IsDark;
        bool reduceMotion = reducedMotion();
        bool couldChangePalette = next == "system" || (next == "dark") != wasDark;
        Point? origin = null;
        RenderTargetBitmap? before = null;
        if (couldChangePalette && !reduceMotion && duration > TimeSpan.Zero &&
            content.IsEffectivelyVisible && content.Bounds.Width >= 1 && content.Bounds.Height >= 1 &&
            !content.GetVisualDescendants().Any(v => v.IsEffectivelyVisible &&
                (v is NativeControlHost || v is Popup { IsOpen: true })))
        {
            origin = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height - 2), content);
            if (origin.HasValue) before = Capture();
            if (before != null)
            {
                layer = new SnapshotLayer(before, content.Bounds.Size);
                host.Children.Add(layer);
            }
        }

        DesktopTheme.Apply(next);
        if (layer != null && before != null && origin.HasValue && DesktopTheme.IsDark != wasDark)
        {
            content.UpdateLayout();
            var after = Capture();
            if (after != null && after.PixelSize == before.PixelSize)
            {
                layer.After = after;
                shape = new LiquidThemeShape(content.Bounds.Width, content.Bounds.Height, origin.Value.X, origin.Value.Y);
                targetMode = next;
                targetDark = DesktopTheme.IsDark;
                clock.Restart();
                int current = ++generation;
                Action<TimeSpan>? frame = null;
                frame = _ =>
                {
                    if (current == generation && RenderFrame()) window.RequestAnimationFrame(frame!);
                };
                window.RequestAnimationFrame(frame);
                return;
            }
            after?.Dispose();
        }

        ClearLayer();
        commit(next);
        if (wasDark == DesktopTheme.IsDark && !reduceMotion) AnimateIcon();
    }

    private void AnimateIcon()
    {
        if (button.Content is not Control icon) return;
        var scale = new ScaleTransform(.84, .84);
        icon.RenderTransform = scale;
        scale.Transitions = new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new QuadraticEaseOut() },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new QuadraticEaseOut() }
        };
        window.RequestAnimationFrame(_ =>
        {
            if (!ReferenceEquals(button.Content, icon) || !window.IsVisible) return;
            scale.ScaleX = scale.ScaleY = 1;
        });
    }

    private RenderTargetBitmap? Capture()
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

    private bool RenderFrame()
    {
        double progress = clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds;
        if (progress >= 1 || DesktopTheme.Mode != targetMode || DesktopTheme.IsDark != targetDark)
        {
            Finish();
            return false;
        }

        try
        {
            layer!.SetClip(Geometry.Parse(shape!.PathAt(progress)));
            return true;
        }
        catch (FormatException)
        {
            Finish();
            return false;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.X != 0 || args.OffsetDelta.Y != 0) Finish();
    }
    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!button.IsPointerOver) Finish();
    }
    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (!button.IsKeyboardFocusWithin || args.Key is not (Key.Space or Key.Enter)) Finish();
    }
    private void Interrupt(object? sender, EventArgs args) => Finish();
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if ((args.Property == Visual.IsVisibleProperty && !window.IsVisible) ||
            (args.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)) Finish();
    }

    private void Finish()
    {
        if (targetMode == null) return;
        string completed = targetMode;
        targetMode = null;
        shape = null;
        generation++;
        clock.Stop();
        ClearLayer();
        if (DesktopTheme.Mode == completed) commit(completed);
    }

    private void ClearLayer()
    {
        if (layer == null) return;
        host.Children.Remove(layer);
        layer.Dispose();
        layer = null;
    }

    private void Closed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        Finish();
        button.Click -= Switch;
        host.SizeChanged -= Interrupt;
        window.ScalingChanged -= Interrupt;
        window.Deactivated -= Interrupt;
        window.PropertyChanged -= WindowChanged;
        window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        window.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        window.Closed -= Closed;
        DesktopTheme.ModeChanged -= RefreshIcon;
    }

    private sealed class SnapshotLayer : Control, IDisposable
    {
        private readonly RenderTargetBitmap before;
        private readonly Rect viewport;
        private Geometry? reveal;
        internal RenderTargetBitmap? After { get; set; }

        internal SnapshotLayer(RenderTargetBitmap before, Size size)
        {
            this.before = before;
            viewport = new Rect(size);
            Name = "LiquidThemeOverlay";
            IsHitTestVisible = false;
            Focusable = false;
            ClipToBounds = true;
        }

        internal void SetClip(Geometry clip)
        {
            reveal = clip;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            context.DrawImage(before, viewport);
            if (After == null || reveal == null) return;
            using (context.PushGeometryClip(reveal)) context.DrawImage(After, viewport);
        }

        public void Dispose()
        {
            before.Dispose();
            After?.Dispose();
            After = null;
        }
    }
}
