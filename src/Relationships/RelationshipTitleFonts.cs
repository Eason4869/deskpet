using System;
using System.Collections.Generic;
namespace Dafeiyu
{
    // Only keepsake headings/stage titles use these subsets. Never apply to names/body/dates.
    public sealed class RelationshipTitleFontAsset
    {
        readonly HashSet<int> codepoints;
        public string Region { get; private set; }
        public string FileName { get; private set; }
        public string FamilyName { get; private set; }
        internal RelationshipTitleFontAsset(string region,string file,string family,int[] points)
        {Region=region;FileName=file;FamilyName=family;codepoints=new HashSet<int>(points);}
        public bool Covers(string title)
        {
            if(title==null)return false;
            foreach(int point in RelationshipTitleFonts.CodePoints(title))if(!codepoints.Contains(point))return false;
            return true;
        }
    }
    public static partial class RelationshipTitleFonts
    {
        public const string Version="relationship-title-serif-1";
        static readonly RelationshipTitleFontAsset[] assets=CreateAssets();
        public static RelationshipTitleFontAsset ForLocale(string locale)
        {
            string region="Latin";
            switch((locale??"").Replace('-','_').ToLowerInvariant()) {
                case "zh_cn": case "zh_hans": region="SC";break;
                case "zh_tw": case "zh_hant": region="TC";break;
                case "ja": case "ja_jp": region="JP";break;
                case "ko": case "ko_kr": region="KR";break;
                case "hi": case "hi_in": region="Devanagari";break;
            }
            foreach(var asset in assets)if(asset.Region==region)return asset;
            throw new InvalidOperationException("Built-in title font manifest is incomplete.");
        }
        public static IEnumerable<int> CodePoints(string text)
        {
            if(text==null)yield break;
            for(int i=0;i<text.Length;i++) {
                char c=text[i];if(c=='\r'||c=='\n')continue;
                if(Char.IsHighSurrogate(c)&&i+1<text.Length&&Char.IsLowSurrogate(text[i+1]))yield return Char.ConvertToUtf32(c,text[++i]);
                else yield return c;
            }
        }
    }
}
