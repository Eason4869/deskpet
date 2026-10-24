using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Dafeiyu.Maintenance
{
    public enum HistoryMaintenanceStatus { Complete, Busy, Partial }
    public sealed class HistoryMaintenanceFailure
    {
        public string Path {get;private set;}
        public string Reason {get;private set;}
        internal HistoryMaintenanceFailure(string path,string reason){Path=path;Reason=reason;}
    }
    public sealed class HistoryMaintenanceResult
    {
        public HistoryMaintenanceStatus Status {get;internal set;}
        public int RemovedFiles {get;internal set;}
        public string AttachmentDirectory {get;private set;}
        public IList<HistoryMaintenanceFailure> Failures {get{return failures.AsReadOnly();}}
        readonly List<HistoryMaintenanceFailure> failures=new List<HistoryMaintenanceFailure>();
        internal HistoryMaintenanceResult(string directory){AttachmentDirectory=directory;}
        internal void Fail(string path,Exception error){Status=HistoryMaintenanceStatus.Partial;failures.Add(new HistoryMaintenanceFailure(path,error.Message));}
    }
    // One operation covers the host's complete app-data root. Callers must pass
    // the same gate (and image manager, when installed) used by that host.
    public sealed class HistoryMaintenanceService
    {
        readonly string root;readonly DataMaintenanceGate gate;readonly ImageGenerationTaskManager images;
        public string AttachmentDirectory {get;private set;}
        public HistoryMaintenanceService(string dataRoot,DataMaintenanceGate maintenance,ImageGenerationTaskManager imageTasks=null)
        {if(maintenance==null)throw new ArgumentNullException("maintenance");root=CachePaths.Normalize(dataRoot);gate=maintenance;images=imageTasks;AttachmentDirectory=Path.Combine(root,"preserved-attachments");}
        public async Task<HistoryMaintenanceResult> ClearAllAsync()
        {
            var result=new HistoryMaintenanceResult(AttachmentDirectory);IDisposable lease;
            // BeginAsync reserves synchronously. Busy is a normal result, while
            // failed real I/O drains are partial failures, never success.
            try{lease=await gate.BeginAsync().ConfigureAwait(false);}
            catch(InvalidOperationException){result.Status=HistoryMaintenanceStatus.Busy;return result;}
            catch(Exception error){if(!StorageError(error))throw;result.Fail(root,error);return result;}
            ConversationSession[] sessions;
            using(lease){sessions=gate.GetParticipants<ConversationSession>();await Task.Run(()=>ClearCore(result,sessions)).ConfigureAwait(false);}
            foreach(var session in sessions)session.NotifyMaintenanceChanged();
            return result;
        }
        void ClearCore(HistoryMaintenanceResult result,ConversationSession[] sessions)
        {
            var pins=new List<CacheDirectoryPins>();var targets=new List<Target>();
            var removed=new HashSet<string>(PathComparer);var historyDirectories=new HashSet<string>(PathComparer);
            try{
                CachePaths.Check(root);if(Directory.Exists(root))pins.Add(CacheDirectoryPins.OpenDirectory(root));
                var owners=new List<string>{root};owners.AddRange(Children(Path.Combine(root,"conversations"),pins));
                foreach(string project in Children(Path.Combine(root,"projects"),pins))owners.AddRange(Children(Path.Combine(project,"chats"),pins));
                foreach(string owner in owners){
                    string history=Path.Combine(owner,"history");historyDirectories.Add(history);
                    AddFiles(history,"*.history",2,targets,pins,result);AddFiles(Path.Combine(history,"agent-tasks"),"*.task",0,targets,pins,result);AddFiles(Path.Combine(owner,"kernel-sessions"),"*.session",0,targets,pins,result);
                    foreach(string queue in new[]{Path.Combine(owner,"queued-messages.json"),Path.Combine(owner,"queue.json")})if(File.Exists(queue)){
                        try{CachePaths.Check(queue);if(ConversationQueue.HasPersistedPendingStrict(queue)){result.Status=HistoryMaintenanceStatus.Busy;return;}}
                        catch(Exception error){if(!StorageError(error))throw;result.Fail(queue,error);result.Status=HistoryMaintenanceStatus.Busy;return;}
                    }
                }
                foreach(string backup in Children(Path.Combine(root,"ownership-backups"),pins))AddFiles(backup,"*.history",1,targets,pins,result);
                foreach(var queue in gate.GetParticipants<ConversationQueue>())if(queue.HasPending||queue.IsRunning){result.Status=HistoryMaintenanceStatus.Busy;return;}
                var archive=new AttachmentArchive(AttachmentDirectory);
                // Complete the preservation phase before removing anything. A
                // corrupt migration backup must not later restore cleared root history.
                foreach(var target in targets)try{
                    if(target.Kind>0)PreserveMessages(archive,HistoryStore.InspectFileStrict(target.File.Path));
                    else if(target.File.Path.EndsWith(".task",StringComparison.OrdinalIgnoreCase))PreserveTasks(archive,new[]{AgentTaskStore.InspectFileStrict(target.File.Path)});
                    VerifyUnchanged(target.File);
                }catch(Exception error){if(!StorageError(error))throw;result.Fail(target.File.Path,error);}
                foreach(var session in sessions)try{
                    string directory=session.MaintenanceHistoryDirectory;
                    if(directory!=null&&!historyDirectories.Contains(directory))throw new IOException(TextResources.Get("dataMaintenance.storageFailure"));
                    PreserveMessages(archive,session.MaintenanceMessages());PreserveTasks(archive,session.MaintenanceTasks());
                    if(!String.IsNullOrEmpty(session.MaintenanceStorageWarning))throw new IOException(session.MaintenanceStorageWarning);
                }catch(Exception error){if(!StorageError(error))throw;result.Fail(session.MaintenanceHistoryDirectory??root,error);}
                if(result.Failures.Count!=0)return;
                // Detach delivery before deletion, including the live manager's
                // cache. Detached records/images remain usable in the gallery.
                var managers=sessions.Select(s=>s.ImageTasks).Concat(new[]{images}).Where(m=>m!=null).Distinct().ToArray();
                foreach(var manager in managers)manager.DetachHistoryForMaintenance();
                if(!managers.Any(m=>PathComparer.Equals(m.MaintenanceDirectory,Path.Combine(root,"generated-images")))){
                    var log=new ImageGenerationTaskStore(root);var records=log.Load();if(!String.IsNullOrEmpty(log.Warning))throw new IOException(log.Warning);
                    if(records.Any(r=>r.Source==ImageGenerationSource.Conversation)){foreach(var record in records)ImageGenerationTaskManager.DetachHistoryRecord(record);log.Save(records);}
                }
                // Promote temporary originals before clearing their history usage
                // leases. Missing assets fail closed without erasing the message.
                foreach(var session in sessions)if(session.ImageStore!=null)foreach(var image in session.MaintenanceMessages().SelectMany(m=>m.GeneratedImages??new List<GeneratedImage>()))session.ImageStore.Promote(image.Id);
                foreach(var target in targets.Where(t=>t.Kind==0))Remove(target,result,removed);
                foreach(var session in sessions)session.ResetContextsForMaintenance();
                if(result.Failures.Count!=0)return;
                foreach(var target in targets.Where(t=>t.Kind==1))Remove(target,result,removed);
                if(result.Failures.Count!=0)return;
                foreach(var target in targets.Where(t=>t.Kind==2))Remove(target,result,removed);
                // Missing files also have loaded unsaved histories. They are safe
                // to clear after their attachments were independently preserved.
                foreach(var session in sessions)session.MarkAbsentHistoriesForMaintenance(removed);
                foreach(var session in sessions)session.ResetHistoriesForMaintenance(removed);
            }
            catch(Exception error){if(!StorageError(error))throw;result.Fail(root,error);}
            finally{foreach(var pin in pins)pin.Dispose();}
        }
        static readonly StringComparer PathComparer=Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
        static bool StorageError(Exception error){return CachePaths.StorageError(error)||error is InvalidDataException||error is CryptographicException||error is SerializationException||error is FormatException||error is ArgumentException||error is System.Xml.XmlException||error is ApiException||error is InvalidOperationException;}
        static void PreserveMessages(AttachmentArchive archive,IEnumerable<ChatMessage> messages){foreach(var message in messages)archive.Preserve(message.Attachments);}
        static void PreserveTasks(AttachmentArchive archive,IEnumerable<AgentTaskState> tasks){foreach(var state in tasks)foreach(var guidance in state.Guidance??new List<AgentGuidance>())archive.Preserve(guidance.Attachments);}
        static IEnumerable<string> Children(string path,List<CacheDirectoryPins> pins)
        {CachePaths.Check(path);if(!Directory.Exists(path))return new string[0];pins.Add(CacheDirectoryPins.OpenDirectory(path));var children=Directory.GetDirectories(path);foreach(string child in children){CachePaths.Check(child);pins.Add(CacheDirectoryPins.OpenDirectory(child));}return children;}
        sealed class Target {internal CacheFile File;internal int Kind;internal CacheDirectoryPins Pins;}
        static void AddFiles(string directory,string pattern,int kind,List<Target> targets,List<CacheDirectoryPins> pins,HistoryMaintenanceResult result)
        {
            CachePaths.Check(directory);if(!Directory.Exists(directory))return;var pin=CacheDirectoryPins.OpenDirectory(directory);pins.Add(pin);
            foreach(string path in Directory.GetFiles(directory,pattern))try{CachePaths.Check(path);if(new FileInfo(path).Length>8*1024*1024+4)throw new InvalidDataException(TextResources.Get("dataMaintenance.storageFailure"));byte[] bytes=File.ReadAllBytes(path);string hash;using(var sha=SHA256.Create())hash=Convert.ToBase64String(sha.ComputeHash(bytes));targets.Add(new Target{Kind=kind,Pins=pin,File=new CacheFile{Path=path,Bytes=bytes.Length,Hash=hash}});}
            catch(Exception error){if(!StorageError(error))throw;result.Fail(path,error);}
        }
        static void VerifyUnchanged(CacheFile file){CachePaths.Check(file.Path);using(var stream=File.OpenRead(file.Path))using(var sha=SHA256.Create())if(stream.Length!=file.Bytes||Convert.ToBase64String(sha.ComputeHash(stream))!=file.Hash)throw new IOException(TextResources.Get("dataMaintenance.storageFailure"));}
        static void Remove(Target target,HistoryMaintenanceResult result,ISet<string> removed)
        {try{target.Pins.DeleteFile(target.File);removed.Add(target.File.Path);result.RemovedFiles++;}catch(Exception error){if(!StorageError(error))throw;result.Fail(target.File.Path,error);}}
    }
}
