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
            if (!navigating && target != null && !target.IsVisible) { RemoveHighlight(); LocalizedUi.Set(status,"Text",()=>(TextResources.Get("onboardingGeometry.targetVisibilityChanged.status"))); }
        }
        private void RemoveHighlight()
        {
            if (target != null) target.IsVisibleChanged -= TargetVisibilityChanged;
            if (layer != null && highlight != null) layer.Remove(highlight); target = null; layer = null; highlight = null;
        }
        private void OwnerClosing(object sender, CancelEventArgs args) { ownerClosing = true; Close(); }
        private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { PositionNearTarget(); }
        private void OwnerMoved(object sender, EventArgs args) { PositionNearTarget(); }
        private void OwnerSized(object sender, SizeChangedEventArgs args) { if (highlight != null) highlight.InvalidateVisual(); PositionNearTarget(); }
        private void PositionNearTarget()
        {
            if (!IsVisible || positioning) return; positioning = true;
            try
            {
                IntPtr mine = new WindowInteropHelper(this).Handle;
                Window main = settingsHost.IsVisible && settingsHost.WindowState != WindowState.Minimized ? settingsHost : null;
                if (mine == IntPtr.Zero) return;
                IntPtr anchor = new WindowInteropHelper(main ?? pet).Handle;
                Rect area = DesktopInterop.WorkArea(anchor == IntPtr.Zero ? mine : anchor);
                var source = PresentationSource.FromVisual(this); Matrix toDevice = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
                double availableWidth=Math.Max(1,(area.Width-24)/toDevice.M11),availableHeight=Math.Max(1,(area.Height-24)/toDevice.M22);
                MinWidth=Math.Min(280,availableWidth);MinHeight=Math.Min(300,availableHeight);Width=Math.Min(390,availableWidth);Height=Math.Min(540,availableHeight);
                double width = Width * toDevice.M11, height = Height * toDevice.M22;
                Rect editor = main == null ? Rect.Empty : DesktopInterop.Bounds(anchor);
                Point desired = main == null ? new Point(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2) : new Point(editor.Right + 12, editor.Top);
                Point chosen = PetMath.ClampPosition(desired.X, desired.Y, width, height, area);
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
