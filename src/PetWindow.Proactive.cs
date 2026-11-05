using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        readonly DesktopObservationPolicy observationPolicy = new DesktopObservationPolicy();
        readonly ProactiveEventStore observationEvents = new ProactiveEventStore();
        DesktopObservationService observationService;
        IDesktopObservationCapture observationCapture;
        internal Func<IDesktopObservationCapture> ObservationCaptureFactory=()=>new WindowsDesktopObservationCapture();
        internal Func<IConversationApi> ObservationApiFactory=()=>new ApiClient(TimeSpan.FromSeconds(8));
        internal Func<uint?> ObservationInputTick=delegate{uint tick;return DesktopInterop.TryLastInputTick(out tick)?(uint?)tick:null;};
        internal Func<string,RelationshipPackContent,string,Task<RelationshipExperienceContent>> GreetingRelationshipReader=null;
        long observationEpoch;long observedInput;long observationResultEpoch;int lastObservationReason=-1;DateTime lastAutomaticObservationUtc;
        readonly object observationStartGate=new object();
        readonly System.Collections.Generic.Dictionary<ProactiveDecision,DesktopObservationIdentity> observationTargets=new System.Collections.Generic.Dictionary<ProactiveDecision,DesktopObservationIdentity>();
        DesktopObservationIdentity observationTarget;long observationCaptureEpoch;bool observationAdmission;
        readonly System.Collections.Generic.List<Window> observationPreviews=new System.Collections.Generic.List<Window>();
        CancellationTokenSource manualObservationCancel;
        bool observationPaused;
        bool greetingObservation;
        IDisposable observationMaintenance;
        internal void RefreshObservationModels(ConnectionSettings connection){observationPolicy.RefreshModels(connection);Interlocked.Exchange(ref lastObservationReason,-1);InvalidateObservation();if(preferences!=null)preferences.RefreshObservation();}
        void InitializeObservation(){observationMaintenance=DataMaintenance.Register(()=>{InvalidateObservation(true);return Task.FromResult(0);});}
        internal DesktopObservationService ObservationService
        {
            get
            {
                if (observationService == null)
                {
                    if (observationCapture == null) observationCapture = ObservationCaptureFactory();
                    observationService = new DesktopObservationService(observationPolicy, new ObservationCurrentCapture(this,observationCapture), ObservationApiFactory, sceneFingerprint: ObservationFingerprint);
                }
                return observationService;
            }
        }
        internal void InvalidateObservation(bool clearImages = false)
        {
            lock(observationStartGate){Interlocked.Increment(ref observationEpoch);observationTarget=null;observationTargets.Clear();}
            var pending=Interlocked.Exchange(ref manualObservationCancel,null);if(pending!=null)try{pending.Cancel();}catch(ObjectDisposedException){}
            if (clearImages){Interlocked.Exchange(ref lastObservationReason,-1);observationEvents.ClearImages();if(Dispatcher.CheckAccess())CloseObservationPreviews();else if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(CloseObservationPreviews));}
        }
        void CloseObservationPreviews(){foreach(var window in observationPreviews.ToArray())window.Close();observationPreviews.Clear();}
        internal static string ObservationFingerprint(byte[] png)
        {
            using (var stream = new MemoryStream(png, false))
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var thumbnail = new TransformedBitmap(frame, new ScaleTransform(16.0 / frame.PixelWidth, 16.0 / frame.PixelHeight));
                var pixels = new FormatConvertedBitmap(thumbnail, PixelFormats.Bgra32, null, 0);var bytes = new byte[16 * 16 * 4];
                pixels.CopyPixels(bytes, 16 * 4, 0);
                for (int i = 0; i < bytes.Length; i += 4) { bytes[i] &= 0xf0; bytes[i + 1] &= 0xf0; bytes[i + 2] &= 0xf0; bytes[i + 3] = 255; }
                using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(bytes));
            }
        }
        internal PetSettings ObservationSettingsSnapshot()
        {
            var snapshot = Settings.Copy(); snapshot.ImageDescriptionOverride = null; return snapshot;
        }
        static string ObservationSettingsSignature(PetSettings settings)
        {
            using(var stream=new MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PetSettings)).WriteObject(stream,settings);
                using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
            }
        }
        internal async Task<ProactiveDecision> GenerateObservationAsync(PetSettings snapshot, string skinId, string skinName,
            System.Collections.Generic.IList<ChatMessage> memory, System.Collections.Generic.IList<string> recent,
            CancellationToken cancel, DesktopObservationMode mode, long? originalEpoch=null)
        {
            IDisposable activity;if (!DataMaintenance.TryEnterActivity(out activity)) return null;
            DesktopObservationService service=null;bool admitted=false;
            try {
                long epoch=originalEpoch??Interlocked.Read(ref observationEpoch);string settingsSignature=ObservationSettingsSignature(snapshot);
                if(cancel.IsCancellationRequested || epoch!=Interlocked.Read(ref observationEpoch) || settingsSignature!=ObservationSettingsSignature(ObservationSettingsSnapshot()))return null;
                service=ObservationService;var lastInput=ObservationInputTick();if(!lastInput.HasValue)return null;
                var context=new DesktopObservationContext(snapshot,skinId,skinName,memory,recent,DateTime.Now,true,!observationPaused,()=>epoch==Interlocked.Read(ref observationEpoch),mode);
                Task<ProactiveDecision> pending;
                lock(observationStartGate){if(observationAdmission || service.IsBusy || cancel.IsCancellationRequested || epoch!=Interlocked.Read(ref observationEpoch) || settingsSignature!=ObservationSettingsSignature(ObservationSettingsSnapshot()))return null;observationAdmission=true;admitted=true;observationCaptureEpoch=epoch;observationTarget=null;Interlocked.Exchange(ref observedInput,lastInput.Value);pending=service.GenerateAsync(context,cancel);}
                if(preferences!=null)preferences.RefreshObservation();
                var decision=await pending;var currentInput=ObservationInputTick();
                if(epoch!=Interlocked.Read(ref observationEpoch)||!currentInput.HasValue||currentInput.Value!=lastInput.Value||settingsSignature!=ObservationSettingsSignature(ObservationSettingsSnapshot())){decision.Dispose();return null;}
                if(mode==DesktopObservationMode.Automatic && decision.Kind!=ProactiveDecisionKind.Skip){lock(observationStartGate){if(observationTarget==null || epoch!=Interlocked.Read(ref observationEpoch)){decision.Dispose();return null;}observationTargets.Clear();observationTargets[decision]=observationTarget;}}
                Interlocked.Exchange(ref observationResultEpoch,epoch);Interlocked.Exchange(ref lastObservationReason,(int)decision.Reason);if(mode==DesktopObservationMode.Automatic && (decision.Kind!=ProactiveDecisionKind.Skip || decision.Reason==ObservationReason.ModelSkip || decision.Reason==ObservationReason.DuplicateMessage))lastAutomaticObservationUtc=DateTime.UtcNow;
                if(preferences!=null)preferences.RefreshObservation();return decision;
            } finally { if(admitted)lock(observationStartGate){observationAdmission=false;observationTarget=null;}if(service==null)activity.Dispose();else {var ignored=ReleaseObservationActivityAsync(service,activity);} }
        }
        bool ObservationTargetCurrent(ProactiveDecision decision)
        {
            if(decision.Kind==ProactiveDecisionKind.Skip)return true;
            DesktopObservationIdentity target;lock(observationStartGate){if(!observationTargets.TryGetValue(decision,out target))return false;}
            try{using(var metadata=new DesktopObservationFrame(new byte[0],DateTimeOffset.UtcNow,target))return observationCapture!=null && observationCapture.IsCurrent(metadata);}
            catch(Exception error){if(error is InvalidOperationException || error is System.ComponentModel.Win32Exception)return false;throw;}
        }
        void ReleaseObservationTarget(ProactiveDecision decision){if(decision!=null)lock(observationStartGate)observationTargets.Remove(decision);}
        bool ObservationInputCurrent(){var tick=ObservationInputTick();return tick.HasValue && tick.Value==(uint)Interlocked.Read(ref observedInput);}
        sealed class ObservationCurrentCapture : IDesktopObservationCapture
        {
            readonly PetWindow owner;readonly IDesktopObservationCapture inner;
            internal ObservationCurrentCapture(PetWindow owner,IDesktopObservationCapture inner){this.owner=owner;this.inner=inner;}
            bool Current(){return owner.ObservationInputCurrent();}
            public DesktopCaptureStatus GetStatus(DesktopCaptureOptions options=null){return inner.GetStatus(options);}
            public Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel){return Current()?inner.CaptureForegroundAsync(options,cancel):Task.FromResult<DesktopObservationFrame>(null);}
            public bool IsCurrent(DesktopObservationFrame frame){if(!Current() || !inner.IsCurrent(frame))return false;lock(owner.observationStartGate){if(!owner.observationAdmission || owner.observationCaptureEpoch!=Interlocked.Read(ref owner.observationEpoch))return false;owner.observationTarget=frame.Target;return true;}}
        }
        async Task ReleaseObservationActivityAsync(DesktopObservationService service, IDisposable activity)
        { try { await service.WaitForIdleAsync().ConfigureAwait(false); } finally { activity.Dispose();if(!Dispatcher.HasShutdownStarted){var refreshDispatch=Dispatcher.BeginInvoke(new Action(delegate{if(!closing && preferences!=null)preferences.RefreshObservation();}));} } }
    }
}
