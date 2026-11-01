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
        long observationEpoch;long observedInput;int lastObservationReason=-1;DateTime lastAutomaticObservationUtc;
        readonly object observationStartGate=new object();
        readonly System.Collections.Generic.List<Window> observationPreviews=new System.Collections.Generic.List<Window>();
        CancellationTokenSource manualObservationCancel;
        bool observationPaused;
        bool greetingObservation;
        IDisposable observationMaintenance;
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
            Interlocked.Increment(ref observationEpoch);
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
            CancellationToken cancel, DesktopObservationMode mode)
        {
            IDisposable activity;if (!DataMaintenance.TryEnterActivity(out activity)) return null;
            DesktopObservationService service=null;
            try {
                string settingsSignature=ObservationSettingsSignature(snapshot);service=ObservationService;long epoch=Interlocked.Read(ref observationEpoch);var lastInput=ObservationInputTick();if(!lastInput.HasValue)return null;
                var context=new DesktopObservationContext(snapshot,skinId,skinName,memory,recent,DateTime.Now,true,!observationPaused,()=>epoch==Interlocked.Read(ref observationEpoch),mode);
                Task<ProactiveDecision> pending;
                lock(observationStartGate){if(service.IsBusy)return null;Interlocked.Exchange(ref observedInput,lastInput.Value);pending=service.GenerateAsync(context,cancel);}
                if(preferences!=null)preferences.RefreshObservation();
                var decision=await pending;var currentInput=ObservationInputTick();
                if(epoch!=Interlocked.Read(ref observationEpoch)||!currentInput.HasValue||currentInput.Value!=lastInput.Value||settingsSignature!=ObservationSettingsSignature(ObservationSettingsSnapshot())){decision.Dispose();return null;}
                Interlocked.Exchange(ref lastObservationReason,(int)decision.Reason);if(mode==DesktopObservationMode.Automatic && (decision.Kind!=ProactiveDecisionKind.Skip || decision.Reason==ObservationReason.ModelSkip || decision.Reason==ObservationReason.DuplicateMessage))lastAutomaticObservationUtc=DateTime.UtcNow;
                if(preferences!=null)preferences.RefreshObservation();return decision;
            } finally { if(service==null)activity.Dispose();else {var ignored=ReleaseObservationActivityAsync(service,activity);} }
        }
        sealed class ObservationCurrentCapture : IDesktopObservationCapture
        {
            readonly PetWindow owner;readonly IDesktopObservationCapture inner;
            internal ObservationCurrentCapture(PetWindow owner,IDesktopObservationCapture inner){this.owner=owner;this.inner=inner;}
            bool Current(){var tick=owner.ObservationInputTick();return tick.HasValue && tick.Value==(uint)Interlocked.Read(ref owner.observedInput);}
            public DesktopCaptureStatus GetStatus(){return inner.GetStatus();}
            public Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel){return Current()?inner.CaptureForegroundAsync(options,cancel):Task.FromResult<DesktopObservationFrame>(null);}
            public bool IsCurrent(DesktopObservationFrame frame){return Current() && inner.IsCurrent(frame);}
        }
        async Task ReleaseObservationActivityAsync(DesktopObservationService service, IDisposable activity)
        { try { await service.WaitForIdleAsync().ConfigureAwait(false); } finally { activity.Dispose();if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(!closing && preferences!=null)preferences.RefreshObservation();})); } }
    }
}
