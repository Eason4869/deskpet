using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
namespace Dafeiyu
{
    // A narrow local-card helper; no global font registration or system installation.
    public static class RelationshipTitleFont
    {
        public static FontFamily FontForLocale(string locale,string title){return GetTypeface(locale,title).FontFamily;}
        public static Typeface GetTypeface(string locale,string title)
        {
            Typeface face;
            return TryGetBundledTypeface(locale,title,out face)?face:new Typeface(SystemFonts.MessageFontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        }
        public static bool TryGetBundledTypeface(string locale,string title,out Typeface face)
        {
            var asset=RelationshipTitleFonts.ForLocale(locale);
            var root=new Uri("pack://application:,,,/"+typeof(RelationshipTitleFont).Assembly.GetName().Name+";component/");
            return TryResolve(new FontFamily(root,"./assets/fonts/relationships/#"+asset.FamilyName),asset,title,out face);
        }
        internal static bool TryResolve(FontFamily family,RelationshipTitleFontAsset asset,string title,out Typeface face)
        {
            face=null;if(!asset.Covers(title))return false;
            try {
                var candidate=new Typeface(family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);GlyphTypeface glyph;
                if(!candidate.TryGetGlyphTypeface(out glyph))return false;
                bool exact=false;foreach(var name in glyph.FamilyNames.Values)if(name==asset.FamilyName)exact=true;
                if(!exact)return false; // TryGetGlyphTypeface alone may return a system substitute.
                foreach(int point in RelationshipTitleFonts.CodePoints(title)){ushort id;if(!glyph.CharacterToGlyphMap.TryGetValue(point,out id)||id==0)return false;}
                face=candidate;return true;
            } catch(IOException){return false;}
              catch(ArgumentException){return false;}
              catch(InvalidOperationException){return false;}
              catch(NotSupportedException){return false;}
        }
    }
}
