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
#endif
                Contracts(); Worker();
#if !DESKTOP_OBSERVATION_PORTABLE
                Native();
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
#if !DESKTOP_OBSERVATION_PORTABLE
        static int Fixture(string[] args)
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            using (var form = new Form { Text = args[1], StartPosition = FormStartPosition.Manual, Location = new Point(150,150), Size = new Size(420,300), FormBorderStyle = FormBorderStyle.None, BackColor = args.Length > 2 && args[2] == "black" ? Color.Black : Color.CadetBlue })
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
                SetWindowPos(fixture.MainWindowHandle, IntPtr.Zero, -100, 80, 1600, 1000, 0x14); Foreground(fixture);
                using (var frame = capture.CaptureForegroundAsync(new DesktopCaptureOptions(), CancellationToken.None).GetAwaiter().GetResult())
                {
                    Check(frame != null && frame.Target.X == -100 && frame.Target.Width == 1600, "physical bounds support negative desktop coordinates");
                    using(var stream = new MemoryStream(frame.EncodedPng)) using(var image = new Bitmap(stream))
                        Check(image.Width <= 1280 && image.Height <= 1280 && (long)image.Width * image.Height <= 1000000 && frame.EncodedPng.Length <= 2 * 1024 * 1024, "automatic PNG enforces independent size limits");
                    SetWindowPos(fixture.MainWindowHandle, IntPtr.Zero, -100, 80, 700, 500, 0x14);
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
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
#endif
    }
}
