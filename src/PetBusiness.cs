using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Dafeiyu
{
    public sealed class HoverInteraction
    {
        public bool Active {get;private set;}
        public bool Update(bool overBody,bool insideWindow,bool allowed)
        {
            if(!insideWindow){Active=false;return false;}
            if(Active || !overBody)return false;
            Active=true;return allowed;
        }
    }
    public sealed class GazeController
    {
        // Continuous clockwise direction, in sixteenths of a full turn.
        public double Direction { get; private set; }
        public void Reset(double direction) { Direction=(direction%16+16)%16; }
        public void Advance(double dx,double dy,double seconds)
        {
            if(Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds<=0 || dx*dx+dy*dy<256) return;
            double target=(Math.Atan2(dx,-dy)*8/Math.PI+16)%16;
            double distance=(target-Direction+24)%16-8;
            Reset(Direction+distance*(1-Math.Exp(-12*seconds)));
        }
        public int Frame(int multiplier) { return (int)Math.Round(Direction*multiplier)%(16*multiplier); }
    }

    public sealed class AnimationPlayer
    {
        public static readonly string[] States = { "idle", "running-right", "running-left", "waving", "jumping", "failed", "waiting", "running", "review" };
        public static readonly string[] Labels = { TextResources.Get("petBusiness.labels.labels"), TextResources.Get("petBusiness.labels.labels.alternativeA"), TextResources.Get("petBusiness.labels.labels.alternativeB"), TextResources.Get("petBusiness.labels.labels.alternativeC"), TextResources.Get("petBusiness.labels.labels.alternativeD"), TextResources.Get("petBusiness.labels.labels.alternativeE"), TextResources.Get("petBusiness.labels.labels.alternativeF"), TextResources.Get("petBusiness.labels.labels.alternativeG"), TextResources.Get("petBusiness.labels.labels.alternativeH") };
        public static readonly int[] Counts = { 6, 8, 8, 4, 5, 8, 6, 6, 6 };
        private static readonly double[] Periods = { .17, .11, .11, .16, .13, .16, .19, .18, .18 };
        private double elapsed;
        private double duration;
        public double Elapsed {get{return elapsed;}}
        private readonly double runningFramePeriod;
        private readonly double[] naturalDurations;
        public int FrameMultiplier { get; private set; }
        public string State { get; private set; }
        public int Frame { get; private set; }
        public int Row { get { return Array.IndexOf(States, State); } }
        public AnimationPlayer(int frameMultiplier=1,double runningFramePeriod=.11,double[] naturalDurations=null)
        {
            if(frameMultiplier!=1 && frameMultiplier!=8) throw new ArgumentOutOfRangeException("frameMultiplier");
            if(runningFramePeriod<=0 || Double.IsNaN(runningFramePeriod) || Double.IsInfinity(runningFramePeriod)) throw new ArgumentOutOfRangeException("runningFramePeriod");
            this.runningFramePeriod=runningFramePeriod;
            if(naturalDurations!=null && (naturalDurations.Length!=States.Length || naturalDurations.Any(v=>v<0 || Double.IsNaN(v) || Double.IsInfinity(v))))throw new ArgumentException("Invalid action durations.");
            this.naturalDurations=naturalDurations==null?null:(double[])naturalDurations.Clone();
            FrameMultiplier=frameMultiplier; Play("idle", 0);
        }
        public void Play(string state, double seconds)
        {
            if (Array.IndexOf(States, state) < 0) throw new ArgumentException("Unknown animation state.");
            State = state; Frame = 0; elapsed = 0; duration = naturalDurations!=null && naturalDurations[Row]>0 ? naturalDurations[Row] : Math.Max(0, seconds);
        }
        public void Advance(double seconds)
        {
            if (seconds < 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            elapsed += seconds;
            if (duration > 0 && elapsed >= duration) { Play("idle", 0); return; }
            double period=Row==1 || Row==2 ? runningFramePeriod : Periods[Row];
            double phase=elapsed / period * FrameMultiplier;
            int count=Counts[Row]*FrameMultiplier;
            // A timed jump lands once. Its requested duration can extend beyond
            // the source cycle, so hold the landing pose until returning to idle.
            Frame = State=="jumping" && duration>0 ? (int)Math.Min(count-1,phase) : (int)(phase%count);
        }
    }

    [DataContract]
    public sealed class AgentReplyChangeSummary
    {
        [DataMember] public string TaskId,Workspace,Status;
        [DataMember] public List<AgentChange> Changes=new List<AgentChange>();
        public AgentReplyChangeSummary Copy()
        { return new AgentReplyChangeSummary{TaskId=TaskId,Workspace=Workspace,Status=Status,Changes=CopyChanges(Changes)}; }
        public static AgentReplyChangeSummary FromTask(AgentTaskState task)
        { return task==null?null:new AgentReplyChangeSummary{TaskId=task.Id,Workspace=task.Workspace,Status=task.Status,Changes=CopyChanges(task.Changes)}; }
        public AgentTaskState ToTask()
        { return new AgentTaskState{Id=TaskId,Workspace=Workspace,Status=Status,Changes=CopyChanges(Changes)}; }
        static List<AgentChange> CopyChanges(IEnumerable<AgentChange> changes)
        { return changes==null?new List<AgentChange>():changes.Where(c=>c!=null).Select(c=>c.Copy()).ToList(); }
    }

    [DataContract]
    public sealed class ChatMessage
    {
        [DataMember] public string Id { get; private set; }
        [DataMember] public string TurnId { get; private set; }
        [DataMember] public string Role { get; private set; }
        [DataMember] public string Content { get; private set; }
        [DataMember] public string Reasoning { get; private set; }
        [DataMember] public long ReasoningMilliseconds { get; private set; }
        [DataMember] public bool WorkMode { get; private set; }
        [DataMember(EmitDefaultValue=false)] public string CompanionSkinId { get; private set; }
        [DataMember(EmitDefaultValue=false)] public string RelationshipCompanionId {get;internal set;}
        [DataMember(EmitDefaultValue=false)] public string RelationshipEventId {get;internal set;}
        [DataMember] public bool RelationshipWork {get;internal set;}
        [DataMember] public string AgentTaskId { get; internal set; }
        [DataMember(EmitDefaultValue=false)] public AgentReplyChangeSummary ChangeSummary { get; set; }
        [DataMember] public DateTime? ProcessingStartedAt { get; private set; }
        [DataMember] public long ProcessingMilliseconds { get; private set; }
        [DataMember] public List<ChatAttachment> Attachments { get; private set; }
        [DataMember(EmitDefaultValue=false)] public List<GeneratedImage> GeneratedImages { get; private set; }
        [DataMember(EmitDefaultValue=false)] public List<string> ImageGenerationTaskIds { get; set; }
        [DataMember] public List<AgentToolEvent> ToolEvents { get; private set; }
        [DataMember] public string Status { get; private set; }
        [DataMember] public string Source { get; private set; }
        [DataMember] public DateTime Timestamp { get; private set; }
        public ChatMessage(string role, string content) : this(role,content,Guid.NewGuid().ToString("N"),"complete","text") { }
        public ChatMessage(string role,string content,string turnId,string status,string source)
        { Id=Guid.NewGuid().ToString("N");TurnId=turnId;Role=role;Content=content??"";Status=status;Source=source;Timestamp=DateTime.UtcNow; }
        public ChatMessage Copy() { var copy=(ChatMessage)MemberwiseClone();copy.SetAttachments(Attachments);copy.SetTools(ToolEvents);copy.SetGeneratedImages(GeneratedImages);copy.ImageGenerationTaskIds=ImageGenerationTaskIds==null?null:new List<string>(ImageGenerationTaskIds);copy.ChangeSummary=ChangeSummary==null?null:ChangeSummary.Copy();return copy; }
        internal void SetGeneratedImages(IEnumerable<GeneratedImage> images) { GeneratedImages=images==null?null:images.Where(i=>i!=null).Take(20).Select(i=>i.Copy()).ToList(); }
        internal void SetTools(IEnumerable<AgentToolEvent> tools) { ToolEvents=tools==null?null:tools.Select(t=>t.Copy()).ToList(); }
        internal void SetAttachments(IEnumerable<ChatAttachment> files) { Attachments=files==null?null:files.Select(f=>f.Copy()).ToList(); }
        internal void SetReasoning(string text,long milliseconds) { Reasoning=text??"";ReasoningMilliseconds=milliseconds; }
        internal void StartProcessing(bool workMode,DateTime? startedAt=null,string skinId=null) { WorkMode=workMode;ProcessingStartedAt=startedAt??DateTime.UtcNow;ProcessingMilliseconds=0;if(!String.IsNullOrWhiteSpace(skinId))CompanionSkinId=skinId; }
        internal void SetProcessingTime(long milliseconds) { ProcessingMilliseconds=Math.Max(0,milliseconds); }
        public string ProcessingLabel(DateTime now)
        {
            long milliseconds=Status=="pending" && ProcessingStartedAt.HasValue?Math.Max(ProcessingMilliseconds,(long)(now.ToUniversalTime()-ProcessingStartedAt.Value).TotalMilliseconds):ProcessingMilliseconds;
            long seconds=Math.Max(0,milliseconds/1000);
            string duration=seconds>=3600?TextResources.Format("petBusiness.processingLabel.durationSeconds",(seconds/3600),(seconds%3600/60),(seconds%60)):seconds>=60?TextResources.Format("petBusiness.processingLabel.durationSeconds.alternativeA",(seconds/60),(seconds%60)):TextResources.Format("petBusiness.processingLabel.durationSeconds.alternativeB",seconds);
            return (WorkMode?TextResources.Get("petBusiness.processingLabel.text"):TextResources.Get("petBusiness.processingLabel.text.alternativeA"))+duration;
        }
        internal void Update(string content,string status) { Content=content??"";Status=status; }
        internal void Normalize()
        {
            Id=String.IsNullOrEmpty(Id)?Guid.NewGuid().ToString("N"):Id;
            TurnId=String.IsNullOrEmpty(TurnId)?Id:TurnId;Content=Content??"";Source=Source??"text";
            if(Status=="pending")Status="interrupted";
            if(ToolEvents!=null)foreach(var item in ToolEvents.Where(t=>t!=null && t.Status=="running")){item.Status="error";item.Summary+=TextResources.Get("petBusiness.normalize.itemSummary");}
            if(Status!="complete" && Status!="interrupted" && Status!="cancelled" && Status!="error")Status="interrupted";
        }
    }
    public sealed class ConversationLog
    {
        private readonly List<ChatMessage> messages = new List<ChatMessage>();
        private readonly HashSet<string> retainedImageReplies=new HashSet<string>();
        private readonly object gate=new object();
        ImageAssetUsageRegistry imageUsage;
        readonly Dictionary<string,IDisposable> imageLeases=new Dictionary<string,IDisposable>(StringComparer.Ordinal);
        public void ConfigureImageUsage(ImageAssetUsageRegistry usage){lock(gate){if(imageUsage!=null&&!Object.ReferenceEquals(imageUsage,usage))throw new InvalidOperationException(TextResources.Get("petBusiness.configureImageUsage.text"));imageUsage=usage;LeaseImageKeys(ImageAssetUsageRegistry.ReferenceKeys(messages));}}
        void LeaseImageKeys(IEnumerable<string> keys){if(imageUsage==null)return;foreach(string key in keys)if(!imageLeases.ContainsKey(key))imageLeases.Add(key,imageUsage.Acquire(key,"memory-history"));}
        internal void ReconcileImageUsage(){lock(gate){var current=new HashSet<string>(ImageAssetUsageRegistry.ReferenceKeys(messages),StringComparer.Ordinal);foreach(string key in imageLeases.Keys.Where(k=>!current.Contains(k)).ToArray()){imageLeases[key].Dispose();imageLeases.Remove(key);}}}
        public void ReleaseImageUsage(){lock(gate){foreach(var lease in imageLeases.Values)lease.Dispose();imageLeases.Clear();}}
        public IList<ChatMessage> Messages { get { lock(gate)return messages.Select(m=>m.Copy()).ToList().AsReadOnly(); } }
        public void Commit(string user, string assistant)
        {
            Commit(user,assistant,"text");
        }
        public void Commit(string user,string assistant,string source)
        {
            lock(gate) { string turn=Guid.NewGuid().ToString("N");messages.Add(new ChatMessage("user",user,turn,"complete",source));messages.Add(new ChatMessage("assistant",assistant,turn,"complete",source));Trim(); }
        }
        public ChatMessage BeginTurn(string text,string source,out ChatMessage user)
        {
            lock(gate) { string turn=Guid.NewGuid().ToString("N");var u=new ChatMessage("user",text,turn,"complete",source);var a=new ChatMessage("assistant","",turn,"pending",source);messages.Add(u);messages.Add(a);Trim();user=u.Copy();return a.Copy(); }
        }
        // Show accepted guidance immediately; its stable turn ID lets delivery reuse
        // this receipt without sending an unpaired user message in normal history.
        public ChatMessage RegisterGuidance(string id,string text,IEnumerable<ChatAttachment> attachments)
        {
            lock(gate){string turn="guide-"+id;var user=messages.Find(m=>m.TurnId==turn && m.Role=="user");if(user==null){user=new ChatMessage("user",text,turn,"complete","guide");messages.Add(user);}else user.Update(text,"complete");user.SetAttachments(attachments);Trim();return user.Copy();}
        }
        public ChatMessage BeginGuidance(string id,string text,out ChatMessage user)
        {
            lock(gate){string turn="guide-"+id;var u=messages.Find(m=>m.TurnId==turn && m.Role=="user");if(u==null){u=new ChatMessage("user",text,turn,"complete","guide");messages.Add(u);}var reply=messages.Find(m=>m.TurnId==turn && m.Role=="assistant");if(reply==null){reply=new ChatMessage("assistant","",turn,"pending","guide");messages.Insert(messages.IndexOf(u)+1,reply);}else reply.Update("","pending");Trim();user=u.Copy();return reply.Copy();}
        }
        public ChatMessage StartProcessing(string id,bool workMode,DateTime? startedAt=null,string skinId=null) { lock(gate){var message=messages.Find(m=>m.Id==id);if(message==null)return null;message.StartProcessing(workMode,startedAt,skinId);return message.Copy();} }
        internal void RetainImageReply(string id,bool retain){lock(gate){if(retain)retainedImageReplies.Add(id);else retainedImageReplies.Remove(id);}}
        internal void RemoveEmptyReply(string id){lock(gate)messages.RemoveAll(m=>m.Id==id&&!retainedImageReplies.Contains(id)&&m.Role=="assistant"&&String.IsNullOrEmpty(m.Content)&&String.IsNullOrEmpty(m.Reasoning)&&(m.ImageGenerationTaskIds==null||m.ImageGenerationTaskIds.Count==0)&&(m.GeneratedImages==null||m.GeneratedImages.Count==0)&&(m.ToolEvents==null||m.ToolEvents.Count==0));}
        internal void PersistGeneratedImages(GeneratedImageStore assets)
        {
            lock(gate)foreach(var message in messages.Where(m=>m.GeneratedImages!=null&&m.GeneratedImages.Any(i=>i.Temporary)))
            {if(assets==null)throw new System.IO.IOException(TextResources.Get("petBusiness.persistGeneratedImages.text"));message.SetGeneratedImages(message.GeneratedImages.Select(i=>i.Temporary?assets.Promote(i.Id):i));}
        }
        internal void LinkAgentTask(string id,string taskId){lock(gate){var message=messages.Find(m=>m.Id==id);if(message!=null){if(message.AgentTaskId!=taskId)message.ChangeSummary=null;message.AgentTaskId=taskId;}}}
        internal void UpdateAgentTaskSummary(AgentTaskState task)
        {
            if(task==null || String.IsNullOrEmpty(task.Id))return;
            lock(gate)foreach(var message in messages.Where(m=>m.Role=="assistant" && m.AgentTaskId==task.Id))message.ChangeSummary=AgentReplyChangeSummary.FromTask(task);
        }
        public ChatMessage Update(string id,string content,string status,string reasoning=null,long reasoningMilliseconds=0,IEnumerable<AgentToolEvent> tools=null,long? processingMilliseconds=null)
        {
            lock(gate) { var message=messages.Find(m=>m.Id==id);if(message==null)return null;message.Update(content,status);if(reasoning!=null)message.SetReasoning(reasoning,reasoningMilliseconds);if(tools!=null)message.SetTools(tools);if(processingMilliseconds.HasValue)message.SetProcessingTime(processingMilliseconds.Value);Trim();return message.Copy(); }
        }
        public void Attach(string id,IEnumerable<ChatAttachment> files) { lock(gate){var message=messages.Find(m=>m.Id==id);if(message!=null)message.SetAttachments(files);} }
        public ChatMessage AttachImageTask(string id,string taskId)
        {lock(gate){var message=messages.Find(m=>m.Id==id&&m.Role=="assistant");if(message==null)return null;LeaseImageKeys(new[]{"job:"+taskId});if(message.ImageGenerationTaskIds==null)message.ImageGenerationTaskIds=new List<string>();if(!message.ImageGenerationTaskIds.Contains(taskId))message.ImageGenerationTaskIds.Add(taskId);return message.Copy();}}
        public ChatMessage AttachGeneratedImage(string id,GeneratedImage image)
        {
            if(image==null)throw new ArgumentNullException("image");
            lock(gate){var message=messages.Find(m=>m.Id==id&&m.Role=="assistant");if(message==null)return null;var images=message.GeneratedImages??new List<GeneratedImage>();if(images.Any(i=>i.Id==image.Id))return message.Copy();if(images.Count>=20)throw new InvalidOperationException(TextResources.Get("petBusiness.attachGeneratedImage.text"));LeaseImageKeys(String.IsNullOrEmpty(image.ReferenceImageId)?new[]{image.Id}:new[]{image.Id,image.ReferenceImageId});message.SetGeneratedImages(images.Concat(new[]{image}));return message.Copy();}
        }
        public void Replace(IList<ChatMessage> restored)
        { lock(gate) { if(restored!=null)LeaseImageKeys(ImageAssetUsageRegistry.ReferenceKeys(restored));messages.Clear();if(restored!=null)foreach(var m in restored){if(m==null || (m.Role!="user" && m.Role!="assistant"))continue;var copy=m.Copy();copy.Normalize();messages.Add(copy);}Trim(); } }
        private void Trim()
        {
            while(messages.Count>0 && (messages.Select(m=>m.TurnId).Distinct().Count()>200 || messages.Sum(m=>(long)Encoding.UTF8.GetByteCount(m.Content)+Encoding.UTF8.GetByteCount(m.Reasoning??"")+ChatAttachment.StoredSize(m.Attachments)+512)>8*1024*1024))
            { string turn=messages[0].TurnId;messages.RemoveAll(m=>m.TurnId==turn); }
        }
        public List<ChatMessage> ForRequest(string persona, string user)
        {
            var result = new List<ChatMessage>();
            if (!String.IsNullOrWhiteSpace(persona)) result.Add(new ChatMessage("system", persona));
            lock(gate) {
                var pairs=new List<ChatMessage>();
                for(int i=0;i+1<messages.Count;i++) { var u=messages[i];var a=messages[i+1];if(u.Role=="user" && a.Role=="assistant" && u.TurnId==a.TurnId && u.Status=="complete" && a.Status=="complete") { var userCopy=u.Copy();var assistantCopy=a.Copy();userCopy.ChangeSummary=null;assistantCopy.ChangeSummary=null;pairs.Add(userCopy);pairs.Add(assistantCopy);i++; } }
                result.AddRange(pairs);
            }
            result.Add(new ChatMessage("user", user)); return result;
        }
        internal void BindRelationship(string replyId,string companionId,string eventId,bool work)
        {lock(gate){var reply=messages.FirstOrDefault(m=>m.Id==replyId);if(reply==null)return;foreach(var message in messages.Where(m=>m.TurnId==reply.TurnId)){message.RelationshipCompanionId=companionId;message.RelationshipEventId=eventId;message.RelationshipWork=work;}}}
        public List<ChatMessage> ForRegeneration(string replyId,string persona)
        {
            lock(gate)
            {
                int index=messages.FindIndex(m=>m.Id==replyId && m.Role=="assistant");if(index<1)throw new InvalidOperationException(TextResources.Get("petBusiness.forRegeneration.text"));
                var user=messages[index-1];if(user.Role!="user" || user.TurnId!=messages[index].TurnId)throw new InvalidOperationException(TextResources.Get("petBusiness.forRegeneration.text.alternativeA"));
                var previous=new ConversationLog();previous.Replace(messages.Take(index-1).ToList());var request=previous.ForRequest(persona,user.Content);request.Last().SetAttachments(user.Attachments);return request;
            }
        }
        // Reserve space for schemas, protocol envelopes and observations. The
        // current user message is never trimmed; only whole prior turns leave.
        public static void BudgetRequest(List<ChatMessage> request)
        {
            Func<ChatMessage,long> size=m=>Encoding.UTF8.GetByteCount(m.Content??"")+ChatAttachment.StoredSize(m.Attachments)+1024;
            var current=request.LastOrDefault(m=>m.Role=="user");
            if(request.Where(m=>m.Role=="system"||Object.ReferenceEquals(m,current)).Sum(size)>7L*1024*1024)throw new ApiException(TextResources.Get("petBusiness.budgetRequest.text"));
        }
        public void Clear() { lock(gate)messages.Clear(); }
    }
}
