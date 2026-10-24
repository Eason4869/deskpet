using Avalonia.Media;
using Dafeiyu;
namespace DeskPet.Desktop;
// Restrict to card headings/stage titles. FontManager owns cached platform typefaces.
public static class RelationshipTitleFont
{
    public static FontFamily FontForLocale(string? locale,string title)=>GetTypeface(locale,title).FontFamily;
    public static Typeface GetTypeface(string? locale,string title)=>TryGetBundledTypeface(locale,title,out var face)?face:new Typeface(FontFamily.Default);
    public static bool TryGetBundledTypeface(string? locale,string title,out Typeface face)
    {
        var asset=RelationshipTitleFonts.ForLocale(locale);
        return TryResolve(new FontFamily("avares://DeskPet/Assets/fonts/relationships#"+asset.FamilyName),asset,title,out face);
    }
    internal static bool TryResolve(FontFamily family,RelationshipTitleFontAsset asset,string title,out Typeface face)
    {
        face=default;if(!asset.Covers(title))return false;
        try {
            var candidate=new Typeface(family);
            if(!FontManager.Current.TryGetGlyphTypeface(candidate,out var glyph)||glyph.FamilyName!=asset.FamilyName)return false;
            foreach(int point in RelationshipTitleFonts.CodePoints(title))if(glyph.CharacterToGlyphMap.GetGlyph(point)==0)return false;
            face=candidate;return true;
        } catch(IOException){return false;}
          catch(ArgumentException){return false;}
          catch(InvalidOperationException){return false;}
          catch(NotSupportedException){return false;}
    }
}
