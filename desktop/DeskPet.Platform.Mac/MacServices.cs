using System.Runtime.InteropServices;
using Dafeiyu;

namespace DeskPet.Platform.Mac;

[Flags] public enum MacHotKeyModifiers : uint { Command = 1 << 8, Shift = 1 << 9, Option = 1 << 11, Control = 1 << 12 }
public enum MicrophonePermission { NotDetermined, Restricted, Denied, Authorized }
[StructLayout(LayoutKind.Sequential)]
public struct MacDisplay { public double X, Y, Width, Height, WorkX, WorkY, WorkWidth, WorkHeight, Scale; }

public static class MacServices
{
    public static bool IsSupported => OperatingSystem.IsMacOSVersionAtLeast(14);
    internal static void EnsureMac() { if (!IsSupported) throw new PlatformNotSupportedException("此服务需要 macOS 14 或更新版本。"); }
    private static string DirectoryPath(string variable, params string[] parts)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(parts) : configured));
    }
    public static string DataDirectory => DirectoryPath("DESKPET_DATA_DIR", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "DeskPet");
    public static string LogDirectory => DirectoryPath("DESKPET_LOG_DIR", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "DeskPet");
    public static void InstallSecretProtection() { EnsureMac(); SecretProtection.Configure(new MacSecretProtector()); }
    public static IAudioRecorder CreateRecorder() { EnsureMac(); return new MacAudioRecorder(false); }
    public static IPcmMicrophone CreateMicrophone() { EnsureMac(); return new MacAudioRecorder(true); }
    public static ILiveAudioOutput CreateLiveOutput() { EnsureMac(); return new MacPcmOutput(); }
    public static IReplySpeech CreateSpeech(ApiClient api) { EnsureMac(); return new MacSpeechOutput(api); }
    public static MicrophonePermission MicrophoneAccess { get { EnsureMac(); return (MicrophonePermission)Native.MicrophoneStatus(); } }

    public static Task<bool> RequestMicrophoneAccessAsync()
    {
        EnsureMac();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Native.PermissionCallback? callback = null;
        GCHandle pin = default;
        callback = granted => { completion.TrySetResult(granted != 0); if (pin.IsAllocated) pin.Free(); };
        pin = GCHandle.Alloc(callback);
        try { Native.MicrophoneRequest(callback); } catch { if (pin.IsAllocated) pin.Free(); throw; }
        return completion.Task;
    }
    public static TimeSpan GetIdleTime() { EnsureMac(); return TimeSpan.FromSeconds(Math.Clamp(Native.IdleSeconds(), 0, TimeSpan.MaxValue.TotalSeconds - 1)); }
    // Public workspace session, display-sleep and system-sleep signals. This is
    // deliberately not a claim that macOS exposes a public screen-lock query.
    public static bool IsSessionInteractive { get { EnsureMac(); return Native.SessionInteractive() != 0; } }
    public static ulong SessionLifecycleRevision { get { EnsureMac(); return Native.SessionLifecycleRevision(); } }
    // Quartz global coordinates: origin at main display's upper left, consistent across displays.
    public static (double X, double Y) GetPointerPosition() { EnsureMac(); Native.Pointer(out var x, out var y); return (x, y); }
    /// <summary>Pointer in logical points relative to the NSView/NSWindow content's upper-left, including outside points.</summary>
    public static (double X, double Y) GetPointerRelativeToWindow(IntPtr handle)
    {
        EnsureMac();
        if (handle == IntPtr.Zero || Native.PointerRelative(handle, out var x, out var y) == 0) throw new ArgumentException("需要已连接到窗口的 NSView 或 NSWindow 句柄。", nameof(handle));
        return (x, y);
    }
    public static MacDisplay[] GetDisplays()
    {
        EnsureMac(); var list = new List<MacDisplay>();
        for (var i = 0; i < Native.DisplayCount(); i++) if (Native.Display(i, out var display) != 0) list.Add(display);
        return list.ToArray();
    }
    public static void ConfigureWindow(IntPtr nsWindow, bool topmost, bool ignoreMouse, bool joinAllSpaces = true)
    {
        EnsureMac(); if (nsWindow == IntPtr.Zero) throw new ArgumentException("需要 NSWindow 句柄。", nameof(nsWindow));
        if (Native.Window(nsWindow, topmost ? 1 : 0, ignoreMouse ? 1 : 0, joinAllSpaces ? 1 : 0) == 0) throw new InvalidOperationException("窗口策略设置失败。");
    }
    // Best-effort public CGWindow query; macOS may redact cross-app window data. No screen recording permission is requested.
    public static bool IsFrontApplicationFullscreen() { EnsureMac(); return Native.FullscreenFront() != 0; }
    public static IDisposable RegisterHotKey(uint virtualKey, MacHotKeyModifiers modifiers, Action callback)
    {
        EnsureMac(); ArgumentNullException.ThrowIfNull(callback);
        return new HotKeyRegistration(virtualKey, modifiers, callback);
    }
    public static int LoginAtStartupStatus { get { EnsureMac(); return Native.LoginStatus(); } }
    public static void SetLoginAtStartup(bool enabled)
    {
        EnsureMac(); var status = Native.LoginSet(enabled ? 1 : 0);
        if (status == -10002) throw new InvalidOperationException("登录项需要批准，请在系统设置 > 通用 > 登录项中允许 DeskPet。");
        if (status != 0) throw new InvalidOperationException($"无法修改登录项（状态 {status}），请在系统设置 > 通用 > 登录项中检查；应用必须从 .app 包运行。");
    }
    private sealed class HotKeyRegistration : IDisposable
    {
        private readonly Native.HotKeyCallback callback;
        private IntPtr handle;
        internal HotKeyRegistration(uint key, MacHotKeyModifiers mods, Action action)
        {
            callback = () => { try { action(); } catch { /* Never unwind a managed exception through Carbon. */ } };
            handle = Native.HotKeyRegister(key, (uint)mods, callback, out var error);
            if (handle == IntPtr.Zero) throw new InvalidOperationException($"全局快捷键注册失败或与其他应用冲突（状态 {error}）。");
        }
        public void Dispose() { var previous = Interlocked.Exchange(ref handle, IntPtr.Zero); if (previous == IntPtr.Zero) return; Native.HotKeyUnregister(previous); GC.KeepAlive(callback); }
    }
}
