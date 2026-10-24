using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Dafeiyu.Maintenance
{
    // Windows directory handles deliberately omit FILE_SHARE_DELETE, preventing junction/
    // directory replacement while checked file names are being removed. POSIX removal is
    // relative to pinned directory descriptors, so ancestor swaps cannot redirect unlink.
    internal sealed class CacheDirectoryPins:IDisposable
    {
        readonly Dictionary<string,SafeFileHandle> handles=new Dictionary<string,SafeFileHandle>(Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        static bool Windows {get{return Environment.OSVersion.Platform==PlatformID.Win32NT;}}
        static bool Mac {get{
#if PORTABLE
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#else
            return Environment.OSVersion.Platform==PlatformID.MacOSX;
#endif
        }}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFileInformationByHandle(SafeFileHandle file,int information,ref byte disposition,uint size);
        [DllImport("libc",SetLastError=true)] static extern int open(string path,int flags);
        [DllImport("libc",SetLastError=true)] static extern int openat(int directory,string path,int flags);
        [DllImport("libc",SetLastError=true)] static extern int unlinkat(int directory,string path,int flags);
        [DllImport("libc",SetLastError=true)] static extern int flock(int file,int operation);
        internal static CacheDirectoryPins Open(string path)
        {return Open(path,true);}
        // History maintenance pins only explicitly selected directories. Never
        // walk a project source tree just to protect one canonical data file.
        internal static CacheDirectoryPins OpenDirectory(string path)
        {return Open(path,false);}
        static CacheDirectoryPins Open(string path,bool recursive)
        {
            var pins=new CacheDirectoryPins();
            try{
                var parents=new Stack<string>();for(string parent=Path.GetDirectoryName(path);!String.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent))parents.Push(parent);
                while(parents.Count>0)pins.Pin(parents.Pop());if(recursive)pins.Tree(path);else pins.Pin(path);return pins;
            }catch{pins.Dispose();throw;}
        }
        void Pin(string path)
        {
            CachePaths.Check(path);SafeFileHandle handle;
            if(Windows)handle=CreateFile(path,0,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
            else{int flags=Mac?0x1100100:0xB0000;string parent=Path.GetDirectoryName(path);int fd=String.IsNullOrEmpty(parent)?open(path,flags):openat(Descriptor(parent),Path.GetFileName(path),flags);if(fd<0)throw IoError();handle=new SafeFileHandle(new IntPtr(fd),true);}
            if(handle.IsInvalid){handle.Dispose();throw IoError();}
            handles.Add(path,handle);CachePaths.Check(path);
        }
        void Tree(string path){Pin(path);foreach(string child in Directory.GetDirectories(path)){CachePaths.Check(child);Tree(child);}}
        int Descriptor(string path){return handles[path].DangerousGetHandle().ToInt32();}
        internal void DeleteFile(CacheFile file)
        {
            CachePaths.Check(file.Path);SafeFileHandle handle;
            if(Windows){handle=CreateFile(file.Path,0x80010000,0,IntPtr.Zero,3,0x00200000,IntPtr.Zero);if(handle.IsInvalid){handle.Dispose();throw IoError();}}
            else{int fd=openat(Descriptor(Path.GetDirectoryName(file.Path)),Path.GetFileName(file.Path),Mac?0x1000100:0xA0000);if(fd<0)throw IoError();handle=new SafeFileHandle(new IntPtr(fd),true);if(flock(fd,6)!=0){handle.Dispose();throw IoError();}}
            using(handle)using(var stream=new FileStream(handle,FileAccess.Read))using(var hash=SHA256.Create()){
                if(stream.Length!=file.Bytes||Convert.ToBase64String(hash.ComputeHash(stream))!=file.Hash)throw new IOException("Cache file changed before deletion.");
                CachePaths.Check(file.Path);
                if(Windows){byte disposition=1;if(!SetFileInformationByHandle(handle,4,ref disposition,1))throw IoError();}
                else if(unlinkat(Descriptor(Path.GetDirectoryName(file.Path)),Path.GetFileName(file.Path),0)!=0)throw IoError();
            }
        }
        internal void DeleteDirectory(string path)
        {
            CachePaths.Check(path);Release(path);
            if(Windows)Directory.Delete(path,false);
            else if(unlinkat(Descriptor(Path.GetDirectoryName(path)),Path.GetFileName(path),Mac?0x80:0x200)!=0)throw IoError();
        }
        static Exception IoError(){int code=Marshal.GetLastWin32Error();if(code==5||(!Windows&&(code==1||code==13)))return new UnauthorizedAccessException("Cache entry is not writable.");return new IOException("Cannot safely access cache entry.",new System.ComponentModel.Win32Exception(code));}
        internal void Release(string path){SafeFileHandle handle;if(handles.TryGetValue(path,out handle)){handles.Remove(path);handle.Dispose();}}
        public void Dispose(){foreach(var handle in handles.Values)handle.Dispose();handles.Clear();}
    }
}
