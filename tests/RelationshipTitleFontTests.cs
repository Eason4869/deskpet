using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.Security.Cryptography;
namespace Dafeiyu.Tests
{
    public static class RelationshipTitleFontTests
    {
        [STAThread] public static int Main(string[] args)
        {
            try {
                if(!args.Contains("--proof")){Console.WriteLine("WPF title UI checks deferred to owned inactive fixture: --proof");return 0;}
                new Application();
                Run();
                string production=args.FirstOrDefault(a=>a.StartsWith("--production="));
                if(production!=null)CheckProduction(production.Substring("--production=".Length));
                return 0;
            } catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        static int count;
        static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);count++;Console.WriteLine("PASS "+label);}
        static string[] Keys(){return new[]{"relationship.stage.firstMeeting","relationship.stage.familiar","relationship.stage.close","relationship.stage.trusted","relationship.stage.heartfelt","relationship.stage.bestFriend"}.Concat(new[]{"relationship.card.title","relationship.card.halloweenTitle","holidayKeepsake.historyTitle"}).ToArray();}
        static IEnumerable<GlyphRun> Glyphs(Drawing drawing)
        {
            var run=drawing as GlyphRunDrawing;if(run!=null)yield return run.GlyphRun;
            var group=drawing as DrawingGroup;if(group!=null)foreach(var child in group.Children)foreach(var glyph in Glyphs(child))yield return glyph;
        }
        static void CheckProduction(string path)
        {
            // Load the built App for inspection only. Never launch its entry point.
            var assembly=Assembly.LoadFrom(Path.GetFullPath(path));
            Check(assembly.GetName().Name==Path.GetFileNameWithoutExtension(path),"production output/assembly basename matches");
            using(var stream=assembly.GetManifestResourceStream(assembly.GetName().Name+".g.resources")) {
                Check(stream!=null,"actual production App contains matching .g.resources");
                using(var reader=new ResourceReader(stream)) {
                    var entries=new Dictionary<string,object>();foreach(System.Collections.DictionaryEntry pair in reader)entries.Add((string)pair.Key,pair.Value);
                    foreach(string locale in new[]{"zh_CN","zh_TW","ja_JP","ko_KR","hi_IN","en_US"}) {
                        var asset=RelationshipTitleFonts.ForLocale(locale);var key="assets/fonts/relationships/"+asset.FileName;
                        Check(entries.ContainsKey(key)&&entries[key] is Stream,"production Resource stream "+asset.Region);
                        using(var sha=SHA256.Create())Check(sha.ComputeHash((Stream)entries[key]).SequenceEqual(sha.ComputeHash(File.ReadAllBytes(Path.Combine("assets/fonts/relationships",asset.FileName)))),"production exact font bytes "+asset.Region);
                        object[] args=new object[]{locale,TextResources.CatalogFor(locale).Get("relationship.card.title"),null};
                        bool resolved=(bool)assembly.GetType("Dafeiyu.RelationshipTitleFont").GetMethod("TryGetBundledTypeface").Invoke(null,args);
                        GlyphTypeface actual;
                        Check(resolved&&((Typeface)args[2]).TryGetGlyphTypeface(out actual)&&actual.FamilyNames.Values.Contains(asset.FamilyName),"production font family resolved from actual Unicode assembly basename "+asset.Region);
                    }
                    foreach(string license in new[]{"ofl-cjk.txt","ofl-latin.txt","ofl-devanagari.txt"})Check(entries.ContainsKey("assets/fonts/relationships/"+license),"production embedded OFL "+license);
                }
            }
            Console.WriteLine("Production WPF resource proof passed; total assertions "+count);
        }
        static void Run()
        {
            var output=Path.GetFullPath("work/localization-completion/title-proofs/wpf");Directory.CreateDirectory(output);
            var assembly=typeof(RelationshipTitleFont).Assembly;
            using(var stream=assembly.GetManifestResourceStream(assembly.GetName().Name+".g.resources")) {
                Check(stream!=null,"actual fixture assembly has correctly named .g.resources");
                using(var reader=new ResourceReader(stream)) {
                    var entries=new Dictionary<string,object>();foreach(System.Collections.DictionaryEntry pair in reader)entries.Add((string)pair.Key,pair.Value);
                    foreach(var locale in UiLanguages.Choices.Where(c=>c.Code!="system")) {
                        var asset=RelationshipTitleFonts.ForLocale(locale.Code);string key="assets/fonts/relationships/"+asset.FileName;
                        Check(entries.ContainsKey(key)&&entries[key] is Stream,"WPF Resource stream "+asset.Region);
                        using(var hash=SHA256.Create()) {
                            ((Stream)entries[key]).Position=0;var bundled=hash.ComputeHash((Stream)entries[key]);
                            var source=hash.ComputeHash(File.ReadAllBytes(Path.Combine("assets/fonts/relationships",asset.FileName)));
                            Check(bundled.SequenceEqual(source),"actual WPF font bytes "+asset.Region);
                        }
                    }
                    foreach(string license in new[]{"ofl-cjk.txt","ofl-latin.txt","ofl-devanagari.txt"})Check(entries.ContainsKey("assets/fonts/relationships/"+license),"embedded license "+license);
                }
            }
            foreach(var locale in UiLanguages.Choices.Where(c=>c.Code!="system")) {
                var asset=RelationshipTitleFonts.ForLocale(locale.Code);var catalog=TextResources.CatalogFor(locale.Code);
                var titles=Keys().Select(catalog.Get).ToArray();
                foreach(var title in titles) {
                    Typeface face;Check(RelationshipTitleFont.TryGetBundledTypeface(locale.Code,title,out face),"real native family + glyph coverage "+locale.Code+" "+title);
                    GlyphTypeface glyph;Check(face.TryGetGlyphTypeface(out glyph)&&glyph.FamilyNames.Values.Contains(asset.FamilyName),"exact renamed family "+locale.Code);
                }
                foreach(bool dark in new[]{false,true}) {
                    var panel=new StackPanel{Width=960,Height=800,Background=dark?Brushes.MidnightBlue:Brushes.White};
                    foreach(string title in titles)panel.Children.Add(new TextBlock{Text=title,FontFamily=RelationshipTitleFont.FontForLocale(locale.Code,title),FontSize=36,Height=96,Foreground=dark?Brushes.LightGoldenrodYellow:Brushes.MidnightBlue,Margin=new Thickness(24,0,24,0),TextWrapping=TextWrapping.Wrap,Language=System.Windows.Markup.XmlLanguage.GetLanguage(locale.Code.Replace('_','-'))});
                    panel.Measure(new Size(960,800));panel.Arrange(new Rect(0,0,960,800));panel.UpdateLayout();
                    var bitmap=new RenderTargetBitmap(960,800,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var target=File.Create(Path.Combine(output,locale.Code+(dark?"-dark":"-light")+".png")))png.Save(target);
                }
            }
            Typeface rejected;
            var sc=RelationshipTitleFonts.ForLocale("zh_CN");
            Check(!RelationshipTitleFont.TryResolve(new FontFamily(new Uri("pack://application:,,,/"),"./missing/#DeskPet Keepsake SC"),sc,"初次见面",out rejected),"missing resource rejected without silent system substitution");
            Check(!RelationshipTitleFont.TryResolve(new FontFamily(new Uri("pack://application:,,,/"),"./assets/fonts/relationships/#Invalid Family"),sc,"初次见面",out rejected),"invalid family rejected");
            Check(!RelationshipTitleFont.TryGetBundledTypeface("zh_CN","未来🦄",out rejected),"future unsupported scalar falls back");
            Check(RelationshipTitleFont.FontForLocale("zh_CN","未来🦄").Source==SystemFonts.MessageFontFamily.Source,"unsupported title keeps readable UI font");
            Check(!sc.Covers("\ud800"),"unpaired surrogate does not pass subset coverage");
            Check(RelationshipTitleFonts.ForLocale("zh-TW").Region=="TC"&&RelationshipTitleFonts.ForLocale("ja_JP").Region=="JP"&&RelationshipTitleFonts.ForLocale("ko_KR").Region=="KR"&&RelationshipTitleFonts.ForLocale("hi_IN").Region=="Devanagari","regional script selectors");
            var hindi=TextResources.CatalogFor("hi_IN").Get("relationship.card.title");
            var text=new TextBlock{Text=hindi,FontFamily=RelationshipTitleFont.FontForLocale("hi_IN",hindi),FontSize=40,Width=900,Height=100,Language=System.Windows.Markup.XmlLanguage.GetLanguage("hi-IN")};text.Measure(new Size(900,100));text.Arrange(new Rect(0,0,900,100));text.UpdateLayout();
            var runs=Glyphs(VisualTreeHelper.GetDrawing(text)).ToArray();
            Check(runs.Length>0&&runs.All(r=>r.GlyphTypeface.FamilyNames.Values.Contains("DeskPet Keepsake Devanagari")),"Hindi native shaping uses own bundled face throughout");
            var shaped=runs.SelectMany(r=>r.GlyphIndices).ToArray();GlyphTypeface direct;RelationshipTitleFont.GetTypeface("hi_IN",hindi).TryGetGlyphTypeface(out direct);
            var unshaped=RelationshipTitleFonts.CodePoints(hindi).Select(p=>direct.CharacterToGlyphMap[p]).ToArray();
            Console.WriteLine("HINDI SHAPED "+String.Join(",",shaped)+" CMAP "+String.Join(",",unshaped));Check(!shaped.SequenceEqual(unshaped)&&shaped.All(id=>id!=0),"Hindi actual native shaped glyph sequence differs from unshaped cmap");
            Console.WriteLine("RelationshipTitleFontTests: "+count+" passed; 26 title sheets at "+output);
        }
    }
}
