using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Dafeiyu
{
    internal sealed class OpenWithApplication
    {
        internal readonly string Name;
        readonly Action<string> open;
        internal OpenWithApplication(string name,Action<string> open){Name=name;this.open=open;}
        internal void Open(string path){open(path);}
    }

    // Ask the Shell for this extension's registered handlers, including packaged
    // applications. Invoking the handler preserves its own launch protocol.
    internal static class WindowsOpenWith
    {
        internal static IList<OpenWithApplication> Applications(string path)
        {
            var result=new List<OpenWithApplication>();
            string extension=Path.GetExtension(path);
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT||String.IsNullOrEmpty(extension))return result;
            IEnumAssocHandlers handlers=null;
            try
            {
                Marshal.ThrowExceptionForHR(SHAssocEnumHandlers(extension,0,out handlers));
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while(handlers!=null)
                {
                    IAssocHandler handler;uint fetched;
                    int status=handlers.Next(1,out handler,out fetched);
                    Marshal.ThrowExceptionForHR(status);
                    if(fetched!=1||handler==null)break;
                    bool retained=false;
                    try
                    {
                        string id,name;handler.GetName(out id);handler.GetUIName(out name);
                        if(String.IsNullOrWhiteSpace(id)||!seen.Add(id))continue;
                        if(String.IsNullOrWhiteSpace(name))name=Path.GetFileNameWithoutExtension(id);
                        var selected=handler;
                        result.Add(new OpenWithApplication(name,file=>Invoke(selected,file)));
                        retained=true;
                    }
                    catch(COMException error){System.Diagnostics.Debug.WriteLine(error);}
                    finally{if(!retained)Marshal.ReleaseComObject(handler);}
                }
            }
            // A stale or unavailable association must not remove the system picker.
            catch(COMException error){System.Diagnostics.Debug.WriteLine(error);}
            finally{if(handlers!=null)Marshal.ReleaseComObject(handlers);}
            return result.OrderBy(app=>app.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static void Invoke(IAssocHandler handler,string path)
        {
            RequireFile(path);
            IShellItem item=null;IntPtr data=IntPtr.Zero;
            try
            {
                var itemId=new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(path),IntPtr.Zero,ref itemId,out item));
                var dataHandler=new Guid("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
                var dataId=new Guid("0000010E-0000-0000-C000-000000000046");
                item.BindToHandler(IntPtr.Zero,ref dataHandler,ref dataId,out data);
                handler.Invoke(data);
            }
            finally{if(data!=IntPtr.Zero)Marshal.Release(data);if(item!=null)Marshal.ReleaseComObject(item);}
        }

        internal static void ChooseApplication(string path,IntPtr owner)
        {
            RequireFile(path);
            // Execute once; never register the selected app as the default.
            var info=new OpenAsInfo{File=Path.GetFullPath(path),Flags=0x04|0x20};
            int status=SHOpenWithDialog(owner,ref info);
            if(status==unchecked((int)0x800704C7)||status==unchecked((int)0x80004004))return;
            Marshal.ThrowExceptionForHR(status);
        }
        static void RequireFile(string path){if(!File.Exists(path))throw new FileNotFoundException(TextResources.Get("replyFiles.openOutput.text"),path);}

        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct OpenAsInfo{[MarshalAs(UnmanagedType.LPWStr)]internal string File;[MarshalAs(UnmanagedType.LPWStr)]internal string Class;internal uint Flags;}
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int SHAssocEnumHandlers(string extension,uint filter,out IEnumAssocHandlers handlers);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid id,out IShellItem item);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int SHOpenWithDialog(IntPtr owner,ref OpenAsInfo info);

        [ComImport,Guid("973810AE-9599-4B88-9E4D-6EE98C9552DA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEnumAssocHandlers{[PreserveSig]int Next(uint count,out IAssocHandler handler,out uint fetched);}
        [ComImport,Guid("F04061AC-1659-4A3F-A954-775AA57FC083"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAssocHandler
        {
            void GetName([MarshalAs(UnmanagedType.LPWStr)]out string name);
            void GetUIName([MarshalAs(UnmanagedType.LPWStr)]out string name);
            void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)]out string path,out int index);
            [PreserveSig]int IsRecommended();
            void MakeDefault([MarshalAs(UnmanagedType.LPWStr)]string description);
            void Invoke(IntPtr data);
            void CreateInvoker(IntPtr data,out IntPtr invoker);
        }
        [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem{void BindToHandler(IntPtr context,ref Guid handler,ref Guid id,out IntPtr result);}
    }
}
