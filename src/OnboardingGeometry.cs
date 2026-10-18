using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed partial class OnboardingWindow
    {
        private void TargetVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!navigating && target != null && !target.IsVisible) { RemoveHighlight(); status.Text = "你切换了设置页，可点下方按钮回到当前步骤。"; }
        }
        private void RemoveHighlight()
        {
            if (target != null) target.IsVisibleChanged -= TargetVisibilityChanged;
            if (layer != null && highlight != null) layer.Remove(highlight); target = null; layer = null; highlight = null;
        }
        private void OwnerClosing(object sender, CancelEventArgs args) { ownerClosing = true; Close(); }
        private void OwnerMoved(object sender, EventArgs args) { PositionNearTarget(); }
        private void OwnerSized(object sender, SizeChangedEventArgs args) { if (highlight != null) highlight.InvalidateVisual(); PositionNearTarget(); }
        private void PositionNearTarget()
        {
            if (!IsVisible || positioning) return; positioning = true;
            try
            {
                IntPtr mine = new WindowInteropHelper(this).Handle, owner = new WindowInteropHelper(target != null ? Window.GetWindow(target) ?? settings : settings).Handle;
                if (mine == IntPtr.Zero || owner == IntPtr.Zero) return;
                Rect area = DesktopInterop.WorkArea(owner), editor = DesktopInterop.Bounds(owner), bounds = DesktopInterop.Bounds(mine);
                var source = PresentationSource.FromVisual(this); Matrix toDevice = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
                Width = Math.Max(280, Math.Min(390, (area.Width - 24) / toDevice.M11)); Height = Math.Max(240, Math.Min(540, (area.Height - 24) / toDevice.M22));
                double width = Width * toDevice.M11, height = Height * toDevice.M22;
                Rect focus = new Rect(editor.Left + editor.Width * .5, editor.Top + editor.Height * .5, 1, 1);
                if (target != null && target.IsVisible)
                {
                    Point a = target.PointToScreen(new Point()), b = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight)); focus = new Rect(a, b);
                }
                Point[] candidates = { new Point(editor.Right + 12, focus.Top), new Point(editor.Left - width - 12, focus.Top), new Point(focus.Right + 16, focus.Top), new Point(focus.Left - width - 16, focus.Top), new Point(focus.Left, focus.Bottom + 16), new Point(focus.Left, focus.Top - height - 16), area.TopLeft, new Point(area.Right - width, area.Top), new Point(area.Left, area.Bottom - height), new Point(area.Right - width, area.Bottom - height) };
                double best = Double.MaxValue; Point chosen = bounds.TopLeft;
                foreach (Point candidate in candidates)
                {
                    Point clamped = PetMath.ClampPosition(candidate.X, candidate.Y, width, height, area); Rect rectangle = new Rect(clamped, new Size(width, height)), overlap = Rect.Intersect(rectangle, focus), editorOverlap = Rect.Intersect(rectangle, editor);
                    double score = (overlap.IsEmpty ? 0 : overlap.Width * overlap.Height) * 10000 + (editorOverlap.IsEmpty ? 0 : editorOverlap.Width * editorOverlap.Height) + Math.Abs(clamped.Y - focus.Y) * .01;
                    if (score < best) { best = score; chosen = clamped; }
                }
                DesktopInterop.Move(mine, chosen);
            }
            finally { positioning = false; }
        }
        private sealed class GuideAdorner : Adorner
        {
            public GuideAdorner(UIElement element) : base(element) { IsHitTestVisible = false; }
            protected override void OnRender(DrawingContext drawing)
            {
                Rect rect = new Rect(AdornedElement.RenderSize); rect.Inflate(4, 4);
                drawing.DrawRoundedRectangle(null, new Pen(Ui.Blue, 2), rect, 10, 10);
            }
        }

    }
}
