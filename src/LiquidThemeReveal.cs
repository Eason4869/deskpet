using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    internal sealed class LiquidThemeReveal : IDisposable
    {
        readonly Window window;
        readonly Grid host;
        readonly FrameworkElement content;
        readonly Button button;
        readonly Action<string> commit;
        readonly TimeSpan duration;
        readonly Func<string> motion;
        readonly LiquidThemeSoundBinding sound;
        readonly ScrollChangedEventHandler scrollChanged;
        readonly Stopwatch clock = new Stopwatch();
        SnapshotLayer layer;
        ThemeRevealPaths paths;
        TimeSpan activeDuration;
        string activeEffect;
        string targetMode;
        FrameworkElement settingsOrigin;
        bool targetDark;
        bool saveOnFinish;
        TimeSpan? lastRenderingTime;

        internal LiquidThemeReveal(Window window, Grid host, FrameworkElement content, Button button,
            Action<string> commit, TimeSpan duration, Func<string> motion, BeginLiquidThemeSound beginSound = null)
        {
            this.window = window;
            this.host = host;
            this.content = content;
            this.button = button;
            this.commit = commit;
            this.duration = duration;
            this.motion = motion;
            sound = new LiquidThemeSoundBinding(beginSound);
            scrollChanged = OnScrollChanged;
            if (button != null)
            {
                button.Click += Switch;
                button.PreviewGotKeyboardFocus += PreservePointerFocus;
            }
            host.SizeChanged += Interrupt;
            window.DpiChanged += Interrupt;
            window.Deactivated += Interrupt;
            window.StateChanged += Interrupt;
            window.IsVisibleChanged += VisibilityChanged;
            window.PreviewMouseDown += OnMouseDown;
            window.PreviewKeyDown += OnKeyDown;
            window.AddHandler(ScrollViewer.ScrollChangedEvent, scrollChanged, true);
            window.Closed += Closed;
            ThemeManager.ModeChanged += RefreshIcon;
            ThemeManager.SystemAppearanceChanging += PrepareSystemAppearance;
            ThemeManager.SystemAppearanceChanged += RevealSystemAppearance;
            RefreshIcon();
        }

        string Caption()
        {
            return TextResources.Get("quietPanel.quietPanel.text") + " · " + ThemeSwitch.Label(ThemeManager.Mode);
        }

        void RefreshIcon()
        {
            if (button == null) return;
            button.Content = ShellIcons.Image("theme-" + ThemeManager.Mode);
            LocalizedUi.Set(button, "ToolTip", Caption);
            LocalizedUi.Automation(button, Caption);
        }

        void Switch(object sender, RoutedEventArgs args)
        {
            ChangeTheme(ThemeSwitch.Next(ThemeManager.Mode), button);
        }

        internal void ChangeTheme(string next, FrameworkElement origin)
        {
            if (targetMode != null) return;
            if (next == ThemeManager.Mode) return;
            if (origin != button) settingsOrigin = origin;
            sound.Cancel();
            bool wasDark = ThemeManager.IsDark;
            bool reduceMotion = !SystemParameters.ClientAreaAnimation;
            string effect = motion();
            bool couldChangePalette = next == "system" || (next == "dark") != wasDark;
            if (couldChangePalette && !reduceMotion) Prepare(effect, origin);
            ThemeManager.Apply(next);
            if (ThemeManager.IsDark != wasDark && StartPrepared(next, true)) return;

            ClearLayer();
            commit(next);
            if (wasDark != ThemeManager.IsDark) sound.Start(ThemeManager.IsDark, duration, true, effect);
            if (origin == button && wasDark == ThemeManager.IsDark && !reduceMotion && effect != ThemeTransitionMotion.Off) AnimateIcon();
        }

        void PrepareSystemAppearance()
        {
            Finish();
            if (SystemParameters.ClientAreaAnimation) Prepare(motion(), button ?? settingsOrigin);
        }

        void RevealSystemAppearance()
        {
            if (!StartPrepared("system", false)) ClearLayer();
        }

        void Prepare(string effect, FrameworkElement anchor)
        {
            if (effect == ThemeTransitionMotion.Off || duration <= TimeSpan.Zero || !content.IsVisible || window.WindowState == WindowState.Minimized ||
                content.ActualWidth < 1 || content.ActualHeight < 1 || HasUncapturableSurface(content)) return;
            var origin = new Point(Math.Max(0, content.ActualWidth - 42), 22);
            if (anchor != null && anchor.IsVisible)
            {
                double originY = effect == ThemeTransitionMotion.Flow ? anchor.ActualHeight - 2 : anchor.ActualHeight / 2;
                origin = anchor.TranslatePoint(new Point(anchor.ActualWidth / 2, originY), content);
            }
            var before = Capture();
            if (before == null) return;
            paths = ThemeRevealPaths.Create(effect, content.ActualWidth, content.ActualHeight, origin.X, origin.Y);
            layer = new SnapshotLayer(before, content.RenderSize, paths);
            activeDuration = ThemeTransitionMotion.Duration(effect, duration);
            activeEffect = effect;
            host.Children.Add(layer);
        }

        bool StartPrepared(string next, bool persist)
        {
            if (layer == null) return false;
            content.UpdateLayout();
            var after = Capture();
            if (after == null || !layer.SetAfter(after)) return false;
            targetMode = next;
            targetDark = ThemeManager.IsDark;
            saveOnFinish = persist;
            lastRenderingTime = null;
            clock.Restart();
            if (persist) sound.Start(targetDark, activeDuration, false, activeEffect);
            CompositionTarget.Rendering += RenderFrame;
            return true;
        }

        void AnimateIcon()
        {
            var icon = button == null ? null : button.Content as FrameworkElement;
            if (icon == null) return;
            var scale = new ScaleTransform(1, 1);
            icon.RenderTransformOrigin = new Point(.5, .5);
            icon.RenderTransform = scale;
            var pulse = new DoubleAnimation(.84, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }

        BitmapSource Capture()
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(content);
                double width = content.ActualWidth, height = content.ActualHeight;
                double scale = Math.Min(1, Math.Sqrt(8500000 / (width * height * dpi.DpiScaleX * dpi.DpiScaleY)));
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX * scale),
                    (int)Math.Ceiling(height * dpi.DpiScaleY * scale), dpi.PixelsPerInchX * scale,
                    dpi.PixelsPerInchY * scale, PixelFormats.Pbgra32);
                bitmap.Render(content);
                bitmap.Freeze();
                return bitmap;
            }
            catch (InvalidOperationException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (OutOfMemoryException) { return null; }
        }

        static bool HasUncapturableSurface(Visual visual)
        {
            var element = visual as UIElement;
            if (element != null && !element.IsVisible) return false;
            var image = visual as Image;
            var popup = visual as Popup;
            if (visual is HwndHost || visual is MediaElement ||
                (image != null && image.Source is D3DImage) || (popup != null && popup.IsOpen)) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                var child = VisualTreeHelper.GetChild(visual, i) as Visual;
                if (child != null && HasUncapturableSurface(child)) return true;
            }
            return false;
        }

        void RenderFrame(object sender, EventArgs args)
        {
            if (targetMode == null) return;
            var render = args as RenderingEventArgs;
            if (render != null)
            {
                if (lastRenderingTime == render.RenderingTime) return;
                lastRenderingTime = render.RenderingTime;
            }
            if (ThemeManager.Mode != targetMode || ThemeManager.IsDark != targetDark || !SystemParameters.ClientAreaAnimation)
            {
                Finish();
                return;
            }
            var progress = clock.Elapsed.TotalMilliseconds / activeDuration.TotalMilliseconds;
            if (progress >= 1)
            {
                Finish(true);
                return;
            }
            try
            {
                layer.SetFrame(paths, progress);
                sound.Advance(progress);
            }
            catch (ArgumentException) { Finish(); }
            catch (InvalidOperationException) { Finish(); }
        }

        static StreamGeometry ClipAt(Action<double, ILiquidThemeOutline> write, double progress)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var context = geometry.Open()) write(progress, new ClipOutline(context));
            return geometry;
        }

        void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (args.HorizontalChange != 0 || args.VerticalChange != 0) Finish();
        }
        void OnMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (button == null || !button.IsMouseOver) Finish();
        }
        void PreservePointerFocus(object sender, KeyboardFocusChangedEventArgs args)
        {
            var oldFocus = args.OldFocus as TextBoxBase;
            if (Mouse.LeftButton == MouseButtonState.Pressed && oldFocus != null &&
                Window.GetWindow(oldFocus) == window) args.Handled = true;
        }
        void OnKeyDown(object sender, KeyEventArgs args)
        {
            if (button == null || !button.IsKeyboardFocusWithin || (args.Key != Key.Space && args.Key != Key.Enter)) Finish();
        }
        void Interrupt(object sender, EventArgs args) { Finish(); }
        void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!window.IsVisible) Finish();
        }

        void Finish(bool completedNormally = false)
        {
            if (completedNormally) sound.Complete();
            else sound.Cancel();
            if (targetMode == null) return;
            string completed = targetMode;
            targetMode = null;
            clock.Stop();
            CompositionTarget.Rendering -= RenderFrame;
            ClearLayer();
            if (saveOnFinish && ThemeManager.Mode == completed) commit(completed);
        }

        void ClearLayer()
        {
            if (layer == null) return;
            host.Children.Remove(layer);
            layer = null;
            paths = null;
        }

        void Closed(object sender, EventArgs args) { Dispose(); }

        public void Dispose()
        {
            Finish();
            if (button != null)
            {
                button.Click -= Switch;
                button.PreviewGotKeyboardFocus -= PreservePointerFocus;
            }
            host.SizeChanged -= Interrupt;
            window.DpiChanged -= Interrupt;
            window.Deactivated -= Interrupt;
            window.StateChanged -= Interrupt;
            window.IsVisibleChanged -= VisibilityChanged;
            window.PreviewMouseDown -= OnMouseDown;
            window.PreviewKeyDown -= OnKeyDown;
            window.RemoveHandler(ScrollViewer.ScrollChangedEvent, scrollChanged);
            window.Closed -= Closed;
            ThemeManager.ModeChanged -= RefreshIcon;
            ThemeManager.SystemAppearanceChanging -= PrepareSystemAppearance;
            ThemeManager.SystemAppearanceChanged -= RevealSystemAppearance;
        }

        sealed class ClipOutline : ILiquidThemeOutline
        {
            readonly StreamGeometryContext context;
            internal ClipOutline(StreamGeometryContext context) { this.context = context; }
            public void MoveTo(double x, double y) { context.BeginFigure(new Point(x, y), true, true); }
            public void LineTo(double x, double y) { context.LineTo(new Point(x, y), false, false); }
            public void CurveTo(double x1, double y1, double x2, double y2, double x, double y)
            {
                context.BezierTo(new Point(x1, y1), new Point(x2, y2), new Point(x, y), false, false);
            }
            public void Close() { }
        }

        sealed class SnapshotLayer : FrameworkElement
        {
            static readonly Brush[] zipperBrushes = CreateZipperBrushes();
            readonly VisualCollection visuals;
            readonly DrawingVisual after = new DrawingVisual();
            readonly DrawingVisual[] refraction;
            readonly DrawingVisual zipper;
            readonly Rect viewport;
            readonly int pixelWidth, pixelHeight;

            internal SnapshotLayer(BitmapSource before, Size size, ThemeRevealPaths paths)
            {
                visuals = new VisualCollection(this);
                viewport = new Rect(size);
                pixelWidth = before.PixelWidth;
                pixelHeight = before.PixelHeight;
                Name = "LiquidThemeOverlay";
                IsHitTestVisible = false;
                ClipToBounds = true;
                var old = new DrawingVisual();
                Draw(old, before, viewport);
                after.Clip = Geometry.Empty;
                visuals.Add(old);
                visuals.Add(after);
                if (paths.Refraction != null)
                {
                    refraction = new DrawingVisual[ThemeRevealPaths.RefractionOpacities.Length];
                    for (int i = 0; i < refraction.Length; i++)
                    {
                        refraction[i] = new DrawingVisual { Clip = Geometry.Empty, Opacity = ThemeRevealPaths.RefractionOpacities[i] };
                        visuals.Add(refraction[i]);
                    }
                }
                if (paths.Zipper != null)
                {
                    zipper = new DrawingVisual();
                    visuals.Add(zipper);
                }
            }

            internal bool SetAfter(BitmapSource bitmap)
            {
                if (bitmap.PixelWidth != pixelWidth || bitmap.PixelHeight != pixelHeight) return false;
                Draw(after, bitmap, viewport);
                if (refraction != null)
                {
                    var shifted = viewport;
                    shifted.Offset(ThemeRevealPaths.RefractionOffsetX, ThemeRevealPaths.RefractionOffsetY);
                    foreach (var band in refraction) Draw(band, bitmap, shifted);
                }
                return true;
            }

            internal void SetFrame(ThemeRevealPaths paths, double progress)
            {
                var reveal = ClipAt(paths.Reveal, progress);
                reveal.Freeze();
                after.Clip = reveal;
                if (refraction != null)
                {
                    for (int i = 0; i < refraction.Length; i++)
                    {
                        var band = new StreamGeometry { FillRule = FillRule.Nonzero };
                        using (var context = band.Open()) paths.Refraction.WriteRefraction(progress, i, new ClipOutline(context));
                        band.Freeze();
                        refraction[i].Clip = band;
                    }
                }
                if (zipper != null)
                {
                    zipper.Opacity = ZipperThemeShape.HardwareOpacity(progress);
                    using (var drawing = zipper.RenderOpen())
                    {
                        for (int i = 0; i < ThemeRevealPaths.ZipperParts.Length; i++)
                        {
                            var part = new StreamGeometry { FillRule = FillRule.Nonzero };
                            using (var context = part.Open()) paths.Zipper.WritePart(progress, ThemeRevealPaths.ZipperParts[i], new ClipOutline(context));
                            part.Freeze();
                            drawing.DrawGeometry(zipperBrushes[i], null, part);
                        }
                    }
                }
            }

            static Brush[] CreateZipperBrushes()
            {
                var pull = new LinearGradientBrush(ColorOf(ThemeRevealPaths.ZipperPullLight), ColorOf(ThemeRevealPaths.ZipperPullDark), 110);
                pull.GradientStops.Insert(1, new GradientStop(ColorOf(ThemeRevealPaths.ZipperPullMiddle), .53));
                var brushes = new Brush[] { new SolidColorBrush(ColorOf(ThemeRevealPaths.ZipperLining)),
                    new SolidColorBrush(ColorOf(ThemeRevealPaths.ZipperRim)), new SolidColorBrush(ColorOf(ThemeRevealPaths.ZipperTeeth)), pull };
                foreach (var brush in brushes) brush.Freeze();
                return brushes;
            }

            static Color ColorOf(uint value) { return Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value); }

            static void Draw(DrawingVisual target, BitmapSource bitmap, Rect rect)
            {
                RenderOptions.SetBitmapScalingMode(target, BitmapScalingMode.Linear);
                using (var context = target.RenderOpen()) context.DrawImage(bitmap, rect);
            }

            protected override int VisualChildrenCount { get { return visuals.Count; } }
            protected override Visual GetVisualChild(int index) { return visuals[index]; }
        }
    }
}
