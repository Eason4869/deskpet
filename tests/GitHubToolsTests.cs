using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class GitHubToolsTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class Handler:HttpMessageHandler
        {
            public readonly List<string> Requests=new List<string>();public string Body="";
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                Requests.Add(request.Method+" "+request.RequestUri+" auth="+(request.Headers.Authorization==null?"none":request.Headers.Authorization.Scheme+" "+request.Headers.Authorization.Parameter));
                string path=request.RequestUri.AbsolutePath;
                string json=path.Contains("/contents/")?"{\"type\":\"file\",\"encoding\":\"base64\",\"content\":\"" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello github")) + "\"}":path.Contains("/issues")?"{\"number\":7,\"title\":\"bug\",\"body\":\"body\"}":"{\"ok\":true}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        static ConnectionSettings Config(){return new ConnectionSettings{BaseUrl="https://api.github.com",ApiKey="secret",Protocol="github"};}
        public static int Main()
        {
            try
            {
                var handler=new Handler();var client=new GitHubClient(new Uri("https://api.github.com"),handler,"openai/codex","secret");
                var read=client.ExecuteAsync("github_read_file",new Dictionary<string,object>{{"path","README.md"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(read.Contains("hello github") && handler.Requests[0].Contains("Bearer secret"),"GitHubReadFile reads repository content with authenticated API");
                var issue=client.ExecuteAsync("github_create_issue",new Dictionary<string,object>{{"title","fix"},{ "body","details"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(issue.Contains("bug") && handler.Requests[1].StartsWith("POST"),"GitHubCreateIssue posts an authenticated issue");
                var web=client.ExecuteAsync("fetch_url",new Dictionary<string,object>{{"url","https://example.test/page"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(web.Length>0 && handler.Requests.Count>=3,"FetchUrl returns bounded readable web text");
                Console.WriteLine("GitHub tools tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
