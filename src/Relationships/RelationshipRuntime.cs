using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
namespace Dafeiyu
{
    // Application-owned. Only successful, real operations enter this outbox.
    // Keep the head on any write failure; later timestamps must not overtake it.
    public sealed class RelationshipRuntime : IDisposable
    {
        [DataContract] sealed class PendingEvent
        {
            [DataMember] public string CompanionId,EventId;
            [DataMember] public RelationshipEventType Type;
            [DataMember] public long UtcTicks;
            [DataMember] public int OffsetMinutes;
            internal RelationshipTime Time(){return new RelationshipTime(new DateTime(UtcTicks,DateTimeKind.Utc),TimeSpan.FromMinutes(OffsetMinutes));}
        }
        readonly object gate=new object();readonly string directory,path;readonly Action<Action> dispatch;
        List<PendingEvent> pending=new List<PendingEvent>();bool blocked;volatile bool disposed;volatile string warning="";volatile int pendingCount;
        readonly object submissionGate=new object();Task worker=Task.FromResult(0),shutdown;bool admissionClosed;
        readonly object settingsGate=new object();volatile bool feedbackEnabled=true;int feedbackSuppressionVersion;
        public bool FeedbackEnabled {get{return feedbackEnabled;}}
        public int FeedbackSuppressionVersion {get{return System.Threading.Volatile.Read(ref feedbackSuppressionVersion);}}
        public event Action FeedbackSettingsChanged;
        public RelationshipService Service {get;private set;}
        public string Warning {get{return warning;}}
        public bool RequiresRestart {get{return blocked;}}
        public string DataDirectory {get{return directory;}}
        public int PendingCount {get{return pendingCount;}}
        public event Action Changed;
        public event Action<string,RelationshipResult> Committed;
        public RelationshipRuntime(string dataDirectory,Action<Action> uiDispatch=null)
        {
            Service=new RelationshipService(new RelationshipStore(dataDirectory));directory=Path.Combine(Path.GetFullPath(dataDirectory),"relationships");path=CompanionPackStorage.AssetPath(dataDirectory,"relationships/pending.json");dispatch=uiDispatch??(action=>action());
            try{
                if(File.Exists(path)){
                    if(new FileInfo(path).Length>8*1024*1024)throw new InvalidDataException();
                    using(var stream=File.OpenRead(path))pending=(List<PendingEvent>)new DataContractJsonSerializer(typeof(List<PendingEvent>)).ReadObject(stream);
                    if(pending==null || pending.Count>10000 || pending.Any(p=>p==null || !RelationshipIdentity.IsValid(p.CompanionId) || !RelationshipRules.ValidEventId(p.EventId) || !RelationshipRules.ValidType(p.Type)))throw new InvalidDataException();
                    foreach(var p in pending)p.Time();
                    pendingCount=pending.Count;
                    if(pending.Count>0)warning=TextResources.Get("relationship.saveWarning");
                }
            }catch(Exception error){if(!StorageError(error))throw;pending=new List<PendingEvent>();pendingCount=0;blocked=true;warning=TextResources.Get("relationship.saveWarning");}
        }
        static bool StorageError(Exception e){return e is IOException || e is UnauthorizedAccessException || e is SerializationException || e is ArgumentException || e is System.Xml.XmlException;}
        void Publish(Action action){dispatch(delegate{if(!disposed)action();});}
        void Notify(){Publish(delegate{var changed=Changed;if(changed!=null)changed();});}
        // HomeModel calls this only from its existing FIFO worker. Serialize the
        // transaction and cache together across pages; UI observers read memory.
        internal RelationshipResult SaveSettings(bool growth,bool feedback,bool aiContext)
        {
            RelationshipResult result;
            lock(settingsGate){
                result=Service.UpdateSettings(growth,feedback,aiContext);
                if(result.Success){feedbackEnabled=result.Snapshot.Settings.FeedbackEnabled;if(!feedbackEnabled)System.Threading.Interlocked.Increment(ref feedbackSuppressionVersion);}
            }
            if(result.Success){
                // No captured bool: delayed/reordered UI callbacks always observe
                // the latest cache and the monotonic history of saved disables.
                Publish(delegate{var changed=FeedbackSettingsChanged;if(changed!=null)changed();});
                Notify();
            }
            return result;
        }
        void SavePending()
        {
            CompanionPackStorage.AssetPath(Path.GetDirectoryName(directory),"relationships/pending.json");Directory.CreateDirectory(directory);string temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".pending.tmp");
            try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(List<PendingEvent>)).WriteObject(stream,pending);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        public void Record(string companionId,RelationshipEventType type,string eventId,RelationshipTime time)
        {
            if(!RelationshipIdentity.IsValid(companionId) || !RelationshipRules.ValidEventId(eventId) || !RelationshipRules.ValidType(type) || time==null)return;
            var notifications=new List<Action>();
            lock(gate){if(disposed)return;if(!pending.Any(p=>p.CompanionId==companionId && p.EventId==eventId))pending.Add(new PendingEvent{CompanionId=companionId,Type=type,EventId=eventId,UtcTicks=time.UtcNow.Ticks,OffsetMinutes=(int)time.LocalOffset.TotalMinutes});pendingCount=pending.Count;Drain(notifications);}foreach(var notification in notifications)Publish(notification);Notify();
        }
        void Drain(List<Action> notifications)
        {
            if(blocked){warning=TextResources.Get("relationship.saveWarning");return;}
            try{
                // Persist only completed events before touching the ledger. A crash
                // after ledger commit is safe: receipts deduplicate replay.
                SavePending();
                while(pending.Count>0){
                    var next=pending[0];var result=Service.Record(next.CompanionId,next.Type,next.EventId,next.Time());
                    if(!result.Success){warning=TextResources.Get("relationship.saveWarning");return;}
                    pending.RemoveAt(0);pendingCount=pending.Count;SavePending();
                    var id=next.CompanionId;notifications.Add(delegate{var committed=Committed;if(committed!=null)committed(id,result);});
                }
                warning="";
            }catch(Exception error){if(!StorageError(error))throw;warning=TextResources.Get("relationship.saveWarning");}
        }
        public void RetryPending(){var notifications=new List<Action>();lock(gate){if(disposed)return;Drain(notifications);}foreach(var notification in notifications)Publish(notification);Notify();}
        public void RecordVisit(string companionId,bool actuallyVisible=true){if(actuallyVisible)Record(companionId,RelationshipEventType.Visit,"visit-"+Guid.NewGuid().ToString("N"),RelationshipTime.Current());}
        public void RecordUserInteraction(string companionId,bool userOrigin,bool started){if(userOrigin && started)Record(companionId,RelationshipEventType.UserInteraction,"interaction-"+Guid.NewGuid().ToString("N"),RelationshipTime.Current());}
        public string StageContext(string companionId)
        {
            if(!RelationshipIdentity.IsValid(companionId))return "";
            try{var snapshot=Service.GetSnapshot(companionId);return snapshot.Settings.AiContextEnabled?RelationshipContent.StageContext(snapshot.StageId):"";}
            catch(Exception error){if(!StorageError(error))throw;warning=TextResources.Get("relationship.saveWarning");return "";}
        }
        // UI producers use this ordered chain. Submission does no storage I/O;
        // timestamps, identity and logical IDs are caller-captured values.
        public Task Enqueue(string companionId,RelationshipEventType type,string eventId,RelationshipTime time)
        {
            lock(submissionGate){
                if(admissionClosed)return Task.FromResult(0);
                worker=worker.ContinueWith(delegate{Record(companionId,type,eventId,time);},TaskScheduler.Default);
                return worker;
            }
        }
        public Task EnqueueVisit(string companionId,bool actuallyVisible=true)
        {
            return actuallyVisible?Enqueue(companionId,RelationshipEventType.Visit,"visit-"+Guid.NewGuid().ToString("N"),RelationshipTime.Current()):Task.FromResult(0);
        }
        public Task EnqueueUserInteraction(string companionId,bool userOrigin,bool started)
        {
            return userOrigin && started?Enqueue(companionId,RelationshipEventType.UserInteraction,"interaction-"+Guid.NewGuid().ToString("N"),RelationshipTime.Current()):Task.FromResult(0);
        }
        public Task RetryPendingAsync()
        {
            lock(submissionGate){if(admissionClosed)return Task.FromResult(0);worker=worker.ContinueWith(delegate{RetryPending();},TaskScheduler.Default);return worker;}
        }
        public Task FlushAsync(){lock(submissionGate)return worker;}
        public Task ShutdownAsync()
        {
            lock(submissionGate){if(shutdown!=null)return shutdown;admissionClosed=true;shutdown=worker.ContinueWith(delegate{DisposeCore();},TaskScheduler.Default);return shutdown;}
        }
        void DisposeCore()
        {
            lock(gate){if(disposed)return;try{if(!blocked && pending.Count>0)SavePending();}catch(Exception error){if(!StorageError(error))throw;}disposed=true;Changed=null;Committed=null;FeedbackSettingsChanged=null;}
        }
        public void Dispose(){ShutdownAsync().GetAwaiter().GetResult();}

    }
}
