using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
    public static class CompleteCompanionNativeTests
    {
        static int checks;
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        [STAThread] public static int Main(string[] args)
        {
            try {
                string root=Environment.CurrentDirectory;
                while(!File.Exists(Path.Combine(root,"assets","holidays","catalog.json"))){root=Path.GetDirectoryName(root);if(root==null)throw new Exception("Missing source assets.");}
                var catalog=new HolidayCatalog(Path.Combine(root,"assets","holidays"));
                Check(catalog.Outfits.Count==50&&catalog.Outfits.All(o=>o.Ready),"All fifty source outfits are complete");
                var assets=new HolidayVisualAssets(null);long peak=0;int clips=0;
                foreach(var outfit in catalog.Outfits) {
                    foreach(var definition in outfit.Clips.Concat(new[]{outfit.Gaze,outfit.EasterEgg})) {
                        var clip=assets.Get(definition);clips++;
                        Check(clip.Count==definition.FrameCount&&clip.Loop==definition.Loop,"Native frame count/loop: "+definition.Path);
                        Check(Object.ReferenceEquals(clip,assets.Get(definition)),"Repeated current clip reuses decode");
                        for(int frame=0;frame<clip.Count;frame++) {
                            var image=new FormatConvertedBitmap(clip.Frame(frame),PixelFormats.Pbgra32,null,0);
                            byte[] pixels=new byte[480*520*4];image.CopyPixels(pixels,480*4,0);
                            bool matching=true;
                            for(int y=0;y<520;y+=17)for(int x=0;x<480;x+=17)matching&=pixels[(y*480+x)*4+3]==clip.AlphaAt(frame,x,y);
                            Check(matching,"Rendered frame and touch alpha agree: "+definition.Path+"/"+frame);
                        }
                        if(!definition.Loop)Check(clip.Sample(clip.Duration+1)==clip.Count-1,"One-shot reaches actual final pose");
                        peak=Math.Max(peak,assets.RetainedPixelBytes);Check(assets.RetainedPixelBytes<=256L*1024*1024,"Bounded retained image and alpha memory");
                    }
                    Console.WriteLine("PASS native full outfit "+outfit.CharacterId+" / "+outfit.HolidayId);
                }
                var samples=new List<long>();
                for(int round=0;round<30;round++) {
                    foreach(var outfit in catalog.Outfits){assets.Get(outfit.FindClip("idle","default"));Check(assets.RetainedPixelBytes<=256L*1024*1024,"Repeated wardrobe cache bound");}
                    assets.Clear();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();samples.Add(GC.GetTotalMemory(false));
                }
                Check(samples.Last()<=samples.Skip(20).Min()+32L*1024*1024,"Thirty-round stable managed memory");
                Check(assets.RetainedPixelBytes==0,"Closing/releases remove retained pixels");
                Console.WriteLine("Complete native companion tests: "+checks+" passed, "+clips+" clips, retained pixel peak "+peak+" bytes, 30 wardrobe rounds");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
