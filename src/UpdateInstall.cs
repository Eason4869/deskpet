using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dafeiyu
{
    [DataContract]
    public sealed class UpdateJob
    {
        [DataMember] public string InstallationDirectory,WorkDirectory,PackagePath,Mode,InstalledVersion,TargetVersion,Digest,AssetUrl;
        [DataMember] public long Size,ParentStartedUtcTicks;
        [DataMember] public int ParentPid;
        [DataMember] public int ProtocolVersion;
        [DataMember] public string InstalledAssemblyVersion,TargetAssemblyVersion,InstalledBuildId,TargetBuildId;
    }
    [DataContract]
    public sealed class UpdateResult
    {
        [DataMember] public bool Success;
        [DataMember] public string Version,Message;
    }

    public static class UpdateInstall
    {
        public const string ExecutableName="桌面伙伴.exe",WorkerName="DeskPet.Update.exe";
        const string UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{3B63C162-076E-4B2F-BDCC-2572664875A9}_is1";
        static readonly HashSet<string> rootFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase){ExecutableName,ExecutableName+".config",WorkerName,WorkerName+".config","README.md","CHANGELOG.md"};
        static Version VersionOf(string version)
        {
            Version result;if(!Version.TryParse((version??"").TrimStart('v','V'),out result))throw new InvalidOperationException(TextResources.Get("updateInstall.versionOf.text"));
            return new Version(result.Major,result.Minor,Math.Max(0,result.Build),Math.Max(0,result.Revision));
        }
        static string Full(string path){if(String.IsNullOrWhiteSpace(path))throw new InvalidOperationException(TextResources.Get("updateInstall.full.text"));return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);}
        static bool Same(string left,string right){return String.Equals(Full(left),Full(right),StringComparison.OrdinalIgnoreCase);}
        public static bool IsInstalledDirectory(string directory)
        {return GetUninstallerPath(directory)!=null;}
        public static string GetUninstallerPath(string directory)
        {
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
                using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view))using(var key=root.OpenSubKey(UninstallKey,false))
                {
                    string value=key==null?null:key.GetValue("InstallLocation") as string;
                    string command=key==null?null:key.GetValue("UninstallString") as string;
                    if(String.IsNullOrWhiteSpace(value) || String.IsNullOrWhiteSpace(command))continue;
                    try
                    {
                        // A portable copy may reuse a directory left in the uninstall registry.
                        // Only a live Inno uninstaller belonging to this directory selects Setup.
                        string uninstaller=command.Trim().Trim('"');
                        string uninstallDirectory=Path.GetDirectoryName(uninstaller);
                        if(Path.IsPathRooted(uninstaller) && !String.IsNullOrWhiteSpace(uninstallDirectory) && Same(value,directory) && Same(uninstallDirectory,directory) &&
                            Regex.IsMatch(Path.GetFileName(uninstaller),@"^unins[0-9]+\.exe$",RegexOptions.IgnoreCase) &&
                            File.Exists(uninstaller) && File.Exists(Path.ChangeExtension(uninstaller,".dat")))return uninstaller;
                    }
                    catch(ArgumentException){}catch(NotSupportedException){}catch(PathTooLongException){}
                }
            return null;
        }
        public static string Hash(string path)
        {using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        public static void Validate(UpdateJob job)
        {
            if(job==null)throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text"));
            string directory=Full(job.InstallationDirectory),work=Full(job.WorkDirectory),package=Full(job.PackagePath);
            if(Same(directory,Path.GetPathRoot(directory)) || !File.Exists(Path.Combine(directory,ExecutableName)))throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeA"));
            string expected=Path.Combine(directory,"data","updates");
            if(!Same(Path.GetDirectoryName(work),expected) || !Regex.IsMatch(Path.GetFileName(work),"^[a-f0-9]{32}$") || !Same(Path.GetDirectoryName(package),work))throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeB"));
            EnsureNoLinks(directory,work);
            string mode=job.Mode;if(mode!="installed" && mode!="portable")throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeC"));
            if(mode=="installed" && !IsInstalledDirectory(directory))throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeD"));
            if(ReleaseVersion.Parse(job.TargetVersion).CompareTo(ReleaseVersion.Parse(job.InstalledVersion))<=0)throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeE"));
            if(job.ProtocolVersion<0||job.ProtocolVersion>2)throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeF"));
            if(job.ProtocolVersion<2&&(ReleaseVersion.Parse(job.TargetVersion).IsPreview||ReleaseVersion.Parse(job.InstalledVersion).IsPreview))throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeG"));
            if(job.ProtocolVersion==2){
                if(job.TargetAssemblyVersion!=ReleaseVersion.Parse(job.TargetVersion).AssemblyBase.ToString() || !Regex.IsMatch(job.TargetBuildId??"",@"\A"+Regex.Escape(job.TargetVersion)+@"\+[a-f0-9]{40}\z"))throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeH"));
                CheckInstalled(job);
            }
            string version=job.TargetVersion.TrimStart('v','V');
            string name=Path.GetFileName(package);
            Uri uri;string url=ReleaseInfo.RepositoryUrl+"/releases/download/v"+version+"/"+name;
            if(!ReleaseInfo.WindowsUpdateAsset(name,version,mode=="portable") || !Uri.TryCreate(job.AssetUrl,UriKind.Absolute,out uri) || !String.Equals(uri.AbsoluteUri,url,StringComparison.Ordinal) || uri.UserInfo.Length>0)throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeI"));
            if(job.Size<=0 || job.Size>300L*1024*1024 || !Regex.IsMatch(job.Digest??"","^[a-fA-F0-9]{64}$") || !File.Exists(package) || new FileInfo(package).Length!=job.Size || !String.Equals(Hash(package),job.Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Get("updateInstall.validate.text.alternativeJ"));
        }
        public static void CheckInstalled(UpdateJob job)
        {
            string path=Path.Combine(job.InstallationDirectory,ExecutableName);
            if(job.ProtocolVersion<2){if(AssemblyName.GetAssemblyName(path).Version!=VersionOf(job.InstalledVersion))throw new InvalidOperationException(TextResources.Get("updateInstall.checkInstalled.text"));return;}
            var actual=ReleaseIdentity.Read(path);
            if(String.IsNullOrEmpty(job.InstalledBuildId)){
                if(actual.CommitSha!=null||actual.Version.IsPreview||actual.Version.CompareTo(ReleaseVersion.Parse(job.InstalledVersion))!=0||actual.AssemblyVersion.ToString()!=job.InstalledAssemblyVersion)throw new InvalidOperationException(TextResources.Get("updateInstall.checkInstalled.text.alternativeA"));
            }else actual.Require(job.InstalledVersion,job.InstalledAssemblyVersion,job.InstalledBuildId);
            if(ReleaseVersion.Parse(job.TargetVersion).CompareTo(actual.Version)<=0)throw new InvalidOperationException(TextResources.Get("updateInstall.checkInstalled.text.alternativeB"));
        }
        static void CheckTarget(UpdateJob job,string path)
        {
            if(job.ProtocolVersion==2)ReleaseIdentity.Read(path).Require(job.TargetVersion,job.TargetAssemblyVersion,job.TargetBuildId);
            else if(AssemblyName.GetAssemblyName(path).Version!=VersionOf(job.TargetVersion))throw new InvalidOperationException(TextResources.Get("updateInstall.checkTarget.text"));
        }
        public static void CheckWorker(UpdateJob job,string path)
        {
            if(job.ProtocolVersion==2)ReleaseIdentity.Read(path).Require(job.InstalledVersion,job.InstalledAssemblyVersion,job.InstalledBuildId);
        }
        static void EnsureNoLinks(string root,string path)
        {
            string basePath=Full(root),target=Full(path);
            if(!Same(basePath,target) && !target.StartsWith(basePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Get("updateInstall.ensureNoLinks.text"));
            while(true)
            {
                if((File.Exists(target) || Directory.Exists(target)) && (File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException(TextResources.Get("updateInstall.ensureNoLinks.text.alternativeA"));
                if(Same(basePath,target))return;target=Path.GetDirectoryName(target);
            }
        }
        static string Relative(ZipArchiveEntry entry)
        {
            string name=entry.FullName;
            if(!name.StartsWith("DafeiyuDeskPet/",StringComparison.Ordinal) || name.IndexOf('\\')>=0 || ((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidOperationException(TextResources.Get("updateInstall.relative.text"));
            string relative=name.Substring("DafeiyuDeskPet/".Length).TrimEnd('/');
            if(relative.Length==0)return null;
            foreach(string part in relative.Split('/'))
                if(part.Length==0 || part=="." || part==".." || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || part.EndsWith(".") || part.EndsWith(" ") || Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))throw new InvalidOperationException(TextResources.Get("updateInstall.relative.text.alternativeA"));
            if(name.EndsWith("/"))
            {
                if(relative!="docs" && relative!="templates" && relative!="previews" && !relative.StartsWith("docs/",StringComparison.Ordinal) && !relative.StartsWith("templates/",StringComparison.Ordinal) && !relative.StartsWith("previews/",StringComparison.Ordinal))throw new InvalidOperationException(TextResources.Get("updateInstall.relative.text.alternativeB"));
                return null;
            }
            string extension=Path.GetExtension(relative).ToLowerInvariant();
            bool known=rootFiles.Contains(relative) || (relative.StartsWith("docs/",StringComparison.Ordinal) && extension==".md") ||
                (relative.StartsWith("templates/",StringComparison.Ordinal) && (extension==".json" || extension==".svg" || extension==".png" || extension==".md")) ||
                (relative.StartsWith("previews/",StringComparison.Ordinal) && (extension==".png" || extension==".svg" || extension==".jpg" || extension==".webp"));
            if(!known)throw new InvalidOperationException(TextResources.Get("updateInstall.relative.text.alternativeC"));
            return relative.Replace('/',Path.DirectorySeparatorChar);
        }
        public static void ApplyPortable(UpdateJob job,Action<int> afterReplace)
        {
            Validate(job);
            string stage=Path.Combine(job.WorkDirectory,"staging"),backup=Path.Combine(job.WorkDirectory,"backup");
            if(Directory.Exists(stage) || Directory.Exists(backup))throw new InvalidOperationException(TextResources.Get("updateInstall.applyPortable.text"));
            var files=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var stream=new FileStream(job.PackagePath,FileMode.Open,FileAccess.Read,FileShare.Read))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
            {
                long total=0;foreach(var entry in zip.Entries)
                {
                    string relative=Relative(entry);if(relative==null)continue;
                    if(!seen.Add(relative))throw new InvalidOperationException(TextResources.Get("updateInstall.applyPortable.text.alternativeA"));
                    total+=entry.Length;if(total>400L*1024*1024 || files.Count>=500)throw new InvalidOperationException(TextResources.Get("updateInstall.applyPortable.text.alternativeB"));files.Add(relative);
                }
                if(!seen.Contains(ExecutableName) || !seen.Contains(ExecutableName+".config") || !seen.Contains(WorkerName))throw new InvalidOperationException(TextResources.Get("updateInstall.applyPortable.text.alternativeC"));
                Directory.CreateDirectory(stage);
                foreach(var entry in zip.Entries)
                {
                    string relative=Relative(entry);if(relative==null)continue;string destination=Path.Combine(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using(var input=entry.Open())using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {byte[] block=new byte[32768];int read;long count=0;while((read=input.Read(block,0,block.Length))>0){count+=read;if(count>entry.Length)throw new InvalidOperationException(TextResources.Get("updateInstall.applyPortable.text.alternativeD"));output.Write(block,0,read);}if(count!=entry.Length)throw new InvalidOperationException(TextResources.Get("updateInstall.applyPortable.text.alternativeE"));output.Flush(true);}
                }
            }
            CheckTarget(job,Path.Combine(stage,ExecutableName));
            if(job.ProtocolVersion==2){CheckTarget(job,Path.Combine(stage,WorkerName));CheckInstalled(job);}
            var retired=new List<string>();foreach(var entry in RetiredRuntimeFiles.Known){string candidate=Path.Combine(job.InstallationDirectory,entry.Key.Replace('/',Path.DirectorySeparatorChar));if(seen.Contains(entry.Key.Replace('/',Path.DirectorySeparatorChar)))continue;try{EnsureNoLinks(job.InstallationDirectory,candidate);if(File.Exists(candidate)&&Hash(candidate)==entry.Value)retired.Add(entry.Key.Replace('/',Path.DirectorySeparatorChar));}catch(InvalidOperationException){}}
            var applied=new List<string>();var existed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Directory.CreateDirectory(backup);
            try
            {
                foreach(string relative in files)
                {
                    string target=Path.Combine(job.InstallationDirectory,relative),previous=Path.Combine(backup,relative);EnsureNoLinks(job.InstallationDirectory,target);
                    if(File.Exists(target)){Directory.CreateDirectory(Path.GetDirectoryName(previous));File.Copy(target,previous,false);existed.Add(relative);}
                    Directory.CreateDirectory(Path.GetDirectoryName(target));ReplaceFile(Path.Combine(stage,relative),target);applied.Add(relative);
                    if(afterReplace!=null)afterReplace(applied.Count);
                }
                foreach(string relative in retired){
                    string target=Path.Combine(job.InstallationDirectory,relative),previous=Path.Combine(backup,relative);EnsureNoLinks(job.InstallationDirectory,target);
                    string expectedHash=RetiredRuntimeFiles.Known[relative.Replace(Path.DirectorySeparatorChar,'/')];
                    if(!File.Exists(target)||Hash(target)!=expectedHash)continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(previous));File.Copy(target,previous,false);existed.Add(relative);File.Delete(target);applied.Add(relative);
                    if(afterReplace!=null)afterReplace(applied.Count);
                }
            }
            catch
            {
                bool restored=true;for(int i=applied.Count-1;i>=0;i--)
                {
                    string relative=applied[i],target=Path.Combine(job.InstallationDirectory,relative);
                    try{if(existed.Contains(relative))ReplaceFile(Path.Combine(backup,relative),target);else File.Delete(target);}catch(IOException){restored=false;}catch(UnauthorizedAccessException){restored=false;}
                }
                if(!restored)throw new InvalidOperationException(TextResources.Get("updateInstall.applyPortable.text.alternativeF"));
                throw;
            }
        }
        static void ReplaceFile(string source,string target)
        {
            string temporary=target+".deskpet-"+Guid.NewGuid().ToString("N")+".tmp";
            try{File.Copy(source,temporary,false);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        static IList<string> InstalledRuntimeFiles(string root)
        {
            var files=new List<string>();foreach(string name in rootFiles)if(File.Exists(Path.Combine(root,name)))files.Add(name);
            foreach(string path in Directory.GetFiles(root,"unins*"))if(Regex.IsMatch(Path.GetFileName(path),@"\Aunins[0-9]+\.(exe|dat)\z",RegexOptions.IgnoreCase))files.Add(Path.GetFileName(path));
            string docs=Path.Combine(root,"docs");if(Directory.Exists(docs)){EnsureNoLinks(root,docs);foreach(string path in Directory.GetFiles(docs,"*.md"))files.Add("docs"+Path.DirectorySeparatorChar+Path.GetFileName(path));}
            foreach(var retired in RetiredRuntimeFiles.Known){string relative=retired.Key.Replace('/',Path.DirectorySeparatorChar),path=Path.Combine(root,relative);try{EnsureNoLinks(root,path);if(File.Exists(path)&&Hash(path)==retired.Value&&!files.Contains(relative))files.Add(relative);}catch(InvalidOperationException){}}
            return files;
        }
        sealed class InstallationRecord
        {
            internal RegistryView View;
            internal readonly Dictionary<string,object> Values=new Dictionary<string,object>();
            internal readonly Dictionary<string,RegistryValueKind> Kinds=new Dictionary<string,RegistryValueKind>();
        }
        static IList<InstallationRecord> BackupInstallationRecords()
        {
            var records=new List<InstallationRecord>();
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
                using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view))using(var key=root.OpenSubKey(UninstallKey,false)){
                    if(key==null)continue;var record=new InstallationRecord{View=view};
                    foreach(string name in key.GetValueNames()){record.Values[name]=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);record.Kinds[name]=key.GetValueKind(name);}
                    records.Add(record);
                }
            return records;
        }
        static void RestoreInstallationRecords(IList<InstallationRecord> records)
        {
            foreach(var record in records)
                using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,record.View))using(var key=root.CreateSubKey(UninstallKey)){
                    foreach(string name in key.GetValueNames())if(!record.Values.ContainsKey(name))key.DeleteValue(name,false);
                    foreach(var value in record.Values)key.SetValue(value.Key,value.Value,record.Kinds[value.Key]);
                }
        }
        public static void ApplyInstaller(UpdateJob job)
        {
            Validate(job);
            var installationRecords=BackupInstallationRecords();
            string backup=Path.Combine(job.WorkDirectory,"installer-backup");if(Directory.Exists(backup))throw new InvalidOperationException(TextResources.Get("updateInstall.applyInstaller.text"));Directory.CreateDirectory(backup);
            var original=new List<string>();foreach(string relative in InstalledRuntimeFiles(job.InstallationDirectory)){
                string source=Path.Combine(job.InstallationDirectory,relative);try{EnsureNoLinks(job.InstallationDirectory,source);}catch(InvalidOperationException){continue;}
                string target=Path.Combine(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,false);original.Add(relative);
            }
            try{
                using(var locked=new FileStream(job.PackagePath,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    var start=new ProcessStartInfo(job.PackagePath,"/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NORESTARTAPPLICATIONS /NOCLOSEAPPLICATIONS /DIR="+Quote(Full(job.InstallationDirectory))+(job.ProtocolVersion==2?" /EXPECTEDBUILD="+Quote(job.TargetBuildId):""))
                        {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=job.WorkDirectory};
                    using(var installer=Process.Start(start)){installer.WaitForExit();if(installer.ExitCode!=0)throw new InvalidOperationException(TextResources.Get("updateInstall.applyInstaller.text.alternativeA"));}
                }
                CheckTarget(job,Path.Combine(job.InstallationDirectory,ExecutableName));
                if(job.ProtocolVersion==2)CheckTarget(job,Path.Combine(job.InstallationDirectory,WorkerName));
            }catch{
                bool restored=true;foreach(string relative in original){string target=Path.Combine(job.InstallationDirectory,relative);
                    try{EnsureNoLinks(job.InstallationDirectory,target);Directory.CreateDirectory(Path.GetDirectoryName(target));ReplaceFile(Path.Combine(backup,relative),target);}catch(IOException){restored=false;}catch(UnauthorizedAccessException){restored=false;}catch(InvalidOperationException){restored=false;}
                }
                try{RestoreInstallationRecords(installationRecords);}catch(IOException){restored=false;}catch(UnauthorizedAccessException){restored=false;}catch(System.Security.SecurityException){restored=false;}
                if(!restored)throw new InvalidOperationException(TextResources.Get("updateInstall.applyInstaller.text.alternativeB"));
                throw;
            }
        }

        public static string Quote(string value)
        {if(value==null || value.IndexOf('"')>=0 || value.IndexOf('\r')>=0 || value.IndexOf('\n')>=0)throw new InvalidOperationException(TextResources.Get("updateInstall.quote.text"));return "\""+value+"\"";}
        public static T Read<T>(string path)
        {using(var stream=File.OpenRead(path)){if(stream.Length>16384)throw new InvalidOperationException(TextResources.Get("updateInstall.read.text"));return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}}
        public static void Write<T>(string path,T value)
        {string temporary=path+".tmp";using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);stream.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
    }
}
