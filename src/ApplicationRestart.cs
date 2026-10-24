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
        public static bool Requested { get; set; }
        public static string Caption(string language)
        {
            return UiLanguages.Resolve(language)==TextResources.CurrentLanguage ? TextResources.Get("onboarding.language.restart") : TextResources.CatalogFor(language).Get("onboarding.language.restart");
        }
        public static ProcessStartInfo StartInfo(string executable, string entryAssembly)
        {
            bool dotnet = String.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);
            return new ProcessStartInfo(executable, (dotnet ? "\"" + entryAssembly + "\" " : "") + ResumeArgument)
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
