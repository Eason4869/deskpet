using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Dafeiyu.Tests
{
    // Pure filesystem tests: never create Application, a browser, or a network request.
    public static class BrowserProfileCleanupTests
    {
        const string Marker=".deskpet-preview-owner",Owner="DeskPet WebPreview runtime v1";
        static int checks;
        static Type runtime=typeof(BrowserProfileCleanupTests).Assembly.GetType("Dafeiyu.WebPreviewRuntime");
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static object Invoke(string name,params object[] args){return runtime.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}
        static string Owned(string parent,bool expired){string root=Path.Combine(parent,"DeskPet-Preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,Marker),Owner,new UTF8Encoding(false));File.WriteAllText(Path.Combine(root,"payload.txt"),"fixture");if(expired)File.SetLastWriteTimeUtc(Path.Combine(root,Marker),DateTime.UtcNow.AddDays(-2));return root;}
        [STAThread]public static int Main()
        {
            string parent=Path.Combine(Directory.GetCurrentDirectory(),"work","browser-cleanup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(parent);
            try{
                Check(runtime.GetMethod("CleanupStaleDirectories",BindingFlags.Static|BindingFlags.NonPublic)!=null,"runtime has safe stale-profile cleanup");
                DateTime cutoff=DateTime.UtcNow.AddDays(-1);string stale=Owned(parent,true),fresh=Owned(parent,false),live=Owned(parent,true),unknown=Owned(parent,true),other=Path.Combine(parent,"unrelated-user-data");File.Delete(Path.Combine(unknown,Marker));Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"keep.txt"),"keep");
                using(var lease=new FileStream(Path.Combine(live,Marker),FileMode.Open,FileAccess.Read,FileShare.Read)){
                    Invoke("CleanupStaleDirectories",parent,cutoff,true);Check(Directory.Exists(stale),"a running WebView process prevents stale-root deletion");
                    Invoke("CleanupStaleDirectories",parent,cutoff,false);Check(!Directory.Exists(stale),"expired owned runtime is reclaimed after all browser processes stop");Check(Directory.Exists(live),"an active application lease protects its runtime even before browser startup");
                }
                Check(Directory.Exists(fresh)&&Directory.Exists(unknown)&&File.Exists(Path.Combine(other,"keep.txt")),"fresh roots unmarked roots and unrelated files are preserved");
                Invoke("CleanupStaleDirectories",parent,cutoff,false);Check(!Directory.Exists(live),"an expired runtime becomes reclaimable after its lease closes");
                string owned=Owned(parent,false),profile=Path.Combine(owned,"browser-profile-"+Guid.NewGuid().ToString("N")),outside=Path.Combine(parent,"browser-profile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(profile);File.WriteAllText(Path.Combine(profile,"cache"),"cache");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"keep"),"keep");
                Check(!(bool)Invoke("TryDeleteExitedBrowserProfile",owned,outside)&&Directory.Exists(outside),"profile cleanup rejects a directory outside its verified runtime root");
                Check(!(bool)Invoke("TryDeleteExitedBrowserProfile",owned,owned)&&Directory.Exists(owned),"profile cleanup cannot delete the runtime root itself");
                Check((bool)Invoke("TryDeleteExitedBrowserProfile",owned,profile)&&!Directory.Exists(profile)&&Directory.Exists(owned),"an exited browser profile is reclaimed within the owned absolute boundary");
                string unmanaged=Path.Combine(owned,"user-folder");Directory.CreateDirectory(unmanaged);Check(!(bool)Invoke("TryDeleteExitedBrowserProfile",owned,unmanaged)&&Directory.Exists(unmanaged),"profile cleanup preserves non-profile directories inside its root");
                Check(!(bool)Invoke("TryDeleteExitedBrowserProfile",unknown,Path.Combine(unknown,"browser-profile-"+Guid.NewGuid().ToString("N"))),"an unmarked runtime cannot authorize profile cleanup");
                string invalidName=Path.Combine(parent,"DeskPet-Preview-user-data");Directory.CreateDirectory(invalidName);File.WriteAllText(Path.Combine(invalidName,Marker),Owner);File.SetLastWriteTimeUtc(Path.Combine(invalidName,Marker),DateTime.UtcNow.AddDays(-2));Invoke("CleanupStaleDirectories",parent,cutoff,false);Check(Directory.Exists(invalidName),"a similar name without a generated GUID never authorizes recursive deletion");
                Console.WriteLine("Browser profile cleanup tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
            finally{string expected=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),"work"))+Path.DirectorySeparatorChar;if(Path.GetFullPath(parent).StartsWith(expected,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(parent))Directory.Delete(parent,true);}
        }
    }
}
