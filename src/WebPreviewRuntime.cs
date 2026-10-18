using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
namespace Dafeiyu
{
    internal static class WebPreviewRuntime
    {
        internal const string Version="1.0.4258.31";
        static readonly object gate=new object();static bool loaded;
        static bool nativeConfigured;
        const string OwnerFile=".deskpet-preview-owner",Owner="DeskPet WebPreview runtime v1";
        static FileStream runtimeLease;
        static readonly Dictionary<string,Assembly> assemblies=new Dictionary<string,Assembly>();
        internal static string DirectoryPath{get;private set;}
        internal static void Ensure()
        {
            lock(gate){if(loaded)return;
                AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args){Assembly assembly;return assemblies.TryGetValue(new AssemblyName(args.Name).Name,out assembly)?assembly:null;};
                foreach(string name in new[]{"Microsoft.Web.WebView2.Core","Microsoft.Web.WebView2.Wpf"})using(var input=typeof(WebPreviewRuntime).Assembly.GetManifestResourceStream("Dafeiyu.WebView2."+name+".dll"))using(var data=new MemoryStream()){if(input==null)throw new FileNotFoundException("缺少网页预览组件。");input.CopyTo(data);assemblies[name]=Assembly.Load(data.ToArray());}
                DirectoryPath=Path.Combine(Path.GetTempPath(),"DeskPet-Preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(DirectoryPath);
                string marker=Path.Combine(DirectoryPath,OwnerFile);File.WriteAllText(marker,Owner,new UTF8Encoding(false));runtimeLease=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read);
                // One bounded startup sweep; abandoned profiles must never delay navigation.
                Task.Run(()=>CleanupStaleDirectories(Path.GetTempPath(),DateTime.UtcNow.AddDays(-1),BrowsersRunning()));
                using(var input=typeof(WebPreviewRuntime).Assembly.GetManifestResourceStream("Dafeiyu.WebView2.WebView2Loader."+(IntPtr.Size==8?"x64":"x86")+".dll"))using(var output=new FileStream(Path.Combine(DirectoryPath,"WebView2Loader.dll"),FileMode.CreateNew,FileAccess.Write)){if(input==null)throw new FileNotFoundException("缺少网页预览组件。");input.CopyTo(output);}
                loaded=true;
            }
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void ConfigureNativeLoader(){if(nativeConfigured)return;Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(DirectoryPath);nativeConfigured=true;}

        static bool CleanupError(Exception error){return error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException||error is System.Security.SecurityException;}
        static bool GeneratedName(string value,string prefix){Guid id;return value.StartsWith(prefix,StringComparison.Ordinal)&&value.Length==prefix.Length+32&&Guid.TryParseExact(value.Substring(prefix.Length),"N",out id);}
        static bool LinkedAncestors(string path){for(var item=new DirectoryInfo(path);item!=null;item=item.Parent)if(item.Exists&&(item.Attributes&FileAttributes.ReparsePoint)!=0)return true;return false;}
        static bool UnlinkedTree(string root){foreach(string child in Directory.EnumerateFileSystemEntries(root)){var attributes=File.GetAttributes(child);if((attributes&FileAttributes.ReparsePoint)!=0)return false;if((attributes&FileAttributes.Directory)!=0&&!UnlinkedTree(child))return false;}return true;}
        static bool OwnedRoot(string root)
        {
            string marker=Path.Combine(root,OwnerFile);return GeneratedName(Path.GetFileName(root),"DeskPet-Preview-")&&!LinkedAncestors(root)&&File.Exists(marker)&&(File.GetAttributes(marker)&FileAttributes.ReparsePoint)==0&&new FileInfo(marker).Length<256&&File.ReadAllText(marker)==Owner;
        }
        static bool BrowsersRunning()
        {
            try{var processes=Process.GetProcessesByName("msedgewebview2");bool running=processes.Length>0;foreach(var process in processes)process.Dispose();return running;}
            catch(Exception error){if(!CleanupError(error)&&!(error is InvalidOperationException||error is System.ComponentModel.Win32Exception))throw;return true;}
        }
        // The parent and candidate are resolved before any recursive deletion. Unknown,
        // linked, fresh, leased or potentially live browser directories are left alone.
        internal static void CleanupStaleDirectories(string parent,DateTime cutoffUtc,bool browsersRunning)
        {
            if(browsersRunning)return;
            try{
                parent=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(LinkedAncestors(parent))return;
                foreach(string candidate in Directory.EnumerateDirectories(parent,"DeskPet-Preview-*"))try{
                    string root=Path.GetFullPath(candidate);if(!String.Equals(Path.GetDirectoryName(root),parent,StringComparison.OrdinalIgnoreCase)||!OwnedRoot(root))continue;
                    string marker=Path.Combine(root,OwnerFile);if(File.GetLastWriteTimeUtc(marker)>=cutoffUtc)continue;
                    using(var lease=new FileStream(marker,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){if(!UnlinkedTree(root))continue;}
                    Directory.Delete(root,true);
                }catch(Exception error){if(!CleanupError(error))throw;}
            }catch(Exception error){if(!CleanupError(error))throw;}
        }
        internal static bool TryDeleteExitedBrowserProfile(string runtimeRoot,string profile)
        {
            try{
                string root=Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),target=Path.GetFullPath(profile);
                if(!String.Equals(Path.GetDirectoryName(target),root,StringComparison.OrdinalIgnoreCase)||!GeneratedName(Path.GetFileName(target),"browser-profile-")||!OwnedRoot(root))return false;
                if(!Directory.Exists(target))return true;if(LinkedAncestors(target)||!UnlinkedTree(target))return false;
                Directory.Delete(target,true);return true;
            }catch(Exception error){if(!CleanupError(error))throw;return false;}
        }
    }
}
