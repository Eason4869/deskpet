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
        public string Generation {get{return "bundled:e-bao:"+PayloadSha256;}}
        public IEnumerable<string> Paths {get{return files.Keys;}}
        #pragma warning disable 0649 // Deserialized bounded metadata
        [DataContract] sealed class Inventory {
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
            Version=inventory.Version;MinimumAppVersion=inventory.Minimum;PayloadSha256=inventory.Payload;ZipSha256=inventory.Zip;
            files=inventory.Files.ToDictionary(e=>e.Path,StringComparer.Ordinal);
            if(files.Values.Any(e=>e.Length<0||e.Length>25*1024*1024||e.Hash==null||e.Hash.Length!=64))throw new InvalidDataException("Invalid bundled companion entry.");
        }
        static OfficialCompanionBundle Load() {
            using(var stream=typeof(OfficialCompanionBundle).Assembly.GetManifestResourceStream(Prefix+"inventory.json")){
                if(stream==null)return null;
                if(stream.Length>1024*1024)throw new InvalidDataException("Oversized bundled inventory.");
                return new OfficialCompanionBundle((Inventory)new DataContractJsonSerializer(typeof(Inventory)).ReadObject(stream));
            }
        }
        readonly object auditGate=new object();internal bool CompletelyVerified {get;private set;}
        internal void VerifyComplete(System.Threading.CancellationToken cancel)
        {
            lock(auditGate){
                if(CompletelyVerified)return;
                foreach(string path in Paths){cancel.ThrowIfCancellationRequested();ReadVerifiedBytes(path);}
                var catalog=CreateHolidayCatalog();catalog.WithCharacter("e-bao",catalog);
                cancel.ThrowIfCancellationRequested();CompletelyVerified=true;
            }
        }
        public bool PrefersBundle(string installedVersion) {ReleaseVersion installed;return !ReleaseVersion.TryParse(installedVersion,out installed)||installed.CompareTo(ReleaseVersion.Parse(Version))<0;}
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
        public HolidayCatalog CreateHolidayCatalog() {return new HolidayCatalog(path=>Open("holidays/"+path),Generation,false);}
    }
}
