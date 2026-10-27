using Dafeiyu;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace DeskPet.Desktop;

// NSOpenPanel supports files and folders together; Avalonia's two separate picker APIs do not.
internal static class NativeAttachmentPicker
{
    public static string[] ShowMac()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException(TextResources.Get("nativeAttachmentPicker.showMac.text"));
        var pool = Send(Class("NSAutoreleasePool"), Select("new"));
        try
        {
            var panel = Send(Class("NSOpenPanel"), Select("openPanel"));
            SendBool(panel, Select("setCanChooseFiles:"), true); SendBool(panel, Select("setCanChooseDirectories:"), true);
            SendBool(panel, Select("setAllowsMultipleSelection:"), true); SendBool(panel, Select("setResolvesAliases:"), true);
            SendObject(panel, Select("setTitle:"), String(TextResources.Get("nativeAttachmentPicker.showMac.string"))); SendObject(panel, Select("setPrompt:"), String(TextResources.Get("nativeAttachmentPicker.showMac.string.alternativeA")));
            if (Send(panel, Select("runModal")) != new IntPtr(1)) return Array.Empty<string>();
            var urls = Send(panel, Select("URLs")); var count = Send(urls, Select("count")).ToInt64();
            if (count > 4) throw new InvalidOperationException(TextResources.Get("nativeAttachmentPicker.showMac.text.alternativeA"));
            var paths = new List<string>();
            for (long i = 0; i < count; i++)
            {
                var url = SendIndex(urls, Select("objectAtIndex:"), new IntPtr(i));
                var path = Marshal.PtrToStringUTF8(Send(Send(url, Select("path")), Select("UTF8String")));
                if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) throw new InvalidOperationException(TextResources.Get("nativeAttachmentPicker.showMac.text.alternativeB"));
                paths.Add(path);
            }
            return paths.ToArray();
        }
        finally { Send(pool, Select("drain")); }
    }
    private static IntPtr String(string value) => SendUtf8(Class("NSString"), Select("stringWithUTF8String:"), value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")] private static extern IntPtr Class(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Select(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr SendIndex(IntPtr target, IntPtr selector, IntPtr index);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendObject(IntPtr target, IntPtr selector, IntPtr value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr SendUtf8(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
}
