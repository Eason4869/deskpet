using System;
using System.Text.RegularExpressions;

namespace Dafeiyu
{
    public static class ReleaseInfo
    {
        public const string AssemblyVersion="0.10.1.0";
        public const string DisplayVersion="0.10.1-dev.1";
        public const string ProductName="桌面伙伴";
        public const string Author="彧晟Eason";
        public const string RepositoryUrl="https://github.com/Eason4869/deskpet";
        public const string InstallationMutex="Local\\Dafeiyu.DeskPet.Installation";

        // 0.5.x remains discoverable by older clients. From 0.6.0 onward the
        // installer name identifies Windows explicitly in mixed-platform releases.
        public static string InstallerAssetName(string version)
        {
            string text;Version numeric;
            if(!TryAssetVersion(version,out text,out numeric))throw new ArgumentException("Invalid release version.","version");
            return (numeric>=new Version(0,6,0)?"DeskPet-Windows-Setup-":"DeskPet-Setup-")+text+".exe";
        }
        public static string PortableAssetName(string version)
        {
            string text;Version numeric;
            if(!TryAssetVersion(version,out text,out numeric))throw new ArgumentException("Invalid release version.","version");
            return "DeskPet-Windows-"+text+".zip";
        }
        public static bool WindowsUpdateAsset(string name)
        {
            if(String.IsNullOrEmpty(name) || name.Length>150)return false;
            var match=Regex.Match(name,@"\ADeskPet-(?:Windows|(?:Windows-)?Setup)(?:-(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:-dev(?:\.[1-9][0-9]*)?)?))?\.(?<extension>zip|exe|msi)\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            if(!match.Success)return false;
            bool portable=name.StartsWith("DeskPet-Windows",StringComparison.OrdinalIgnoreCase) && !name.StartsWith("DeskPet-Windows-Setup",StringComparison.OrdinalIgnoreCase);
            if(portable != match.Groups["extension"].Value.Equals("zip",StringComparison.OrdinalIgnoreCase))return false;
            string text;Version numeric;
            return !match.Groups["version"].Success || TryAssetVersion(match.Groups["version"].Value,out text,out numeric);
        }
        // Automatic execution also binds the asset to the requested version and
        // installation mode. Both names are accepted during the migration.
        public static bool WindowsUpdateAsset(string name,string version,bool portable)
        {
            string text;Version numeric;
            if(!TryAssetVersion(version,out text,out numeric))return false;
            if(portable)return name=="DeskPet-Windows-"+text+".zip";
            return name=="DeskPet-Setup-"+text+".exe" || name=="DeskPet-Windows-Setup-"+text+".exe";
        }
        static bool TryAssetVersion(string version,out string text,out Version numeric)
        {
            text=(version??"").TrimStart('v','V');numeric=null;ReleaseVersion parsed;
            if(!ReleaseVersion.TryParse(text,out parsed))return false;numeric=parsed.AssemblyBase;return true;
        }
    }
}
