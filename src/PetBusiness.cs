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
        public static readonly string[] Labels = { "待机", "向右跑", "向左跑", "招呼", "跳跃", "委屈", "等待", "思考", "检查成果" };
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
    public sealed class ChatMessage
    {
        [DataMember] public string Id { get; private set; }
        [DataMember] public string TurnId { get; private set; }
        [DataMember] public string Role { get; private set; }
        [DataMember] public string Content { get; private set; }
        [DataMember] public string Reasoning { get; private set; }
        [DataMember] public long ReasoningMilliseconds { get; private set; }
        [DataMember] public bool WorkMode { get; private set; }
        [DataMember] public string AgentTaskId { get; internal set; }
        [DataMember] public DateTime? ProcessingStartedAt { get; private set; }
        [DataMember] public long ProcessingMilliseconds { get; private set; }
        [DataMember] public List<ChatAttachment> Attachments { get; private set; }
        [DataMember] public List<AgentToolEvent> ToolEvents { get; private set; }
        [DataMember] public string Status { get; private set; }
        [DataMember] public string Source { get; private set; }
        [DataMember] public DateTime Timestamp { get; private set; }
        public ChatMessage(string role, string content) : this(role,content,Guid.NewGuid().ToString("N"),"complete","text") { }
        public ChatMessage(string role,string content,string turnId,string status,string source)
        { Id=Guid.NewGuid().ToString("N");TurnId=turnId;Role=role;Content=content??"";Status=status;Source=source;Timestamp=DateTime.UtcNow; }
        public ChatMessage Copy() { var copy=(ChatMessage)MemberwiseClone();copy.SetAttachments(Attachments);copy.SetTools(ToolEvents);return copy; }
        internal void SetTools(IEnumerable<AgentToolEvent> tools) { ToolEvents=tools==null?null:tools.Select(t=>t.Copy()).ToList(); }
        internal void SetAttachments(IEnumerable<ChatAttachment> files) { Attachments=files==null?null:files.Select(f=>f.Copy()).ToList(); }
        internal void SetReasoning(string text,long milliseconds) { Reasoning=text??"";ReasoningMilliseconds=milliseconds; }
        internal void StartProcessing(bool workMode) { WorkMode=workMode;ProcessingStartedAt=DateTime.UtcNow;ProcessingMilliseconds=0; }
        internal void SetProcessingTime(long milliseconds) { ProcessingMilliseconds=Math.Max(0,milliseconds); }
        public string ProcessingLabel(DateTime now)
        {
            long milliseconds=Status=="pending" && ProcessingStartedAt.HasValue?Math.Max(ProcessingMilliseconds,(long)(now.ToUniversalTime()-ProcessingStartedAt.Value).TotalMilliseconds):ProcessingMilliseconds;
            long seconds=Math.Max(0,milliseconds/1000);
            string duration=seconds>=3600?(seconds/3600)+"小时 "+(seconds%3600/60)+"分 "+(seconds%60)+"秒":seconds>=60?(seconds/60)+"分 "+(seconds%60)+"秒":seconds+"秒";
            return (WorkMode?"处理时间 ":"思考时间 ")+duration;
        }
        internal void Update(string content,string status) { Content=content??"";Status=status; }
        internal void Normalize()
        {
            Id=String.IsNullOrEmpty(Id)?Guid.NewGuid().ToString("N"):Id;
            TurnId=String.IsNullOrEmpty(TurnId)?Id:TurnId;Content=Content??"";Source=Source??"text";
            if(Status=="pending")Status="interrupted";
            if(ToolEvents!=null)foreach(var item in ToolEvents.Where(t=>t!=null && t.Status=="running")){item.Status="error";item.Summary+=" · 已中断";}
            if(Status!="complete" && Status!="interrupted" && Status!="cancelled" && Status!="error")Status="interrupted";
        }
    }
    public sealed class ConversationLog
    {
        private readonly List<ChatMessage> messages = new List<ChatMessage>();
        private readonly object gate=new object();
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
        public ChatMessage StartProcessing(string id,bool workMode) { lock(gate){var message=messages.Find(m=>m.Id==id);if(message==null)return null;message.StartProcessing(workMode);return message.Copy();} }
        internal void LinkAgentTask(string id,string taskId){lock(gate){var message=messages.Find(m=>m.Id==id);if(message!=null)message.AgentTaskId=taskId;}}
        public ChatMessage Update(string id,string content,string status,string reasoning=null,long reasoningMilliseconds=0,IEnumerable<AgentToolEvent> tools=null,long? processingMilliseconds=null)
        {
            lock(gate) { var message=messages.Find(m=>m.Id==id);if(message==null)return null;message.Update(content,status);if(reasoning!=null)message.SetReasoning(reasoning,reasoningMilliseconds);if(tools!=null)message.SetTools(tools);if(processingMilliseconds.HasValue)message.SetProcessingTime(processingMilliseconds.Value);Trim();return message.Copy(); }
        }
        public void Attach(string id,IEnumerable<ChatAttachment> files) { lock(gate){var message=messages.Find(m=>m.Id==id);if(message!=null)message.SetAttachments(files);} }
        public void Replace(IList<ChatMessage> restored)
        { lock(gate) { messages.Clear();if(restored!=null)foreach(var m in restored){if(m==null || (m.Role!="user" && m.Role!="assistant"))continue;var copy=m.Copy();copy.Normalize();messages.Add(copy);}Trim(); } }
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
                for(int i=0;i+1<messages.Count;i++) { var u=messages[i];var a=messages[i+1];if(u.Role=="user" && a.Role=="assistant" && u.TurnId==a.TurnId && u.Status=="complete" && a.Status=="complete") { pairs.Add(u.Copy());pairs.Add(a.Copy());i++; } }
                if(pairs.Count>20)pairs.RemoveRange(0,pairs.Count-20);
                long characters=pairs.Sum(m=>(long)m.Content.Length);
                while(pairs.Count>0 && characters>100000){characters-=pairs[0].Content.Length+pairs[1].Content.Length;pairs.RemoveRange(0,2);}
                result.AddRange(pairs);
            }
            result.Add(new ChatMessage("user", user)); return result;
        }
        public List<ChatMessage> ForRegeneration(string replyId,string persona)
        {
            lock(gate)
            {
                int index=messages.FindIndex(m=>m.Id==replyId && m.Role=="assistant");if(index<1)throw new InvalidOperationException("找不到这条回复。");
                var user=messages[index-1];if(user.Role!="user" || user.TurnId!=messages[index].TurnId)throw new InvalidOperationException("这条回复没有对应的用户消息。");
                var previous=new ConversationLog();previous.Replace(messages.Take(index-1).ToList());var request=previous.ForRequest(persona,user.Content);request.Last().SetAttachments(user.Attachments);return request;
            }
        }
        // Reserve space for schemas, protocol envelopes and observations. The
        // current user message is never trimmed; only whole prior turns leave.
        public static void BudgetRequest(List<ChatMessage> request)
        {
            Func<ChatMessage,long> size=m=>Encoding.UTF8.GetByteCount(m.Content??"")+ChatAttachment.StoredSize(m.Attachments)+1024;
            while(request.Sum(size)>7L*1024*1024)
            {
                int first=request.FindIndex(m=>m.Role!="system");
                if(first<0 || first>=request.Count-1)throw new ApiException("当前消息与附件超过请求预算，请减少附件后重试。");
                string turn=request[first].TurnId;request.RemoveAt(first);
                if(first<request.Count-1 && request[first].Role=="assistant")request.RemoveAt(first);
            }
        }
        public void Clear() { lock(gate)messages.Clear(); }
    }
}
