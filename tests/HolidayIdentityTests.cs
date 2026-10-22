using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Dafeiyu.Tests
{
    public static class HolidayIdentityTests
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        static void CopyTree(string source,string destination)
        {foreach(string path in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string target=Path.Combine(destination,path.Substring(source.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(path,target);}}
        static string Trusted(SkinDefinition skin){var property=typeof(SkinDefinition).GetProperty("BuiltinCharacterId");return property==null?null:(string)property.GetValue(skin,null);}
        [STAThread] public static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"holiday-identity-"+Guid.NewGuid().ToString("N"));
            try {
                string data=Path.Combine(root,"data"),legacyId="skin-"+Guid.NewGuid().ToString("N");
                var seed=new SkinCatalog(Path.Combine(root,"seed"));var bundled=seed.FindOfficial("gugugaga");
                string legacy=Path.Combine(data,"skins",legacyId),importSource=Path.Combine(root,"import");
                CopyTree(bundled.Source,legacy);CopyTree(bundled.Source,importSource);
                SkinManifest historical;using(var input=File.OpenRead(Path.Combine(legacy,"pet.json")))historical=SkinCatalog.ReadManifest(input);
                historical.PackageId=null;historical.Avatar=null;historical.PackVersion=null;historical.ExtraAnimations=null;historical.AlternateAnimations=null;historical.VoicePresets=null;historical.SpecialAction2=null;
                string legacyManifest=Path.Combine(legacy,"pet.json");using(var output=File.Create(legacyManifest))SkinCatalog.Serializer().WriteObject(output,historical);
                byte[] legacyBytes=File.ReadAllBytes(legacyManifest);
                var catalog=new SkinCatalog(data);
                foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"})Check(Trusted(catalog.FindOfficial(id))==id,"trusted registration "+id);
                var fake=new SkinDefinition("e-bao","E包","ignored",null,false);Check(Trusted(fake)==null,"same id and name cannot claim builtin trust");
                var migrated=catalog.FindOfficial("gugugaga");
                Check(migrated.Id==legacyId && migrated.Source==legacy && Trusted(migrated)=="gugugaga" && migrated.PickerKey=="official:gugugaga","preexisting canonical legacy personal id retains source trust and picker provenance");
                Check(new SkinCatalog(data).FindOfficial("gugugaga").Id==legacyId,"legacy persona identity survives reload");
                string manifest=Path.Combine(importSource,"pet.json");var custom=catalog.Import(manifest);
                Check(custom.Id!=migrated.Id && custom.PickerKey=="custom-pack:gugugaga" && Trusted(custom)==null,"ordinary same id import receives separate custom persona and provenance");
                Check(catalog.FindOfficial("gugugaga")==migrated && File.ReadAllBytes(legacyManifest).SequenceEqual(legacyBytes),"custom import preserves trusted legacy row and original metadata");
                SkinManifest replacement;using(var input=File.OpenRead(manifest))replacement=SkinCatalog.ReadManifest(input);replacement.Name="用户同 ID 版本";
                using(var output=File.Create(manifest))SkinCatalog.Serializer().WriteObject(output,replacement);
                var updated=catalog.Import(manifest);Check(updated.Id==custom.Id && updated.Source!=custom.Source && Trusted(updated)==null,"same custom id update retains its own persona with a new source");
                string oldFolder=Path.Combine(data,"skins","skin-"+Guid.NewGuid().ToString("N"));CopyTree(legacy,oldFolder);
                var reconciled=new SkinCatalog(data);var reloaded=reconciled.FindPicker("custom-pack:gugugaga");
                Check(reloaded.Id==custom.Id && Trusted(reloaded)==null && reloaded.Name=="用户同 ID 版本","matching old legacy folder cannot confer trust on custom same id update");
                Check(reconciled.FindOfficial("gugugaga").Id==legacyId && Trusted(reconciled.FindOfficial("gugugaga"))=="gugugaga" && reconciled.Skins.Count(s=>s.PickerKey=="official:gugugaga")==1,"official legacy identity and unique trusted row survive custom update and restart");
                return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
