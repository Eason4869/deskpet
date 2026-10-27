using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Dafeiyu;
namespace DeskPet.Desktop.Tests;
internal static class RelationshipTitleFontChecks
{
    public static int Run(string directory)
    {
        directory=Path.Combine(directory,"avalonia");Directory.CreateDirectory(directory);
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label);}
        var keys=new[]{"relationship.stage.firstMeeting","relationship.stage.familiar","relationship.stage.close","relationship.stage.trusted","relationship.stage.heartfelt","relationship.stage.bestFriend"}.Concat(new[]{"relationship.card.title","relationship.card.halloweenTitle","holidayKeepsake.historyTitle"}).ToArray();
        foreach(var locale in UiLanguages.Choices.Where(c=>c.Code!="system")) {
            var asset=RelationshipTitleFonts.ForLocale(locale.Code);
            using(var resource=AssetLoader.Open(new Uri("avares://DeskPet/Assets/fonts/relationships/"+asset.FileName))) {
                Check(SHA256.HashData(resource).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine("assets/fonts/relationships",asset.FileName)))),"real production avares font bytes "+asset.Region);
            }
            using var catalogStream=typeof(TextResources).Assembly.GetManifestResourceStream("Dafeiyu.Locales."+locale.Code+".json")!;
            using var catalog=JsonDocument.Parse(catalogStream);
            var titles=keys.Select(key=>catalog.RootElement.GetProperty(key).GetString()!).ToArray();
            foreach(string title in titles) {
                Check(RelationshipTitleFont.TryGetBundledTypeface(locale.Code,title,out var face),"real local font + glyph coverage "+locale.Code+" "+title);
                Check(FontManager.Current.TryGetGlyphTypeface(face,out var glyph)&&glyph.FamilyName==asset.FamilyName,"exact renamed family "+locale.Code);
            }
            foreach(bool dark in new[]{false,true}) {
                var panel=new StackPanel{Width=960,Height=800,Background=dark?Brushes.MidnightBlue:Brushes.White};
                foreach(string title in titles)panel.Children.Add(new TextBlock{Text=title,FontFamily=RelationshipTitleFont.FontForLocale(locale.Code,title),FontSize=36,Height=96,Foreground=dark?Brushes.LightGoldenrodYellow:Brushes.MidnightBlue,Margin=new Thickness(24,0,24,0),TextWrapping=TextWrapping.Wrap});
                panel.Measure(new Size(960,800));panel.Arrange(new Rect(0,0,960,800));
                using var bitmap=new RenderTargetBitmap(new PixelSize(960,800),new Vector(96,96));
                bitmap.Render(panel);bitmap.Save(Path.Combine(directory,locale.Code+(dark?"-dark":"-light")+".png"),PngBitmapEncoderOptions.Default);
            }
        }
        foreach(string license in new[]{"OFL-CJK.txt","OFL-Latin.txt","OFL-Devanagari.txt"}) {
            using var stream=AssetLoader.Open(new Uri("avares://DeskPet/Assets/fonts/relationships/"+license));
            using var reader=new StreamReader(stream);
            var text=reader.ReadToEnd();Check(text.Contains("SIL OPEN FONT LICENSE")&&text.Contains("Copyright"),"real bundled OFL/copyright "+license);
        }
        var sc=RelationshipTitleFonts.ForLocale("zh_CN");
        Check(!RelationshipTitleFont.TryResolve(new FontFamily("avares://DeskPet/Missing#DeskPet Keepsake SC"),sc,"初次见面",out _),"missing resource rejected without silent substitute");
        Check(!RelationshipTitleFont.TryResolve(new FontFamily("avares://DeskPet/Assets/fonts/relationships#Invalid Family"),sc,"初次见面",out _),"invalid family rejected");
        Check(!RelationshipTitleFont.TryGetBundledTypeface("zh_CN","未来🦄",out _),"unsupported scalar falls back");
        Check(RelationshipTitleFont.FontForLocale("zh_CN","未来🦄")==FontFamily.Default,"fallback is readable local UI family");
        Check(!sc.Covers("\ud800"),"unpaired surrogate rejected");
        using(var stream=typeof(TextResources).Assembly.GetManifestResourceStream("Dafeiyu.Locales.hi_IN.json")!) {
            using var catalog=JsonDocument.Parse(stream);string hindi=catalog.RootElement.GetProperty("relationship.card.title").GetString()!;
            var text=new TextBlock{Text=hindi,FontFamily=RelationshipTitleFont.FontForLocale("hi_IN",hindi),FontSize=40,Width=900,Height=100};
            text.Measure(new Size(900,100));text.Arrange(new Rect(0,0,900,100));
            var runs=text.TextLayout.TextLines.SelectMany(line=>line.TextRuns).OfType<ShapedTextRun>().ToArray();
            Check(runs.Length>0&&runs.All(r=>r.GlyphRun.GlyphTypeface.FamilyName=="DeskPet Keepsake Devanagari"),"Hindi TextBlock shaping uses bundled face throughout");
            var shaped=runs.SelectMany(r=>r.GlyphRun.GlyphInfos.Select(info=>info.GlyphIndex)).ToArray();
            if(!FontManager.Current.TryGetGlyphTypeface(RelationshipTitleFont.GetTypeface("hi_IN",hindi),out var face))throw new Exception("Hindi face unavailable");
            var direct=RelationshipTitleFonts.CodePoints(hindi).Select(face.CharacterToGlyphMap.GetGlyph).ToArray();
            Check(!shaped.SequenceEqual(direct)&&shaped.All(id=>id!=0),"Hindi actual TextBlock shaped glyph sequence differs from direct cmap");
        }
        Console.WriteLine("26 native title sheets at "+directory);
        return count;
    }
}
