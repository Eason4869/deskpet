using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeskPet.Desktop;
using Dafeiyu;
namespace DeskPet.Desktop.Tests;
internal static class RelationshipStageArtChecks
{
    static int count;
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label);}
    static RelationshipCardData Data(string character,string stage)
    {
        var saved=new RelationshipSnapshot{State=new CompanionRelationshipState{CompanionId="official:"+character,FirstLocalDate="2026-07-18",Milestones=new List<RelationshipMilestone>{new RelationshipMilestone{StageId=stage,EarnedLocalDate="2026-10-25"}}}};
        return RelationshipCardData.FromSnapshot(saved,stage,character=="e-bao"?"E包":character=="dafeiyu"?"大肥鱼":character=="marfu"?"玛尔芙":character=="xiaobei"?"小贝":"咕嘎");
    }
    internal static int Run(string locale,bool resources,bool quick=false)
    {
        TextResources.ConfigureStartupLanguage(locale);
        if(resources)count+=RelationshipStageArtContractChecks.Run(RelationshipStageArtSource.OpenResource);
        string directory=Path.GetFullPath("work/f4b/cards/avalonia/"+locale);Directory.CreateDirectory(directory);
        using(var scenes=new RelationshipStageArtSource()){
            if(resources){
                foreach(var character in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}){
                    foreach(var stage in RelationshipRules.Stages){var image=scenes.Get("official:"+character,stage);Check(image!=null&&image.PixelSize.Width==800,"actual production decode 800px "+character+" "+stage);}
                    Check(scenes.Count==6,"current companion owns at most six sources");
                }
                foreach(var asset in RelationshipStageArt.All.Where(a=>a.HolidayId!=null)){
                    var captured=RelationshipStageArtSource.CreateProvider().Capture(asset.CompanionId,asset.CharacterId,asset.HolidayId,asset.OutfitId,asset.OutfitVersion);
                    using(var source=captured.OpenRead()){var bytes=RelationshipStageArt.ReadVerified(source,asset);using(var memory=new MemoryStream(bytes))using(var full=new Bitmap(memory))Check(full.PixelSize.Width==asset.PixelWidth&&full.PixelSize.Height==asset.PixelHeight,"actual holiday native decode "+asset.CharacterId);}
                }
                CacheOwnership();RasterOrnaments();
            }
            var characters=(locale=="zh_CN"||locale=="de_DE")?new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}:new[]{"e-bao"};
            foreach(string character in characters)foreach(string stage in RelationshipRules.Stages)foreach(bool dark in new[]{false,true}){
                if(quick&&(character!="e-bao"||(stage!="first_meeting"&&stage!="best_friend")))continue;
                var data=Data(character,stage);var image=scenes.Get(data);Check(image!=null,"selected own scene");
                var visual=RelationshipCardRenderer.Create(data,new CompanionPortraitSource(null),dark,image);
                visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
                var title=visual.Children.OfType<TextBlock>().Single(t=>t.Name=="RelationshipCardStage");
                Check(title.Text==data.StageName&&title.FontSize>=32&&title.FontSize<=44&&title.TextTrimming==TextTrimming.None,"complete native artistic title "+locale+" "+stage);
                foreach(var text in visual.Children.OfType<TextBlock>()){
                    var probe=new TextBlock{Text=text.Text,Width=text.Width,FontSize=text.FontSize,FontFamily=text.FontFamily,TextWrapping=text.TextWrapping};
                    probe.Measure(new Size(text.Width,Double.PositiveInfinity));Check(probe.DesiredSize.Height<=text.Height+.5,"whole text fits "+locale+" "+stage+" "+text.Name);
                }
                Check(title.TextLayout.Height<=title.FontSize*2.7,"title at most two native lines");
                var ornament=visual.Children.OfType<Avalonia.Controls.Shapes.Path>().Single();
                Check(Canvas.GetTop(ornament)>=Canvas.GetTop(title)+(double)title.Tag!+8&&ornament.Height>=20&&ornament.Height<=26,"ornament is separate strip with gap");
                var ink=ornament.RenderedGeometry!.Bounds;Check(ink.X>=0&&ink.Y>=0&&ink.Right<=210&&ink.Bottom<=26,"actual native ornament geometry stays within 210x26 strip");
                Check(Canvas.GetTop(ornament)+ornament.Height+8<=270,"ornament does not overlap memory");
                visual.Dispose();
                var png=RelationshipCardRenderer.Render(data,new CompanionPortraitSource(null),dark,image);
                string file=Path.Combine(directory,character+"-"+stage+(dark?"-dark":"-light")+".png");File.WriteAllBytes(file,png);
                using(var capture=new HomeCardCaptureForFixture(data,png))Check(capture.Width==800&&capture.Height==480,"frozen exported PNG is 800x480");
                if(resources&&character=="e-bao"&&stage=="first_meeting"&&!dark){FrozenSave(data,png,directory);PreviewPicker(data,png,directory);}
            }
        }
        Console.WriteLine("F4b avalonia "+locale+" assertions="+count+" originals="+directory);return count;
    }
    internal static int OrnamentOnly(){TextResources.ConfigureStartupLanguage("zh_CN");RasterOrnaments();WholeOrnamentCards();return count;}
    static void RasterOrnaments()
    {
        bool allValid=true;
        using var scenes=new RelationshipStageArtSource();
        foreach(string style in RelationshipRules.Stages.Concat(new[]{"halloween"}))foreach(bool dark in new[]{false,true}){
            var ornament=RelationshipCardRenderer.OrnamentFor(style,dark);Canvas.SetLeft(ornament,0);Canvas.SetTop(ornament,0);
            var canvas=new Canvas{Width=400,Height=60};canvas.Children.Add(ornament);canvas.Measure(new Size(400,60));canvas.Arrange(new Rect(0,0,400,60));
            using var image=new RenderTargetBitmap(new PixelSize(400,60),new Vector(96,96));image.Render(canvas);
            using var pixels=new WriteableBitmap(image.PixelSize,new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);using var buffer=pixels.Lock();image.CopyPixels(buffer);
            var bytes=new byte[buffer.RowBytes*60];System.Runtime.InteropServices.Marshal.Copy(buffer.Address,bytes,0,bytes.Length);
            int left=400,top=60,right=-1,bottom=-1;
            for(int y=0;y<60;y++)for(int x=0;x<400;x++)if(bytes[y*buffer.RowBytes+x*4+3]>0){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            Console.WriteLine("GEOMETRY "+style+" raw="+Geometry.Parse(RelationshipCardDecoration.PathFor(style)).Bounds+" transformed="+ornament.Data!.Bounds+" rendered="+ornament.RenderedGeometry!.Bounds+" matrix="+ornament.Data.Transform);
            Console.WriteLine("RASTER "+style+" "+dark+" "+left+","+top+".."+right+","+bottom);
            var directory=Path.GetFullPath("work/f4b/ornament-proofs/avalonia");Directory.CreateDirectory(directory);image.Save(Path.Combine(directory,style+(dark?"-dark":"-light")+".png"),new PngBitmapEncoderOptions());
            if(style=="heartfelt")Check(bytes[14*buffer.RowBytes+195*4+3]>0,"trailing horizontal line survives actual raster rendering");
            bool valid=right>=left&&left>=0&&right<210&&top>=0&&bottom<26;allValid&=valid;if(valid)Check(true,"actual transparent raster ornament ink bounds "+style+" "+dark);
        }
        Check(allValid,"all seven styles stay within actual raster ink bounds");
    }
    static void WholeOrnamentCards()
    {
        using(var scenes=new RelationshipStageArtSource()){
            var source=RelationshipStageArtSource.CreateProvider().Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.0");byte[] raw;
            using(var stream=source.OpenRead())raw=RelationshipStageArt.ReadVerified(stream,source.Descriptor);
            using var memory=new MemoryStream(raw);using var holiday=Bitmap.DecodeToWidth(memory,800);
            string directory=Path.GetFullPath("work/f4b/ornament-full-cards/avalonia");Directory.CreateDirectory(directory);
            foreach(string style in RelationshipRules.Stages.Concat(new[]{"halloween"}))foreach(bool dark in new[]{false,true}){
                var data=Data("e-bao",style=="halloween"?"first_meeting":style);var visual= RelationshipCardRenderer.Create(data,new CompanionPortraitSource(null),dark,style=="halloween"?holiday:scenes.Get(data));
                if(style=="halloween"){
                    // Explicit unearned layout fixture, not the future H1 owned holiday card.
                    visual.Children.Remove(visual.Children.OfType<TextBlock>().Single(t=>t.Name=="RelationshipCardStage"));visual.Children.Remove(visual.Children.OfType<Avalonia.Controls.Shapes.Path>().Single());
                    visual.Children.OfType<TextBlock>().Single(t=>t.Name=="RelationshipCardMemory").Text="";
                    var method=typeof(RelationshipCardRenderer).GetMethod("SceneText",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                    double measured=(double)method.Invoke(null,new object[]{visual,TextResources.Get("relationship.card.halloweenTitle"),108.0,104.0,44.0,32.0,dark,"RelationshipCardStage",true})!;
                    var ornament=RelationshipCardRenderer.OrnamentFor("halloween",dark);Canvas.SetLeft(ornament,456);Canvas.SetTop(ornament,108+measured+12);visual.Children.Add(ornament);
                }
                visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
                foreach(var label in visual.Children.OfType<TextBlock>()){
                    var probe=new TextBlock{Text=label.Text,Width=label.Width,FontSize=label.FontSize,FontFamily=label.FontFamily,TextWrapping=TextWrapping.Wrap};
                    probe.Measure(new Size(label.Width,Double.PositiveInfinity));Check(probe.DesiredSize.Height<=label.Height+.5,"focused whole card text fits "+style+" "+label.Name);
                }
                using var bitmap=new RenderTargetBitmap(new PixelSize(800,480),new Vector(96,96));bitmap.Render(visual);bitmap.Save(Path.Combine(directory,style+(dark?"-dark":"-light")+".png"),new PngBitmapEncoderOptions());
                visual.Dispose();
            }
        }
        Console.WriteLine("avalonia seven motif whole layout cards verified; Halloween is unearned layout proof only");
    }
    static void CacheOwnership()
    {
        int opens=0;var source=new RelationshipStageArtSource(delegate(RelationshipStageArtAsset a){opens++;return RelationshipStageArtSource.OpenResource(a);});
        var first=source.Get("official:e-bao","first_meeting")??throw new Exception("source missing");Check(Object.ReferenceEquals(first,source.Get("official:e-bao","first_meeting"))&&opens==1,"scene cached by version, no repeat I/O");
        var capture=RelationshipStageArtSource.CreateProvider().Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.0");
        source.SetIdentity("official:marfu");Check(source.Count==0,"identity switch clears owned scene cache");
        source.Dispose();Check(source.Count==0&&source.Get("official:e-bao","first_meeting")==null,"disposed page cannot load");
        using(var raw=capture.OpenRead())Check(RelationshipStageArt.ReadVerified(raw,capture.Descriptor).Length==capture.Descriptor.ByteLength,"original raw scene survives page disposal");
        var tracking=new TrackingStream(File.ReadAllBytes("assets/relationships/e-bao/first_meeting.png"));
        using(var tracked=new RelationshipStageArtSource(delegate(RelationshipStageArtAsset a){return tracking;}))Check(tracked.Get("official:e-bao","first_meeting")!=null&&tracking.Closed,"successful native load closes original source stream");
        var rejected=new TrackingStream(new byte[]{1,2,3});
        using(var tracked=new RelationshipStageArtSource(delegate(RelationshipStageArtAsset a){return rejected;}))Check(tracked.Get("official:e-bao","first_meeting")==null&&rejected.Closed,"failed validation closes original source stream");
        bool released=false;try{using(var saved=new MemoryStream())first.Save(saved,new PngBitmapEncoderOptions());}catch(ObjectDisposedException){released=true;}Check(released,"identity switch disposes the owned native scene");
        opens=0;using(var bad=new RelationshipStageArtSource(delegate(RelationshipStageArtAsset a){opens++;return new MemoryStream(new byte[]{1,2,3});})){
            Check(bad.Get("official:e-bao","first_meeting")==null&&bad.Get("official:e-bao","first_meeting")==null&&opens==1,"invalid source negative cached by version");
            Check(bad.Get("official:marfu","first_meeting")==null&&opens==2,"identity switch does not borrow failed or previous source");
        }
        using(var missing=new RelationshipStageArtSource(delegate(RelationshipStageArtAsset a){throw new FileNotFoundException();}))Check(missing.Get("official:e-bao","familiar")==null,"missing resource preserves fallback");
        using(var unknown=new RelationshipStageArtSource(delegate(RelationshipStageArtAsset a){throw new Exception("must not read");}))Check(unknown.Get("custom-pack:e-bao","first_meeting")==null,"custom portrait fallback does no scene I/O");
        var fallback=RelationshipCardRenderer.Create(Data("e-bao","first_meeting"),new CompanionPortraitSource(null),false,null);Check(fallback.Children.OfType<CompanionPortraitVisual>().Count()==1,"F3 complete own portrait remains fallback");fallback.Dispose();
    }
    internal static int Sample(string locale)
    {
        TextResources.ConfigureStartupLanguage(locale);string directory=Path.GetFullPath("work/f4b/title-samples/avalonia/"+locale);Directory.CreateDirectory(directory);
        using(var scenes=new RelationshipStageArtSource())foreach(bool dark in new[]{false,true}){
            var data=Data("e-bao","first_meeting");var image=scenes.Get(data);
            var visual= RelationshipCardRenderer.Create(data,new CompanionPortraitSource(null),dark,image);
            visual.Children.Remove(visual.Children.OfType<TextBlock>().Single(t=>t.Name=="RelationshipCardStage"));
            string title=TextResources.Get("relationship.card.title");
            var method=typeof(RelationshipCardRenderer).GetMethod("SceneText",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)??throw new Exception("title layout helper missing");
            double measured=(double)(method.Invoke(null,new object[]{visual,title,108.0,104.0,44.0,32.0,dark,"RelationshipCardStage",true})??throw new Exception("measured title missing"));
            var ornament=visual.Children.OfType<Avalonia.Controls.Shapes.Path>().Single();Canvas.SetTop(ornament,108+measured+12);
            visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
            var label=visual.Children.OfType<TextBlock>().Single(t=>t.Name=="RelationshipCardStage");
            Check(measured<=104&&measured<=label.FontSize*2.7,"whole localized artistic sample fits actual title bounds "+locale);
            Check(label.FontFamily==RelationshipTitleFont.FontForLocale(locale,title),"actual localized sample uses accepted title family "+locale);
            Check(FontManager.Current.TryGetGlyphTypeface(RelationshipTitleFont.GetTypeface(locale,title),out var glyph)&&glyph.FamilyName==RelationshipTitleFonts.ForLocale(locale).FamilyName,"native local face retained for sample");
            using var bitmap=new RenderTargetBitmap(new PixelSize(800,480),new Vector(96,96));bitmap.Render(visual);
            bitmap.Save(Path.Combine(directory,dark?"dark.png":"light.png"),new PngBitmapEncoderOptions());
            visual.Dispose();
        }
        Console.WriteLine("F4b avalonia actual localized title samples="+locale+" assertions="+count);return count;
    }
    static void PreviewPicker(RelationshipCardData data,byte[] png,string directory)
    {
        string path=Path.Combine(directory,"picker-frozen.png");bool same=true;
        var pending=new System.Threading.Tasks.TaskCompletionSource<string?>();
        using var capture=new HomeCardCapture(data,png);
        var preview=new RelationshipCardPreview(capture,"",delegate{return true;},delegate{return same;},delegate{},delegate{return pending.Task;});
        var button=preview.Children.OfType<Button>().Single(x=>x.Name=="SaveRelationshipCard");
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check(!button.IsEnabled&&!File.Exists(path),"actual preview button awaits path selection without writes");same=false;pending.SetResult(path);Pump(delegate{return button.IsEnabled;});
        Check(!File.Exists(path),"identity switch while picker awaits prevents file write");
        same=true;pending=new System.Threading.Tasks.TaskCompletionSource<string?>();button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        pending.SetResult(null);Pump(delegate{return button.IsEnabled;});Check(!File.Exists(path),"actual preview cancellation writes nothing");
        pending=new System.Threading.Tasks.TaskCompletionSource<string?>();button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        pending.SetResult(path);Pump(delegate{return button.IsEnabled;});Check(File.ReadAllBytes(path).SequenceEqual(png),"actual preview saves the opened frozen bytes");File.Delete(path);
        pending=new System.Threading.Tasks.TaskCompletionSource<string?>();button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        pending.SetResult(Path.Combine(directory,"missing","fail.png"));Pump(delegate{return button.IsEnabled;});
        Check(preview.Children.OfType<TextBlock>().Single(x=>x.Name=="RelationshipCardStatus").Text==TextResources.Get("relationship.card.failed"),"actual preview failure is visible and save enabled for retry");
        pending=new System.Threading.Tasks.TaskCompletionSource<string?>();button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        pending.SetResult(path);Pump(delegate{return button.IsEnabled;});Check(File.ReadAllBytes(path).SequenceEqual(png),"actual preview retries same frozen PNG");File.Delete(path);
    }
    static void Pump(Func<bool> done)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();while(!done()){
            if(timer.ElapsedMilliseconds>10000)throw new TimeoutException("bounded preview fixture");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(1);
        }
    }
    static void FrozenSave(RelationshipCardData data,byte[] png,string directory)
    {
        string path=Path.Combine(directory,"frozen-save.png");
        Check(RelationshipCardFile.SaveAsync(null,png).GetAwaiter().GetResult()==RelationshipCardSaveResult.Cancelled,"cancelled picker path writes nothing");
        Check(!File.Exists(path),"preview/cancel has no save side effect");
        Check(RelationshipCardFile.SaveAsync(path,png).GetAwaiter().GetResult()==RelationshipCardSaveResult.Saved&&File.ReadAllBytes(path).SequenceEqual(png),"save writes exact frozen capture bytes");
        File.Delete(path);
        Check(RelationshipCardFile.SaveAsync(Path.Combine(directory,"missing","card.png"),png).GetAwaiter().GetResult()==RelationshipCardSaveResult.Failed,"save failure is explicit");
        Check(RelationshipCardFile.SaveAsync(path,png).GetAwaiter().GetResult()==RelationshipCardSaveResult.Saved,"same capture retries after failure");File.Delete(path);
    }
    sealed class TrackingStream:MemoryStream
    {
        internal bool Closed;internal TrackingStream(byte[] bytes):base(bytes){}
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
    }
    sealed class HomeCardCaptureForFixture:IDisposable
    {
        readonly HomeCardCapture capture;public int Width=>capture.Image.PixelSize.Width;public int Height=>capture.Image.PixelSize.Height;internal HomeCardCaptureForFixture(RelationshipCardData data,byte[] bytes){capture=new HomeCardCapture(data,bytes);}public void Dispose(){capture.Dispose();}
    }
}
