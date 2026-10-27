using System;
using System.Windows;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        PreferenceSaveScheduler preferenceSaves;PreferenceChangeSet pendingPreferences;IDisposable preferenceMaintenance;
        long settingsSaveRevision,preferenceFailureRevision=-1;
        bool scaleLayoutPending,scaleSaveRequested;Rect scaleAnchor;int preferenceEditDepth;
        PreferenceSaveScheduler EnsurePreferenceSaves()
        {
            if(preferenceSaves==null){
                var scheduler=new PreferenceSaveScheduler(SavePendingPreferencesAsync,TimeSpan.FromMilliseconds(250));
                try{preferenceMaintenance=DataMaintenance.Register(()=>Dispatcher.InvokeAsync(new Func<Task>(DrainPreferenceMaintenanceAsync)).Task.Unwrap());}
                catch{scheduler.Dispose();throw;}
                preferenceSaves=scheduler;for(int i=0;i<preferenceEditDepth;i++)scheduler.Suspend();
            }return preferenceSaves;
        }
        async Task DrainPreferenceMaintenanceAsync()
        {if(!await FlushPreferenceSavesAsync())throw new IOException(TextResources.Get("dataManagement.pendingSaveFailed"));}
        async Task<bool> SavePendingPreferencesAsync()
        {
            var patch=pendingPreferences;if(patch==null)return true;long noticeRevision=settingsSaveRevision;
            try{
                await Store.SavePreferencePatchAsync(patch);
                if(ReferenceEquals(pendingPreferences,patch))pendingPreferences=null;
                if(preferenceFailureRevision==noticeRevision&&settingsSaveRevision==noticeRevision&&Notice==TextResources.Get("petWindow.saveSettings.notice")){
                    Notice=Store.Warning;preferenceFailureRevision=-1;if(preferences!=null)preferences.ShowNotice(Notice);
                }return true;
            }catch(Exception error){
                if(!(error is IOException||error is UnauthorizedAccessException||error is System.Runtime.Serialization.SerializationException||error is ArgumentException||error is InvalidOperationException))throw;
                preferenceFailureRevision=++settingsSaveRevision;Notice=TextResources.Get("petWindow.saveSettings.notice");if(preferences!=null)preferences.ShowNotice(Notice);return false;
            }
        }
        internal void BeginPreferenceEdit()
        {
            Dispatcher.VerifyAccess();if(closing)return;
            if(preferenceSaves==null&&!DataMaintenance.IsMaintenanceActive)EnsurePreferenceSaves();
            preferenceEditDepth++;if(preferenceSaves!=null)preferenceSaves.Suspend();
        }
        internal void EndPreferenceEdit(){Dispatcher.VerifyAccess();if(preferenceEditDepth==0)return;preferenceEditDepth--;if(preferenceSaves!=null)preferenceSaves.Resume();}
        internal void RequestPreferenceSave(){RequestPreferenceFields("Scale","Volume","X","Y");}
        internal void RequestScaleSave(){RequestPreferenceFields("Scale","X","Y");}
        internal void RequestVolumeSave(){RequestPreferenceFields("Volume");}
        void RequestPreferenceFields(params string[] fields)
        {
            Dispatcher.VerifyAccess();if(closing)return;
            // Do not register producers after a maintenance reservation.
            if(DataMaintenance.IsMaintenanceActive){deferredSettingsSave=true;return;}
            var scheduler=EnsurePreferenceSaves();pendingPreferences=Store.MergePreferences(pendingPreferences,Store.CapturePreferences(Settings,fields));scheduler.Request();
        }
        internal async Task<bool> FlushPreferenceSavesAsync()
        {CompleteScaleLayout();return preferenceSaves==null||await preferenceSaves.FlushAsync();}
        internal void PreviewScale(double value)
        {
            if(Double.IsNaN(value)||Double.IsInfinity(value)||value<.75||value>2.25)throw new ArgumentOutOfRangeException("value");
            Settings.Scale=value;ApplyScaleGeometry(false);UpdateFrame();
        }
        internal void CommitScale()
        {if(scaleLayoutPending)scaleSaveRequested=true;else RequestScaleSave();}
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
            if(persist)RequestScaleSave();
        }
    }
}
