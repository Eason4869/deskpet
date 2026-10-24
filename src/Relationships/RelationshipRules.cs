using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Dafeiyu
{
    public static class RelationshipRules
    {
        internal static readonly string[] Stages = { "first_meeting", "familiar", "close", "trusted", "heartfelt", "best_friend" };
        internal static readonly int[] Thresholds = { 0, 80, 240, 480, 760, 1000 };
        public static string StageForScore(int score)
        {
            if (score < 0 || score > 1000) throw new ArgumentOutOfRangeException("score");
            for (int i = Thresholds.Length - 1; i >= 0; i--) if (score >= Thresholds[i]) return Stages[i];
            return Stages[0];
        }
        internal static bool ValidEventId(string id) { return !String.IsNullOrWhiteSpace(id) && id.Length <= 200 && !id.Any(Char.IsControl); }
        internal static bool ValidType(RelationshipEventType type) { return type >= RelationshipEventType.Visit && type <= RelationshipEventType.SuccessfulWorkTask; }
        internal static bool Apply(RelationshipData data, string id, RelationshipEventType type, string token, RelationshipTime time, out int awarded)
        {
            awarded = 0;
            if (!data.Settings.GrowthEnabled) return false;
            CompanionRelationshipState state;
            bool fresh = !data.Companions.TryGetValue(id, out state);
            string date = time.LocalDate;
            if (fresh) {
                state = NewState(id, time);
                data.Companions.Add(id, state);
            } else {
                if (state.Receipts.Any(r => r.EventId == token) || time.UtcNow < state.LastSeenUtc) return false;
                AdvanceCalendar(state, time);
            }
            awarded = Award(state, type, time.UtcNow);
            state.Receipts.Add(new RelationshipReceipt { EventId = token, EventType = type, Utc = time.UtcNow, AwardedPoints = awarded, OffsetMinutes = (int)time.LocalOffset.TotalMinutes, BudgetEpoch = state.BudgetEpoch });
            for (int i = state.Milestones.Count; i < Stages.Length && state.Score >= Thresholds[i]; i++)
                state.Milestones.Add(new RelationshipMilestone { StageId = Stages[i], EarnedUtc = time.UtcNow, EarnedLocalDate = date });
            return true;
        }
        static CompanionRelationshipState NewState(string id, RelationshipTime time)
        {
            string date = time.LocalDate;
            return new CompanionRelationshipState { CompanionId = id, FirstMetUtc = time.UtcNow, LastSeenUtc = time.UtcNow, FirstLocalDate = date, BudgetLocalDate = date, DateHighWater = date, OffsetMinutes = (int)time.LocalOffset.TotalMinutes, ActiveLocalDates = new List<string> { date }, Receipts = new List<RelationshipReceipt>(), Milestones = new List<RelationshipMilestone>() };
        }
        static void AdvanceCalendar(CompanionRelationshipState state, RelationshipTime time)
        {
            string date = time.LocalDate;
            string naturalDate = time.UtcNow.AddMinutes(state.OffsetMinutes).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string previousDate = state.LastSeenUtc.AddMinutes(state.OffsetMinutes).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            bool naturalDayAdvanced = String.CompareOrdinal(naturalDate, previousDate) > 0;
            // Activity follows a naturally elapsed midnight in the previous zone. Same-instant
            // travel cannot create activity, while a real later day still counts through DST/travel.
            if (naturalDayAdvanced && String.CompareOrdinal(date, state.ActiveLocalDates[state.ActiveLocalDates.Count - 1]) > 0)
                state.ActiveLocalDates.Add(date);
            // Only a naturally elapsed midnight and a previously unclaimed date start a new
            // budget. Offset-only shifts carry counters; real new days refresh through DST/travel.
            if (naturalDayAdvanced && String.CompareOrdinal(date, state.DateHighWater) > 0) {
                state.BudgetEpoch++;
                state.DailyTotal = state.VisitPoints = state.InteractionPoints = state.ConversationPoints = state.WorkPoints = 0;
            }
            if (String.CompareOrdinal(date, state.DateHighWater) > 0) state.DateHighWater = date;
            state.OffsetMinutes = (int)time.LocalOffset.TotalMinutes;
            state.BudgetLocalDate = date; state.LastSeenUtc = time.UtcNow;
        }
        static int Award(CompanionRelationshipState state, RelationshipEventType type, DateTime utc)
        {
            int points = 0;
            switch (type) {
                case RelationshipEventType.Visit: points = state.VisitPoints == 0 ? 2 : 0; break;
                case RelationshipEventType.UserInteraction:
                    if (!state.LastRewardedInteractionUtc.HasValue || (utc - state.LastRewardedInteractionUtc.Value).TotalSeconds >= 20) points = Math.Min(1, 6 - state.InteractionPoints);
                    break;
                case RelationshipEventType.CompletedConversation: points = Math.Min(2, 8 - state.ConversationPoints); break;
                case RelationshipEventType.SuccessfulWorkTask: points = Math.Min(4, 8 - state.WorkPoints); break;
            }
            int awarded = Math.Min(points, Math.Min(20 - state.DailyTotal, 1000 - state.Score));
            state.Score += awarded; state.DailyTotal += awarded;
            switch (type) {
                case RelationshipEventType.Visit: state.VisitPoints += awarded; break;
                case RelationshipEventType.UserInteraction: state.InteractionPoints += awarded; if (awarded > 0) state.LastRewardedInteractionUtc = utc; break;
                case RelationshipEventType.CompletedConversation: state.ConversationPoints += awarded; break;
                case RelationshipEventType.SuccessfulWorkTask: state.WorkPoints += awarded; break;
            }
            return awarded;
        }
        internal static void Validate(RelationshipData data)
        {
            if (data == null || data.SchemaVersion != 1 || data.Settings == null || data.Companions == null || data.Companions.Count > 4096) throw Invalid();
            foreach (var pair in data.Companions) {
                var s = pair.Value;
                if (!RelationshipIdentity.IsValid(pair.Key) || s == null || s.CompanionId != pair.Key || s.Score < 0 || s.Score > 1000 || !Utc(s.FirstMetUtc) || !Utc(s.LastSeenUtc) || s.LastSeenUtc < s.FirstMetUtc || !Date(s.FirstLocalDate) || !Date(s.BudgetLocalDate) || !Date(s.DateHighWater) || Math.Abs((long)s.OffsetMinutes) > 840 || String.CompareOrdinal(s.BudgetLocalDate, s.DateHighWater) > 0 || String.CompareOrdinal(s.FirstLocalDate, s.DateHighWater) > 0) throw Invalid();
                if (!LocalNearUtc(s.FirstLocalDate, s.FirstMetUtc) || s.BudgetLocalDate != s.LastSeenUtc.AddMinutes(s.OffsetMinutes).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) throw Invalid();
                if (s.DailyTotal < 0 || s.DailyTotal > 20 || s.VisitPoints < 0 || s.VisitPoints > 2 || s.InteractionPoints < 0 || s.InteractionPoints > 6 || s.ConversationPoints < 0 || s.ConversationPoints > 8 || s.WorkPoints < 0 || s.WorkPoints > 8 || s.DailyTotal != s.VisitPoints + s.InteractionPoints + s.ConversationPoints + s.WorkPoints || s.DailyTotal > s.Score) throw Invalid();
                if (s.ActiveLocalDates == null || s.ActiveLocalDates.Count == 0 || s.ActiveLocalDates.Count > 100000 || s.ActiveLocalDates[0] != s.FirstLocalDate || s.ActiveLocalDates.Distinct(StringComparer.Ordinal).Count() != s.ActiveLocalDates.Count) throw Invalid();
                string previous = null;
                foreach (var date in s.ActiveLocalDates) { if (!Date(date) || String.CompareOrdinal(date, s.FirstLocalDate) < 0 || String.CompareOrdinal(date, s.DateHighWater) > 0 || previous != null && String.CompareOrdinal(date, previous) <= 0) throw Invalid(); previous = date; }
                if (s.LastRewardedInteractionUtc.HasValue && (!Utc(s.LastRewardedInteractionUtc.Value) || s.LastRewardedInteractionUtc < s.FirstMetUtc || s.LastRewardedInteractionUtc > s.LastSeenUtc)) throw Invalid();
                if (s.Receipts == null || s.Receipts.Count == 0 || s.Receipts.Count > 100000 || s.Milestones == null || s.Milestones.Count > Stages.Length) throw Invalid();
                // Rebuild counters in one pass. Do not call Apply here: its live-event duplicate
                // search would turn replay into O(n squared) for a bounded but large receipt ledger.
                var ids = new HashSet<string>(StringComparer.Ordinal); DateTime receiptUtc = s.FirstMetUtc;
                CompanionRelationshipState replay = null;
                var crossings = new RelationshipReceipt[Stages.Length]; int crossedCount = 0;
                foreach (var receipt in s.Receipts) {
                    if (receipt == null || !ValidEventId(receipt.EventId) || !ids.Add(receipt.EventId) || !ValidType(receipt.EventType) || !Utc(receipt.Utc) || receipt.Utc < receiptUtc || receipt.Utc > s.LastSeenUtc || Math.Abs((long)receipt.OffsetMinutes) > 840 || receipt.BudgetEpoch < 0) throw Invalid();
                    var time = new RelationshipTime(receipt.Utc, TimeSpan.FromMinutes(receipt.OffsetMinutes));
                    if (replay == null) replay = NewState(pair.Key, time); else AdvanceCalendar(replay, time);
                    if (receipt.BudgetEpoch != replay.BudgetEpoch || receipt.AwardedPoints != Award(replay, receipt.EventType, receipt.Utc)) throw Invalid();
                    while (crossedCount < Stages.Length && replay.Score >= Thresholds[crossedCount]) crossings[crossedCount++] = receipt;
                    receiptUtc = receipt.Utc;
                }
                if (s.Receipts[0].Utc != s.FirstMetUtc || receiptUtc != s.LastSeenUtc || s.Score != replay.Score || s.FirstLocalDate != replay.FirstLocalDate || s.BudgetLocalDate != replay.BudgetLocalDate || s.DateHighWater != replay.DateHighWater || s.OffsetMinutes != replay.OffsetMinutes || s.BudgetEpoch != replay.BudgetEpoch || s.DailyTotal != replay.DailyTotal || s.VisitPoints != replay.VisitPoints || s.InteractionPoints != replay.InteractionPoints || s.ConversationPoints != replay.ConversationPoints || s.WorkPoints != replay.WorkPoints || s.LastRewardedInteractionUtc != replay.LastRewardedInteractionUtc || !s.ActiveLocalDates.SequenceEqual(replay.ActiveLocalDates)) throw Invalid();
                if (s.Milestones.Count != crossedCount) throw Invalid();
                for (int i = 0; i < crossedCount; i++) {
                    var milestone = s.Milestones[i]; var crossing = crossings[i];
                    if (milestone == null || milestone.StageId != Stages[i] || milestone.EarnedUtc != crossing.Utc || milestone.EarnedLocalDate != crossing.LocalDate) throw Invalid();
                }
            }
        }
        static bool Utc(DateTime value) { return value.Kind == DateTimeKind.Utc && value.Year >= 1970 && value.Year <= 9998; }
        static bool Date(string value) { DateTime date; return value != null && value.Length == 10 && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date.Year >= 1969 && date.Year <= 9999; }
        static bool LocalNearUtc(string date, DateTime utc) { return Math.Abs((DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture) - utc.Date).TotalDays) <= 1; }
        static InvalidDataException Invalid() { return new InvalidDataException("Invalid relationship payload; existing data is preserved."); }
    }
}
