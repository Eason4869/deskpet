using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace Dafeiyu
{
    internal sealed class OfficialCompanionStorage
    {
        internal readonly string Root;internal Action<string,string> Commit=AtomicFile.Commit;
        internal OfficialCompanionStorage(string dataRoot){if(String.IsNullOrWhiteSpace(dataRoot))throw new ArgumentException(TextResources.Get("officialCompanionStorage.officialCompanionStorage.text"));Root=Path.Combine(Path.GetFullPath(dataRoot),"official-companions");SafeDirectory(Root);}
        internal static void SafeDirectory(string path)
        {
            string current=Path.GetFullPath(path);
            while(!String.IsNullOrEmpty(current)){if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.safeDirectory.text"));current=Path.GetDirectoryName(current);}
        }
        internal static string SafePath(string root,string relative)
        {
            if(String.IsNullOrEmpty(relative)||relative.Length>220||relative.StartsWith("/",StringComparison.Ordinal)||relative.Contains("\\")||relative.Contains(":"))throw new InvalidDataException(TextResources.Get("officialCompanionStorage.safePath.text"));
            foreach(string part in relative.Split('/'))if(part.Length==0||part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||part.Any(c=>c<32||"<>\"|?*".IndexOf(c)>=0)||Regex.IsMatch(part,@"\A(?:con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|\z)",RegexOptions.IgnoreCase))throw new InvalidDataException(TextResources.Get("officialCompanionStorage.safePath.text"));
            SafeDirectory(root);string current=root;foreach(string part in relative.Split('/')){current=Path.Combine(current,part);SafeDirectory(current);}return current;
        }
        internal static void DeleteOwned(string directory){if(!Directory.Exists(directory))return;SafeDirectory(directory);foreach(string path in Directory.GetFileSystemEntries(directory)){SafeDirectory(path);if(Directory.Exists(path))DeleteOwned(path);else File.Delete(path);}Directory.Delete(directory);}
        internal static string HashFile(string path){using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        internal string CharacterRoot(string id){OfficialCompanionRules.Id(id);return SafePath(Root,id);}
        internal static string VersionDirectory(OfficialCompanionAsset asset){return "v-"+asset.Version+"-"+asset.Sha256;}
        internal OfficialCompanionInstallation Installed(string id)
        {
            OfficialCompanionRules.Id(id);
            try {
                string root=CharacterRoot(id),pointer=SafePath(root,"current.txt");if(!File.Exists(pointer))return null;
                if(new FileInfo(pointer).Length>200)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.installed.text"));
                string directory=File.ReadAllText(pointer,new UTF8Encoding(false,true));
                if(directory.Length>150||directory.Trim()!=directory||!Regex.IsMatch(directory,@"\Av-[a-zA-Z0-9.-]+-[a-f0-9]{64}\z"))throw new InvalidDataException(TextResources.Get("officialCompanionStorage.installed.text"));
                return ReadVersion(SafePath(root,directory),id,null);
            }catch(Exception error){if(error is IOException||error is InvalidDataException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is OverflowException)return null;throw;}
        }
        internal OfficialCompanionInstallation ReadVersion(string directory,string id,OfficialCompanionAsset expected)
        {
            if(!Directory.Exists(directory))return null;SafeDirectory(directory);
            var receipt=OfficialJson.File(SafePath(directory,"receipt.json"),1024*1024);
            if(OfficialJson.Integer(receipt,"schema_version")!=1||OfficialJson.Text(receipt,"repository",200)!=ReleaseInfo.RepositoryUrl)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text"));
            var asset=ReadAsset(OfficialJson.Object(OfficialJson.Required(receipt,"asset")));
            if(asset.Id!=id||Path.GetFileName(directory)!=VersionDirectory(asset)||(expected!=null&&!SameAsset(asset,expected)))throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text.alternativeA"));
            var listed=OfficialJson.Array(OfficialJson.Required(receipt,"files"));if(listed.Length<1||listed.Length>CompanionPackagePolicy.MaximumEntries)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text.alternativeB"));
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
            foreach(var raw in listed){var item=OfficialJson.Object(raw);string path=OfficialJson.Text(item,"path",220),hash=OfficialJson.Text(item,"sha256",64);long length=OfficialJson.Integer(item,"size");if(path=="receipt.json"||!names.Add(path)||!OfficialCompanionRules.Hash(hash)||length<0||length>OfficialCompanionPackageValidation.FileLimit(path))throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text.alternativeC"));total=checked(total+length);if(total>CompanionPackagePolicy.MaximumExpandedBytes)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text.alternativeD"));string full=SafePath(directory,path);if(!File.Exists(full)||new FileInfo(full).Length!=length||HashFile(full)!=hash)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text.alternativeE"));}
            var actual=Inventory(directory);if(actual.Count!=names.Count||actual.Any(p=>!names.Contains(p)))throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text.alternativeF"));
            if(!names.Contains("pet.json")||!names.Contains("companions.json")||!names.Contains("holidays/catalog.json"))throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readVersion.text.alternativeG"));
            return new OfficialCompanionInstallation(asset,directory);
        }
        internal static bool SameAsset(OfficialCompanionAsset a,OfficialCompanionAsset b){return a.Id==b.Id&&a.Version==b.Version&&a.Sha256==b.Sha256&&a.Size==b.Size&&a.Name==b.Name&&a.MinimumAppVersion==b.MinimumAppVersion&&a.Outfits.Count==b.Outfits.Count&&a.Outfits.All(x=>b.Outfits.Any(y=>x.OutfitId==y.OutfitId&&x.Version==y.Version));}
        internal static List<string> Inventory(string directory)
        {
            var result=new List<string>();Walk(directory,directory,result);return result;
        }
        static void Walk(string root,string current,List<string> result)
        {
            SafeDirectory(current);foreach(string path in Directory.GetFileSystemEntries(current)){SafeDirectory(path);if(Directory.Exists(path))Walk(root,path,result);else {string relative=path.Substring(root.Length+1).Replace(Path.DirectorySeparatorChar,'/');if(relative!="receipt.json")result.Add(relative);if(result.Count>CompanionPackagePolicy.MaximumEntries)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.walk.text"));}}
        }
        internal void WriteReceipt(string stage,OfficialCompanionAsset asset,CancellationToken cancel)
        {
            var files=new List<object>();foreach(string relative in Inventory(stage).OrderBy(x=>x,StringComparer.Ordinal)){cancel.ThrowIfCancellationRequested();string path=SafePath(stage,relative);files.Add(new {path=relative,size=new FileInfo(path).Length,sha256=HashFile(path)});}
            File.WriteAllBytes(SafePath(stage,"receipt.json"),OfficialJson.Bytes(new{schema_version=1,repository=ReleaseInfo.RepositoryUrl,asset=AssetRecord(asset),files=files.ToArray()}));
        }
        internal static object AssetRecord(OfficialCompanionAsset asset){return new {id=asset.Id,name=asset.Name,version=asset.Version,min_app_version=asset.MinimumAppVersion,tag=asset.Tag,asset_name=asset.AssetName,size=asset.Size,sha256=asset.Sha256,outfits=asset.Outfits.Select(x=>new{outfit_id=x.OutfitId,version=x.Version}).ToArray()};}
        static OfficialCompanionAsset ReadAsset(Dictionary<string,object> record)
        {
            string id=OfficialJson.Text(record,"id",48);OfficialCompanionRules.Id(id);string version=OfficialCompanionRules.Version(OfficialJson.Text(record,"version",64)),minimum=OfficialCompanionRules.Version(OfficialJson.Text(record,"min_app_version",64)),tag=OfficialJson.Text(record,"tag",90),name=OfficialJson.Text(record,"asset_name",150),hash=OfficialJson.Text(record,"sha256",64);long size=OfficialJson.Integer(record,"size");
            string feed=tag.StartsWith("companions-v",StringComparison.Ordinal)?tag.Substring(12):tag.StartsWith("v",StringComparison.Ordinal)?tag.Substring(1):null;OfficialCompanionRules.Version(feed);
            if(name!="DeskPet-Companion-"+id+"-"+version+".zip"||!OfficialCompanionRules.Hash(hash)||size<=0||size>OfficialCompanionRules.MaximumPackage)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readAsset.text"));
            var outfits=new List<OfficialCompanionOutfit>();var seen=new HashSet<string>();foreach(var raw in OfficialJson.Array(OfficialJson.Required(record,"outfits"))){var item=OfficialJson.Object(raw);string outfit=OfficialJson.Text(item,"outfit_id",64);if(!Regex.IsMatch(outfit,@"\A[a-z0-9][a-z0-9-]{0,63}\z")||!seen.Add(outfit)||outfits.Count==32)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.readAsset.text.alternativeA"));outfits.Add(new OfficialCompanionOutfit(outfit,OfficialCompanionRules.Version(OfficialJson.Text(item,"version",64))));}
            return new OfficialCompanionAsset(id,OfficialJson.Text(record,"name",80),version,minimum,tag,name,size,hash,outfits);
        }
        internal OfficialCompanionInstallation Publish(string stage,OfficialCompanionAsset asset,CancellationToken cancel,Action checkAlive,Action<Action> publication)
        {
            string root=CharacterRoot(asset.Id),name=VersionDirectory(asset),destination=SafePath(root,name),pointer=SafePath(root,"current.txt"),temporary=SafePath(root,".current-"+Guid.NewGuid().ToString("N")),quarantine=null;bool moved=false,published=false;
            try {
                cancel.ThrowIfCancellationRequested();checkAlive();
                if(Directory.Exists(destination)){quarantine=SafePath(root,".invalid-"+Guid.NewGuid().ToString("N"));Directory.Move(destination,quarantine);}
                Directory.Move(stage,destination);moved=true;
                // Verify with the actual, longer version root before changing the pointer. This also
                // catches native callbacks modifying files and unsupported platform path lengths.
                if(ReadVersion(destination,asset.Id,asset)==null)throw new InvalidDataException(TextResources.Get("officialCompanionStorage.publish.text"));
                File.WriteAllText(temporary,name,new UTF8Encoding(false));
                publication(()=>{cancel.ThrowIfCancellationRequested();checkAlive();Commit(temporary,pointer);published=true;});
                return new OfficialCompanionInstallation(asset,destination);
            }finally {
                if(File.Exists(temporary))File.Delete(temporary);
                if(!published){try{if(moved&&Directory.Exists(destination))Directory.Move(destination,stage);}finally{if(quarantine!=null&&Directory.Exists(quarantine))Directory.Move(quarantine,destination);}}
            }
        }
    }
}
