using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public sealed class ApplicationSnapshotTarget
    {
        public string Title { get; set; }
        public string AppName { get; set; }
        public byte[] IconPng { get; set; }
        public IntPtr Handle { get; set; }
        public int ProcessId { get; set; }
        internal long ProcessStartTicks;
        internal uint WindowThread;
        internal string OriginalTitle;
        internal string WindowClass;
    }

#if PORTABLE
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
#endif
    public static class ApplicationSnapshots
    {
        private const int MaximumBytes = 4 * 1024 * 1024;
        private static int busy;
        private static int iconBusy;
        private static readonly object iconSync = new object();
        private static readonly Dictionary<string, byte[]> executableIcons = new Dictionary<string, byte[]>();

        public static IList<ApplicationSnapshotTarget> List()
        {
            return ListWindows(false);
        }
        public static ApplicationSnapshotTarget Topmost()
        {
            IList<ApplicationSnapshotTarget> targets = ListWindows(true);
            return targets.Count == 0 ? null : targets[0];
        }
        private static IList<ApplicationSnapshotTarget> ListWindows(bool firstOnly)
        {
            EnsureWindows();
            var targets = new List<ApplicationSnapshotTarget>();
            int own = Process.GetCurrentProcess().Id;
            var seen = new HashSet<IntPtr>();
            // Walk the desktop Z-order explicitly; never sort by title or process enumeration.
            for (IntPtr handle = GetTopWindow(IntPtr.Zero); handle != IntPtr.Zero && seen.Add(handle) && seen.Count <= 2000; handle = GetWindow(handle, 2))
            {
                try
                {
                    uint pid; uint thread = GetWindowThreadProcessId(handle, out pid);
                    if (pid == 0 || pid == own || !Eligible(handle)) continue;
                    string title = Title(handle);
                    if (String.IsNullOrWhiteSpace(title)) continue;
                    long start; string name;
                    using (Process process = Process.GetProcessById((int)pid)) { start = process.StartTime.ToUniversalTime().Ticks; name = process.ProcessName; }
                    targets.Add(new ApplicationSnapshotTarget
                    {
                        Title = title.Length > 100 ? title.Substring(0, 99) + "…" : title,
                        AppName = name, IconPng = WindowIcon(handle, (int)pid, start),
                        Handle = handle, ProcessId = (int)pid, ProcessStartTicks = start,
                        WindowThread = thread, OriginalTitle = title, WindowClass = ClassName(handle)
                    });
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                if ((firstOnly && targets.Count > 0) || targets.Count >= 200) break;
            }
            return targets;
        }

        public static async Task<byte[]> CaptureAsync(ApplicationSnapshotTarget target, CancellationToken token)
        {
            EnsureWindows(); token.ThrowIfCancellationRequested();
            if (target == null) throw new ArgumentNullException("target");
            // Snapshot the selected identity; callers may refresh or mutate the menu while awaiting.
            var selected = new ApplicationSnapshotTarget
            {
                Title = target.Title, AppName = target.AppName, Handle = target.Handle, ProcessId = target.ProcessId,
                ProcessStartTicks = target.ProcessStartTicks, WindowThread = target.WindowThread,
                OriginalTitle = target.OriginalTitle, WindowClass = target.WindowClass
            };
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                throw new InvalidOperationException(TextResources.Get("applicationSnapshots.captureAsync.text"));
            Task<byte[]> capture;
            try
            {
                // PrintWindow can block inside another application's window procedure. A dedicated
                // background worker owns all GDI objects, even after the caller times out or cancels.
                capture = Task.Factory.StartNew(delegate
                {
                    try { return Capture(selected, token); }
                    finally { Interlocked.Exchange(ref busy, 0); }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch { Interlocked.Exchange(ref busy, 0); throw; }
            return await WaitForCaptureAsync(capture, token).ConfigureAwait(false);
        }

        internal static async Task<byte[]> WaitForCaptureAsync(Task<byte[]> capture, CancellationToken token)
        {
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task timeout = Task.Delay(2000, timer.Token);
                Task finished = await Task.WhenAny(capture, timeout).ConfigureAwait(false);
                if (finished == capture)
                {
                    timer.Cancel();
                    if (token.IsCancellationRequested) { ObserveFailure(capture); token.ThrowIfCancellationRequested(); }
                    return await capture.ConfigureAwait(false);
                }
                // Observe late errors without retaining the captured image or reporting a second result.
                ObserveFailure(capture);
                token.ThrowIfCancellationRequested();
                throw new TimeoutException(TextResources.Get("applicationSnapshots.waitForCaptureAsync.text"));
            }
        }
        private static void ObserveFailure(Task<byte[]> capture)
        {
            capture.ContinueWith(t => { var ignored = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static byte[] Capture(ApplicationSnapshotTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ValidateIdentity(target);
            IntPtr previousDpi = IntPtr.Zero;
            try
            {
                try { previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { }
                Rect bounds;
                if (!GetWindowRect(target.Handle, out bounds)) throw Unsupported();
                long width = (long)bounds.Right - bounds.Left, height = (long)bounds.Bottom - bounds.Top;
                if (width < 100 || height < 60 || width > 8192 || height > 8192 || width * height > 16000000)
                    throw new InvalidOperationException(TextResources.Get("applicationSnapshots.capture.text"));
                using (var bitmap = new Bitmap((int)width, (int)height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Magenta);
                        IntPtr dc = graphics.GetHdc();
                        try
                        {
                            token.ThrowIfCancellationRequested(); ValidateIdentity(target);
                            if (!PrintWindow(target.Handle, dc, 2)) throw Unsupported();
                        }
                        finally { graphics.ReleaseHdc(dc); }
                    }
                    token.ThrowIfCancellationRequested(); ValidateIdentity(target);
                    Rect after;
                    if (!GetWindowRect(target.Handle, out after) || after.Right - after.Left != width || after.Bottom - after.Top != height)
                        throw new InvalidOperationException(TextResources.Get("applicationSnapshots.capture.text.alternativeA"));
                    RejectEmpty(bitmap);
                    return Encode(bitmap, token);
                }
            }
            catch (ExternalException) { throw Unsupported(); }
            finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
        }

        private static byte[] Encode(Bitmap original, CancellationToken token)
        {
            int width = original.Width, height = original.Height;
            double scale = Math.Min(1.0, Math.Min(3072.0 / Math.Max(width, height), Math.Sqrt(6000000.0 / ((long)width * height))));
            width = Math.Max(1, (int)(width * scale)); height = Math.Max(1, (int)(height * scale));
            // PNG remains lossless at each selected resolution; no screenshot touches disk.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using (var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics graphics = Graphics.FromImage(resized))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(original, new Rectangle(0, 0, width, height));
                    }
                    using (var stream = new MemoryStream())
                    {
                        resized.Save(stream, ImageFormat.Png);
                        token.ThrowIfCancellationRequested();
                        if (stream.Length <= MaximumBytes) return stream.ToArray();
                    }
                }
                width = Math.Max(1, width * 3 / 4); height = Math.Max(1, height * 3 / 4);
            }
            throw new InvalidOperationException(TextResources.Get("applicationSnapshots.encode.text"));
        }

        private static void RejectEmpty(Bitmap bitmap)
        {
            int black = 0, sentinel = 0, total = 0;
            for (int y = 0; y < 24; y++) for (int x = 0; x < 32; x++)
            {
                Color pixel = bitmap.GetPixel((x * 2 + 1) * bitmap.Width / 64, (y * 2 + 1) * bitmap.Height / 48);
                total++;
                if (pixel.R < 8 && pixel.G < 8 && pixel.B < 8) black++;
                if (pixel.R > 245 && pixel.G < 10 && pixel.B > 245) sentinel++;
            }
            if (black > total * .98 || sentinel > total * .9) throw Unsupported();
        }

        private static void ValidateIdentity(ApplicationSnapshotTarget target)
        {
            if (target.Handle == IntPtr.Zero || target.ProcessId == Process.GetCurrentProcess().Id || target.ProcessStartTicks == 0 || !IsWindow(target.Handle))
                throw new InvalidOperationException(TextResources.Get("applicationSnapshots.validateIdentity.text"));
            uint pid; uint thread = GetWindowThreadProcessId(target.Handle, out pid);
            if (pid != target.ProcessId || thread != target.WindowThread || !Eligible(target.Handle) ||
                !String.Equals(Title(target.Handle), target.OriginalTitle, StringComparison.Ordinal) || !String.Equals(ClassName(target.Handle), target.WindowClass, StringComparison.Ordinal))
                throw new InvalidOperationException(TextResources.Get("applicationSnapshots.validateIdentity.text"));
            try
            {
                using (Process process = Process.GetProcessById(target.ProcessId))
                    if (process.StartTime.ToUniversalTime().Ticks != target.ProcessStartTicks) throw new InvalidOperationException(TextResources.Get("applicationSnapshots.validateIdentity.text.alternativeA"));
            }
            catch (ArgumentException) { throw new InvalidOperationException(TextResources.Get("applicationSnapshots.validateIdentity.text.alternativeB")); }
            catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException(TextResources.Get("applicationSnapshots.validateIdentity.text.alternativeC")); }
        }

        private static bool Eligible(IntPtr handle)
        {
            if (handle == GetShellWindow() || handle == GetDesktopWindow() || !IsWindowVisible(handle) || IsIconic(handle) || GetWindow(handle, 4) != IntPtr.Zero || (GetWindowLong(handle, -20) & 0x80) != 0) return false;
            int cloaked;
            if (DwmGetWindowAttribute(handle, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            Rect bounds;
            return GetWindowRect(handle, out bounds) && (long)bounds.Right - bounds.Left >= 100 && (long)bounds.Bottom - bounds.Top >= 60;
        }
        private static string Title(IntPtr handle) { var text = new StringBuilder(1024); GetWindowText(handle, text, text.Capacity); return text.ToString(); }
        private static string ClassName(IntPtr handle) { var text = new StringBuilder(256); GetClassName(handle, text, text.Capacity); return text.ToString(); }
        private static byte[] WindowIcon(IntPtr window, int pid, long start)
        {
            string key = pid + ":" + start;
            try
            {
                IntPtr icon;
                SendMessageTimeout(window, 0x007F, new IntPtr(2), IntPtr.Zero, 0x0002 | 0x0020, 20, out icon);
                if (icon == IntPtr.Zero) icon = ClassIcon(window, -34);
                if (icon == IntPtr.Zero) icon = ClassIcon(window, -14);
                if (icon != IntPtr.Zero)
                {
                    IntPtr copy = CopyIcon(icon);
                    if (copy != IntPtr.Zero)
                    {
                        try { using (var value = Icon.FromHandle(copy)) return EncodeIcon(value); }
                        finally { DestroyIcon(copy); }
                    }
                }
                lock (iconSync) { byte[] cached; if (executableIcons.TryGetValue(key, out cached)) return (byte[])cached.Clone(); }
                // Executable icon extraction can involve shell extensions or slow storage. Keep it
                // off the caller's UI thread and cap it at one worker, returning a generic app icon
                // immediately until an executable-specific result is cached for a later refresh.
                if (Interlocked.CompareExchange(ref iconBusy, 1, 0) == 0)
                    Task.Factory.StartNew(delegate
                    {
                        try
                        {
                            using (Process process = Process.GetProcessById(pid))
                            {
                                if (process.StartTime.ToUniversalTime().Ticks != start) return;
                                string path = process.MainModule.FileName;
                                if (path.StartsWith("\\\\", StringComparison.Ordinal)) return;
                                using (Icon extracted = Icon.ExtractAssociatedIcon(path))
                                {
                                    if (extracted == null) return;
                                    byte[] bytes = EncodeIcon(extracted);
                                    lock (iconSync) { if (executableIcons.Count >= 128) executableIcons.Clear(); executableIcons[key] = bytes; }
                                }
                            }
                        }
                        catch (Exception) { }
                        finally { Interlocked.Exchange(ref iconBusy, 0); }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (ArgumentException) { }
            catch (ExternalException) { }
            try { return EncodeIcon(SystemIcons.Application); }
            catch (ExternalException) { return null; }
        }
        private static byte[] EncodeIcon(Icon icon)
        {
            if (icon.Width < 1 || icon.Height < 1 || icon.Width > 256 || icon.Height > 256) throw new ArgumentException(TextResources.Get("applicationSnapshots.encodeIcon.text"));
            using (Bitmap bitmap = icon.ToBitmap()) using (var stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
        }
        private static IntPtr ClassIcon(IntPtr window, int index) { return IntPtr.Size == 8 ? GetClassLongPtr(window, index) : new IntPtr(unchecked((int)GetClassLong(window, index))); }
        private static InvalidOperationException Unsupported() { return new InvalidOperationException(TextResources.Get("applicationSnapshots.unsupported.text")); }
        private static void EnsureWindows() { if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException(TextResources.Get("applicationSnapshots.ensureWindows.text")); }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong(IntPtr window, int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int length);
    }
}
