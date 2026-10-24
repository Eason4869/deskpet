using System;
using System.IO;
using System.Linq;
using Dafeiyu;
namespace Dafeiyu.Tests
{
    public static class QuietMotionTests
    {
        static int passed,failed;
        static void Check(bool ok,string label){Console.WriteLine((ok?"PASS ":"FAIL ")+label);if(ok)passed++;else failed++;}
        static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource frame){var image=new System.Windows.Media.Imaging.FormatConvertedBitmap(frame,System.Windows.Media.PixelFormats.Pbgra32,null,0);var bytes=new byte[480*520*4];image.CopyPixels(bytes,480*4,0);return bytes;}
        static void SourcePixels(MotionClip clip,string role,string sheet)
        {
            var source=IllustratedPack.ReadImage(Path.Combine("assets/companion-quiet",role,sheet+".png"),3840,520);
            for(int frame=0;frame<8;frame++)Check(Pixels(clip.Frame(frame)).SequenceEqual(Pixels(new System.Windows.Media.Imaging.CroppedBitmap(source,new System.Windows.Int32Rect(frame*480,0,480,520)))),"selected original pixels "+role+"/"+sheet+"/"+frame);
        }
        [STAThread] public static int Main(string[] args)
        {try{return Run(args);}catch(Exception e){Console.WriteLine(e.GetType().FullName+": "+e.Message);Console.WriteLine(e.StackTrace);return 2;}}
        static int Run(string[] args)
        {
            if(args.Contains("--compile-only"))return 0;
            var catalog=new SkinCatalog(Path.GetFullPath("work/hdskin-validation/integration/q-"+Guid.NewGuid().ToString("N")));
            foreach(var skin in catalog.Skins.Where(s=>s.Embedded||s.Bundled)){
                var idle=new SmoothMotion(skin).Get(0);
                Check(idle!=null&&idle.Count==8&&idle.Duration==2,"builtin idle eight frames/2 seconds "+skin.Id);
                if(idle!=null)SourcePixels(idle,skin.BuiltinCharacterId??skin.Id,"idle");
                foreach(bool original in new[]{true,false}){
                    var clip=new AdditionalInteractions().Get(skin,"sleeping"+(original?"-original":""));
                    Check(clip!=null&&clip.Count==8&&clip.Duration==3.2,"builtin sleep eight frames/3.2 seconds "+skin.Id+" "+original);
                    if(clip!=null)SourcePixels(clip,skin.BuiltinCharacterId??skin.Id,original?"sleeping":"sleeping-alternate");
                    var clock=new InteractionAnimation();clock.Configure(skin,original);clock.Start("sleeping");clock.Advance(2.8);
                    Check(clock.Frame==7,"sleep actual timer reaches eighth frame at 2.8 seconds "+skin.Id+" "+original);
                    clock.Advance(.4);Check(clock.Active&&clock.Frame==0,"sleep actual timer wraps at 3.2 seconds "+skin.Id+" "+original);
                }
                var custom=new SkinDefinition(skin.Id,"Custom","",null,false);
                Check(AdditionalInteractions.Quiet(custom,"idle",true)==null&&AdditionalInteractions.Quiet(custom,"sleeping",false)==null,"same-ID custom cannot borrow quiet source "+skin.Id);
            }
            var provider=new RelationshipSceneProvider(a=>File.OpenRead(Path.Combine("assets",a.RelativePath)));
            var old=provider.Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.0");
            var current=provider.Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.3");
            Check(current!=null&&current.Descriptor.OutfitVersion=="1.0.3","reviewed current outfit binding is exact 1.0.3");
            Check(old!=null&&current!=null&&old.Descriptor.Sha256==current.Descriptor.Sha256&&old.Descriptor.SceneVersion==current.Descriptor.SceneVersion,"old and current outfit keep original scene identity");
            Check(RelationshipStageArt.All.Count==35,"original scene inventory remains35");
            foreach(var version in new[]{"1.0.1","1.0.2","1.0.4","2.0.0"})Check(provider.Capture("official:e-bao","e-bao","halloween","halloween-2026",version)==null,"unreviewed version rejected "+version);
            passed+=QuietKeepsakeChecks.Run(Path.GetFullPath("work/hdskin-validation/integration/keepsake-wpf-"+Guid.NewGuid().ToString("N")),new HolidayKeepsakeSceneValidator());
            Console.WriteLine("QuietMotion: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
    }
}
