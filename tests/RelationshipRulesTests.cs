using System;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class RelationshipRulesTests
    {
        static void Check(bool value, string label) { RelationshipFixture.Check(value, label); }
        public static int Main()
        {
            try {
                ClockCases();
                Check(RelationshipRules.StageForScore(79) == "first_meeting" && RelationshipRules.StageForScore(80) == "familiar", "79/80 boundary");
                Check(RelationshipRules.StageForScore(239) == "familiar" && RelationshipRules.StageForScore(240) == "close", "239/240 boundary");
                Check(RelationshipRules.StageForScore(479) == "close" && RelationshipRules.StageForScore(480) == "trusted" && RelationshipRules.StageForScore(759) == "trusted" && RelationshipRules.StageForScore(760) == "heartfelt", "middle boundaries");
                Check(RelationshipRules.StageForScore(999) == "heartfelt" && RelationshipRules.StageForScore(1000) == "best_friend", "999/1000 boundary");
                using (var f = new RelationshipFixture()) {
                    var s = f.Service; var t = RelationshipFixture.Time("2026-10-24T12:00:00Z"); string id = "official:e-bao";
                    var first = s.RecordChecked(id, RelationshipEventType.Visit, "visit-1", t);
                    Check(first.AwardedPoints == 2, "visit +2: " + first.Error);
                    Check(s.RecordChecked(id, RelationshipEventType.Visit, "visit-2", t).AwardedPoints == 0, "one visit per day");
                    Check(s.RecordChecked(id, RelationshipEventType.UserInteraction, "tap-1", t).AwardedPoints == 1, "interaction +1");
                    Check(s.RecordChecked(id, RelationshipEventType.UserInteraction, "tap-fast", RelationshipFixture.Time("2026-10-24T12:00:19Z")).AwardedPoints == 0, "19 seconds rejected");
                    Check(s.RecordChecked(id, RelationshipEventType.UserInteraction, "tap-2", RelationshipFixture.Time("2026-10-24T12:00:20Z")).AwardedPoints == 1, "20 seconds accepted");
                    for (int n = 3; n <= 8; n++) s.RecordChecked(id, RelationshipEventType.UserInteraction, "tap-" + n, RelationshipFixture.Time("2026-10-24T12:10:00Z").AddSeconds(n * 20));
                    for (int n = 0; n < 6; n++) s.RecordChecked(id, RelationshipEventType.CompletedConversation, "chat-" + n, RelationshipFixture.Time("2026-10-24T13:00:00Z"));
                    Check(s.GetSnapshot(id).State.DailyTotal == 16 && s.GetSnapshot(id).State.InteractionPoints == 6 && s.GetSnapshot(id).State.ConversationPoints == 8, "category caps");
                    Check(s.RecordChecked(id, RelationshipEventType.SuccessfulWorkTask, "task", RelationshipFixture.Time("2026-10-24T14:00:00Z")).AwardedPoints == 4, "work separate reward");
                    Check(s.RecordChecked(id, RelationshipEventType.CompletedConversation, "task", RelationshipFixture.Time("2026-10-25T14:00:00Z")).AwardedPoints == 0, "same logical work cannot also reward conversation across days");
                    Check(s.GetSnapshot("official:marfu").State == null, "companions independent");
                    Check(!s.Record("bad/id", RelationshipEventType.Visit, "x", t).Success && !s.Record(id, (RelationshipEventType)99, "x", t).Success && !s.Record(id, RelationshipEventType.Visit, "", t).Success, "invalid events fail without growth");
                    s.UpdateSettings(false, false, false);
                    Check(s.RecordChecked(id, RelationshipEventType.Visit, "paused", RelationshipFixture.Time("2026-10-26T12:00:00Z")).AwardedPoints == 0 && s.GetSnapshot(id).State.Score == 20, "disabled growth retains data");
                    Check(!f.Service.GetSnapshot(id).Settings.GrowthEnabled && !f.Service.GetSnapshot(id).Settings.FeedbackEnabled && !f.Service.GetSnapshot(id).Settings.AiContextEnabled, "settings persisted");
                }
                using (var f = new RelationshipFixture()) {
                    var s = f.Service; string id = "official:e-bao"; var t = RelationshipFixture.Time("2026-10-24T23:59:00Z");
                    for (int n = 0; n < 3; n++) s.RecordChecked(id, RelationshipEventType.UserInteraction, "tap" + n, t.AddSeconds(n * 20));
                    for (int n = 0; n < 4; n++) s.RecordChecked(id, RelationshipEventType.CompletedConversation, "chat" + n, t.AddSeconds(40));
                    for (int n = 0; n < 2; n++) s.RecordChecked(id, RelationshipEventType.SuccessfulWorkTask, "work" + n, t.AddSeconds(40));
                    Check(s.GetSnapshot(id).State.DailyTotal == 19, "daily19 fixture actual " + s.GetSnapshot(id).State.DailyTotal);
                    Check(s.RecordChecked(id, RelationshipEventType.Visit, "last", t.AddSeconds(40)).AwardedPoints == 1 && s.GetSnapshot(id).State.DailyTotal == 20, "partial daily20");
                    Check(f.Service.RecordChecked(id, RelationshipEventType.Visit, "next", t.AddSeconds(60)).AwardedPoints == 2, "local midnight resets within24h");
                    Check(f.Service.RecordChecked(id, RelationshipEventType.Visit, "last", t.AddSeconds(86460)).AwardedPoints == 0, "receipt survives restart and day");
                }
                using (var f = new RelationshipFixture()) {
                    var s = f.Service; string id = "official:e-bao";
                    for (int day = 0; day < 51; day++) {
                        var time = new RelationshipTime(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(day), TimeSpan.Zero);
                        s.RecordChecked(id, RelationshipEventType.Visit, "v" + day, time);
                        for (int n = 0; n < 6; n++) s.RecordChecked(id, RelationshipEventType.UserInteraction, day + "i" + n, time.AddSeconds(n * 20));
                        for (int n = 0; n < 4; n++) s.RecordChecked(id, RelationshipEventType.CompletedConversation, day + "c" + n, time.AddSeconds(120));
                        s.RecordChecked(id, RelationshipEventType.SuccessfulWorkTask, "w" + day, time.AddSeconds(120));
                    }
                    var snap = s.GetSnapshot(id);
                    Check(snap.State.Score == 1000 && snap.StageId == "best_friend" && snap.State.ActiveLocalDates.Count == 51, "max score tracks actual active days");
                    Check(snap.State.Milestones.Count == 6 && snap.State.Milestones.Last().StageId == "best_friend", "earned stages retained");
                    Check(snap.AcquaintanceDays(RelationshipFixture.Time("2026-03-01T00:00:00Z")) == 60 && snap.State.ActiveLocalDates.Count == 51, "away time calendar span distinct from active days");
                    Check(snap.AcquaintanceDays(RelationshipFixture.Time("2025-01-01T00:00:00Z")) == 1, "backward clock span clamp");
                    var changedOffsetAtMaximum = f.Service.RecordChecked(id, RelationshipEventType.Visit, "max-offset-day", RelationshipFixture.Time("2026-02-21T12:00:00Z", 60));
                    Check(changedOffsetAtMaximum.Snapshot.State.Score == 1000 && changedOffsetAtMaximum.Snapshot.State.ActiveLocalDates.Count == 52, "actual next day with offset change counts at maximum score");
                }
                Console.WriteLine("RelationshipRulesTests: all behavior scenarios passed"); return 0;
            } catch (Exception e) { Console.WriteLine("FAIL RelationshipRulesTests: " + e); return 1; }
        }
        static void ClockCases()
        {
            using (var f = new RelationshipFixture()) {
                string id = "custom-pack:review";
                f.Service.RecordChecked(id, RelationshipEventType.Visit, "day1", RelationshipFixture.Time("2026-10-24T12:00:00Z"));
                var next = f.Service.RecordChecked(id, RelationshipEventType.CompletedConversation, "day2", RelationshipFixture.Time("2026-10-25T12:00:00Z", 60));
                Check(next.Snapshot.State.ActiveLocalDates.SequenceEqual(new[] { "2026-10-24", "2026-10-25" }), "R1 elapsed natural day plus offset change counts activity after restart");
                Check(next.Snapshot.State.DailyTotal == 2 && next.Snapshot.State.VisitPoints == 0, "R1 actual next natural day refreshes budget despite changed offset");
            }
            using (var f = new RelationshipFixture()) {
                string id = "official:e-bao"; var s = f.Service;
                s.RecordChecked(id, RelationshipEventType.Visit, "east", RelationshipFixture.Time("2026-10-24T23:30:00Z", 840));
                s.RecordChecked(id, RelationshipEventType.Visit, "west", RelationshipFixture.Time("2026-10-24T23:30:00Z", -720));
                var continued = f.Service.RecordChecked(id, RelationshipEventType.CompletedConversation, "catch-up", RelationshipFixture.Time("2026-10-26T11:00:00Z", -720));
                Check(continued.Success && continued.AwardedPoints == 2 && continued.Snapshot.State.DailyTotal == 4, "backward-zone date catches up without reopening budget: " + continued.Error);
            }
            using (var f = new RelationshipFixture()) {
                var s = f.Service; string id = "official:e-bao";
                s.RecordChecked(id, RelationshipEventType.Visit, "v", RelationshipFixture.Time("2026-10-24T23:30:00Z"));
                Check(s.RecordChecked(id, RelationshipEventType.Visit, "travel", RelationshipFixture.Time("2026-10-24T23:30:00Z", 120)).AwardedPoints == 0, "same instant travel carries budget");
                Check(s.GetSnapshot(id).State.ActiveLocalDates.Count == 1, "travel alone invents no active day");
                Check(f.Service.RecordChecked(id, RelationshipEventType.Visit, "rollback", RelationshipFixture.Time("2026-10-23T23:30:00Z", 120)).AwardedPoints == 0, "UTC rollback no new budget");
                var next = f.Service.RecordChecked(id, RelationshipEventType.Visit, "next", RelationshipFixture.Time("2026-10-25T22:00:00Z", 120));
                Check(next.AwardedPoints == 2, "normal midnight after travel: " + next.Error);
                Check(f.Service.RecordChecked(id, RelationshipEventType.Visit, "back-zone", RelationshipFixture.Time("2026-10-25T22:00:00Z", -720)).AwardedPoints == 0, "backward offset carries budget");
                Check(f.Service.RecordChecked(id, RelationshipEventType.Visit, "claimed", RelationshipFixture.Time("2026-10-26T12:00:00Z", -720)).AwardedPoints == 0, "claimed date cannot reopen");
                Check(f.Service.RecordChecked(id, RelationshipEventType.Visit, "forward-zone", RelationshipFixture.Time("2026-10-26T12:00:00Z", 840)).AwardedPoints == 0, "future offset date carries budget");
                Check(f.Service.RecordChecked(id, RelationshipEventType.CompletedConversation, "after-backward-anchor", RelationshipFixture.Time("2026-10-27T12:00:00Z", 840)).Success, "travel anchors stay valid as UTC advances");
            }
            using (var f = new RelationshipFixture()) {
                var s = f.Service; string id = "official:e-bao";
                s.RecordChecked(id, RelationshipEventType.Visit, "fall", RelationshipFixture.Time("2026-10-25T00:30:00Z", 120));
                Check(s.RecordChecked(id, RelationshipEventType.UserInteraction, "fall-tap", RelationshipFixture.Time("2026-10-25T01:30:00Z", 60)).AwardedPoints == 1, "DST repeated local hour uses monotonic UTC interval");
                Check(s.RecordChecked(id, RelationshipEventType.Visit, "future", RelationshipFixture.Time("2026-12-01T00:00:00Z", 60)).AwardedPoints == 2, "forward UTC uses actual observed day");
                Check(f.Service.RecordChecked(id, RelationshipEventType.SuccessfulWorkTask, "backward", RelationshipFixture.Time("2026-11-01T00:00:00Z", 60)).AwardedPoints == 0, "backward UTC after future jump blocked across restart");
                Check(f.Service.GetSnapshot(id).State.ActiveLocalDates.Count == 2, "future jump does not reconstruct intervening active days");
            }
            using (var f = new RelationshipFixture()) {
                var s = f.Service; string id = "official:e-bao";
                s.RecordChecked(id, RelationshipEventType.Visit, "dst", RelationshipFixture.Time("2026-03-29T00:59:00Z", 60));
                Check(s.RecordChecked(id, RelationshipEventType.Visit, "dst-shift", RelationshipFixture.Time("2026-03-29T01:00:00Z", 120)).AwardedPoints == 0, "DST offset no reset");
                Check(f.Service.RecordChecked(id, RelationshipEventType.Visit, "dst-midnight", RelationshipFixture.Time("2026-03-29T22:00:00Z", 120)).AwardedPoints == 2, "DST midnight resets normally");
            }
        }
    }
}
