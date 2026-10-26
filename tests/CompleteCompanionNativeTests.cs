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
        static void CollectReleasedImages()
        {
            for(int pass=0;pass<3;pass++){
                GC.Collect();GC.WaitForPendingFinalizers();
                var frame=new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference LoadTrackedFrame(HolidayVisualAssets assets,OutfitClipDefinition definition)
        {return new WeakReference(assets.Get(definition).Frame(0));}
        [STAThread] public static int Main(string[] args)
        {
            try {
                string root=Environment.CurrentDirectory;
                while(!File.Exists(Path.Combine(root,"assets","holidays","catalog.json"))){root=Path.GetDirectoryName(root);if(root==null)throw new Exception("Missing source assets.");}
                Console.WriteLine("NATIVE_PROCESS_BITS="+(Environment.Is64BitProcess?64:32));
                var catalog=new HolidayCatalog(Path.Combine(root,"assets","holidays"));
                Check(catalog.Outfits.Count==50&&catalog.Outfits.All(o=>o.Ready),"All fifty source outfits are complete");
                var assets=new HolidayVisualAssets(null);long peak=0;int clips=0;
                bool performanceOnly=args.Contains("--performance-only");
                if(!performanceOnly){
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
                    // This offline corpus decodes thousands of native WIC images without a
                    // normal UI workload. Finish each validation batch before the next;
                    // the separate 55-appearance rounds below measure post-release stability.
                    assets.Clear();CollectReleasedImages();
                    Console.WriteLine("PASS native full outfit "+outfit.CharacterId+" / "+outfit.HolidayId);
                }
                    assets.Clear();CollectReleasedImages();
                    var start=new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--performance-only"){
                        UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
                    using(var process=System.Diagnostics.Process.Start(start)){
                        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                        if(!process.WaitForExit(10*60*1000)){process.Kill();throw new TimeoutException("Native wardrobe benchmark child.");}
                        Console.Write(output.GetAwaiter().GetResult());Console.Error.Write(errors.GetAwaiter().GetResult());
                        if(process.ExitCode!=0)throw new Exception("Fresh-process native wardrobe benchmark failed.");
                    }
                    Console.WriteLine("Complete native validation: "+checks+" checks, "+clips+" clips, retained peak "+peak+" bytes; fresh55x30 benchmark passed");return 0;
                }
                var samples=new List<long>();var processSamples=new List<object>();var privateSamples=new List<long>();
                var defaults=new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}.Select(id=>new OutfitClipDefinition("idle.png",8,8,4,true,480,520,0,0,"idle","default","default:"+id,_=>File.OpenRead(Path.Combine(root,"assets","complete-defaults",id,"animations","idle.png")))).ToArray();
                var wardrobe=catalog.Outfits.Select(o=>o.FindClip("idle","default")).Concat(defaults).ToArray();
                var elapsed=System.Diagnostics.Stopwatch.StartNew();
                for(int round=0;round<30;round++) {
                    var releasedFrames=new List<WeakReference>();
                    foreach(var definition in wardrobe){releasedFrames.Add(LoadTrackedFrame(assets,definition));peak=Math.Max(peak,assets.RetainedPixelBytes);Check(assets.RetainedPixelBytes<=256L*1024*1024,"Repeated wardrobe cache bound");}
                    assets.Clear();CollectReleasedImages();Check(releasedFrames.All(frame=>!frame.IsAlive),"All past appearance frames released");samples.Add(GC.GetTotalMemory(false));
                    using(var process=System.Diagnostics.Process.GetCurrentProcess()){process.Refresh();privateSamples.Add(process.PrivateMemorySize64);processSamples.Add(new {round=round+1,privateBytes=process.PrivateMemorySize64,rssBytes=process.WorkingSet64,threads=process.Threads.Count,handles=process.HandleCount,managedBytes=samples.Last(),decodes=assets.DecodeCount,retainedPixelBytes=assets.RetainedPixelBytes});}
                }
                Check(samples.Last()<=samples.Skip(20).Min()+32L*1024*1024,"Thirty-round stable managed memory");
                string report=Path.Combine(root,"work","dev3-validation","wpf-performance.json");Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{appearances=55,rounds=30,elapsedSeconds=elapsed.Elapsed.TotalSeconds,retainedPixelPeakBytes=peak,lastTenPrivateRangeBytes=privateSamples.Skip(20).Max()-privateSamples.Skip(20).Min(),postWarmupHighWaterGrowthBytes=Math.Max(0,privateSamples.Skip(20).Max()-privateSamples.Take(20).Max()),allRetiredFramesReleased=true,samples=processSamples}));
                Check(privateSamples.Skip(20).Max()<=privateSamples.Take(20).Max()+32L*1024*1024,"Native private high-water growth stays within32MiB after warmup");
                Check(privateSamples.Skip(20).Max()<=privateSamples.Skip(20).Min()+32L*1024*1024,"Last ten released private-memory samples stay within32MiB");
                Check(assets.RetainedPixelBytes==0,"Closing/releases remove retained pixels");
                Console.WriteLine("Complete native companion tests: "+checks+" passed, "+clips+" clips, retained pixel peak "+peak+" bytes, 30 wardrobe rounds");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
