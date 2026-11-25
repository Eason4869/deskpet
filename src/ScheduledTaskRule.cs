using System;
using System.Linq;
using System.Runtime.Serialization;

namespace Dafeiyu
{
    public enum ScheduledFrequency { Once, Daily, Weekly, Monthly }
    public enum ScheduledTaskKind { Reminder, Ai }
    public enum ScheduledNotification { All, Failures, None }
    public enum ScheduledRunState { Pending, Queued, Running, Succeeded, Failed, Skipped, Interrupted }

    [DataContract]
    public sealed class ScheduledTaskRule
    {
        [DataMember] public ScheduledFrequency Frequency = ScheduledFrequency.Daily;
        [DataMember] public int MinuteOfDay = 540;
        [DataMember] public long OnceDateTicks;
        [DataMember] public int Weekdays;
        [DataMember] public int DayOfMonth = 1;

        public bool IsValid
        {
            get
            {
                return Enum.IsDefined(typeof(ScheduledFrequency), Frequency) && MinuteOfDay >= 0 && MinuteOfDay < 1440
                    && (Frequency != ScheduledFrequency.Once || OnceDateTicks > 0 && OnceDateTicks < DateTime.MaxValue.Date.Ticks && OnceDateTicks % TimeSpan.TicksPerDay == 0)
                    && (Frequency != ScheduledFrequency.Weekly || Weekdays > 0 && Weekdays < 128)
                    && (Frequency != ScheduledFrequency.Monthly || DayOfMonth >= 1 && DayOfMonth <= 31);
            }
        }

        public ScheduledTaskRule Copy() { return (ScheduledTaskRule)MemberwiseClone(); }

        internal bool SameSchedule(ScheduledTaskRule other)
        {
            return Frequency == other.Frequency && MinuteOfDay == other.MinuteOfDay
                && (Frequency != ScheduledFrequency.Once || OnceDateTicks == other.OnceDateTicks)
                && (Frequency != ScheduledFrequency.Weekly || Weekdays == other.Weekdays)
                && (Frequency != ScheduledFrequency.Monthly || DayOfMonth == other.DayOfMonth);
        }

        public ScheduledOccurrence? Next(DateTimeOffset afterUtc, TimeZoneInfo zone, long afterCivilTicks = 0)
        {
            if (!IsValid) return null;
            if (Frequency == ScheduledFrequency.Once)
            {
                var once = OnDate(new DateTime(OnceDateTicks), zone);
                return once.Utc > afterUtc && once.CivilTicks > afterCivilTicks ? (ScheduledOccurrence?)once : null;
            }

            var date = TimeZoneInfo.ConvertTime(afterUtc, zone).Date;
            if (afterCivilTicks > date.Ticks)
                date = new DateTime(afterCivilTicks).Date;
            return Search(date, 1, zone, actual => actual.Utc > afterUtc && actual.CivilTicks > afterCivilTicks);
        }

        internal ScheduledOccurrence? Latest(DateTimeOffset now, TimeZoneInfo zone)
        {
            if (!IsValid) return null;
            if (Frequency == ScheduledFrequency.Once)
            {
                var once = OnDate(new DateTime(OnceDateTicks), zone);
                return once.Utc <= now ? (ScheduledOccurrence?)once : null;
            }
            return Search(TimeZoneInfo.ConvertTime(now, zone).Date, -1, zone, actual => actual.Utc <= now);
        }

        ScheduledOccurrence? Search(DateTime date, int direction, TimeZoneInfo zone, Func<ScheduledOccurrence, bool> accepts)
        {
            // A monthly rule on the 31st can span two months between occurrences.
            for (int i = 0; i < 93; i++)
            {
                if (Matches(date))
                {
                    var occurrence = OnDate(date, zone);
                    if (accepts(occurrence)) return occurrence;
                }
                if (direction > 0 && date == DateTime.MaxValue.Date || direction < 0 && date == DateTime.MinValue.Date)
                    break;
                date = date.AddDays(direction);
            }
            return null;
        }

        bool Matches(DateTime date)
        {
            if (Frequency == ScheduledFrequency.Weekly) return (Weekdays & 1 << (int)date.DayOfWeek) != 0;
            if (Frequency == ScheduledFrequency.Monthly) return date.Day == DayOfMonth;
            return true;
        }

        ScheduledOccurrence OnDate(DateTime date, TimeZoneInfo zone)
        {
            var civil = DateTime.SpecifyKind(date.AddMinutes(MinuteOfDay), DateTimeKind.Unspecified);
            var effective = civil;
            while (zone.IsInvalidTime(effective)) effective = effective.AddMinutes(1);
            var offset = zone.IsAmbiguousTime(effective) ? zone.GetAmbiguousTimeOffsets(effective).Max() : zone.GetUtcOffset(effective);
            return new ScheduledOccurrence(civil.Ticks, new DateTimeOffset(effective, offset).ToUniversalTime());
        }
    }

    public struct ScheduledOccurrence
    {
        public readonly long CivilTicks;
        public readonly DateTimeOffset Utc;

        public ScheduledOccurrence(long civilTicks, DateTimeOffset utc)
        {
            CivilTicks = civilTicks;
            Utc = utc;
        }
    }

    [DataContract]
    public sealed class ScheduledTaskDefinition
    {
        [DataMember] public string Id;
        [DataMember] public string Title;
        [DataMember] public string Content;
        [DataMember] public ScheduledTaskKind Kind;
        [DataMember] public ScheduledNotification Notification;
        [DataMember] public ScheduledTaskRule Rule = new ScheduledTaskRule();
        [DataMember] public bool Enabled = true;
        [DataMember] public string ProviderId;
        [DataMember] public string Model;
        [DataMember] public bool WebSearch;

        public ScheduledTaskDefinition Copy()
        {
            var copy = (ScheduledTaskDefinition)MemberwiseClone();
            copy.Rule = Rule == null ? null : Rule.Copy();
            return copy;
        }

        internal bool IsValid
        {
            get
            {
                return !String.IsNullOrWhiteSpace(Title) && Title.Length <= 160
                    && !String.IsNullOrWhiteSpace(Content) && Content.Length <= 16000
                    && Rule != null && Rule.IsValid && Enum.IsDefined(typeof(ScheduledTaskKind), Kind)
                    && Enum.IsDefined(typeof(ScheduledNotification), Notification)
                    && String.IsNullOrEmpty(ProviderId) == String.IsNullOrEmpty(Model);
            }
        }
    }

    [DataContract]
    public sealed class ScheduledTaskRun
    {
        [DataMember] public string Id;
        [DataMember] public string TaskId;
        [DataMember] public string Title;
        [DataMember] public DateTimeOffset DueUtc;
        [DataMember] public long CivilTicks;
        [DataMember] public DateTimeOffset UpdatedUtc;
        [DataMember] public ScheduledRunState State;
        [DataMember] public string Result;

        internal bool Active
        {
            get { return State == ScheduledRunState.Pending || State == ScheduledRunState.Queued || State == ScheduledRunState.Running; }
        }

        internal ScheduledTaskRun Copy() { return (ScheduledTaskRun)MemberwiseClone(); }
    }
}
