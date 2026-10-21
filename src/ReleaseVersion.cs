using System;
using System.Globalization;
using System.Text.RegularExpressions;
namespace Dafeiyu
{
    public enum UpdateChannel { Stable, Preview }
    public sealed class ReleaseVersion : IComparable<ReleaseVersion>,IComparable
    {
        public string Canonical {get;private set;}
        public bool IsPreview {get;private set;}
        public Version AssemblyBase {get;private set;}
        public int PreviewNumber {get;private set;}
        public static ReleaseVersion Parse(string text)
        {ReleaseVersion value;if(!TryParse(text,out value))throw new ArgumentException("无效的发行版本号。");return value;}
        public static bool TryParse(string text,out ReleaseVersion value)
        {
            value=null;if(String.IsNullOrEmpty(text)||text.Length>64)return false;
            var m=Regex.Match(text,@"\A[vV]?([0-9]+)\.([0-9]+)\.([0-9]+)(?:\.([0-9]+))?(?:-(dev)(?:\.([1-9][0-9]*))?)?\z");
            if(!m.Success || (m.Groups[4].Success&&m.Groups[5].Success))return false;
            var parts=new int[4];for(int i=0;i<4;i++)if(m.Groups[i+1].Success&&(!Int32.TryParse(m.Groups[i+1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out parts[i])||parts[i]>65534))return false;
            int dev=0;if(m.Groups[6].Success&&!Int32.TryParse(m.Groups[6].Value,NumberStyles.None,CultureInfo.InvariantCulture,out dev))return false;
            value=new ReleaseVersion{Canonical=text.TrimStart('v','V'),IsPreview=m.Groups[5].Success,PreviewNumber=dev,AssemblyBase=new Version(parts[0],parts[1],parts[2],parts[3])};return true;
        }
        public int CompareTo(ReleaseVersion other)
        {
            if(other==null)return 1;int n=AssemblyBase.CompareTo(other.AssemblyBase);if(n!=0)return n;
            if(IsPreview!=other.IsPreview)return IsPreview?-1:1;return PreviewNumber.CompareTo(other.PreviewNumber);
        }
        public int CompareTo(object other){return CompareTo((ReleaseVersion)other);}
        public override string ToString(){return Canonical;}
    }
}
