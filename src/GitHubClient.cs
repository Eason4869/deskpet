using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    internal sealed class GitHubClient : IDisposable
    {
        const int MaximumResponseBytes=2*1024*1024,MaximumTextChars=120000;
        readonly Uri apiRoot;readonly HttpClient http;readonly string repository,apiToken;bool disposed;
        internal string Repository {get{return repository;}}
        internal GitHubClient(Uri root,HttpMessageHandler handler,string repository,string token)
        {
            apiRoot=root??new Uri("https://api.github.com/");this.repository=repository??"";this.apiToken=token??"";
            http=new HttpClient(handler??new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        }
        internal GitHubClient(string repository,ConnectionSettings settings):this(new Uri(String.IsNullOrWhiteSpace(settings==null?null:settings.BaseUrl)?"https://api.github.com/":settings.BaseUrl),null,repository,settings==null?null:settings.ApiKey)
        {
        }
        public async Task<string> ExecuteAsync(string name,Dictionary<string,object> args,CancellationToken token)
        {
            // Writes may have reached the server before a transport failure;
            // preserve their existing interruption/reconciliation behavior.
            if(!AgentToolCatalog.IsReadOnly(name))return await ExecuteCoreAsync(name,args,token).ConfigureAwait(false);
            // A remote failure is an observation for the agent, not a failed
            // conversation submission that should be returned to the input queue.
            try{return await ExecuteCoreAsync(name,args,token).ConfigureAwait(false);}
            catch(OperationCanceledException){if(token.IsCancellationRequested)throw;return TextResources.Get("gitHubClient.executeAsync.text");}
            catch(ApiException e){return TextResources.Format("gitHubClient.executeAsync.template",e.Message);}
            catch(HttpRequestException){return TextResources.Get("gitHubClient.executeAsync.text.alternativeA");}
            catch(IOException){return TextResources.Get("gitHubClient.executeAsync.text.alternativeB");}
        }
        async Task<string> ExecuteCoreAsync(string name,Dictionary<string,object> args,CancellationToken token)
        {
            if(disposed)throw new ObjectDisposedException("GitHubClient");
            args=args??new Dictionary<string,object>();
            if(name=="fetch_url")return await FetchUrlAsync(GetString(args,"url",null),token).ConfigureAwait(false);
            var repo=ParseRepository(repository);
            if(name=="github_read_file")return await ReadFileAsync(repo,GetString(args,"path",null),GetString(args,"ref",null),token).ConfigureAwait(false);
            if(name=="github_list_files")return await ListFilesAsync(repo,GetString(args,"path",""),GetString(args,"ref",null),token).ConfigureAwait(false);
            if(name=="github_search_code")return await SearchCodeAsync(repo,GetString(args,"query",null),token).ConfigureAwait(false);
            if(name=="github_read_issue")return await SendJsonAsync(HttpMethod.Get,"/repos/"+repo.Owner+"/"+repo.Name+"/issues/"+GetInt(args,"number",0,1,1000000),null,token).ConfigureAwait(false);
            if(name=="github_create_issue")return await SendJsonAsync(HttpMethod.Post,"/repos/"+repo.Owner+"/"+repo.Name+"/issues",new Dictionary<string,object>{{"title",GetString(args,"title",null)},{"body",GetString(args,"body","")}},token).ConfigureAwait(false);
            if(name=="github_comment")return await SendJsonAsync(HttpMethod.Post,"/repos/"+repo.Owner+"/"+repo.Name+"/issues/"+GetInt(args,"number",0,1,1000000)+"/comments",new Dictionary<string,object>{{"body",GetString(args,"body",null)}},token).ConfigureAwait(false);
            if(name=="github_list_pull_requests")return await SendJsonAsync(HttpMethod.Get,"/repos/"+repo.Owner+"/"+repo.Name+"/pulls?state="+Uri.EscapeDataString(GetString(args,"state","open")),null,token).ConfigureAwait(false);
            if(name=="github_create_pull_request")return await SendJsonAsync(HttpMethod.Post,"/repos/"+repo.Owner+"/"+repo.Name+"/pulls",new Dictionary<string,object>{{"title",GetString(args,"title",null)},{"body",GetString(args,"body","")},{"head",GetString(args,"head",null)},{"base",GetString(args,"base",null)}},token).ConfigureAwait(false);
            throw new ArgumentException(TextResources.Get("gitHubClient.executeCoreAsync.text"));
        }
        async Task<string> ReadFileAsync(Repo repo,string path,string reference,CancellationToken token)
        {
            if(String.IsNullOrWhiteSpace(path))throw new ArgumentException(TextResources.Get("gitHubClient.readFileAsync.text"));
            string url="/repos/"+repo.Owner+"/"+repo.Name+"/contents/"+EscapePath(path)+(String.IsNullOrWhiteSpace(reference)?"":"?ref="+Uri.EscapeDataString(reference));
            string json=await SendJsonAsync(HttpMethod.Get,url,null,token).ConfigureAwait(false);var value=new JavaScriptSerializer{MaxJsonLength=MaximumResponseBytes}.DeserializeObject(json) as Dictionary<string,object>;
            if(value==null)throw new ApiException(TextResources.Get("gitHubClient.readFileAsync.text.alternativeA"));
            object raw;if(!value.TryGetValue("content",out raw) || !(raw is string))return json;
            try{return Encoding.UTF8.GetString(Convert.FromBase64String((string)raw));}catch(FormatException){throw new ApiException(TextResources.Get("gitHubClient.readFileAsync.text.alternativeB"));}
        }
        async Task<string> ListFilesAsync(Repo repo,string path,string reference,CancellationToken token)
        {
            string url="/repos/"+repo.Owner+"/"+repo.Name+"/contents/"+EscapePath(path)+(String.IsNullOrWhiteSpace(reference)?"":"?ref="+Uri.EscapeDataString(reference));return await SendJsonAsync(HttpMethod.Get,url,null,token).ConfigureAwait(false);
        }
        Task<string> SearchCodeAsync(Repo repo,string query,CancellationToken token)
        {
            if(String.IsNullOrWhiteSpace(query))throw new ArgumentException(TextResources.Get("gitHubClient.searchCodeAsync.text"));
            return SendJsonAsync(HttpMethod.Get,"/search/code?q="+Uri.EscapeDataString(query+" repo:"+repo.Owner+"/"+repo.Name),null,token);
        }
        async Task<string> FetchUrlAsync(string url,CancellationToken token)
        {
            Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri) || (uri.Scheme!="https" && uri.Scheme!="http"))throw new ArgumentException(TextResources.Get("gitHubClient.fetchUrlAsync.text"));
            if(uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase) || uri.Host=="127.0.0.1" || uri.Host=="::1")throw new ArgumentException(TextResources.Get("gitHubClient.fetchUrlAsync.text.alternativeA"));
            using(var request=new HttpRequestMessage(HttpMethod.Get,uri)){using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)){if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Get("gitHubClient.fetchUrlAsync.text.alternativeB"));using(var memory=new MemoryStream()){await response.Content.CopyToAsync(memory).ConfigureAwait(false);if(memory.Length>MaximumResponseBytes)throw new ApiException(TextResources.Get("gitHubClient.fetchUrlAsync.text.alternativeC"));string text=Encoding.UTF8.GetString(memory.ToArray());text=Regex.Replace(text,"(?is)<script.*?</script>|<style.*?</style>"," ");text=Regex.Replace(text,"<[^>]+>"," ");text=Regex.Replace(text,"\\s+"," ").Trim();return text.Length>MaximumTextChars?TextResources.Format("gitHubClient.fetchUrlAsync.template",text.Substring(0,MaximumTextChars)):text;}}}
        }
        async Task<string> SendJsonAsync(HttpMethod method,string path,object payload,CancellationToken token)
        {
            using(var request=new HttpRequestMessage(method,new Uri(apiRoot,path.TrimStart('/'))))
            {
                if(!String.IsNullOrEmpty(apiToken))request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",apiToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));request.Headers.Add("X-GitHub-Api-Version","2022-11-28");
                if(payload!=null)request.Content=new StringContent(new JavaScriptSerializer{MaxJsonLength=MaximumResponseBytes}.Serialize(payload),Encoding.UTF8,"application/json");
                using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                {
                    using(var memory=new MemoryStream()){await response.Content.CopyToAsync(memory).ConfigureAwait(false);if(memory.Length>MaximumResponseBytes)throw new ApiException(TextResources.Get("gitHubClient.sendJsonAsync.text"));string body=Encoding.UTF8.GetString(memory.ToArray());if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Format("gitHubClient.sendJsonAsync.template",(int)response.StatusCode));return body;}
                }
            }
        }
        static Repo ParseRepository(string value)
        {
            var match=Regex.Match(value??"", "^([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)$");if(!match.Success)throw new ArgumentException(TextResources.Get("gitHubClient.parseRepository.text"));return new Repo{Owner=match.Groups[1].Value,Name=match.Groups[2].Value};
        }
        static string EscapePath(string path){return String.Join("/",path.Replace('\\','/').Split('/').Select(Uri.EscapeDataString));}
        static string GetString(Dictionary<string,object> args,string key,string fallback){object value;return args.TryGetValue(key,out value)&&value is string?(string)value:fallback;}
        static int GetInt(Dictionary<string,object> args,string key,int fallback,int min,int max){object value;if(!args.TryGetValue(key,out value))return fallback;int number;return Int32.TryParse(Convert.ToString(value),out number)?Math.Max(min,Math.Min(max,number)):fallback;}
        public void Dispose(){if(disposed)return;disposed=true;http.Dispose();}
        sealed class Repo{public string Owner,Name;}
    }
}
