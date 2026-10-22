using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Dafeiyu.Tests
{
    // Release authoring gate: real ZIP -> pure validation -> WIC decode ->
    // immutable publication -> verified final root -> fresh native decoder.
    public static class OfficialCompanionPackageSmoke
    {
        [STAThread] public static int Main(string[] args)
        {
            try {
                if(args.Length!=1)throw new ArgumentException("Supply the generated artifact directory.");
                string artifacts=Path.GetFullPath(args[0]);
                var indexes=Directory.GetFiles(artifacts,"DeskPet-Companions-*.json");
                if(indexes.Length!=1)throw new InvalidDataException("Exactly one complete index is required.");
                var index=OfficialJson.File(indexes[0],1024*1024);
                if(OfficialJson.Integer(index,"schema_version")!=1)throw new InvalidDataException("Unknown index schema.");
                var entries=OfficialJson.Array(index["companions"]);
                if(entries.Length!=5)throw new InvalidDataException("All five companions are required.");
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"op",Guid.NewGuid().ToString("N").Substring(0,8));
                if(Directory.Exists(root))throw new IOException("Validation root already exists.");
                Directory.CreateDirectory(root);var store=new OfficialCompanionStorage(root);
                var seen=new HashSet<string>(StringComparer.Ordinal);var results=new List<object>();
                var clock=System.Diagnostics.Stopwatch.StartNew();
                foreach(var raw in entries) {
                    var entry=OfficialJson.Object(raw);string id=OfficialJson.Text(entry,"id",48);OfficialCompanionRules.Id(id);
                    if(!seen.Add(id))throw new InvalidDataException("Duplicate companion.");
                    string version=OfficialCompanionRules.Version(OfficialJson.Text(entry,"version",64));
                    string name=OfficialJson.Text(entry,"asset_name",150);
                    if(name!="DeskPet-Companion-"+id+"-"+version+".zip")throw new InvalidDataException("Unexpected artifact name.");
                    string zip=OfficialCompanionStorage.SafePath(artifacts,name);
                    long size=OfficialJson.Integer(entry,"size");string hash=OfficialJson.Text(entry,"sha256",64);
                    if(!OfficialCompanionRules.Hash(hash)||new FileInfo(zip).Length!=size||OfficialCompanionStorage.HashFile(zip)!=hash)throw new InvalidDataException("Artifact digest or size changed.");
                    var outfits=OfficialJson.Array(entry["outfits"]).Select(x=>OfficialJson.Object(x)).Select(x=>new OfficialCompanionOutfit(OfficialJson.Text(x,"outfit_id",64),OfficialJson.Text(x,"version",64))).ToArray();
                    var asset=new OfficialCompanionAsset(id,OfficialJson.Text(entry,"name",80),version,OfficialCompanionRules.Version(OfficialJson.Text(entry,"min_app_version",64)),"companions-v"+version,name,size,hash,outfits);
                    string stage=Path.Combine(store.CharacterRoot(id),".authoring");Directory.CreateDirectory(stage);
                    OfficialCompanionPackageValidation.Extract(zip,stage,CancellationToken.None);
                    OfficialCompanionPackageValidation.Validate(stage,asset,CancellationToken.None);
                    new WpfOfficialNativeValidator().ValidateAsync(new OfficialCompanionInstallation(asset,stage),CancellationToken.None).GetAwaiter().GetResult();
                    store.WriteReceipt(stage,asset,CancellationToken.None);
                    var installed=store.Publish(stage,asset,CancellationToken.None,()=>{},action=>action());
                    var reopened=store.Installed(id);
                    if(reopened==null||reopened.RootDirectory!=installed.RootDirectory||Directory.Exists(stage))throw new InvalidDataException("Final-root publication failed.");
                    new WpfOfficialNativeValidator().ValidateAsync(reopened,CancellationToken.None).GetAwaiter().GetResult();
                    results.Add(new{id=id,sha256=hash,size=size,finalRootValidated=true,nativeValidated=true});
                    Console.WriteLine("PASS complete official native package: "+id+" / "+version);
                }
                string report=Path.Combine(Path.GetDirectoryName(artifacts),"native-validation.json");
                File.WriteAllBytes(report,OfficialJson.Bytes(new{schema_version=1,status="passed",elapsedSeconds=clock.Elapsed.TotalSeconds,companions=results.ToArray()}));
                Console.WriteLine("PASS all five complete native packages; report: "+report);return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
