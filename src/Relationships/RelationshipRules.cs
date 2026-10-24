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
                state = new CompanionRelationshipState { CompanionId = id, FirstMetUtc = time.UtcNow, LastSeenUtc = time.UtcNow, FirstLocalDate = date, BudgetLocalDate = date, DateHighWater = date, OffsetMinutes = (int)time.LocalOffset.TotalMinutes, ActiveLocalDates = new List<string> { date }, Receipts = new List<RelationshipReceipt>(), Milestones = new List<RelationshipMilestone>() };
                data.Companions.Add(id, state);
            } else {
                if (state.Receipts.Any(r => r.EventId == token) || time.UtcNow < state.LastSeenUtc) return false;
                if ((int)time.LocalOffset.TotalMinutes != state.OffsetMinutes) {
                    // A change of offset never creates budget or activity. Remember even future anchors.
                    state.OffsetMinutes = (int)time.LocalOffset.TotalMinutes; state.BudgetLocalDate = date;
                    if (String.CompareOrdinal(date, state.DateHighWater) > 0) state.DateHighWater = date;
                } else if (String.CompareOrdinal(date, state.DateHighWater) > 0) {
                    state.BudgetLocalDate = state.DateHighWater = date;
                    state.DailyTotal = state.VisitPoints = state.InteractionPoints = state.ConversationPoints = state.WorkPoints = 0;
                    state.ActiveLocalDates.Add(date);
                }
                // Keep the observed anchor current even while a previously claimed date blocks reset.
                state.BudgetLocalDate = date;
                state.LastSeenUtc = time.UtcNow;
            }
            int points = 0;
            switch (type) {
                case RelationshipEventType.Visit: points = state.VisitPoints == 0 ? 2 : 0; break;
                case RelationshipEventType.UserInteraction:
                    if (!state.LastRewardedInteractionUtc.HasValue || (time.UtcNow - state.LastRewardedInteractionUtc.Value).TotalSeconds >= 20) points = Math.Min(1, 6 - state.InteractionPoints);
                    break;
                case RelationshipEventType.CompletedConversation: points = Math.Min(2, 8 - state.ConversationPoints); break;
                case RelationshipEventType.SuccessfulWorkTask: points = Math.Min(4, 8 - state.WorkPoints); break;
            }
            awarded = Math.Min(points, Math.Min(20 - state.DailyTotal, 1000 - state.Score));
            state.Score += awarded; state.DailyTotal += awarded;
            switch (type) {
                case RelationshipEventType.Visit: state.VisitPoints += awarded; break;
                case RelationshipEventType.UserInteraction: state.InteractionPoints += awarded; if (awarded > 0) state.LastRewardedInteractionUtc = time.UtcNow; break;
                case RelationshipEventType.CompletedConversation: state.ConversationPoints += awarded; break;
                case RelationshipEventType.SuccessfulWorkTask: state.WorkPoints += awarded; break;
            }
            state.Receipts.Add(new RelationshipReceipt { EventId = token, EventType = type, Utc = time.UtcNow, AwardedPoints = awarded });
            for (int i = 0; i < Stages.Length; i++) if (state.Score >= Thresholds[i] && !state.Milestones.Any(m => m.StageId == Stages[i]))
                state.Milestones.Add(new RelationshipMilestone { StageId = Stages[i], EarnedUtc = time.UtcNow, EarnedLocalDate = date });
            return true;
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
                var ids = new HashSet<string>(StringComparer.Ordinal); long sum = 0; DateTime receiptUtc = s.FirstMetUtc; DateTime? lastInteraction = null;
                foreach (var receipt in s.Receipts) {
                    if (receipt == null || !ValidEventId(receipt.EventId) || !ids.Add(receipt.EventId) || !ValidType(receipt.EventType) || !Utc(receipt.Utc) || receipt.Utc < receiptUtc || receipt.Utc > s.LastSeenUtc || receipt.AwardedPoints < 0 || receipt.AwardedPoints > (receipt.EventType == RelationshipEventType.Visit || receipt.EventType == RelationshipEventType.CompletedConversation ? 2 : receipt.EventType == RelationshipEventType.UserInteraction ? 1 : 4)) throw Invalid();
                    sum += receipt.AwardedPoints; receiptUtc = receipt.Utc;
                    if (receipt.EventType == RelationshipEventType.UserInteraction && receipt.AwardedPoints > 0) lastInteraction = receipt.Utc;
                }
                if (sum != s.Score) throw Invalid();
                if (s.Receipts[0].Utc != s.FirstMetUtc || receiptUtc != s.LastSeenUtc) throw Invalid();
                if (lastInteraction != s.LastRewardedInteractionUtc) throw Invalid();
                int count = 0; DateTime earned = s.FirstMetUtc;
                for (int i = 0; i < Stages.Length && s.Score >= Thresholds[i]; i++) {
                    if (count >= s.Milestones.Count) throw Invalid(); var m = s.Milestones[count++];
                    if (m == null || m.StageId != Stages[i] || !Utc(m.EarnedUtc) || m.EarnedUtc < earned || m.EarnedUtc > s.LastSeenUtc || !Date(m.EarnedLocalDate) || !LocalNearUtc(m.EarnedLocalDate, m.EarnedUtc)) throw Invalid();
                    earned = m.EarnedUtc;
                }
                if (count != s.Milestones.Count) throw Invalid();
            }
        }
        static bool Utc(DateTime value) { return value.Kind == DateTimeKind.Utc && value.Year >= 1970 && value.Year <= 9998; }
        static bool Date(string value) { DateTime date; return value != null && value.Length == 10 && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date.Year >= 1969 && date.Year <= 9999; }
        static bool LocalNearUtc(string date, DateTime utc) { return Math.Abs((DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture) - utc.Date).TotalDays) <= 1; }
        static InvalidDataException Invalid() { return new InvalidDataException("Invalid relationship payload; existing data is preserved."); }
    }
}
