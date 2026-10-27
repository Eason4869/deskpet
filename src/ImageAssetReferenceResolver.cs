using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class ImagePurgeResult
    {
        public readonly List<string> CleanupPendingIds=new List<string>();
        public readonly List<string> DeletedIds=new List<string>();
        public readonly Dictionary<string,string> Blocked=new Dictionary<string,string>(StringComparer.Ordinal);
    }
    public sealed class ImageReferenceCleanupPreview
    {
        public IList<GeneratedImage> Candidates {get;private set;}
        public int Count {get{return Candidates.Count;}}
        public long TotalBytes {get;private set;}
        internal ImageReferenceCleanupPreview(IEnumerable<GeneratedImage> candidates)
        {var copies=candidates.Select(i=>i.Copy()).ToList();Candidates=copies.AsReadOnly();TotalBytes=copies.Sum(i=>i.Bytes);}
    }
    public sealed class ImageAssetReferenceResolver
    {
        readonly string root;
        readonly GeneratedImageStore store;
        readonly ImageGenerationTaskManager tasks;
        public ImageAssetReferenceResolver(string dataRoot,GeneratedImageStore store,ImageGenerationTaskManager tasks)
        {
            root=Path.GetFullPath(dataRoot);
            this.store=store;
            this.tasks=tasks;
        }
        internal Dictionary<string,List<string>> TaskReferenceKeys()
        {
            var keys=new Dictionary<string,List<string>>(StringComparer.Ordinal);
            if(tasks!=null)foreach(var task in tasks.List())foreach(string id in task.ReferenceImageIds??new List<string>())
            {List<string> owners;if(!keys.TryGetValue(id,out owners)){owners=new List<string>();keys[id]=owners;}owners.Add("job:"+task.Id);}
            return keys;
        }
        internal HashSet<string> Scan(CancellationToken token,ImageAssetUsageRegistry usage)
        {
            var assets=store.AssetSnapshot();
            var jobs=assets.Where(i=>!String.IsNullOrEmpty(i.ImageJobId)).GroupBy(i=>i.ImageJobId).ToDictionary(g=>g.Key,g=>g.Select(i=>i.Id).ToArray(),StringComparer.Ordinal);
            var taskMap=tasks==null?new Dictionary<string,ImageGenerationTaskSnapshot>():tasks.List().ToDictionary(t=>t.Id,StringComparer.Ordinal);
            var result=new HashSet<string>(StringComparer.Ordinal);
            foreach(var asset in assets)if(!String.IsNullOrEmpty(asset.ReferenceImageId))result.Add(asset.ReferenceImageId);
            foreach(var task in taskMap.Values)if(!task.IsTerminal||usage.IsInUse("job:"+task.Id))foreach(string id in task.ReferenceImageIds??new List<string>())result.Add(id);
            foreach(var directory in HistoryDirectories())
            {
                token.ThrowIfCancellationRequested();
                Check(directory);
                foreach(string path in Directory.EnumerateFiles(directory,"*.history"))
                {
                    token.ThrowIfCancellationRequested();
                    foreach(var message in HistoryStore.InspectFileStrict(path))
                    {
                        foreach(string key in ImageAssetUsageRegistry.ReferenceKeys(new[]
                        {
                            message
                        }
                        ))
                        {
                            if(!key.StartsWith("job:",StringComparison.Ordinal))
                            {
                                result.Add(key);
                                continue;
                            }
                            string job=key.Substring(4);
                            string[] linked;
                            if(jobs.TryGetValue(job,out linked))foreach(string id in linked)result.Add(id);
                            ImageGenerationTaskSnapshot record;
                            if(taskMap.TryGetValue(job,out record))
                            {
                                if(!String.IsNullOrEmpty(record.ResultImageId))result.Add(record.ResultImageId);
                                foreach(string id in record.ReferenceImageIds??new List<string>())result.Add(id);
                            }
                        }
                    }
                }
            }
            return result;
        }
        IEnumerable<string> HistoryDirectories()
        {
            // Enumerate known parent directories rather than Directory.Exists: access errors
            // must not turn an inaccessible archived history into an empty scan.
            foreach(string child in Children(root))
            {
                string name=Path.GetFileName(child);
                if(String.Equals(name,"history",StringComparison.OrdinalIgnoreCase))yield return child;
                else if(String.Equals(name,"conversations",StringComparison.OrdinalIgnoreCase))
                {
                    foreach(string conversation in Children(child))
                    foreach(string history in NamedChildren(conversation,"history"))yield return history;
                }
                else if(String.Equals(name,"projects",StringComparison.OrdinalIgnoreCase))
                {
                    foreach(string project in Children(child))
                    foreach(string chats in NamedChildren(project,"chats"))
                    foreach(string chat in Children(chats))
                    foreach(string history in NamedChildren(chat,"history"))yield return history;
                }
            }
        }
        static IEnumerable<string> NamedChildren(string path,string name)
        {
            return Children(path).Where(child=>String.Equals(Path.GetFileName(child),name,StringComparison.OrdinalIgnoreCase));
        }
        static IEnumerable<string> Children(string path)
        {
            Check(path);
            var children=Directory.GetDirectories(path);
            foreach(string child in children)Check(child);
            return children;
        }
        static void Check(string path)
        {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException(TextResources.Get("imageAssetReferenceResolver.check.text"));
        }
    }
    public sealed class ImageAssetManagementService
    {
        internal Action AfterReservations;
        internal Action BeforeAssetReservations;
        readonly GeneratedImageStore store;
        readonly ImageAssetUsageRegistry usage;
        readonly ImageAssetReferenceResolver resolver;
        public ImageAssetManagementService(GeneratedImageStore store,ImageAssetUsageRegistry usage,ImageAssetReferenceResolver resolver)
        {
            this.store=store;
            this.usage=usage;
            this.resolver=resolver;
            store.BindUsage(usage);
        }
        public Task<ImagePurgeResult> InspectReferencesAsync(IEnumerable<string> ids,CancellationToken token)
        {
            var selected=ids.Distinct().ToArray();
            return Task.Run(()=>Inspect(selected,token),token);
        }
        ImagePurgeResult Inspect(string[] ids,CancellationToken token)
        {
            var result=new ImagePurgeResult();
            var refs=resolver.Scan(token,usage);
            var assets=store.AssetSnapshot().ToDictionary(i=>i.Id,StringComparer.Ordinal);
            foreach(string id in ids)
            {
                GeneratedImage item;
                assets.TryGetValue(id,out item);
                if(item==null)result.Blocked[id]=TextResources.Get("imageAssetReferenceResolver.inspect.resultBlockedId");
                else if(refs.Contains(id)||usage.IsInUse(id)||(!String.IsNullOrEmpty(item.ImageJobId)&&usage.IsInUse("job:"+item.ImageJobId)))result.Blocked[id]=TextResources.Get("imageAssetReferenceResolver.inspect.resultBlockedId.alternativeA");
            }
            return result;
        }
        // Preview is advisory. Cleanup takes the confirmed IDs, never an unconfirmed
        // fresh candidate set, and must reacquire reservations and rescan references.
        public Task<ImageReferenceCleanupPreview> InspectOrphanReferencesAsync(CancellationToken token)
        {
            return Task.Run(()=>{
                var refs=resolver.Scan(token,usage);
                return new ImageReferenceCleanupPreview(store.AssetSnapshot().Where(i=>i.IsReference&&!refs.Contains(i.Id)&&!usage.IsInUse(i.Id)&&(String.IsNullOrEmpty(i.ImageJobId)||!usage.IsInUse("job:"+i.ImageJobId))));
            },token);
        }
        public Task<ImagePurgeResult> CleanupOrphanReferencesAsync(IEnumerable<string> candidateIds,CancellationToken token)
        {return PurgeCore(candidateIds,token,true);}
        public Task<ImagePurgeResult> PurgeAsync(IEnumerable<string> ids,CancellationToken token)
        {return PurgeCore(ids,token,false);}
        Task<ImagePurgeResult> PurgeCore(IEnumerable<string> ids,CancellationToken token,bool privateReferences)
        {
            var selected=ids.Distinct().ToArray();
            return Task.Run(()=>
            {
                var result=new ImagePurgeResult();
                var leases=new List<IDisposable>();
                var candidates=new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    var assets=store.AssetSnapshot().ToDictionary(i=>i.Id,StringComparer.Ordinal);
                    if(BeforeAssetReservations!=null)BeforeAssetReservations();
                    foreach(string id in selected)
                    {
                        token.ThrowIfCancellationRequested();
                        GeneratedImage item;
                        assets.TryGetValue(id,out item);
                        if(item==null||(privateReferences?!item.IsReference:!item.TrashedUtc.HasValue))
                        {
                            result.Blocked[id]=privateReferences?TextResources.Get("imageAssetReferenceResolver.purgeCore.blockedIdPrivateReferences"):TextResources.Get("imageAssetReferenceResolver.purgeCore.blockedIdPrivateReferences.alternativeA");
                            continue;
                        }
                        var lease=usage.TryAcquireDeletion(id);
                        if(lease==null)
                        {
                            result.Blocked[id]=TextResources.Get("imageAssetReferenceResolver.purgeCore.resultBlockedId");
                            continue;
                        }
                        leases.Add(lease);
                        candidates.Add(id);
                    }
                    // A task publishes its input edge while holding that input's lease.
                    // Reserve all candidate assets first: the following snapshot then
                    // includes any owner that completed before these reservations, and
                    // new owners cannot enter while dependent job keys are collected.
                    var taskKeys=resolver.TaskReferenceKeys();
                    var reservedKeys=new HashSet<string>(StringComparer.Ordinal);
                    foreach(string id in candidates.ToArray())
                    {
                        token.ThrowIfCancellationRequested();
                        var item=assets[id];
                        var keys=new List<string>();
                        if(!String.IsNullOrEmpty(item.ImageJobId))keys.Add("job:"+item.ImageJobId);
                        List<string> linked;
                        if(taskKeys.TryGetValue(id,out linked))keys.AddRange(linked);
                        bool reserved=true;
                        foreach(string key in keys.Distinct())if(!reservedKeys.Contains(key))
                        {
                            var lease=usage.TryAcquireDeletion(key);
                            if(lease==null){reserved=false;break;}
                            leases.Add(lease);reservedKeys.Add(key);
                        }
                        if(!reserved){candidates.Remove(id);result.Blocked[id]=TextResources.Get("imageAssetReferenceResolver.purgeCore.resultBlockedId.alternativeA");}
                    }
                    // The final disk scan happens after reservations, never under a session/UI gate.
                    if(AfterReservations!=null)AfterReservations();
                    var refs=resolver.Scan(token,usage);
                    foreach(string id in candidates.ToArray())if(refs.Contains(id))
                    {
                        candidates.Remove(id);
                        result.Blocked[id]=TextResources.Get("imageAssetReferenceResolver.purgeCore.resultBlockedId.alternativeB");
                    }
                    token.ThrowIfCancellationRequested();
                    var deletedJobKeys=candidates.Where(id=>taskKeys.ContainsKey(id)).SelectMany(id=>taskKeys[id]).Distinct().ToArray();
                    store.PurgeChecked(candidates,privateReferences,deletedJobKeys);
                    result.DeletedIds.AddRange(candidates);
                }
                catch(OperationCanceledException)
                {
                    throw;
                }
                catch(Exception e)
                {
                    if(!(e is IOException||e is UnauthorizedAccessException||e is System.Runtime.Serialization.SerializationException||e is System.Security.Cryptography.CryptographicException||e is System.Xml.XmlException||e is InvalidOperationException))throw;
                    foreach(string id in selected)if(!result.Blocked.ContainsKey(id))
                    {
                        if(store.IsPurged(id))
                        {
                            result.DeletedIds.Add(id);
                            result.CleanupPendingIds.Add(id);
                        }
                        else result.Blocked[id]=TextResources.Get("imageAssetReferenceResolver.purgeCore.resultBlockedId.alternativeC");
                    }
                }
                finally
                {
                    foreach(var lease in leases)lease.Dispose();
                }
                if(result.DeletedIds.Count>0)store.NotifyManagementChanged();
                return result;
            },token);
        }
    }
}
