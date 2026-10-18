using System;
namespace Dafeiyu
{
    // A request ledger contains only accounting metadata, never request/response bodies.
    public sealed class UsageRecord
    {
        public string RequestId, Provider, Model, Feature, ConversationId, ProjectId, Status;
        public DateTime TimestampUtc;
        public long? InputTokens, OutputTokens, CachedInputTokens, ReasoningTokens, TotalTokens;
        public bool IsEstimated;
        internal UsageRecord Copy() { return (UsageRecord)MemberwiseClone(); }
        internal static long? Max(long? a,long? b) { return !a.HasValue?b:!b.HasValue?a:Math.Max(a.Value,b.Value); }
        internal static long? Sum(long? a,long? b) { return a.HasValue && b.HasValue && a.Value<=Int64.MaxValue-b.Value?a.Value+b.Value:(long?)null; }
    }
}
