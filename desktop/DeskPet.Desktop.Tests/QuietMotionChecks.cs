using Dafeiyu;
using DeskPet.Desktop;
using Avalonia;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
internal static class QuietMotionChecks
{
    static byte[] Pixels(Bitmap bitmap,PixelRect rect){var bytes=new byte[rect.Width*rect.Height*4];var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{bitmap.CopyPixels(rect,pinned.AddrOfPinnedObject(),bytes.Length,rect.Width*4);}finally{pinned.Free();}return bytes;}
    internal static int Run(string root,bool installed=false)
    {
        int count=0;var failures=new List<string>();
        void Check(bool ok,string label){if(!ok)failures.Add(label);else count++;}
        var catalog=new HolidayCatalog(Path.GetFullPath("assets/holidays"));
        var evidence=Path.GetFullPath("work/hdskin-validation/integration/avalonia-"+(installed?"installed":"builtin"));Directory.CreateDirectory(evidence);
        var skins=SpriteAssets.LoadSkins(root).Where(s=>s.BuiltIn&&!s.IsPlaceholder).ToList();
        if(installed)skins=OfficialCompanionProfiles.All.Select(p=>SpriteAssets.ReadOfficial(new OfficialCompanionStorage(Path.GetFullPath("work/hdi/i-"+p.Id+"/data")).Installed(p.Id)!,p.Id)).ToList();
        foreach(var skin in skins){
            using var assets=new SpriteAssets();
            Check(installed?!skin.CanonicalBuiltIn&&!skin.BuiltIn:skin.CanonicalBuiltIn,"source provenance is explicit "+skin.Id);
            if(skin.OfficialCharacterId=="gugugaga")Check(skin.PackageId=="gugugaga","gugu keeps original package metadata");
            var holiday=(installed?new HolidayCatalog(Path.Combine(skin.Folder!,"holidays")):catalog).Outfits.Single(o=>o.CharacterId==skin.OfficialCharacterId);
            foreach(bool dressed in new[]{false,true})foreach(var action in new[]{"idle","sleeping"})foreach(bool original in action=="idle"?new[]{true}:new[]{true,false}){
                var clip=assets.Resolve(skin,action,original,dressed?holiday:null);double fps=action=="idle"?4:2.5;
                var label=skin.Id+"/"+(dressed?"holiday":"normal")+"/"+action+"/"+original;
                Check(clip.Frames==8&&clip.Fps==fps&&clip.Loop,"selected quiet timing "+label);
                if(!dressed)Check(installed?!clip.File.StartsWith("companion-quiet/"):clip.File=="companion-quiet/"+skin.OfficialCharacterId+"/"+(action=="idle"?"idle":original?"sleeping":"sleeping-alternate")+".png","selected source belongs to actual provenance "+label);
                var player=new InteractionPlayback((a,o)=>assets.Resolve(skin,a,o,dressed?holiday:null));player.Play(action,original);
                var view=new SpriteView{Assets=assets,Skin=skin,Clip=player.Clip,Width=480,Height=520};view.Measure(new Size(480,520));view.Arrange(new Rect(0,0,480,520));
                int draws=0;view.FrameRendered+=()=>draws++;var hashes=new HashSet<string>();
                for(int tick=0;tick<Math.Round(16/fps/.04);tick++){
                    if(tick>0)player.Advance(.04);
                    var expected=(int)Math.Floor(tick*.04*fps+1e-7)%8;
                    Check(player.Frame==expected,"actual native clock "+label+" tick"+tick);
                    if(tick>0&&expected==(int)Math.Floor((tick-1)*.04*fps+1e-7)%8)continue;
                    view.Frame=player.Frame;view.Clip=player.Clip;
                    using var capture=new RenderTargetBitmap(new PixelSize(480,520),new Vector(96,96));capture.Render(view);
                    var actual=Pixels(capture,new PixelRect(0,0,480,520));var source=Pixels(assets.Get(skin,clip),SpriteAssets.Rect(clip,view.Frame));
                    Check(actual.SequenceEqual(source),"production draw preserves full-frame pixels/offsets "+label+" frame"+view.Frame);
                    Check(!view.Hits(new Point(0,0)),"transparent corner passes through "+label);
                    int opaque=-1;
                    for(int pixel=0;pixel<480*520;pixel++)if(source[pixel*4+3]>200){opaque=pixel;break;}
                    Check(opaque>=0&&view.Hits(new Point(opaque%480+.1,opaque/480+.1)),"actual displayed geometry hits opaque body "+label);
                    if(tick<8/fps/.04){capture.Save(Path.Combine(evidence,label.Replace('/','-')+"-"+view.Frame+".png"),new PngBitmapEncoderOptions());hashes.Add(Convert.ToHexString(SHA256.HashData(actual)));}
                }
                Check(hashes.Count==8&&draws>=16,"eight distinct actual draws across two loops "+label);
                Console.WriteLine("PASS rendered "+label+" eight frames / two deterministic loops");
            }
            // A matching manifest ID cannot mark a custom/installed source canonical.
            var custom=new PetSkin{Id=skin.Id,Name="custom",Prompt="",Folder=root,PackageId=skin.OfficialCharacterId,BuiltinCharacterId=skin.OfficialCharacterId};
            custom.Clips["idle"]=new ClipSpec("own.png",8,3,6,true);custom.Clips["sleeping"]=new ClipSpec("own-sleep.png",8,5,5,true);
            Check(!custom.CanonicalBuiltIn&&assets.Resolve(custom,"idle",true).Frames==3&&assets.Resolve(custom,"sleeping",true).Fps==5,"same-ID custom keeps own metadata "+skin.Id);
            Check(assets.Gaze(skin).Frames==16,"nonquiet gaze preserved "+skin.Id);
        }
        if(failures.Count>0)throw new Exception("QuietMotion: "+failures.Count+" failures; "+String.Join("; ",failures.Take(5)));
        return count+Task.Run(()=>QuietKeepsakeChecks.Run(Path.Combine(root,"keepsakes"),new HolidayKeepsakeSceneValidator())).GetAwaiter().GetResult();
    }
}
