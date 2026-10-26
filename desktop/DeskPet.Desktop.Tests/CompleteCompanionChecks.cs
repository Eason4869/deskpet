using Dafeiyu;
using DeskPet.Desktop;
internal static class CompleteCompanionChecks {
internal static int Run(string root){int count=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
var catalog=new HolidayCatalog(Path.GetFullPath("assets/holidays"));
var skins=SpriteAssets.LoadSkins(root);
Check(catalog.Outfits.Count==50,"fifty reviewed outfits");
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
return count;}
}
