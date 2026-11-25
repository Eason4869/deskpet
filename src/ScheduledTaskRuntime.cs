using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    public sealed class ScheduledTaskRuntime : IDisposable
    {
        readonly ScheduledTaskAi ai;
        readonly ScheduledTaskService service;
        readonly DataMaintenanceGate maintenance;
        readonly Func<PetSettings> settings;
        readonly Queue<ScheduledTaskWork> aiQueue = new Queue<ScheduledTaskWork>();
        readonly Queue<ScheduledTaskWork> reminders = new Queue<ScheduledTaskWork>();
        readonly Queue<Completion> completions = new Queue<Completion>();
        readonly HashSet<Task<ScheduledDraftReply>> drafts = new HashSet<Task<ScheduledDraftReply>>();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        sealed class Completion
        {
            internal ScheduledTaskWork Work;
            internal ScheduledRunState State;
            internal string Result;
        }
        Task running = Task.FromResult(0);
        Task stopDrain;
        bool recover = true, disposed;
        volatile bool stopping;
        long lastZoneCheck;
        string zoneId;
        int suspended, resumed;
        public event Action Changed;
        public event Action Missed;
        public event Action<ScheduledTaskWork, ScheduledRunState, string> Completed;
        public string Error { get; private set; }
        internal bool HasUnsavedCompletions { get { return completions.Count != 0; } }

        public ScheduledTaskRuntime(string root, DataMaintenanceGate maintenance, IConversationApi api, Func<PetSettings> settings)
        {
            this.settings = settings;
            this.maintenance = maintenance;
            ai = new ScheduledTaskAi(api);
            try { service = new ScheduledTaskService(root, maintenance); }
            catch (Exception error) { if (!StorageFailure(error)) throw; SetError(); }
        }

        public IReadOnlyList<ScheduledTaskSnapshot> List()
        {
            return service == null ? new ScheduledTaskSnapshot[0] : service.List();
        }

        public IReadOnlyList<ScheduledTaskRun> Pending()
        {
            return service == null ? new ScheduledTaskRun[0] : service.Pending();
        }

        public IEnumerable<ScheduledModelChoice> Models() { return ScheduledTaskAi.Models(settings()); }

        public void PowerSuspended() { Interlocked.Exchange(ref suspended, 1); }
        public void PowerResumed()
        {
            Interlocked.Exchange(ref resumed, 1);
            Interlocked.Exchange(ref suspended, 0);
        }

        public void Tick()
        {
            if (service == null || stopping || Volatile.Read(ref suspended) != 0) return;
            IDisposable activity;
            if (!maintenance.TryEnterActivity(out activity)) return;
            using (activity)
            {
                recover |= Interlocked.Exchange(ref resumed, 0) != 0;
                var ticks = Stopwatch.GetTimestamp();
                if (recover || ticks - lastZoneCheck >= Stopwatch.Frequency * 60)
                {
                    TimeZoneInfo.ClearCachedData();
                    lastZoneCheck = ticks;
                }
                var zone = TimeZoneInfo.Local;
                bool zoneChanged = zone.Id != zoneId;
                zoneId = zone.Id;
                try
                {
                    var work = service.Scan(DateTimeOffset.UtcNow, zone, recover);
                    foreach (var job in work) Enqueue(job);
                    bool show = recover && Pending().Count != 0;
                    recover = false;
                    if (show && Missed != null) Missed();
                    if (work.Count != 0 || show || zoneChanged) OnChanged();
                    Start();
                    if (Error != null) { Error = null; OnChanged(); }
                }
                catch (Exception error)
                {
                    if (!StorageFailure(error)) throw;
                    SetError();
                }
            }
        }

        public string Save(ScheduledTaskDefinition task)
        {
            var id = Available().Save(task, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            OnChanged();
            return id;
        }

        public void SetEnabled(string id, bool enabled)
        {
            Available().SetEnabled(id, enabled, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            OnChanged();
        }

        public void Delete(string id)
        {
            Available().Delete(id);
            OnChanged();
        }

        public void Resolve(ScheduledTaskRun run, bool execute)
        {
            var work = Available().Resolve(run.TaskId, run.Id, execute);
            if (work != null) Enqueue(work);
            OnChanged();
            Tick();
        }

        public Task<ScheduledDraftReply> DraftAsync(IList<ChatMessage> turns, CancellationToken cancel)
        {
            lock (drafts)
            {
                if (stopping) throw new OperationCanceledException();
                var draft = GenerateDraftAsync(turns, cancel);
                drafts.Add(draft);
                draft.ContinueWith(completed => { lock (drafts) drafts.Remove(completed); }, TaskScheduler.Default);
                return draft;
            }
        }

        async Task<ScheduledDraftReply> GenerateDraftAsync(IList<ChatMessage> turns, CancellationToken cancel)
        {
            using (maintenance.EnterActivity())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancel))
                return await ai.DraftAsync(turns, settings(), DateTimeOffset.UtcNow, TimeZoneInfo.Local, linked.Token);
        }

        ScheduledTaskService Available()
        {
            if (service == null || stopping) throw new InvalidOperationException(TextResources.Get("scheduledTasks.storageError"));
            return service;
        }

        void Enqueue(ScheduledTaskWork work)
        {
            (work.Definition.Kind == ScheduledTaskKind.Ai ? aiQueue : reminders).Enqueue(work);
        }

        void Start()
        {
            if (stopping || Volatile.Read(ref suspended) != 0 || maintenance.IsMaintenanceActive) return;
            FlushCompletions();
            while (reminders.Count != 0)
            {
                var work = Take(reminders);
                if (work != null) Finish(work, ScheduledRunState.Succeeded, work.Definition.Content);
            }
            if (running.IsCompleted && aiQueue.Count != 0) running = RunAiAsync();
        }

        ScheduledTaskWork Take(Queue<ScheduledTaskWork> queue)
        {
            var work = queue.Peek();
            bool claimed = service.Begin(work);
            queue.Dequeue();
            return claimed ? work : null;
        }

        async Task RunAiAsync()
        {
            // Yield so the running task is registered before processing can finish.
            await Task.Yield();
            while (!stopping && Volatile.Read(ref suspended) == 0 && aiQueue.Count != 0)
            {
                IDisposable activity;
                if (!maintenance.TryEnterActivity(out activity)) break;
                using (activity)
                {
                    ScheduledTaskWork work;
                    try { work = Take(aiQueue); }
                    catch (Exception error)
                    {
                        if (!StorageFailure(error)) throw;
                        SetError();
                        break;
                    }
                    if (work == null) continue;
                    OnChanged();
                    ScheduledRunState state;
                    string result;
                    try
                    {
                        result = await ai.ExecuteAsync(work.Definition, settings(), lifetime.Token);
                        state = ScheduledRunState.Succeeded;
                    }
                    catch (OperationCanceledException) { state = ScheduledRunState.Interrupted; result = null; }
                    catch (ApiException error) { state = ScheduledRunState.Failed; result = error.Message; }
                    catch (Exception) { state = ScheduledRunState.Failed; result = TextResources.Get("scheduledTasks.runError"); }
                    try { Finish(work, state, result); }
                    catch (Exception error)
                    {
                        if (!StorageFailure(error)) throw;
                        SetError();
                        break;
                    }
                }
            }
        }

        void Finish(ScheduledTaskWork work, ScheduledRunState state, string result)
        {
            completions.Enqueue(new Completion { Work = work, State = state, Result = result });
            FlushCompletions();
        }

        void FlushCompletions()
        {
            while (completions.Count != 0)
            {
                var completion = completions.Peek();
                bool saved = service.Finish(completion.Work, completion.State, completion.Result);
                completions.Dequeue();
                if (!saved || stopping) continue;
                OnChanged();
                var notify = Completed;
                if (!stopping && notify != null && ShouldNotify(completion.Work.Definition.Notification, completion.State))
                    notify(completion.Work, completion.State, completion.Result);
            }
        }

        public static bool ShouldNotify(ScheduledNotification policy, ScheduledRunState state)
        {
            return policy == ScheduledNotification.All || policy == ScheduledNotification.Failures
                && (state == ScheduledRunState.Failed || state == ScheduledRunState.Interrupted);
        }

        void OnChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        void SetError()
        {
            if (Error != null) return;
            Error = TextResources.Get("scheduledTasks.storageError");
            OnChanged();
        }

        static bool StorageFailure(Exception error)
        {
            return error is System.IO.IOException || error is UnauthorizedAccessException
                || error is System.Runtime.Serialization.SerializationException || error is System.Security.Cryptography.CryptographicException
                || error is System.Xml.XmlException;
        }

        public Task StopAsync()
        {
            lock (drafts)
            {
                if (stopDrain != null) return stopDrain;
                stopping = true;
                stopDrain = DrainAsync(Task.WhenAll(drafts.Select(DrainDraftAsync).Concat(new[] { running })));
            }
            lifetime.Cancel();
            return stopDrain;
        }

        async Task DrainAsync(Task pending)
        {
            await pending;
            if (service != null) FlushCompletions();
        }

        static async Task DrainDraftAsync(Task draft)
        {
            // The editor owns the request outcome, including cancellation.
            try { await draft.ConfigureAwait(false); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var drain = StopAsync();
            if (drain.IsCompleted) Release();
            else drain.ContinueWith(_ => Release(), TaskScheduler.Default);
        }

        void Release()
        {
            if (service != null) service.Dispose();
            lifetime.Dispose();
        }
    }
}
