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
        sealed class FailureHandler:HttpMessageHandler
        {
            internal string Mode;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                if(Mode=="network")throw new HttpRequestException("PRIVATE transport details");
                if(Mode=="body")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new BrokenStream())});
                if(Mode=="timeout")throw new OperationCanceledException();
                if(Mode=="cancel"){token.ThrowIfCancellationRequested();throw new OperationCanceledException(token);}
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("PRIVATE response body")});
            }
        }
        sealed class BrokenStream:System.IO.Stream
        {
            public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return false;}}
            public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}
            public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException();}public override long Seek(long value,System.IO.SeekOrigin origin){throw new NotSupportedException();}
            public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
            public override int Read(byte[] buffer,int offset,int count){throw new System.IO.IOException("PRIVATE body details");}
            public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){throw new System.IO.IOException("PRIVATE body details");}
            public override Task CopyToAsync(System.IO.Stream destination,int bufferSize,CancellationToken token){throw new System.IO.IOException("PRIVATE body details");}
        }
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
                foreach(string tool in new[]{"fetch_url","github_read_issue"})foreach(string mode in new[]{"http","network","body","timeout"})
                using(var failing=new GitHubClient(new Uri("https://api.github.com"),new FailureHandler{Mode=mode},"openai/codex","secret"))
                {
                    string result=failing.ExecuteAsync(tool,new Dictionary<string,object>{{"url","https://example.test/page"},{"number",1}},CancellationToken.None).GetAwaiter().GetResult();
                    Check(result.StartsWith("错误：")&&!result.Contains("PRIVATE")&&!result.Contains("secret"),tool+" reports sanitized "+mode+" failure as a tool result");
                }
                using(var writing=new GitHubClient(new Uri("https://api.github.com"),new FailureHandler{Mode="network"},"openai/codex","secret"))
                {
                    bool uncertain=false;try{writing.ExecuteAsync("github_create_issue",new Dictionary<string,object>{{"title","issue"}},CancellationToken.None).GetAwaiter().GetResult();}catch(HttpRequestException){uncertain=true;}
                    Check(uncertain,"write transport failures still require reconciliation rather than inviting duplicate writes");
                }
                using(var stopping=new GitHubClient(new Uri("https://api.github.com"),new FailureHandler{Mode="cancel"},"openai/codex","secret"))
                using(var cancel=new CancellationTokenSource())
                {
                    cancel.Cancel();bool cancelled=false;try{stopping.ExecuteAsync("fetch_url",new Dictionary<string,object>{{"url","https://example.test/page"}},cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled,"explicit cancellation remains cancellation");
                }
                Console.WriteLine("GitHub tools tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
