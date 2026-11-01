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
    private bool disposed;
    internal IDesktopObservationCapture Capture {get;}
    private readonly TrackedCapture tracked;
    internal DesktopObservationPolicy Policy {get;}=new();
    internal DesktopObservationService Service {get;}
    internal ProactiveEventStore Events {get;}=new();
    internal long Epoch=>Volatile.Read(ref epoch);
    internal bool IsCurrent(long stamp)=>stamp==Epoch&&!Volatile.Read(ref disposed);
    internal DesktopObservationHost(DataMaintenanceGate maintenance,IDesktopObservationCapture capture,Func<IConversationApi>? api=null)
    {
        this.maintenance=maintenance;tracked=new TrackedCapture(capture);Capture=tracked;
        Service=new DesktopObservationService(Policy,tracked,api??(()=>new ApiClient(TimeSpan.FromSeconds(8))),sceneFingerprint:SceneFingerprint);
        registration=maintenance.Register(DrainAsync,owner:this);
    }
    internal void Invalidate(bool clearImages=true)
    {
        CancellationTokenSource old;
        lock(gate){if(disposed)return;Interlocked.Increment(ref epoch);old=cancellation;cancellation=new();}
        old.Cancel();old.Dispose();if(clearImages)Events.ClearImages();
    }
    internal Task<ProactiveDecision> GenerateAsync(PetSettings settings,string companion,string name,IList<ChatMessage>? memory,IList<string>? recent,DateTime local,bool due,bool eligible,DesktopObservationMode mode=DesktopObservationMode.Automatic)
    {
        CancellationToken token;long stamp;
        lock(gate){ObjectDisposedException.ThrowIf(disposed,this);token=cancellation.Token;stamp=epoch;}
        var context=new DesktopObservationContext(settings,companion,name,memory,recent,local,due,eligible,()=>IsCurrent(stamp),mode);
        var activity=maintenance.EnterActivity();
        return GenerateOwnedAsync(context,token,activity);
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
        using var activity=maintenance.EnterActivity();
        if(!IsCurrent(stamp)||!settings.DesktopObservationEnabled||Capture.GetStatus()!=DesktopCaptureStatus.Ready)return null;
        var frame=await Capture.CaptureForegroundAsync(new DesktopCaptureOptions(settings.DesktopObservationExcludedApps),linked.Token).ConfigureAwait(false);
        if(frame==null)return null;
        if(!IsCurrent(stamp)||linked.IsCancellationRequested||!Capture.IsCurrent(frame)||new DesktopCaptureOptions(settings.DesktopObservationExcludedApps).IsExcluded(frame.Target.ApplicationId)){frame.Dispose();return null;}
        return frame;
    }
    private sealed class TrackedCapture(IDesktopObservationCapture inner):IDesktopObservationCapture
    {
        internal DesktopObservationIdentity? Target;
        public DesktopCaptureStatus GetStatus()=>inner.GetStatus();
        public bool IsCurrent(DesktopObservationFrame frame)=>inner.IsCurrent(frame);
        public async Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel)
        {var frame=await inner.CaptureForegroundAsync(options,cancel).ConfigureAwait(false);Volatile.Write(ref Target,frame?.Target);return frame!;}
    }
    private async Task<ProactiveDecision> GenerateOwnedAsync(DesktopObservationContext context,CancellationToken token,IDisposable activity)
    {
        try{return await Service.GenerateAsync(context,token).ConfigureAwait(false);}
        finally{_ = ReleaseAfterIdleAsync(activity);}
    }
    private async Task ReleaseAfterIdleAsync(IDisposable activity)
    {try{await Service.WaitForIdleAsync().ConfigureAwait(false);}finally{activity.Dispose();}}
    internal async Task DrainAsync(){Invalidate();await Service.WaitForIdleAsync().ConfigureAwait(false);}
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
