using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    internal sealed class LiquidThemeReveal : IDisposable
    {
        readonly Window window;
        readonly Grid host;
        readonly FrameworkElement content;
        readonly ThemeTitleSwitch themeSwitch;
        readonly Action<string> commit;
        readonly TimeSpan duration;
        readonly Func<string> motion;
        readonly LiquidThemeSoundBinding sound;
        readonly Stopwatch clock = new Stopwatch();
        readonly ThemeRevealTimeline timeline = new ThemeRevealTimeline();
        SnapshotLayer layer;
        ThemeRevealPaths paths;
        TimeSpan activeDuration;
        string activeEffect;
        string targetMode;
        FrameworkElement settingsOrigin;
        bool targetDark;
        bool saveOnFinish, reversing, redirected;
        BitmapSource systemFrame;
        TimeSpan? lastRenderingTime;
        internal Action BeforeCapture { get; set; }

        internal LiquidThemeReveal(Window window, Grid host, FrameworkElement content, ThemeTitleSwitch themeSwitch,
            Action<string> commit, TimeSpan duration, Func<string> motion, BeginLiquidThemeSound beginSound = null)
        {
            this.window = window;
            this.host = host;
            this.content = content;
            this.themeSwitch = themeSwitch;
            this.commit = commit;
            this.duration = duration;
            this.motion = motion;
            sound = new LiquidThemeSoundBinding(beginSound);
            if (themeSwitch != null)
            {
                themeSwitch.Selected += ChangeTheme;
                themeSwitch.PreviewGotKeyboardFocus += PreservePointerFocus;
            }
            host.SizeChanged += Resize;
            window.Deactivated += PauseSound;
            window.StateChanged += StateChanged;
            window.IsVisibleChanged += VisibilityChanged;
            window.Closed += Closed;
            ThemeManager.ModeChanged += RefreshSwitch;
            ThemeManager.Changed += RefreshSwitch;
            ThemeManager.SystemAppearanceChanging += PrepareSystemAppearance;
            ThemeManager.SystemAppearanceChanged += RevealSystemAppearance;
            if (themeSwitch != null) themeSwitch.Refresh(ThemeManager.Mode, ThemeManager.IsDark, false);
        }

        void RefreshSwitch()
        {
            if (themeSwitch != null) themeSwitch.Refresh(ThemeManager.Mode, ThemeManager.IsDark,
                SystemParameters.ClientAreaAnimation);
        }

        internal void ChangeTheme(string next, FrameworkElement origin)
        {
            if (next == ThemeManager.Mode) return;
            if (themeSwitch == null) settingsOrigin = origin;
            bool wasDark = ThemeManager.IsDark;
            bool reduceMotion = !SystemParameters.ClientAreaAnimation;
            bool couldChangePalette = next == "system" || (next == "dark") != wasDark;
            if (targetMode != null && !reduceMotion && ThemeTransitionMotion.Normalize(motion()) != ThemeTransitionMotion.Off)
            {
                var outgoing = couldChangePalette ? Capture(content) : null;
                ThemeManager.Apply(next);
                if (wasDark == ThemeManager.IsDark)
                {
                    targetMode = next;
                    saveOnFinish = true;
                    return;
                }
                if (Retarget(next, true, outgoing)) return;
                Finish();
                commit(next);
                return;
            }
            Finish();
            string effect = ThemeTransitionMotion.Resolve(motion());
            if (couldChangePalette && !reduceMotion) Prepare(effect, origin);
            ThemeManager.Apply(next);
            if (ThemeManager.IsDark != wasDark && StartPrepared(next, true)) return;

            ClearLayer();
            commit(next);
            if (wasDark != ThemeManager.IsDark) sound.Start(ThemeManager.IsDark, duration, true, effect);
        }

        void PrepareSystemAppearance()
        {
            if (targetMode != null && SystemParameters.ClientAreaAnimation &&
                ThemeTransitionMotion.Normalize(motion()) != ThemeTransitionMotion.Off)
            {
                systemFrame = Capture(content);
                return;
            }
            Finish();
            if (SystemParameters.ClientAreaAnimation) Prepare(ThemeTransitionMotion.Resolve(motion()),
                themeSwitch == null ? settingsOrigin : themeSwitch.SystemButton);
        }

        void RevealSystemAppearance()
        {
            if (targetMode != null)
            {
                var outgoing = systemFrame;
                systemFrame = null;
                if (!Retarget("system", saveOnFinish, outgoing)) Finish();
                return;
            }
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
            var before = Capture(content);
            if (before == null) return;
            if (BeforeCapture != null) BeforeCapture();
            paths = ThemeRevealPaths.Create(effect, content.ActualWidth, content.ActualHeight, origin.X, origin.Y);
            layer = new SnapshotLayer(before, content.RenderSize, paths);
            UpdateCapsuleClip();
            activeDuration = ThemeTransitionMotion.Duration(effect, duration);
            activeEffect = effect;
            host.Children.Add(layer);
        }

        void UpdateCapsuleClip()
        {
            var capsule = themeSwitch == null ? null : themeSwitch.Capsule;
            if (capsule != null && capsule.IsVisible && capsule.ActualWidth > 0 && capsule.ActualHeight > 0)
            {
                var capsuleBounds = capsule.TransformToVisual(content).TransformBounds(new Rect(capsule.RenderSize));
                double radius = capsule.CornerRadius.TopLeft;
                layer.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(content.RenderSize)),
                    new RectangleGeometry(capsuleBounds, radius * capsuleBounds.Width / capsule.ActualWidth,
                        radius * capsuleBounds.Height / capsule.ActualHeight));
            }
        }

        bool StartPrepared(string next, bool persist)
        {
            if (layer == null) return false;
            content.UpdateLayout();
            if (paths.Refraction != null)
            {
                var after = Capture(content);
                if (after == null) return false;
                layer.SetRefractionSource(after);
            }
            targetMode = next;
            targetDark = ThemeManager.IsDark;
            saveOnFinish = persist;
            lastRenderingTime = null;
            reversing = redirected = false;
            timeline.Start(activeDuration);
            clock.Restart();
            if (persist) sound.Start(targetDark, activeDuration, false, activeEffect);
            CompositionTarget.Rendering += RenderFrame;
            return true;
        }

        bool Retarget(string next, bool persist, BitmapSource outgoing)
        {
            if (outgoing == null) return false;
            reversing = !reversing;
            redirected = true;
            targetMode = next;
            targetDark = ThemeManager.IsDark;
            saveOnFinish = persist;
            layer.SetDirection(reversing, outgoing);
            timeline.Retarget(reversing);
            clock.Restart();
            sound.Cancel();
            if (persist) sound.Start(targetDark, timeline.Duration, true, activeEffect);
            return true;
        }

        internal static BitmapSource Capture(FrameworkElement content)
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

        internal static bool HasUncapturableSurface(Visual visual)
        {
            var element = visual as UIElement;
            if (element != null && !element.IsVisible) return false;
            var image = visual as Image;
            var popup = visual as Popup;
            var control = visual as FrameworkElement;
            var combo = visual as ComboBox;
            if (visual is HwndHost || visual is MediaElement ||
                (image != null && image.Source is D3DImage) || (popup != null && popup.IsOpen) ||
                (control != null && control.ContextMenu != null && control.ContextMenu.IsOpen) ||
                (combo != null && combo.IsDropDownOpen)) return true;
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
            if (!timeline.Advance(clock.Elapsed))
            {
                Finish(true);
                return;
            }
            try
            {
                layer.SetFrame(paths, timeline.Progress);
                if (!redirected) sound.Advance(timeline.Progress);
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

        void PreservePointerFocus(object sender, KeyboardFocusChangedEventArgs args)
        {
            var oldFocus = args.OldFocus as TextBoxBase;
            if (Mouse.LeftButton == MouseButtonState.Pressed && oldFocus != null &&
                Window.GetWindow(oldFocus) == window) args.Handled = true;
        }
        void Resize(object sender, SizeChangedEventArgs args) { if (layer != null) UpdateCapsuleClip(); }
        void PauseSound(object sender, EventArgs args) { sound.Cancel(); }
        void StateChanged(object sender, EventArgs args) { if (window.WindowState == WindowState.Minimized) Finish(); }
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
            systemFrame = null;
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

        internal BitmapSource TakeOverForCompanion(FrameworkElement region)
        {
            if (layer == null) return null;
            try
            {
                var frame = Capture(host);
                if (frame == null) return null;
                var bounds = region.TransformToVisual(host).TransformBounds(new Rect(region.RenderSize));
                double scaleX = frame.PixelWidth / host.ActualWidth;
                double scaleY = frame.PixelHeight / host.ActualHeight;
                int x = Math.Max(0, (int)Math.Round(bounds.Left * scaleX));
                int y = Math.Max(0, (int)Math.Round(bounds.Top * scaleY));
                int right = Math.Min(frame.PixelWidth, (int)Math.Round(bounds.Right * scaleX));
                int bottom = Math.Min(frame.PixelHeight, (int)Math.Round(bounds.Bottom * scaleY));
                if (x >= right || y >= bottom) return null;
                var cropped = new CroppedBitmap(frame, new Int32Rect(x, y, right - x, bottom - y));
                cropped.Freeze();
                return cropped;
            }
            catch (InvalidOperationException) { return null; }
            catch (ArgumentException) { return null; }
            catch (OutOfMemoryException) { return null; }
            finally
            {
                Finish();
                ClearLayer();
            }
        }

        void Closed(object sender, EventArgs args) { Dispose(); }

        public void Dispose()
        {
            Finish();
            if (themeSwitch != null)
            {
                themeSwitch.Selected -= ChangeTheme;
                themeSwitch.PreviewGotKeyboardFocus -= PreservePointerFocus;
            }
            host.SizeChanged -= Resize;
            window.Deactivated -= PauseSound;
            window.StateChanged -= StateChanged;
            window.IsVisibleChanged -= VisibilityChanged;
            window.Closed -= Closed;
            ThemeManager.ModeChanged -= RefreshSwitch;
            ThemeManager.Changed -= RefreshSwitch;
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

        internal sealed class SnapshotLayer : FrameworkElement
        {
            static readonly Brush[] zipperBrushes = CreateZipperBrushes();
            readonly VisualCollection visuals;
            readonly DrawingVisual snapshot = new DrawingVisual();
            readonly ScaleTransform scale = new ScaleTransform();
            readonly DrawingVisual[] refraction;
            readonly DrawingVisual zipper;
            readonly DrawingVisual[] rings;
            readonly Rect viewport;
            Geometry reveal = Geometry.Empty;
            bool reverse;

            internal SnapshotLayer(BitmapSource before, Size size, ThemeRevealPaths paths, string name = "LiquidThemeOverlay")
            {
                visuals = new VisualCollection(this);
                viewport = new Rect(size);
                Name = name;
                IsHitTestVisible = false;
                ClipToBounds = true;
                Draw(snapshot, before, viewport);
                visuals.Add(snapshot);
                if (paths.Refraction != null)
                {
                    refraction = new DrawingVisual[RefractionThemeShape.Layers.Length];
                    for (int i = 0; i < refraction.Length; i++)
                    {
                        var optics = RefractionThemeShape.Layers[i];
                        refraction[i] = new DrawingVisual { Clip = Geometry.Empty, Opacity = optics.Opacity };
                        if (optics.IsTint)
                        {
                            var tint = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                            for (int stop = 0; stop < RefractionThemeShape.TintStops.Length; stop++)
                                tint.GradientStops.Add(new GradientStop(ColorOf(optics.TintAt(stop)), RefractionThemeShape.TintStops[stop]));
                            tint.Freeze();
                            using (var drawing = refraction[i].RenderOpen()) drawing.DrawRectangle(tint, null, viewport);
                        }
                        visuals.Add(refraction[i]);
                    }
                }
                if (paths.Zipper != null)
                {
                    zipper = new DrawingVisual();
                    visuals.Add(zipper);
                }
                if (paths.CompanionRipple != null)
                {
                    rings = new DrawingVisual[CompanionRevealPaths.RingCount];
                    for (int i = 0; i < rings.Length; i++)
                    {
                        rings[i] = new DrawingVisual { Clip = Geometry.Empty };
                        var brush = Ui.FixedBrush(i == 0 ? "#BDF5FF" : "#62CCFF");
                        using (var drawing = rings[i].RenderOpen()) drawing.DrawRectangle(brush, null, viewport);
                        visuals.Add(rings[i]);
                    }
                }
                foreach (DrawingVisual visual in visuals) visual.Transform = scale;
            }

            internal void SetRefractionSource(BitmapSource bitmap)
            {
                for (int i = 0; i < refraction.Length; i++)
                {
                    var optics = RefractionThemeShape.Layers[i];
                    if (optics.IsTint) continue;
                    var shifted = viewport;
                    shifted.Offset(optics.OffsetX, optics.OffsetY);
                    Draw(refraction[i], bitmap, shifted);
                }
            }

            internal void SetDirection(bool reversed, BitmapSource outgoing)
            {
                reverse = reversed;
                Draw(snapshot, outgoing, viewport);
                UpdateSnapshotClip();
            }

            void UpdateSnapshotClip()
            {
                snapshot.Clip = reverse ? reveal : new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(viewport), reveal);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                scale.ScaleX = finalSize.Width / viewport.Width;
                scale.ScaleY = finalSize.Height / viewport.Height;
                return finalSize;
            }

            internal void SetFrame(ThemeRevealPaths paths, double progress)
            {
                if (progress <= 0) return;
                reveal = ClipAt(paths.Reveal, progress);
                reveal.Freeze();
                UpdateSnapshotClip();
                if (rings != null)
                {
                    for (int i = 0; i < rings.Length; i++)
                    {
                        var ring = new StreamGeometry { FillRule = FillRule.Nonzero };
                        using (var geometry = ring.Open()) paths.CompanionRipple.WriteRing(progress, i, new ClipOutline(geometry));
                        ring.Freeze();
                        rings[i].Clip = ring;
                        rings[i].Opacity = CompanionRevealPaths.RingOpacity(progress, i, ThemeManager.IsDark);
                    }
                }
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
