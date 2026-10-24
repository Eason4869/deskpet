using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Dafeiyu;
public static class RelationshipStageArtContractChecks
{
    public static int Run(Func<RelationshipStageArtAsset,Stream> resource)
    {
        int count=0;Action<bool,string> check=delegate(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label);};
        check(RelationshipStageArt.All.Count==35,"controlled manifest has thirty stages plus five holiday scenes");
        check(RelationshipStageArt.All.Select(x=>x.RelativePath).Distinct().Count()==35,"all controlled paths distinct");
        foreach(var asset in RelationshipStageArt.All){
            RelationshipStageArtAsset found;
            check(RelationshipStageArt.TryGet(asset.CompanionId,asset.StageId,out found)==(asset.HolidayId==null),"stage selection exact "+asset.SceneId);
            if(asset.HolidayId==null)check(Object.ReferenceEquals(found,asset),"stable descriptor identity");
            using(var stream=resource(asset))check(RelationshipStageArt.ReadVerified(stream,asset).Length==asset.ByteLength,"actual production original/hash/geometry "+asset.SceneId);
        }
        RelationshipStageArtAsset ignored;
        foreach(var id in new[]{"e-bao","official:E-bao","official:unknown","custom-pack:e-bao","legacy-skin:skin-ebao","E包","official:e-bao/..",null})check(!RelationshipStageArt.TryGet(id,"first_meeting",out ignored),"spoof/name/custom/unknown rejected "+id);
        foreach(var stage in new[]{"FIRST_MEETING","../familiar","halloween","future",null})check(!RelationshipStageArt.TryGet("official:e-bao",stage,out ignored),"unknown stage rejected "+stage);
        int opens=0;
        var provider=new RelationshipSceneProvider(delegate(RelationshipStageArtAsset a){opens++;return resource(a);});
        var capture=provider.Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.0");
        if(capture==null)throw new Exception("raw capture missing");
        check(opens==0,"raw source capture is pure lookup/no I/O");
        check(provider.Capture("official:e-bao","marfu","halloween","halloween-2026","1.0.0")==null,"verified character mismatch rejected");
        check(provider.Capture("custom-pack:e-bao","e-bao","halloween","halloween-2026","1.0.0")==null,"custom identity cannot capture official holiday art");
        check(provider.Capture("official:e-bao","e-bao","halloween","wrong","1.0.0")==null&&provider.Capture("official:e-bao","e-bao","halloween","halloween-2026","2.0.0")==null,"outfit ID and version both exact");
        check(provider.Capture("official:e-bao","e-bao","future","halloween-2026","1.0.0")==null,"future holiday has no implicit fallback");
        var again=provider.Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.0");
        check(capture.SourceGeneration==again.SourceGeneration&&capture.Descriptor.SceneVersion==again.Descriptor.SceneVersion,"generation runtime stable while persisted scene version stays content-bound");
        check(capture.SourceGeneration!=new RelationshipSceneProvider(resource).Capture("official:e-bao","e-bao","halloween","halloween-2026","1.0.0").SourceGeneration,"provider generation is runtime-only");
        byte[] original;using(var stream=capture.OpenRead())original=RelationshipStageArt.ReadVerified(stream,capture.Descriptor);
        check(original.SequenceEqual(File.ReadAllBytes(Path.Combine("assets",capture.Descriptor.RelativePath))),"retained raw source is exact original, no downsampling");
        RelationshipStageArt.TryGet("official:e-bao","first_meeting",out ignored);var first=ignored;
        Action<byte[],string> reject=delegate(byte[] bytes,string label){bool rejected=false;try{using(var stream=new MemoryStream(bytes))RelationshipStageArt.ReadVerified(stream,first);}catch(InvalidDataException){rejected=true;}check(rejected,label);};
        byte[] valid;using(var stream=resource(first))valid=RelationshipStageArt.ReadVerified(stream,first);
        reject(valid.Take(40).ToArray(),"truncated source rejected");
        var bad=(byte[])valid.Clone();bad[0]=0;reject(bad,"invalid PNG source rejected");
        bad=(byte[])valid.Clone();bad[19]^=1;reject(bad,"descriptor dimensions mismatch rejected");
        bad=(byte[])valid.Clone();bad[bad.Length/2]^=1;reject(bad,"wrong SHA256 source rejected");
        reject(new byte[RelationshipStageArt.MaximumBytes+1],"oversized stream rejected");
        // A controlled fixture descriptor with a freshly recomputed hash still cannot accept bad ratio.
        var ctor=typeof(RelationshipStageArtAsset).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Single();
        bad=(byte[])valid.Clone();bad[16]=0;bad[17]=0;bad[18]=3;bad[19]=32;
        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bad)).Replace("-","").ToLowerInvariant();
        var ratio=(RelationshipStageArtAsset)ctor.Invoke(new object[]{"official:e-bao","first_meeting","test.png",800,first.PixelHeight,bad.Length,hash});
        bool ratioRejected=false;try{using(var stream=new MemoryStream(bad))RelationshipStageArt.ReadVerified(stream,ratio);}catch(InvalidDataException){ratioRejected=true;}check(ratioRejected,"controlled wrong-ratio descriptor rejected despite matching hash");
        return count;
    }
}
