using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Dafeiyu
{
    public sealed class ProactiveEvent
    {
        public string Id {get;private set;}
        public string CompanionId {get;private set;}
        public string Text {get;private set;}
        public byte[] Png {get;private set;}
        public DateTime CapturedUtc {get;private set;}
        public string TargetId {get;private set;}
        public string ModelReference {get;private set;}
        public string Description {get;private set;}
        public string ConversationId {get;internal set;}
        public ProactiveEvent(string id,string companionId,string text,byte[] png,DateTime capturedUtc,string targetId,string modelReference,string description=null)
        {
            if(String.IsNullOrWhiteSpace(id)||String.IsNullOrWhiteSpace(companionId)||String.IsNullOrWhiteSpace(text))throw new ArgumentException("Event ID, companion ID and text are required.");
            if(png!=null && (png.Length<8 || png.Length>2*1024*1024 || png[0]!=137 || png[1]!=80 || png[2]!=78 || png[3]!=71))throw new ArgumentException("Invalid PNG.");
            if(description!=null && description.Length>240)throw new ArgumentException("Observation description exceeds 240 characters.");
            Id=id;CompanionId=companionId;Text=text;Png=png==null?null:(byte[])png.Clone();CapturedUtc=capturedUtc.ToUniversalTime();TargetId=targetId;ModelReference=modelReference;Description=description;
        }
        internal ProactiveEvent Copy(){var copy=(ProactiveEvent)MemberwiseClone();copy.Png=Png==null?null:(byte[])Png.Clone();return copy;}
        internal void ClearImage(){Png=null;Description=null;}
    }
    public sealed class ProactiveEventStore : IDisposable
    {
        readonly object gate=new object();readonly Dictionary<string,ProactiveEvent> events=new Dictionary<string,ProactiveEvent>(StringComparer.Ordinal);readonly Func<DateTime> clock;readonly Timer timer;bool disposed;
        public ProactiveEventStore(Func<DateTime> utcClock=null){clock=utcClock??(()=>DateTime.UtcNow);timer=new Timer(_=>OnTimer(),null,Timeout.Infinite,Timeout.Infinite);}
        void Expire(){var now=clock().ToUniversalTime();foreach(var key in events.Where(x=>now-x.Value.CapturedUtc>=TimeSpan.FromMinutes(15) || x.Value.CapturedUtc>now).Select(x=>x.Key).ToArray())events.Remove(key);}
        void Schedule(){if(disposed)return;if(events.Count==0){timer.Change(Timeout.Infinite,Timeout.Infinite);return;}var remaining=events.Values.Min(x=>(x.CapturedUtc.AddMinutes(15)-clock().ToUniversalTime()).TotalMilliseconds);timer.Change((int)Math.Max(1,Math.Min(Int32.MaxValue,remaining)),Timeout.Infinite);}
        void OnTimer(){lock(gate){if(disposed)return;Expire();Schedule();}}
        public string Register(ProactiveEvent value)
        {
            if(value==null)throw new ArgumentNullException("value");lock(gate){if(disposed)throw new ObjectDisposedException("ProactiveEventStore");Expire();ProactiveEvent existing;if(events.TryGetValue(value.Id,out existing)){if(existing.CompanionId!=value.CompanionId)throw new InvalidOperationException("Event belongs to another companion.");return existing.Id;}var now=clock().ToUniversalTime();if(value.CapturedUtc>now || now-value.CapturedUtc>=TimeSpan.FromMinutes(15))throw new InvalidOperationException("Proactive event has expired.");events.Add(value.Id,value.Copy());while(events.Values.Count(x=>x.Png!=null)>2 || events.Values.Where(x=>x.Png!=null).Sum(x=>(long)x.Png.Length)>4L*1024*1024){events.Values.Where(x=>x.Png!=null).OrderBy(x=>x.CapturedUtc).First().ClearImage();}Schedule();return value.Id;}
        }
        public ProactiveEvent TryGet(string eventId){lock(gate){Expire();ProactiveEvent value;return eventId!=null && events.TryGetValue(eventId,out value)?value.Copy():null;}}
        public string BindConversation(string eventId,string companionId,string conversationId)
        {lock(gate){Expire();Schedule();ProactiveEvent value;if(eventId==null || !events.TryGetValue(eventId,out value) || value.CompanionId!=companionId)throw new InvalidOperationException("Event does not belong to this companion or has expired.");if(String.IsNullOrWhiteSpace(conversationId))throw new ArgumentException("Conversation ID is required.");if(value.ConversationId==null)value.ConversationId=conversationId;return value.ConversationId;}}
        public void ClearImages(){lock(gate){foreach(var value in events.Values)value.ClearImage();Schedule();}}
        public void Clear(){lock(gate){events.Clear();Schedule();}}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;events.Clear();timer.Dispose();}}
    }
}
