using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dafeiyu;

namespace DeskPet.Platform.Mac;

internal interface IMacDesktopCaptureNative
{
    DesktopCaptureStatus GetStatus();
    DesktopObservationIdentity? GetForeground();
    bool IsCurrent(DesktopObservationIdentity target);
    // Accepted calls complete exactly once, including errors. Refused calls never callback.
    // Byte-array ownership transfers to completed, including late completions.
    bool Begin(DesktopObservationIdentity target, Action<byte[]?> completed);
}

public sealed class MacDesktopObservation : IDesktopObservationCapture
{
    private readonly IMacDesktopCaptureNative native;
    private static int busy;
    public MacDesktopObservation() : this(new MacDesktopCaptureNative()) { }
    internal MacDesktopObservation(IMacDesktopCaptureNative native) { this.native = native; }
    public DesktopCaptureStatus GetStatus() => Volatile.Read(ref busy) != 0 ? DesktopCaptureStatus.Busy : native.GetStatus();
    public bool IsCurrent(DesktopObservationFrame frame) => frame?.Target is { } target && native.IsCurrent(target);

    public async Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancel.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return null!;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var token = lifetime.Token;
        // Includes read-only native preflight in the deadline, without blocking the UI thread.
        var work = Task.Run(async () =>
        {
            byte[]? bytes = null;
            try
            {
                if (token.IsCancellationRequested || native.GetStatus() != DesktopCaptureStatus.Ready) return null;
                var target = native.GetForeground();
                if (target == null || options.IsExcluded(target.ApplicationId) || !native.IsCurrent(target) || token.IsCancellationRequested) return null;
                var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!native.Begin(target, value => completion.SetResult(value))) return null;
                bytes = await completion.Task.ConfigureAwait(false);
                // A finished caller no longer needs native/UI currentness; clear late bytes in finally.
                if (token.IsCancellationRequested) return null;
                if (bytes == null || !ValidPngBounds(bytes) || !native.IsCurrent(target)) return null;
                var frame = new DesktopObservationFrame(bytes, DateTimeOffset.UtcNow, target);
                bytes = null;
                return frame;
            }
            finally
            {
                if (bytes != null) Array.Clear(bytes);
                Volatile.Write(ref busy, 0);
            }
        });
        try { return (await work.WaitAsync(TimeSpan.FromSeconds(2), cancel).ConfigureAwait(false))!; }
        catch
        {
            lifetime.Cancel();
            // The worker and callback remain rooted until native completion. Never free a
            // callback/context on timeout, nor release the slot while ScreenCaptureKit runs.
            _ = work.ContinueWith(done =>
            {
                if (done.Status == TaskStatus.RanToCompletion) done.Result?.Dispose();
                else if (done.IsFaulted) _ = done.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        finally
        {
            _ = work.ContinueWith(_ => lifetime.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static bool ValidPngBounds(byte[] bytes)
    {
        if (bytes.Length < 33 || bytes.Length > DesktopCaptureOptions.MaximumPngBytes ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8)) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return false;
        int width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)), height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        return width > 0 && height > 0 && width <= DesktopCaptureOptions.MaximumDimension && height <= DesktopCaptureOptions.MaximumDimension &&
            (long)width * height <= DesktopCaptureOptions.MaximumPixels;
    }
}

internal sealed class MacDesktopCaptureNative : IMacDesktopCaptureNative
{
    // Process-lifetime root: native can callback after any managed caller timeout/GC.
    private static readonly Native.ObservationCallback Callback = Complete;
    public DesktopCaptureStatus GetStatus() => MacServices.IsSupported ? (DesktopCaptureStatus)Native.ObservationStatus() : DesktopCaptureStatus.UnsupportedPlatform;
    public DesktopObservationIdentity? GetForeground()
    {
        if (!MacServices.IsSupported) return null;
        var pointer = Native.ObservationTarget();
        if (pointer == IntPtr.Zero) return null;
        try { return JsonSerializer.Deserialize<DesktopObservationIdentity>(Marshal.PtrToStringUTF8(pointer)!); }
        finally { Native.ObservationTargetFree(pointer); }
    }
    public bool IsCurrent(DesktopObservationIdentity target) => MacServices.IsSupported && Native.ObservationCurrent(JsonSerializer.Serialize(target)) != 0;
    public bool Begin(DesktopObservationIdentity target, Action<byte[]?> completed)
    {
        if (!MacServices.IsSupported) return false;
        var root = GCHandle.Alloc(completed);
        try
        {
            if (Native.ObservationBegin(JsonSerializer.Serialize(target), Callback, GCHandle.ToIntPtr(root)) != 0) return true;
        }
        catch { root.Free(); throw; }
        root.Free(); return false;
    }
    private static void Complete(IntPtr context, int status, IntPtr data, int length)
    {
        var root = GCHandle.FromIntPtr(context);
        byte[]? bytes = null;
        try
        {
            if (status == 0 && data != IntPtr.Zero && length > 0 && length <= DesktopCaptureOptions.MaximumPngBytes)
            { bytes = new byte[length]; Marshal.Copy(data, bytes, 0, length); }
            var completed = (Action<byte[]?>)root.Target!;
            completed(bytes); bytes = null;
        }
        catch { /* Never unwind through Objective-C; native still owns its borrowed buffer. */ }
        finally { if (bytes != null) Array.Clear(bytes); root.Free(); }
    }
}
