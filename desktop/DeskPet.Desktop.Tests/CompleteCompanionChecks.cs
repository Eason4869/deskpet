using Dafeiyu;
using DeskPet.Desktop;
using Avalonia;using Avalonia.Media.Imaging;
internal static class CompleteCompanionChecks {
internal static int RunDeferred(string root){int count=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
var catalog=new HolidayCatalog(Path.GetFullPath("assets/holidays"));var skins=SpriteAssets.LoadSkins(root);
using(var deferred=new SpriteAssets()) {
var pending=new SpriteView{Assets=deferred,Skin=skins.First(s=>s.OfficialCharacterId=="e-bao")};
foreach(var outfit in catalog.Outfits.Take(12)){pending.Clip=deferred.Resolve(pending.Skin,"idle",true,outfit);Check(deferred.RetainedPixelBytes<=64L*1024*1024,"render-deferred wardrobe remains within cache budget");}
pending.Clip=null;
}
return count;}
internal static int Run(string root){int count=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
var catalog=new HolidayCatalog(Path.GetFullPath("assets/holidays"));
var skins=SpriteAssets.LoadSkins(root);
Check(catalog.Outfits.Count==50,"fifty reviewed outfits");count+=RunDeferred(root);
using var wardrobe=new SpriteAssets();
foreach(var outfit in catalog.Outfits){
var skin=skins.First(s=>s.OfficialCharacterId==outfit.CharacterId);
using var assets=new SpriteAssets();
foreach(var name in new[]{"jumping","waving","petting","poked","sleeping"}){
var spec=outfit.FindClip(name,"default");var resolved=assets.Resolve(skin,name,true,outfit);
Check(resolved.Fps==spec.Fps&&resolved.Loop==spec.Loop,"own timing "+outfit.OutfitId+"/"+name);
var player=new InteractionPlayback((a,o)=>assets.Resolve(skin,a,o,outfit));player.Play(name,true);
for(int i=0;i<spec.FrameCount;i++){Check(player.Action==name&&player.Frame==i,"pose "+i+"/"+name);assets.PrepareFrame(skin,player.Clip,player.Frame);player.Advance(1/spec.Fps);}
Check(spec.Loop?player.Action==name&&player.Frame==0:player.Action=="idle","complete cycle "+name);
}
}
var samples=new List<object>();var privateSamples=new List<long>();long peak=0;var timer=System.Diagnostics.Stopwatch.StartNew();
var view=new SpriteView{Assets=wardrobe,Skin=skins.First(s=>s.OfficialCharacterId=="e-bao"),Width=480,Height=520};view.Measure(new Size(480,520));view.Arrange(new Rect(0,0,480,520));
for(int round=0;round<30;round++){
foreach(var outfit in catalog.Outfits){var skin=skins.First(s=>s.OfficialCharacterId==outfit.CharacterId);view.Skin=skin;
foreach(var source in new[]{outfit.FindClip("idle","default"),outfit.Gaze}){var clip=SpriteAssets.OutfitClip(outfit,source);wardrobe.PrepareFrame(skin,clip,0);view.Clip=clip;view.Frame=0;using(var capture=new RenderTargetBitmap(new PixelSize(480,520),new Vector(96,96)))capture.Render(view);Check(wardrobe.RetainedPixelBytes<=64L*1024*1024,"wardrobe decoded byte budget");peak=Math.Max(peak,wardrobe.RetainedPixelBytes);Check(!view.Hits(new Point(0,0)),"rendered transparent corner remains pass-through");}
}
foreach(var skin in skins.Where(s=>s.BuiltIn&&!s.IsPlaceholder)){view.Skin=skin;view.Clip=wardrobe.Resolve(skin,"idle",true);view.Frame=0;using(var capture=new RenderTargetBitmap(new PixelSize(480,520),new Vector(96,96)))capture.Render(view);}
GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();using(var process=System.Diagnostics.Process.GetCurrentProcess()){process.Refresh();privateSamples.Add(process.PrivateMemorySize64);samples.Add(new {round=round+1,privateBytes=process.PrivateMemorySize64,rssBytes=process.WorkingSet64,threads=process.Threads.Count,handles=OperatingSystem.IsWindows()?(int?)process.HandleCount:null,decodes=wardrobe.DecodeCount,retainedPixelBytes=wardrobe.RetainedPixelBytes});}
}
var report=Path.GetFullPath("work/dev3-validation/avalonia-performance.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new{appearances=55,rounds=30,elapsedSeconds=timer.Elapsed.TotalSeconds,retainedPixelPeakBytes=peak,samples}));
Check(privateSamples.Skip(20).Max()<=privateSamples.Skip(20).Min()+32L*1024*1024,"native private memory stabilizes after warmup");
view.Clip=null;wardrobe.Dispose();Check(wardrobe.RetainedPixelBytes==0,"disposed wardrobe releases image and mask bytes");
return count;}
}
