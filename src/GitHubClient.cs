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
        internal const string PublicApi = "https://api.github.com/";
        internal const string CliProtocol = "github-cli";
        const int MaximumResponseBytes=2*1024*1024,MaximumTextChars=120000;
        readonly Uri apiRoot; readonly HttpClient http;
        readonly string apiToken, expectedLogin;
        readonly Func<HttpMethod, string, string, CancellationToken, Task<string>> apiTransport;
        string repository;
        bool disposed;
        internal string Repository {get{return repository;}}
        internal string CliExecutable { get; private set; }
        internal bool CanCreateRepository
        {
            get
            {
                return (apiToken.Length != 0 || (apiTransport != null && expectedLogin.Length != 0)) &&
                    String.Equals(apiRoot.AbsoluteUri, PublicApi, StringComparison.OrdinalIgnoreCase);
            }
        }
        internal GitHubClient(Uri root,HttpMessageHandler handler,string repository,string token,
            Func<HttpMethod, string, string, CancellationToken, Task<string>> apiTransport = null, string expectedLogin = null)
        {
            apiRoot=root??new Uri(PublicApi);this.repository=repository??"";this.apiToken=token??"";
            this.apiTransport = apiTransport;
            this.expectedLogin = expectedLogin ?? "";
            http=new HttpClient(handler??new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        }
        internal GitHubClient(string repository,ConnectionSettings settings):this(new Uri(String.IsNullOrWhiteSpace(settings==null?null:settings.BaseUrl)?"https://api.github.com/":settings.BaseUrl),null,repository,settings==null?null:settings.ApiKey)
        {
        }

        internal static GitHubClient ForApp(string repository, string apiToken, HttpMessageHandler handler = null)
        {
            return new GitHubClient(new Uri(PublicApi), handler, ApplicationRepository(repository), apiToken);
        }

        internal static GitHubClient WithApiTransport(string repository, string expectedLogin,
            Func<HttpMethod, string, string, CancellationToken, Task<string>> transport)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            return new GitHubClient(new Uri(PublicApi), null, ApplicationRepository(repository), null, transport, expectedLogin);
        }

        internal static GitHubClient ForCli(string repository, string expectedLogin, GitHubCliTransport cli)
        {
            var client = WithApiTransport(repository, expectedLogin, cli.SendAsync);
            client.CliExecutable = cli.ExecutablePath;
            return client;
        }

        internal static bool UsesCli(PetSettings settings)
        {
            return settings != null && settings.GitHub != null && settings.GitHub.Protocol == CliProtocol &&
                IsPublicEndpoint(settings.GitHub.BaseUrl) && !String.IsNullOrWhiteSpace(settings.GitHubCliAccount);
        }

        internal static bool IsPublicEndpoint(string endpoint)
        {
            return String.Equals((endpoint ?? "").TrimEnd('/'), PublicApi.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        internal static GitHubClient ForConnection(string repository, PetSettings settings)
        {
            if (UsesCli(settings))
            {
                var cli = GitHubCliTransport.Find();
                if (cli != null) return ForCli(repository, settings.GitHubCliAccount, cli);
                return ForApp(repository, null);
            }
            return new GitHubClient(repository, settings == null ? null : settings.GitHub);
        }

        internal void UseRepository(string value) { repository = ApplicationRepository(value); }

        static string ApplicationRepository(string repository)
        {
            string normalized = repository ?? "";
            if (normalized.Length != 0 && (normalized.Length > 300 || !Regex.IsMatch(normalized, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*\z")))
                throw new ArgumentException(TextResources.Get("gitHubUpdates.repository.text"));
            return normalized;
        }

        internal async Task<string> GetAuthenticatedLoginAsync(CancellationToken token)
        {
            string json = await SendJsonAsync(HttpMethod.Get, "/user", null, token).ConfigureAwait(false);
            string login = GetString(ParseObject(json), "login", "");
            if (!Regex.IsMatch(login, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,99}\z")) throw new FormatException();
            if (expectedLogin.Length != 0 && !String.Equals(login, expectedLogin, StringComparison.OrdinalIgnoreCase))
                throw new ApiException(TextResources.Get("gitHubApp.failed"));
            return login;
        }

        internal async Task<GitHubPushAuthentication> GetPushAuthenticationAsync(CancellationToken token)
        {
            if (!CanCreateRepository) return null;
            string login = await GetAuthenticatedLoginAsync(token).ConfigureAwait(false);
            return new GitHubPushAuthentication { Login = login, CliExecutable = CliExecutable, Token = apiToken };
        }

        internal async Task ConfirmRepositoryAsync(string value, CancellationToken token)
        {
            var repo = ParseRepository(ApplicationRepository(value));
            string response = await SendJsonAsync(HttpMethod.Get, "/repos/" + repo.Owner + "/" + repo.Name, null, token).ConfigureAwait(false);
            if (!String.Equals(GetString(ParseObject(response), "full_name", ""), value, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }

        internal async Task<string> GetDefaultBranchAsync(CancellationToken token)
        {
            var repo = ParseRepository(repository);
            string json = await SendJsonAsync(HttpMethod.Get, "/repos/" + repo.Owner + "/" + repo.Name, null, token).ConfigureAwait(false);
            string branch = GetString(ParseObject(json), "default_branch", "");
            if (String.IsNullOrWhiteSpace(branch) || branch.Length > 240 || branch.Any(Char.IsControl)) throw new FormatException();
            return branch;
        }

        internal async Task<Uri> CreateAppPullRequestAsync(string title, string body, string head, string baseBranch, CancellationToken token)
        {
            string json = await ExecuteCoreAsync("github_create_pull_request", new Dictionary<string, object> {
                { "title", title }, { "body", body }, { "head", head }, { "base", baseBranch }
            }, token).ConfigureAwait(false);
            object raw;
            if (!ParseObject(json).TryGetValue("number", out raw) || !(raw is int) || (int)raw <= 0) throw new FormatException();
            return new Uri("https://github.com/" + repository + "/pull/" + ((int)raw).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        static Dictionary<string, object> ParseObject(string json)
        {
            var value = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes }.DeserializeObject(json) as Dictionary<string, object>;
            if (value == null) throw new FormatException();
            return value;
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
            if(name=="github_create_repository")return await CreateRepositoryAsync(args,token).ConfigureAwait(false);
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

        internal async Task<string> CreateRepositoryAsync(Dictionary<string, object> args, CancellationToken token, Action beforeEffect = null)
        {
            string name = GetString(args, "name", "").Trim();
            string visibility = GetString(args, "visibility", "private");
            string description = GetString(args, "description", "");
            if (!Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\z") ||
                name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                (visibility != "private" && visibility != "public") || description.Length > 350)
                throw new ArgumentException(TextResources.Get("gitHubUpdates.repository.text"));
            if (!CanCreateRepository) throw new ApiException(TextResources.Get("workSummary.gitHub.noToken"));

            string owner = await GetAuthenticatedLoginAsync(token).ConfigureAwait(false);
            string expected = owner + "/" + name;
            bool isPrivate = visibility == "private";
            string json = await SendJsonAsync(HttpMethod.Post, "/user/repos", new Dictionary<string, object> {
                { "name", name }, { "description", description }, { "private", isPrivate }
            }, token, beforeEffect).ConfigureAwait(false);
            var result = ParseObject(json);
            string fullName = GetString(result, "full_name", "");
            string branch = GetString(result, "default_branch", "");
            object actualPrivacy;
            if (!String.Equals(expected, fullName, StringComparison.OrdinalIgnoreCase) ||
                !result.TryGetValue("private", out actualPrivacy) || !(actualPrivacy is bool) || (bool)actualPrivacy != isPrivate ||
                String.IsNullOrWhiteSpace(branch) || branch.Length > 240 || branch.Any(Char.IsControl))
                throw new FormatException();
            return new JavaScriptSerializer().Serialize(new {
                repository = fullName, url = "https://github.com/" + fullName, default_branch = branch
            });
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
            if(String.IsNullOrWhiteSpace(query) || Regex.IsMatch(query,@"(?i)\b(repo|org|user|owner)\s*:"))
                throw new ArgumentException(TextResources.Get("gitHubClient.searchCodeAsync.text"));
            return SendJsonAsync(HttpMethod.Get,"/search/code?q="+Uri.EscapeDataString(query+" repo:"+repo.Owner+"/"+repo.Name),null,token);
        }
        async Task<string> FetchUrlAsync(string url,CancellationToken token)
        {
            Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri) || (uri.Scheme!="https" && uri.Scheme!="http"))throw new ArgumentException(TextResources.Get("gitHubClient.fetchUrlAsync.text"));
            if(uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase) || uri.Host=="127.0.0.1" || uri.Host=="::1")throw new ArgumentException(TextResources.Get("gitHubClient.fetchUrlAsync.text.alternativeA"));
            using(var request=new HttpRequestMessage(HttpMethod.Get,uri)){using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)){if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Get("gitHubClient.fetchUrlAsync.text.alternativeB"));using(var memory=new MemoryStream()){await response.Content.CopyToAsync(memory).ConfigureAwait(false);if(memory.Length>MaximumResponseBytes)throw new ApiException(TextResources.Get("gitHubClient.fetchUrlAsync.text.alternativeC"));string text=Encoding.UTF8.GetString(memory.ToArray());text=Regex.Replace(text,"(?is)<script.*?</script>|<style.*?</style>"," ");text=Regex.Replace(text,"<[^>]+>"," ");text=Regex.Replace(text,"\\s+"," ").Trim();return text.Length>MaximumTextChars?TextResources.Format("gitHubClient.fetchUrlAsync.template",text.Substring(0,MaximumTextChars)):text;}}}
        }
        async Task<string> SendJsonAsync(HttpMethod method,string path,object payload,CancellationToken token, Action beforeEffect = null)
        {
            string body = payload == null ? null : new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes }.Serialize(payload);
            if (apiTransport != null)
            {
                if (method != HttpMethod.Get && expectedLogin.Length == 0)
                    throw new ApiException(TextResources.Get("gitHubApp.failed"));
                if (path != "/user" && expectedLogin.Length != 0)
                    await GetAuthenticatedLoginAsync(token).ConfigureAwait(false);
                if (beforeEffect != null) beforeEffect();
                return await apiTransport(method, path, body, token).ConfigureAwait(false);
            }
            using(var request=new HttpRequestMessage(method,new Uri(apiRoot,path.TrimStart('/'))))
            {
                if(!String.IsNullOrEmpty(apiToken))request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",apiToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));request.Headers.Add("X-GitHub-Api-Version","2022-11-28");request.Headers.UserAgent.ParseAdd("DeskPet");
                if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
                if (beforeEffect != null) beforeEffect();
                using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                {
                    if(!response.IsSuccessStatusCode)throw new ApiException(TextResources.Format("gitHubClient.sendJsonAsync.template",(int)response.StatusCode));
                    using (var cancelBody = token.Register(response.Dispose))
                    using (var memory = new MemoryStream())
                    {
                        try
                        {
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                var buffer = new byte[8192];
                                int count;
                                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                                {
                                    if (memory.Length + count > MaximumResponseBytes) throw new ApiException(TextResources.Get("gitHubClient.sendJsonAsync.text"));
                                    memory.Write(buffer, 0, count);
                                }
                            }
                        }
                        catch (Exception) { token.ThrowIfCancellationRequested(); throw; }
                        token.ThrowIfCancellationRequested();
                        return Encoding.UTF8.GetString(memory.ToArray());
                    }
                }
            }
        }
        static Repo ParseRepository(string value)
        {
            var match=Regex.Match(value??"", @"\A([A-Za-z0-9_-][A-Za-z0-9_.-]*)/([A-Za-z0-9_.-]+)\z");
            if(!match.Success || match.Groups[2].Value=="." || match.Groups[2].Value=="..")
                throw new ArgumentException(TextResources.Get("gitHubClient.parseRepository.text"));
            return new Repo{Owner=match.Groups[1].Value,Name=match.Groups[2].Value};
        }
        static string EscapePath(string path)
        {
            var parts = path.Replace('\\','/').Split('/');
            if (parts.Any(part => part == "." || part == ".." || part.Any(Char.IsControl)))
                throw new ArgumentException(TextResources.Get("gitHubClient.readFileAsync.text"));
            return String.Join("/", parts.Select(Uri.EscapeDataString));
        }
        static string GetString(Dictionary<string,object> args,string key,string fallback){object value;return args.TryGetValue(key,out value)&&value is string?(string)value:fallback;}
        static int GetInt(Dictionary<string,object> args,string key,int fallback,int min,int max){object value;if(!args.TryGetValue(key,out value))return fallback;int number;return Int32.TryParse(Convert.ToString(value),out number)?Math.Max(min,Math.Min(max,number)):fallback;}
        public void Dispose(){if(disposed)return;disposed=true;http.Dispose();}
        sealed class Repo{public string Owner,Name;}
    }
}
