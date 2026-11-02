using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#if !DESKTOP_OBSERVATION_PORTABLE
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
#endif
namespace Dafeiyu.Tests
{
    public static class DesktopObservationCaptureTests
    {
        static int assertions;
        static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        [STAThread] public static int Main(string[] args)
        {
            try
            {
#if !DESKTOP_OBSERVATION_PORTABLE
                if (args.Length > 0 && args[0] == "--fixture") return Fixture(args);
                if (args.Length > 0 && args[0] == "--full-screen-only") { FullScreen();Console.WriteLine("Full-screen capture checks: "+assertions+" passed");return 0; }
#endif
                if (args.Length > 0 && args[0] == "--adapter-timeout-only") { FinalValidation(false); return 0; }
                if (args.Length > 0 && args[0] == "--adapter-cancel-only") { FinalValidation(true); return 0; }
                Contracts(); Worker(); FinalValidation(false); FinalValidation(true);
#if !DESKTOP_OBSERVATION_PORTABLE
                Native();
                FullScreen();
#endif
                Console.WriteLine("DesktopObservationCaptureTests: " + assertions + " assertions passed."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        static void Contracts()
        {
            var exclusions = new[] { "C:\\fixture\\app.exe" };
            var options = new DesktopCaptureOptions(exclusions); exclusions[0] = "changed";
            Check(options.IsExcluded("c:\\FIXTURE\\app.exe"), "exclusion uses stable identity, copied at construction");
            Check(!options.IsExcluded("app"), "process name does not substitute executable identity");
            var full=new DesktopCaptureOptions(new[]{"C:\\fixture\\app.exe"},DesktopObservationCaptureScope.FullScreen);
            Check(!full.IsExcluded("C:\\fixture\\app.exe"),"full screen exclusions do not suppress capture");
            var identity = new DesktopObservationIdentity(42, 7, 900, 8, "app", "title", "class", -100, 20, 800, 600);
            byte[] bytes = { 137, 80, 78, 71 };
            var frame = new DesktopObservationFrame(bytes, DateTimeOffset.UtcNow, identity);
            Check(frame.Target.WindowId == 42 && frame.Target.X == -100 && frame.TargetStateVersion != null, "frame preserves exact identity, bounds and state");
            bool oversized = false;
            try { new DesktopObservationFrame(new byte[2 * 1024 * 1024 + 1], DateTimeOffset.UtcNow, identity); }
            catch (ArgumentException) { oversized = true; }
            Check(oversized, "shared frame refuses images above automatic byte bound");
            frame.Dispose(); frame.Dispose();
            Check(frame.EncodedPng == null && frame.Target == null && frame.TargetStateVersion == null && frame.IsDisposed && bytes[0] == 0, "dispose clears owned bytes and reference, idempotently");
        }
#if !DESKTOP_OBSERVATION_PORTABLE
        static void FullScreen()
        {
            var capture=new WindowsDesktopObservationCapture();
            var options=new DesktopCaptureOptions(new[]{Process.GetCurrentProcess().MainModule.FileName},DesktopObservationCaptureScope.FullScreen);
            Check(capture.GetStatus(options)==DesktopCaptureStatus.Ready,"full screen preflight does not require an eligible foreground application");
            using(var frame=capture.CaptureForegroundAsync(options,CancellationToken.None).GetAwaiter().GetResult())
            {
                Check(frame!=null && frame.Target.Scope==DesktopObservationCaptureScope.FullScreen && frame.Target.WindowId==0,"full screen produces a desktop frame instead of foreground window");
                var bounds=ApplicationSnapshots.ObservationTarget(options).ObservationIdentity;
                Check(frame.Target.X==bounds.X && frame.Target.Y==bounds.Y && frame.Target.Width==bounds.Width && frame.Target.Height==bounds.Height,"full screen frame covers all monitors including desktop origin");
                Check(capture.IsCurrent(frame),"full screen currentness validates desktop geometry");
                Console.WriteLine("Full-screen desktop: "+frame.Target.Width+"x"+frame.Target.Height+" at "+frame.Target.X+","+frame.Target.Y+"; PNG bytes="+frame.EncodedPng.Length);
                using(var stream=new MemoryStream(frame.EncodedPng))using(var image=new Bitmap(stream))Check(image.Width<=1280 && image.Height<=1280 && (long)image.Width*image.Height<=1000000 && frame.EncodedPng.Length<=2*1024*1024,"full screen uses automatic image bounds");
            }
            var locked=new WindowsDesktopObservationCapture(()=>false);Check(locked.CaptureForegroundAsync(options,CancellationToken.None).GetAwaiter().GetResult()==null,"full screen still refuses a locked desktop");
        }
#endif
        static void Worker()
        {
            var worker = new DesktopCaptureWorker(); var release = new ManualResetEventSlim();
            var started = new ManualResetEventSlim(); byte[] late = { 1, 2, 3 }; int calls = 0;
            var watch = Stopwatch.StartNew(); bool timeout = false;
            try { worker.RunAsync(delegate(CancellationToken token) { Interlocked.Increment(ref calls); started.Set(); release.Wait(); return late; }, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (TimeoutException) { timeout = true; }
            Check(timeout && started.IsSet && watch.ElapsedMilliseconds >= 1800 && watch.ElapsedMilliseconds < 3500, "pending worker returns after two second watchdog");
            bool busy = false;
            try { worker.RunAsync(delegate(CancellationToken token) { Interlocked.Increment(ref calls); return late; }, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { busy = true; }
            Check(busy && worker.IsBusy && calls == 1, "timed out native worker still owns the only slot");
            release.Set(); SpinWait.SpinUntil(delegate { return !worker.IsBusy; }, 3000);
            Check(!worker.IsBusy && late[0] == 0, "late encoded bytes cleared before worker slot released");
            using (var cancel = new CancellationTokenSource())
            {
                var release2 = new ManualResetEventSlim(); byte[] cancelledBytes = { 4 };
                cancel.CancelAfter(80); watch.Restart(); bool cancelled = false;
                try { worker.RunAsync(delegate(CancellationToken token) { release2.Wait(); return cancelledBytes; }, cancel.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && watch.ElapsedMilliseconds < 1000 && worker.IsBusy, "cancellation promptly returns without releasing native worker");
                release2.Set(); SpinWait.SpinUntil(delegate { return !worker.IsBusy; }, 3000);
                Check(cancelledBytes[0] == 0, "cancelled late image discarded"); release2.Dispose();
            }
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel(); bool cancelled = false;
                try { worker.RunAsync(delegate(CancellationToken token) { calls++; return new byte[1]; }, cancel.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && calls == 1 && !worker.IsBusy, "pre-cancel never starts capture");
            }
            byte[] success = worker.RunAsync(delegate(CancellationToken token) { return new byte[] { 9 }; }, CancellationToken.None).GetAwaiter().GetResult();
            Check(success[0] == 9 && !worker.IsBusy, "completed capture transfers live bytes and permits next capture");
            release.Dispose(); started.Dispose();
        }
        // Inject only native boundaries; exercise the actual adapter's deadline, gate and byte ownership.
#if DESKTOP_OBSERVATION_PORTABLE
#pragma warning disable CA1416
#endif
        static void FinalValidation(bool cancellation)
        {
            var worker = new DesktopCaptureWorker();
            using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim())
            using (var cancel = new CancellationTokenSource())
            {
                byte[] encoded = { 137, 80, 78, 71 }; int selections = 0, encodings = 0;
                var identity = new DesktopObservationIdentity(42, 7, 900, 8, "C:\\fixture\\app.exe", "title", "class", 0, 0, 800, 600);
                var target = new ApplicationSnapshotTarget { ObservationIdentity = identity };
                var adapter = new WindowsDesktopObservationCapture(delegate { return true; }, worker,
                    delegate(DesktopCaptureOptions options) { Interlocked.Increment(ref selections); return target; },
                    delegate(ApplicationSnapshotTarget value, CancellationToken token) { Interlocked.Increment(ref encodings); return encoded; },
                    delegate(DesktopObservationIdentity value) { entered.Set(); release.Wait(); return true; });
                Task<DesktopObservationFrame> capture = null, second = null;
                var watch = Stopwatch.StartNew();
                try
                {
                    capture = Task.Factory.StartNew(delegate { return adapter.CaptureForegroundAsync(new DesktopCaptureOptions(), cancel.Token); }).Unwrap();
                    Check(entered.Wait(1000) && encodings == 1 && encoded[0] == 137, "adapter completed encoding before pending final validation");
                    if (cancellation) cancel.Cancel();
                    Check(SpinWait.SpinUntil(delegate { return capture.IsCompleted; }, cancellation ? 1000 : 3200),
                        cancellation ? "adapter cancellation returns while final validation remains pending" : "adapter deadline includes pending final validation");
                    bool expectedFailure = false;
                    try { var unexpected = capture.GetAwaiter().GetResult(); if (unexpected != null) unexpected.Dispose(); }
                    catch (TimeoutException) { expectedFailure = !cancellation; }
                    catch (OperationCanceledException) { expectedFailure = cancellation; }
                    Check(expectedFailure && watch.ElapsedMilliseconds < (cancellation ? 1500 : 3500) && (cancellation || watch.ElapsedMilliseconds >= 1800),
                        "adapter reports timeout/cancellation without delivering a frame");
                    Check(worker.IsBusy, "pending final validation retains the same worker slot");
                    second = Task.Factory.StartNew(delegate { return adapter.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None); }).Unwrap();
                    Check(SpinWait.SpinUntil(delegate { return second.IsCompleted; }, 500), "second capture fails promptly while final validation owns worker");
                    bool busy = false;
                    try { var unexpected = second.GetAwaiter().GetResult(); if (unexpected != null) unexpected.Dispose(); }
                    catch (InvalidOperationException) { busy = true; }
                    Check(busy && selections == 1 && encodings == 1, "pending final validation starts no second capture or encoding worker");
                    release.Set();
                    Check(SpinWait.SpinUntil(delegate { return !worker.IsBusy; }, 1500) && encoded[0] == 0 && encoded[3] == 0,
                        "late final validation clears encoded bytes before worker release");
                    Console.WriteLine("Adapter final validation " + (cancellation ? "cancellation" : "timeout") + ": passed.");
                }
                finally
                {
                    release.Set();
                    foreach (var pending in new[] { capture, second }) if (pending != null)
                    {
                        SpinWait.SpinUntil(delegate { return pending.IsCompleted; }, 1500);
                        if (pending.Status == TaskStatus.RanToCompletion && pending.Result != null) pending.Result.Dispose();
                        else if (pending.IsFaulted) { var ignored = pending.Exception; }
                    }
                    SpinWait.SpinUntil(delegate { return !worker.IsBusy; }, 1500);
                }
            }
        }
#if DESKTOP_OBSERVATION_PORTABLE
#pragma warning restore CA1416
#endif
#if !DESKTOP_OBSERVATION_PORTABLE
        static int Fixture(string[] args)
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            // Keep a CI-like size ceiling on the synthetic window regardless of the host display.
            using (var form = new Form { Text = args[1], StartPosition = FormStartPosition.Manual, Location = new Point(150,150), Size = new Size(420,300), MaximumSize = new Size(800,600), FormBorderStyle = FormBorderStyle.None, BackColor = args.Length > 2 && args[2] == "black" ? Color.Black : Color.CadetBlue })
            {
                var timer = new System.Windows.Forms.Timer { Interval = 25000 }; timer.Tick += delegate { form.Close(); }; timer.Start();
                form.Shown += delegate
                {
                    AllowSetForegroundWindow(-1);
                    if(args.Length > 2 && args[2] == "protected" && !SetWindowDisplayAffinity(form.Handle, 0x11)) throw new Exception("Fixture protection could not be set.");
                };
                Application.Run(form); timer.Dispose();
            }
            return 0;
        }
        static Process StartFixture(string mode)
        {
            var process = Process.Start(new ProcessStartInfo { FileName = typeof(DesktopObservationCaptureTests).Assembly.Location, Arguments = "--fixture \"Observation synthetic " + Guid.NewGuid().ToString("N") + "\" " + mode, UseShellExecute = false });
            Check(SpinWait.SpinUntil(delegate { process.Refresh(); return process.MainWindowHandle != IntPtr.Zero; }, 5000), "controlled fixture visible"); return process;
        }
        static void Foreground(Process process)
        {
            Check(SpinWait.SpinUntil(delegate { Activate(process.MainWindowHandle); return GetForegroundWindow() == process.MainWindowHandle; }, 2000), "controlled fixture owns foreground before any capture (expected=" + process.MainWindowHandle + ", actual=" + GetForegroundWindow() + ")");
        }
        static void Activate(IntPtr window)
        {
            uint ignored; uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            uint ownThread = GetCurrentThreadId(); bool attached = foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
            try { SetForegroundWindow(window); }
            finally { if(attached) AttachThreadInput(ownThread, foregroundThread, false); }
        }
        static void Native()
        {
            AutomaticEncodingLimits();
            SetProcessDpiAwarenessContext(new IntPtr(-4)); Process fixture = null, background = null, black = null, protectedWindow = null;
            var capture = new WindowsDesktopObservationCapture();
            try
            {
                fixture = StartFixture(""); background = StartFixture(""); Foreground(fixture);
                SetWindowPos(background.MainWindowHandle, new IntPtr(-1), 0,0,0,0, 0x13);
                using (var frame = capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult())
                {
                    Check(frame != null && frame.Target.ProcessId == fixture.Id && capture.IsCurrent(frame), "foreground chosen despite background topmost");
                    Check(frame.CapturedAtUtc <= DateTimeOffset.UtcNow && frame.CapturedAtUtc > DateTimeOffset.UtcNow.AddSeconds(-4), "frame capture time recorded");
                    var id = frame.Target;
                    using(var reused = new DesktopObservationFrame(new byte[] { 1 }, DateTimeOffset.UtcNow,
                        new DesktopObservationIdentity(id.WindowId, id.ProcessId, id.ProcessStartTicks + 1, id.WindowThreadId, id.ApplicationId, id.WindowTitle, id.WindowClass, id.X, id.Y, id.Width, id.Height)))
                        Check(!capture.IsCurrent(reused), "same HWND/PID with different process lifetime rejected as reused identity");
                    SetWindowText(fixture.MainWindowHandle, "changed synthetic title");
                    Check(!capture.IsCurrent(frame), "title change invalidates pending image");
                }
                SetWindowText(fixture.MainWindowHandle, new string('x', 1100) + "A"); Foreground(fixture);
                using(var frame = capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult())
                {
                    SetWindowText(fixture.MainWindowHandle, new string('x', 1100) + "B");
                    Check(!capture.IsCurrent(frame), "title change after first 1024 characters invalidates image");
                }
                Foreground(fixture);
                Console.WriteLine("Native fixture DPI: " + GetDpiForWindow(fixture.MainWindowHandle));
                var excluded = new DesktopCaptureOptions(new[] { fixture.MainModule.FileName });
                Check(capture.CaptureForegroundAsync(excluded, CancellationToken.None).GetAwaiter().GetResult() == null, "stable executable path exclusion prevents image");
                // Native geometry is independent of encoder limits and fits a small CI desktop.
                bool positioned = SetWindowPos(fixture.MainWindowHandle, IntPtr.Zero, -100, 80, 640, 480, 0x14);
                int positionError = Marshal.GetLastWin32Error();
                Foreground(fixture);
                NativeRect bounds;
                bool measured = GetWindowRect(fixture.MainWindowHandle, out bounds);
                Console.WriteLine("Native requested=(-100,80,640,480), SetWindowPos=" + positioned + ", error=" + positionError +
                    ", GetWindowRect=" + measured + ", actual=" + bounds + ", maxTrack=" + SystemInformation.MaxWindowTrackSize +
                    ", virtualScreen=" + SystemInformation.VirtualScreen + ", alive=" + !fixture.HasExited + ", foreground=" + (GetForegroundWindow() == fixture.MainWindowHandle));
                Check(positioned && measured && bounds.Left == -100 && bounds.Top == 80 && bounds.Right - bounds.Left == 640 && bounds.Bottom - bounds.Top == 480,
                    "controlled negative-coordinate fixture reaches requested physical bounds");
                using (var frame = capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult())
                {
                    Console.WriteLine("Native captured=" + (frame == null ? "null" : "(" + frame.Target.X + "," + frame.Target.Y + "," + frame.Target.Width + "," + frame.Target.Height + ")"));
                    Check(frame != null && frame.Target.X == bounds.Left && frame.Target.Y == bounds.Top &&
                        frame.Target.Width == bounds.Right - bounds.Left && frame.Target.Height == bounds.Bottom - bounds.Top,
                        "captured physical bounds exactly match negative-coordinate native window");
                    using(var stream = new MemoryStream(frame.EncodedPng)) using(var image = new Bitmap(stream))
                        Check(image.Width <= 1280 && image.Height <= 1280 && (long)image.Width * image.Height <= 1000000 && frame.EncodedPng.Length <= 2 * 1024 * 1024, "automatic PNG enforces independent size limits");
                    Check(SetWindowPos(fixture.MainWindowHandle, IntPtr.Zero, -100, 80, 700, 500, 0x14), "controlled fixture resize succeeds");
                    Check(!capture.IsCurrent(frame), "resize invalidates frame");
                }
                using(var own = new Form { Text = "Own synthetic window", Size = new Size(300,200) })
                {
                    own.Show(); Activate(own.Handle); Application.DoEvents();
                    Check(GetForegroundWindow() == own.Handle && capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult() == null, "own foreground skips without picking background");
                }
                Foreground(fixture);
                using(var frame = capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult())
                {
                    Foreground(background); Check(!capture.IsCurrent(frame), "background scene rejected before upload");
                }
                Foreground(fixture);
                using(var frame = capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult())
                {
                    var unavailable = new WindowsDesktopObservationCapture(delegate { return false; });
                    Check(unavailable.GetStatus() == DesktopCaptureStatus.NonInteractiveDesktop && !unavailable.IsCurrent(frame), "locked or noninteractive desktop invalidates frame");
                    Check(unavailable.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult() == null, "locked desktop does not capture");
                    fixture.Kill(); fixture.WaitForExit(3000); Check(!capture.IsCurrent(frame), "closed or reused window rejected");
                }
                protectedWindow = StartFixture("protected"); Foreground(protectedWindow);
                Check(capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult() == null, "explicit capture-protected window skipped without capture fallback");
                black = StartFixture("black"); Foreground(black); bool rejected = false;
                try { capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult(); }
                catch(InvalidOperationException) { rejected = true; }
                Check(rejected, "black/protected rendering rejected without widening scope");
            }
            finally { foreach(var p in new[] { fixture, background, black, protectedWindow }) if(p != null) { try { if(!p.HasExited) { p.Kill(); p.WaitForExit(3000); } } finally { p.Dispose(); } } }
        }
        static void AutomaticEncodingLimits()
        {
            // These inputs exceed each automatic limit independently of the native display size.
            using (var wide = new Bitmap(4000, 1000))
            {
                using (var graphics = Graphics.FromImage(wide)) graphics.Clear(Color.CadetBlue);
                byte[] png = ApplicationSnapshots.Encode(wide, CancellationToken.None, true);
                using (var stream = new MemoryStream(png)) using (var image = new Bitmap(stream))
                    Check(image.Width == 1280 && image.Height == 320, "automatic encoder reduces oversized long edge to 1280");
            }
            using (var square = new Bitmap(1600, 1600))
            {
                using (var graphics = Graphics.FromImage(square)) graphics.Clear(Color.CadetBlue);
                byte[] png = ApplicationSnapshots.Encode(square, CancellationToken.None, true);
                using (var stream = new MemoryStream(png)) using (var image = new Bitmap(stream))
                    Check(image.Width == 1000 && image.Height == 1000, "automatic encoder independently reduces area to one million pixels");
            }
            using (var noisy = new Bitmap(1250, 800, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                var data = noisy.LockBits(new Rectangle(0, 0, 1250, 800), System.Drawing.Imaging.ImageLockMode.WriteOnly, noisy.PixelFormat);
                try { var pixels = new byte[data.Stride * 800]; new Random(7348).NextBytes(pixels); Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
                finally { noisy.UnlockBits(data); }
                byte[] manual = ApplicationSnapshots.Encode(noisy, CancellationToken.None, false);
                Check(manual.Length > 2 * 1024 * 1024, "synthetic noise exceeds automatic byte cap while already within dimension and pixel caps");
                byte[] automatic = ApplicationSnapshots.Encode(noisy, CancellationToken.None, true);
                using (var stream = new MemoryStream(automatic)) using (var image = new Bitmap(stream))
                    Check(automatic.Length <= 2 * 1024 * 1024 && image.Width < 1250 && image.Height < 800,
                        "automatic encoder reduces high entropy image to two MiB byte cap");
            }
        }
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool SetWindowText(IntPtr window, string text);
        struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            public override string ToString() { return "(" + Left + "," + Top + "," + (Right - Left) + "," + (Bottom - Top) + ")"; }
        }
        [DllImport("user32.dll", SetLastError=true)] static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
        [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
#endif
    }
}
