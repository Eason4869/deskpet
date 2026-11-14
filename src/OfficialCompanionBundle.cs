using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Dafeiyu
{
    // Immutable application-owned source. Each read opens the seekable assembly ZIP;
    // only the requested entry is inflated, verified and passed to the decoder.
    public sealed class OfficialCompanionBundle
    {
        const string Prefix="Dafeiyu.BundledEbao.";
        static readonly Lazy<OfficialCompanionBundle> bundled=new Lazy<OfficialCompanionBundle>(Load);
        public static OfficialCompanionBundle Ebao {get{return bundled.Value;}}
        readonly Dictionary<string,Entry> files;
        public string Version {get;private set;}
        public string MinimumAppVersion {get;private set;}
        public string PayloadSha256 {get;private set;}
        public string ZipSha256 {get;private set;}
        public string CanonicalZipSha256 {get;private set;}
        public bool HasCompleteCoverage {get;private set;}
        public string Generation {get{return "bundled:e-bao:"+PayloadSha256+":"+(HasCompleteCoverage?"complete":"defaults")+":"+ZipSha256;}}
        public IEnumerable<string> Paths {get{return files.Keys;}}
        #pragma warning disable 0649 // Deserialized bounded metadata
        [DataContract] sealed class Inventory {
            [DataMember(Name="schema_version")] public int Schema;
            [DataMember(Name="coverage")] public string Coverage;
            [DataMember(Name="canonical_zip_sha256")] public string CanonicalZip;
            [DataMember(Name="version")] public string Version;
            [DataMember(Name="min_app_version")] public string Minimum;
            [DataMember(Name="payload_sha256")] public string Payload;
            [DataMember(Name="zip_sha256")] public string Zip;
            [DataMember(Name="files")] public List<Entry> Files;
        }
        [DataContract] sealed class Entry {
            [DataMember(Name="path")] public string Path;
            [DataMember(Name="length")] public long Length;
            [DataMember(Name="sha256")] public string Hash;
        }
        #pragma warning restore 0649
        OfficialCompanionBundle(Inventory inventory) {
            ReleaseVersion version,minimum;
            if(!ReleaseVersion.TryParse(inventory.Version,out version)||!ReleaseVersion.TryParse(inventory.Minimum,out minimum)||minimum.CompareTo(ReleaseVersion.Parse(ReleaseInfo.DisplayVersion))>0||inventory.Files==null||inventory.Files.Count>1024)throw new InvalidDataException("Invalid bundled companion inventory.");
            // Older complete embedded sources remain readable; new build gates
            // require an explicit schema2 coverage declaration for derived inputs.
            if(inventory.Schema!=0 && inventory.Schema!=2 || inventory.Schema==2 && inventory.Coverage!="defaults" && inventory.Coverage!="complete")throw new InvalidDataException("Invalid bundled coverage.");
            HasCompleteCoverage=inventory.Schema==0 || inventory.Coverage=="complete";
            Version=inventory.Version;MinimumAppVersion=inventory.Minimum;PayloadSha256=inventory.Payload;ZipSha256=inventory.Zip;CanonicalZipSha256=inventory.Schema==0?inventory.Zip:inventory.CanonicalZip;
            if(!OfficialCompanionRules.Hash(PayloadSha256)||!OfficialCompanionRules.Hash(ZipSha256)||!OfficialCompanionRules.Hash(CanonicalZipSha256))throw new InvalidDataException("Invalid bundled source identity.");
            files=inventory.Files.ToDictionary(e=>e.Path,StringComparer.Ordinal);
            foreach(var entry in files.Values){OfficialCompanionStorage.ValidateRelativePath(entry.Path);if(entry.Length<0||entry.Length>25*1024*1024||!OfficialCompanionRules.Hash(entry.Hash))throw new InvalidDataException("Invalid bundled companion entry.");}
            if(HasCompleteCoverage && !files.ContainsKey("holidays/catalog.json"))throw new InvalidDataException("Complete coverage requires a wardrobe catalog.");
            if(!HasCompleteCoverage){
                var manifest=OfficialJson.Object(OfficialJson.Parse(ReadVerifiedBytes("pet.json"),65536));
                var required=new HashSet<string>(StringComparer.Ordinal){"pet.json","companions.json",OfficialJson.Text(manifest,"preview",220),OfficialJson.Text(manifest,"avatar",220),OfficialJson.Text(OfficialJson.Object(OfficialJson.Required(manifest,"gaze")),"file",220)};
                foreach(string group in new[]{"animations","extra_animations","alternate_animations"})foreach(var clip in OfficialJson.Object(OfficialJson.Required(manifest,group)).Values)required.Add(OfficialJson.Text(OfficialJson.Object(clip),"file",220));
                if(!required.SetEquals(files.Keys)||required.Any(path=>path.StartsWith("holidays/",StringComparison.Ordinal)))throw new InvalidDataException("Incomplete default manifest coverage.");
            }
        }
        static OfficialCompanionBundle Load() {
            using(var stream=typeof(OfficialCompanionBundle).Assembly.GetManifestResourceStream(Prefix+"inventory.json")){
                if(stream==null)return null;
                if(stream.Length>1024*1024)throw new InvalidDataException("Oversized bundled inventory.");
                return new OfficialCompanionBundle((Inventory)new DataContractJsonSerializer(typeof(Inventory)).ReadObject(stream));
            }
        }
        readonly object auditGate=new object();internal bool CompletelyVerified {get;private set;}
        bool declaredVerified;
        internal void VerifyDeclaredContents(System.Threading.CancellationToken cancel)
        {
            lock(auditGate){if(declaredVerified)return;foreach(string path in Paths){cancel.ThrowIfCancellationRequested();ReadVerifiedBytes(path);}cancel.ThrowIfCancellationRequested();declaredVerified=true;}
        }
        internal void VerifyComplete(System.Threading.CancellationToken cancel)
        {
            if(!HasCompleteCoverage)throw new InvalidDataException("Default-only source is not a complete replacement.");
            lock(auditGate){if(CompletelyVerified)return;VerifyDeclaredContents(cancel);var catalog=CreateHolidayCatalog();catalog.WithCharacter("e-bao",catalog);cancel.ThrowIfCancellationRequested();CompletelyVerified=true;}
        }
        public bool PrefersBundle(string installedVersion) {ReleaseVersion installed;return HasCompleteCoverage&&(!ReleaseVersion.TryParse(installedVersion,out installed)||installed.CompareTo(ReleaseVersion.Parse(Version))<0);}
        public Stream Open(string path) {return new MemoryStream(ReadVerifiedBytes(path),false);}
        public byte[] ReadVerifiedBytes(string path) {
            Entry expected;if(!files.TryGetValue(path,out expected))throw new InvalidDataException("Unknown bundled companion entry.");
            using(var resource=typeof(OfficialCompanionBundle).Assembly.GetManifestResourceStream(Prefix+"zip"))
            using(var zip=new ZipArchive(resource,ZipArchiveMode.Read,false)) {
                var entry=zip.GetEntry(path);if(entry==null||entry.Length!=expected.Length)throw new InvalidDataException("Bundled companion length mismatch.");
                var bytes=new byte[(int)expected.Length];using(var input=entry.Open()){int offset=0,count;while(offset<bytes.Length&&(count=input.Read(bytes,offset,bytes.Length-offset))>0)offset+=count;if(offset!=bytes.Length||input.ReadByte()!=-1)throw new InvalidDataException("Truncated bundled companion.");}
                if(OfficialCompanionSnapshot.Digest(bytes)!=expected.Hash)throw new InvalidDataException("Bundled companion hash mismatch.");return bytes;
            }
        }
        // A v1.0.3 user may own the original embedded Halloween without any
        // managed installation. Keep that entire legacy source, never combine
        // its wardrobe with the new default-only generation.
        public static HolidayCatalog RetainedLegacyEbaoWardrobe(string dataDirectory)
        {
            var state=new HolidayStateStore(Path.Combine(dataDirectory,"holiday-state.json")).Load();
            var owned=state.ForCharacter("e-bao").UnlockedOutfits;
            if(owned.Count==0)return null;
            var legacy=new HolidayCatalog();
            if(!String.IsNullOrEmpty(legacy.Warning)||!legacy.Outfits.Any(outfit=>outfit.CharacterId=="e-bao"&&outfit.Ready&&owned.Contains(outfit.OutfitId)))return null;
            return new HolidayCatalog(legacy.Outfits.Where(outfit=>outfit.CharacterId=="e-bao"));
        }
        public HolidayCatalog CreateHolidayCatalog() {return HasCompleteCoverage?new HolidayCatalog(path=>Open("holidays/"+path),Generation,false):new HolidayCatalog(new OutfitDefinition[0]);}
    }
}
