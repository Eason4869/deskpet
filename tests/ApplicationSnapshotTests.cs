using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Dafeiyu.Tests
{
    public static class ApplicationSnapshotTests
    {
        private static int assertions;
        private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--fixture")
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
                Application.EnableVisualStyles();
                var fixture = new Fixture(args.Length > 2 && args[2] == "hang")
                {
                    Text = args[1], StartPosition = FormStartPosition.Manual, Location = new Point(120, 120),
                    Size = new Size(420, 300), FormBorderStyle = FormBorderStyle.None,
                    BackColor = Color.FromArgb(35, 148, 189), ShowInTaskbar = true
                };
                var timeout = new System.Windows.Forms.Timer { Interval = 20000 };
                timeout.Tick += delegate { fixture.Close(); }; timeout.Start();
                Application.Run(fixture); timeout.Dispose(); return 0;
            }
            Process child = null, hung = null, front = null; Form overlay = null;
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
                EncodingLimits();
                string title = "DeskPet snapshot fixture " + Guid.NewGuid().ToString("N");
                child = StartFixture(title, false);
                ApplicationSnapshotTarget target = WaitForTarget(child, title);
                Check(target.ProcessId == child.Id && target.Title == title, "real other-process window listed");
                Check(target.AppName == child.ProcessName, "target includes application name");
                Check(target.IconPng != null && target.IconPng.Length > 8 && target.IconPng[0] == 137, "target includes PNG application icon");
                Check(ApplicationSnapshots.List().All(t => t.ProcessId != Process.GetCurrentProcess().Id), "own windows excluded");
                overlay = new Form
                {
                    Text = "Snapshot exclusion overlay", StartPosition = FormStartPosition.Manual,
                    Location = new Point(100, 100), Size = new Size(500, 400), FormBorderStyle = FormBorderStyle.None,
                    TopMost = true, BackColor = Color.Black, ShowInTaskbar = false
                };
                overlay.Show(); overlay.Update(); Application.DoEvents(); Thread.Sleep(100);
                Check(!ApplicationSnapshots.List().Any(t => t.Handle == overlay.Handle), "visible own process excluded");
                byte[] png = ApplicationSnapshots.CaptureAsync(target, CancellationToken.None).GetAwaiter().GetResult();
                Check(png.Length > 8 && png[0] == 137 && png[1] == 80 && png.Length <= 4 * 1024 * 1024, "PNG within attachment byte cap");
                using (var stream = new MemoryStream(png)) using (var image = new Bitmap(stream))
                {
                    Check(image.Width == 420 && image.Height == 300, "physical window dimensions captured");
                    Color center = image.GetPixel(image.Width / 2, image.Height / 2);
                    Check(Math.Abs(center.R - 35) < 5 && Math.Abs(center.G - 148) < 5 && Math.Abs(center.B - 189) < 5, "captures selected window pixels despite black overlay");
                }
                var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool didCancel = false;
                try { ApplicationSnapshots.CaptureAsync(target, cancelled.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { didCancel = true; }
                Check(didCancel, "pre-cancellation honored");
                var forged = new ApplicationSnapshotTarget { Title = target.Title, Handle = target.Handle, ProcessId = target.ProcessId };
                bool rejected = false;
                try { ApplicationSnapshots.CaptureAsync(forged, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "unstamped or reused identity refused");
                string frontTitle = "Frontmost snapshot fixture " + new string('x', 120);
                front = StartFixture(frontTitle, false);
                var foremost = WaitForTarget(front, frontTitle);
                SetWindowPos(target.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                SetWindowPos(foremost.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                Check(foremost.Title.Length == 100, "long window titles limited to 100 characters");
                Check(ApplicationSnapshots.Topmost().Handle == foremost.Handle, "frontmost helper chooses highest eligible external window");
                var ordered = ApplicationSnapshots.List().ToList();
                Check(ordered.FindIndex(t => t.Handle == foremost.Handle) < ordered.FindIndex(t => t.Handle == target.Handle), "List preserves native Z-order");
                front.Kill(); front.WaitForExit(3000);
                child.Kill(); child.WaitForExit(3000);
                rejected = false;
                try { ApplicationSnapshots.CaptureAsync(target, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "closed window fails clearly");
                string hungTitle = "DeskPet slow snapshot fixture " + Guid.NewGuid().ToString("N");
                hung = StartFixture(hungTitle, true);
                var slow = WaitForTarget(hung, hungTitle);
                bool unsupported = false;
                try { ApplicationSnapshots.CaptureAsync(slow, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (InvalidOperationException ex) { unsupported = ex.Message.Contains("不支持快照"); }
                catch (TimeoutException) { unsupported = true; }
                Check(unsupported, "unrenderable window clearly fails without screen fallback");
                hung.Kill(); hung.WaitForExit(3000);
                // DWM may satisfy PrintWindow from cached content without messaging an application.
                // Exercise the same watchdog using an intentionally pending worker, independently
                // of which rendering path the local Windows compositor selects.
                var pending = new TaskCompletionSource<byte[]>();
                var clock = Stopwatch.StartNew(); bool timedOut = false;
                try { ApplicationSnapshots.WaitForCaptureAsync(pending.Task, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (TimeoutException) { timedOut = true; }
                Check(timedOut && clock.ElapsedMilliseconds >= 1800 && clock.ElapsedMilliseconds < 3500, "pending capture watchdog completes around two seconds");
                var during = new CancellationTokenSource();
                during.CancelAfter(80); clock.Restart(); didCancel = false;
                try { ApplicationSnapshots.WaitForCaptureAsync(pending.Task, during.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { didCancel = true; }
                Check(didCancel && clock.ElapsedMilliseconds < 1000, "in-flight cancellation returns promptly");
                pending.SetResult(new byte[0]); during.Dispose();
                Console.WriteLine("ApplicationSnapshotTests: " + assertions + " assertions passed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                if (overlay != null) overlay.Dispose();
                foreach (Process process in new[] { child, hung, front })
                    if (process != null) { try { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } finally { process.Dispose(); } }
            }
        }
        private static void EncodingLimits()
        {
            using (var wide = new Bitmap(4000, 2000))
            {
                using(var graphics = Graphics.FromImage(wide)) graphics.Clear(Color.CadetBlue);
                byte[] manual = ApplicationSnapshots.Encode(wide, CancellationToken.None, false);
                using(var stream = new MemoryStream(manual)) using(var image = new Bitmap(stream))
                    Check(image.Width == 3072 && image.Height == 1536 && manual.Length <= 4 * 1024 * 1024, "manual image keeps 3072 long edge and 4 MiB allowance");
            }
            using(var noisy = new Bitmap(1250, 800, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                var data = noisy.LockBits(new Rectangle(0,0,1250,800), System.Drawing.Imaging.ImageLockMode.WriteOnly, noisy.PixelFormat);
                try { var pixels = new byte[data.Stride * 800]; new Random(7348).NextBytes(pixels); Marshal.Copy(pixels,0,data.Scan0,pixels.Length); }
                finally { noisy.UnlockBits(data); }
                byte[] manual = ApplicationSnapshots.Encode(noisy, CancellationToken.None, false);
                Check(manual.Length > 2 * 1024 * 1024 && manual.Length <= 4 * 1024 * 1024, "manual high entropy PNG still allowed beyond automatic byte cap");
                byte[] automatic = ApplicationSnapshots.Encode(noisy, CancellationToken.None, true);
                using(var stream = new MemoryStream(automatic)) using(var image = new Bitmap(stream))
                    Check(automatic.Length <= 2 * 1024 * 1024 && image.Width < 1250 && (long)image.Width * image.Height <= 1000000, "automatic high entropy PNG shrinks to byte cap");
            }
        }
        private static Process StartFixture(string title, bool hang)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = typeof(ApplicationSnapshotTests).Assembly.Location,
                Arguments = "--fixture \"" + title + "\"" + (hang ? " hang" : ""), UseShellExecute = false
            });
        }
        private static ApplicationSnapshotTarget WaitForTarget(Process process, string title)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 5000)
            {
                string expected = title.Length > 100 ? title.Substring(0, 99) + "…" : title;
                var target = ApplicationSnapshots.List().FirstOrDefault(t => t.ProcessId == process.Id && t.Title == expected);
                if (target != null) return target;
                Thread.Sleep(30);
            }
            throw new Exception("Fixture window was not available on the interactive desktop.");
        }
        private sealed class Fixture : Form
        {
            private readonly bool hang;
            public Fixture(bool hang) { this.hang = hang; }
            protected override CreateParams CreateParams
            {
                get { var value = base.CreateParams; if (hang) value.ExStyle |= 0x00200000; return value; }
            }
            protected override void WndProc(ref Message message)
            {
                if (hang && (message.Msg == 0x0317 || message.Msg == 0x0318)) Thread.Sleep(6000);
                base.WndProc(ref message);
            }
        }
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
