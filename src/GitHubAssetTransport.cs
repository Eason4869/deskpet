using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed partial class GitHubUpdates
    {
        readonly object routingGate=new object();
        internal Func<bool,string,HttpClient> AssetClientFactory=null;
        HttpClient AssetProxy(bool enabled,string address){return enabled?(AssetClientFactory==null?Create(true,address):AssetClientFactory(true,address)):null;}
        internal async Task<object[]> OfficialReleasePageAsync(int page,OfficialDownloadRouting routing,CancellationToken cancel)
        {
            using(var proxy=AssetProxy(routing.UseProxy,routing.ProxyAddress)) {
                var response=await ReadReleaseJson(routing.UseProxy?proxy:direct,"https://api.github.com/repos/"+OfficialCompanionRules.Repository+"/releases?per_page=20&page="+page,cancel).ConfigureAwait(false);
                return OfficialJson.Array(response.Value);
            }
        }
        internal Task<string> OfficialAssetAsync(string url,string name,long size,string hash,long maximum,string folder,OfficialDownloadRouting routing,IProgress<double> progress,IProgress<string> reporter,CancellationToken cancel,Action<OfficialCompanionRoute> routeChanged=null)
        {
            if(!TrustedOfficialAsset(url,name)||size<=0||size>maximum||maximum>(name.EndsWith(".json",StringComparison.Ordinal)?1024*1024:OfficialCompanionRules.MaximumPackage)||!OfficialCompanionRules.Hash(hash))throw new ApiException(TextResources.Get("gitHubAssetTransport.officialAssetAsync.text"));
            return TransferAssetAsync(url,name,size,"sha256:"+hash,maximum,folder,routing,progress,reporter,cancel,routeChanged);
        }
        static bool TrustedOfficialAsset(string url,string name)
        {
            if(String.IsNullOrEmpty(url)||String.IsNullOrEmpty(name)||name.Length>150)return false;
            string prefix="https://github.com/"+OfficialCompanionRules.Repository+"/releases/download/";if(!url.StartsWith(prefix,StringComparison.Ordinal))return false;
            string[] parts=url.Substring(prefix.Length).Split('/');if(parts.Length!=2||parts[1]!=name)return false;string tag=parts[0],text=tag.StartsWith("companions-v",StringComparison.Ordinal)?tag.Substring(12):tag.StartsWith("v",StringComparison.Ordinal)?tag.Substring(1):null;ReleaseVersion feed;
            if(!ReleaseVersion.TryParse(text,out feed)||feed.Canonical!=text)return false;
            if(name=="DeskPet-Companions-"+feed.Canonical+".json")return true;
            foreach(string id in OfficialCompanionRules.Ids){string lead="DeskPet-Companion-"+id+"-";if(name.StartsWith(lead,StringComparison.Ordinal)&&name.EndsWith(".zip",StringComparison.Ordinal)){string version=name.Substring(lead.Length,name.Length-lead.Length-4);ReleaseVersion parsed;if(ReleaseVersion.TryParse(version,out parsed)&&parsed.Canonical==version)return true;}}
            return false;
        }
        async Task<string> TransferAssetAsync(string url,string name,long size,string digest,long maximum,string folder,OfficialDownloadRouting routing,IProgress<double> progress,IProgress<string> reporter,CancellationToken cancel,Action<OfficialCompanionRoute> routeChanged=null)
        {
            if(routing.UseMirror) {
                if(routeChanged!=null)routeChanged(OfficialCompanionRoute.Mirror);
                if(!Regex.IsMatch(digest??"",@"\Asha256:[0-9a-fA-F]{64}\z"))throw new ApiException(TextResources.Get("gitHubAssetTransport.transferAssetAsync.text"));
                if(reporter!=null)reporter.Report(TextResources.Format("gitHubAssetTransport.transferAssetAsync.reporterReport",new Uri(routing.MirrorAddress).Host));
                try{return await TransferAttemptAsync(routing.MirrorAddress.TrimEnd('/')+"/"+url,name,size,digest,maximum,folder,false,null,progress,cancel,true).ConfigureAwait(false);}
                catch(OperationCanceledException){throw;}
                catch(ApiException){cancel.ThrowIfCancellationRequested();if(routeChanged!=null)routeChanged(OfficialCompanionRoute.Direct);if(reporter!=null)reporter.Report(TextResources.Get("gitHubAssetTransport.transferAssetAsync.reporterReport.alternativeA"));if(progress!=null)progress.Report(0);}
            } else {if(routeChanged!=null)routeChanged(routing.UseProxy?OfficialCompanionRoute.Proxy:OfficialCompanionRoute.Direct);if(reporter!=null)reporter.Report(routing.UseProxy?TextResources.Get("gitHubAssetTransport.transferAssetAsync.reporterReport.alternativeB"):TextResources.Get("gitHubAssetTransport.transferAssetAsync.reporterReport.alternativeC"));}
            return await TransferAttemptAsync(url,name,size,digest,maximum,folder,routing.UseMirror?false:routing.UseProxy,routing.ProxyAddress,progress,cancel,false).ConfigureAwait(false);
        }
        async Task<string> TransferAttemptAsync(string requestUrl,string name,long expected,string digest,long maximum,string folder,bool useProxy,string proxyAddress,IProgress<double> progress,CancellationToken cancel,bool mirrored)
        {
            cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(folder);
            string target=Path.Combine(folder,name);if(File.Exists(target))target=Path.Combine(folder,Path.GetFileNameWithoutExtension(name)+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+Path.GetExtension(name));
            string partial=target+"."+Guid.NewGuid().ToString("N")+".part";bool complete=false,owned=false;
            using(var proxy=AssetProxy(useProxy,proxyAddress))using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel))using(var idle=CancellationTokenSource.CreateLinkedTokenSource(bounded.Token)) {
                bounded.CancelAfter(TimeSpan.FromMinutes(10));var token=idle.Token;
                try {
                    using(var header=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        header.CancelAfter(TimeSpan.FromSeconds(mirrored?15:30));
                        using(var response=await (useProxy?proxy:direct).GetAsync(requestUrl,HttpCompletionOption.ResponseHeadersRead,header.Token).ConfigureAwait(false))using(token.Register(response.Dispose)) {
                            if(mirrored)idle.CancelAfter(TimeSpan.FromSeconds(30));
                            if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Format("gitHubAssetTransport.transferAttemptAsync.template",(int)response.StatusCode));
                            if(response.Content.Headers.ContentLength.HasValue&&(response.Content.Headers.ContentLength.Value>maximum||(expected>0&&response.Content.Headers.ContentLength.Value!=expected)))throw new ApiException(TextResources.Get("gitHubAssetTransport.transferAttemptAsync.text"));
                            using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var file=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,true)) {
                                owned=true;var block=new byte[32768];int count;long total=0;
                                while((count=await stream.ReadAsync(block,0,block.Length,token).ConfigureAwait(false))>0) {
                                    if(mirrored)idle.CancelAfter(TimeSpan.FromSeconds(30));total+=count;
                                    if(total>maximum||(expected>0&&total>expected))throw new ApiException(TextResources.Get("gitHubAssetTransport.transferAttemptAsync.text.alternativeA"));
                                    await file.WriteAsync(block,0,count,token).ConfigureAwait(false);if(progress!=null&&expected>0)progress.Report(Math.Min(99,total*100.0/expected));
                                }
                                if(total==0||(expected>0&&total!=expected))throw new ApiException(TextResources.Get("gitHubAssetTransport.transferAttemptAsync.text"));file.Flush(true);
                            }
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if(!String.IsNullOrEmpty(digest)&&digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))using(var file=File.OpenRead(partial))using(var hash=SHA256.Create()) {
                        string actual=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","");if(!String.Equals(actual,digest.Substring(7),StringComparison.OrdinalIgnoreCase))throw new ApiException(TextResources.Get("gitHubAssetTransport.transferAttemptAsync.text.alternativeB"));
                    }
                    token.ThrowIfCancellationRequested();File.Move(partial,target);complete=true;if(progress!=null)progress.Report(100);return target;
                }catch(Exception error){throw SafeError(error,cancel,token,TextResources.Get("gitHubAssetTransport.transferAttemptAsync.safeError"));}
                finally{if(owned&&!complete&&File.Exists(partial))File.Delete(partial);}
            }
        }
    }
}
