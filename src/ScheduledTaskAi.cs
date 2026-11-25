using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed class ScheduledDraftReply
    {
        public string Question;
        public ScheduledTaskDefinition Definition;
    }

    public sealed class ScheduledModelChoice
    {
        public string ProviderId, Model, Label;
        public override string ToString() { return Label; }
    }

    public sealed class ScheduledTaskAi
    {
        readonly IConversationApi api;
        public ScheduledTaskAi(IConversationApi api) { this.api = api; }

        public static IEnumerable<ScheduledModelChoice> Models(PetSettings settings)
        {
            yield return new ScheduledModelChoice { Label = TextResources.Get("scheduledTasks.defaultModel") };
            foreach (var provider in settings.TextProviders.Where(p => p != null && p.Connection != null))
                foreach (var model in (provider.Models ?? new List<string>()).Concat(new[] { provider.Connection.Model }).Where(m => !String.IsNullOrWhiteSpace(m)).Distinct())
                    yield return new ScheduledModelChoice { ProviderId = provider.Id, Model = model, Label = provider.Name + " · " + provider.DisplayName(model) };
        }

        public async Task<ScheduledDraftReply> DraftAsync(IList<ChatMessage> conversation, PetSettings settings,
            DateTimeOffset now, TimeZoneInfo zone, CancellationToken cancel)
        {
            const string instructions = "Help the user define one local DeskPet scheduled task. This is a draft only. " +
                "Use the last user message's language for the question, title and content. " +
                "Respond with exactly one JSON object. If the task's content, kind, recurrence or time is uncertain, return {\"question\":\"one short clarification\"}. " +
                "Use this shape when ready: {\"task\":{\"title\":\"short title\",\"content\":\"reminder or runnable prompt\",\"kind\":\"reminder or ai\"," +
                "\"frequency\":\"once, daily, weekly or monthly\",\"time\":\"HH:mm\",\"date\":\"yyyy-MM-dd for once\"," +
                "\"weekdays\":[1,5],\"day\":1}}. Use weekdays for weekly (Sunday=0) and day for monthly. " +
                "Resolve relative dates only when the user gives enough information. Never fill missing times with defaults. Local current time: ";
            var messages = new List<ChatMessage> { new ChatMessage("system", instructions +
                TimeZoneInfo.ConvertTime(now, zone).ToString("yyyy-MM-dd HH:mm dddd zzz", CultureInfo.InvariantCulture) + " (" + zone.Id + ").") };
            messages.AddRange(conversation.Skip(Math.Max(0, conversation.Count - 20)));
            var model = ResolveModel(settings, null);
            string answer;
            using (UsageTracking.BeginScope("scheduled-task-draft"))
                answer = await api.ChatAsync(model, messages, cancel).ConfigureAwait(false);
            return Parse(answer, now, zone);
        }

        public async Task<string> ExecuteAsync(ScheduledTaskDefinition task, PetSettings settings, CancellationToken cancel)
        {
            var model = ResolveModel(settings, task);
            var messages = new List<ChatMessage> { new ChatMessage("user", task.Content) };
            using (UsageTracking.BeginScope("scheduled-task"))
            {
                if (!task.WebSearch)
                    return await api.ChatAsync(model, messages, cancel).ConfigureAwait(false);
                var search = settings.WebSearch.Copy();
                search.Enabled = true;
                search.ShowSources = true;
                search.Validate(model);
                var searchable = api as IWebSearchConversationApi;
                if (searchable == null) throw new ApiException(TextResources.Get("webSearch.unsupported"));
                return await searchable.ChatSearchAsync(model, messages, search, null, null, null, false, cancel).ConfigureAwait(false);
            }
        }

        static ConnectionSettings ResolveModel(PetSettings settings, ScheduledTaskDefinition task)
        {
            var model = settings.Text.Copy();
            if (task != null && !String.IsNullOrEmpty(task.ProviderId))
            {
                var provider = settings.TextProviders.FirstOrDefault(p => p != null && p.Id == task.ProviderId);
                if (provider == null || provider.Connection == null || provider.Connection.Model != task.Model
                    && (provider.Models == null || !provider.Models.Contains(task.Model)))
                    throw new ApiException(TextResources.Get("scheduledTasks.missingModel"));
                model = provider.Connection.Copy();
                model.Model = task.Model;
                model.ContextWindowTokens = provider.ContextCapacity(model.Model);
                provider.ApplyModelCapabilities(model);
            }
            ApiClient.ValidateConnection(model, ApiClient.ChatPath(model));
            return model;
        }

        static ScheduledDraftReply Parse(string answer, DateTimeOffset now, TimeZoneInfo zone)
        {
            var value = (answer ?? "").Trim();
            if (value.StartsWith("```", StringComparison.Ordinal) && value.EndsWith("```", StringComparison.Ordinal))
            {
                int newline = value.IndexOf('\n');
                if (newline >= 0 && newline < value.Length - 3)
                    value = value.Substring(newline + 1, value.Length - newline - 4).Trim();
            }
            if (value.Length < 32768)
            {
                try
                {
                    var payload = new JavaScriptSerializer { MaxJsonLength = 32768, RecursionLimit = 12 }.Deserialize<DraftPayload>(value);
                    if (payload != null)
                    {
                        if (!String.IsNullOrWhiteSpace(payload.question) && payload.question.Length <= 1000)
                            return new ScheduledDraftReply { Question = payload.question.Trim() };
                        var definition = payload.task == null ? null : payload.task.ToDefinition();
                        if (definition != null && definition.IsValid && definition.Rule.Next(now, zone).HasValue)
                            return new ScheduledDraftReply { Definition = definition };
                    }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (FormatException) { }
#if PORTABLE
                catch (System.Text.Json.JsonException) { }
#endif
            }
            return new ScheduledDraftReply { Question = TextResources.Get("scheduledTasks.invalidDraft") };
        }

        public sealed class DraftPayload
        {
            public string question { get; set; }
            public DraftTask task { get; set; }
        }

        public sealed class DraftTask
        {
            public string title { get; set; }
            public string content { get; set; }
            public string kind { get; set; }
            public string frequency { get; set; }
            public string time { get; set; }
            public string date { get; set; }
            public int[] weekdays { get; set; }
            public int day { get; set; }

            internal ScheduledTaskDefinition ToDefinition()
            {
                DateTime clock;
                ScheduledFrequency repeat;
                ScheduledTaskKind taskKind;
                if (!DateTime.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out clock)
                    || !Enum.TryParse(frequency, true, out repeat) || !String.Equals(repeat.ToString(), frequency, StringComparison.OrdinalIgnoreCase)
                    || !Enum.TryParse(kind, true, out taskKind) || !String.Equals(taskKind.ToString(), kind, StringComparison.OrdinalIgnoreCase))
                    return null;
                var rule = new ScheduledTaskRule { Frequency = repeat, MinuteOfDay = clock.Hour * 60 + clock.Minute, DayOfMonth = day };
                if (repeat == ScheduledFrequency.Once)
                {
                    DateTime once;
                    if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out once)) return null;
                    rule.OnceDateTicks = once.Date.Ticks;
                }
                if (repeat == ScheduledFrequency.Weekly && weekdays != null)
                    foreach (var weekday in weekdays)
                    {
                        if (weekday < 0 || weekday > 6) return null;
                        rule.Weekdays |= 1 << weekday;
                    }
                return new ScheduledTaskDefinition { Title = title, Content = content, Kind = taskKind, Rule = rule };
            }
        }
    }
}
