using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public interface IRelationshipFeedbackScheduler { IDisposable Schedule(TimeSpan delay, Action callback); }
    public sealed class RelationshipFeedbackNotice
    {
        public string CompanionId { get; internal set; }
        public int AwardedPoints { get; internal set; }
        public List<RelationshipMilestone> Milestones { get; internal set; }
    }
    // Application lifetime, independent of the home page. Consumes suppressed
    // milestones as well as displayed ones so there is no deferred popup backlog.
    public sealed class RelationshipFeedback : IDisposable
    {
        sealed class Scheduler : IRelationshipFeedbackScheduler
        {
            readonly Action<Action> dispatch;
            internal Scheduler(Action<Action> dispatch) { this.dispatch=dispatch; }
            public IDisposable Schedule(TimeSpan delay, Action callback) { return new Timer(_=>dispatch(callback),null,delay,Timeout.InfiniteTimeSpan); }
        }
        readonly RelationshipRuntime runtime;
        readonly Func<string,bool> allowed;
        readonly Action<RelationshipFeedbackNotice> show;
        readonly IRelationshipFeedbackScheduler scheduler;
        readonly object gate=new object();
        IDisposable timer;
        RelationshipFeedbackNotice pending;
        int pendingSuppressionVersion;
        volatile bool disposed;
        public Task PendingDisplay {get;private set;}
        public RelationshipFeedback(RelationshipRuntime runtime,Func<string,bool> allowed,Action<RelationshipFeedbackNotice> show,
            IRelationshipFeedbackScheduler scheduler=null,Action<Action> dispatch=null)
        {
            if(runtime==null||allowed==null||show==null)throw new ArgumentNullException("feedback dependencies");
            this.runtime=runtime;this.allowed=allowed;this.show=show;PendingDisplay=Task.FromResult(0);
            this.scheduler=scheduler??new Scheduler(dispatch??(action=>action()));
            runtime.Committed+=Committed;runtime.FeedbackSettingsChanged+=SettingsChanged;
        }
        void SettingsChanged()
        {
            lock(gate){if(disposed)return;if(runtime.FeedbackEnabled&&pendingSuppressionVersion==runtime.FeedbackSuppressionVersion)return;
                pending=null;if(timer!=null)timer.Dispose();timer=null;}
        }
        async void Committed(string id,RelationshipResult result) { await AcceptAsync(id,result); }
        public async Task AcceptAsync(string id,RelationshipResult result)
        {
            if(disposed||result==null||!result.Success||result.Snapshot==null||result.Snapshot.State==null||result.Snapshot.State.CompanionId!=id)return;
            int suppressionVersion=runtime.FeedbackSuppressionVersion;
            bool admitted=runtime.FeedbackEnabled&&allowed(id)&&result.Snapshot.Settings.FeedbackEnabled;
            var claimed=await Task.Run(delegate {
                var milestones=new List<RelationshipMilestone>();
                // Claim even first_meeting: it is a durable home card, never an
                // automatic threshold celebration. Core transactions arbitrate races.
                foreach(var milestone in result.Snapshot.State.Milestones.Where(m=>!m.NotificationClaimed)){
                    var claim=runtime.Service.ClaimMilestone(id,milestone.StageId);
                    if(!claim.Success)return null;
                    if(claim.ClaimedNotification&&milestone.StageId!="first_meeting")milestones.Add(milestone);
                }
                try { return runtime.Service.GetSnapshot(id).Settings.FeedbackEnabled?milestones:null; }
                catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is System.Runtime.Serialization.SerializationException||error is ArgumentException))throw;return null;}
            });
            // The companion, focus, quiet mode or busy state may change during I/O.
            if(disposed||!admitted||claimed==null||!runtime.FeedbackEnabled||suppressionVersion!=runtime.FeedbackSuppressionVersion||!allowed(id))return;
            if(result.AwardedPoints==0&&claimed.Count==0)return;
            lock(gate){
                if(disposed||!runtime.FeedbackEnabled||suppressionVersion!=runtime.FeedbackSuppressionVersion)return;
                if(pendingSuppressionVersion!=suppressionVersion)pending=null;
                pendingSuppressionVersion=suppressionVersion;
                if(pending==null||pending.CompanionId!=id)pending=new RelationshipFeedbackNotice{CompanionId=id,Milestones=new List<RelationshipMilestone>()};
                pending.AwardedPoints+=result.AwardedPoints;pending.Milestones.AddRange(claimed);
                if(timer!=null)timer.Dispose();timer=scheduler.Schedule(TimeSpan.FromMilliseconds(400),delegate{PendingDisplay=DisplayAsync();});
            }
        }
        async Task DisplayAsync()
        {
            RelationshipFeedbackNotice notice;int suppressionVersion;
            lock(gate){notice=pending;suppressionVersion=pendingSuppressionVersion;pending=null;if(timer!=null)timer.Dispose();timer=null;if(disposed)return;}
            if(notice==null||!runtime.FeedbackEnabled||suppressionVersion!=runtime.FeedbackSuppressionVersion||!allowed(notice.CompanionId))return;
            bool enabled=await Task.Run(delegate{
                try{return runtime.Service.GetSnapshot(notice.CompanionId).Settings.FeedbackEnabled;}
                catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is System.Runtime.Serialization.SerializationException||error is ArgumentException))throw;return false;}
            });
            if(!disposed&&enabled&&runtime.FeedbackEnabled&&suppressionVersion==runtime.FeedbackSuppressionVersion&&allowed(notice.CompanionId))show(notice);
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;runtime.Committed-=Committed;runtime.FeedbackSettingsChanged-=SettingsChanged;if(timer!=null)timer.Dispose();timer=null;pending=null;}}
    }
}
