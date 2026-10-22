using System.Runtime.InteropServices;

namespace DeskPet.Platform.Mac;

internal static class Native
{
    private const string Library = "DeskPetMac";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void PermissionCallback(int granted);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void HotKeyCallback();
    [DllImport(Library, EntryPoint="dp_keychain_key")] internal static extern int KeychainKey([Out] byte[] key, int length);
    [DllImport(Library, EntryPoint="dp_microphone_status")] internal static extern int MicrophoneStatus();
    [DllImport(Library, EntryPoint="dp_microphone_request")] internal static extern void MicrophoneRequest(PermissionCallback callback);
    [DllImport(Library, EntryPoint="dp_microphone_start")] internal static extern IntPtr MicrophoneStart(out int error);
    [DllImport(Library, EntryPoint="dp_microphone_read")] internal static extern int MicrophoneRead(IntPtr handle, [Out] byte[] data, int length);
    [DllImport(Library, EntryPoint="dp_microphone_stop")] internal static extern void MicrophoneStop(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_output_start")] internal static extern IntPtr OutputStart(int rate, out int error);
    [DllImport(Library, EntryPoint="dp_output_enqueue")] internal static extern int OutputEnqueue(IntPtr handle, byte[] bytes, int length);
    [DllImport(Library, EntryPoint="dp_output_pending")] internal static extern long OutputPending(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_output_completed")] internal static extern long OutputCompleted(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_output_volume")] internal static extern void OutputVolume(IntPtr handle, float volume);
    [DllImport(Library, EntryPoint="dp_output_interrupt")] internal static extern int OutputInterrupt(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_output_stop")] internal static extern void OutputStop(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_wave_start")] internal static extern IntPtr WaveStart(byte[] bytes, int length, float volume);
    [DllImport(Library, EntryPoint="dp_wave_status")] internal static extern int WaveStatus(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_wave_volume")] internal static extern void WaveVolume(IntPtr handle, float volume);
    [DllImport(Library, EntryPoint="dp_wave_stop")] internal static extern void WaveStop(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_idle_seconds")] internal static extern double IdleSeconds();
    [DllImport(Library, EntryPoint="dp_session_interactive")] internal static extern int SessionInteractive();
    [DllImport(Library, EntryPoint="dp_session_lifecycle_revision")] internal static extern ulong SessionLifecycleRevision();
    [DllImport(Library, EntryPoint="dp_pointer")] internal static extern void Pointer(out double x, out double y);
    [DllImport(Library, EntryPoint="dp_pointer_relative")] internal static extern int PointerRelative(IntPtr handle, out double x, out double y);
    [DllImport(Library, EntryPoint="dp_display_count")] internal static extern int DisplayCount();
    [DllImport(Library, EntryPoint="dp_display")] internal static extern int Display(int index, out MacDisplay display);
    [DllImport(Library, EntryPoint="dp_window")] internal static extern int Window(IntPtr handle, int topmost, int ignoreMouse, int allSpaces);
    [DllImport(Library, EntryPoint="dp_fullscreen_front")] internal static extern int FullscreenFront();
    [DllImport(Library, EntryPoint="dp_hotkey_register")] internal static extern IntPtr HotKeyRegister(uint key, uint modifiers, HotKeyCallback callback, out int error);
    [DllImport(Library, EntryPoint="dp_hotkey_unregister")] internal static extern void HotKeyUnregister(IntPtr handle);
    [DllImport(Library, EntryPoint="dp_login_set")] internal static extern int LoginSet(int enabled);
    [DllImport(Library, EntryPoint="dp_login_status")] internal static extern int LoginStatus();
}
