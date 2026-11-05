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
        internal DesktopObservationIdentity ObservationIdentity;
    }

#if PORTABLE
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
#endif
    public static class ApplicationSnapshots
    {
        private const int MaximumBytes = 4 * 1024 * 1024;
        private static readonly DesktopCaptureWorker captureWorker = new DesktopCaptureWorker();
        internal static DesktopCaptureWorker CaptureWorker { get { return captureWorker; } }
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
            return await captureWorker.RunAsync(delegate(CancellationToken lifetime) { return Capture(selected, lifetime, false); }, token).ConfigureAwait(false);
        }

        internal static async Task<byte[]> WaitForCaptureAsync(Task<byte[]> capture, CancellationToken token)
        {
            try { return await DesktopCaptureWorker.WaitAsync(capture, token).ConfigureAwait(false); }
            catch
            {
                var captureCleanup=capture.ContinueWith(delegate(Task<byte[]> finished)
                {
                    if (finished.Status == TaskStatus.RanToCompletion && finished.Result != null) Array.Clear(finished.Result, 0, finished.Result.Length);
                    else if (finished.IsFaulted) { var ignored = finished.Exception; }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }
        }

        internal static byte[] CaptureObservation(ApplicationSnapshotTarget target, CancellationToken cancel)
        {
            if(target.ObservationIdentity.Scope==DesktopObservationCaptureScope.FullScreen)return CaptureDesktop(target.ObservationIdentity,cancel);
            try { return Capture(target, cancel, true); }
            catch (InvalidOperationException) { if (!MatchesObservation(target.ObservationIdentity)) return null; throw; }
        }

        // Exact foreground only, with no Z-order walk and no icon retrieval.
        internal static ApplicationSnapshotTarget Foreground(DesktopCaptureOptions options)
        {
            EnsureWindows();
            try
            {
                IntPtr previousDpi = IntPtr.Zero;
                try
                {
                    try { previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                    IntPtr handle = GetForegroundWindow(); uint pid;
                    uint thread = GetWindowThreadProcessId(handle, out pid);
                    if (handle == IntPtr.Zero || pid == 0 || pid == Process.GetCurrentProcess().Id || !Eligible(handle)) return null;
                    uint affinity;
                    // GetWindowDisplayAffinity is documented to fail on non-layered windows.
                    // Honor explicit protection; ordinary non-layered windows still use PrintWindow
                    // with black/sentinel rejection and never a screen-copy fallback.
                    if (GetWindowDisplayAffinity(handle, out affinity) && affinity != 0) return null;
                    string title = Title(handle), windowClass = ClassName(handle);
                    if (String.IsNullOrWhiteSpace(title)) return null;
                    long start; string appId, name;
                    using (Process process = Process.GetProcessById((int)pid))
                    {
                        start = process.StartTime.ToUniversalTime().Ticks; name = process.ProcessName;
                        appId = process.MainModule.FileName;
                    }
                    if (String.IsNullOrWhiteSpace(appId) || !Path.IsPathRooted(appId) || options.IsExcluded(appId)) return null;
                    Rect bounds;
                    if (!GetWindowRect(handle, out bounds)) return null;
                    var identity = new DesktopObservationIdentity(handle.ToInt64(), (int)pid, start, thread, appId, title, windowClass,
                        bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                    if (GetForegroundWindow() != handle) return null;
                    return new ApplicationSnapshotTarget { Handle = handle, ProcessId = (int)pid, ProcessStartTicks = start,
                        WindowThread = thread, Title = title, OriginalTitle = title, AppName = name, WindowClass = windowClass, ObservationIdentity = identity };
                }
                finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }

        internal static bool InteractiveDesktop()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Environment.UserInteractive) return false;
            IntPtr desktop = OpenInputDesktop(0, false, 1);
            if (desktop == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(256); int needed;
                return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out needed) &&
                    String.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseDesktop(desktop); }
        }

        internal static bool HasEligibleForeground()
        {
            IntPtr handle = GetForegroundWindow(); uint pid;
            GetWindowThreadProcessId(handle, out pid);
            if (handle == IntPtr.Zero || pid == 0 || pid == Process.GetCurrentProcess().Id || !Eligible(handle)) return false;
            uint affinity;
            return !(GetWindowDisplayAffinity(handle, out affinity) && affinity != 0) && GetForegroundWindow() == handle;
        }

        // A live Windows process cannot replace its executable image while retaining its PID and
        // start time. Revalidate that lifetime and cheap window state, without module enumeration.
        internal static bool MatchesObservation(DesktopObservationIdentity expected)
        {
            if (expected == null || !InteractiveDesktop()) return false;
            if(expected.Scope==DesktopObservationCaptureScope.FullScreen){var bounds=DesktopBounds();return expected.X==bounds.X && expected.Y==bounds.Y && expected.Width==bounds.Width && expected.Height==bounds.Height;}
            if(String.IsNullOrWhiteSpace(expected.ApplicationId) || !Path.IsPathRooted(expected.ApplicationId))return false;
            IntPtr previousDpi = IntPtr.Zero;
            try
            {
                try { previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                IntPtr handle = new IntPtr(expected.WindowId);
                if (GetForegroundWindow() != handle) return false;
                ValidateIdentity(new ApplicationSnapshotTarget { Handle = handle, ProcessId = expected.ProcessId,
                    ProcessStartTicks = expected.ProcessStartTicks, WindowThread = expected.WindowThreadId,
                    OriginalTitle = expected.WindowTitle, WindowClass = expected.WindowClass });
                Rect bounds; uint affinity;
                return GetWindowRect(handle, out bounds) && bounds.Left == expected.X && bounds.Top == expected.Y &&
                    bounds.Right - bounds.Left == expected.Width && bounds.Bottom - bounds.Top == expected.Height &&
                    !(GetWindowDisplayAffinity(handle, out affinity) && affinity != 0) && GetForegroundWindow() == handle;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
            finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
        }

        internal static bool MatchesObservationWithApplicationPath(DesktopObservationIdentity expected)
        {
            if (expected == null || !InteractiveDesktop()) return false;
            if(expected.Scope==DesktopObservationCaptureScope.FullScreen)return MatchesObservation(expected);
            ApplicationSnapshotTarget current = Foreground(new DesktopCaptureOptions());
            if (current == null) return false;
            DesktopObservationIdentity actual = current.ObservationIdentity;
            return actual.WindowId == expected.WindowId && actual.ProcessId == expected.ProcessId &&
                actual.ProcessStartTicks == expected.ProcessStartTicks && actual.WindowThreadId == expected.WindowThreadId &&
                actual.X == expected.X && actual.Y == expected.Y && actual.Width == expected.Width && actual.Height == expected.Height &&
                String.Equals(actual.ApplicationId, expected.ApplicationId, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(actual.WindowClass, expected.WindowClass, StringComparison.Ordinal) &&
                String.Equals(actual.WindowTitle, expected.WindowTitle, StringComparison.Ordinal);
        }

        internal static ApplicationSnapshotTarget ObservationTarget(DesktopCaptureOptions options)
        {
            if(options.Scope!=DesktopObservationCaptureScope.FullScreen)return Foreground(options);
            if(!InteractiveDesktop())return null;
            var bounds=DesktopBounds();
            if(bounds.Width<1 || bounds.Height<1)return null;
            return new ApplicationSnapshotTarget{ObservationIdentity=new DesktopObservationIdentity(0,0,0,0,"","","",bounds.X,bounds.Y,bounds.Width,bounds.Height,DesktopObservationCaptureScope.FullScreen)};
        }
        static Rectangle DesktopBounds()
        {
            IntPtr previous=IntPtr.Zero;
            try{
                try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
                // Query physical virtual-desktop bounds without a Windows Forms dependency in the shared core.
                const int virtualX=76,virtualY=77,virtualWidth=78,virtualHeight=79;
                return new Rectangle(GetSystemMetrics(virtualX),GetSystemMetrics(virtualY),GetSystemMetrics(virtualWidth),GetSystemMetrics(virtualHeight));
            }
            finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
        }
        static byte[] CaptureDesktop(DesktopObservationIdentity target,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if(!MatchesObservation(target))return null;
            if((long)target.Width*target.Height>64000000)throw Unsupported();
            IntPtr previous=IntPtr.Zero;
            try{
                try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
                using(var bitmap=new Bitmap(target.Width,target.Height,PixelFormat.Format24bppRgb)){
                    using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(target.X,target.Y,0,0,bitmap.Size,CopyPixelOperation.SourceCopy);
                    cancel.ThrowIfCancellationRequested();if(!MatchesObservation(target))return null;
                    RejectEmpty(bitmap);var encoded=Encode(bitmap,cancel,true);
                    if(!MatchesObservation(target)){Array.Clear(encoded,0,encoded.Length);return null;}return encoded;
                }
            }catch(ExternalException){throw Unsupported();}
            finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
        }

        private static byte[] Capture(ApplicationSnapshotTarget target, CancellationToken token, bool observation)
        {
            token.ThrowIfCancellationRequested();
            ValidateIdentity(target);
            if (observation && !MatchesObservation(target.ObservationIdentity)) throw Unsupported();
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
                            if (observation && !MatchesObservation(target.ObservationIdentity)) throw Unsupported();
                            if (!PrintWindow(target.Handle, dc, 2)) throw Unsupported();
                        }
                        finally { graphics.ReleaseHdc(dc); }
                    }
                    token.ThrowIfCancellationRequested(); ValidateIdentity(target);
                    Rect after;
                    if (!GetWindowRect(target.Handle, out after) || after.Right - after.Left != width || after.Bottom - after.Top != height)
                        throw new InvalidOperationException(TextResources.Get("applicationSnapshots.capture.text.alternativeA"));
                    if (observation && !MatchesObservation(target.ObservationIdentity)) throw Unsupported();
                    RejectEmpty(bitmap);
                    byte[] encoded = Encode(bitmap, token, observation);
                    if (observation && !MatchesObservation(target.ObservationIdentity)) { Array.Clear(encoded, 0, encoded.Length); throw Unsupported(); }
                    return encoded;
                }
            }
            catch (ExternalException) { throw Unsupported(); }
            finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
        }

        internal static byte[] Encode(Bitmap original, CancellationToken token, bool observation)
        {
            int width = original.Width, height = original.Height;
            double scale = Math.Min(1.0, Math.Min((observation ? DesktopCaptureOptions.MaximumDimension : 3072.0) / Math.Max(width, height), Math.Sqrt((observation ? DesktopCaptureOptions.MaximumPixels : 6000000.0) / ((long)width * height))));
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
                        if (stream.Length <= (observation ? DesktopCaptureOptions.MaximumPngBytes : MaximumBytes)) return stream.ToArray();
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
        private static string Title(IntPtr handle)
        {
            int length = GetWindowTextLength(handle);
            if (length <= 0 || length > 32768) return String.Empty;
            var text = new StringBuilder(length + 1);
            int copied = GetWindowText(handle, text, text.Capacity);
            // A growing title must never become an apparently stable truncated identity.
            return copied == length && GetWindowTextLength(handle) == length ? text.ToString() : String.Empty;
        }
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
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr value, int index, StringBuilder text, int length, out int needed);
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int length);
    }
#if PORTABLE
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
#endif
    public sealed class WindowsDesktopObservationCapture : IDesktopObservationCapture
    {
        private readonly Func<bool> interactive;
        private readonly DesktopCaptureWorker worker;
        private readonly Func<DesktopCaptureOptions, ApplicationSnapshotTarget> foreground;
        private readonly Func<ApplicationSnapshotTarget, CancellationToken, byte[]> encode;
        private readonly Func<DesktopObservationIdentity, bool> finalValidation;
        public WindowsDesktopObservationCapture() : this(ApplicationSnapshots.InteractiveDesktop) { }
        internal WindowsDesktopObservationCapture(Func<bool> interactive) : this(interactive, ApplicationSnapshots.CaptureWorker,
            ApplicationSnapshots.ObservationTarget, ApplicationSnapshots.CaptureObservation, ApplicationSnapshots.MatchesObservationWithApplicationPath) { }
        // Native boundaries are injectable, but scheduling, deadline and ownership stay in this adapter.
        internal WindowsDesktopObservationCapture(Func<bool> interactive, DesktopCaptureWorker worker,
            Func<DesktopCaptureOptions, ApplicationSnapshotTarget> foreground,
            Func<ApplicationSnapshotTarget, CancellationToken, byte[]> encode, Func<DesktopObservationIdentity, bool> finalValidation)
        {
            this.interactive = interactive; this.worker = worker; this.foreground = foreground;
            this.encode = encode; this.finalValidation = finalValidation;
        }
        public DesktopCaptureStatus GetStatus(DesktopCaptureOptions options=null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return DesktopCaptureStatus.UnsupportedPlatform;
            if (!interactive()) return DesktopCaptureStatus.NonInteractiveDesktop;
            if (worker.IsBusy) return DesktopCaptureStatus.Busy;
            if(options!=null && options.Scope==DesktopObservationCaptureScope.FullScreen)return DesktopCaptureStatus.Ready;
            // Status is only a cheap preflight. Executable lookup and exclusion admission stay
            // inside the bounded capture worker, never on a synchronous UI status call.
            return ApplicationSnapshots.HasEligibleForeground() ? DesktopCaptureStatus.Ready : DesktopCaptureStatus.NoEligibleForeground;
        }
        public async Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (options == null) throw new ArgumentNullException("options");
            if (!interactive()) return null;
            ApplicationSnapshotTarget target = null;
            DateTimeOffset capturedAt = default(DateTimeOffset);
            byte[] png = await worker.RunAsync(delegate(CancellationToken lifetime)
            {
                if (!interactive()) return null;
                target = foreground(options);
                if (target == null) return null;
                capturedAt = DateTimeOffset.UtcNow;
                byte[] encoded = null;
                try
                {
                    encoded = encode(target, lifetime);
                    lifetime.ThrowIfCancellationRequested();
                    if (encoded == null || !finalValidation(target.ObservationIdentity)) return null;
                    lifetime.ThrowIfCancellationRequested();
                    byte[] delivered = encoded; encoded = null; return delivered;
                }
                finally { if (encoded != null) Array.Clear(encoded, 0, encoded.Length); }
            }, cancel).ConfigureAwait(false);
            if (png == null) return null;
            DesktopObservationFrame frame = null;
            try
            {
                cancel.ThrowIfCancellationRequested();
                frame = new DesktopObservationFrame(png, capturedAt, target.ObservationIdentity);
                cancel.ThrowIfCancellationRequested();
                return frame;
            }
            catch
            {
                if (frame != null) frame.Dispose(); else Array.Clear(png, 0, png.Length);
                throw;
            }
        }
        public bool IsCurrent(DesktopObservationFrame frame)
        {
            return frame != null && !frame.IsDisposed && interactive() && ApplicationSnapshots.MatchesObservation(frame.Target);
        }
    }

}
