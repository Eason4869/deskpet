using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace Dafeiyu
{
    public sealed partial class GeneratedImageStore
    {
        internal Action<string> ManagementFault;
        internal bool ManagementSimulateCrash;
        bool purgeRecoveryPending;
        readonly HashSet<string> purgedJobKeys=new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> purgedIds=new HashSet<string>(StringComparer.Ordinal);
        [DataContract] sealed class PurgeJournal
        {
            [DataMember] public List<GeneratedImage> Images;
            [DataMember] public bool Committed;
            [DataMember] public string TemporaryFolder;
            [DataMember(EmitDefaultValue=false)] public List<string> ReferenceJobKeys;
        }
        public void SetFavorite(IEnumerable<string> ids,bool value)
        {
            UpdateMetadata(ids,i=>i.IsFavorite=value);
        }
        public void MoveToTrash(IEnumerable<string> ids)
        {
            var now=DateTime.UtcNow;
            UpdateMetadata(ids,i=>i.TrashedUtc=now);
        }
        public void Restore(IEnumerable<string> ids)
        {
            UpdateMetadata(ids,i=>i.TrashedUtc=null);
        }
        public IList<GeneratedImage> ListIncludingTrash()
        {
            lock(gate)
            {
                EnsureOpen();
                return images.Where(i=>!i.IsReference).Select(i=>i.Copy()).ToList();
            }
        }
        internal bool IsPurged(string id)
        {
            lock(gate)return purgedIds.Contains(id);
        }
        internal IList<GeneratedImage> AssetSnapshot()
        {
            lock(gate)
            {
                EnsureOpen();
                return images.Select(i=>i.Copy()).ToList();
            }
        }
        void UpdateMetadata(IEnumerable<string> ids,Action<GeneratedImage> update)
        {
            var selected=new HashSet<string>(ids,StringComparer.Ordinal);
            foreach(var id in selected)ValidateId(id);
            lock(gate)
            {
                EnsureOpen();
                if(recoveryRequired)throw new IOException(TextResources.Get("generatedImageStoreManagement.updateMetadata.text"));
                var next=images.Select(i=>i.Copy()).ToList();
                foreach(var item in next)if(selected.Contains(item.Id))update(item);
                SaveIndex(next);
                images.Clear();
                images.AddRange(next);
            }
            Notify();
        }
        void Fault(string phase)
        {
            var fault=ManagementFault;
            if(fault!=null)fault(phase);
        }
        void WriteJournal(string path,PurgeJournal journal)
        {
            if(journal.ReferenceJobKeys!=null&&journal.ReferenceJobKeys.Count>MaximumItems)throw new IOException(TextResources.Get("generatedImageStoreManagement.writeJournal.text"));
            using(var stream=new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(PurgeJournal)).WriteObject(stream,journal);
                WriteAtomic(path,SecretProtection.Protect(stream.ToArray()));
            }
        }
        // Only the checked management service enters this internal transaction. One batch, one index commit.
        internal void PurgeChecked(ISet<string> ids,bool privateReferences=false,IEnumerable<string> referenceJobKeys=null)
        {
            lock(gate)
            {
                EnsureOpen();
                if(recoveryRequired)throw new IOException(TextResources.Get("generatedImageStoreManagement.purgeChecked.text"));
                if(purgeRecoveryPending)RecoverPurgeTransactions();
                var selected=images.Where(i=>ids.Contains(i.Id)).ToList();
                if(selected.Any(i=>privateReferences?!i.IsReference:!i.TrashedUtc.HasValue))throw new InvalidOperationException(TextResources.Get("generatedImageStoreManagement.purgeChecked.text.alternativeA"));
                if(selected.Count==0)return;
                if(!File.Exists(indexPath))SaveIndex(images);
                string quarantine=Path.Combine(directory,"purge-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(quarantine);
                CheckDirectory(quarantine);
                string manifest=Path.Combine(quarantine,"journal.dat");
                var journal=new PurgeJournal
                {
                    ReferenceJobKeys=referenceJobKeys==null?null:referenceJobKeys.Distinct().ToList(),
                    TemporaryFolder=Path.GetFileName(temporaryDirectory),Images=selected.Select(i=>new GeneratedImage
                    {
                        Id=i.Id,ImageJobId=i.ImageJobId,MediaType=i.MediaType,IsReference=i.IsReference,Temporary=i.Temporary,CreatedUtc=DateTime.SpecifyKind(DateTime.MinValue,DateTimeKind.Utc)
                    }).ToList()
                };
                WriteJournal(manifest,journal);
                try
                {
                    Fault("Prepared");
                    foreach(var item in selected)
                    {
                        string source=ExistingPath(item);
                        File.Move(source,Path.Combine(quarantine,item.Id+Extension(item.MediaType)));
                    }
                    Fault("Quarantined");
                    var next=images.Where(i=>!ids.Contains(i.Id)).ToList();
                    SaveIndex(next,Path.GetFileName(quarantine));
                    images.Clear();
                    images.AddRange(next);
                    journal.Committed=true;
                    foreach(var item in selected)
                    {
                        purgedIds.Add(item.Id);
                        if(!String.IsNullOrEmpty(item.ImageJobId))purgedJobKeys.Add("job:"+item.ImageJobId);
                    }
                    if(journal.ReferenceJobKeys!=null)foreach(string key in journal.ReferenceJobKeys)purgedJobKeys.Add(key);
                    if(assetUsage!=null)assetUsage.MarkDeleted(purgedIds.Concat(purgedJobKeys));
                    Fault("IndexCommitted");
                    WriteJournal(manifest,journal);
                    Fault("Cleanup");
                    CleanQuarantine(quarantine,journal);
                }
                catch
                {
                    purgeRecoveryPending=true;
                    if(!journal.Committed&&!ManagementSimulateCrash)
                    {
                        RollbackQuarantine(quarantine,journal);
                        File.Delete(manifest);
                        Directory.Delete(quarantine,false);
                    }
                    throw;
                }
            }
        }
        internal void NotifyManagementChanged()
        {
            Notify();
        }
        void RollbackQuarantine(string folder,PurgeJournal journal)
        {
            foreach(var item in journal.Images)
            {
                string held=Path.Combine(folder,item.Id+Extension(item.MediaType));
                if(File.Exists(held))
                {
                    string destination=item.Temporary?Path.Combine(directory,journal.TemporaryFolder,item.Id+Extension(item.MediaType)):FilePath(item);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    CheckDirectory(Path.GetDirectoryName(destination));
                    if(File.Exists(destination))throw new IOException(TextResources.Get("generatedImageStoreManagement.rollbackQuarantine.text"));
                    File.Move(held,destination);
                }
            }
        }
        void CleanQuarantine(string folder,PurgeJournal journal)
        {
            if(journal.ReferenceJobKeys!=null)foreach(string key in journal.ReferenceJobKeys)purgedJobKeys.Add(key);
            foreach(var item in journal.Images)
            {
                purgedIds.Add(item.Id);
                if(!String.IsNullOrEmpty(item.ImageJobId))purgedJobKeys.Add("job:"+item.ImageJobId);
            }
            foreach(var item in journal.Images)
            {
                string held=Path.Combine(folder,item.Id+Extension(item.MediaType));
                if(File.Exists(held))
                {
                    if((File.GetAttributes(held)&FileAttributes.ReparsePoint)!=0)throw new IOException(TextResources.Get("generatedImageStoreManagement.cleanQuarantine.text"));
                    File.Delete(held);
                }
            }
        }
        void RecoverPurgeTransactions()
        {
            purgeRecoveryPending=true;
            bool cleanupPending=false;
            foreach(string folder in Directory.GetDirectories(directory,"purge-*"))
            {
                CheckDirectory(folder);
                string manifest=Path.Combine(folder,"journal.dat");
                if(!File.Exists(manifest))
                {
                    if(Directory.GetFileSystemEntries(folder).Length==0)Directory.Delete(folder,false);
                    else throw new IOException(TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.text"));
                    continue;
                }
                if(new FileInfo(manifest).Length>MaximumIndexBytes||(File.GetAttributes(manifest)&FileAttributes.ReparsePoint)!=0)throw new IOException(TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.text.alternativeA"));
                PurgeJournal journal;
                byte[] plain=SecretProtection.Unprotect(File.ReadAllBytes(manifest));
                if(plain.Length>MaximumIndexBytes)throw new IOException(TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.text.alternativeB"));
                using(var stream=new MemoryStream(plain))journal=(PurgeJournal)new DataContractJsonSerializer(typeof(PurgeJournal)).ReadObject(stream);
                if(journal==null||journal.TemporaryFolder==null||!journal.TemporaryFolder.StartsWith("temporary-",StringComparison.Ordinal)||!ValidId(journal.TemporaryFolder.Substring(10))||journal.Images==null||journal.Images.Count>MaximumItems||journal.Images.Any(i=>i==null||!ValidId(i.Id)||Extension(i.MediaType)==null))throw new IOException(TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.text.alternativeA"));
                if(journal.ReferenceJobKeys!=null&&(journal.ReferenceJobKeys.Count>MaximumItems||journal.ReferenceJobKeys.Any(key=>String.IsNullOrEmpty(key)||!key.StartsWith("job:",StringComparison.Ordinal)||key.Length>132)))throw new IOException(TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.text.alternativeC"));
                if((recoveryRequired||!File.Exists(indexPath))&&!journal.Committed)throw new IOException(TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.text.alternativeD"));
                bool committed=journal.Committed||lastPurgeId==Path.GetFileName(folder);
                if(committed)
                {
                    if(!journal.Committed)
                    {
                        journal.Committed=true;
                        WriteJournal(manifest,journal);
                    }
                    try{CleanQuarantine(folder,journal);}
                    catch(IOException){cleanupPending=true;Warning=TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.warning");}
                    catch(UnauthorizedAccessException){cleanupPending=true;Warning=TextResources.Get("generatedImageStoreManagement.recoverPurgeTransactions.warning.alternativeA");}
                }
                else
                {
                    RollbackQuarantine(folder,journal);
                    File.Delete(manifest);
                    Directory.Delete(folder,false);
                }
            }
            images.RemoveAll(i=>purgedIds.Contains(i.Id));
            if(assetUsage!=null)assetUsage.MarkDeleted(purgedIds.Concat(purgedJobKeys));
            purgeRecoveryPending=cleanupPending;
        }
    }
}
