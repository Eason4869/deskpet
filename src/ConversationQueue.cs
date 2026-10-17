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
        [DataMember] public string Text {get;set;}
        [DataMember] public bool IsGuidance {get;set;}
        [DataMember] public bool ConversationOnly {get;set;}
        [DataMember] public string Workspace {get;set;}
        [DataMember] public int Revision {get;set;}
        [DataMember] public List<ChatMessage> Context {get;set;}
        [DataMember] public List<ChatAttachment> Attachments {get;set;}
        public QueuedMessage Copy(){return new QueuedMessage{Id=Id,Text=Text,IsGuidance=IsGuidance,ConversationOnly=ConversationOnly,Workspace=Workspace,Revision=Revision,Attachments=ChatAttachment.Snapshot(Attachments),Context=Context==null?null:Context.Select(m=>m.Copy()).ToList()};}
    }
    // One queue belongs to the shared session, so closing or expanding a chat
    // window cannot duplicate requests. Only explicit Resume restarts disk queues.
    public sealed class ConversationQueue : IDisposable
    {
        [DataContract] sealed class StoredQueue{[DataMember] public int Version=1;[DataMember] public string Payload;}
        readonly ConversationSession session;readonly Func<PetSettings> settings;readonly Func<string> name;readonly string path;
        readonly object gate=new object();readonly Dictionary<string,List<QueuedMessage>> queues;
        readonly HashSet<string> paused=new HashSet<string>();bool running,steering,disposed,loadFailed;QueuedMessage inFlight;
        Task runTask=Task.FromResult(0);public string Notice {get;private set;}int pauseRevision;
        public int PauseRevision {get{lock(gate)return pauseRevision;}}
        public Task FlushAsync(){lock(gate)return runTask;}
        public event Action Changed;
        public ConversationQueue(ConversationSession session,Func<PetSettings> settings,Func<string> name,string path)
        {
            this.session=session;this.settings=settings;this.name=name;this.path=path;Notice="";session.GuidanceReader=ReadGuidance;session.GuidanceApplied=AckGuidance;
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
            catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is SerializationException || e is CryptographicException || e is FormatException))throw;queues=new Dictionary<string,List<QueuedMessage>>();loadFailed=true;Notice="排队内容读取失败，原文件已保留。请检查数据目录及系统账户后重启。";}
            session.StateChanged+=SessionChanged;session.HistoryChanged+=SessionChanged;
        }
        List<QueuedMessage> Current{get{List<QueuedMessage> list;string id=session.PersonaId;if(!queues.TryGetValue(id,out list))queues[id]=list=new List<QueuedMessage>();return list;}}
        public IList<QueuedMessage> Items{get{lock(gate)return Current.Select(m=>m.Copy()).ToList().AsReadOnly();}}
        public bool IsRunning{get{lock(gate)return running || steering;}}
        public bool IsPaused{get{lock(gate)return paused.Contains(session.PersonaId);}}
        public bool HasPending{get{lock(gate)return loadFailed || queues.Any(p=>p.Value.Count>0);}}
        static string Validate(string text){text=(text??"").Trim();if(text.Length==0 || text.Length>8000)throw new ApiException("请输入 1～8000 个字符。");return text;}
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
        {
            var files=ChatAttachment.Snapshot(attachments);if(String.IsNullOrWhiteSpace(text) && files.Count>0)text="请查看附件。";
            text=Validate(text);ApiClient.ValidateConnection(settings().Text,ApiClient.ChatPath(settings().Text));
            lock(gate){Writable();if(Current.Count>=20)throw new InvalidOperationException("最多排队 20 条，请先处理或删除一些消息。");bool fresh=Current.Count==0 && !running && !session.IsBusy;var entry=new QueuedMessage{Id=Guid.NewGuid().ToString("N"),Text=text,Attachments=files,ConversationOnly=conversationOnly};Current.Add(entry);try{Save();}catch{Current.Remove(entry);throw;}if(fresh)paused.Remove(session.PersonaId);Notice="";}
            Announce();Kick();
        }
        public void Edit(string id,string text)
        {text=Validate(text);lock(gate){Writable();var entry=Current.SingleOrDefault(m=>m.Id==id);if(entry==null)throw new InvalidOperationException("这条消息已开始发送。");string previous=entry.Text;int revision=entry.Revision;entry.Text=text;if(entry.IsGuidance)entry.Revision++;try{Save();}catch{entry.Text=previous;entry.Revision=revision;throw;}}Announce();}
        public void Remove(string id)
        {lock(gate){Writable();var entry=Current.SingleOrDefault(m=>m.Id==id);if(entry==null)return;int index=Current.IndexOf(entry);Current.Remove(entry);try{Save();}catch{Current.Insert(index,entry);throw;}}Announce();}
        // Resolve stable IDs under the same lock as dequeue. A stale drag must
        // never reinsert a request that has started, or target a removed row.
        public bool MoveBefore(string id,string beforeId)
        {
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
        }
        public void Resume(){lock(gate){Writable();paused.Remove(session.PersonaId);Notice="";}Announce();Kick();}
        public void Hold(){lock(gate)paused.Add(session.PersonaId);Announce();}
        public async Task PauseAsync()
        {
            Task wait;lock(gate){pauseRevision++;paused.Add(session.PersonaId);wait=runTask;}Announce();await session.StopAsync();await wait;
        }
        public Task SteerAsync(string text,string queuedId=null,IList<ChatAttachment> attachments=null,bool conversationOnly=false)
        {
            var files=ChatAttachment.Snapshot(attachments);if(String.IsNullOrWhiteSpace(text) && files.Count>0)text="请查看附件。";text=Validate(text);var cfg=settings();ApiClient.ValidateConnection(cfg.Text,ApiClient.ChatPath(cfg.Text));
            lock(gate){Writable();var entry=queuedId==null?new QueuedMessage{Id=Guid.NewGuid().ToString("N"),Text=text,Attachments=files,ConversationOnly=conversationOnly}:Current.SingleOrDefault(m=>m.Id==queuedId);if(entry==null)throw new InvalidOperationException("这条消息已开始发送。");if(queuedId==null && Current.Count>=20)throw new InvalidOperationException("队列已满，请先处理已有消息。");
                int index=Current.IndexOf(entry);bool previous=entry.IsGuidance;string project=entry.Workspace;entry.IsGuidance=true;entry.Workspace=session.ActiveAgentWorkspace??(cfg.AgentEnabled?cfg.AgentWorkspace:null);
                if(index>=0)Current.RemoveAt(index);int position=Current.TakeWhile(m=>m.IsGuidance).Count();Current.Insert(position,entry);
                try{Save();}catch{Current.Remove(entry);entry.IsGuidance=previous;entry.Workspace=project;if(index>=0)Current.Insert(index,entry);throw;}
                if(!session.IsBusy)paused.Remove(session.PersonaId);Notice="";
            }Announce();Kick();return Task.FromResult(0);
        }
        IList<AgentGuidance> ReadGuidance(string persona,string workspace)
        {lock(gate){List<QueuedMessage> list;if(disposed || !queues.TryGetValue(persona,out list))return new List<AgentGuidance>();return list.Where(m=>m.IsGuidance && SameProject(m.Workspace,workspace)).Select(m=>new AgentGuidance{Id=m.Id,Text=m.Text,Revision=m.Revision,Attachments=ChatAttachment.Snapshot(m.Attachments)}).ToList();}}
        void AckGuidance(string persona,IList<AgentGuidance> accepted)
        {lock(gate){List<QueuedMessage> list;if(disposed || !queues.TryGetValue(persona,out list))return;var previous=list.ToList();list.RemoveAll(m=>accepted.Any(g=>g.Id==m.Id && g.Revision==m.Revision));try{Save();}catch{list.Clear();list.AddRange(previous);throw;}}Announce();}
        static bool SameProject(string a,string b){return !String.IsNullOrEmpty(a) && !String.IsNullOrEmpty(b) && String.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);}
        public async Task InterruptAsync(string text,string queuedId=null,IList<ChatAttachment> attachments=null,bool conversationOnly=false)
        {
            var files=ChatAttachment.Snapshot(attachments);if(String.IsNullOrWhiteSpace(text) && files.Count>0)text="请查看附件。";
            text=Validate(text);ApiClient.ValidateConnection(settings().Text,ApiClient.ChatPath(settings().Text));
            string persona;Task wait;QueuedMessage entry;List<ChatMessage> preceding;int revision;
            lock(gate)
            {
                Writable();if(steering)throw new InvalidOperationException("正在切换回复，请稍候。");persona=session.PersonaId;
                entry=queuedId==null?new QueuedMessage{Id=Guid.NewGuid().ToString("N"),Text=text,Attachments=files,ConversationOnly=conversationOnly}:Current.SingleOrDefault(m=>m.Id==queuedId);
                if(entry==null)throw new InvalidOperationException("这条消息已开始发送。");if(queuedId==null && Current.Count>=20)throw new InvalidOperationException("队列已满，请先删除一条消息。");
                // Persist the new instruction before stopping a request or clearing its draft.
                if(queuedId==null){Current.Add(entry);try{Save();}catch{Current.Remove(entry);throw;}}
                preceding=inFlight==null || inFlight.Context==null?new List<ChatMessage>():inFlight.Context.Select(m=>m.Copy()).ToList();revision=pauseRevision;steering=true;paused.Add(persona);wait=runTask;
            }
            Announce();
            try
            {
                await session.StopAsync();await wait;
                lock(gate)
                {
                    if(disposed)return;
                    if(session.PersonaId!=persona)throw new InvalidOperationException("伙伴已切换，消息保留在原队列。");
                    var history=session.History.Messages;var last=history.LastOrDefault();
                    if(last!=null && last.Role=="assistant" && last.Status!="complete")
                    {
                        var original=history.LastOrDefault(m=>m.TurnId==last.TurnId && m.Role=="user");entry.Context=preceding;if(original!=null)entry.Context.Add(original.Copy());
                        if(!String.IsNullOrEmpty(last.Content))entry.Context.Add(new ChatMessage("assistant",last.Content+"\n[以上是被用户打断的未完成回复，请按新的要求继续。]"));
                        while(entry.Context.Count>2 && (entry.Context.Count>20 || entry.Context.Sum(m=>(long)m.Content.Length)>100000))entry.Context.RemoveRange(0,2);
                    }
                    Current.Remove(entry);Current.Insert(0,entry);Save();if(revision==pauseRevision)paused.Remove(persona);Notice="";
                }
            }
            catch{lock(gate){paused.Add(persona);}throw;}
            finally{lock(gate)steering=false;Announce();Kick();}
        }
        void SessionChanged(){Announce();Kick();}
        void Kick()
        {
            lock(gate){if(disposed || running || steering || session.IsBusy || paused.Contains(session.PersonaId) || Current.Count==0)return;running=true;runTask=RunAsync(session.PersonaId);}
        }
        async Task RunAsync(string persona)
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
                        if(!String.IsNullOrEmpty(session.HistoryWarning) || !String.IsNullOrEmpty(session.AgentTaskWarning)){paused.Add(persona);Notice="记录尚未保存，队列已暂停并保留；请修复保存问题后继续。";return;}
                        entry=Current[0];var current=session.CurrentAgentTask;
                        if(entry.IsGuidance && current!=null && SameProject(entry.Workspace,current.Workspace) && (current.Guidance??new List<AgentGuidance>()).Any(g=>g.Id==entry.Id && g.Revision==entry.Revision)){
                            if(current.Status!="complete"){paused.Add(persona);Notice="这条引导已接入尚未完成的任务，请使用继续任务核对结果后继续。";return;}alreadyCompleted=true;
                        }
                        Current.RemoveAt(0);try{Save();}catch{Current.Insert(0,entry);paused.Add(persona);Notice="排队内容保存失败，已暂停。请检查数据目录权限。";return;}inFlight=entry;
                    }
                    Announce();
                    if(alreadyCompleted)continue;
                    try{var cfg=settings();if(entry.ConversationOnly && !entry.IsGuidance){cfg=cfg.Copy();cfg.AgentEnabled=false;}if(entry.IsGuidance && !String.IsNullOrEmpty(entry.Workspace) && (!cfg.AgentEnabled || !SameProject(entry.Workspace,cfg.AgentWorkspace)))throw new ApiException("这条引导属于其他项目，已保留在队列；请切回原项目后继续。");AgentTaskState resume=null;if(entry.IsGuidance && cfg.AgentEnabled && SameProject(entry.Workspace,cfg.AgentWorkspace)){var current=session.CurrentAgentTask;if(current!=null && SameProject(current.Workspace,cfg.AgentWorkspace)){resume=current;if(resume.Guidance==null)resume.Guidance=new List<AgentGuidance>();if(!resume.Guidance.Any(g=>g.Id==entry.Id && g.Revision==entry.Revision))resume.Guidance.Add(new AgentGuidance{Id=entry.Id,Text=entry.Text,Revision=entry.Revision,Attachments=ChatAttachment.Snapshot(entry.Attachments)});if(!AgentTaskStore.FitsCheckpoint(resume))throw new ApiException("引导超过任务检查点容量，消息已保留在队列；请减少附件或开始新的任务。");}}await session.SendTextAsync(entry.Text,cfg,name(),entry.Context,entry.Attachments,resume);}
                    catch(OperationCanceledException){lock(gate)paused.Add(persona);return;}
                    catch(Exception e)
                    {
                        lock(gate){queues[persona].Insert(0,entry);pauseRevision++;paused.Add(persona);Notice=e is ApiException || e is InvalidOperationException?e.Message:"发送未完成，队列已暂停。";try{Save();}catch(IOException){Notice+=" 队列保存失败，请检查数据目录。";}catch(UnauthorizedAccessException){Notice+=" 队列保存失败，请检查数据目录权限。";}}return;
                    }
                }
            }
            finally{lock(gate){running=false;inFlight=null;}Announce();Kick();}
        }
        public void Dispose(){lock(gate){disposed=true;foreach(var key in queues.Keys)paused.Add(key);}if(session.GuidanceReader==ReadGuidance){session.GuidanceReader=null;session.GuidanceApplied=null;}session.StateChanged-=SessionChanged;session.HistoryChanged-=SessionChanged;}
    }
}
