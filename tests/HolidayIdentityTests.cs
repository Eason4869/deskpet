using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Dafeiyu.Tests
{
    public static class HolidayIdentityTests
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        static string Trusted(SkinDefinition skin){var property=typeof(SkinDefinition).GetProperty("BuiltinCharacterId");return property==null?null:(string)property.GetValue(skin,null);}
        [STAThread] public static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"holiday-identity-"+Guid.NewGuid().ToString("N"));
            try {
                var catalog=new SkinCatalog(Path.Combine(root,"data"));
                foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"})Check(Trusted(catalog.Find(id))==id,"trusted registration "+id);
                var fake=new SkinDefinition("e-bao","E包","ignored",null,false);Check(Trusted(fake)==null,"same id and name cannot claim builtin trust");
                var bundled=catalog.Find("gugugaga");string legacy=Path.Combine(root,"legacy");Directory.CreateDirectory(legacy);
                foreach(string source in Directory.GetFiles(bundled.Source,"*",SearchOption.AllDirectories)) {string target=Path.Combine(legacy,source.Substring(bundled.Source.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target);}
                string manifest=Path.Combine(legacy,"pet.json"),json=File.ReadAllText(manifest);
                File.WriteAllText(manifest,Regex.Replace(json,"\"id\"\\s*:\\s*\"gugugaga\"\\s*,?",""));
                var migrated=catalog.Import(manifest);Check(migrated.Id.StartsWith("skin-") && Trusted(migrated)=="gugugaga","legacy personal id retains source trust");
                Check(new SkinCatalog(Path.Combine(root,"data")).Find(migrated.Id).Id==migrated.Id,"legacy persona identity survives reload");
                File.WriteAllText(manifest,json);var updated=catalog.Import(manifest);
                Check(updated.Id==migrated.Id && Trusted(updated)==null,"same id import retains persona but never acquires builtin trust");
                Check(Trusted(new SkinCatalog(Path.Combine(root,"data")).Find(updated.Id))==null,"untrusted update stays untrusted on reload");
                File.WriteAllText(manifest,json.Replace("\"name\": \"咕嘎\"","\"name\": \"用户同 ID 版本\""));catalog.Import(manifest);
                string oldFolder=Path.Combine(root,"data","skins","skin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(oldFolder);
                foreach(string source in Directory.GetFiles(bundled.Source,"*",SearchOption.AllDirectories)){string target=Path.Combine(oldFolder,source.Substring(bundled.Source.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target);}
                File.WriteAllText(Path.Combine(oldFolder,"pet.json"),Regex.Replace(json,"\"id\"\\s*:\\s*\"gugugaga\"\\s*,?",""));
                var reconciled=new SkinCatalog(Path.Combine(root,"data")).Find("gugugaga");
                Check(Trusted(reconciled)==null && reconciled.Name=="用户同 ID 版本","matching old legacy folder cannot confer trust on installed override");
                return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
