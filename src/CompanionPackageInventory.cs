using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace Dafeiyu
{
    // Enumerates bytes, not UI objects. Native image decoding remains a mandatory install gate.
    internal sealed class CompanionPackageInventory
    {
        internal string PackageId {get;private set;}
        internal string ContentSha256 {get;private set;}
        internal IList<string> AllReferences {get;private set;}
        internal long ExpandedBytes {get;private set;}
        internal string HolidayRoot {get;private set;}
        internal HolidayCatalog HolidayCatalog {get;private set;}
        readonly string root;
        readonly CancellationToken cancel;
        readonly Dictionary<string,string> references=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        CompanionPackageInventory(string root,CancellationToken cancel){this.root=Path.GetFullPath(root);this.cancel=cancel;}
        internal static CompanionPackageInventory Read(string root,CancellationToken cancel)
        {var result=new CompanionPackageInventory(root,cancel);result.Collect();return result;}
        string Add(string path)
        {
            cancel.ThrowIfCancellationRequested();string old;
            if(references.TryGetValue(path,out old)){if(old!=path)throw Invalid();return CompanionPackStorage.AssetPath(root,path);}
            string full=CompanionPackStorage.AssetPath(root,path);
            if(!File.Exists(full))throw Invalid();
            long size=new FileInfo(full).Length;if(size>CompanionPackagePolicy.FileLimit(path,false))throw Invalid();
            ExpandedBytes=checked(ExpandedBytes+size);CompanionPackagePolicy.CheckExpanded(ExpandedBytes);
            references.Add(path,path);return full;
        }
        Dictionary<string,object> Json(string path)
        {
            int maximum=checked((int)(path=="pet.json"?CompanionPackagePolicy.MaximumManifestBytes:CompanionPackagePolicy.MaximumMetadataBytes));
            byte[] bytes=File.ReadAllBytes(Add(path));
            if(bytes.Length>=3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191)bytes=bytes.Skip(3).ToArray();
            return OfficialJson.Object(OfficialJson.Parse(bytes,maximum));
        }
        static Dictionary<string,object> Object(object value){return OfficialJson.Object(value);}
        static string Text(Dictionary<string,object> value,string key){return OfficialJson.Text(value,key,200);}
        void DefaultFiles(Dictionary<string,object> manifest)
        {
            var appearance=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string key in new[]{"animations","extra_animations","alternate_animations"}) {
                object raw;if(!manifest.TryGetValue(key,out raw))continue;
                foreach(var clip in Object(raw).Values)appearance.Add(Text(Object(clip),"file"));
            }
            object gaze;if(manifest.TryGetValue("gaze",out gaze))appearance.Add(Text(Object(gaze),"file"));
            foreach(string key in new[]{"avatar","preview"}){object value;if(manifest.TryGetValue(key,out value)&&value is string&&!String.IsNullOrEmpty((string)value))appearance.Add((string)value);}
            long bytes=0;foreach(string path in appearance){bytes=checked(bytes+new FileInfo(Add(path)).Length);}
            if(bytes>CompanionPackagePolicy.MaximumAppearanceBytes)throw Invalid();
        }
        void Collect()
        {
            var manifest=Json("pet.json");PackageId=Text(manifest,"id");if(!CompanionPackStorage.ValidId(PackageId))throw Invalid();
            DefaultFiles(manifest);
            if(File.Exists(CompanionPackStorage.AssetPath(root,"companions.json"))) {
                var identity=Json("companions.json");if(Text(identity,"id")!=PackageId)throw Invalid();
            }
            string holiday=CompanionPackStorage.AssetPath(root,"holidays");
            if(Directory.Exists(holiday)) {
                var index=Json("holidays/catalog.json");var entries=OfficialJson.Array(OfficialJson.Required(index,"outfits"));
                if(entries.Length==0||entries.Length>CompanionPackagePolicy.MaximumHolidayOutfits)throw Invalid();
                foreach(var raw in entries){var entry=Object(raw);if(Text(entry,"character_id")!=PackageId)throw Invalid();Json("holidays/"+Text(entry,"definition"));}
                HolidayRoot=holiday;HolidayCatalog=new HolidayCatalog(holiday);
                if(!String.IsNullOrEmpty(HolidayCatalog.Warning)||HolidayCatalog.Outfits.Count!=entries.Length||HolidayCatalog.Outfits.Any(x=>!x.Ready||x.CharacterId!=PackageId))throw Invalid();
                foreach(var outfit in HolidayCatalog.Outfits) {
                    long bytes=0;var images=outfit.Clips.Concat(new[]{outfit.Gaze,outfit.EasterEgg}).Select(x=>x.Path).Concat(new[]{outfit.Preview}).Distinct(StringComparer.OrdinalIgnoreCase);
                    foreach(string path in images)bytes=checked(bytes+new FileInfo(Add("holidays/"+path)).Length);
                    if(bytes>CompanionPackagePolicy.MaximumAppearanceBytes)throw Invalid();
                }
            }
            int count=0;InspectDirectory(root,"",ref count);
            var files=references.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var digest=new StringBuilder();
            foreach(string path in files){cancel.ThrowIfCancellationRequested();string full=CompanionPackStorage.AssetPath(root,path);using(var stream=File.OpenRead(full))using(var hash=SHA256.Create())digest.Append(path).Append('\0').Append(stream.Length.ToString(CultureInfo.InvariantCulture)).Append('\0').Append(BitConverter.ToString(hash.ComputeHash(stream))).Append('\n');}
            using(var hash=SHA256.Create())ContentSha256=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(digest.ToString()))).Replace("-","").ToLowerInvariant();
            AllReferences=new ReadOnlyCollection<string>(files);
        }
        void InspectDirectory(string directory,string prefix,ref int count)
        {
            foreach(string entry in Directory.EnumerateFileSystemEntries(directory)) {
                cancel.ThrowIfCancellationRequested();if(++count>CompanionPackagePolicy.MaximumEntries)throw Invalid();
                if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)throw Invalid();
                string relative=prefix+Path.GetFileName(entry);CompanionPackStorage.CheckRelative(relative);
                if(Directory.Exists(entry)){InspectDirectory(entry,relative+"/",ref count);continue;}
                string existing;if(references.TryGetValue(relative,out existing)){if(existing!=relative)throw Invalid();continue;}
                if(prefix==""&&(relative=="README.txt"||relative=="README.md"||relative=="LICENSE"||relative=="LICENSE.txt"||relative=="LICENSE.md")){Add(relative);continue;}
                throw Invalid();
            }
        }
        static InvalidDataException Invalid(){return new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeE"));}
    }
}
