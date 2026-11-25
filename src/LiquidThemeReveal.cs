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
        readonly ScrollChangedEventHandler scrollChanged;
        readonly Stopwatch clock = new Stopwatch();
        SnapshotLayer layer;
        LiquidThemeShape shape;
        string targetMode;
        bool targetDark;
        bool saveOnFinish;

        internal LiquidThemeReveal(Window window, Grid host, FrameworkElement content, Button button,
            Action<string> commit, TimeSpan duration)
        {
            this.window = window;
            this.host = host;
            this.content = content;
            this.button = button;
            this.commit = commit;
            this.duration = duration;
            scrollChanged = OnScrollChanged;
            button.Click += Switch;
            button.PreviewGotKeyboardFocus += PreservePointerFocus;
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
            button.Content = ShellIcons.Image("theme-" + ThemeManager.Mode);
            LocalizedUi.Set(button, "ToolTip", Caption);
            LocalizedUi.Automation(button, Caption);
        }

        void Switch(object sender, RoutedEventArgs args)
        {
            if (targetMode != null) return;
            string next = ThemeSwitch.Next(ThemeManager.Mode);
            bool wasDark = ThemeManager.IsDark;
            bool reduceMotion = !SystemParameters.ClientAreaAnimation;
            bool couldChangePalette = next == "system" || (next == "dark") != wasDark;
            if (couldChangePalette && !reduceMotion) Prepare();
            ThemeManager.Apply(next);
            if (ThemeManager.IsDark != wasDark && StartPrepared(next, true)) return;

            ClearLayer();
            commit(next);
            if (wasDark == ThemeManager.IsDark && !reduceMotion) AnimateIcon();
        }

        void PrepareSystemAppearance()
        {
            Finish();
            if (SystemParameters.ClientAreaAnimation) Prepare();
        }

        void RevealSystemAppearance()
        {
            if (!StartPrepared("system", false)) ClearLayer();
        }

        void Prepare()
        {
            if (duration <= TimeSpan.Zero || !content.IsVisible || window.WindowState == WindowState.Minimized ||
                content.ActualWidth < 1 || content.ActualHeight < 1 || HasUncapturableSurface(content)) return;
            var origin = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight - 2), content);
            var before = Capture();
            if (before == null) return;
            layer = new SnapshotLayer(before, content.RenderSize);
            shape = new LiquidThemeShape(content.ActualWidth, content.ActualHeight, origin.X, origin.Y);
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
            clock.Restart();
            CompositionTarget.Rendering += RenderFrame;
            return true;
        }

        void AnimateIcon()
        {
            var icon = button.Content as FrameworkElement;
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
            if (ThemeManager.Mode != targetMode || ThemeManager.IsDark != targetDark || !SystemParameters.ClientAreaAnimation)
            {
                Finish();
                return;
            }
            var progress = clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds;
            if (progress >= 1)
            {
                Finish();
                return;
            }
            try { layer.SetClip(Geometry.Parse(shape.PathAt(progress))); }
            catch (FormatException) { Finish(); }
        }

        void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (args.HorizontalChange != 0 || args.VerticalChange != 0) Finish();
        }
        void OnMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (!button.IsMouseOver) Finish();
        }
        void PreservePointerFocus(object sender, KeyboardFocusChangedEventArgs args)
        {
            var oldFocus = args.OldFocus as TextBoxBase;
            if (Mouse.LeftButton == MouseButtonState.Pressed && oldFocus != null &&
                Window.GetWindow(oldFocus) == window) args.Handled = true;
        }
        void OnKeyDown(object sender, KeyEventArgs args)
        {
            if (!button.IsKeyboardFocusWithin || (args.Key != Key.Space && args.Key != Key.Enter)) Finish();
        }
        void Interrupt(object sender, EventArgs args) { Finish(); }
        void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!window.IsVisible) Finish();
        }

        void Finish()
        {
            if (targetMode == null) return;
            string completed = targetMode;
            targetMode = null;
            shape = null;
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
            shape = null;
        }

        void Closed(object sender, EventArgs args) { Dispose(); }

        public void Dispose()
        {
            Finish();
            button.Click -= Switch;
            button.PreviewGotKeyboardFocus -= PreservePointerFocus;
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

        sealed class SnapshotLayer : FrameworkElement
        {
            readonly VisualCollection visuals;
            readonly DrawingVisual after = new DrawingVisual();
            readonly Rect viewport;
            readonly int pixelWidth, pixelHeight;

            internal SnapshotLayer(BitmapSource before, Size size)
            {
                visuals = new VisualCollection(this);
                viewport = new Rect(size);
                pixelWidth = before.PixelWidth;
                pixelHeight = before.PixelHeight;
                Name = "LiquidThemeOverlay";
                IsHitTestVisible = false;
                ClipToBounds = true;
                var old = new DrawingVisual();
                Draw(old, before);
                after.Clip = Geometry.Empty;
                visuals.Add(old);
                visuals.Add(after);
            }

            internal bool SetAfter(BitmapSource bitmap)
            {
                if (bitmap.PixelWidth != pixelWidth || bitmap.PixelHeight != pixelHeight) return false;
                Draw(after, bitmap);
                return true;
            }

            internal void SetClip(Geometry clip)
            {
                clip.Freeze();
                after.Clip = clip;
            }

            void Draw(DrawingVisual target, BitmapSource bitmap)
            {
                RenderOptions.SetBitmapScalingMode(target, BitmapScalingMode.Linear);
                using (var context = target.RenderOpen()) context.DrawImage(bitmap, viewport);
            }

            protected override int VisualChildrenCount { get { return visuals.Count; } }
            protected override Visual GetVisualChild(int index) { return visuals[index]; }
        }
    }
}
