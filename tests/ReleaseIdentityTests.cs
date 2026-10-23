using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
namespace Dafeiyu.Tests
{
    public static class ReleaseIdentityTests
    {
        static void Compile(string path,string version,string sha)
        {
            string source=path+".cs";File.WriteAllText(source,"[assembly:System.Reflection.AssemblyVersion(\"0.9.4.0\")][assembly:System.Reflection.AssemblyFileVersion(\"0.9.4.0\")][assembly:System.Reflection.AssemblyInformationalVersion(\""+version+"+"+sha+"\")] class Fixture { static void Main(){} }",System.Text.Encoding.UTF8);
            using(var process=Process.Start(new ProcessStartInfo(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"csc.exe"),"/nologo /out:"+UpdateInstall.Quote(path)+" "+UpdateInstall.Quote(source)){UseShellExecute=false,CreateNoWindow=true})){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("metadata fixture compilation failed");}
        }
        static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("invalid identity accepted");}
        public static int Main()
        {
            try {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"identity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                string old=Path.Combine(root,UpdateInstall.ExecutableName),target=Path.Combine(root,"target.exe"),sha=new string('a',40),oldSha=new string('b',40);
                Compile(old,"0.9.4-dev.2",oldSha);Compile(target,"0.9.4",sha);
                var identity=ReleaseIdentity.Read(old);if(!identity.Version.IsPreview||identity.AssemblyVersion.ToString()!="0.9.4.0")throw new Exception("preview PE identity lost");
                Reject(()=>identity.Require("0.9.4","0.9.4.0","0.9.4+"+sha));
                string work=Path.Combine(root,"data","updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);string package=Path.Combine(work,"DeskPet-Windows-0.9.4.zip");
                using(var zip=new ZipArchive(File.Create(package),ZipArchiveMode.Create))foreach(string name in new[]{UpdateInstall.ExecutableName,UpdateInstall.WorkerName,UpdateInstall.ExecutableName+".config"}) {
                    using(var output=zip.CreateEntry("DafeiyuDeskPet/"+name).Open()){if(name.EndsWith(".exe"))using(var input=File.OpenRead(target))input.CopyTo(output);else output.WriteByte(0);}
                }
                var job=new UpdateJob{ProtocolVersion=2,InstallationDirectory=root,WorkDirectory=work,PackagePath=package,Mode="portable",InstalledVersion="0.9.4-dev.2",TargetVersion="0.9.4",InstalledAssemblyVersion="0.9.4.0",TargetAssemblyVersion="0.9.4.0",InstalledBuildId="0.9.4-dev.2+"+oldSha,TargetBuildId="0.9.4+"+sha,Size=new FileInfo(package).Length,Digest=UpdateInstall.Hash(package),AssetUrl=ReleaseInfo.RepositoryUrl+"/releases/download/v0.9.4/DeskPet-Windows-0.9.4.zip"};
                UpdateInstall.Validate(job);string good=job.TargetBuildId;job.TargetBuildId="0.9.4+unverified";Reject(()=>UpdateInstall.Validate(job));job.TargetBuildId=good;
                UpdateInstall.CheckWorker(job,old);Reject(()=>UpdateInstall.CheckWorker(job,target));
                Compile(target,"0.9.4-dev.2",sha);Reject(()=>UpdateInstall.CheckWorker(job,target));Compile(target,"0.9.4",sha);
                string docs=Path.Combine(root,"docs");Directory.CreateDirectory(docs);
                string project=Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                string retired=Path.Combine(root,"templates","pet.json");Directory.CreateDirectory(Path.GetDirectoryName(retired));
                File.Copy(Path.Combine(project,"templates","pet.json"),retired);
                if(UpdateInstall.Hash(retired)!=RetiredRuntimeFiles.Known["templates/pet.json"])throw new Exception("retirement fixture no longer matches previously shipped bytes");
                File.WriteAllText(Path.Combine(docs,"roadmap.md"),"user edited roadmap");
                try{UpdateInstall.ApplyPortable(job,count=>{if(count==4)throw new InvalidOperationException("rollback cleanup fixture");});throw new Exception("cleanup rollback not exercised");}catch(InvalidOperationException error){if(error.Message!="rollback cleanup fixture")throw;}
                if(!File.Exists(retired)||UpdateInstall.Hash(retired)!=RetiredRuntimeFiles.Known["templates/pet.json"]||!ReleaseIdentity.Read(old).Version.IsPreview)throw new Exception("cleanup rollback lost old files");
                string next=Path.Combine(root,"data","updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(next);string nextPackage=Path.Combine(next,Path.GetFileName(package));File.Copy(package,nextPackage);job.WorkDirectory=next;job.PackagePath=nextPackage;
                UpdateInstall.ApplyPortable(job,null);
                if(File.Exists(retired)||File.ReadAllText(Path.Combine(docs,"roadmap.md"))!="user edited roadmap")throw new Exception("retired cleanup lost user modifications");
                ReleaseIdentity.Read(old).Require("0.9.4","0.9.4.0","0.9.4+"+sha);
                Reject(()=>UpdateInstall.Validate(job));
                Console.WriteLine("Release identity tests passed: same-base dev to stable, commit mismatch and changed installation");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
