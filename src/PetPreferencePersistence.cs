using System;
using System.Windows;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        PreferenceSaveScheduler preferenceSaves;
        bool scaleLayoutPending,scaleSaveRequested;Rect scaleAnchor;
        internal void RequestPreferenceSave()
        {
            Dispatcher.VerifyAccess();if(closing)return;
            if(DataMaintenance.IsMaintenanceActive)deferredSettingsSave=true;
            if(preferenceSaves==null)preferenceSaves=new PreferenceSaveScheduler(TrySaveSettings,TimeSpan.FromMilliseconds(250));
            preferenceSaves.Request();
        }
        internal bool FlushPreferenceSaves()
        {CompleteScaleLayout();return preferenceSaves==null||preferenceSaves.Flush();}
        internal void PreviewScale(double value)
        {
            if(Double.IsNaN(value)||Double.IsInfinity(value)||value<.75||value>2.25)throw new ArgumentOutOfRangeException("value");
            Settings.Scale=value;ApplyScaleGeometry(false);UpdateFrame();
        }
        internal void CommitScale()
        {if(scaleLayoutPending)scaleSaveRequested=true;else RequestPreferenceSave();}
        void ApplyScaleGeometry(bool persistPosition)
        {
            double width=192*Settings.Scale,height=208*Settings.Scale;
            if(Width==width&&Height==height)return;
            if(handle==IntPtr.Zero){Width=width;Height=height;return;}
            if(!scaleLayoutPending)scaleAnchor=DesktopInterop.Bounds(handle);
            Width=width;Height=height;scaleSaveRequested|=persistPosition;
            if(scaleLayoutPending)return;scaleLayoutPending=true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(CompleteScaleLayout));
        }
        void CompleteScaleLayout()
        {
            if(!scaleLayoutPending||!Dispatcher.CheckAccess())return;
            scaleLayoutPending=false;bool persist=scaleSaveRequested;scaleSaveRequested=false;
            Rect after=DesktopInterop.Bounds(handle);
            DesktopInterop.Move(handle,new Point(scaleAnchor.Left,scaleAnchor.Bottom-after.Height));DesktopInterop.ClampWindow(handle);
            Rect final=DesktopInterop.Bounds(handle);Settings.X=final.Left;Settings.Y=final.Top;
            if(persist)RequestPreferenceSave();
        }
    }
}
