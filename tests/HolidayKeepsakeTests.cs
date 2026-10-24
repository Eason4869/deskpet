using System;
using System.Collections;
using System.IO;
using System.Linq;
using Dafeiyu.Maintenance;
namespace Dafeiyu.Tests
{
    public static class HolidayKeepsakeTests
    {
        public static int Main(string[] args)
        {
            try {
                if(args.Contains("--json-boundary")){Console.WriteLine("PASS: "+HolidayKeepsakeContractChecks.RunJsonBoundary("work/h1a/json-wpf-"+Guid.NewGuid().ToString("N"),new HolidayKeepsakeSceneValidator())+" JSON document boundary contracts");return 0;}
                if(args.Length==3&&args[0]=="--holiday-keepsake-child")return HolidayKeepsakeContractChecks.Child(args[1],args[2],new HolidayKeepsakeSceneValidator());
                var type=typeof(HolidayCalendar).Assembly.GetType("Dafeiyu.HolidayKeepsakeRegistry");
                if(type==null)throw new Exception("Holiday acquisition registry is missing.");
                var registry=type.GetProperty("BuiltIn").GetValue(null,null);
                var definitions=(IList)type.GetProperty("Definitions").GetValue(registry,null);
                if(definitions.Count!=1)throw new Exception("Only the implemented holiday may be offered.");
                string root=Path.GetFullPath("work/h1a/tests/"+Guid.NewGuid().ToString("N"));
                var provider=new RelationshipSceneProvider(asset=>File.OpenRead(Path.Combine("assets",asset.RelativePath)));
                var catalog=new HolidayCatalog(new[]{new OutfitDefinition("e-bao","halloween-2026","halloween","fixture","1.0.0",true,false,new OutfitClipDefinition[0],null,null,null,null)});
                var runtime=new HolidayKeepsakeRuntime(root,new DataMaintenanceGate(),new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,provider,new NativeValidator());
                var encounter=new HolidayKeepsakeEncounter("official:e-bao","e-bao","Captured name",new RelationshipTime(new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.Zero));
                var result=runtime.ObserveAsync(encounter,catalog).Result;
                if(result.Status!=HolidayKeepsakeObservationStatus.Completed||result.Results.Count!=1||result.Results[0].Status!=HolidayKeepsakeStatus.Saved)
                    throw new Exception("A real eligible Halloween encounter must durably save its original scene.");
                string state=File.ReadAllText(Path.Combine(root,"commemorations/holiday-state.json"));
                if(!state.Contains("Captured name")||state.Contains("SourceGeneration")||state.Contains("RelativePath"))throw new Exception("Persist frozen facts, never runtime generation or source path.");
                var descriptor=provider.Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.0").Descriptor;
                if(!File.ReadAllBytes(Path.Combine(root,"commemorations/scenes/"+descriptor.Sha256+".png")).SequenceEqual(File.ReadAllBytes(Path.Combine("assets",descriptor.RelativePath))))throw new Exception("Retained original bytes changed.");
                if(typeof(HolidayCalendar).Assembly.GetType("Dafeiyu.HolidayKeepsakeSceneValidator")==null)throw new Exception("The WPF original-size native validator is missing.");
                if(typeof(RelationshipStageArtAsset).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).All(c=>c.GetParameters().Length!=12))throw new Exception("Reviewed future holiday scenes cannot use the existing capture contract.");
                Console.WriteLine("PASS: "+HolidayKeepsakeContractChecks.Run("work/h1a/wpf-"+Guid.NewGuid().ToString("N"),new HolidayKeepsakeSceneValidator())+" holiday keepsake contracts");return 0;
            }catch(Exception e){Console.WriteLine("FAIL: "+e);return 1;}
        }
        sealed class NativeValidator:IHolidayKeepsakeSceneValidator
        {
            public void Validate(byte[] png,int width,int height)
            {
                using(var input=new MemoryStream(png,false)){
                    var decoder=new System.Windows.Media.Imaging.PngBitmapDecoder(input,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    var frame=decoder.Frames[0];if(frame.PixelWidth!=width||frame.PixelHeight!=height)throw new InvalidDataException();
                    int stride=(width*frame.Format.BitsPerPixel+7)/8;frame.CopyPixels(new byte[stride*height],stride,0);
                }
            }
        }
    }
}
