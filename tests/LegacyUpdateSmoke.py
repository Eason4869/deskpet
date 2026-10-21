"""Apply a real release ZIP with an existing update engine in an isolated directory."""
from pathlib import Path
import argparse
import os
import subprocess
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--worker', type=Path, required=True)
parser.add_argument('--package', type=Path, required=True)
parser.add_argument('--work-directory', type=Path, help='Isolated test root; avoids nested checkout MAX_PATH limits')
options = parser.parse_args()
work = options.work_directory.resolve() if options.work_directory else Path(__file__).resolve().parents[1] / 'work'
root = work / ('update-' + uuid.uuid4().hex[:12])
root.mkdir(parents=True)
source = root / 'ApplyRelease.cs'
source.write_text(r'''
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
[assembly:System.Reflection.AssemblyVersion("0.0.1.0")]
[assembly:System.Reflection.AssemblyInformationalVersion("0.0.1.0")]
class ApplyRelease {
    static void Set(object job,string name,object value) { job.GetType().GetField(name).SetValue(job,value); }
    static string Hash(string path) { using(var f=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
    static int Main(string[] args) {
        try {
            var assembly=Assembly.LoadFile(Path.GetFullPath(args[0]));
            var engine=assembly.GetType("Dafeiyu.UpdateInstall",true);
            var job=Activator.CreateInstance(assembly.GetType("Dafeiyu.UpdateJob",true));
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"owned install"),data=Path.Combine(directory,"data");
            string work=Path.Combine(data,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
            string name=Path.GetFileName(args[1]),version=name.Substring("DeskPet-Windows-".Length);version=version.Substring(0,version.Length-4);
            string package=Path.Combine(work,name);File.Copy(args[1],package);
            string exe=Path.Combine(directory,"桌面伙伴.exe");File.Copy(version.Contains("-dev")?Assembly.GetExecutingAssembly().Location:args[0],exe);
            string oldHash=Hash(exe);
            foreach(string relative in new[]{"settings.json","history/keep.history","skins/personal/pet.json"}) {
                string path=Path.Combine(data,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"private sentinel "+relative);
            }
            Set(job,"InstallationDirectory",directory);Set(job,"WorkDirectory",work);Set(job,"PackagePath",package);Set(job,"Mode","portable");
            Set(job,"InstalledVersion","0.0.1");Set(job,"TargetVersion",version);Set(job,"Size",new FileInfo(package).Length);Set(job,"Digest",Hash(package));
            Set(job,"AssetUrl","https://github.com/Eason4869/deskpet/releases/download/v"+version+"/"+name);
            if(version.Contains("-dev")) {
                Set(job,"ProtocolVersion",2);Set(job,"InstalledAssemblyVersion","0.0.1.0");
                string target=Path.Combine(work,"identity.exe");using(var zip=new System.IO.Compression.ZipArchive(File.OpenRead(package),System.IO.Compression.ZipArchiveMode.Read))using(var input=zip.GetEntry("DafeiyuDeskPet/桌面伙伴.exe").Open())using(var output=File.Create(target))input.CopyTo(output);
                Set(job,"TargetAssemblyVersion",AssemblyName.GetAssemblyName(target).Version.ToString());Set(job,"TargetBuildId",System.Diagnostics.FileVersionInfo.GetVersionInfo(target).ProductVersion);
            }
            engine.GetMethod("ApplyPortable").Invoke(null,new object[]{job,null});
            if(AssemblyName.GetAssemblyName(exe).Version.ToString()!=version.Split('-')[0]+".0")throw new Exception("Installed version differs");
            if(Hash(Path.Combine(work,"backup","桌面伙伴.exe"))!=oldHash)throw new Exception("Original binary backup differs");
            foreach(string relative in new[]{"settings.json","history/keep.history","skins/personal/pet.json"})
                if(File.ReadAllText(Path.Combine(data,relative))!="private sentinel "+relative)throw new Exception("Personal data changed");
            Console.WriteLine("PASS real update engine "+assembly.GetName().Version+" applied release "+version+"; backup, settings, history and skins preserved");return 0;
        } catch(Exception e) {Console.WriteLine("FAIL "+(e.InnerException??e).Message);return 1;}
    }
}
''', encoding='utf-8-sig')
compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
runner = root / 'ApplyRelease.exe'
subprocess.run([str(compiler), '/nologo', '/codepage:65001', '/reference:System.IO.Compression.dll', '/out:' + str(runner), str(source)], check=True, timeout=30)
subprocess.run([str(runner), str(options.worker.resolve()), str(options.package.resolve())], check=True, timeout=90)
