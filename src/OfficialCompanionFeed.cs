using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    internal sealed class OfficialCompanionFeed
    {
        internal sealed class Snapshot
        {
            internal readonly Dictionary<string,OfficialCompanionAsset> Assets;
            internal Snapshot(Dictionary<string,OfficialCompanionAsset> assets){Assets=new Dictionary<string,OfficialCompanionAsset>(assets,StringComparer.Ordinal);}
        }
        sealed class Release
        {
            internal string Tag;internal ReleaseVersion Version;internal Dictionary<string,object>[] Assets;
        }
        readonly GitHubUpdates transport;readonly ReleaseVersion appVersion;readonly UpdateChannel channel;readonly string temporaryRoot;
        internal OfficialCompanionFeed(GitHubUpdates transport,string appVersion,UpdateChannel channel,string temporaryRoot)
        {this.transport=transport;this.appVersion=ReleaseVersion.Parse(appVersion);this.channel=channel;this.temporaryRoot=temporaryRoot;}
        internal async Task<Snapshot> RefreshAsync(OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                bounded.CancelAfter(TimeSpan.FromSeconds(90));var token=bounded.Token;string stage=null;
                try {
                    var releases=new List<Release>();var tags=new HashSet<string>(StringComparer.Ordinal);
                    for(int page=1;page<=3;page++) {
                        token.ThrowIfCancellationRequested();Report(progress,page,11);
                        var raw=await transport.OfficialReleasePageAsync(page,routing,token).ConfigureAwait(false);
                        if(raw.Length>20)throw new InvalidDataException(TextResources.Get("officialCompanionFeed.refreshAsync.text"));
                        foreach(object item in raw) {
                            Release release=ReadRelease(item);if(release!=null&&tags.Add(release.Tag))releases.Add(release);
                        }
                        if(raw.Length<20)break;
                    }
                    var selected=new Dictionary<string,OfficialCompanionAsset>(StringComparer.Ordinal);int indexes=0;
                    foreach(var release in releases.OrderByDescending(x=>x.Version)) {
                        if(indexes==8||selected.Count==5)break;
                        string name="DeskPet-Companions-"+release.Version.Canonical+".json";
                        var index=release.Assets.Where(a=>StringValue(a,"name")==name).ToArray();if(index.Length!=1)continue;
                        string hash;long size;if(!AssetIdentity(release,index[0],name,1024*1024,out size,out hash))continue;
                        indexes++;Report(progress,3+indexes,11);
                        try {
                            if(stage==null){OfficialCompanionStorage.SafeDirectory(temporaryRoot);Directory.CreateDirectory(temporaryRoot);stage=Path.Combine(temporaryRoot,".metadata-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);}
                            string path=await transport.OfficialAssetAsync(OfficialCompanionRules.AssetUrl(release.Tag,name),name,size,hash,1024*1024,stage,routing,null,null,token).ConfigureAwait(false);
                            Dictionary<string,OfficialCompanionAsset> candidates;
                            try{candidates=ReadIndex(OfficialJson.File(path,1024*1024),release);}finally{File.Delete(path);}
                            foreach(string id in OfficialCompanionRules.Ids)if(!selected.ContainsKey(id)&&ReleaseVersion.Parse(candidates[id].MinimumAppVersion).CompareTo(appVersion)<=0)selected.Add(id,candidates[id]);
                        }catch(OperationCanceledException){throw;}
                        catch(Exception error){if(!Expected(error))throw;}
                    }
                    if(selected.Count==0)throw new ApiException(TextResources.Get("officialCompanionFeed.refreshAsync.text.alternativeA"));
                    return new Snapshot(selected);
                }catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw new OperationCanceledException(cancel);throw new ApiException(TextResources.Get("officialCompanionFeed.refreshAsync.text.alternativeB"));}
                finally{if(stage!=null)OfficialCompanionStorage.DeleteOwned(stage);}
            }
        }
        static void Report(IProgress<OfficialCompanionProgress> progress,int count,int total){if(progress!=null)progress.Report(new OfficialCompanionProgress("","metadata","",count,total,0));}
        static bool Expected(Exception error){return error is IOException||error is InvalidDataException||error is ApiException||error is ArgumentException||error is InvalidOperationException||error is OverflowException||error is UnauthorizedAccessException;}
        Release ReadRelease(object raw)
        {
            try {
                var value=OfficialJson.Object(raw);if(OfficialJson.Bool(value,"draft"))return null;
                string tag=OfficialJson.Text(value,"tag_name",90),text;
                if(tag.StartsWith("companions-v",StringComparison.Ordinal))text=tag.Substring(12);else if(tag.StartsWith("v",StringComparison.Ordinal))text=tag.Substring(1);else return null;
                ReleaseVersion version;if(!ReleaseVersion.TryParse(text,out version)||version.Canonical!=text||version.IsPreview!=OfficialJson.Bool(value,"prerelease")||(channel==UpdateChannel.Stable&&version.IsPreview))return null;
                if(OfficialJson.Text(value,"html_url",250)!="https://github.com/"+OfficialCompanionRules.Repository+"/releases/tag/"+tag)return null;
                return new Release{Tag=tag,Version=version,Assets=OfficialJson.Array(OfficialJson.Required(value,"assets")).Select(OfficialJson.Object).ToArray()};
            }catch(Exception error){if(!Expected(error))throw;return null;}
        }
        static string StringValue(Dictionary<string,object> value,string key){object raw;return value.TryGetValue(key,out raw)?raw as string:null;}
        static bool AssetIdentity(Release release,Dictionary<string,object> asset,string name,long cap,out long size,out string hash)
        {
            size=0;hash=null;
            try {
                size=OfficialJson.Integer(asset,"size");string digest=OfficialJson.Text(asset,"digest",71);
                if(size<=0||size>cap||!Regex.IsMatch(digest,@"\Asha256:[a-fA-F0-9]{64}\z")||OfficialJson.Text(asset,"browser_download_url",350)!=OfficialCompanionRules.AssetUrl(release.Tag,name))return false;
                hash=digest.Substring(7).ToLowerInvariant();return true;
            }catch(Exception error){if(!Expected(error))throw;return false;}
        }
        static Dictionary<string,OfficialCompanionAsset> ReadIndex(Dictionary<string,object> value,Release release)
        {
            if(OfficialJson.Integer(value,"schema_version")!=1)throw new InvalidDataException(TextResources.Get("officialCompanionFeed.readIndex.text"));
            var entries=OfficialJson.Array(OfficialJson.Required(value,"companions"));if(entries.Length!=5)throw new InvalidDataException(TextResources.Get("officialCompanionFeed.readIndex.text.alternativeA"));
            var result=new Dictionary<string,OfficialCompanionAsset>(StringComparer.Ordinal);
            foreach(var raw in entries) {
                var item=OfficialJson.Object(raw);string id=OfficialJson.Text(item,"id",48);OfficialCompanionRules.Id(id);
                string name=OfficialJson.Text(item,"name",80),version=OfficialCompanionRules.Version(OfficialJson.Text(item,"version",64)),minimum=OfficialCompanionRules.Version(OfficialJson.Text(item,"min_app_version",64));
                string filename=OfficialJson.Text(item,"asset_name",150),hash=OfficialJson.Text(item,"sha256",64);long size=OfficialJson.Integer(item,"size");
                if(filename!="DeskPet-Companion-"+id+"-"+version+".zip"||!OfficialCompanionRules.Hash(hash)||size<=0||size>OfficialCompanionRules.MaximumPackage)throw new InvalidDataException(TextResources.Get("officialCompanionFeed.readIndex.text.alternativeB"));
                var matching=release.Assets.Where(a=>StringValue(a,"name")==filename).ToArray();long actualSize;string actualHash;
                if(matching.Length!=1||!AssetIdentity(release,matching[0],filename,OfficialCompanionRules.MaximumPackage,out actualSize,out actualHash)||size!=actualSize||hash!=actualHash)throw new InvalidDataException(TextResources.Get("officialCompanionFeed.readIndex.text.alternativeC"));
                var outfits=new List<OfficialCompanionOutfit>();var seen=new HashSet<string>(StringComparer.Ordinal);
                var list=OfficialJson.Array(OfficialJson.Required(item,"outfits"));if(list.Length>32)throw new InvalidDataException(TextResources.Get("officialCompanionFeed.readIndex.text.alternativeD"));
                foreach(var entry in list){var outfit=OfficialJson.Object(entry);string outfitId=OfficialJson.Text(outfit,"outfit_id",64);if(!Regex.IsMatch(outfitId,@"\A[a-z0-9][a-z0-9-]{0,63}\z")||!seen.Add(outfitId))throw new InvalidDataException(TextResources.Get("officialCompanionFeed.readIndex.text.alternativeE"));outfits.Add(new OfficialCompanionOutfit(outfitId,OfficialCompanionRules.Version(OfficialJson.Text(outfit,"version",64))));}
                result.Add(id,new OfficialCompanionAsset(id,name,version,minimum,release.Tag,filename,size,hash,outfits));
            }
            return result;
        }
    }
}
