using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace Dafeiyu
{
    public enum RelationshipEventType { Visit, UserInteraction, CompletedConversation, SuccessfulWorkTask }

    // JSON's legacy DateTime encoding loses submillisecond ticks. Store UTC ticks so restart
    // cannot turn a tiny rollback or a 19.9995-second interval into an eligible reward.
    internal static class RelationshipTimestamp
    {
        internal static long Ticks(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required."); return value.Ticks; }
    }

    public sealed class RelationshipTime
    {
        public DateTime UtcNow { get; private set; }
        public TimeSpan LocalOffset { get; private set; }
        public RelationshipTime(DateTime utcNow, TimeSpan localOffset)
        {
            if (utcNow.Kind != DateTimeKind.Utc || utcNow.Year < 1970 || utcNow.Year > 9998 || localOffset.TotalMinutes % 1 != 0 || Math.Abs(localOffset.TotalMinutes) > 840)
                throw new ArgumentException("A bounded UTC instant and whole-minute local offset are required.");
            UtcNow = utcNow; LocalOffset = localOffset;
        }
        public static RelationshipTime Current() { var now = DateTimeOffset.Now; return new RelationshipTime(now.UtcDateTime, now.Offset); }
        public RelationshipTime AddSeconds(double seconds) { return new RelationshipTime(UtcNow.AddSeconds(seconds), LocalOffset); }
        public string LocalDate { get { return UtcNow.Add(LocalOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }
    }
    [DataContract] public sealed class RelationshipSettings
    {
        [DataMember(IsRequired = true)] public bool GrowthEnabled { get; set; }
        [DataMember(IsRequired = true)] public bool FeedbackEnabled { get; set; }
        [DataMember(IsRequired = true)] public bool AiContextEnabled { get; set; }
        public RelationshipSettings() { GrowthEnabled = FeedbackEnabled = AiContextEnabled = true; }
    }
    [DataContract] public sealed class RelationshipReceipt
    {
        [DataMember(IsRequired = true)] public string EventId { get; set; }
        [DataMember(IsRequired = true)] public RelationshipEventType EventType { get; set; }
        [DataMember(Name = "Utc", IsRequired = true)] long utcTicks;
        public DateTime Utc { get { return new DateTime(utcTicks, DateTimeKind.Utc); } set { utcTicks = RelationshipTimestamp.Ticks(value); } }
        [DataMember(IsRequired = true)] public int AwardedPoints { get; set; }
        [DataMember(IsRequired = true)] public int OffsetMinutes { get; set; }
        [DataMember(IsRequired = true)] public int BudgetEpoch { get; set; }
        public string LocalDate { get { return Utc.AddMinutes(OffsetMinutes).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }
    }
    [DataContract] public sealed class RelationshipMilestone
    {
        [DataMember(IsRequired = true)] public string StageId { get; set; }
        [DataMember(Name = "EarnedUtc", IsRequired = true)] long earnedUtcTicks;
        public DateTime EarnedUtc { get { return new DateTime(earnedUtcTicks, DateTimeKind.Utc); } set { earnedUtcTicks = RelationshipTimestamp.Ticks(value); } }
        [DataMember(IsRequired = true)] public string EarnedLocalDate { get; set; }
        [DataMember(IsRequired = true)] public bool NotificationClaimed { get; set; }
    }
    [DataContract] public sealed class CompanionRelationshipState
    {
        [DataMember(IsRequired = true)] public string CompanionId { get; set; }
        [DataMember(IsRequired = true)] public int Score { get; set; }
        [DataMember(Name = "FirstMetUtc", IsRequired = true)] long firstMetUtcTicks;
        public DateTime FirstMetUtc { get { return new DateTime(firstMetUtcTicks, DateTimeKind.Utc); } set { firstMetUtcTicks = RelationshipTimestamp.Ticks(value); } }
        [DataMember(IsRequired = true)] public string FirstLocalDate { get; set; }
        [DataMember(Name = "LastSeenUtc", IsRequired = true)] long lastSeenUtcTicks;
        public DateTime LastSeenUtc { get { return new DateTime(lastSeenUtcTicks, DateTimeKind.Utc); } set { lastSeenUtcTicks = RelationshipTimestamp.Ticks(value); } }
        [DataMember(IsRequired = true)] public List<string> ActiveLocalDates { get; set; }
        [DataMember(IsRequired = true)] public string BudgetLocalDate { get; set; }
        [DataMember(IsRequired = true)] public string DateHighWater { get; set; }
        [DataMember(IsRequired = true)] public int OffsetMinutes { get; set; }
        [DataMember(IsRequired = true)] public int DailyTotal { get; set; }
        [DataMember(IsRequired = true)] public int VisitPoints { get; set; }
        [DataMember(IsRequired = true)] public int InteractionPoints { get; set; }
        [DataMember(IsRequired = true)] public int ConversationPoints { get; set; }
        [DataMember(IsRequired = true)] public int WorkPoints { get; set; }
        [DataMember(IsRequired = true)] internal int BudgetEpoch { get; set; }
        [DataMember(Name = "LastRewardedInteractionUtc", IsRequired = true)] long? lastRewardedInteractionUtcTicks;
        public DateTime? LastRewardedInteractionUtc { get { return lastRewardedInteractionUtcTicks.HasValue ? (DateTime?)new DateTime(lastRewardedInteractionUtcTicks.Value, DateTimeKind.Utc) : null; } set { lastRewardedInteractionUtcTicks = value.HasValue ? (long?)RelationshipTimestamp.Ticks(value.Value) : null; } }
        [DataMember(IsRequired = true)] public List<RelationshipReceipt> Receipts { get; set; }
        [DataMember(IsRequired = true)] public List<RelationshipMilestone> Milestones { get; set; }
    }
    [DataContract] internal sealed class RelationshipData
    {
        [DataMember(IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(IsRequired = true)] public RelationshipSettings Settings { get; set; }
        [DataMember(IsRequired = true)] public Dictionary<string, CompanionRelationshipState> Companions { get; set; }
        public RelationshipData() { SchemaVersion = 1; Settings = new RelationshipSettings(); Companions = new Dictionary<string, CompanionRelationshipState>(StringComparer.Ordinal); }
    }
    public sealed class RelationshipSnapshot
    {
        public RelationshipSettings Settings { get; internal set; }
        public CompanionRelationshipState State { get; internal set; }
        public string StageId { get { return RelationshipRules.StageForScore(State == null ? 0 : State.Score); } }
        public int AcquaintanceDays(RelationshipTime time)
        {
            if (State == null) return 0;
            return Math.Max(1, (int)(DateTime.ParseExact(time.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) - DateTime.ParseExact(State.FirstLocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)).TotalDays + 1);
        }
    }
    public sealed class RelationshipResult
    {
        // Presentation admission only; never part of saved relationship data.
        internal int? FeedbackSuppressionVersion;
        public bool Success { get; internal set; }
        public int AwardedPoints { get; internal set; }
        public bool ClaimedNotification { get; internal set; }
        public string Error { get; internal set; }
        public RelationshipSnapshot Snapshot { get; internal set; }
    }
}
