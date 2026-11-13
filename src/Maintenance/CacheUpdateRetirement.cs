using System;
using System.IO;
using System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace Dafeiyu.Maintenance
{
    // Only the successfully completed, validated updater writes this extra proof. Merely
    // finding an old job/ready/result is insufficient to retire a legacy worker's rollback.
    public static class CacheUpdateRetirement
    {
        [DataContract] sealed class Job
        {
            [DataMember] public string InstallationDirectory {get;set;}
            [DataMember] public string WorkDirectory {get;set;}
            [DataMember] public string TargetVersion {get;set;}
            [DataMember] public string PackagePath {get;set;}
            [DataMember] public string Digest {get;set;}
            [DataMember] public long Size {get;set;}
        }
        [DataContract] sealed class Result
        {
            [DataMember] public bool Success {get;set;}
            [DataMember] public string Version {get;set;}
        }
        [DataContract] sealed class Proof
        {
            [DataMember] public string JobHash;
            [DataMember] public string ResultHash;
            [DataMember] public string Version {get;set;}
        }
        [DataContract] sealed class Retired
        {
            [DataMember] public string Identity;
            [DataMember] public string Completion;
        }
        internal static bool IsRetired(string root,string path)
        {
            try{
                string marker=ProofPath(root,path)+".retired";CachePaths.Check(marker);if(!File.Exists(marker))return false;
                var retired=Read<Retired>(marker);var proof=Read<Proof>(ProofPath(root,path));
                string executable=Path.Combine(Path.GetDirectoryName(root),"桌面伙伴.exe");CachePaths.Check(executable);
                if(proof==null)return false;
                foreach(string name in new[]{"job.json","result.json"}){string file=Path.Combine(path,name);CachePaths.Check(file);if(File.Exists(file)&&Hash(file)!=(name=="job.json"?proof.JobHash:proof.ResultHash))return false;}
                return retired!=null&&retired.Identity==CachePaths.Identity(path)&&retired.Completion==Hash(ProofPath(root,path))&&ReleaseIdentity.Read(executable).Version.CompareTo(ReleaseVersion.Parse(proof.Version))>=0;
            }catch(Exception e){if(CachePaths.StorageError(e)||e is SerializationException||e is InvalidOperationException||e is ArgumentException||e is BadImageFormatException)return false;throw;}
        }
        // Written under the resource's exclusive lease before touching its inventory.
        // It permits retry after job/result were removed, without adopting legacy jobs.
        internal static void BeginRetirement(string root,string path)
        {
            if(IsRetired(root,path))return;if(!IsCompleted(root,path))return;
            string marker=ProofPath(root,path)+".retired",temporary=marker+".tmp";CachePaths.Check(marker);CachePaths.Check(temporary);
            using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))new DataContractJsonSerializer(typeof(Retired)).WriteObject(stream,new Retired{Identity=CachePaths.Identity(path),Completion=Hash(ProofPath(root,path))});
            if(File.Exists(marker))File.Replace(temporary,marker,null);else File.Move(temporary,marker);
        }
        internal static string LatestPackage(string root,CancellationToken cancel)
        {
            string updates=Path.Combine(root,"updates");CachePaths.Check(updates);if(!Directory.Exists(updates))return null;
            string latest=null;ReleaseVersion version=null;DateTime written=DateTime.MinValue;
            foreach(string path in Directory.EnumerateDirectories(updates))try{
                cancel.ThrowIfCancellationRequested();CachePaths.Check(path);
                if(CachePaths.Category(root,path)!="updates"||!CacheResourceLease.Registered(root,path)||!IsCompleted(root,path))continue;
                // Shared use protects validation; active workers also remain undeletable.
                using(CacheResourceLease.ProtectExisting(root,path)){
                    var job=Read<Job>(Path.Combine(path,"job.json"));if(String.IsNullOrEmpty(job.PackagePath)||String.IsNullOrEmpty(job.Digest)||job.Size<=0)continue;
                    string package=CachePaths.Normalize(job.PackagePath);CachePaths.Check(package);
                    if(!String.Equals(Path.GetDirectoryName(package),path,CachePaths.Comparison)||!File.Exists(package)||new FileInfo(package).Length!=job.Size||HashHex(package)!=job.Digest)continue;
                    var candidate=ReleaseVersion.Parse(job.TargetVersion);DateTime stamp=File.GetLastWriteTimeUtc(ProofPath(root,path));
                    if(version==null||candidate.CompareTo(version)>0||candidate.CompareTo(version)==0&&(stamp>written||stamp==written&&String.CompareOrdinal(package,latest)>0)){latest=package;version=candidate;written=stamp;}
                }
            }catch(Exception e){if(!CachePaths.StorageError(e)&&!(e is SerializationException)&&!(e is ArgumentException)&&!(e is InvalidOperationException))throw;}
            return latest;
        }
        public static void RecordCompleted(string dataRoot,string resource,string version)
        {
            string root=CachePaths.Normalize(dataRoot),path=CachePaths.Normalize(resource);
            if(!CacheResourceLease.Registered(root,path)||CachePaths.Category(root,path)!="updates")return;
            var proof=new Proof{JobHash=Hash(Path.Combine(path,"job.json")),ResultHash=Hash(Path.Combine(path,"result.json")),Version=version};
            if(!Matches(root,path,proof,true))throw new IOException("Update completion identity does not match.");
            string destination=ProofPath(root,path),temporary=destination+".tmp";CachePaths.Check(destination);CachePaths.Check(temporary);
            using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(Proof)).WriteObject(stream,proof);stream.Flush(true);}
            if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
        }
        internal static bool IsCompleted(string root,string path)
        {
            try{string proof=ProofPath(root,path);CachePaths.Check(proof);return File.Exists(proof)&&Matches(root,path,Read<Proof>(proof),false);}
            catch(Exception e){if(CachePaths.StorageError(e)||e is SerializationException||e is InvalidOperationException||e is ArgumentException||e is BadImageFormatException)return false;throw;}
        }
        static bool Matches(string root,string path,Proof proof,bool exact)
        {
            if(proof==null||String.IsNullOrWhiteSpace(proof.Version)||String.IsNullOrWhiteSpace(proof.JobHash)||String.IsNullOrWhiteSpace(proof.ResultHash))return false;
            var job=Read<Job>(Path.Combine(path,"job.json"));var result=Read<Result>(Path.Combine(path,"result.json"));
            if(job==null||result==null||String.IsNullOrWhiteSpace(job.InstallationDirectory)||String.IsNullOrWhiteSpace(job.WorkDirectory)||String.IsNullOrWhiteSpace(job.TargetVersion)||String.IsNullOrWhiteSpace(result.Version))return false;
            string install=Path.GetDirectoryName(root);if(!String.Equals(Path.GetFileName(root),"data",CachePaths.Comparison)||!String.Equals(job.InstallationDirectory,install,CachePaths.Comparison)||!String.Equals(job.WorkDirectory,path,CachePaths.Comparison)||job.TargetVersion!=proof.Version||!result.Success||result.Version!=proof.Version)return false;
            string executable=Path.Combine(install,"桌面伙伴.exe");CachePaths.Check(executable);int version=ReleaseIdentity.Read(executable).Version.CompareTo(ReleaseVersion.Parse(proof.Version));
            return (exact?version==0:version>=0)&&proof.JobHash==Hash(Path.Combine(path,"job.json"))&&proof.ResultHash==Hash(Path.Combine(path,"result.json"));
        }
        static string ProofPath(string root,string path){return CacheResourceLease.Key(Path.Combine(root,"cache-maintenance"),path)+".completed";}
        static T Read<T>(string path){CachePaths.Check(path);using(var stream=File.OpenRead(path)){if(stream.Length>16384)throw new IOException("Oversized update record.");return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}}
        static string HashHex(string path){CachePaths.Check(path);using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static string Hash(string path){CachePaths.Check(path);using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream));}
    }
}
