using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Dafeiyu.Maintenance;

namespace Dafeiyu.Tests
{
    public static class ScheduledTaskTests
    {
        public static int Run()
        {
            var utc = TimeZoneInfo.Utc;
            var now = new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);
            var daily = new ScheduledTaskRule { Frequency = ScheduledFrequency.Daily, MinuteOfDay = 540 };
            Assert(daily.Next(now, utc).Value.Utc == now.AddHours(1), "daily");
            var monthly = new ScheduledTaskRule { Frequency = ScheduledFrequency.Monthly, DayOfMonth = 31, MinuteOfDay = 540 };
            Assert(monthly.Next(now, utc).Value.Utc.Month == 12, "skip short month");
            var weekly = new ScheduledTaskRule { Frequency = ScheduledFrequency.Weekly, Weekdays = 1 << (int)DayOfWeek.Friday, MinuteOfDay = 540 };
            Assert(weekly.Next(now, utc).Value.Utc.DayOfWeek == DayOfWeek.Friday, "weekly");
            TimeZoneInfo eastern;
            try { eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch (TimeZoneNotFoundException) { eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            var gap = new ScheduledTaskRule { Frequency = ScheduledFrequency.Daily, MinuteOfDay = 150 };
            Assert(gap.Next(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), eastern).Value.Utc.Hour == 7, "spring gap");
            var fold = new ScheduledTaskRule { Frequency = ScheduledFrequency.Daily, MinuteOfDay = 90 };
            var early = fold.Next(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), eastern).Value.Utc;
            Assert(early.Hour == 5 && fold.Next(early, eastern).Value.Utc.Day == 2, "fall fold once");

            var root = Path.Combine(Path.GetTempPath(), "deskpet-schedule-" + Guid.NewGuid().ToString("N"));
            try
            {
                string id;
                using (var service = new ScheduledTaskService(root, new DataMaintenanceGate()))
                {
                    id = service.Save(new ScheduledTaskDefinition { Title = "Water", Content = "Drink water", Rule = daily }, now, utc);
                    Assert(service.Scan(now.AddHours(49), utc, true).Count == 0, "recovery does not run tasks");
                    Assert(service.Pending().Single().DueUtc == now.AddHours(49), "latest missed only");
                }
                using (var service = new ScheduledTaskService(root, new DataMaintenanceGate()))
                {
                    var missed = service.Pending().Single();
                    var work = service.Resolve(id, missed.Id, true);
                    Assert(work != null && service.Resolve(id, missed.Id, true) == null, "durable single claim");
                    Assert(service.Scan(now.AddHours(49), utc, false).Count == 0, "no duplicate on tick");
                    service.Begin(work);
                }
                using (var service = new ScheduledTaskService(root, new DataMaintenanceGate()))
                {
                    Assert(service.List().Single().Runs.Single().State == ScheduledRunState.Interrupted, "crash recovery");
                    var next = service.Scan(now.AddHours(73), utc, false).Single();
                    service.Begin(next);
                    service.Finish(next, ScheduledRunState.Succeeded, "Drink water");
                    Assert(service.List().Single().Runs.Count == 2, "continue daily after missed run");
                    service.Scan(now.AddHours(97), utc, true);
                    var missed = service.Pending().Single();
                    Assert(service.Resolve(id, missed.Id, false) == null && service.Pending().Count == 0, "skip clears pending");
                    Assert(service.List().Single().Runs.Any(r => r.State == ScheduledRunState.Skipped), "skip history");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            CheckAi().GetAwaiter().GetResult();
            Console.WriteLine("Scheduled task tests passed.");
            return 0;
        }

        static async Task CheckAi()
        {
            var api = new FakeApi();
            var settings = new PetSettings { Text = new ConnectionSettings { BaseUrl = "http://localhost:8080/v1", Model = "default" } };
            var ai = new ScheduledTaskAi(api);
            var messages = new List<ChatMessage> { new ChatMessage("user", "Remind me to write my report on Fridays") };
            api.Answer = "{\"question\":\"At what time?\"}";
            var clarification = await ai.DraftAsync(messages, settings, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, CancellationToken.None);
            Assert(clarification.Definition == null && clarification.Question == "At what time?", "ask for missing fields");
            messages.Add(new ChatMessage("assistant", clarification.Question));
            messages.Add(new ChatMessage("user", "At 16:30"));
            api.Answer = "{\"task\":{\"title\":\"Weekly report\",\"content\":\"Write the report\",\"kind\":\"reminder\",\"frequency\":\"weekly\",\"time\":\"16:30\",\"weekdays\":[5]}}";
            var draft = await ai.DraftAsync(messages, settings, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, CancellationToken.None);
            Assert(draft.Definition.Rule.Weekdays == 32 && draft.Definition.Rule.MinuteOfDay == 990 && api.Messages.Count == 4, "multi-turn draft");
            api.Answer = "{\"task\":{\"title\":\"Report\",\"content\":\"Write the report\",\"kind\":\"ai\",\"frequency\":\"daily\",\"time\":\"41:00\"}}";
            Assert((await ai.DraftAsync(messages, settings, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, CancellationToken.None)).Definition == null, "invalid draft rejected");

            var selected = new TextProviderSettings { Id = "provider-1", Name = "Test", Connection = new ConnectionSettings { BaseUrl = "http://localhost:8081/v1", Model = "selected" } };
            selected.Models.Add("selected");
            settings.TextProviders.Add(selected);
            settings.WebSearch = new WebSearchSettings { Enabled = false, NativeProtocol = "openai-responses", ShowSources = false };
            var definition = new ScheduledTaskDefinition { Title = "Report", Content = "Write the report", Kind = ScheduledTaskKind.Ai, ProviderId = selected.Id, Model = "selected", WebSearch = true };
            api.Answer = "Report with sources";
            var result = await ai.ExecuteAsync(definition, settings, CancellationToken.None);
            Assert(result == api.Answer && api.Model == "selected" && api.Searched && !settings.WebSearch.Enabled && settings.Text.Model == "default", "task-specific model and search without changing defaults");
            Assert(ScheduledTaskRuntime.ShouldNotify(ScheduledNotification.All, ScheduledRunState.Succeeded)
                && ScheduledTaskRuntime.ShouldNotify(ScheduledNotification.Failures, ScheduledRunState.Failed)
                && !ScheduledTaskRuntime.ShouldNotify(ScheduledNotification.Failures, ScheduledRunState.Succeeded)
                && !ScheduledTaskRuntime.ShouldNotify(ScheduledNotification.None, ScheduledRunState.Failed), "notification policy");
            api.Answer = "{invalid";
            Assert((await ai.DraftAsync(messages, settings, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, CancellationToken.None)).Question != null, "malformed draft can be corrected");
            definition.Model = "removed";
            try { await ai.ExecuteAsync(definition, settings, CancellationToken.None); throw new InvalidOperationException("missing model was accepted"); }
            catch (ApiException) { }
        }

        sealed class FakeApi : IConversationApi, IWebSearchConversationApi
        {
            public string Answer, Model;
            public IList<ChatMessage> Messages;
            public bool Searched;
            public Task<string> ChatAsync(ConnectionSettings settings, IList<ChatMessage> messages, CancellationToken cancel)
            { Model = settings.Model; Messages = messages; return Task.FromResult(Answer); }
            public Task<string> ChatSearchAsync(ConnectionSettings settings, IList<ChatMessage> messages, WebSearchSettings search,
                Action<string> onDelta, Action<string> onReasoning, Action<AgentToolEvent> onToolEvent, bool stream, CancellationToken cancel)
            { Searched = search.ShowSources; return ChatAsync(settings, messages, cancel); }
            public Task<string> TranscribeAsync(ConnectionSettings settings, byte[] wav, CancellationToken cancel) { throw new NotSupportedException(); }
            public Task<byte[]> SpeechAsync(ConnectionSettings settings, string voice, string text, CancellationToken cancel) { throw new NotSupportedException(); }
            public void Dispose() { }
        }

        static void Assert(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
        }
    }
}
