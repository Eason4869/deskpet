using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Dafeiyu.Maintenance
{
    public sealed class CacheCandidate
    {
        public string DirectoryPath {get;private set;}
        public string Category {get;private set;}
        public long Bytes {get;private set;}
        internal readonly string Fingerprint;
        internal readonly List<CacheFile> Files;
        internal readonly List<string> Directories;
        internal CacheCandidate(string path,string category,long bytes,string fingerprint,List<CacheFile> files,List<string> directories){DirectoryPath=path;Category=category;Bytes=bytes;Fingerprint=fingerprint;Files=files;Directories=directories;}
    }
    internal sealed class CacheFile
    {
        internal string Path,Hash;internal long Bytes;
    }
    public sealed class CacheScan
    {
        public ReadOnlyCollection<CacheCandidate> Candidates {get;private set;}
        public long TotalBytes {get{return Candidates.Sum(x=>x.Bytes);}}
        public int ProtectedItems {get;internal set;}
        public int FailedItems {get;internal set;}
        internal readonly string Root;
        internal CacheScan(string root,List<CacheCandidate> items){Root=root;Candidates=items.AsReadOnly();}
    }
    public sealed class CacheCleanResult
    {
        public long FreedBytes {get;internal set;}
        public int DeletedItems {get;internal set;}
        public int SkippedItems {get;internal set;}
        public int FailedItems {get;internal set;}
    }
    public sealed class CacheMaintenanceService
    {
        readonly string root;
        public CacheMaintenanceService(string dataRoot){root=CachePaths.Normalize(dataRoot);}
        public Task<CacheScan> ScanAsync(){return Task.Run(()=>Scan(DateTime.UtcNow));}
        public Task<CacheCleanResult> CleanAsync(CacheScan scan){return Task.Run(()=>Clean(scan,DateTime.UtcNow));}
        public CacheScan Scan(DateTime utcNow)
        {
            var items=new List<CacheCandidate>();var result=new CacheScan(root,items);
            try{
                CachePaths.Check(root);
                foreach(string path in Resources())try{
                    if(!CacheResourceLease.Registered(root,path)){result.ProtectedItems++;continue;}
                    using(OfficialCompanionMaintenance.LockCandidate(root,path))using(CacheResourceLease.TryExclusive(root,path)){
                        var candidate=Read(path,utcNow);if(candidate==null)result.ProtectedItems++;else items.Add(candidate);
                    }
                }catch(Exception e){if(!CachePaths.StorageError(e))throw;result.ProtectedItems++;}
            }catch(Exception e){if(!CachePaths.StorageError(e))throw;result.FailedItems++;}
            return result;
        }
        IEnumerable<string> Resources()
        {
            string updates=Path.Combine(root,"updates");CachePaths.Check(updates);
            if(Directory.Exists(updates))foreach(string path in Directory.GetDirectories(updates))if(CachePaths.Category(root,path)!=null)yield return path;
            string official=Path.Combine(root,"official-companions");CachePaths.Check(official);
            foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}){
                string parent=Path.Combine(official,id);CachePaths.Check(parent);
                if(Directory.Exists(parent))foreach(string path in Directory.GetDirectories(parent))if(CachePaths.Category(root,path)!=null||Path.GetFileName(path).StartsWith("v-",StringComparison.Ordinal))yield return path;
            }
        }
        public CacheCleanResult Clean(CacheScan scan,DateTime utcNow)
        {
            if(scan==null||!String.Equals(root,scan.Root,CachePaths.Comparison))throw new ArgumentException("Cache scan belongs to another data directory.");
            var result=new CacheCleanResult();
            foreach(var candidate in scan.Candidates){
                try{
                    CachePaths.Check(candidate.DirectoryPath);
                    using(OfficialCompanionMaintenance.LockCandidate(root,candidate.DirectoryPath))using(CacheResourceLease.TryExclusive(root,candidate.DirectoryPath)){
                        var current=Read(candidate.DirectoryPath,utcNow);
                        if(current==null||current.Fingerprint!=candidate.Fingerprint){result.SkippedItems++;continue;}
                        // No recursive delete: every file and directory is independently checked.
                        // Cooperative download/worker users cannot enter until this lease closes.
                        using(var pins=CacheDirectoryPins.Open(candidate.DirectoryPath)){
                            current=Read(candidate.DirectoryPath,utcNow);
                            if(current==null||current.Fingerprint!=candidate.Fingerprint){result.SkippedItems++;continue;}
                            // Delete only the validated inventory. A file appearing after this
                            // snapshot is never swept up by a fresh recursive enumeration.
                            foreach(var file in current.Files){CachePaths.Check(file.Path);pins.DeleteFile(file);result.FreedBytes+=file.Bytes;}
                            foreach(string directory in current.Directories.OrderByDescending(x=>x.Length)){CachePaths.Check(directory);pins.DeleteDirectory(directory);}
                            pins.DeleteDirectory(candidate.DirectoryPath);result.DeletedItems++;
                        }
                    }
                }catch(Exception e){if(!CachePaths.StorageError(e))throw;if(e is UnauthorizedAccessException)result.FailedItems++;else result.SkippedItems++;}
            }
            return result;
        }
        CacheCandidate Read(string path,DateTime now)
        {
            CachePaths.Check(path);string category=CachePaths.Category(root,path);
            if(category==null||!Directory.Exists(path)||!CacheResourceLease.Registered(root,path))return null;
            string proof=null;if(category=="pack-versions"){proof=OfficialCompanionMaintenance.RetirementProof(root,path,now);if(proof==null)return null;}
            // A completion proof additionally binds the job, successful receipt and installed
            // target version. Unknown old jobs and needed/failed rollback remain protected.
            if(category=="updates"&&!CacheUpdateRetirement.IsCompleted(root,path))foreach(string marker in new[]{"job.json","ready","staging","backup","installer-backup"})if(File.Exists(Path.Combine(path,marker))||Directory.Exists(Path.Combine(path,marker)))return null;
            var signature=new StringBuilder(proof??"");long bytes=0;var files=new List<CacheFile>();var directories=new List<string>();if(!Inventory(path,path,now.AddDays(-7),signature,ref bytes,files,directories))return null;
            using(var hash=SHA256.Create())return new CacheCandidate(path,category,bytes,Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))),files,directories);
        }
        static bool Inventory(string root,string path,DateTime cutoff,StringBuilder signature,ref long bytes,List<CacheFile> files,List<string> directories)
        {
            CachePaths.Check(path);var directory=new DirectoryInfo(path);
            if(directory.LastWriteTimeUtc>cutoff)return false;
            signature.Append("D|").Append(path.Substring(root.Length)).Append('|').Append(CachePaths.Identity(path)).Append('|').Append(directory.CreationTimeUtc.Ticks).Append('|').Append(directory.LastWriteTimeUtc.Ticks).Append('\n');
            foreach(string entry in Directory.GetFileSystemEntries(path).OrderBy(x=>x,StringComparer.Ordinal)){
                CachePaths.Check(entry);
                if(Directory.Exists(entry)){directories.Add(entry);if(!Inventory(root,entry,cutoff,signature,ref bytes,files,directories))return false;}
                else{
                    var file=new FileInfo(entry);if(file.LastWriteTimeUtc>cutoff)return false;
                    using(var stream=new FileStream(entry,FileMode.Open,FileAccess.Read,FileShare.Read))using(var hash=SHA256.Create()){
                        string digest=Convert.ToBase64String(hash.ComputeHash(stream));files.Add(new CacheFile{Path=entry,Hash=digest,Bytes=stream.Length});
                        bytes=checked(bytes+stream.Length);signature.Append("F|").Append(entry.Substring(root.Length)).Append('|').Append(CachePaths.Identity(entry)).Append('|').Append(file.CreationTimeUtc.Ticks).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('|').Append(stream.Length).Append('|').Append(digest).Append('\n');
                    }
                }
            }
            return true;
        }
    }
}
