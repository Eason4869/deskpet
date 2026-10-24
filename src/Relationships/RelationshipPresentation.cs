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

    public sealed class RelationshipHomeView
    {
        public string CompanionId { get; internal set; }
        public RelationshipSnapshot Snapshot { get; internal set; }
        public bool ReadFailed { get; internal set; }
        public RelationshipProgress Progress { get; internal set; }
        public int ActiveDays { get; internal set; }
        public int AcquaintanceDays { get; internal set; }
        public int TodayPoints { get; internal set; }
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
                    if (!(error is System.IO.IOException || error is UnauthorizedAccessException || error is System.Runtime.Serialization.SerializationException || error is ArgumentException)) throw;
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
            return QueueSettings(() => Task.FromResult(SaveSettings(growth,feedback,aiContext)));
        }
        public Task<RelationshipResult> RetryAsync() { return QueueSettings(RetryCoreAsync); }
        Task<RelationshipResult> QueueSettings(Func<Task<RelationshipResult>> operation)
        {
            // Admission is ordered synchronously, before any retry awaits the runtime.
            // Work always starts on a worker; this lock never covers disk I/O.
            lock(settingsAdmission){
                var next=settingsWorker.ContinueWith(delegate{return operation();},TaskScheduler.Default).Unwrap();
                settingsWorker=next;return next;
            }
        }
        RelationshipResult SaveSettings(bool growth,bool feedback,bool aiContext)
        {
            var result=runtime.SaveSettings(growth,feedback,aiContext);
            lock(gate)pendingSettings=result.Success?null:new RelationshipSettings{GrowthEnabled=growth,FeedbackEnabled=feedback,AiContextEnabled=aiContext};
            return result;
        }
        async Task<RelationshipResult> RetryCoreAsync()
        {
            await runtime.RetryPendingAsync().ConfigureAwait(false);
            RelationshipSettings intent;lock(gate)intent=pendingSettings;
            return intent==null?new RelationshipResult{Success=true}:SaveSettings(intent.GrowthEnabled,intent.FeedbackEnabled,intent.AiContextEnabled);
        }
    }
}
