using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
namespace Dafeiyu
{
    public sealed class ReleaseIdentity
    {
        public ReleaseVersion Version {get;private set;}
        public Version AssemblyVersion {get;private set;}
        public string InformationalVersion {get;private set;}
        public string CommitSha {get;private set;}
        public static ReleaseIdentity Read(string executablePath)
        {
            var numeric=AssemblyName.GetAssemblyName(executablePath).Version;
            string info=FileVersionInfo.GetVersionInfo(executablePath).ProductVersion??"";
            var match=Regex.Match(info,@"\A(.+)\+([a-f0-9]{40})\z");ReleaseVersion version;
            if(match.Success) {
                version=ReleaseVersion.Parse(match.Groups[1].Value);
                if(version.AssemblyBase!=numeric)throw new InvalidOperationException("发行版本与程序集身份不一致。");
                return new ReleaseIdentity{Version=version,AssemblyVersion=numeric,InformationalVersion=info,CommitSha=match.Groups[2].Value};
            }
            if(info.Contains("+") || !ReleaseVersion.TryParse(info,out version) || version.IsPreview) {
                if(!String.IsNullOrEmpty(info))throw new InvalidOperationException("程序发行身份无效。");
                version=ReleaseVersion.Parse(numeric.ToString());
            }
            if(version.AssemblyBase!=numeric)throw new InvalidOperationException("历史程序数字版本不一致。");
            return new ReleaseIdentity{Version=version,AssemblyVersion=numeric,InformationalVersion=info};
        }
        public void Require(string release,string numeric,string build)
        {
            if(Version.CompareTo(ReleaseVersion.Parse(release))!=0 || AssemblyVersion.ToString()!=numeric || String.IsNullOrEmpty(build) || InformationalVersion!=build)
                throw new InvalidOperationException("程序与已验证的发行构建身份不一致。");
        }
    }
}
