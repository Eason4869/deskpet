using System;
using System.IO;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class RelationshipStoreTests
    {
        static string diagnosticPath;
        static int storageDiagnostics;
        static void TraceFixture(string path){diagnosticPath=path;System.Threading.Interlocked.Exchange(ref storageDiagnostics,0);Console.WriteLine("STORE_FIXTURE path="+path);}
        public static int Main()
        {
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> storageDiagnostic=delegate(object sender,System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args){var error=args.Exception;if((error is IOException||error is UnauthorizedAccessException)&&System.Threading.Interlocked.Increment(ref storageDiagnostics)<=8)Console.WriteLine("STORE_FIRST_CHANCE path="+diagnosticPath+" hresult=0x"+error.HResult.ToString("X8")+" "+error);};
            AppDomain.CurrentDomain.FirstChanceException+=storageDiagnostic;
            try { ReviewBudgetIntegrity(); ReviewMilestoneIntegrity(); using (var f = new RelationshipFixture()) {
                TraceFixture(f.Store.FilePath);
                var a = f.Service; var b = f.Service; string id = "official:e-bao"; var t = RelationshipFixture.Time("2026-10-24T12:00:00Z");
                RelationshipFixture.Check(a.GetSnapshot(id).Settings.GrowthEnabled && a.GetSnapshot(id).Settings.FeedbackEnabled && a.GetSnapshot(id).Settings.AiContextEnabled, "settings default true");
                a.Record(id, RelationshipEventType.Visit, "visit", t);
                b.Record(id, RelationshipEventType.CompletedConversation, "chat", t);
                a.Record(id, RelationshipEventType.SuccessfulWorkTask, "work", t);
                RelationshipFixture.Check(b.GetSnapshot(id).State.Score == 8 && b.GetSnapshot(id).State.Receipts.Count == 3, "interleaved services no lost updates");
                var copy = a.GetSnapshot(id); copy.State.Score = 999; copy.State.Receipts.Clear(); copy.State.ActiveLocalDates.Clear(); copy.Settings.GrowthEnabled = false;
                RelationshipFixture.Check(a.GetSnapshot(id).State.Score == 8 && a.GetSnapshot(id).State.Receipts.Count == 3 && a.GetSnapshot(id).Settings.GrowthEnabled, "snapshot is independent");
                RelationshipFixture.Check(a.ClaimMilestone(id, "first_meeting").ClaimedNotification && !b.ClaimMilestone(id, "first_meeting").ClaimedNotification && !a.ClaimMilestone(id, "familiar").ClaimedNotification, "claim unlock exactlyonce persisted");
                a.Record("official:marfu", RelationshipEventType.Visit, "marfu", t);
                string path = f.Store.FilePath; string before = File.ReadAllText(path);
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    var failure = a.Record(id, RelationshipEventType.CompletedConversation, "retry", t);
                    RelationshipFixture.Check(!failure.Success && failure.AwardedPoints == 0 && failure.Snapshot.State.Score == 8, "save failure exposes previous snapshot");
                    RelationshipFixture.Check(!a.ClaimMilestone("official:marfu", "first_meeting").ClaimedNotification, "failed save cannot expose notification claim");
                }
                RelationshipFixture.Check(File.ReadAllText(path) == before, "failed save byte-identical");
                RelationshipFixture.Check(a.Record(id, RelationshipEventType.CompletedConversation, "retry", t).AwardedPoints == 2, "sameID retry succeeds after failed save");
                RelationshipFixture.Check(b.ClaimMilestone("official:marfu", "first_meeting").ClaimedNotification, "failed claim can be retried");
                foreach (string invalid in new[] {"{broken", "{\"SchemaVersion\":2,\"Companions\":\"future-shape\"}", "{\"SchemaVersion\":1,\"Companions\":null}"}) {
                    File.WriteAllText(path, invalid);
                    RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "new", t).Success && File.ReadAllText(path) == invalid, "invalid/newer schema preserved");
                }
                File.WriteAllText(path, before.Replace("\"DailyTotal\":8", "\"DailyTotal\":-1"));
                string negative = File.ReadAllText(path);
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "negative", t).Success && File.ReadAllText(path) == negative, "negative counter validation blocks overwrite");
                File.WriteAllText(path, before.Replace("official:e-bao", "invalid/id"));
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "invalidID", t).Success, "payload invalid companion ID rejected");
                File.WriteAllText(path, before.Replace("\"Score\":8", "\"Score\":1001"));
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "invalidScore", t).Success, "payload out of range rejected");
                Corrupt(path, before, delegate(RelationshipData d) { d.Companions[id].FirstMetUtc = d.Companions[id].FirstMetUtc.AddHours(-1); });
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "invented-first-met", t).Success, "first meeting must match first actual receipt");
                Corrupt(path, before, delegate(RelationshipData d) { d.Companions[id].OffsetMinutes = 840; });
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "mismatched-anchor", t).Success, "persisted offset must match observed date anchor");
                Corrupt(path, before, delegate(RelationshipData d) { d.Companions[id].LastRewardedInteractionUtc = t.UtcNow; });
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "invented-interaction", t).Success, "interaction cooldown requires an actual rewarded receipt");
                Corrupt(path, before, delegate(RelationshipData d) { d.Companions[id].LastSeenUtc = d.Companions[id].LastSeenUtc.AddHours(1); });
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "invented-last-seen", t).Success, "last seen must match last actual receipt");
                File.WriteAllText(path, before.Replace("\"FirstMetUtc\":", "\"FirstMetUtc\":\"not-a-date\",\"IgnoredFirstMetUtc\":"));
                string impossible = File.ReadAllText(path);
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "impossible-time", t).Success && File.ReadAllText(path) == impossible, "impossible timestamp preserved and blocks overwrite");
                File.WriteAllText(path, new string(' ', 32 * 1024 * 1024 + 1));
                RelationshipFixture.Check(!f.Service.Record(id, RelationshipEventType.Visit, "oversized", t).Success && new FileInfo(path).Length == 32 * 1024 * 1024 + 1, "oversized store preserved");
                File.WriteAllText(path, before);
                var threads = new System.Threading.Thread[8]; var results = new RelationshipResult[8];
                for (int n = 0; n < threads.Length; n++) { int index = n; threads[n] = new System.Threading.Thread(delegate() { results[index] = f.Service.Record("official:marfu", RelationshipEventType.CompletedConversation, "parallel" + index, t); }); threads[n].Start(); }
                foreach (var thread in threads) thread.Join();
                var concurrent=b.GetSnapshot("official:marfu").State;
                bool serialized=results.All(r=>r!=null&&r.Success)&&concurrent.Score==10&&concurrent.Receipts.Count==9;
                if(!serialized)Console.WriteLine("CONCURRENT_STORE path="+path+" score="+concurrent.Score+" receipts="+concurrent.Receipts.Count+" results="+String.Join(" | ",results.Select((r,i)=>i+":"+(r==null?"null":r.Success+":"+r.Error))));
                RelationshipFixture.Check(serialized, "concurrent services serialize cap counters and receipts");
                var precise = new RelationshipTime(t.UtcNow.AddTicks(9000), TimeSpan.Zero);
                a.Record("custom-pack:precise", RelationshipEventType.UserInteraction, "precise-first", precise);
                RelationshipFixture.Check(f.Service.Record("custom-pack:precise", RelationshipEventType.SuccessfulWorkTask, "sub-ms-rollback", t).AwardedPoints == 0, "submillisecond UTC rollback remains blocked after restart");
                var beforeTwenty = new RelationshipTime(precise.UtcNow.AddTicks(199995000), TimeSpan.Zero);
                RelationshipFixture.Check(f.Service.Record("custom-pack:precise", RelationshipEventType.UserInteraction, "sub-ms-cooldown", beforeTwenty).AwardedPoints == 0, "submillisecond interaction cooldown remains strict after restart");
            } Console.WriteLine("RelationshipStoreTests: all behavior scenarios passed"); return 0;
            } catch (Exception e) { Console.WriteLine("FAIL RelationshipStoreTests: " + e); return 1; }
            finally{AppDomain.CurrentDomain.FirstChanceException-=storageDiagnostic;}
        }
        static void ReviewBudgetIntegrity()
        {
            using (var f = new RelationshipFixture()) {
                TraceFixture(f.Store.FilePath);
                string id = "custom-pack:review"; var time = RelationshipFixture.Time("2026-10-24T12:00:00Z");
                f.Service.RecordChecked(id, RelationshipEventType.Visit, "day1", time);
                string path = f.Store.FilePath, valid = File.ReadAllText(path);
                Corrupt(path, valid, delegate(RelationshipData data) { data.Companions[id].DailyTotal = 0; data.Companions[id].VisitPoints = 0; });
                string invalid = File.ReadAllText(path);
                var result = f.Service.Record(id, RelationshipEventType.Visit, "duplicate-visit", time);
                RelationshipFixture.Check(!result.Success && result.AwardedPoints == 0 && File.ReadAllText(path) == invalid, "R2 reset in-range visit budget rejected byte-identically");
                File.WriteAllText(path, valid);
                f.Service.RecordChecked(id, RelationshipEventType.CompletedConversation, "travel", RelationshipFixture.Time("2026-10-24T12:00:00Z", 840));
                valid = File.ReadAllText(path);
                Corrupt(path, valid, delegate(RelationshipData data) { data.Companions[id].DailyTotal = 2; data.Companions[id].ConversationPoints = 0; });
                invalid = File.ReadAllText(path);
                result = f.Service.Record(id, RelationshipEventType.CompletedConversation, "extra-travel", RelationshipFixture.Time("2026-10-24T12:00:00Z", 840));
                RelationshipFixture.Check(!result.Success && result.AwardedPoints == 0 && File.ReadAllText(path) == invalid, "R2 positive travel-carried counter contradiction rejected byte-identically");
            }
        }
        static void ReviewMilestoneIntegrity()
        {
            using (var f = new RelationshipFixture()) {
                TraceFixture(f.Store.FilePath);
                string id = "custom-pack:review";
                var first = RelationshipFixture.Time("2026-10-24T12:00:00Z");
                for (int day = 0; day < 10; day++) for (int n = 0; n < 4; n++)
                    f.Service.RecordChecked(id, RelationshipEventType.CompletedConversation, day + "chat" + n, new RelationshipTime(first.UtcNow.AddDays(day), TimeSpan.Zero));
                var familiar = f.Service.GetSnapshot(id).State.Milestones.Single(m => m.StageId == "familiar");
                RelationshipFixture.Check(familiar.EarnedUtc == RelationshipFixture.Time("2026-11-02T12:00:00Z").UtcNow && familiar.EarnedLocalDate == "2026-11-02", "R3 literal threshold fixture reaches80 on Nov2");
                using(var runtime=new RelationshipRuntime(f.Root)){
                    RelationshipFixture.Check(runtime.Service.ClaimMilestone(id,"familiar").ClaimedNotification,"earned familiar notification can be claimed before shutdown");
                    runtime.ShutdownAsync().GetAwaiter().GetResult();
                }
                using(var reopened=new RelationshipRuntime(f.Root)){
                    var restored=reopened.Service.GetSnapshot(id).State;
                    RelationshipFixture.Check(restored.Score==80&&restored.Receipts.Count==40&&restored.FirstMetUtc==first.UtcNow&&restored.Milestones.Single(m=>m.StageId=="familiar").EarnedUtc==familiar.EarnedUtc,"runtime reopen preserves earned score receipts first meeting and milestone facts");
                    RelationshipFixture.Check(restored.Milestones.Single(m=>m.StageId=="familiar").NotificationClaimed&&!reopened.Service.ClaimMilestone(id,"familiar").ClaimedNotification,"runtime reopen never reclaims an already celebrated milestone");
                }
                string path = f.Store.FilePath, valid = File.ReadAllText(path);
                Corrupt(path, valid, delegate(RelationshipData data) { var milestone = data.Companions[id].Milestones.Single(m => m.StageId == "familiar"); milestone.EarnedUtc = first.UtcNow; milestone.EarnedLocalDate = "2026-10-24"; });
                bool timestampRejected = RejectMilestoneFile(f, path, id);
                Corrupt(path, valid, delegate(RelationshipData data) { data.Companions[id].Milestones.Single(m => m.StageId == "familiar").EarnedLocalDate = "2026-11-01"; });
                bool dateRejected = RejectMilestoneFile(f, path, id);
                Corrupt(path, valid, delegate(RelationshipData data) { data.Companions[id].Milestones[0].EarnedLocalDate = "2026-10-23"; });
                bool firstMeetingRejected = RejectMilestoneFile(f, path, id);
                RelationshipFixture.Check(timestampRejected && dateRejected && firstMeetingRejected, "R3 threshold provenance rejected byte-identically: timestamp=" + timestampRejected + ", localDate=" + dateRejected + ", firstMeeting=" + firstMeetingRejected);
            }
        }
        static bool RejectMilestoneFile(RelationshipFixture fixture, string path, string id)
        {
            string invalid = File.ReadAllText(path);
            var result = fixture.Service.Record(id, RelationshipEventType.Visit, "after-corruption", RelationshipFixture.Time("2026-11-02T12:00:00Z"));
            return !result.Success && result.AwardedPoints == 0 && File.ReadAllText(path) == invalid;
        }
        static void Corrupt(string path, string json, Action<RelationshipData> change)
        {
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(RelationshipData));
            RelationshipData data;
            using (var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))) data = (RelationshipData)serializer.ReadObject(input);
            change(data);
            using (var output = File.Create(path)) serializer.WriteObject(output, data);
        }
    }
}
