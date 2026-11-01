using System.Security.Cryptography;
using Dafeiyu;
using Dafeiyu.Maintenance;
using SkiaSharp;

namespace DeskPet.Desktop;

// One application lifetime owns quotas, cancellation and transient event media.
internal sealed class DesktopObservationHost : IDisposable
{
    private readonly object gate=new();
    private readonly DataMaintenanceGate maintenance;
    private readonly IDisposable registration;
    private CancellationTokenSource cancellation=new();
    private long epoch;
    private TimeSpan? lastIdle,lastInputSample;
    private DateTime? lastManualTest;
    private readonly HashSet<Task> ownedActivities=new();
    private bool disposed;
    internal IDesktopObservationCapture Capture {get;}
    private readonly TrackedCapture tracked;
    internal DesktopObservationPolicy Policy {get;}=new();
    internal DesktopObservationService Service {get;}
    internal ProactiveEventStore Events {get;}=new();
    internal event Action? ImagesCleared;
    internal long Epoch=>Volatile.Read(ref epoch);
    internal bool IsCurrent(long stamp)=>stamp==Epoch&&!Volatile.Read(ref disposed);
    internal void ObserveInputIdle(TimeSpan idle,TimeSpan monotonic)
    {
        if(idle==TimeSpan.MaxValue){lastIdle=null;lastInputSample=null;return;}
        // Aggregate idle only. No key, pointer contents or window metadata is read.
        bool changed=lastIdle.HasValue&&lastInputSample.HasValue&&idle+TimeSpan.FromMilliseconds(2)<lastIdle.Value+(monotonic-lastInputSample.Value);
        lastIdle=idle;lastInputSample=monotonic;
        if(changed)Invalidate(false);
    }
    internal DesktopObservationHost(DataMaintenanceGate maintenance,IDesktopObservationCapture capture,Func<IConversationApi>? api=null)
    {
        this.maintenance=maintenance;tracked=new TrackedCapture(capture,()=>Epoch);Capture=tracked;
        Service=new DesktopObservationService(Policy,tracked,api??(()=>new ApiClient(TimeSpan.FromSeconds(8))),sceneFingerprint:SceneFingerprint);
        registration=maintenance.Register(DrainAsync,owner:this);
    }
    internal void Invalidate(bool clearImages=true)
    {
        CancellationTokenSource old;
        lock(gate){if(disposed)return;Interlocked.Increment(ref epoch);old=cancellation;cancellation=new();}
        old.Cancel();old.Dispose();if(clearImages){Volatile.Write(ref tracked.Target,null);Events.ClearImages();ImagesCleared?.Invoke();}
    }
    internal Task<ProactiveDecision> GenerateAsync(PetSettings settings,string companion,string name,IList<ChatMessage>? memory,IList<string>? recent,DateTime local,bool due,bool eligible,DesktopObservationMode mode=DesktopObservationMode.Automatic,Func<bool>? current=null)
    {
        CancellationToken token;long stamp;
        lock(gate){ObjectDisposedException.ThrowIf(disposed,this);token=cancellation.Token;stamp=epoch;}
        var context=new DesktopObservationContext(settings,companion,name,memory,recent,local,due,eligible,()=>IsCurrent(stamp)&&(current==null||current()),mode);
        var activity=maintenance.EnterActivity();
        var release=TrackActivity();return GenerateOwnedAsync(context,token,activity,release);
    }
    internal bool IsForegroundCurrent()
    {
        var target=tracked.Target;if(target==null)return false;
        using var metadata=new DesktopObservationFrame(Array.Empty<byte>(),DateTimeOffset.UtcNow,target);
        return Capture.GetStatus()==DesktopCaptureStatus.Ready&&Capture.IsCurrent(metadata);
    }
    internal async Task<DesktopObservationFrame?> PreviewAsync(PetSettings settings,long stamp,CancellationToken cancel)
    {
        CancellationToken token;lock(gate){token=cancellation.Token;}
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,cancel);
        var activity=maintenance.EnterActivity();var release=TrackActivity();
        DesktopObservationFrame? frame=null;
        try
        {
            if(!IsCurrent(stamp)||!settings.DesktopObservationEnabled||!settings.GreetingEnabled||!settings.AiGreetingEnabled||Capture.GetStatus()!=DesktopCaptureStatus.Ready)return null;
            frame=await Capture.CaptureForegroundAsync(new DesktopCaptureOptions(settings.DesktopObservationExcludedApps),linked.Token).ConfigureAwait(false);
            if(frame==null)return null;
            if(!IsCurrent(stamp)||linked.IsCancellationRequested||!Capture.IsCurrent(frame)||new DesktopCaptureOptions(settings.DesktopObservationExcludedApps).IsExcluded(frame.Target.ApplicationId))return null;
            var result=frame;frame=null;return result;
        }
        finally{frame?.Dispose();ReleaseActivity(activity,release);}
    }
    internal bool TryBeginManualTest(DateTime utcNow)
    {lock(gate){if(lastManualTest.HasValue&&utcNow-lastManualTest.Value<TimeSpan.FromSeconds(10))return false;lastManualTest=utcNow;return true;}}
    private sealed class TrackedCapture(IDesktopObservationCapture inner,Func<long> epoch):IDesktopObservationCapture
    {
        internal DesktopObservationIdentity? Target;
        public DesktopCaptureStatus GetStatus()=>inner.GetStatus();
        public bool IsCurrent(DesktopObservationFrame frame)=>inner.IsCurrent(frame);
        public async Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel)
        {long stamp=epoch();var frame=await inner.CaptureForegroundAsync(options,cancel).ConfigureAwait(false);if(stamp==epoch())Volatile.Write(ref Target,frame?.Target);return frame!;}
    }
    private TaskCompletionSource<bool> TrackActivity()
    {var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);lock(gate)ownedActivities.Add(release.Task);return release;}
    private void ReleaseActivity(IDisposable activity,TaskCompletionSource<bool> release)
    {activity.Dispose();lock(gate)ownedActivities.Remove(release.Task);release.TrySetResult(true);}
    private async Task<ProactiveDecision> GenerateOwnedAsync(DesktopObservationContext context,CancellationToken token,IDisposable activity,TaskCompletionSource<bool> release)
    {
        try{return await Service.GenerateAsync(context,token).ConfigureAwait(false);}
        finally{_ = ReleaseAfterIdleAsync(activity,release);}
    }
    private async Task ReleaseAfterIdleAsync(IDisposable activity,TaskCompletionSource<bool> release)
    {try{await Service.WaitForIdleAsync().ConfigureAwait(false);}finally{ReleaseActivity(activity,release);}}
    internal async Task DrainAsync(){Invalidate();await Service.WaitForIdleAsync().ConfigureAwait(false);Task[] activities;lock(gate)activities=ownedActivities.ToArray();await Task.WhenAll(activities).ConfigureAwait(false);}
    internal static string SceneFingerprint(byte[] png)
    {
        using var source=SKBitmap.Decode(png)??throw new InvalidDataException();
        using var sample=new SKBitmap(16,16,SKColorType.Rgba8888,SKAlphaType.Opaque);
        using(var canvas=new SKCanvas(sample)){canvas.Clear(SKColors.Black);canvas.DrawBitmap(source,new SKRect(0,0,16,16));}
        var pixels=new byte[16*16*3];int offset=0;
        for(int y=0;y<16;y++)for(int x=0;x<16;x++){var c=sample.GetPixel(x,y);pixels[offset++]=(byte)(c.Red>>4);pixels[offset++]=(byte)(c.Green>>4);pixels[offset++]=(byte)(c.Blue>>4);}
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
    public void Dispose()
    {
        Invalidate();lock(gate){if(disposed)return;disposed=true;cancellation.Dispose();}
        registration.Dispose();Events.Dispose();
    }
}
