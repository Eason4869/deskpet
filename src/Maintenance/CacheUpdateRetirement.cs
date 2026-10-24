using System;
using System.IO;
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
            var job=Read<Job>(Path.Combine(path,"job.json"));var result=Read<Result>(Path.Combine(path,"result.json"));
            string install=Path.GetDirectoryName(root);if(!String.Equals(Path.GetFileName(root),"data",CachePaths.Comparison)||!String.Equals(job.InstallationDirectory,install,CachePaths.Comparison)||!String.Equals(job.WorkDirectory,path,CachePaths.Comparison)||job.TargetVersion!=proof.Version||!result.Success||result.Version!=proof.Version)return false;
            string executable=Path.Combine(install,"桌面伙伴.exe");CachePaths.Check(executable);int version=ReleaseIdentity.Read(executable).Version.CompareTo(ReleaseVersion.Parse(proof.Version));
            return (exact?version==0:version>=0)&&proof.JobHash==Hash(Path.Combine(path,"job.json"))&&proof.ResultHash==Hash(Path.Combine(path,"result.json"));
        }
        static string ProofPath(string root,string path){return CacheResourceLease.Key(Path.Combine(root,"cache-maintenance"),path)+".completed";}
        static T Read<T>(string path){CachePaths.Check(path);using(var stream=File.OpenRead(path)){if(stream.Length>16384)throw new IOException("Oversized update record.");return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}}
        static string Hash(string path){CachePaths.Check(path);using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream));}
    }
}
