using System.Buffers.Binary;
using Dafeiyu;
using DeskPet.Platform.Mac;

internal static class DesktopObservationCaptureTests
{
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS observation: " + name); }
    private static async Task Reject<T>(Task task) where T : Exception { try { await task; } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    internal static async Task Run()
    {
        if (!MacServices.IsSupported)
        {
            var unsupported = new MacDesktopObservation();
            Check(unsupported.GetStatus() == DesktopCaptureStatus.UnsupportedPlatform, "unsupported guard before native load");
            try { await MacServices.RequestScreenCaptureAccessAsync(); throw new Exception("permission request was not guarded"); } catch (PlatformNotSupportedException) { Check(true, "explicit permission request guarded before native load"); }
            Check(await unsupported.CaptureForegroundAsync(new(), default) == null, "unsupported skips");
        }
        var native = new Fake { Status = DesktopCaptureStatus.PermissionRequired };
        var capture = new MacDesktopObservation(native);
        Check(capture.GetStatus() == DesktopCaptureStatus.PermissionRequired, "read-only permission status");
        Check(await capture.CaptureForegroundAsync(new(), default) == null && native.Starts == 0, "denied never starts native capture");
        native.Status = DesktopCaptureStatus.Ready;
        Check(await capture.CaptureForegroundAsync(new(["com.example.front"]), default) == null && native.Starts == 0, "bundle exclusion before screenshot");
        var pending = capture.CaptureForegroundAsync(new(), default);
        await native.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var other = new MacDesktopObservation(new Fake());
        Check(other.GetStatus() == DesktopCaptureStatus.Busy && await other.CaptureForegroundAsync(new(), default) == null, "one slot across adapters");
        var png = Png(320, 200); native.Complete(png);
        using (var frame = await pending)
        {
            Check(frame != null && capture.IsCurrent(frame), "valid current result delivered");
            native.Current = false; Check(!capture.IsCurrent(frame!), "changed foreground invalidates"); native.Current = true;
            frame!.Dispose(); Check(png.All(b => b == 0) && frame.Target == null, "frame disposal clears content and identity");
        }
        foreach (var bytes in new[] { Png(1281, 20), Png(1100, 1000), Png(0, 2), new byte[2097153], new byte[24] })
        {
            native.Reset(); pending = capture.CaptureForegroundAsync(new(), default); await native.Started.Task;
            native.Complete(bytes); Check(await pending == null && bytes.All(b => b == 0), "invalid image rejected and cleared");
        }
        native.Reset(); pending = capture.CaptureForegroundAsync(new(), default); await native.Started.Task;
        native.Current = false; png = Png(32, 20); native.Complete(png);
        Check(await pending == null && png.All(b => b == 0), "stale delivery cleared"); native.Current = true;
        native.Reset(); using var cancel = new CancellationTokenSource();
        pending = capture.CaptureForegroundAsync(new(), cancel.Token); await native.Started.Task; cancel.Cancel();
        await Reject<OperationCanceledException>(pending);
        Check(other.GetStatus() == DesktopCaptureStatus.Busy, "cancel retains native slot");
        GC.Collect(); GC.WaitForPendingFinalizers(); png = Png(32, 20); native.Complete(png);
        await Eventually(() => png.All(b => b == 0) && capture.GetStatus() == DesktopCaptureStatus.Ready);
        Check(true, "late cancellation callback clears content and releases slot");
        native.Reset(); pending = capture.CaptureForegroundAsync(new(), default); await native.Started.Task;
        await Reject<TimeoutException>(pending);
        Check(other.GetStatus() == DesktopCaptureStatus.Busy, "2 second deadline retains native slot");
        png = Png(32, 20); native.Complete(png);
        await Eventually(() => png.All(b => b == 0) && capture.GetStatus() == DesktopCaptureStatus.Ready);
        Check(true, "late timeout completion clears bytes");
        native.Reset(); native.PreflightGate = new ManualResetEventSlim(false); native.Starts = 0;
        pending = capture.CaptureForegroundAsync(new(), default);
        await Reject<TimeoutException>(pending);
        native.PreflightGate.Set();
        await Eventually(() => native.Starts != 0 || capture.GetStatus() == DesktopCaptureStatus.Ready);
        if (native.Starts != 0) native.Complete(Png(32, 20));
        Check(native.Starts == 0, "expired preflight never starts a new native screenshot");
        native.PreflightGate.Dispose(); native.PreflightGate = null;
        native.Reset(); native.Accept = false;
        Check(await capture.CaptureForegroundAsync(new(), default) == null && capture.GetStatus() == DesktopCaptureStatus.Ready, "native refusal releases slot");
    }
    private static async Task Eventually(Func<bool> condition)
    { using var timeout = new CancellationTokenSource(2000); while (!condition()) await Task.Delay(10, timeout.Token); }
    private static byte[] Png(int width, int height)
    {
        var png = new byte[33]; new byte[] {137,80,78,71,13,10,26,10}.CopyTo(png,0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8),13); "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16),width); BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20),height); return png;
    }
    private sealed class Fake : IMacDesktopCaptureNative
    {
        internal DesktopCaptureStatus Status = DesktopCaptureStatus.Ready;
        internal bool Current = true, Accept = true;
        internal int Starts;
        internal ManualResetEventSlim? PreflightGate;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<byte[]?>? completion;
        public DesktopCaptureStatus GetStatus() { PreflightGate?.Wait(); return Status; }
        public DesktopObservationIdentity GetForeground() => new(123, 456, 789, 0, "com.example.front", "private", "", 0, 0, 800, 600);
        public bool IsCurrent(DesktopObservationIdentity target) => Current;
        public bool Begin(DesktopObservationIdentity target, Action<byte[]?> completed) { Starts++; completion = completed; Started.TrySetResult(); return Accept; }
        internal void Complete(byte[] bytes) => completion!(bytes);
        internal void Reset() { Started = new(TaskCreationOptions.RunContinuationsAsynchronously); completion = null; }
    }
}
