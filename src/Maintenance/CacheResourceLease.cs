using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dafeiyu.Maintenance
{
    // Persistent registration proves ownership; an open shared handle proves current use.
    // The lock is outside the resource so publication/moves never copy it into a character pack.
    public sealed class CacheResourceLease : IDisposable
    {
        FileStream handle;
        CacheResourceLease(FileStream handle){this.handle=handle;}
        public static CacheResourceLease Acquire(string dataRoot,string resourceDirectory)
        {return Acquire(dataRoot,resourceDirectory,true);}
        // A validated helper may protect a job from a legacy parent, but never marks
        // that pre-existing unregistered directory as safe to reclaim.
        public static CacheResourceLease ProtectExisting(string dataRoot,string resourceDirectory)
        {return Acquire(dataRoot,resourceDirectory,false);}
        static CacheResourceLease Acquire(string dataRoot,string resourceDirectory,bool register)
        {
            string root=CachePaths.Normalize(dataRoot),path=CachePaths.Normalize(resourceDirectory);
            if(CachePaths.Category(root,path)==null)throw new ArgumentException("Not an application cache resource.");
            CachePaths.Check(root);CachePaths.Check(path);
            string registry=Path.Combine(root,"cache-maintenance");CachePaths.Check(registry);Directory.CreateDirectory(registry);
            string key=Key(registry,path);CachePaths.Check(key+".lock");
            var stream=new FileStream(key+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
            try{
                CachePaths.Check(path);bool created=!Directory.Exists(path);Directory.CreateDirectory(path);CachePaths.Check(path);
                string record=key+".owner";CachePaths.Check(record);
                if(register&&(created||!File.Exists(record))){
                    // Existing unregistered directories cannot be adopted: an older downloader
                    // may still use them without understanding this lease protocol.
                    if(!created)throw new IOException("Unregistered cache resource is protected.");
                    File.WriteAllText(record,"DeskPet-cache-1\n"+path+"\n"+CachePaths.Identity(path),new UTF8Encoding(false));
                }
                if(register&&!Registered(root,path))throw new IOException("Cache resource identity changed.");
                return new CacheResourceLease(stream);
            }catch{stream.Dispose();throw;}
        }
        internal static FileStream TryExclusive(string root,string path)
        {
            string key=Key(Path.Combine(root,"cache-maintenance"),path);CachePaths.Check(key+".lock");
            return new FileStream(key+".lock",FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        }
        internal static bool Registered(string root,string path)
        {
            string record=Key(Path.Combine(root,"cache-maintenance"),path)+".owner";CachePaths.Check(record);
            if(!File.Exists(record)||new FileInfo(record).Length>8192||!Directory.Exists(path))return false;
            return File.ReadAllText(record)=="DeskPet-cache-1\n"+path+"\n"+CachePaths.Identity(path);
        }
        internal static string Key(string registry,string path)
        {
            string canonical=Environment.OSVersion.Platform==PlatformID.Win32NT?path.ToUpperInvariant():path;
            using(var sha=SHA256.Create())return Path.Combine(registry,BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-","").ToLowerInvariant());
        }
        public void Dispose(){var current=System.Threading.Interlocked.Exchange(ref handle,null);if(current!=null)current.Dispose();}
    }

    internal static class CachePaths
    {
        [StructLayout(LayoutKind.Sequential)] struct FileInformation
        {
            public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Created,Accessed,Written;
            public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle file,out FileInformation information);
        [DllImport("libc",EntryPoint="open",SetLastError=true)] static extern int OpenUnix(string path,int flags);
        [DllImport("libc",SetLastError=true)] static extern int fstat(int file,IntPtr stat);
        [DllImport("libc",EntryPoint="fstat$INODE64",SetLastError=true)] static extern int fstatDarwinInode64(int file,IntPtr stat);
        static int Stat(int file,IntPtr buffer,bool mac)
        {
#if PORTABLE
            // Darwin x86_64 keeps both legacy and inode64 symbols; arm64 exposes only
            // the modern layout via plain fstat. Match stat.h __DARWIN_INODE64 and
            // cdefs.h __DARWIN_ONLY_64_BIT_INO_T, never interpret legacy x64 stat bytes.
            // https://github.com/apple/darwin-xnu/blob/main/bsd/sys/stat.h
            // https://github.com/apple/darwin-xnu/blob/main/bsd/sys/cdefs.h
            if(mac){if(RuntimeInformation.ProcessArchitecture==Architecture.X64)return fstatDarwinInode64(file,buffer);if(RuntimeInformation.ProcessArchitecture!=Architecture.Arm64)throw new IOException("Unsupported Darwin file identity ABI.");}
#endif
            return fstat(file,buffer);
        }
        internal static string Identity(string path)
        {
            Check(path);
            if(Environment.OSVersion.Platform==PlatformID.Win32NT){
                using(var handle=CreateFile(NativePath(path),0,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero)){
                    FileInformation info;if(handle.IsInvalid||!GetFileInformationByHandle(handle,out info))throw new IOException("Cannot read cache file identity.");
                    return info.Volume+":"+info.IndexHigh+":"+info.IndexLow;
                }
            }
#if PORTABLE
            bool mac=RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#else
            bool mac=Environment.OSVersion.Platform==PlatformID.MacOSX;
#endif
            int fd=OpenUnix(path,mac?0x1000100:0xA0000);if(fd<0)throw new IOException("Cannot read cache file identity.");
            using(var handle=new SafeFileHandle(new IntPtr(fd),true)){
                // Linux and Darwin stat layouts both place the 64-bit inode at offset 8.
                // Darwin uses a 32-bit device id; Linux uses 64 bits. Reserve more than either ABI.
                IntPtr buffer=Marshal.AllocHGlobal(512);try{if(Stat(fd,buffer,mac)!=0)throw new IOException("Cannot stat cache file.");return (mac?(long)Marshal.ReadInt32(buffer):Marshal.ReadInt64(buffer))+":"+Marshal.ReadInt64(buffer,8);}finally{Marshal.FreeHGlobal(buffer);}
            }
        }
        internal static string NativePath(string path)
        {
            string full=Path.GetFullPath(path);if(full.StartsWith(@"\\?\",StringComparison.Ordinal))return full;
            return full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full.Substring(2):@"\\?\"+full;
        }
        internal static readonly StringComparison Comparison=Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        internal static string Normalize(string path){return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
        internal static string Category(string root,string path)
        {
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,Comparison))return null;
            string relative=path.Substring(root.Length+1).Replace(Path.DirectorySeparatorChar,'/');
            if(Regex.IsMatch(relative,@"\Aupdates/[a-f0-9]{32}\z"))return "updates";
            if(Regex.IsMatch(relative,@"\Aofficial-companions/(e-bao|dafeiyu|marfu|xiaobei|gugugaga)/\.(stage|transfer)-[a-f0-9]{32}\z"))return "downloads";
            if(Regex.IsMatch(relative,@"\Aofficial-companions/(e-bao|dafeiyu|marfu|xiaobei|gugugaga)/v2-[a-zA-Z0-9.-]+-[a-f0-9]{32}-[a-f0-9]{32}\z"))return "pack-versions";
            return null;
        }
        internal static void Check(string path)
        {
            for(string current=Path.GetFullPath(path);!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)){
                // GetAttributes also detects dangling links on supported runtimes.
                try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked cache paths are protected.");}
                catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
            }
        }
        internal static bool StorageError(Exception e){return e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is System.Security.SecurityException;}
    }
}
