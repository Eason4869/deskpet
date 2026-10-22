using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Dafeiyu.Tests
{
    public static class OfficialCompanionLegacyTests
    {
        static int passed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        public static int Main()
        {
            string temporary=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"legacy-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try {
                string repo=Environment.CurrentDirectory;
                while(!Directory.Exists(Path.Combine(repo,"assets","bundled-skins"))) {
                    repo=Path.GetDirectoryName(repo);if(String.IsNullOrEmpty(repo))throw new InvalidOperationException("Run from the repository or a child directory.");
                }
                foreach(string id in new[]{"xiaobei","gugugaga"}) {
                    string source=Path.Combine(repo,"assets","bundled-skins",id);
                    var trusted=OfficialCompanionLegacy.MatchImport(source,id,CancellationToken.None);
                    Check(trusted!=null&&trusted.CharacterId==id&&trusted.RootDirectory==source,"canonical original source matches "+id);
                    Check(OfficialCompanionLegacy.MatchImport(source,id=="xiaobei"?"gugugaga":"xiaobei",CancellationToken.None)==null,"same source cannot confer another role identity "+id);
                    string revision=id=="xiaobei"?"0b50a6ecd8a5bbdf97ae5779":"4e57798b0b0c635ba5a61a2d";
                    string cache=Path.Combine(temporary,"bundled-skins",id+"-"+revision);
                    Copy(source,cache);
                    var found=OfficialCompanionLegacy.FindBundled(temporary,id,CancellationToken.None);
                    Check(found!=null&&found.RootDirectory==cache,"legacy real cache naming and full hashes verified "+id);
                    byte[] original=File.ReadAllBytes(Path.Combine(cache,"pet.json"));
                    string repair=cache+"-repair-"+Guid.NewGuid().ToString("N");Copy(cache,repair);
                    File.WriteAllText(Path.Combine(cache,"pet.json"),"{}");
                    Check(OfficialCompanionLegacy.FindBundled(temporary,id,CancellationToken.None).RootDirectory==repair,"bad base retains verified existing repair "+id);
                    File.WriteAllText(Path.Combine(repair,"pet.json"),"{}");
                    Check(OfficialCompanionLegacy.FindBundled(temporary,id,CancellationToken.None)==null,"all damaged cache candidates rejected "+id);
                    File.WriteAllBytes(Path.Combine(cache,"pet.json"),original);
                    var manifest=OfficialJson.File(Path.Combine(cache,"pet.json"),65536);
                    foreach(string key in new[]{"id","pack_version","avatar","extra_animations","alternate_animations","voice_presets","special_action2"})manifest.Remove(key);
                    manifest["atlas"]=null;Write(cache,manifest);
                    Check(OfficialCompanionLegacy.MatchImport(cache,id,CancellationToken.None)!=null,"historical stripped serializer profile recognized "+id);
                    Check(OfficialCompanionLegacy.FindBundled(temporary,id,CancellationToken.None)==null,"semantic imported profile does not fake exact old cache manifest "+id);
                    var jump=OfficialJson.Object(OfficialJson.Object(manifest["animations"])["jumping"]);
                    object oldFps=jump["fps"];jump["fps"]=3;Write(cache,manifest);
                    Check(OfficialCompanionLegacy.MatchImport(cache,id,CancellationToken.None)==null,"changed original timing stays custom "+id);
                    jump["fps"]=oldFps;jump["hold_last_seconds"]=.5;Write(cache,manifest);
                    Check(OfficialCompanionLegacy.MatchImport(cache,id,CancellationToken.None)==null,"changed final hold stays custom "+id);
                    jump["hold_last_seconds"]=0;Write(cache,manifest);
                    Check(OfficialCompanionLegacy.MatchImport(cache,id,CancellationToken.None)!=null,"serialized zero final hold retains exact old semantics "+id);
                    string prompt=(string)manifest["system_prompt"];manifest["system_prompt"]=prompt+"changed";Write(cache,manifest);
                    Check(OfficialCompanionLegacy.MatchImport(cache,id,CancellationToken.None)==null,"changed persona cannot acquire official provenance "+id);
                    manifest["system_prompt"]=prompt;manifest["extra_animations"]=new Dictionary<string,object>();Write(cache,manifest);
                    Check(OfficialCompanionLegacy.MatchImport(cache,id,CancellationToken.None)==null,"partially declared optional profile is not stripped canonical profile "+id);
                    manifest.Remove("extra_animations");Write(cache,manifest);
                    string preview=Path.Combine(cache,(string)manifest["preview"]);byte[] png=File.ReadAllBytes(preview);png[png.Length-1]^=1;File.WriteAllBytes(preview,png);
                    Check(OfficialCompanionLegacy.MatchImport(cache,id,CancellationToken.None)==null,"same-length modified image rejected "+id);
                    Check(!File.Exists(Path.Combine(cache,"receipt.json"))&&!Directory.Exists(Path.Combine(temporary,"official-companions")),"recognition creates no complete installation or identity receipt "+id);
                }
                Check(OfficialCompanionLegacy.MatchImport(temporary,"custom",CancellationToken.None)==null,"unknown IDs never become official");
                using(var cancel=new CancellationTokenSource()) {
                    cancel.Cancel();bool canceled=false;try{OfficialCompanionLegacy.MatchImport(temporary,"xiaobei",cancel.Token);}catch(OperationCanceledException){canceled=true;}
                    Check(canceled,"cancellation remains cancellation");
                }
                Console.WriteLine("Official legacy tests: "+passed+" passed");return 0;
            } catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally {Directory.Delete(temporary,true);}
        }
        static void Write(string folder,object value){File.WriteAllBytes(Path.Combine(folder,"pet.json"),OfficialJson.Bytes(value));}
        static void Copy(string source,string target)
        {
            Directory.CreateDirectory(target);
            foreach(string path in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {
                string relative=path.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                if(Path.GetExtension(path)!=".png"&&relative!="pet.json")continue;
                string destination=Path.Combine(target,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(path,destination);
            }
        }
    }
}
