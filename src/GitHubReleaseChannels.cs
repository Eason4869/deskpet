using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class GitHubUpdates
    {
        public async Task<UpdateRelease> CheckAsync(string repository,bool useProxy,string proxyAddress,bool portable,UpdateChannel channel,CancellationToken cancel)
        {
            if(channel!=UpdateChannel.Stable&&channel!=UpdateChannel.Preview)throw new ArgumentException(TextResources.Get("gitHubReleaseChannels.checkAsync.text"));
            string repo=Repository(repository);
            using(var proxy=useProxy?Create(true,proxyAddress):null)
            using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                bounded.CancelAfter(TimeSpan.FromSeconds(30));var token=bounded.Token;var client=useProxy?proxy:direct;
                try {
                    Dictionary<string,object> best=null;ReleaseVersion bestVersion=null;
                    for(int page=1;page<=10;page++) {
                        bool next;var raw=await ReadReleaseJson(client,"repos/"+repo+"/releases?per_page=100&page="+page,token).ConfigureAwait(false);
                        var array=raw.Value as object[];if(array==null)throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeA"));next=raw.HasNext;
                        foreach(var item in array) {
                            var release=item as Dictionary<string,object>;if(release==null)continue;
                            object draft,preview,tag;if(!release.TryGetValue("draft",out draft)||!(draft is bool)||(bool)draft||!release.TryGetValue("prerelease",out preview)||!(preview is bool)||!release.TryGetValue("tag_name",out tag))continue;
                            ReleaseVersion version;if(!(tag is string)||!((string)tag).StartsWith("v",StringComparison.Ordinal)||!ReleaseVersion.TryParse((string)tag,out version)||version.IsPreview!=(bool)preview)continue;
                            if(channel==UpdateChannel.Stable&&version.IsPreview)continue;
                            if(bestVersion==null||version.CompareTo(bestVersion)>0){best=release;bestVersion=version;}
                        }
                        if(!next)break;if(page==10)throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeB"));
                    }
                    if(best==null)throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeC"));
                    string tagName=(string)best["tag_name"],pageUrl="https://github.com/"+repo+"/releases/tag/"+tagName;
                    object actualPage;if(!best.TryGetValue("html_url",out actualPage)||(string)actualPage!=pageUrl)throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeD"));
                    var result=new UpdateRelease{VersionTag=tagName,PageUrl=pageUrl,TargetAssemblyVersion=bestVersion.AssemblyBase.ToString()};
                    object assets;int score=-1;if(best.TryGetValue("assets",out assets)&&assets is object[])foreach(var item in (object[])assets) {
                        var asset=item as Dictionary<string,object>;if(asset==null)continue;object nameValue,urlValue,digest,sizeValue;
                        if(!asset.TryGetValue("name",out nameValue)||!(nameValue is string)||!ReleaseInfo.WindowsUpdateAsset((string)nameValue,bestVersion.Canonical,portable))continue;
                        if(!asset.TryGetValue("browser_download_url",out urlValue)||!asset.TryGetValue("digest",out digest)||!asset.TryGetValue("size",out sizeValue))continue;
                        string name=(string)nameValue;long size;string url=urlValue as string;
                        if(url!="https://github.com/"+repo+"/releases/download/"+tagName+"/"+name||!SafeAsset(url)||!Int64.TryParse(Convert.ToString(sizeValue),out size)||size<=0||size>300L*1024*1024||!Regex.IsMatch(digest as string??"",@"\Asha256:[a-fA-F0-9]{64}\z"))continue;
                        int candidate=Score(name);if(candidate<=score)continue;score=candidate;result.AssetName=name;result.AssetUrl=url;result.Digest=(string)digest;result.Size=size;
                    }
                    if(result.AssetName==null)throw new ApiException(TextResources.Format("gitHubReleaseChannels.checkAsync.template",tagName));
                    var reference=(await ReadReleaseJson(client,"repos/"+repo+"/git/ref/tags/"+Uri.EscapeDataString(tagName),token).ConfigureAwait(false)).Value as Dictionary<string,object>;
                    var seen=new HashSet<string>();
                    for(int depth=0;depth<=4;depth++) {
                        object target;if(reference==null||!reference.TryGetValue("object",out target))throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeE"));
                        var obj=target as Dictionary<string,object>;object shaValue,typeValue;
                        if(obj==null||!obj.TryGetValue("sha",out shaValue)||!obj.TryGetValue("type",out typeValue)||!Regex.IsMatch(shaValue as string??"",@"\A[a-f0-9]{40}\z"))throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeF"));
                        string sha=(string)shaValue;
                        if((string)typeValue=="commit"){result.CommitSha=sha;return result;}
                        if((string)typeValue!="tag"||!seen.Add(sha)||depth==4)throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeG"));
                        reference=(await ReadReleaseJson(client,"repos/"+repo+"/git/tags/"+sha,token).ConfigureAwait(false)).Value as Dictionary<string,object>;
                    }
                    throw new ApiException(TextResources.Get("gitHubReleaseChannels.checkAsync.text.alternativeH"));
                }catch(Exception error){throw SafeError(error,cancel,token,TextResources.Get("gitHubReleaseChannels.checkAsync.safeError"));}
            }
        }
        sealed class ReleaseJson {public object Value;public bool HasNext;}
        async Task<ReleaseJson> ReadReleaseJson(HttpClient client,string path,CancellationToken token)
        {
            using(var request=new HttpRequestMessage(HttpMethod.Get,new Uri(apiRoot,path))) {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                using(token.Register(response.Dispose)) {
                    if(response.StatusCode==HttpStatusCode.Forbidden||(int)response.StatusCode==429)throw new ApiException(TextResources.Get("gitHubReleaseChannels.readReleaseJson.text"));
                    if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Format("gitHubReleaseChannels.readReleaseJson.template",(int)response.StatusCode));
                    using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var buffer=new MemoryStream()) {
                        var block=new byte[8192];int count;
                        while((count=await stream.ReadAsync(block,0,block.Length,token).ConfigureAwait(false))>0){if(buffer.Length+count>4*1024*1024)throw new ApiException(TextResources.Get("gitHubReleaseChannels.readReleaseJson.text.alternativeA"));buffer.Write(block,0,count);}
                        IEnumerable<string> links;bool next=false;if(response.Headers.TryGetValues("Link",out links))foreach(string link in links)if(link.Contains("rel=\"next\""))next=true;
                        return new ReleaseJson{Value=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.DeserializeObject(Encoding.UTF8.GetString(buffer.ToArray())),HasNext=next};
                    }
                }
            }
        }
    }
}
