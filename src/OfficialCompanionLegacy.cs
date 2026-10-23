using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dafeiyu
{
    // A proven old default source is usable offline, but is never a complete
    // official installation, does not issue a receipt and contains no holidays.
    public sealed class OfficialCompanionLegacySource
    {
        public string CharacterId {get;private set;}
        public string RootDirectory {get;private set;}
        public string SourceKey {get;private set;}
        internal OfficialCompanionLegacySource(string id,string root,string revision)
        {CharacterId=id;RootDirectory=root;SourceKey="legacy-"+revision+"-"+Guid.NewGuid().ToString("N");}
    }

    public static class OfficialCompanionLegacy
    {
        static readonly string[] OptionalProfile={"id","pack_version","avatar","extra_animations","alternate_animations","voice_presets","special_action2"};
        static readonly Dictionary<string,object>[] Records=ReadRecords();
        static Dictionary<string,object>[] ReadRecords()
        {return OfficialJson.Array(OfficialJson.Required(OfficialJson.Object(OfficialJson.Parse(Encoding.UTF8.GetBytes(OfficialCompanionLegacyData.Json),65536)),"roles")).Select(OfficialJson.Object).ToArray();}
        static Dictionary<string,object> Record(string id)
        {return Records.FirstOrDefault(x=>(string)x["id"]==id);}

        // Caller runs hashing and subsequent native decoding off the dispatcher.
        public static OfficialCompanionLegacySource FindBundled(string dataRoot,string characterId,CancellationToken cancel)
        {
            var record=Record(characterId);if(record==null)return null;
            try {
                string parent=CompanionPackStorage.AssetPath(Path.GetFullPath(dataRoot),"bundled-skins");
                if(!Directory.Exists(parent))return null;
                string name=characterId+"-"+((string)record["cache_version"]).Substring(0,24);
                string first=CompanionPackStorage.AssetPath(parent,name);
                var found=Match(first,characterId,cancel,true);if(found!=null)return found;
                int inspected=0;
                foreach(string path in Directory.EnumerateDirectories(parent,name+"-repair-*")) {
                    cancel.ThrowIfCancellationRequested();if(++inspected>32)break;
                    if(!Regex.IsMatch(Path.GetFileName(path),"\\A"+Regex.Escape(name)+"-repair-[a-f0-9]{32}\\z"))continue;
                    found=Match(path,characterId,cancel,true);if(found!=null)return found;
                }
            } catch(Exception error) {if(!Expected(error))throw;}
            return null;
        }

        // Existing catalog-discovered imports only: no arbitrary filesystem scan.
        // Names/IDs alone are insufficient; all declared canonical pixels and
        // profile/timing semantics must match one explicit supported snapshot.
        public static OfficialCompanionLegacySource MatchImport(string directory,string characterId,CancellationToken cancel)
        {return Match(directory,characterId,cancel,false);}

        static OfficialCompanionLegacySource Match(string directory,string id,CancellationToken cancel,bool exactManifest)
        {
            cancel.ThrowIfCancellationRequested();var record=Record(id);if(record==null)return null;
            try {
                string root=Path.GetFullPath(directory),manifestPath=CompanionPackStorage.AssetPath(root,"pet.json");
                if(!File.Exists(manifestPath))return null;
                var expected=OfficialJson.Object(record["manifest"]);
                var actual=OfficialJson.File(manifestPath,65536);
                bool stripped=!exactManifest&&OptionalProfile.All(key=>!actual.ContainsKey(key)||actual[key]==null);
                if(!EquivalentManifest(actual,expected,stripped))return null;
                var names=References(actual);
                if(exactManifest)names.Add("pet.json");
                var files=OfficialJson.Array(record["files"]).Select(OfficialJson.Object).ToDictionary(x=>(string)x["path"],StringComparer.Ordinal);
                long total=0;
                foreach(string name in names) {
                    cancel.ThrowIfCancellationRequested();Dictionary<string,object> entry;
                    if(!files.TryGetValue(name,out entry))return null;
                    long size=Convert.ToInt64(entry["size"],CultureInfo.InvariantCulture);
                    total=checked(total+size);if(size<1||size>25L*1024*1024||total>256L*1024*1024)return null;
                    string path=CompanionPackStorage.AssetPath(root,name);
                    using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())
                        if(stream.Length!=size||Hex(sha.ComputeHash(stream))!=(string)entry["sha256"])return null;
                }
                return new OfficialCompanionLegacySource(id,root,(string)record["cache_version"]);
            } catch(Exception error) {if(!Expected(error))throw;return null;}
        }
        static bool EquivalentManifest(Dictionary<string,object> actual,Dictionary<string,object> expected,bool stripped)
        {
            var first=new Dictionary<string,object>(actual,StringComparer.Ordinal);
            var second=new Dictionary<string,object>(expected,StringComparer.Ordinal);
            // The historical DataContract serializer emits the unused v1 atlas
            // as null. It cannot redirect a recognized v2 manifest to other art.
            object atlas;if(first.TryGetValue("atlas",out atlas)&&atlas==null)first.Remove("atlas");
            if(stripped)foreach(string key in OptionalProfile){first.Remove(key);second.Remove(key);}
            else foreach(string key in OptionalProfile)if(first.ContainsKey(key)&&first[key]==null&&!second.ContainsKey(key))first.Remove(key);
            return Equivalent(first,second);
        }
        static bool Equivalent(object actual,object expected)
        {
            if(actual==null||expected==null)return actual==expected;
            var left=actual as Dictionary<string,object>;var right=expected as Dictionary<string,object>;
            if(left!=null||right!=null) {
                if(left==null||right==null)return false;
                // A newly serialized zero hold has exactly the old completion
                // semantics; nonzero changes must match the trusted snapshot.
                if(left.ContainsKey("hold_last_seconds")&&!right.ContainsKey("hold_last_seconds")&&Number(left["hold_last_seconds"])==0) {
                    left=new Dictionary<string,object>(left,StringComparer.Ordinal);left.Remove("hold_last_seconds");
                }
                return left.Count==right.Count&&left.All(x=>right.ContainsKey(x.Key)&&Equivalent(x.Value,right[x.Key]));
            }
            var a=actual as object[];var b=expected as object[];
            if(a!=null||b!=null)return a!=null&&b!=null&&a.Length==b.Length&&a.Zip(b,Equivalent).All(x=>x);
            if(IsNumber(actual)&&IsNumber(expected))return Number(actual)==Number(expected);
            return actual.GetType()==expected.GetType()&&actual.Equals(expected);
        }
        static bool IsNumber(object value){return value is int||value is long||value is double||value is decimal;}
        static double Number(object value){return IsNumber(value)?Convert.ToDouble(value,CultureInfo.InvariantCulture):Double.NaN;}
        static HashSet<string> References(Dictionary<string,object> manifest)
        {
            var result=new HashSet<string>(StringComparer.Ordinal){(string)manifest["preview"],(string)OfficialJson.Object(manifest["gaze"])["file"]};
            foreach(string group in new[]{"animations","extra_animations","alternate_animations"}) {
                object raw;if(manifest.TryGetValue(group,out raw)&&raw!=null)
                    foreach(var item in OfficialJson.Object(raw).Values)result.Add((string)OfficialJson.Object(item)["file"]);
            }
            object avatar;if(manifest.TryGetValue("avatar",out avatar)&&avatar!=null)result.Add((string)avatar);
            return result;
        }
        static string Hex(byte[] value){return BitConverter.ToString(value).Replace("-","").ToLowerInvariant();}
        static bool Expected(Exception error)
        {return error is InvalidDataException||error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is System.Security.SecurityException;}
    }
}
