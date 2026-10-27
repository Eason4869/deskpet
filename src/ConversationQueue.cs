using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Dafeiyu
{
    [DataContract] public sealed class QueuedMessage
    {
        [DataMember] public string Id {get;set;}
        [DataMember(EmitDefaultValue=false)] public string RelationshipCompanionId {get;set;}
        [DataMember] public string Text {get;set;}
        [DataMember] public bool IsGuidance {get;set;}
        [DataMember] public bool ConversationOnly {get;set;}
        [DataMember] public string Workspace {get;set;}
        [DataMember] public int Revision {get;set;}
        [DataMember] public List<ChatMessage> Context {get;set;}
        [DataMember] public List<ChatAttachment> Attachments {get;set;}
        public QueuedMessage Copy(){return new QueuedMessage{Id=Id,RelationshipCompanionId=RelationshipCompanionId,Text=Text,IsGuidance=IsGuidance,ConversationOnly=ConversationOnly,Workspace=Workspace,Revision=Revision,Attachments=ChatAttachment.Snapshot(Attachments),Context=Context==null?null:Context.Select(m=>m.Copy()).ToList()};}
    }
    // One queue belongs to the shared session, so closing or expanding a chat
    // window cannot duplicate requests. Only explicit Resume restarts disk queues.
    public sealed class ConversationQueue : IDisposable
    {
        [DataContract] sealed class StoredQueue{[DataMember] public int Version=1;[DataMember] public string Payload;}
        readonly ConversationSession session;readonly Func<PetSettings> settings;readonly Func<string> name;readonly string path;
        readonly object gate;readonly Dictionary<string,List<QueuedMessage>> queues;
        readonly HashSet<string> paused=new HashSet<string>();bool running,steering,disposed,loadFailed;QueuedMessage inFlight;
        readonly IDisposable maintenanceRegistration;
        Task runTask=Task.FromResult(0);public string Notice {get;private set;}int pauseRevision;
        public int PauseRevision {get{lock(gate)return pauseRevision;}}
        public Task FlushAsync(){lock(gate)return runTask;}
        public event Action Changed;
        public ConversationQueue(ConversationSession session,Func<PetSettings> settings,Func<string> name,string path)
        {
            this.session=session;using(session.EnterRequestActivity()){gate=session.SynchronizationRoot;this.settings=settings;this.name=name;this.path=path;Notice="";session.GuidanceReader=ReadGuidance;session.GuidanceApplied=AckGuidance;
            queues=new Dictionary<string,List<QueuedMessage>>();
            if(File.Exists(path))try
            {
                StoredQueue saved;using(var stream=File.OpenRead(path))saved=(StoredQueue)new DataContractJsonSerializer(typeof(StoredQueue)).ReadObject(stream);
                if(saved==null || saved.Version!=1 || String.IsNullOrEmpty(saved.Payload))throw new SerializationException();
                byte[] data=SecretProtection.Unprotect(Convert.FromBase64String(saved.Payload));
                using(var stream=new MemoryStream(data))queues=(Dictionary<string,List<QueuedMessage>>)new DataContractJsonSerializer(typeof(Dictionary<string,List<QueuedMessage>>)).ReadObject(stream);
                if(queues==null || queues.Any(p=>p.Value==null || p.Value.Count>20 || p.Value.Any(m=>m==null || String.IsNullOrEmpty(m.Id) || String.IsNullOrWhiteSpace(m.Text) || m.Text.Length>8000)))throw new SerializationException();
                foreach(var pair in queues)paused.Add(pair.Key);
            }
            catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is SerializationException || e is CryptographicException || e is FormatException))throw;queues=new Dictionary<string,List<QueuedMessage>>();loadFailed=true;Notice=TextResources.Get("conversationQueue.conversationQueue.notice");}
            session.StateChanged+=SessionChanged;session.HistoryChanged+=SessionChanged;maintenanceRegistration=session.Maintenance.Register(FlushAsync,()=>HasPending||IsRunning,this);}
        }
        List<QueuedMessage> Current{get{List<QueuedMessage> list;string id=session.PersonaId;if(!queues.TryGetValue(id,out list))queues[id]=list=new List<QueuedMessage>();return list;}}
        public IList<QueuedMessage> Items{get{lock(gate)return Current.Select(m=>m.Copy()).ToList().AsReadOnly();}}
        public bool IsRunning{get{lock(gate)return running || steering;}}
        public bool IsPaused{get{lock(gate)return paused.Contains(session.PersonaId);}}
        public bool HasPending{get{lock(gate)return loadFailed || queues.Any(p=>p.Value.Count>0);}}
        internal static bool HasPersistedPendingStrict(string path)
        {
            if(new FileInfo(path).Length>32*1024*1024)throw new SerializationException();StoredQueue saved;
            using(var stream=File.OpenRead(path))saved=(StoredQueue)new DataContractJsonSerializer(typeof(StoredQueue)).ReadObject(stream);
            if(saved==null||saved.Version!=1||String.IsNullOrEmpty(saved.Payload))throw new SerializationException();
            byte[] plain=SecretProtection.Unprotect(Convert.FromBase64String(saved.Payload));if(plain.Length>32*1024*1024)throw new SerializationException();Dictionary<string,List<QueuedMessage>> contents;
            using(var stream=new MemoryStream(plain))contents=(Dictionary<string,List<QueuedMessage>>)new DataContractJsonSerializer(typeof(Dictionary<string,List<QueuedMessage>>)).ReadObject(stream);
            if(contents==null||contents.Any(p=>p.Value==null))throw new SerializationException();return contents.Any(p=>p.Value.Count!=0);
        }
        static string Validate(string text){text=(text??"").Trim();if(text.Length==0 || text.Length>8000)throw new ApiException(TextResources.Get("conversationQueue.validate.text"));return text;}
        void Writable(){if(disposed)throw new ObjectDisposedException("ConversationQueue");if(loadFailed)throw new InvalidOperationException(Notice);}
        void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
            byte[] data;using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(Dictionary<string,List<QueuedMessage>>)).WriteObject(memory,queues);data=memory.ToArray();}
            var saved=new StoredQueue{Payload=Convert.ToBase64String(SecretProtection.Protect(data))};
            using(var stream=File.Create(temp))new DataContractJsonSerializer(typeof(StoredQueue)).WriteObject(stream,saved);
            for(int attempt=0;;attempt++)try
            {
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);break;
            }
            catch(IOException e)
            {
                // File scanners can briefly hold the just-written queue between
                // adjacent submissions. Keep the atomic replacement and retry only
                // transient Windows sharing / replacement errors, never permissions.
                int code=e.HResult&0xffff;if(attempt>=3 || (code!=32 && code!=33 && code!=1175) || !File.Exists(temp))throw;
                System.Threading.Thread.Sleep(25*(attempt+1));
            }
        }
        void Announce(){if(!disposed && Changed!=null)Changed();}
        public void Enqueue(string text,IList<ChatAttachment> attachments=null,bool conversationOnly=false)
        {using(session.EnterRequestActivity()){
            var files=ChatAttachment.Snapshot(attachments);if(String.IsNullOrWhiteSpace(text) && files.Count>0)text=TextResources.Get("conversationQueue.enqueue.text");
            text=Validate(text);ApiClient.ValidateConnection(settings().Text,ApiClient.ChatPath(settings().Text));
            lock(gate){Writable();if(Current.Count>=20)throw new InvalidOperationException(TextResources.Get("conversationQueue.enqueue.text.alternativeA"));bool fresh=Current.Count==0 && !running && !session.IsBusy;var entry=new QueuedMessage{Id=Guid.NewGuid().ToString("N"),RelationshipCompanionId=session.CaptureRelationshipCompanion()??"",Text=text,Attachments=files,ConversationOnly=conversationOnly};Current.Add(entry);try{Save();}catch{Current.Remove(entry);throw;}if(fresh)paused.Remove(session.PersonaId);Notice="";}
            Announce();Kick();
        }}
        public void Edit(string id,string text)
        {using(session.EnterRequestActivity()){text=Validate(text);lock(gate){Writable();var entry=Current.SingleOrDefault(m=>m.Id==id);if(entry==null)throw new InvalidOperationException(TextResources.Get("conversationQueue.edit.text"));string previous=entry.Text;int revision=entry.Revision;entry.Text=text;if(entry.IsGuidance)entry.Revision++;try{Save();}catch{entry.Text=previous;entry.Revision=revision;throw;}}Announce();}}
        public void Remove(string id)
        {using(session.EnterRequestActivity()){lock(gate){Writable();var entry=Current.SingleOrDefault(m=>m.Id==id);if(entry==null)return;int index=Current.IndexOf(entry);Current.Remove(entry);try{Save();}catch{Current.Insert(index,entry);throw;}}Announce();}}
        // Resolve stable IDs under the same lock as dequeue. A stale drag must
        // never reinsert a request that has started, or target a removed row.
        public bool MoveBefore(string id,string beforeId)
        {using(session.EnterRequestActivity()){
            lock(gate)
            {
                Writable();var list=Current;var entry=list.SingleOrDefault(m=>m.Id==id);
                if(entry==null || (beforeId!=null && !list.Any(m=>m.Id==beforeId)))return false;
                if(id==beforeId)return true;
                int from=list.IndexOf(entry),to=beforeId==null?list.Count:list.FindIndex(m=>m.Id==beforeId);
                if(from<to)to--;if(from==to)return true;
                list.RemoveAt(from);list.Insert(to,entry);
                try{Save();}catch{list.RemoveAt(to);list.Insert(from,entry);throw;}
            }
            Announce();return true;
        }}
        public void Resume(){using(session.EnterRequestActivity()){lock(gate){Writable();paused.Remove(session.PersonaId);Notice="";}Announce();Kick();}}
        public void Hold(){lock(gate)paused.Add(session.PersonaId);Announce();}
        public async Task PauseAsync()
        {
            Task wait;lock(gate){pauseRevision++;paused.Add(session.PersonaId);wait=runTask;}Announce();await session.StopAsync();await wait;
        }
        public Task SteerAsync(string text,string queuedId=null,IList<ChatAttachment> attachments=null,bool conversationOnly=false)
        {using(session.EnterRequestActivity()){
            var files=ChatAttachment.Snapshot(attachments);if(String.IsNullOrWhiteSpace(text) && files.Count>0)text=TextResources.Get("conversationQueue.steerAsync.text");text=Validate(text);var cfg=settings();ApiClient.ValidateConnection(cfg.Text,ApiClient.ChatPath(cfg.Text));QueuedMessage receipt;string persona;
            lock(gate){Writable();var entry=queuedId==null?new QueuedMessage{Id=Guid.NewGuid().ToString("N"),RelationshipCompanionId=session.CaptureRelationshipCompanion()??"",Text=text,Attachments=files,ConversationOnly=conversationOnly}:Current.SingleOrDefault(m=>m.Id==queuedId);if(entry==null)throw new InvalidOperationException(TextResources.Get("conversationQueue.steerAsync.text.alternativeA"));if(queuedId==null && Current.Count>=20)throw new InvalidOperationException(TextResources.Get("conversationQueue.steerAsync.text.alternativeB"));
                int index=Current.IndexOf(entry);bool previous=entry.IsGuidance;string project=entry.Workspace;entry.IsGuidance=true;entry.Workspace=session.ActiveAgentWorkspace??(cfg.AgentEnabled?cfg.AgentWorkspace:null);
                if(index>=0)Current.RemoveAt(index);int position=Current.TakeWhile(m=>m.IsGuidance).Count();Current.Insert(position,entry);
                try{Save();}catch{Current.Remove(entry);entry.IsGuidance=previous;entry.Workspace=project;if(index>=0)Current.Insert(index,entry);throw;}
                if(!session.IsBusy)paused.Remove(session.PersonaId);Notice="";receipt=entry.Copy();persona=session.PersonaId;
            }session.RegisterGuidance(persona,receipt.Id,receipt.Text,receipt.Attachments);Announce();Kick();return Task.FromResult(0);
        }}
        IList<AgentGuidance> ReadGuidance(string persona,string workspace)
        {lock(gate){List<QueuedMessage> list;if(disposed || !queues.TryGetValue(persona,out list))return new List<AgentGuidance>();return list.Where(m=>m.IsGuidance && SameProject(m.Workspace,workspace)).Select(m=>new AgentGuidance{Id=m.Id,Text=m.Text,Revision=m.Revision,Attachments=ChatAttachment.Snapshot(m.Attachments)}).ToList();}}
        void AckGuidance(string persona,IList<AgentGuidance> accepted)
        {lock(gate){List<QueuedMessage> list;if(disposed || !queues.TryGetValue(persona,out list))return;var previous=list.ToList();list.RemoveAll(m=>accepted.Any(g=>g.Id==m.Id && g.Revision==m.Revision));try{Save();}catch{list.Clear();list.AddRange(previous);throw;}}Announce();}
        static bool SameProject(string a,string b){return !String.IsNullOrEmpty(a) && !String.IsNullOrEmpty(b) && String.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);}
        public async Task InterruptAsync(string text,string queuedId=null,IList<ChatAttachment> attachments=null,bool conversationOnly=false)
        {using(session.EnterRequestActivity()){
            var files=ChatAttachment.Snapshot(attachments);if(String.IsNullOrWhiteSpace(text) && files.Count>0)text=TextResources.Get("conversationQueue.interruptAsync.text");
            text=Validate(text);ApiClient.ValidateConnection(settings().Text,ApiClient.ChatPath(settings().Text));
            string persona,activeText;Task wait;QueuedMessage entry;List<ChatMessage> preceding;int revision;
            lock(gate)
            {
                Writable();if(steering)throw new InvalidOperationException(TextResources.Get("conversationQueue.interruptAsync.text.alternativeA"));persona=session.PersonaId;
                entry=queuedId==null?new QueuedMessage{Id=Guid.NewGuid().ToString("N"),RelationshipCompanionId=session.CaptureRelationshipCompanion()??"",Text=text,Attachments=files,ConversationOnly=conversationOnly}:Current.SingleOrDefault(m=>m.Id==queuedId);
                if(entry==null)throw new InvalidOperationException(TextResources.Get("conversationQueue.interruptAsync.text.alternativeB"));if(queuedId==null && Current.Count>=20)throw new InvalidOperationException(TextResources.Get("conversationQueue.interruptAsync.text.alternativeC"));
                // Persist the new instruction before stopping a request or clearing its draft.
                if(queuedId==null){Current.Add(entry);try{Save();}catch{Current.Remove(entry);throw;}}
                activeText=inFlight==null?null:inFlight.Text;preceding=inFlight==null || inFlight.Context==null?new List<ChatMessage>():inFlight.Context.Select(m=>m.Copy()).ToList();revision=pauseRevision;steering=true;paused.Add(persona);wait=runTask;
            }
            Announce();
            try
            {
                await session.StopAsync();await wait;
                lock(gate)
                {
                    if(disposed)return;
                    if(session.PersonaId!=persona)throw new InvalidOperationException(TextResources.Get("conversationQueue.interruptAsync.text.alternativeD"));
                    var history=session.History.Messages;var last=history.LastOrDefault(m=>m.Role=="assistant");
                    if(last!=null && last.Role=="assistant" && last.Status!="complete")
                    {
                        var original=history.LastOrDefault(m=>m.TurnId==last.TurnId && m.Role=="user");
                        if(original!=null && original.Source=="guide" && activeText!=null)original=history.LastOrDefault(m=>m.Role=="user"&&m.Source!="guide"&&m.Content==activeText)??original;
                        entry.Context=preceding;if(original!=null)entry.Context.Add(original.Copy());
                        var partial=last;if(String.IsNullOrEmpty(partial.Content) && original!=null)partial=history.SkipWhile(m=>m.Id!=original.Id).LastOrDefault(m=>m.Role=="assistant"&&!String.IsNullOrEmpty(m.Content));
                        if(partial!=null && !String.IsNullOrEmpty(partial.Content))entry.Context.Add(new ChatMessage("assistant",TextResources.Format("conversationQueue.interruptAsync.entryContext",partial.Content)));
                        // The shared request budget selects/compacts complete context after steering.
                    }
                    Current.Remove(entry);Current.Insert(0,entry);Save();if(revision==pauseRevision)paused.Remove(persona);Notice="";
                }
            }
            catch{lock(gate){paused.Add(persona);}throw;}
            finally{lock(gate)steering=false;Announce();Kick();}
        }}
        void SessionChanged(){Announce();Kick();}
        void Kick()
        {
            lock(gate){if(disposed || running || steering || session.IsBusy || paused.Contains(session.PersonaId) || Current.Count==0)return;IDisposable admission;try{admission=session.EnterRequestActivity();}catch(InvalidOperationException){return;}running=true;runTask=RunAsync(session.PersonaId,admission);}
        }
        async Task RunAsync(string persona,IDisposable admission)
        {
            // Yield once so runTask is assigned before cancellation or event handlers run.
            await Task.Yield();
            try
            {
                while(true)
                {
                    // Finish durable writes before deciding whether an accepted guide
                    // is still work or only an acknowledgement awaiting persistence.
                    await session.FlushHistoryAsync().ConfigureAwait(false);
                    QueuedMessage entry;bool alreadyCompleted=false;
                    lock(gate)
                    {
                        if(disposed || steering || paused.Contains(persona) || session.PersonaId!=persona || Current.Count==0 || session.IsBusy)return;
                        if(!String.IsNullOrEmpty(session.HistoryWarning) || !String.IsNullOrEmpty(session.AgentTaskWarning)){paused.Add(persona);Notice=TextResources.Get("conversationQueue.runAsync.notice");return;}
                        entry=Current[0];var current=session.CurrentAgentTask;
                        if(entry.IsGuidance && current!=null && SameProject(entry.Workspace,current.Workspace) && (current.Guidance??new List<AgentGuidance>()).Any(g=>g.Id==entry.Id && g.Revision==entry.Revision)){
                            if(current.Status!="complete"){paused.Add(persona);Notice=TextResources.Get("conversationQueue.runAsync.notice.alternativeA");return;}alreadyCompleted=true;
                        }
                        Current.RemoveAt(0);try{Save();}catch{Current.Insert(0,entry);paused.Add(persona);Notice=TextResources.Get("conversationQueue.runAsync.notice.alternativeB");return;}inFlight=entry;
                    }
                    Announce();
                    if(alreadyCompleted)continue;bool agentStarted=false;
                    try{var cfg=settings();if(entry.ConversationOnly && !entry.IsGuidance){cfg=cfg.Copy();cfg.AgentEnabled=false;}if(entry.IsGuidance && !String.IsNullOrEmpty(entry.Workspace) && (!cfg.AgentEnabled || !SameProject(entry.Workspace,cfg.AgentWorkspace)))throw new ApiException(TextResources.Get("conversationQueue.runAsync.text"));AgentTaskState resume=null;if(entry.IsGuidance && cfg.AgentEnabled && SameProject(entry.Workspace,cfg.AgentWorkspace)){var current=session.CurrentAgentTask;if(current!=null && SameProject(current.Workspace,cfg.AgentWorkspace)){resume=current;if(resume.Guidance==null)resume.Guidance=new List<AgentGuidance>();if(!resume.Guidance.Any(g=>g.Id==entry.Id && g.Revision==entry.Revision))resume.Guidance.Add(new AgentGuidance{Id=entry.Id,Text=entry.Text,Revision=entry.Revision,Attachments=ChatAttachment.Snapshot(entry.Attachments)});if(!AgentTaskStore.FitsCheckpoint(resume))throw new ApiException(TextResources.Get("conversationQueue.runAsync.text.alternativeA"));}}await session.SendTextAsync(entry.Text,cfg,name(),entry.Context,entry.Attachments,resume,kernelInputId:entry.Id,relationshipCompanionId:entry.RelationshipCompanionId??"",guidanceId:entry.IsGuidance?entry.Id:null,onAgentStarted:()=>agentStarted=true);}
                    catch(OperationCanceledException){lock(gate)paused.Add(persona);return;}
                    catch(Exception e)
                    {
                        lock(gate){if(!agentStarted)queues[persona].Insert(0,entry);pauseRevision++;paused.Add(persona);Notice=e is ApiException || e is InvalidOperationException?e.Message:TextResources.Get("conversationQueue.runAsync.invalidOperationExceptionEMessage");if(agentStarted)Notice+=TextResources.Get("conversationQueue.runAsync.notice.alternativeC");try{Save();}catch(IOException){Notice+=TextResources.Get("conversationQueue.runAsync.notice.alternativeD");}catch(UnauthorizedAccessException){Notice+=TextResources.Get("conversationQueue.runAsync.notice.alternativeE");}}return;
                    }
                }
            }
            finally{lock(gate){running=false;inFlight=null;}admission.Dispose();Announce();Kick();}
        }
        public void Dispose(){lock(gate){disposed=true;foreach(var key in queues.Keys)paused.Add(key);}if(session.GuidanceReader==ReadGuidance){session.GuidanceReader=null;session.GuidanceApplied=null;}session.StateChanged-=SessionChanged;session.HistoryChanged-=SessionChanged;FlushAsync().ContinueWith(done=>{var observed=done.Exception;maintenanceRegistration.Dispose();},TaskScheduler.Default);}
    }
}
