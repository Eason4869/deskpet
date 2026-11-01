using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        DateTime lastObservationTest=DateTime.MinValue;
        internal bool ObservationPaused { get { return observationPaused; } }
        internal bool ObservationBusy {get{return observationService!=null && observationService.IsBusy;}}
        internal ObservationReason? LastObservationReason {get{int value=Interlocked.CompareExchange(ref lastObservationReason,0,0);return value<0?(ObservationReason?)null:(ObservationReason)value;}}
        internal DateTime? LastAutomaticObservationUtc {get{return lastAutomaticObservationUtc==default(DateTime)?(DateTime?)null:lastAutomaticObservationUtc;}}
        internal ObservationAdmission ObservationAdmission(DesktopObservationContext context) { return observationPolicy.TryBegin(context,DateTime.UtcNow); }
        internal void SetObservationPaused(bool paused)
        {
            observationPaused=paused;InvalidateObservation(paused);
            if(paused && greetingObservation)CancelPendingGreeting(false);
        }
        async Task<bool> ObservationCountdownAsync()
        {
            long epoch=Interlocked.Read(ref observationEpoch);
            var text=Ui.Text("",14);text.Margin=new Thickness(18);var notice=new Window{Content=text,Width=380,Height=100,WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Background=ThemeManager.Surface};
            try{
                notice.Show();for(int remaining=3;remaining>0;remaining--){text.Text=TextResources.Format("desktopObservation.countdown",remaining);await Task.Delay(1000);if(closing || sessionLocked || DataMaintenance.IsMaintenanceActive || epoch!=Interlocked.Read(ref observationEpoch))return false;}
                return !closing && !sessionLocked && !DataMaintenance.IsMaintenanceActive && !Settings.QuietMode && !DesktopInterop.ForegroundIsFullScreen(handle);
            }finally{notice.Close();}
        }
        internal async Task<DesktopObservationFrame> CaptureLocalObservationAsync()
        {
            if(closing || sessionLocked || DataMaintenance.IsMaintenanceActive || !GreetingAllowed(DesktopInterop.ForegroundIsFullScreen(handle),true))return null;
            long epoch=Interlocked.Read(ref observationEpoch);IDisposable activity;if(!DataMaintenance.TryEnterActivity(out activity))return null;
            try{
                if(observationCapture==null)observationCapture=ObservationCaptureFactory();
                if(observationCapture.GetStatus()!=DesktopCaptureStatus.Ready)return null;
                var snapshot=ObservationSettingsSnapshot();var options=new DesktopCaptureOptions(snapshot.DesktopObservationExcludedApps);
                var frame=await observationCapture.CaptureForegroundAsync(options,CancellationToken.None);
                try{
                    if(frame==null || frame.Target==null || options.IsExcluded(frame.Target.ApplicationId) || sessionLocked || closing || epoch!=Interlocked.Read(ref observationEpoch) || !observationCapture.IsCurrent(frame))return null;
                    var result=frame;frame=null;return result;
                }finally{if(frame!=null)frame.Dispose();}
            }finally{activity.Dispose();}
        }
        internal async Task PreviewObservationAsync()
        {
            if(closing || sessionLocked || DataMaintenance.IsMaintenanceActive)return;
            if(!await ObservationCountdownAsync())return;
            try{
                using(var frame=await CaptureLocalObservationAsync())
                {
                    if(frame==null){if(!closing && !sessionLocked)MessageBox.Show(TextResources.Get("desktopObservation.previewEmpty"),TextResources.Get("desktopObservation.previewLocal"));return;}
                    var bitmap=new BitmapImage();using(var stream=new MemoryStream(frame.EncodedPng,false)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();}
                    var image=new Image{Source=bitmap,Stretch=System.Windows.Media.Stretch.Uniform};var preview=new Window{Title=TextResources.Get("desktopObservation.previewLocal"),Content=image,Width=800,Height=600,Background=ThemeManager.Surface};observationPreviews.Add(preview);preview.Closed+=delegate{image.Source=null;observationPreviews.Remove(preview);};preview.Show();
                }
            }catch(Exception){if(!closing && !sessionLocked)MessageBox.Show(TextResources.Get("desktopObservation.previewEmpty"),TextResources.Get("desktopObservation.previewLocal"));}
        }
        internal async Task TestObservationAsync()
        {
            if(DateTime.UtcNow-lastObservationTest<TimeSpan.FromSeconds(10))return;
            lastObservationTest=DateTime.UtcNow;
            var snapshot=ObservationSettingsSnapshot();var summary=DesktopObservationUiText.ReceivingSummary(snapshot);
            if(observationPaused){MessageBox.Show(TextResources.Get("desktopObservation.paused"),TextResources.Get("desktopObservation.uploadTest"));return;}
            var context=new DesktopObservationContext(snapshot,Skin.Id,Skin.Name,null,null,DateTime.Now,true,true,()=>true,DesktopObservationMode.ManualTest);var admission=observationPolicy.TryBegin(context,DateTime.UtcNow);
            if(!admission.Allowed || summary.Length==0){MessageBox.Show(DesktopObservationUiText.ReasonText(admission.Reason),TextResources.Get("desktopObservation.uploadTest"));return;}
            string confirmed=DesktopObservationConsent.Destination(snapshot);
            if(new DesktopObservationConsentWindow(snapshot,true){Owner=preferences==null?this:preferences.HostWindow}.ShowDialog()!=true)return;
            if(!await ObservationCountdownAsync())return;
            snapshot=ObservationSettingsSnapshot();if(!DesktopObservationConsent.IsConfirmed(snapshot) || DesktopObservationConsent.Destination(snapshot)!=confirmed || !GreetingAllowed(DesktopInterop.ForegroundIsFullScreen(handle),true))return;
            long epoch=Interlocked.Read(ref observationEpoch);var cancellation=new CancellationTokenSource();if(Interlocked.CompareExchange(ref manualObservationCancel,cancellation,null)!=null){cancellation.Dispose();return;}
            try{
                if(epoch!=Interlocked.Read(ref observationEpoch))return;
                using(var decision=await GenerateObservationAsync(snapshot,Skin.Id,Skin.Name,null,null,cancellation.Token,DesktopObservationMode.ManualTest))
                {
                    if(decision==null || cancellation.IsCancellationRequested || epoch!=Interlocked.Read(ref observationEpoch))return;
                    string result=decision.Kind==ProactiveDecisionKind.Speak?decision.Message:DesktopObservationUiText.ReasonText(decision.Reason);
                    MessageBox.Show(result,TextResources.Get("desktopObservation.uploadTest"));
                }
            }catch(OperationCanceledException){}finally{Interlocked.CompareExchange(ref manualObservationCancel,null,cancellation);cancellation.Dispose();}
        }
    }
}
