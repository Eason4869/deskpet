using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class UpdateRelease
    {
        public string VersionTag,PageUrl,AssetName,AssetUrl,Digest,CommitSha,TargetAssemblyVersion;public long Size;
        public bool IsNewerThan(Version installed){Version next;string tag=(VersionTag??"").TrimStart('v','V');return Version.TryParse(tag,out next) && NormalizeVersion(next)>NormalizeVersion(installed);}
        public bool IsNewerThan(string installed){ReleaseVersion next,current;return ReleaseVersion.TryParse(VersionTag,out next)&&ReleaseVersion.TryParse(installed,out current)&&next.CompareTo(current)>0;}
        static Version NormalizeVersion(Version value){return new Version(value.Major,value.Minor,Math.Max(0,value.Build),Math.Max(0,value.Revision));}
    }
    public interface IUpdateFeed : IDisposable
    {
        Task<UpdateRelease> CheckAsync(string repository,bool useProxy,string proxyAddress,bool preferPortable,CancellationToken cancel);
        Task<UpdateRelease> CheckAsync(string repository,bool useProxy,string proxyAddress,bool preferPortable,UpdateChannel channel,CancellationToken cancel);
        Task<string> DownloadAsync(UpdateRelease release,string folder,bool useProxy,string proxyAddress,IProgress<double> progress,CancellationToken cancel);
    }
    public interface IUpdateDownloadRouting
    {
        void ConfigureMirror(bool enabled,string address,IProgress<string> route);
    }
    public sealed partial class GitHubUpdates : IUpdateFeed,IUpdateDownloadRouting
    {
        readonly Uri apiRoot;readonly HttpClient direct;
        bool useMirror;string mirrorAddress;IProgress<string> route;
        public static string MirrorPrefix(string address)
        {
            string value=(address??"").Trim().TrimEnd('/');Uri uri;
            if(!Uri.TryCreate(value,UriKind.Absolute,out uri) || uri.Scheme!="https" || String.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0 || value.IndexOfAny(new[]{'\r','\n','\t',' '})>=0 || uri.AbsolutePath.IndexOf(":",StringComparison.Ordinal)>=0)
                throw new InvalidOperationException(TextResources.Get("gitHubUpdates.mirrorPrefix.text"));
            return value;
        }
        public void ConfigureMirror(bool enabled,string address,IProgress<string> reporter){string prefix=enabled?MirrorPrefix(address):null;lock(routingGate){mirrorAddress=prefix;useMirror=enabled;route=reporter;}}
        void Route(string text){if(route!=null)route.Report(text);}
        public GitHubUpdates() : this(new Uri("https://api.github.com/")){}
        public GitHubUpdates(Uri root)
        {
            if(root==null || !(root.Scheme=="https" || root.IsLoopback))throw new ArgumentException("Invalid API root.");
#if !PORTABLE
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
#endif
            apiRoot=root;direct=Create(false,null);
        }
        internal GitHubUpdates(Uri root,HttpMessageHandler handler):this(root){direct.Dispose();direct=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};direct.DefaultRequestHeaders.UserAgent.ParseAdd("DafeiyuDeskPet/"+ReleaseInfo.DisplayVersion);}
        static HttpClient Create(bool useProxy,string address){var client=useProxy?ProxyConfiguration.CreateHttpClient(address,true):new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=true}){Timeout=Timeout.InfiniteTimeSpan};client.DefaultRequestHeaders.UserAgent.ParseAdd("DafeiyuDeskPet/"+ReleaseInfo.DisplayVersion);return client;}
        public static string Repository(string address)
        {
            Uri uri;if(!Uri.TryCreate((address??"").Trim(),UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.Host!="github.com" || !uri.IsDefaultPort || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0)throw new ApiException(TextResources.Get("gitHubUpdates.repository.text"));
            string path=uri.AbsolutePath.Trim('/');if(path.EndsWith(".git",StringComparison.OrdinalIgnoreCase))path=path.Substring(0,path.Length-4);
            if(!Regex.IsMatch(path,@"^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$") || path.Equals("OWNER/REPO",StringComparison.OrdinalIgnoreCase))throw new ApiException(TextResources.Get("gitHubUpdates.repository.text.alternativeA"));return path;
        }
        public Task<UpdateRelease> CheckAsync(string repository,bool useProxy,CancellationToken cancel){return CheckAsync(repository,useProxy,ProxyConfiguration.DefaultAddress,cancel);}
        public Task<UpdateRelease> CheckAsync(string repository,bool useProxy,string proxyAddress,CancellationToken cancel){return CheckAsync(repository,useProxy,proxyAddress,false,cancel);}
        public async Task<UpdateRelease> CheckAsync(string repository,bool useProxy,string proxyAddress,bool preferPortable,CancellationToken cancel)
        {
            string repo=Repository(repository);
            using(var proxy=useProxy?Create(true,proxyAddress):null)
            using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel))using(var request=new HttpRequestMessage(HttpMethod.Get,new Uri(apiRoot,"repos/"+repo+"/releases/latest")))
            {
                bounded.CancelAfter(TimeSpan.FromSeconds(30));var token=bounded.Token;request.Headers.Accept.ParseAdd("application/vnd.github+json");
                try
                {
                    using(var response=await (useProxy?proxy:direct).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))using(token.Register(response.Dispose))
                    {
                        if(response.StatusCode==HttpStatusCode.NotFound)throw new ApiException(TextResources.Get("gitHubUpdates.checkAsync.text"));
                        if(response.StatusCode==HttpStatusCode.Forbidden || (int)response.StatusCode==429)throw new ApiException(TextResources.Get("gitHubUpdates.checkAsync.text.alternativeA"));
                        if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Format("gitHubUpdates.checkAsync.template",(int)response.StatusCode));
                        using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var memory=new MemoryStream())
                        {
                            byte[] block=new byte[8192];int count;while((count=await stream.ReadAsync(block,0,block.Length,token).ConfigureAwait(false))>0){if(memory.Length+count>1024*1024)throw new ApiException(TextResources.Get("gitHubUpdates.checkAsync.text.alternativeB"));memory.Write(block,0,count);}
                            var json=new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray())) as Dictionary<string,object>;if(json==null)throw new FormatException();
                            var release=new UpdateRelease{VersionTag=(string)json["tag_name"],PageUrl=(string)json["html_url"]};Uri page;if(!Uri.TryCreate(release.PageUrl,UriKind.Absolute,out page) || page.Host!="github.com" || page.Scheme!="https")throw new FormatException();int best=-1;
                            foreach(object raw in (object[])json["assets"])
                            {
                                var asset=(Dictionary<string,object>)raw;string name=(string)asset["name"];int score=Score(name);if(score<0)continue;if(preferPortable && Path.GetExtension(name).Equals(".zip",StringComparison.OrdinalIgnoreCase))score+=100;if(score<=best)continue;string url=(string)asset["browser_download_url"];if(!SafeAsset(url))continue;long size=Convert.ToInt64(asset["size"]);if(size<=0 || size>300L*1024*1024)continue;
                                best=score;release.AssetName=name;release.AssetUrl=url;release.Size=size;object digest;if(asset.TryGetValue("digest",out digest))release.Digest=digest as string;
                            }
                            if(String.IsNullOrWhiteSpace(release.VersionTag))throw new FormatException();return release;
                        }
                    }
                }
                catch(Exception e){throw SafeError(e,cancel,token,TextResources.Get("gitHubUpdates.checkAsync.safeError"));}
            }
        }
        static int Score(string name)
        {
            if(!ReleaseInfo.WindowsUpdateAsset(name))return -1;
            string lower=name.ToLowerInvariant();
            string ext=Path.GetExtension(lower);int score=ext==".msi"?40:ext==".exe"?30:ext==".zip"?10:-1;if(score<0)return -1;if(lower.Contains("setup"))score+=20;if(lower.StartsWith("deskpet-windows-setup",StringComparison.Ordinal))score+=25;return score;
        }
        bool SafeAsset(string address){Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri) || uri.UserInfo.Length>0 || uri.Fragment.Length>0)return false;if(apiRoot.IsLoopback && uri.IsLoopback && uri.Scheme=="http")return true;return uri.Scheme=="https" && uri.Host=="github.com" && uri.AbsolutePath.Contains("/releases/download/");}
        public Task<string> DownloadAsync(UpdateRelease release,string folder,bool useProxy,IProgress<double> progress,CancellationToken cancel){return DownloadAsync(release,folder,useProxy,ProxyConfiguration.DefaultAddress,progress,cancel);}
        public Task<string> DownloadAsync(UpdateRelease release,string folder,bool useProxy,string proxyAddress,IProgress<double> progress,CancellationToken cancel)
        {
            if(release==null || !SafeAsset(release.AssetUrl) || Score(release.AssetName)<0 || release.AssetName!=Path.GetFileName(release.AssetName) || release.AssetName.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || release.AssetName.Length>150)throw new ApiException(TextResources.Get("gitHubUpdates.downloadAsync.text"));
            OfficialDownloadRouting routing;IProgress<string> reporter;
            lock(routingGate){routing=new OfficialDownloadRouting(useMirror,mirrorAddress,useProxy,proxyAddress);reporter=route;}
            return TransferAssetAsync(release.AssetUrl,release.AssetName,release.Size,release.Digest,300L*1024*1024,folder,routing,progress,reporter,cancel);
        }
        public async Task TestMirrorAsync(string address,CancellationToken cancel)
        {
            MirrorPrefix(address);
            var release=await CheckAsync(ReleaseInfo.RepositoryUrl,false,ProxyConfiguration.DefaultAddress,false,cancel).ConfigureAwait(false);
            await TestMirrorAsync(address,release,cancel).ConfigureAwait(false);
        }
        internal async Task TestMirrorAsync(string address,UpdateRelease release,CancellationToken cancel)
        {
            string prefix=MirrorPrefix(address);
            if(release==null || !SafeAsset(release.AssetUrl))throw new ApiException(TextResources.Get("gitHubUpdates.testMirrorAsync.text"));
            using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel))using(var request=new HttpRequestMessage(HttpMethod.Get,prefix+"/"+release.AssetUrl))
            {
                bounded.CancelAfter(TimeSpan.FromSeconds(15));request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(0,1023);
                try{using(var response=await direct.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,bounded.Token).ConfigureAwait(false))using(bounded.Token.Register(response.Dispose))
                {
                    if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Format("gitHubUpdates.testMirrorAsync.template",(int)response.StatusCode));
                    if(response.Content.Headers.ContentType!=null && response.Content.Headers.ContentType.MediaType=="text/html")throw new ApiException(TextResources.Get("gitHubUpdates.testMirrorAsync.text.alternativeA"));
                    using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        byte[] sample=new byte[2];int count=0;while(count<sample.Length){int read=await stream.ReadAsync(sample,count,sample.Length-count,bounded.Token).ConfigureAwait(false);if(read==0)break;count+=read;}
                        string extension=Path.GetExtension(release.AssetName).ToLowerInvariant();bool binary=count==2 && (extension==".exe"?sample[0]==77 && sample[1]==90:extension==".zip"?sample[0]==80 && sample[1]==75:sample[0]==208 && sample[1]==207);
                        if(!binary)throw new ApiException(TextResources.Get("gitHubUpdates.testMirrorAsync.text.alternativeB"));
                    }
                }}catch(Exception e){throw SafeError(e,cancel,bounded.Token,TextResources.Get("gitHubUpdates.testMirrorAsync.safeError"));}
            }
        }
        static Exception SafeError(Exception e,CancellationToken cancel,CancellationToken timeout,string action){if(cancel.IsCancellationRequested)return new OperationCanceledException(cancel);if(timeout.IsCancellationRequested)return new ApiException(TextResources.Format("gitHubUpdates.safeError.template",action));if(e is ApiException)return e;return new ApiException(TextResources.Format("gitHubUpdates.safeError.template.alternativeA",action));}
        public void Dispose(){direct.Dispose();}
    }
}
