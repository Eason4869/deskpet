using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Dafeiyu.Maintenance;

namespace Dafeiyu.Tests
{
    public static class CacheMaintenanceTests
    {
        static int passed,failed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Run(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
        static string Quote(string value){return "\""+value+"\"";}
        static void Wait(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>deadline)throw new TimeoutException("Cache child fixture");Thread.Sleep(10);}}
        static void Link(string path,string target)
        {
            if(Environment.OSVersion.Platform==PlatformID.Win32NT){using(var process=Process.Start(new ProcessStartInfo("cmd.exe","/c mklink /J "+Quote(path)+" "+Quote(target)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Fixture junction: "+process.StandardError.ReadToEnd());}}
            else if(Symlink(target,path)!=0)throw new IOException("Fixture symlink failed");
        }
        [System.Runtime.InteropServices.DllImport("libc",EntryPoint="symlink",SetLastError=true)] static extern int Symlink(string target,string path);
        sealed class Fixture:IDisposable
        {
            internal readonly string Root=Path.GetFullPath(Path.Combine("work","cache-maintenance-fixtures",Guid.NewGuid().ToString("N"),"data"));
            // Advance the injected maintenance clock, not real directory timestamps. Directory
            // timestamp setters can contend with Windows indexing immediately after creation.
            internal readonly DateTime Now=DateTime.UtcNow.AddDays(8);
            internal Fixture(){Directory.CreateDirectory(Root);}
            internal string Candidate(string relative,int bytes=17)
            {
                string path=Path.GetFullPath(Path.Combine(Root,relative));
                using(CacheResourceLease.Acquire(Root,path)){File.WriteAllBytes(Path.Combine(path,"package.zip"),new byte[bytes]);}
                Age(path);return path;
            }
            internal void Age(string path){foreach(string file in Directory.GetFiles(path,"*",SearchOption.AllDirectories))File.SetLastWriteTimeUtc(file,Now.AddDays(-8));}
            public void Dispose(){} // Keep owned fixture evidence; never recursively delete a caller path.
        }
        public static int Main(string[] args)
        {
            if(args.Length==3&&args[0]=="--cache-lease-fixture"){
                using(CacheResourceLease.Acquire(args[1],args[2])){File.WriteAllText(Path.Combine(args[1],"child-ready"),"ready");while(!File.Exists(Path.Combine(args[1],"child-release")))Thread.Sleep(20);}return 0;
            }
            Run("only registered expired cache roots are candidates; all user data unchanged",()=>{
                using(var f=new Fixture()){
                    f.Candidate("updates/"+new string('a',32),17);f.Candidate("official-companions/e-bao/.stage-"+new string('b',32),23);
                    string[] preserved={"settings.json","relationships/state.json","relationships/pending.json","history/a.history","images/temporary-x/user.png","official-companions/e-bao/v-installed/pet.json","exports/card.png","projects/project/source/file.tmp","updates/"+new string('c',32)+"/old-unregistered.zip"};
                    foreach(string name in preserved){string path=Path.Combine(f.Root,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"preserved:"+name);File.SetLastWriteTimeUtc(path,f.Now.AddDays(-90));}
                    var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);Check(scan.Candidates.Count==2&&scan.TotalBytes==40,"whitelist or byte total");
                    var result=service.Clean(scan,f.Now);Check(result.FreedBytes==40&&result.DeletedItems==2&&result.FailedItems==0,"actual deletion totals");
                    foreach(string name in preserved)Check(File.ReadAllText(Path.Combine(f.Root,name))=="preserved:"+name,"changed "+name);
                    Check(service.Clean(scan,f.Now).FreedBytes==0&&service.Scan(f.Now).TotalBytes==0,"repeat overcount");
                }
            });
            Run("new lease after scan and simultaneous owners protect the resource",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32));var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);
                    using(var a=CacheResourceLease.Acquire(f.Root,path))using(var b=CacheResourceLease.Acquire(f.Root,path)){
                        Check(service.Scan(f.Now).TotalBytes==0,"active scan");var r=service.Clean(scan,f.Now);Check(r.FreedBytes==0&&r.SkippedItems==1&&File.Exists(Path.Combine(path,"package.zip")),"scan/use race");
                    }
                    Check(service.Clean(scan,f.Now).FreedBytes==17,"released lease still blocked");
                }
            });
            Run("legacy or current job ready applying and rollback directories remain protected",()=>{
                foreach(string marker in new[]{"job.json","ready","staging/file","backup/file","installer-backup/file"})using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+Guid.NewGuid().ToString("N"));string file=Path.Combine(path,marker);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,"uncertain operation");f.Age(path);
                    Check(new CacheMaintenanceService(f.Root).Scan(f.Now).TotalBytes==0,"protected "+marker);
                }
            });
            Run("content size timestamp and directory replacement invalidate scanned snapshot",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32));var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);
                    File.WriteAllBytes(Path.Combine(path,"package.zip"),Enumerable.Repeat((byte)7,17).ToArray());f.Age(path);
                    Check(service.Clean(scan,f.Now).SkippedItems==1&&File.Exists(Path.Combine(path,"package.zip")),"same size changed content removed");
                    scan=service.Scan(f.Now);Directory.Move(path,path+"-kept");Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,"package.zip"),Enumerable.Repeat((byte)7,17).ToArray());f.Age(path);
                    Check(service.Clean(scan,f.Now).SkippedItems==1,"directory replacement removed");
                }
            });
            Run("partial clean reports actual bytes and skips locked files",()=>{
                using(var f=new Fixture()){
                    string blocked=f.Candidate("updates/"+new string('a',32),11);f.Candidate("updates/"+new string('b',32),19);var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);
                    using(File.Open(Path.Combine(blocked,"package.zip"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
                        var r=service.Clean(scan,f.Now);Check(r.FreedBytes==19&&r.DeletedItems==1&&r.SkippedItems+r.FailedItems==1,"partial totals");
                    }
                    scan=service.Scan(f.Now);
                    using(File.Open(Path.Combine(blocked,"package.zip"),FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                        var r=service.Clean(scan,f.Now);Check(r.FreedBytes==0&&r.SkippedItems==1&&File.Exists(Path.Combine(blocked,"package.zip")),"readable in-use file was removed");
                    }
                }
            });
            Run("recent resource and arbitrary tmp directory are never candidates",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32));File.SetLastWriteTimeUtc(Path.Combine(path,"package.zip"),f.Now);
                    Check(new CacheMaintenanceService(f.Root).Scan(f.Now).Candidates.Count==0,"recent file");
                    bool rejected=false;try{using(CacheResourceLease.Acquire(f.Root,Path.Combine(f.Root,"images","temporary-user"))){}}catch(ArgumentException){rejected=true;}
                    Check(rejected,"non-cache registration permitted");
                }
            });
            Run("proven completed update is reclaimable while changed result or installation is protected",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32));string install=Path.GetDirectoryName(f.Root);
                    File.Copy(typeof(CacheMaintenanceTests).Assembly.Location,Path.Combine(install,"桌面伙伴.exe"),true);
                    var json=new System.Web.Script.Serialization.JavaScriptSerializer();
                    File.WriteAllText(Path.Combine(path,"job.json"),json.Serialize(new{InstallationDirectory=install,WorkDirectory=path,TargetVersion=ReleaseInfo.DisplayVersion}));
                    File.WriteAllText(Path.Combine(path,"result.json"),json.Serialize(new{Success=true,Version=ReleaseInfo.DisplayVersion}));Directory.CreateDirectory(Path.Combine(path,"backup"));File.WriteAllText(Path.Combine(path,"backup","previous.exe"),"rollback");
                    CacheUpdateRetirement.RecordCompleted(f.Root,path,ReleaseInfo.DisplayVersion);f.Age(path);
                    var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);Check(scan.Candidates.Count==1,"completed job never cleanable");
                    File.WriteAllText(Path.Combine(path,"result.json"),json.Serialize(new{Success=false,Version=ReleaseInfo.DisplayVersion}));f.Age(path);Check(service.Clean(scan,f.Now).SkippedItems==1,"changed result deleted rollback");
                    File.WriteAllText(Path.Combine(path,"result.json"),json.Serialize(new{Success=true,Version=ReleaseInfo.DisplayVersion}));CacheUpdateRetirement.RecordCompleted(f.Root,path,ReleaseInfo.DisplayVersion);f.Age(path);scan=service.Scan(f.Now);
                    Check(service.Clean(scan,f.Now).DeletedItems==1&&!Directory.Exists(path),"proven completed update not deleted");
                }
            });
            foreach(string record in new[]{"job.json","result.json","proof"})Run("null "+record+" protects only that update and scan continues",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32)),install=Path.GetDirectoryName(f.Root);
                    File.Copy(typeof(CacheMaintenanceTests).Assembly.Location,Path.Combine(install,"桌面伙伴.exe"),true);
                    var json=new System.Web.Script.Serialization.JavaScriptSerializer();
                    File.WriteAllText(Path.Combine(path,"job.json"),json.Serialize(new{InstallationDirectory=install,WorkDirectory=path,TargetVersion=ReleaseInfo.DisplayVersion}));
                    File.WriteAllText(Path.Combine(path,"result.json"),json.Serialize(new{Success=true,Version=ReleaseInfo.DisplayVersion}));
                    Directory.CreateDirectory(Path.Combine(path,"backup"));File.WriteAllText(Path.Combine(path,"backup","previous.exe"),"retain rollback");
                    CacheUpdateRetirement.RecordCompleted(f.Root,path,ReleaseInfo.DisplayVersion);
                    string proof=Directory.GetFiles(Path.Combine(f.Root,"cache-maintenance"),"*.completed").Single();
                    File.WriteAllText(record=="proof"?proof:Path.Combine(path,record),"null");f.Age(path);
                    var retained=Directory.GetFiles(path,"*",SearchOption.AllDirectories).Concat(new[]{proof}).ToDictionary(p=>p,p=>File.ReadAllBytes(p));
                    string clean=f.Candidate("updates/"+new string('b',32),19);var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);
                    Check(scan.Candidates.Count==1&&scan.TotalBytes==19,"bad record aborted or entered scan");
                    var result=service.Clean(scan,f.Now);Check(result.DeletedItems==1&&result.FreedBytes==19&&!Directory.Exists(clean),"unrelated valid cache was not reclaimed");
                    foreach(var pair in retained)Check(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value),"invalid update metadata or rollback changed");
                    Check(service.Scan(f.Now).Candidates.Count==0,"rescan did not protect null update");
                }
            });
            Run("another process holds resource across scan execution and releases on exit",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32));var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);
                    string exe=Process.GetCurrentProcess().MainModule.FileName,assembly=typeof(CacheMaintenanceTests).Assembly.Location;
                    string arguments=(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase)?Quote(assembly)+" ":"")+"--cache-lease-fixture "+Quote(f.Root)+" "+Quote(path);
                    using(var child=Process.Start(new ProcessStartInfo(exe,arguments){UseShellExecute=false,CreateNoWindow=true})){
                        try{Wait(()=>child.HasExited||File.Exists(Path.Combine(f.Root,"child-ready")));Check(!child.HasExited,"child failed");Check(service.Clean(scan,f.Now).SkippedItems==1&&Directory.Exists(path),"cross-process lease ignored");}
                        finally{File.WriteAllText(Path.Combine(f.Root,"child-release"),"release");if(!child.WaitForExit(15000))throw new TimeoutException("Lease child exit");}
                    }
                    Check(service.Clean(scan,f.Now).DeletedItems==1,"child exit did not release lock");
                }
            });
            Run("junction swaps and nested links cannot delete an outside protected tree",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32)),outside=Path.Combine(f.Root,"user-images");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"original.png"),"original");
                    var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);
                    Directory.Move(path,path+"-saved");Link(path,outside);Check(service.Clean(scan,f.Now).SkippedItems==1&&File.ReadAllText(Path.Combine(outside,"original.png"))=="original","swapped junction followed");
                    string nested=f.Candidate("updates/"+new string('b',32));Link(Path.Combine(nested,"linked"),outside);Check(service.Scan(f.Now.AddDays(9)).TotalBytes==0&&File.ReadAllText(Path.Combine(outside,"original.png"))=="original","nested junction followed");
                }
            });
            Run("read-only deletion failure reports partial bytes without whole-success",()=>{
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return;
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32),13);File.WriteAllBytes(Path.Combine(path,"z-denied.zip"),new byte[29]);f.Age(path);File.SetAttributes(Path.Combine(path,"z-denied.zip"),FileAttributes.ReadOnly);
                    var service=new CacheMaintenanceService(f.Root);var result=service.Clean(service.Scan(f.Now),f.Now);
                    Check(result.FreedBytes==13&&result.DeletedItems==0&&result.FailedItems==1&&File.Exists(Path.Combine(path,"z-denied.zip")),"partial I/O failure misreported");
                }
            });
            Run("replacement with copied timestamps cannot reuse registered directory identity",()=>{
                using(var f=new Fixture()){
                    string path=f.Candidate("updates/"+new string('a',32));var service=new CacheMaintenanceService(f.Root);var scan=service.Scan(f.Now);
                    DateTime created=Directory.GetCreationTimeUtc(path),written=Directory.GetLastWriteTimeUtc(path),fileCreated=File.GetCreationTimeUtc(Path.Combine(path,"package.zip"));
                    Directory.Move(path,path+"-saved");Directory.CreateDirectory(path);File.Copy(Path.Combine(path+"-saved","package.zip"),Path.Combine(path,"package.zip"));File.SetCreationTimeUtc(Path.Combine(path,"package.zip"),fileCreated);File.SetLastWriteTimeUtc(Path.Combine(path,"package.zip"),f.Now.AddDays(-8));Directory.SetCreationTimeUtc(path,created);Directory.SetLastWriteTimeUtc(path,written);
                    Check(service.Clean(scan,f.Now).SkippedItems==1&&File.Exists(Path.Combine(path,"package.zip")),"copied timestamps fooled identity");
                }
            });
            Console.WriteLine("CacheMaintenanceTests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
    }
}
