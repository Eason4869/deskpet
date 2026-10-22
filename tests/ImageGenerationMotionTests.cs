using System;
namespace Dafeiyu.Tests
{
    public static class ImageGenerationMotionTests
    {
        static int passed;
        public static int Main()
        {
            passed=0;try {
                for(int i=-1000;i<=1000;i++) { var s=ImageGenerationMotion.Sample(i/37.0);Check(s.X>=0&&s.X<=1&&s.Y>=0&&s.Y<=1&&s.Radius>0&&s.Radius<=1&&s.Opacity>=0&&s.Opacity<=1,"MotionSamplesStayFiniteAndBounded"); }
                var queued=new ImageGenerationTaskRecord{QueuedUtc=DateTime.UtcNow.AddMinutes(-2)};Check(ImageGenerationMotion.ElapsedSeconds(queued,DateTime.UtcNow)==0,"queued wait excluded from elapsed");queued.StartedUtc=DateTime.UtcNow.AddSeconds(-3);Check(ImageGenerationMotion.ElapsedSeconds(queued,DateTime.UtcNow)==3,"execution elapsed starts at running");Check(ImageGenerationMotion.DotOpacity(ImageGenerationMotion.Sample(0),.79,.5)<=.40,"dot opacity stays subtle");
                var a=ImageGenerationMotion.Sample(.26);var b=ImageGenerationMotion.Sample(1.26);Check(Math.Abs(a.X-b.X)<1e-9&&Math.Abs(a.Y-b.Y)<1e-9,"SamePhaseProducesSameFrame");
                foreach(ImageGenerationTaskState s in Enum.GetValues(typeof(ImageGenerationTaskState)))Check(ImageGenerationMotion.ShouldAnimate(s,true,true,false,false)==(s==ImageGenerationTaskState.Generating||s==ImageGenerationTaskState.Downloading||s==ImageGenerationTaskState.Saving),"QueuedAndTerminalStatesDoNotAnimate");
                Check(!ImageGenerationMotion.ShouldAnimate(ImageGenerationTaskState.Generating,true,false,false,false)&&!ImageGenerationMotion.ShouldAnimate(ImageGenerationTaskState.Generating,true,true,true,false)&&!ImageGenerationMotion.ShouldAnimate(ImageGenerationTaskState.Generating,true,true,false,true),"HiddenOrReducedMotionStopsClock");
                Check(ImageGenerationMotion.Sample(double.NaN).X>=0&&ImageGenerationMotion.Sample(double.PositiveInfinity).X<=1,"invalid phase fallback");
                string path=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"motion-settings-"+Guid.NewGuid().ToString("N")+".json");
                try{System.IO.File.WriteAllText(path,"{}");var store=new SettingsStore(path);Check(store.Load().ImageGenerationMotionEnabled,"legacy settings enable motion");var cfg=new PetSettings();cfg.ImageGenerationMotionEnabled=false;store.Save(cfg);Check(!store.Load().ImageGenerationMotionEnabled&&!cfg.Copy().ImageGenerationMotionEnabled,"false preference roundtrip and copy");}finally{if(System.IO.File.Exists(path))System.IO.File.Delete(path);}
                Console.WriteLine("ImageGenerationMotionTests: "+passed+" passed");return 0;
            }catch(Exception e){Console.WriteLine(e);return 1;}
        }
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;}
    }
}
