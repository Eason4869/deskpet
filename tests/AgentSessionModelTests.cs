using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentSessionModelTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args.Length == 4 && args[1] == "--journal-child") return RunJournalChild(args[2], args[3]);
                var session=new AgentSessionModel("s1","p1","c1");
                var input=new AgentSessionInput{IdempotencyId="input-1",Kind="prompt",Text="first"};
                var first=session.Enqueue(input);var duplicate=session.Enqueue(new AgentSessionInput{IdempotencyId="input-1",Kind="prompt",Text="ignored"});
                Check(Object.ReferenceEquals(first,duplicate) && session.Projection.PendingCount==1,"same-session input idempotency prevents duplicate admission");
                var second=session.Enqueue(new AgentSessionInput{IdempotencyId="input-2",Kind="steer",Text="second",Steer=true});
                Check(session.Projection.PendingCount==2,"different inputs remain pending until delivered");
                var delivered=session.DeliverNext();
                Check(delivered.IdempotencyId=="input-1" && session.Projection.PendingCount==1 && session.Projection.DeliveredCount==1,"inbox delivery preserves order and separates pending from delivered");
                session.StartStep();session.CompleteStep("step-1");
                Check(session.Projection.Step==1 && session.Projection.RecoveryState=="idle","step completion advances durable session state");
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"session-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var store=new AgentSessionStore(root);store.Save(session);var loaded=store.Load("p1","c1");
                Check(loaded!=null && loaded.SessionId=="s1" && loaded.Projection.DeliveredCount==1,"session store preserves admitted and delivered state");
                TurnJournalRoundTripsAndRecovers(root);
                TurnJournalRepairsOnlyTornTail(root);
                TurnJournalSerializesWriters(root);
                TurnJournalBatchesDuringLiveTurn(root);
                TurnJournalCompactionValidatesUnderLease(root);
                TurnJournalSupportsLongPaths(root);
                if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
                    typeof(AgentSessionModelTests).Assembly.GetName().Name == "AgentSessionModelTests")
                    TurnJournalCoordinatesProcesses(root);
                Console.WriteLine("Agent session model tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }

        static void TurnJournalRoundTripsAndRecovers(string root)
        {
            var journal = new AgentTurnJournal(root);
            var reopened = new AgentTurnJournal(root);
            using (journal.AcquireTurn())
            {
                journal.Append("turn.started", "private-turn", "private-task");
                journal.Append("step.started", "private-turn", "private-task", "private-step");
                journal.Append("tool.started", "private-turn", "private-task", "private-step", "private-call");
                var observed = reopened.Read();
                Check(observed.Count == 3 && observed.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 2, 3 })
                    && observed.All(e => e.Version == 1 && e.CreatedUtc.Kind == DateTimeKind.Utc && e.CreatedUtc > DateTime.UtcNow.AddMinutes(-5))
                    && observed[2].TurnId == "private-turn" && observed[2].TaskId == "private-task"
                    && observed[2].StepId == "private-step" && observed[2].CallId == "private-call",
                    "cross-instance replay preserves sequence, UTC, and references");
                Check(reopened.RecoverInterrupted().Count == 0, "reopening does not recover a live turn");
                Check(ThrowsIOException(() => { using (reopened.AcquireTurn()) { } }),
                    "only one live turn can hold the conversation lease");
            }

            Check(!File.ReadAllText(Path.Combine(root, "turn.events")).Contains("private-"),
                "turn events encrypt the entire record at rest");
            var recovered = reopened.RecoverInterrupted();
            Check(recovered.Select(e => e.Kind).SequenceEqual(new[] { "tool.finished", "step.finished", "turn.finished" })
                && recovered.All(e => e.Outcome == "interrupted") && recovered[0].CallId == "private-call"
                && recovered[1].StepId == "private-step" && recovered[2].TurnId == "private-turn",
                "recovery closes an incomplete tool, step, and turn in order");
            Check(journal.RecoverInterrupted().Count == 0 && journal.Read().Count == 6,
                "interrupted events are recovered exactly once");
            journal.Clear();
            Check(reopened.Read().Count == 0 && new AgentSessionStore(root).Load("p1", "c1") != null,
                "clearing the turn journal preserves the separate legacy session");
        }

        static void TurnJournalRepairsOnlyTornTail(string root)
        {
            string directory = Path.Combine(root, "torn");
            var journal = new AgentTurnJournal(directory);
            journal.Append("turn.started", "torn-turn");
            string path = Path.Combine(directory, "turn.events");
            string firstLine = File.ReadAllText(path);
            File.AppendAllText(path, "DPTE1:partial-record");
            long incompleteLength = new FileInfo(path).Length;
            Check(journal.Read().Count == 1 && new FileInfo(path).Length == incompleteLength,
                "read tolerates a torn final append without modifying it");
            journal.Append("turn.finished", "torn-turn", outcome: "completed");
            Check(journal.Read().Select(e => e.Sequence).SequenceEqual(new long[] { 1, 2 })
                && File.ReadAllText(path).StartsWith(firstLine, StringComparison.Ordinal)
                && !File.ReadAllText(path).Contains("partial-record"),
                "appending repairs only the torn bytes and preserves prior records");

            File.AppendAllText(path, "DPTE1:corrupt-record\n");
            byte[] corrupt = File.ReadAllBytes(path);
            Check(ThrowsIOException(() => journal.Append("turn.started", "later"))
                && ThrowsIOException(() => journal.RecoverInterrupted())
                && File.ReadAllBytes(path).SequenceEqual(corrupt),
                "complete corrupt records block appends and recovery without losing bytes");

            File.WriteAllText(path, firstLine + firstLine);
            byte[] duplicate = File.ReadAllBytes(path);
            Check(ThrowsIOException(() => journal.Read()) && File.ReadAllBytes(path).SequenceEqual(duplicate),
                "replay rejects repeated sequence numbers even when both envelopes decrypt");
        }

        static void TurnJournalSerializesWriters(string root)
        {
            string directory = Path.Combine(root, "concurrent");
            Parallel.For(0, 12, index => new AgentTurnJournal(directory).Append("turn.started", "turn-" + index));
            var events = new AgentTurnJournal(directory).Read();
            Check(events.Select(e => e.Sequence).SequenceEqual(Enumerable.Range(1, 12).Select(index => (long)index))
                && events.Select(e => e.TurnId).Distinct().Count() == 12,
                "concurrent journal instances append unique, contiguous sequences");
        }

        static void TurnJournalBatchesDuringLiveTurn(string root)
        {
            string directory = Path.Combine(root, "batch");
            var writer = new AgentTurnJournal(directory);
            var reader = new AgentTurnJournal(directory);
            string payload = new string('测', 64 * 1024) + "private reply";
            var tool = new AgentToolEvent { Id = "private-id", Summary = "private tool", ElapsedMilliseconds = 123 };
            using (writer.AcquireTurn())
            {
                writer.Append("turn.started", "batch-turn");
                var written = writer.AppendBatch(new[]
                {
                    new AgentTurnEvent { Kind = "reply.delta", TurnId = "batch-turn", Text = payload, Revision = 42 },
                    new AgentTurnEvent { Kind = "tool.updated", TurnId = "batch-turn", Tool = tool }
                });
                Check(written.Select(e => e.Sequence).SequenceEqual(new long[] { 2, 3 })
                    && reader.Read()[1].Text == payload && reader.Read()[1].Revision == 42
                    && reader.Read()[2].Tool.Summary == "private tool" && reader.Read()[2].Tool.ElapsedMilliseconds == 123,
                    "live batch appends ordered and durable deltas and tool payloads");
                Check(ThrowsIOException(() => reader.Append("turn.started", "other")),
                    "a separate writer cannot append during the live turn");
                writer.Append("turn.finished", "batch-turn", outcome: "completed");
            }
            Check(!File.ReadAllText(Path.Combine(directory, AgentTurnJournal.FileName)).Contains("private"),
                "reply and tool payloads remain encrypted at rest");
            Check(reader.RecoverInterrupted().Count == 0 && reader.Read().Count == 4,
                "reopening a completed batch does not synthesize recovery records");
        }

        static void TurnJournalCompactionValidatesUnderLease(string root)
        {
            string directory = Path.Combine(root, "compact");
            var writer = new AgentTurnJournal(directory);
            var compactor = new AgentTurnJournal(directory);
            writer.Append("turn.started", "compacted");
            string path = Path.Combine(directory, AgentTurnJournal.FileName);
            using (writer.AcquireTurn())
            {
                Check(!compactor.TryClear(e => { throw new Exception("live journal was inspected for compaction"); })
                    && File.Exists(path), "compaction cannot inspect or delete a live journal");
                Check(ThrowsIOException(() => compactor.Discard()), "explicit discard rejects a live journal");
            }
            Check(!compactor.TryClear(e => e[0].TurnId != "compacted") && File.Exists(path),
                "compaction preserves data rejected by its checkpoint predicate");
            Check(compactor.TryClear(e => e[0].TurnId == "compacted") && compactor.Read().Count == 0,
                "compaction clears an inactive journal accepted by its checkpoint predicate");

            writer.Append("turn.started", "corrupt");
            File.AppendAllText(path, "DPTE1:corrupt-record\n");
            byte[] corrupt = File.ReadAllBytes(path);
            Check(ThrowsIOException(() => compactor.TryClear(e => true)) && File.ReadAllBytes(path).SequenceEqual(corrupt),
                "compaction preserves complete corrupt records");
            compactor.Discard();
            Check(!File.Exists(path) && compactor.Read().Count == 0,
                "explicit discard removes corrupt journal data on request");
        }

        static void TurnJournalSupportsLongPaths(string root)
        {
            string directory = Path.Combine(root, new string('a', 95), new string('b', 95), new string('c', 95));
            var journal = new AgentTurnJournal(directory);
            using (journal.AcquireTurn())
            {
                journal.Append("turn.started", "long-path");
                Check(new AgentTurnJournal(directory).Read()[0].TurnId == "long-path",
                    "a long journal path works through a live writer and separate reader");
            }
            journal.Clear();
        }

        static void TurnJournalCoordinatesProcesses(string root)
        {
            string directory = Path.Combine(root, "processes");
            var journal = new AgentTurnJournal(directory);
            using (journal.AcquireTurn())
            {
                journal.Append("turn.started", "parent");
                using (var child = StartJournalChild(directory, "observe"))
                {
                    Check(WaitForJournalChild(child), "a second process reads a live turn and leaves its lease untouched");
                }
            }

            using (var first = StartJournalChild(directory, "child-one"))
            using (var second = StartJournalChild(directory, "child-two"))
            {
                bool firstWritten = WaitForJournalChild(first);
                bool secondWritten = WaitForJournalChild(second);
                var records = journal.Read();
                Check(firstWritten && secondWritten && records.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 2, 3 })
                    && records.Skip(1).Select(e => e.TurnId).OrderBy(id => id).SequenceEqual(new[] { "child-one", "child-two" }),
                    "two processes append distinct events with consecutive sequence numbers");
            }
        }

        static Process StartJournalChild(string directory, string command)
        {
            return Process.Start(new ProcessStartInfo(typeof(AgentSessionModelTests).Assembly.Location,
                "\"--journal-child\" \"" + directory + "\" " + command)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        static bool WaitForJournalChild(Process child)
        {
            if (child.WaitForExit(15000)) return child.ExitCode == 0;
            child.Kill();
            child.WaitForExit();
            return false;
        }

        static int RunJournalChild(string directory, string command)
        {
            var journal = new AgentTurnJournal(directory);
            if (command == "observe")
            {
                return journal.Read().Count == 1 && journal.RecoverInterrupted().Count == 0 &&
                    ThrowsIOException(() => { using (journal.AcquireTurn()) { } }) ? 0 : 1;
            }
            journal.Append("turn.started", command);
            return 0;
        }

        static bool ThrowsIOException(Action action)
        {
            try { action(); return false; }
            catch (IOException) { return true; }
        }
    }
}
