using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Dafeiyu
{
    // Launch only after normal shutdown (and the Windows instance mutex release).
    public static class ApplicationRestart
    {
        public const string ResumeArgument = "--resume-onboarding";
        static bool requested,resumeGuide=true;
        public static bool Requested { get{return requested;} set{requested=value;if(value)resumeGuide=true;} }
        public static void Request(bool resumeOnboarding){Requested=true;resumeGuide=resumeOnboarding;}
        public static string Caption(string language)
        {
            return UiLanguages.Resolve(language)==TextResources.CurrentLanguage ? TextResources.Get("onboarding.language.restart") : TextResources.CatalogFor(language).Get("onboarding.language.restart");
        }
        public static ProcessStartInfo StartInfo(string executable, string entryAssembly)
        {return StartInfo(executable,entryAssembly,resumeGuide);}
        public static ProcessStartInfo StartInfo(string executable, string entryAssembly,bool resumeOnboarding)
        {
            bool dotnet = String.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);
            return new ProcessStartInfo(executable, (dotnet ? "\"" + entryAssembly + "\"" : "") + (resumeOnboarding ? (dotnet ? " " : "")+ResumeArgument : ""))
            { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(entryAssembly) };
        }
        public static void Launch()
        {
            string entry = Assembly.GetEntryAssembly().Location;
            using (var current = Process.GetCurrentProcess())
            using (Process.Start(StartInfo(current.MainModule.FileName, entry))) { }
        }
    }
}
