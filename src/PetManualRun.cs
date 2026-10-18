using System;
using System.Windows;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        private double manualRunRemaining;
        private double manualRunX;
        private int manualRunDirection;

        private void StartManualRun(string state,double duration)
        {
            if(handle==IntPtr.Zero || Settings.LockPosition || !IsPetVisible)return;
            int direction=state=="running-right"?1:state=="running-left"?-1:0;
            if(direction==0)return;
            manualRunDirection=direction;
            manualRunRemaining=duration>0?duration:2;
            manualRunX=DesktopInterop.Bounds(handle).Left;
        }

        // Manual movement is independent of automatic wandering and conversation activity.
        private bool AdvanceManualRun(double delta)
        {
            if(manualRunRemaining<=0)return false;
            string state=manualRunDirection>0?"running-right":"running-left";
            if(dragging || Settings.LockPosition || interaction.Active || Animation.State!=state)
            {StopManualRun();return false;}
            Rect bounds=DesktopInterop.Bounds(handle),area=DesktopInterop.WorkArea(handle);
            double elapsed=Math.Min(delta,manualRunRemaining);
            double dpiScale=Width>0?bounds.Width/Width:1;
            // Retain subpixel progress rather than rounding every frame at the native HWND.
            double desired=manualRunX+manualRunDirection*120*Settings.Scale*dpiScale*elapsed;
            Point target=PetMath.ClampPosition(desired,bounds.Top,bounds.Width,bounds.Height,area);
            manualRunX=target.X;
            // Running only changes the horizontal position; vertical placement is preserved.
            DesktopInterop.Move(handle,new Point(target.X,bounds.Top));
            manualRunRemaining-=elapsed;
            if(manualRunRemaining<=.0001 || Math.Abs(target.X-desired)>.01)
            StopManualRun();
            return true;
        }

        private void StopManualRun()
        {
            if(manualRunDirection==0)return;
            manualRunRemaining=0;manualRunDirection=0;
            if(Animation.State=="running-right" || Animation.State=="running-left")Animation.Play("idle",0);
            SavePosition();
        }
    }
}
