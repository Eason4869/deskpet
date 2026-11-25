using System;
using System.Collections.Generic;
using System.Linq;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    public sealed class ScheduledTaskSnapshot
    {
        public ScheduledTaskDefinition Definition { get; private set; }
        public IReadOnlyList<ScheduledTaskRun> Runs { get; private set; }
        readonly DateTimeOffset startAfter;
        readonly long cursor;

        internal ScheduledTaskSnapshot(ScheduledTaskRecord record)
        {
            Definition = record.Definition.Copy();
            Runs = record.Runs.OrderByDescending(run => run.UpdatedUtc).Select(run => run.Copy()).ToArray();
            startAfter = record.StartAfterUtc;
            cursor = record.LastCivilTicks;
        }

        public ScheduledOccurrence? Next(DateTimeOffset now, TimeZoneInfo zone)
        {
            return Definition.Enabled ? Definition.Rule.Next(now > startAfter ? now : startAfter, zone, cursor) : null;
        }
    }

    public sealed class ScheduledTaskWork
    {
        public ScheduledTaskDefinition Definition { get; private set; }
        public ScheduledTaskRun Run { get; private set; }

        internal ScheduledTaskWork(ScheduledTaskDefinition definition, ScheduledTaskRun run)
        {
            Definition = definition.Copy();
            Run = run.Copy();
        }
    }

    public sealed class ScheduledTaskService : IDisposable
    {
        readonly object gate = new object();
        readonly ScheduledTaskStore store;
        readonly DataMaintenanceGate maintenance;
        List<ScheduledTaskRecord> records;
        public ScheduledTaskService(string root, DataMaintenanceGate maintenance)
        {
            this.maintenance = maintenance;
            store = new ScheduledTaskStore(root);
            try
            {
                records = store.Load();
                bool changed = false;
                foreach (var record in records)
                {
                    foreach (var run in record.Runs.Where(r => r.State == ScheduledRunState.Running))
                    {
                        Update(run, ScheduledRunState.Interrupted);
                        changed = true;
                    }
                    var waiting = record.Runs.Where(r => r.State == ScheduledRunState.Queued || r.State == ScheduledRunState.Pending)
                        .OrderByDescending(r => r.DueUtc).ToArray();
                    if (waiting.Any(r => r.State == ScheduledRunState.Queued) || waiting.Length > 1)
                    {
                        for (int i = 0; i < waiting.Length; i++)
                            Update(waiting[i], i == 0 && record.Definition.Enabled ? ScheduledRunState.Pending : ScheduledRunState.Skipped);
                        changed = true;
                    }
                }
                if (changed) Commit(records);
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        public IReadOnlyList<ScheduledTaskSnapshot> List()
        {
            lock (gate) return records.Select(r => new ScheduledTaskSnapshot(r)).ToArray();
        }

        public IReadOnlyList<ScheduledTaskRun> Pending()
        {
            lock (gate) return records.SelectMany(r => r.Runs).Where(r => r.State == ScheduledRunState.Pending)
                .OrderBy(r => r.DueUtc).Select(r => r.Copy()).ToArray();
        }

        public string Save(ScheduledTaskDefinition definition, DateTimeOffset now, TimeZoneInfo zone)
        {
            var draft = definition.Copy();
            draft.Title = (draft.Title ?? "").Trim();
            draft.Content = (draft.Content ?? "").Trim();
            if (!draft.IsValid) throw new InvalidOperationException(TextResources.Get("scheduledTasks.invalid"));
            using (maintenance.EnterActivity())
            lock (gate)
            {
                var next = Copy();
                var existing = next.SingleOrDefault(r => r.Definition.Id == draft.Id);
                bool scheduleChanged = existing == null || !existing.Definition.Rule.SameSchedule(draft.Rule);
                if (scheduleChanged && !draft.Rule.Next(now, zone).HasValue)
                    throw new InvalidOperationException(TextResources.Get("scheduledTasks.futureDate"));

                if (existing == null)
                {
                    if (!String.IsNullOrEmpty(draft.Id)) throw new InvalidOperationException(TextResources.Get("scheduledTasks.invalid"));
                    existing = new ScheduledTaskRecord();
                    draft.Id = Guid.NewGuid().ToString("N");
                    next.Add(existing);
                }
                if (scheduleChanged)
                {
                    SkipWaiting(existing);
                    existing.StartAfterUtc = now;
                    existing.LastCivilTicks = 0;
                }
                existing.Definition = draft;
                Commit(next);
                return draft.Id;
            }
        }

        public void SetEnabled(string id, bool enabled, DateTimeOffset now, TimeZoneInfo zone)
        {
            using (maintenance.EnterActivity())
            lock (gate)
            {
                var next = Copy();
                var record = next.SingleOrDefault(r => r.Definition.Id == id);
                if (record == null || record.Definition.Enabled == enabled) return;
                if (enabled && !record.Definition.Rule.Next(now, zone, record.LastCivilTicks).HasValue)
                    throw new InvalidOperationException(TextResources.Get("scheduledTasks.futureDate"));
                record.Definition.Enabled = enabled;
                record.StartAfterUtc = now;
                if (!enabled) SkipWaiting(record);
                Commit(next);
            }
        }

        public void Delete(string id)
        {
            using (maintenance.EnterActivity())
            lock (gate)
            {
                var next = Copy();
                var record = next.SingleOrDefault(r => r.Definition.Id == id);
                if (record == null) return;
                if (record.Runs.Any(r => r.State == ScheduledRunState.Running))
                    throw new InvalidOperationException(TextResources.Get("scheduledTasks.runningDelete"));
                next.Remove(record);
                Commit(next);
            }
        }

        public IReadOnlyList<ScheduledTaskWork> Scan(DateTimeOffset now, TimeZoneInfo zone, bool recovery)
        {
            var jobs = new List<ScheduledTaskWork>();
            IDisposable activity;
            if (!maintenance.TryEnterActivity(out activity)) return jobs;
            using (activity)
            lock (gate)
            {
                List<ScheduledTaskRecord> next = null;
                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    if (!record.Definition.Enabled) continue;
                    var due = record.Definition.Rule.Latest(now, zone);
                    if (!due.HasValue || due.Value.Utc <= record.StartAfterUtc || due.Value.CivilTicks <= record.LastCivilTicks) continue;
                    if (next == null) next = Copy();
                    record = next[i];
                    if (recovery)
                        foreach (var previous in record.Runs.Where(r => r.State == ScheduledRunState.Pending))
                            Update(previous, ScheduledRunState.Skipped);
                    var run = new ScheduledTaskRun
                    {
                        Id = Guid.NewGuid().ToString("N"), TaskId = record.Definition.Id, Title = record.Definition.Title,
                        DueUtc = due.Value.Utc, CivilTicks = due.Value.CivilTicks, UpdatedUtc = now,
                        State = recovery ? ScheduledRunState.Pending : ScheduledRunState.Queued
                    };
                    record.Runs.Add(run);
                    record.LastCivilTicks = due.Value.CivilTicks;
                    if (!recovery) jobs.Add(new ScheduledTaskWork(record.Definition, run));
                }
                if (next != null) Commit(next);
            }
            return jobs;
        }

        public ScheduledTaskWork Resolve(string taskId, string runId, bool execute)
        {
            using (maintenance.EnterActivity())
            lock (gate)
            {
                var next = Copy();
                var record = next.SingleOrDefault(r => r.Definition.Id == taskId);
                var run = record == null ? null : record.Runs.SingleOrDefault(r => r.Id == runId);
                if (run == null || run.State != ScheduledRunState.Pending) return null;
                Update(run, execute ? ScheduledRunState.Queued : ScheduledRunState.Skipped);
                Commit(next);
                return execute ? new ScheduledTaskWork(record.Definition, run) : null;
            }
        }

        public bool Begin(ScheduledTaskWork work)
        {
            return Transition(work, ScheduledRunState.Queued, ScheduledRunState.Running, null);
        }

        public bool Finish(ScheduledTaskWork work, ScheduledRunState state, string result)
        {
            if (state != ScheduledRunState.Succeeded && state != ScheduledRunState.Failed && state != ScheduledRunState.Interrupted)
                throw new ArgumentOutOfRangeException("state");
            return Transition(work, ScheduledRunState.Running, state, result);
        }

        bool Transition(ScheduledTaskWork work, ScheduledRunState expected, ScheduledRunState state, string result)
        {
            using (maintenance.EnterActivity())
            lock (gate)
            {
                var next = Copy();
                var record = next.SingleOrDefault(r => r.Definition.Id == work.Definition.Id);
                var run = record == null ? null : record.Runs.SingleOrDefault(r => r.Id == work.Run.Id);
                if (run == null || run.State != expected) return false;
                Update(run, state);
                run.Result = result != null && result.Length > 32000 ? result.Substring(0, 32000) : result;
                Commit(next);
                return true;
            }
        }

        List<ScheduledTaskRecord> Copy() { return records.Select(r => r.Copy()).ToList(); }

        void Commit(List<ScheduledTaskRecord> next)
        {
            foreach (var record in next)
            {
                var expired = record.Runs.Where(r => !r.Active).OrderByDescending(r => r.UpdatedUtc).Skip(30).ToArray();
                foreach (var run in expired) record.Runs.Remove(run);
            }
            store.Save(next);
            records = next;
        }

        static void SkipWaiting(ScheduledTaskRecord record)
        {
            foreach (var run in record.Runs.Where(r => r.State == ScheduledRunState.Pending || r.State == ScheduledRunState.Queued))
                Update(run, ScheduledRunState.Skipped);
        }

        static void Update(ScheduledTaskRun run, ScheduledRunState state)
        {
            run.State = state;
            run.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        public void Dispose() { lock (gate) store.Dispose(); }
    }
}
