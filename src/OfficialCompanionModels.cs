using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed class OfficialDownloadRouting
    {
        public bool UseMirror {get;private set;} public string MirrorAddress {get;private set;}
        public bool UseProxy {get;private set;} public string ProxyAddress {get;private set;}
        public OfficialDownloadRouting(bool useMirror,string mirrorAddress,bool useProxy,string proxyAddress)
        {UseMirror=useMirror;MirrorAddress=useMirror?GitHubUpdates.MirrorPrefix(mirrorAddress):(mirrorAddress??"");UseProxy=useProxy;ProxyAddress=proxyAddress??"";}
        internal string Key {get{return (UseMirror?"1":"0")+":"+MirrorAddress.Length+":"+MirrorAddress+":"+(UseProxy?"1":"0")+":"+ProxyAddress;}}
    }
    public sealed class OfficialCompanionOutfit
    {
        public string OutfitId {get;private set;} public string Version {get;private set;}
        internal OfficialCompanionOutfit(string id,string version){OutfitId=id;Version=version;}
    }
    public sealed class OfficialCompanionAsset
    {
        public string Id {get;private set;} public string Name {get;private set;} public string Version {get;private set;}
        public string MinimumAppVersion {get;private set;} public string AssetName {get;private set;} public string AssetUrl {get;private set;}
        public long Size {get;private set;} public string Sha256 {get;private set;} public IList<OfficialCompanionOutfit> Outfits {get;private set;}
        internal string Tag {get;private set;}
        internal OfficialCompanionAsset(string id,string name,string version,string minimum,string tag,string filename,long size,string hash,IEnumerable<OfficialCompanionOutfit> outfits)
        {Id=id;Name=name;Version=version;MinimumAppVersion=minimum;Tag=tag;AssetName=filename;AssetUrl=OfficialCompanionRules.AssetUrl(tag,filename);Size=size;Sha256=hash;Outfits=new ReadOnlyCollection<OfficialCompanionOutfit>(outfits.Select(x=>new OfficialCompanionOutfit(x.OutfitId,x.Version)).ToList());}
    }
    public sealed class OfficialCompanionInstallation
    {
        public OfficialCompanionAsset Asset {get;private set;} public string RootDirectory {get;private set;}
        public string PetManifestPath {get;private set;} public string HolidayRootDirectory {get;private set;}
        internal OfficialCompanionInstallation(OfficialCompanionAsset asset,string root){Asset=asset;RootDirectory=root;PetManifestPath=Path.Combine(root,"pet.json");HolidayRootDirectory=Path.Combine(root,"holidays");}
    }
    public enum OfficialCompanionCheckMode { Cached, Force, AfterApplicationUpdate }
    public sealed class OfficialCompanionResult
    {
        public OfficialCompanionInstallation Installation {get;private set;} public bool UsedOfflineFallback {get;private set;}
        public bool MetadataChecked {get;private set;} public bool InstalledNow {get;private set;} public string Warning {get;private set;}
        internal OfficialCompanionResult(OfficialCompanionInstallation installation,bool fallback,bool metadata,bool installed,string warning)
        {Installation=installation;UsedOfflineFallback=fallback;MetadataChecked=metadata;InstalledNow=installed;Warning=warning;}
    }
    public sealed class OfficialCompanionProgress
    {
        public string CharacterId {get;private set;} public string Stage {get;private set;} public string Route {get;private set;}
        public long CompletedBytes {get;private set;} public long TotalBytes {get;private set;} public double Percent {get;private set;}
        public bool RequiresUpdateNotice {get{return Stage=="download"||Stage=="validate"||Stage=="install"&&Percent<100;}}
        internal OfficialCompanionProgress(string id,string stage,string route,long completed,long total,double percent)
        {CharacterId=id;Stage=stage;Route=route??"";CompletedBytes=completed;TotalBytes=total;Percent=Math.Max(0,Math.Min(100,percent));}
    }
    internal sealed class OfficialProgress<T> : IProgress<T>
    {
        readonly Action<T> action;internal OfficialProgress(Action<T> action){this.action=action;}public void Report(T value){try{action(value);}catch{} }
    }
    internal static class OfficialCompanionRules
    {
        internal static readonly string[] Ids={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};
        internal const long MaximumPackage=256L*1024*1024;
        internal static string Repository {get{return GitHubUpdates.Repository(ReleaseInfo.RepositoryUrl);}}
        internal static string AssetUrl(string tag,string filename){return "https://github.com/"+Repository+"/releases/download/"+tag+"/"+filename;}
        internal static void Id(string id){if(!Ids.Contains(id))throw new ArgumentException("未知官方角色。","id");}
        internal static bool Hash(string hash){return hash!=null&&Regex.IsMatch(hash,@"\A[a-f0-9]{64}\z");}
        internal static string Version(string text){ReleaseVersion value;if(!ReleaseVersion.TryParse(text,out value)||value.Canonical!=text)throw new InvalidDataException("角色包版本无效。");return text;}
        internal static string SafeMessage {get{return "官方角色包操作未完成，请检查网络、兼容版本及存储权限后重试。";}}
    }
    internal static class OfficialJson
    {
        internal static object Parse(byte[] bytes,int maximum){if(bytes.Length>maximum)throw new InvalidDataException("角色元数据过大。");return new JavaScriptSerializer{MaxJsonLength=maximum,RecursionLimit=40}.DeserializeObject(new UTF8Encoding(false,true).GetString(bytes));}
        internal static Dictionary<string,object> Object(object raw){var value=raw as Dictionary<string,object>;if(value==null)throw new InvalidDataException("角色元数据对象无效。");return value;}
        internal static object[] Array(object raw){var value=raw as object[];if(value==null)throw new InvalidDataException("角色元数据列表无效。");return value;}
        internal static object Required(Dictionary<string,object> value,string key){object raw;if(!value.TryGetValue(key,out raw))throw new InvalidDataException("角色元数据字段缺失。");return raw;}
        internal static string Text(Dictionary<string,object> value,string key,int max){var text=Required(value,key) as string;if(String.IsNullOrWhiteSpace(text)||text.Length>max||text.Any(c=>Char.IsControl(c)))throw new InvalidDataException("角色元数据文本无效。");return text;}
        internal static long Integer(Dictionary<string,object> value,string key){object raw=Required(value,key);if(!(raw is int || raw is long))throw new InvalidDataException("角色元数据数值无效。");return Convert.ToInt64(raw,CultureInfo.InvariantCulture);}
        internal static double Number(Dictionary<string,object> value,string key){object raw=Required(value,key);if(!(raw is int || raw is long || raw is double || raw is decimal))throw new InvalidDataException("角色元数据数值无效。");double n=Convert.ToDouble(raw,CultureInfo.InvariantCulture);if(Double.IsNaN(n)||Double.IsInfinity(n))throw new InvalidDataException("角色元数据数值无效。");return n;}
        internal static bool Bool(Dictionary<string,object> value,string key){object raw=Required(value,key);if(!(raw is bool))throw new InvalidDataException("角色元数据布尔值无效。");return (bool)raw;}
        internal static byte[] Bytes(object value){return new UTF8Encoding(false).GetBytes(new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Serialize(value));}
        internal static Dictionary<string,object> File(string path,int max){if(new FileInfo(path).Length>max)throw new InvalidDataException("角色元数据过大。");return Object(Parse(System.IO.File.ReadAllBytes(path),max));}
    }
}
