using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Dafeiyu
{
    public sealed class RelationshipService
    {
        readonly RelationshipStore store;
        public RelationshipService(RelationshipStore store) { if (store == null) throw new ArgumentNullException("store"); this.store = store; }
        public RelationshipSnapshot GetSnapshot(string companionId)
        {
            RequireIdentity(companionId);
            using (store.Acquire()) return Snapshot(store.Load(), companionId);
        }
        public RelationshipResult Record(string companionId, RelationshipEventType eventType, string logicalEventId, RelationshipTime time)
        {
            if (!RelationshipIdentity.IsValid(companionId) || !RelationshipRules.ValidType(eventType) || !RelationshipRules.ValidEventId(logicalEventId) || time == null)
                return new RelationshipResult { Success = false, Error = "Invalid relationship event." };
            return Execute(companionId, delegate(RelationshipData next, RelationshipResult result) {
                int points; bool changed = RelationshipRules.Apply(next, companionId, eventType, logicalEventId, time, out points); result.AwardedPoints = points; return changed;
            });
        }
        public RelationshipResult UpdateSettings(bool growthEnabled, bool feedbackEnabled, bool aiContextEnabled)
        {
            return Execute(null, delegate(RelationshipData next, RelationshipResult result) {
                next.Settings.GrowthEnabled = growthEnabled; next.Settings.FeedbackEnabled = feedbackEnabled; next.Settings.AiContextEnabled = aiContextEnabled; return true;
            });
        }
        public RelationshipResult ClaimMilestone(string companionId, string stageId)
        {
            if (!RelationshipIdentity.IsValid(companionId) || !RelationshipRules.Stages.Contains(stageId)) return new RelationshipResult { Success = false, Error = "Invalid milestone claim." };
            return Execute(companionId, delegate(RelationshipData next, RelationshipResult result) {
                CompanionRelationshipState state;
                if (!next.Companions.TryGetValue(companionId, out state)) return false;
                var milestone = state.Milestones.FirstOrDefault(m => m.StageId == stageId);
                if (milestone == null || milestone.NotificationClaimed) return false;
                milestone.NotificationClaimed = true; result.ClaimedNotification = true; return true;
            });
        }
        RelationshipResult Execute(string id, Func<RelationshipData, RelationshipResult, bool> mutate)
        {
            RelationshipData previous = null;
            try {
                using (store.Acquire()) {
                    previous = store.Load(); var next = RelationshipStore.Copy(previous); var result = new RelationshipResult { Success = true };
                    if (mutate(next, result)) store.Save(next);
                    result.Snapshot = Snapshot(next, id); return result;
                }
            } catch (Exception error) {
                if (!(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is SerializationException || error is ArgumentException)) throw;
                return new RelationshipResult { Success = false, Error = error.Message, Snapshot = previous == null ? null : Snapshot(previous, id) };
            }
        }
        static RelationshipSnapshot Snapshot(RelationshipData data, string id)
        {
            CompanionRelationshipState state = null; if (id != null) data.Companions.TryGetValue(id, out state);
            return new RelationshipSnapshot { Settings = data.Settings, State = state };
        }
        static void RequireIdentity(string id) { if (!RelationshipIdentity.IsValid(id)) throw new InvalidDataException("Invalid relationship companion identity."); }
    }
}
