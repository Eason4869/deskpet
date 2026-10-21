using System;
using System.Globalization;

namespace Dafeiyu
{
    public sealed class OnboardingUpgradeResult
    {
        public bool Changed {get;private set;}
        public bool ShowGuide {get;private set;}
        internal OnboardingUpgradeResult(bool changed,bool showGuide){Changed=changed;ShowGuide=showGuide;}
    }

    // Call immediately after loading settings and save Changed before opening a
    // window. Recording the trigger, rather than completion, also deduplicates a
    // closed guide and preserves the high-water mark across application downgrades.
    public static class OnboardingUpgrade
    {
        public const string LegacySeries="0.5";
        public const int WorkGuideVersion=2;
        public const int WorkGuideStep=14;
        public static OnboardingUpgradeResult Prepare(PetSettings settings,string version)
        {
            if(settings==null)throw new ArgumentNullException("settings");
            ReleaseVersion releaseVersion;
            if(ReleaseVersion.TryParse(version,out releaseVersion))version=releaseVersion.AssemblyBase.ToString();
            Version current;
            if(!TrySeries(version,out current))return new OnboardingUpgradeResult(false,false);
            bool first=String.IsNullOrEmpty(settings.LastOnboardingSeries) && settings.OnboardingState=="new";
            Version recorded;
            if(!TrySeries(settings.LastOnboardingSeries,out recorded))recorded=new Version(0,5);
            bool seriesUpgrade=current.CompareTo(recorded)>0;
            Version release;Version.TryParse(version.Trim(),out release);
            bool hasWorkGuide=release.CompareTo(new Version(0,6,1))>=0;
            // One content revision within 0.6 starts at the new lessons; future
            // patches stay silent and downgrades never consume another guide.
            bool workUpgrade=!first && !seriesUpgrade && current.Equals(new Version(0,6)) && recorded.Equals(current) && hasWorkGuide && settings.OnboardingVersion<WorkGuideVersion;
            bool show=first || seriesUpgrade || workUpgrade;
            string series=Format(show?current:recorded);
            bool changed=settings.LastOnboardingSeries!=series;
            settings.LastOnboardingSeries=series;
            if(show) {
                settings.OnboardingLessonId="";settings.OnboardingCompletedLessons.Clear();settings.OnboardingSkippedLessons.Clear();settings.OnboardingVerifiedLessons.Clear();settings.OnboardingState="new";settings.OnboardingStep=workUpgrade?WorkGuideStep:0;
                settings.OnboardingVersion=Math.Max(settings.OnboardingVersion,hasWorkGuide?WorkGuideVersion:1);
                changed=true;
            }
            return new OnboardingUpgradeResult(changed,show);
        }
        static bool TrySeries(string value,out Version series)
        {
            series=null;
            // Reject corrupt input before parsing, including unbounded digit runs.
            if(String.IsNullOrWhiteSpace(value) || value.Length>128)return false;
            Version parsed;if(!Version.TryParse(value.Trim(),out parsed))return false;
            series=new Version(parsed.Major,parsed.Minor);return true;
        }
        static string Format(Version value){return value.Major.ToString(CultureInfo.InvariantCulture)+"."+value.Minor.ToString(CultureInfo.InvariantCulture);}
    }
}
