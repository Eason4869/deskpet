using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Dafeiyu
{
    // Presentation derives from the ledger's rules; neither desktop host owns thresholds.
    public sealed class RelationshipProgress
    {
        public int Score { get; private set; }
        public string StageId { get; private set; }
        public string NextStageId { get; private set; }
        public int? NextThreshold { get; private set; }
        public int RemainingPoints { get; private set; }
        public double Fraction {get{return (double)Score/(NextThreshold??1000);}}
        public RelationshipStageNode[] Nodes {get{return Array.ConvertAll(RelationshipRules.Stages,delegate(string id){int index=Array.IndexOf(RelationshipRules.Stages,id),current=Array.IndexOf(RelationshipRules.Stages,StageId);return new RelationshipStageNode{StageId=id,Completed=index<current,Current=index==current};});}}
        public static RelationshipProgress FromScore(int score)
        {
            var stage = RelationshipRules.StageForScore(score);
            int index = Array.IndexOf(RelationshipRules.Stages, stage);
            bool next = index + 1 < RelationshipRules.Stages.Length;
            return new RelationshipProgress { Score = score, StageId = stage,
                NextStageId = next ? RelationshipRules.Stages[index + 1] : null,
                NextThreshold = next ? (int?)RelationshipRules.Thresholds[index + 1] : null,
                RemainingPoints = next ? RelationshipRules.Thresholds[index + 1] - score : 0 };
        }
        public string ScoreText { get { return Score.ToString(CultureInfo.CurrentCulture) + " / " + (NextThreshold ?? 1000).ToString(CultureInfo.CurrentCulture); } }
        public string NextText { get { return NextStageId == null ? TextResources.Get("relationship.home.maximum") : TextResources.Format("relationship.home.next", RemainingPoints, RelationshipContent.StageName(NextStageId)); } }
    }
    public sealed class RelationshipStageNode
    {
        public string StageId {get;internal set;}
        public bool Completed {get;internal set;}
        public bool Current {get;internal set;}
    }

    public sealed class RelationshipHomeView
    {
        public string CompanionId { get; internal set; }
        public RelationshipSnapshot Snapshot { get; internal set; }
        public bool ReadFailed { get; internal set; }
        public string ProfileArtStageId
        {
            get {
                var state=Snapshot==null?null:Snapshot.State;
                if(state==null||String.IsNullOrEmpty(CompanionId)||state.CompanionId!=CompanionId||state.Milestones==null||state.Score<0||state.Score>1000)return null;
                for(int index=RelationshipRules.Stages.Length-1;index>=0;index--){
                    string stage=RelationshipRules.Stages[index];RelationshipStageArtAsset asset;
                    if(state.Score>=RelationshipRules.Thresholds[index]&&state.Milestones.Exists(m=>m!=null&&m.StageId==stage)&&RelationshipStageArt.TryGet(CompanionId,stage,out asset))return stage;
                }
                return null;
            }
        }
        public RelationshipProgress Progress { get; internal set; }
        public int ActiveDays { get; internal set; }
        public int AcquaintanceDays { get; internal set; }
        public int TodayPoints { get; internal set; }
        public bool GrowthPaused {get{return Snapshot!=null&&!Snapshot.Settings.GrowthEnabled;}}
        public bool DailyLimitReached {get{return Snapshot!=null&&TodayPoints>=20;}}
        public bool MaximumReached {get{return Progress!=null&&Progress.NextStageId==null;}}
        public IEnumerable<string> StatusTexts
        {
            get {
                if(GrowthPaused)yield return TextResources.Get("relationship.home.growthPaused");
                if(DailyLimitReached)yield return TextResources.Get("relationship.home.dailyLimit");
                if(MaximumReached)yield return TextResources.Get("relationship.home.maximum");
            }
        }
    }

    // All contended store reads/writes run on workers. A failed read carries only the
    // last successful snapshot for THIS identity; null is unknown, never a fresh meeting.
    public sealed class RelationshipHomeModel
    {
        readonly RelationshipRuntime runtime;
        readonly Dictionary<string, RelationshipSnapshot> saved = new Dictionary<string, RelationshipSnapshot>(StringComparer.Ordinal);
        readonly object gate = new object();
        readonly object settingsAdmission = new object();
        Task settingsWorker = Task.FromResult(0);
        RelationshipSettings pendingSettings;
        public RelationshipHomeModel(RelationshipRuntime runtime) { if (runtime == null) throw new ArgumentNullException("runtime"); this.runtime = runtime; }
        public Task<RelationshipHomeView> LoadAsync(string id, RelationshipTime time)
        {
            return Task.Run(delegate {
                RelationshipSnapshot snapshot = null; bool failed = false;
                try { snapshot = runtime.Service.GetSnapshot(id); lock (gate) saved[id] = snapshot; }
                catch (Exception error) {
                    if (!(error is System.IO.IOException || error is System.IO.InvalidDataException || error is UnauthorizedAccessException || error is System.Runtime.Serialization.SerializationException || error is ArgumentException)) throw;
                    failed = true; lock (gate) saved.TryGetValue(id, out snapshot);
                }
                var state = snapshot == null ? null : snapshot.State;
                return new RelationshipHomeView { CompanionId = id, Snapshot = snapshot, ReadFailed = failed,
                    Progress = snapshot == null ? null : RelationshipProgress.FromScore(state == null ? 0 : state.Score),
                    ActiveDays = state == null ? 0 : state.ActiveLocalDates.Count,
                    AcquaintanceDays = snapshot == null ? 0 : snapshot.AcquaintanceDays(time),
                    TodayPoints = RelationshipRules.TodayPoints(state, time) };
            });
        }
        public Task<RelationshipResult> UpdateSettingsAsync(bool growth, bool feedback, bool aiContext)
        {
            return QueueSettings(request => Task.FromResult(SaveSettings(request,growth,feedback,aiContext)),delegate{RememberSettings(growth,feedback,aiContext);});
        }
        public Task<RelationshipResult> RetryAsync() { return QueueSettings(RetryCoreAsync); }
        Task<RelationshipResult> QueueSettings(Func<RelationshipRuntime.SettingsRequest,Task<RelationshipResult>> operation,Action rejected=null)
        {
            // Admission is ordered synchronously, before any retry awaits the runtime.
            // Work always starts on a worker; this lock never covers disk I/O.
            lock(settingsAdmission){
                var request=runtime.TryBeginSettingsRequest();
                if(request==null){if(rejected!=null)rejected();return Task.FromResult(new RelationshipResult{Success=false});}
                var next=settingsWorker.ContinueWith(async delegate{
                    using(request){try{return await operation(request).ConfigureAwait(false);}catch(Exception){return new RelationshipResult{Success=false};}}
                },TaskScheduler.Default).Unwrap();
                settingsWorker=next;return next;
            }
        }
        void RememberSettings(bool growth,bool feedback,bool aiContext)
        {lock(gate)pendingSettings=new RelationshipSettings{GrowthEnabled=growth,FeedbackEnabled=feedback,AiContextEnabled=aiContext};}
        RelationshipResult SaveSettings(RelationshipRuntime.SettingsRequest request,bool growth,bool feedback,bool aiContext)
        {
            bool saved=false;
            try{var result=request.Save(growth,feedback,aiContext);saved=result.Success;return result;}
            finally{if(saved){lock(gate)pendingSettings=null;}else RememberSettings(growth,feedback,aiContext);}
        }
        async Task<RelationshipResult> RetryCoreAsync(RelationshipRuntime.SettingsRequest request)
        {
            await request.RetryPendingAsync().ConfigureAwait(false);
            if(runtime.RequiresRestart||!String.IsNullOrEmpty(runtime.Warning))return new RelationshipResult{Success=false};
            RelationshipSettings intent;lock(gate)intent=pendingSettings;
            return intent==null?new RelationshipResult{Success=true}:SaveSettings(request,intent.GrowthEnabled,intent.FeedbackEnabled,intent.AiContextEnabled);
        }
    }
}
