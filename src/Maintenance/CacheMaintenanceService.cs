using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Dafeiyu.Maintenance
{
    public sealed class CacheCandidate
    {
        public string DirectoryPath {get;private set;}
        public string Category {get;private set;}
        public long Bytes {get;private set;}
        internal readonly string Fingerprint;
        internal string Retirement;
        internal bool KeepRoot,AfterUpdate;
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
        readonly Func<string,string,CancellationToken,bool> verifyReplacement;
        public CacheMaintenanceService(string dataRoot):this(dataRoot,null){}
        internal CacheMaintenanceService(string dataRoot,Func<string,string,CancellationToken,bool> verifyReplacement){root=CachePaths.Normalize(dataRoot);this.verifyReplacement=verifyReplacement;}
        public Task<CacheScan> ScanAsync(){return Task.Run(()=>Scan(DateTime.UtcNow));}
        public Task<CacheCleanResult> CleanAsync(CacheScan scan){return Task.Run(()=>Clean(scan,DateTime.UtcNow));}
        public CacheScan Scan(DateTime utcNow)
        {return Scan(utcNow,false,CancellationToken.None);}
        internal CacheScan ScanPackHistory(DateTime utcNow,CancellationToken cancel)
        {return Scan(utcNow,true,cancel);}
        internal CacheScan ScanAfterUpdate(DateTime utcNow,CancellationToken cancel)
        {return Scan(utcNow,false,cancel,true);}
        CacheScan Scan(DateTime utcNow,bool historyOnly,CancellationToken cancel,bool afterUpdate=false)
        {
            var items=new List<CacheCandidate>();var result=new CacheScan(root,items);
            try{
                cancel.ThrowIfCancellationRequested();CachePaths.Check(root);string retained=historyOnly?null:CacheUpdateRetirement.LatestPackage(root,cancel);
                foreach(string path in Resources(historyOnly))try{
                    cancel.ThrowIfCancellationRequested();if(historyOnly&&CachePaths.Category(root,path)!="pack-versions")continue;
                    if(!CacheResourceLease.Registered(root,path)){result.ProtectedItems++;continue;}
                    using(AcquireCandidate(path)){
                        var candidate=Read(path,utcNow,cancel,retained,afterUpdate);if(candidate==null)result.ProtectedItems++;else items.Add(candidate);
                    }
                }catch(Exception e){if(!CachePaths.StorageError(e))throw;result.ProtectedItems++;}
            }catch(Exception e){if(!CachePaths.StorageError(e))throw;result.FailedItems++;}
            return result;
        }
        IEnumerable<string> Resources(bool historyOnly)
        {
            if(!historyOnly){string updates=Path.Combine(root,"updates");CachePaths.Check(updates);
            if(Directory.Exists(updates))foreach(string path in Directory.EnumerateDirectories(updates))if(CachePaths.Category(root,path)!=null)yield return path;}
            string official=Path.Combine(root,"official-companions");CachePaths.Check(official);
            foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}){
                string parent=Path.Combine(official,id);CachePaths.Check(parent);
                if(Directory.Exists(parent))foreach(string path in Directory.EnumerateDirectories(parent))if(CachePaths.Category(root,path)!=null||Path.GetFileName(path).StartsWith("v-",StringComparison.Ordinal))yield return path;
            }
        }
        public CacheCleanResult Clean(CacheScan scan,DateTime utcNow)
        {return Clean(scan,utcNow,CancellationToken.None);}
        internal CacheCleanResult Clean(CacheScan scan,DateTime utcNow,CancellationToken cancel)
        {
            if(scan==null||!String.Equals(root,scan.Root,CachePaths.Comparison))throw new ArgumentException("Cache scan belongs to another data directory.");
            var result=new CacheCleanResult();
            int remaining=scan.Candidates.Count;string retained=CacheUpdateRetirement.LatestPackage(root,cancel);
            foreach(var candidate in scan.Candidates){
                if(cancel.IsCancellationRequested){result.SkippedItems+=remaining;break;}remaining--;
                try{
                    CachePaths.Check(candidate.DirectoryPath);
                    using(AcquireCandidate(candidate.DirectoryPath)){
                        var current=Read(candidate.DirectoryPath,utcNow,cancel,retained,candidate.AfterUpdate);
                        if(current==null||current.Fingerprint!=candidate.Fingerprint){result.SkippedItems++;continue;}
                        // No recursive delete: every file and directory is independently checked.
                        // Cooperative download/worker users cannot enter until this lease closes.
                        using(var pins=CacheDirectoryPins.Open(candidate.DirectoryPath)){
                            current=Read(candidate.DirectoryPath,utcNow,cancel,retained,candidate.AfterUpdate);
                            if(current==null||current.Fingerprint!=candidate.Fingerprint){result.SkippedItems++;continue;}
                            if(current.Category=="updates")CacheUpdateRetirement.BeginRetirement(root,current.DirectoryPath);
                            // Delete only the validated inventory. A file appearing after this
                            // snapshot is never swept up by a fresh recursive enumeration.
                            foreach(var file in current.Files){CachePaths.Check(file.Path);pins.DeleteFile(file,action=>CommitDelete(current,action));result.FreedBytes+=file.Bytes;}
                            foreach(string directory in current.Directories.OrderByDescending(x=>x.Length)){CachePaths.Check(directory);CommitDelete(current,()=>pins.DeleteDirectory(directory));}
                            if(!current.KeepRoot)CommitDelete(current,()=>pins.DeleteDirectory(candidate.DirectoryPath));result.DeletedItems++;
                        }
                    }
                }catch(OperationCanceledException){result.SkippedItems+=remaining+1;break;}
                catch(Exception e){if(!CachePaths.StorageError(e))throw;if(e is UnauthorizedAccessException)result.FailedItems++;else result.SkippedItems++;}
            }
            return result;
        }
        IDisposable AcquireCandidate(string path)
        {
            using(OfficialCompanionMaintenance.LockCandidate(root,path)){
                if(CachePaths.Category(root,path)=="pack-versions"&&OfficialCompanionMaintenance.RetirementToken(path)==null)throw new IOException("Current or uncertain companion is protected.");
                return CacheResourceLease.TryExclusive(root,path);
            }
        }
        void CommitDelete(CacheCandidate candidate,Action action)
        {
            using(OfficialCompanionMaintenance.LockCandidate(root,candidate.DirectoryPath)){
                if(candidate.Category=="pack-versions"&&candidate.Retirement!=OfficialCompanionMaintenance.RetirementToken(candidate.DirectoryPath))throw new IOException("Companion lifecycle changed before deletion.");
                action();
            }
        }
        CacheCandidate Read(string path,DateTime now,CancellationToken cancel,string retained,bool afterUpdate)
        {
            CachePaths.Check(path);string category=CachePaths.Category(root,path);
            if(category==null||!Directory.Exists(path)||!CacheResourceLease.Registered(root,path))return null;
            string proof=null;if(category=="pack-versions"){proof=OfficialCompanionMaintenance.RetirementProof(root,path,now,cancel,verifyReplacement);if(proof==null)return null;}
            // A completion proof additionally binds the job, successful receipt and installed
            // target version. Unknown old jobs and needed/failed rollback remain protected.
            bool completed=category=="updates"&&(CacheUpdateRetirement.IsCompleted(root,path)||CacheUpdateRetirement.IsRetired(root,path));
            if(category=="downloads"&&(File.Exists(Path.Combine(Path.GetDirectoryName(path),"version-transaction.json"))||File.Exists(Path.Combine(Path.GetDirectoryName(path),"bundle-transaction.json"))))return null;
            if(category=="updates"&&!completed)foreach(string marker in new[]{"job.json","ready","staging","backup","installer-backup"})if(File.Exists(Path.Combine(path,marker))||Directory.Exists(Path.Combine(path,marker)))return null;
            var signature=new StringBuilder(proof??"");long bytes=0;var files=new List<CacheFile>();var directories=new List<string>();if(!Inventory(path,path,category=="pack-versions"||afterUpdate?DateTime.MaxValue:now.AddDays(-7),signature,ref bytes,files,directories,cancel))return null;
            bool keep=retained!=null&&String.Equals(Path.GetDirectoryName(retained),path,CachePaths.Comparison);
            if(keep){
                files.RemoveAll(f=>String.Equals(f.Path,retained,CachePaths.Comparison)||String.Equals(f.Path,Path.Combine(path,"job.json"),CachePaths.Comparison)||String.Equals(f.Path,Path.Combine(path,"result.json"),CachePaths.Comparison));
                bytes=files.Sum(f=>f.Bytes);if(files.Count==0&&directories.Count==0)return null;
            }
            signature.Append("|retained|").Append(keep?retained:"");
            using(var hash=SHA256.Create())return new CacheCandidate(path,category,bytes,Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))),files,directories){Retirement=proof,KeepRoot=keep,AfterUpdate=afterUpdate};
        }
        static bool Inventory(string root,string path,DateTime cutoff,StringBuilder signature,ref long bytes,List<CacheFile> files,List<string> directories,CancellationToken cancel)
        {
            CachePaths.Check(path);var directory=new DirectoryInfo(path);
            if(directory.LastWriteTimeUtc>cutoff)return false;
            signature.Append("D|").Append(path.Substring(root.Length)).Append('|').Append(CachePaths.Identity(path)).Append('|').Append(directory.CreationTimeUtc.Ticks).Append('|').Append(directory.LastWriteTimeUtc.Ticks).Append('\n');
            foreach(string entry in Directory.GetFileSystemEntries(path).OrderBy(x=>x,StringComparer.Ordinal)){
                cancel.ThrowIfCancellationRequested();CachePaths.Check(entry);
                if(Directory.Exists(entry)){directories.Add(entry);if(!Inventory(root,entry,cutoff,signature,ref bytes,files,directories,cancel))return false;}
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
