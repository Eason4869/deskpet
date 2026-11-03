using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public enum DesktopCaptureStatus { Ready, UnsupportedPlatform, PermissionRequired, NonInteractiveDesktop, NoEligibleForeground, Busy }
    public enum DesktopObservationCaptureScope { Application, FullScreen }

    public interface IDesktopObservationCapture
    {
        // A read-only preflight: never prompts for platform permissions.
        DesktopCaptureStatus GetStatus(DesktopCaptureOptions options = null);
        // Null means skip. Cancellation and timeout end the caller's wait, not a native call.
        Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options, CancellationToken cancel);
        bool IsCurrent(DesktopObservationFrame frame);
    }

    public sealed class DesktopCaptureOptions
    {
        private readonly HashSet<string> exclusions;
        public DesktopObservationCaptureScope Scope { get; private set; }
        public DesktopCaptureOptions() : this(null) { }
        public DesktopCaptureOptions(IEnumerable<string> excludedApplications) : this(excludedApplications, DesktopObservationCaptureScope.Application) { }
        public DesktopCaptureOptions(IEnumerable<string> excludedApplications, DesktopObservationCaptureScope scope)
        {
            Scope = scope == DesktopObservationCaptureScope.FullScreen ? scope : DesktopObservationCaptureScope.Application;
            exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludedApplications != null) foreach (string value in excludedApplications)
                if (!String.IsNullOrWhiteSpace(value)) exclusions.Add(value.Trim());
        }
        public bool IsExcluded(string applicationId) { return Scope == DesktopObservationCaptureScope.Application && applicationId != null && exclusions.Contains(applicationId); }
        public const int MaximumDimension = 1280;
        public const int MaximumPixels = 1000000;
        public const int MaximumPngBytes = 2 * 1024 * 1024;
    }

    // Local-only metadata; never include application path/title in model payloads or logs.
    public sealed class DesktopObservationIdentity
    {
        public long WindowId { get; private set; }
        public int ProcessId { get; private set; }
        public long ProcessStartTicks { get; private set; }
        public uint WindowThreadId { get; private set; }
        public string ApplicationId { get; private set; }
        public string WindowTitle { get; private set; }
        public string WindowClass { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string TargetId { get; private set; }
        public string StateVersion { get; private set; }
        public DesktopObservationCaptureScope Scope { get; private set; }
        public DesktopObservationIdentity(long windowId, int processId, long processStartTicks, uint windowThreadId,
            string applicationId, string windowTitle, string windowClass, int x, int y, int width, int height,
            DesktopObservationCaptureScope scope = DesktopObservationCaptureScope.Application)
        {
            Scope = scope;
            WindowId = windowId; ProcessId = processId; ProcessStartTicks = processStartTicks; WindowThreadId = windowThreadId;
            ApplicationId = applicationId; WindowTitle = windowTitle; WindowClass = windowClass;
            X = x; Y = y; Width = width; Height = height;
            TargetId = String.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}", windowId, processId, processStartTicks, windowThreadId);
            // Equality is also checked field by field by the platform adapter, not only by this stamp.
            string state = String.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}:{4}:{5}:{6}:{7}", TargetId, x, y, width, height, applicationId, windowClass, windowTitle);
            using (var hash = SHA256.Create()) StateVersion = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(state)));
        }
    }

    public sealed class DesktopObservationFrame : IDisposable
    {
        private byte[] encodedPng;
        public byte[] EncodedPng { get { return encodedPng; } }
        public DateTimeOffset CapturedAtUtc { get; private set; }
        public DesktopObservationIdentity Target { get; private set; }
        public string TargetStateVersion { get { DesktopObservationIdentity target = Target; return target == null ? null : target.StateVersion; } }
        public bool IsDisposed { get { return encodedPng == null; } }
        // Ownership of encodedPng transfers to this frame. Do not retain aliases after disposal.
        public DesktopObservationFrame(byte[] encodedPng, DateTimeOffset capturedAtUtc, DesktopObservationIdentity target)
        {
            if (encodedPng == null) throw new ArgumentNullException("encodedPng");
            if (target == null) throw new ArgumentNullException("target");
            if (encodedPng.Length > DesktopCaptureOptions.MaximumPngBytes) throw new ArgumentException("Desktop observation image exceeds byte limit.", "encodedPng");
            this.encodedPng = encodedPng; CapturedAtUtc = capturedAtUtc.ToUniversalTime(); Target = target;
        }
        public void Dispose()
        {
            byte[] bytes = Interlocked.Exchange(ref encodedPng, null);
            if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
            Target = null;
        }
    }

    // A worker slot belongs to the underlying synchronous operation until it really returns.
    // This is shared by manual and automatic Windows capture; no replacement worker on timeout.
    internal sealed class DesktopCaptureWorker
    {
        private int busy;
        public bool IsBusy { get { return Interlocked.CompareExchange(ref busy, 0, 0) != 0; } }
        public async Task<byte[]> RunAsync(Func<CancellationToken, byte[]> operation, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException(TextResources.Get("applicationSnapshots.captureAsync.text"));
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            CancellationToken token = lifetime.Token;
            Task<byte[]> capture;
            try
            {
                capture = Task.Factory.StartNew(delegate
                {
                    byte[] bytes = null;
                    try
                    {
                        bytes = operation(token);
                        token.ThrowIfCancellationRequested();
                        byte[] result = bytes; bytes = null; return result;
                    }
                    finally
                    {
                        if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                        Interlocked.Exchange(ref busy, 0);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch { lifetime.Dispose(); Interlocked.Exchange(ref busy, 0); throw; }
            try { return await WaitAsync(capture, cancel).ConfigureAwait(false); }
            catch
            {
                lifetime.Cancel();
                Continue(capture, delegate(Task<byte[]> finished)
                {
                    if (finished.Status == TaskStatus.RanToCompletion && finished.Result != null) Array.Clear(finished.Result, 0, finished.Result.Length);
                    else if (finished.IsFaulted) { var ignored = finished.Exception; }
                });
                throw;
            }
            finally
            {
                Continue(capture, delegate { lifetime.Dispose(); });
            }
        }
        private static void Continue(Task<byte[]> capture, Action<Task<byte[]>> cleanup)
        {
            capture.ContinueWith(cleanup, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        internal static async Task<byte[]> WaitAsync(Task<byte[]> capture, CancellationToken cancel)
        {
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                Task finished = await Task.WhenAny(capture, Task.Delay(2000, timer.Token)).ConfigureAwait(false);
                if (finished == capture)
                {
                    timer.Cancel(); cancel.ThrowIfCancellationRequested(); return await capture.ConfigureAwait(false);
                }
                cancel.ThrowIfCancellationRequested(); throw new TimeoutException(TextResources.Get("applicationSnapshots.waitForCaptureAsync.text"));
            }
        }
    }
}
