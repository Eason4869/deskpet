using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Dafeiyu.Tests
{
    public static class BundledSkinTests
    {
        static int passed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);passed++;}
        static byte[] Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return sha.ComputeHash(stream);}
        [STAThread] public static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundled-skin-tests-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string data=Path.Combine(root,"data");
                var catalog=new SkinCatalog(data);var skin=catalog.Find("xiaobei");
                var guga=catalog.Find("gugugaga");
                string package=Path.Combine(root,"guga.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(guga.Source,package);
                var installed=catalog.Import(package);
                Check(installed.Id!=guga.Id && installed.BuiltinCharacterId==null && installed.PickerKey=="custom-pack:gugugaga" && catalog.FindOfficial("gugugaga")==guga,"custom ZIP keeps separate persona and cannot replace official provenance");
                Check(installed.Source!=guga.Source && installed.Source.Contains("companion-packs"),"ZIP is installed outside immutable bundled cache");
                Check(AdditionalInteractions.Has(installed,"stumble") && AdditionalInteractions.HasOriginal(installed,"jumping"),"installed pack retains both extra and alternate motions");
                Check(SkinCatalog.GetAvatar(installed).PixelWidth>100,"installed ZIP supplies its own avatar");
                var packSettings=new PetSettings();PersonaProfiles.Activate(packSettings,installed);
                var speechService=new ConnectionSettings{BaseUrl="https://api.openai.com/v1",Model="tts-1",Protocol="openai"};
                Check(PersonaProfiles.VoiceFor(packSettings,installed.Id,"speech",speechService)=="shimmer","pack data selects its own supported speech recommendation");
                Check(new SkinCatalog(data).Find(installed.Id).Source==installed.Source && new SkinCatalog(data).FindOfficial("gugugaga").Id==guga.Id,"custom package and official identity survive reload independently");
                LegacyMigration(root,guga,package);
                RejectArchive(catalog,root,"../escape.txt",false,false);
                RejectArchive(catalog,root,"payload.exe",false,false);
                RejectArchive(catalog,root,"a.txt",true,false);
                RejectArchive(catalog,root,"link.txt",false,true);
                Check(new SkinCatalog(data).Find(installed.Id).Source==installed.Source,"rejected archives leave custom package untouched");
                var removedData=Path.Combine(root,"remove-data");var removal=new SkinCatalog(removedData);var removed=removal.Import(package);
                var removalSettings=new PetSettings();PersonaProfiles.Save(removalSettings,removed,"保留自定义人格");removalSettings.VoiceName="custom-private-voice";PersonaProfiles.SaveVoices(removalSettings);
                removal.Remove(removed);Check(removal.Skins.Count==5 && new SkinCatalog(removedData).FindPicker(removed.PickerKey)==null && new SkinCatalog(removedData).FindOfficial("gugugaga")!=null && Directory.Exists(removed.Source),"removing custom companion preserves its data and discoverable official entry");
                var reimported=removal.Import(package);PersonaProfiles.Activate(removalSettings,reimported);
                Check(reimported.Id==removed.Id && removal.Skins.Count==6 && new SkinCatalog(removedData).Skins.Count==6,"custom reimport restores its stable identity alongside official partner");
                Check(removalSettings.SystemPrompt=="保留自定义人格" && removalSettings.VoiceName=="custom-private-voice","custom same-ID update retains persona and voice preferences");
                removal.Remove(reimported);removal.RestoreRemoved();Check(removal.Skins.Count==6 && !removal.HasRemoved,"restore returns separate official and custom companions");
                while(removal.Skins.Count>1)removal.Remove(removal.Skins.Last());
                bool lastBlocked=false;try{removal.Remove(removal.Skins[0]);}catch(InvalidOperationException){lastBlocked=true;}Check(lastBlocked && removal.Skins.Count==1,"last companion cannot be removed");
                Check(guga.Id=="gugugaga" && guga.Illustrated && guga.Name=="咕嘎","fresh install offers fifth companion Guga");
                Check(guga.Source!=skin.Source && guga.Source.Contains("gugugaga-"),"Guga cache is isolated from Xiaobei");
                Check(guga.SystemPrompt.Contains("咕嘎") && guga.SystemPrompt.Contains("不确定") && guga.SpecialAction=="咕嘎摇摇","Guga persona and signature survive manifest loading");
                Check(SkinCatalog.GetAvatar(guga).PixelWidth>100,"Guga dedicated avatar decodes");
                var gugaSettings=new PetSettings();PersonaProfiles.Activate(gugaSettings,guga);
                PersonaProfiles.Save(gugaSettings,guga,"咕嘎自定义人格");PersonaProfiles.Activate(gugaSettings,skin);PersonaProfiles.Activate(gugaSettings,guga);
                Check(gugaSettings.SystemPrompt=="咕嘎自定义人格","Guga persona override survives companion switching");
                PersonaProfiles.Reset(gugaSettings,guga);Check(gugaSettings.SystemPrompt==guga.SystemPrompt,"Guga persona reset restores its own prompt");
                string gugaPreview=Path.Combine(guga.Source,guga.Manifest.Preview);byte[] gugaHash=Hash(gugaPreview);
                File.WriteAllText(gugaPreview,"damaged Guga cache");var gugaRepair=new SkinCatalog(data).Find("gugugaga");
                Check(gugaRepair.Source!=guga.Source && Hash(Path.Combine(gugaRepair.Source,gugaRepair.Manifest.Preview)).SequenceEqual(gugaHash),"Guga cache repairs from its own embedded bytes");
                Check(new SkinCatalog(data).Find("xiaobei").Source==skin.Source,"Guga repair leaves Xiaobei cache unchanged");
                Check(skin.Id=="xiaobei" && skin.Illustrated && skin.Name=="小贝","fresh install offers bundled Xiaobei without import");
                Check(catalog.Skins.Count(s=>s.Id=="xiaobei")==1 && catalog.Skins.Count(s=>s.Embedded)==3,"preset is unique and preserves original three partners");
                Check(skin.SystemPrompt.Contains("男性") && skin.SystemPrompt.Length>120 && skin.SpecialAction!="专属动作","bundled preset retains complete male persona and named action");
                var settings=new PetSettings();PersonaProfiles.Activate(settings,skin);
                Check(settings.SkinId=="xiaobei" && settings.SystemPrompt==skin.SystemPrompt,"selecting preset activates Xiaobei persona");
                PersonaProfiles.Save(settings,skin,"用户保留的小贝人格");PersonaProfiles.Activate(settings,catalog.Find("e-bao"));PersonaProfiles.Activate(settings,skin);
                Check(settings.SystemPrompt=="用户保留的小贝人格","bundled persona respects the user's per-companion override");
                PersonaProfiles.Reset(settings,skin);Check(settings.SystemPrompt==skin.SystemPrompt,"reset restores the complete bundled persona");
                Check(skin.Source.StartsWith(Path.GetFullPath(Path.Combine(data,"bundled-skins"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"preset assets use separate bundled cache");
                Check(!Directory.Exists(Path.Combine(data,"skins")),"fresh bundled loading does not create user imported skins directory");
                Check(skin.Manifest.Animations.Count==17 && skin.Manifest.Animations.Values.All(a=>a.Frames==8 && a.Fps>=1 && a.Fps<=8),"bundled Xiaobei has eight real poses per action at low frame rate");
                var motion=new SmoothMotion(skin);
                for(int action=0;action<IllustratedPack.Actions.Length;action++) {
                    string name=IllustratedPack.Actions[action];
                    double expected=name=="idle" ? 2 : name=="running-right" || name=="running-left" ? 1.76 : name=="jumping" || name=="landing" || name=="poked" ? 1 : name=="sleeping" ? 3.2 : name=="special" ? 2 : 1.6;
                    var clip=motion.Get(action<9?action:action+2);
                    Check(clip.Count==8 && Math.Abs(clip.Duration-expected)<1e-8,"bundled runtime honors selected eight-frame duration: "+name);
                }
                Check(skin.Load().HasGaze && SkinCatalog.GetPreview(skin).PixelWidth==480,"bundled gaze and preview decode");
                string json=Path.Combine(skin.Source,"pet.json");DateTime written=File.GetLastWriteTimeUtc(json);
                var again=new SkinCatalog(data).Find("xiaobei");
                Check(again.Source==skin.Source && File.GetLastWriteTimeUtc(json)==written,"reopening reuses verified immutable cache");
                string customSource=Path.Combine(root,"same-name-custom");Directory.CreateDirectory(customSource);
                foreach(string relative in IllustratedPack.Files(skin.Manifest)){string target=Path.Combine(customSource,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Path.Combine(skin.Source,relative),target);}
                SkinManifest customManifest;using(var input=File.OpenRead(json))customManifest=SkinCatalog.ReadManifest(input);customManifest.SystemPrompt+="\n这是用户自定义的同名伙伴。";
                string customJson=Path.Combine(customSource,"pet.json");using(var output=File.Create(customJson))SkinCatalog.Serializer().WriteObject(output,customManifest);
                var imported=catalog.Import(customJson);string importJson=Path.Combine(imported.Source,"pet.json");byte[] original=Hash(importJson);
                Check(imported.Id.StartsWith("skin-") && imported.Id!="xiaobei","user import with same name has independent identity");
                var reopened=new SkinCatalog(data);
                Check(reopened.Find(imported.Id).Id==imported.Id && reopened.Find("xiaobei").Id=="xiaobei","preset and same-named user import both survive reload");
                Check(Hash(importJson).SequenceEqual(original),"loading preset never rewrites user imported manifest");
                string cachedPreview=Path.Combine(skin.Source,skin.Manifest.Preview);byte[] preview=Hash(cachedPreview);
                File.WriteAllText(cachedPreview,"damaged local cache");
                var repaired=new SkinCatalog(data).Find("xiaobei");
                Check(repaired.Id=="xiaobei" && repaired.Source!=skin.Source && Hash(Path.Combine(repaired.Source,repaired.Manifest.Preview)).SequenceEqual(preview),"damaged cache is replaced from trusted embedded bytes in a new folder");
                Check(Hash(importJson).SequenceEqual(original) && reopened.Find(imported.Id).Load().HasGaze,"cache recovery leaves imported appearance usable");
                Check(new SkinCatalog(data).Find("xiaobei").Source==repaired.Source,"recovered cache is reused without accumulating copies");
                File.WriteAllText(Path.Combine(repaired.Source,"pet.json"),"{\"version\":2,\"name\":\"tampered\"}");
                Check(new SkinCatalog(data).Find("xiaobei").Name=="小贝","modified cached persona cannot override embedded preset");
                Check(!Directory.GetDirectories(Path.Combine(data,"bundled-skins"),".staging-*").Any(),"successful extraction leaves no staging folders");
                Check(new SkinCatalog(data).Find("not-installed").Id=="e-bao","unknown selection preserves existing default fallback");
                Console.WriteLine("PASS "+passed+" bundled skin checks");return 0;
            } catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally {Directory.Delete(root,true);}
        }
        static void LegacyMigration(string root,SkinDefinition original,string package)
        {
            string source=Path.Combine(root,"legacy-source"),data=Path.Combine(root,"migration-data");Directory.CreateDirectory(source);
            foreach(string relative in IllustratedPack.Files(original.Manifest)) {
                string target=Path.Combine(source,relative.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Path.Combine(original.Source,relative.Replace('/',Path.DirectorySeparatorChar)),target);
            }
            SkinManifest manifest;using(var stream=File.OpenRead(Path.Combine(original.Source,"pet.json")))manifest=SkinCatalog.ReadManifest(stream);
            manifest.PackageId=null;manifest.Avatar=null;manifest.PackVersion=null;manifest.ExtraAnimations=null;manifest.AlternateAnimations=null;manifest.VoicePresets=null;manifest.SpecialAction2=null;
            using(var stream=File.Create(Path.Combine(source,"pet.json")))SkinCatalog.Serializer().WriteObject(stream,manifest);
            // Model an actual old installation before official aliases existed.
            string legacyId="skin-"+Guid.NewGuid().ToString("N"),legacyFolder=Path.Combine(data,"skins",legacyId);Directory.CreateDirectory(legacyFolder);
            foreach(string relative in IllustratedPack.Files(manifest).Concat(new[]{"pet.json"})){string target=Path.Combine(legacyFolder,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Path.Combine(source,relative),target);}
            var catalog=new SkinCatalog(data);var legacy=catalog.Find(legacyId);
            Check(legacy.Id==legacyId && legacy.BuiltinCharacterId=="gugugaga" && catalog.Skins.Count(s=>s.PickerKey=="official:gugugaga")==1,"canonical prior legacy import retains personal identity and verified official provenance");
            var settings=new PetSettings();PersonaProfiles.Activate(settings,legacy);PersonaProfiles.Save(settings,legacy,"保留旧版咕嘎人格");settings.VoiceName="private-legacy-voice";PersonaProfiles.SaveVoices(settings);
            var updated=catalog.Import(package);PersonaProfiles.Activate(settings,updated);PersonaProfiles.Activate(settings,legacy);
            Check(updated.Id!=legacy.Id && updated.BuiltinCharacterId==null && settings.SystemPrompt=="保留旧版咕嘎人格" && settings.VoiceName=="private-legacy-voice","manual custom ZIP cannot take over canonical legacy identity or its persona and voice");
            Check(new SkinCatalog(data).Find(legacy.Id).Id==legacy.Id && new SkinCatalog(data).FindPicker(updated.PickerKey).Id==updated.Id,"legacy alias and separate custom package both survive restart");
            var repeated=catalog.Import(Path.Combine(source,"pet.json"));
            Check(repeated.Id==legacy.Id && repeated.BuiltinCharacterId=="gugugaga","repeated exact-art legacy import resolves retained trusted companion");
            SkinManifest slow;using(var input=File.OpenRead(Path.Combine(original.Source,"pet.json")))slow=SkinCatalog.ReadManifest(input);
            slow.Animations["jumping"].Fps=4;slow.AlternateAnimations["jumping"].Fps=4;
            using(var file=File.Create(Path.Combine(source,"pet.json")))SkinCatalog.Serializer().WriteObject(file,slow);
            var slower=catalog.Import(Path.Combine(source,"pet.json"));var clock=slower.CreateAnimation();clock.Play("jumping",.7);clock.Advance(1.1);
            Check(clock.State=="jumping","package alternate jump honors declared two-second duration");
            using(var file=File.Create(Path.Combine(source,"pet.json")))SkinCatalog.Serializer().WriteObject(file,manifest);
            var bitmap=IllustratedPack.ReadImage(Path.Combine(source,"preview.png"),480,520);var edited=new System.Windows.Media.Imaging.WriteableBitmap(bitmap);
            edited.WritePixels(new System.Windows.Int32Rect(0,0,1,1),new byte[]{0,0,255,255},4,0);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(edited));using(var file=File.Create(Path.Combine(source,"preview.png")))encoder.Save(file);
            var custom=new SkinCatalog(Path.Combine(root,"custom-data")).Import(Path.Combine(source,"pet.json"));
            Check(custom.Manifest.PackageId==null,"modified custom artwork is not overwritten based on its name");
        }
        static void RejectArchive(SkinCatalog catalog,string root,string entryName,bool duplicate,bool link)
        {
            string archive=Path.Combine(root,Guid.NewGuid().ToString("N")+".zip");
            using(var output=File.Create(archive))using(var zip=new System.IO.Compression.ZipArchive(output,System.IO.Compression.ZipArchiveMode.Create)) {
                var entry=zip.CreateEntry(entryName);if(link)entry.ExternalAttributes=unchecked((int)0xA1FF0000);
                using(var writer=new StreamWriter(entry.Open()))writer.Write("invalid");
                if(duplicate)using(var writer=new StreamWriter(zip.CreateEntry(entryName.ToUpperInvariant()).Open()))writer.Write("duplicate");
            }
            try{catalog.Import(archive);}catch(Exception error){if(!(error is IOException || error is InvalidDataException))throw;Check(true,"ZIP rejects unsafe entry: "+entryName+(duplicate?" duplicate":"")+(link?" symlink":""));return;}
            throw new Exception("ZIP accepted unsafe entry: "+entryName);
        }
    }
}
