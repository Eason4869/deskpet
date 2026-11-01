using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public interface IClock
    {
        DateTime UtcNow { get; }
        Task Delay(TimeSpan duration, CancellationToken cancel);
    }

    public sealed class DesktopObservationClock : IClock
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
        public Task Delay(TimeSpan duration, CancellationToken cancel) { return Task.Delay(duration, cancel); }
    }

    public enum DesktopObservationMode { Automatic, ManualTest }
    // Stable codes for host localization. No native identity, provider error or settings text.
    public enum ObservationReason
    {
        Allowed, Disabled, NotDue, Ineligible, Stale, NoRoute, InvalidConfiguration,
        ConsentRequired, MinimumInterval, HourlyLimit, Cooldown, Busy, CaptureUnavailable,
        CaptureTimeout, NoFrame, Excluded, DuplicateScene, ModelSkip, DuplicateMessage,
        ModelFailed, InvalidResponse, ModelTimeout, TotalTimeout, Cancelled
    }

    // Construct on the host with detached data and a fast, thread-safe epoch predicate.
    // The predicate must cover foreground, companion/relationship/persona, model/endpoint/
    // capability, language, enablement, privacy and chat activity. Never read UI controls here.
    public sealed class DesktopObservationContext
    {
        internal readonly PetSettings Settings;
        internal readonly ImageRouteSnapshot Route;
        internal readonly IList<ChatMessage> Memory;
        internal readonly IList<string> Recent;
        internal readonly Func<bool> Current;
        public string CompanionId { get; private set; }
        public string CompanionName { get; private set; }
        public DateTime LocalTime { get; private set; }
        public bool Due { get; private set; }
        public bool Eligible { get; private set; }
        public DesktopObservationMode Mode { get; private set; }
        public LanguageSelection Language { get; private set; }
        public ImageRouteKind RouteKind { get { return Route.Route; } }

        public DesktopObservationContext(PetSettings settings, string companionId, string companionName,
            IList<ChatMessage> permittedMemory, IList<string> recentShown, DateTime localTime,
            bool due, bool eligible, Func<bool> isCurrent, DesktopObservationMode mode = DesktopObservationMode.Automatic)
        {
            if (settings == null || isCurrent == null) throw new ArgumentNullException();
            if (String.IsNullOrWhiteSpace(companionId)) throw new ArgumentException("Companion ID is required.");
            Settings = settings.Copy(); Route = ImageModelResolver.Resolve(Settings, null);
            CompanionId = companionId; CompanionName = Limit(companionName, 120); LocalTime = localTime;
            Due = due; Eligible = eligible; Current = isCurrent; Mode = mode;
            Language = LanguageRequestScope.Capture();
            Memory = CompanionMemory.Recent(permittedMemory);
            Recent = (recentShown ?? new string[0]).Where(s => s != null).Take(16).Select(s => Limit(s, 80)).ToArray();
        }
        internal static string Limit(string value, int maximum)
        {
            value = value ?? "";
            if (value.Length <= maximum) return value;
            if (Char.IsHighSurrogate(value[maximum - 1])) maximum--;
            return value.Substring(0, maximum);
        }
    }

    public static class DesktopObservationConsent
    {
        public const int Version = 1;
        // Binds consent to every destination receiving screenshot or derived content, including
        // protocol and API path. Only the digest is persisted; no credentials are duplicated.
        public static string Destination(PetSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            return Destination(ImageModelResolver.Resolve(settings, null));
        }
        internal static string Destination(ImageRouteSnapshot route)
        {
            if (route.Route == ImageRouteKind.Unavailable) return "";
            string value = Endpoint(route.ConversationConnection);
            if (route.Route == ImageRouteKind.Described) value += "\n" + Endpoint(route.DescriptionConnection);
            using (var hash = SHA256.Create()) return "v1:" + Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        static string Endpoint(ConnectionSettings connection)
        {
            return (connection.Protocol ?? "").Trim().ToLowerInvariant() + "\n" +
                ApiClient.Endpoint(connection, ApiClient.ChatPath(connection)).AbsoluteUri;
        }
        public static bool IsConfirmed(PetSettings settings)
        {
            try { return settings != null && settings.DesktopObservationConsentVersion == Version &&
                settings.DesktopObservationConsentDestination == Destination(settings) && Destination(settings).Length > 0; }
            catch (ApiException) { return false; }
        }
    }

    public sealed class ObservationAdmission
    {
        public bool Allowed { get { return Reason == ObservationReason.Allowed; } }
        public ObservationReason Reason { get; private set; }
        public int AutomaticCallsLastHour { get; private set; }
        public int ConsecutiveFailures { get; private set; }
        public DateTime? RetryAtUtc { get; private set; }
        internal ObservationAdmission(ObservationReason reason, int count, int failures, DateTime? retry = null)
        { Reason = reason; AutomaticCallsLastHour = count; ConsecutiveFailures = failures; RetryAtUtc = retry; }
    }

    // Application lifetime, shared by all companions and automatic requests. Admission is
    // read-only; the single-flight coordinator charges quota immediately before its first call.
    public sealed class DesktopObservationPolicy
    {
        readonly object gate = new object();
        readonly List<DateTime> attempts = new List<DateTime>();
        readonly Dictionary<string, DateTime> scenes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        DateTime? lastAttempt, cooldown;
        int failures;
        long manualTests;
        public long ManualTestCount { get { lock (gate) return manualTests; } }

        public ObservationAdmission TryBegin(DesktopObservationContext context, DateTime now)
        {
            if (context == null) throw new ArgumentNullException("context");
            now = now.ToUniversalTime();
            // Current is deliberately outside the policy lock; no callback holds shared state.
            bool current = context.Current();
            lock (gate)
            {
                attempts.RemoveAll(t => now >= t && now - t >= TimeSpan.FromHours(1));
                var settings = context.Settings;
                ObservationReason reason = ObservationReason.Allowed;
                if (!settings.DesktopObservationEnabled || !settings.GreetingEnabled || !settings.AiGreetingEnabled) reason = ObservationReason.Disabled;
                else if (!context.Eligible) reason = ObservationReason.Ineligible;
                else if (!current) reason = ObservationReason.Stale;
                else if (context.Mode == DesktopObservationMode.Automatic && !context.Due) reason = ObservationReason.NotDue;
                else if (context.Route.Route == ImageRouteKind.Unavailable) reason = ObservationReason.NoRoute;
                else
                {
                    try
                    {
                        ApiClient.ValidateConnection(context.Route.ConversationConnection, ApiClient.ChatPath(context.Route.ConversationConnection));
                        if (context.Route.Route == ImageRouteKind.Described)
                            ApiClient.ValidateConnection(context.Route.DescriptionConnection, ApiClient.ChatPath(context.Route.DescriptionConnection));
                        if (settings.DesktopObservationConsentVersion != DesktopObservationConsent.Version ||
                            settings.DesktopObservationConsentDestination != DesktopObservationConsent.Destination(context.Route)) reason = ObservationReason.ConsentRequired;
                    }
                    catch (ApiException) { reason = ObservationReason.InvalidConfiguration; }
                }
                if (reason != ObservationReason.Allowed) return new ObservationAdmission(reason, attempts.Count, failures);
                if (context.Mode == DesktopObservationMode.ManualTest) return new ObservationAdmission(reason, attempts.Count, failures);
                if (cooldown.HasValue && now < cooldown.Value) return new ObservationAdmission(ObservationReason.Cooldown, attempts.Count, failures, cooldown);
                if (attempts.Count >= 4) return new ObservationAdmission(ObservationReason.HourlyLimit, attempts.Count, failures, attempts.Min().AddHours(1));
                if (lastAttempt.HasValue && now < lastAttempt.Value.AddMinutes(10)) return new ObservationAdmission(ObservationReason.MinimumInterval, attempts.Count, failures, lastAttempt.Value.AddMinutes(10));
                return new ObservationAdmission(reason, attempts.Count, failures);
            }
        }
        public void RecordModelAttempt(DesktopObservationContext context, DateTime now)
        {
            lock (gate)
            {
                if (context.Mode == DesktopObservationMode.ManualTest) { manualTests++; return; }
                now = now.ToUniversalTime(); attempts.Add(now); lastAttempt = now;
            }
        }
        public void RecordFailure(DateTime now)
        {
            lock (gate) { failures = Math.Min(3, failures + 1); cooldown = now.ToUniversalTime().AddMinutes(failures == 1 ? 10 : failures == 2 ? 30 : 60); }
        }
        public void RecordSuccess() { lock (gate) { failures = 0; cooldown = null; } }
        public bool TryObserveScene(string fingerprint, DateTime now)
        {
            if (String.IsNullOrEmpty(fingerprint)) throw new ArgumentException("Scene fingerprint is required.");
            now = now.ToUniversalTime();
            lock (gate)
            {
                foreach (var key in scenes.Where(p => now >= p.Value && now - p.Value >= TimeSpan.FromMinutes(30)).Select(p => p.Key).ToArray()) scenes.Remove(key);
                if (scenes.ContainsKey(fingerprint)) return false;
                while (scenes.Count >= 128) scenes.Remove(scenes.OrderBy(p => p.Value).First().Key);
                scenes.Add(fingerprint, now); return true;
            }
        }
    }
}
