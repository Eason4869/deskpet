using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class WebSearchTests
    {
        static int passed,failed;
        static readonly JavaScriptSerializer json=new JavaScriptSerializer();
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        static void Run(string name,Func<Task> test){try{test().GetAwaiter().GetResult();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
        static ConnectionSettings Model(string url,string protocol="openai"){return new ConnectionSettings{BaseUrl=url,Protocol=protocol,Model="fixture",ApiKey="dummy-model-key",StreamReplies=false};}
        static WebSearchSettings Tavily(string url){return new WebSearchSettings{Enabled=true,Provider="tavily",Connection=new ConnectionSettings{BaseUrl=url,Model="search",ApiKey="dummy-search-key"}};}
        const string Results="{\"results\":[{\"title\":\"Official source\",\"url\":\"https://example.com/news\",\"content\":\"Verified fact\"},{\"title\":\"unsafe\",\"url\":\"javascript:alert(1)\",\"content\":\"ignored\"}]}";
        public static int Main()
        {
            Run("Tavily uses its own encrypted key and returns bounded public sources",async delegate{
                using(var server=new ApiTests.Fixture(Results))using(var api=new ApiClient()){
                    var settings=Tavily(server.Url);var result=await api.SearchWebAsync(settings,Model("https://api.openai.com/v1"),"最新消息",CancellationToken.None);await server.Done;
                    var body=(Dictionary<string,object>)json.DeserializeObject(server.Body);Check((string)body["query"]=="最新消息"&&(int)body["max_results"]==5,"wrong Tavily query");Check(server.Authorization=="Bearer dummy-search-key"&&!server.Body.Contains("dummy-model-key"),"wrong authentication");Check(result.Text.Contains("Verified fact")&&result.Sources.Count==1,"results lost or unsafe URL accepted");
                }
            });
            Run("native protocols send the documented search request and preserve citations",async delegate{
                foreach(string protocol in new[]{"openai-responses","anthropic","qwen"}){
                    string response=protocol=="openai-responses"?"{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"fact\",\"annotations\":[{\"type\":\"url_citation\",\"url\":\"https://example.com/news\",\"title\":\"News\"}]}]}]}":protocol=="anthropic"?"{\"content\":[{\"type\":\"text\",\"text\":\"fact\",\"citations\":[{\"type\":\"web_search_result_location\",\"url\":\"https://example.com/news\",\"title\":\"News\"}]}]}":"{\"choices\":[{\"message\":{\"content\":\"fact\"}}],\"search_info\":{\"search_results\":[{\"title\":\"News\",\"url\":\"https://example.com/news\"}]}}";
                    using(var server=new ApiTests.Fixture(response))using(var api=new ApiClient()){
                        var cfg=new WebSearchSettings{Enabled=true,NativeProtocol=protocol};var result=await api.SearchWebAsync(cfg,Model(server.Url,protocol=="anthropic"?"anthropic":"openai"),"query",CancellationToken.None);await server.Done;var body=(Dictionary<string,object>)json.DeserializeObject(server.Body);
                        Check(server.Headers.StartsWith("POST /"+(protocol=="openai-responses"?"responses":protocol=="anthropic"?"messages":"chat/completions")+" HTTP"),"wrong native endpoint");Check(result.Text.Contains("fact")&&result.Sources.Count==1,"native text/citations lost");
                        if(protocol=="qwen")Check(Object.Equals(body["enable_search"],true),"qwen search disabled");else Check(((object[])body["tools"]).Length==1,"native tool absent");
                    }
                }
            });
            Run("ordinary conversation exposes only search and feeds actual results to the model",async delegate{
                using(var search=new ApiTests.Fixture(Results))
                using(var model=new SequenceServer("{\"choices\":[{\"message\":{\"content\":null,\"tool_calls\":[{\"id\":\"call1\",\"type\":\"function\",\"function\":{\"name\":\"web_search\",\"arguments\":\"{\\\"query\\\":\\\"news\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}","{\"choices\":[{\"message\":{\"content\":\"Final answer\"}}]}"))using(var api=new ApiClient()){
                    string answer;var cfg=new PetSettings{AgentEnabled=false,ReadReplies=false,SaveChatHistory=false,Text=Model(model.Url),WebSearch=Tavily(search.Url)};
                    using(var conversation=new ConversationSession(api,new SilentSpeech(),()=>null)){await conversation.SendTextAsync("search news",cfg,"fixture");answer=conversation.History.Messages.Last().Content;Check(conversation.CurrentAgentTask==null,"search created a local agent task");}
                    await model.Done;await search.Done;
                    var body=(Dictionary<string,object>)json.DeserializeObject(model.Bodies[0]);var tools=(object[])body["tools"];Check(tools.Length==1&&json.Serialize(tools[0]).Contains("web_search"),"local tools exposed");Check(answer.Contains("Final answer")&&answer.Contains("https://example.com/news"),"answer/citations missing");Check(model.Bodies[1].Contains("Verified fact"),"search result not returned to model");
                }
            });
            Run("configuration copies and persists keys without plaintext",async delegate{
                var original=new PetSettings{WebSearch=Tavily("https://api.tavily.com")};var copy=original.Copy();copy.WebSearch.Connection.ApiKey="changed";Check(original.WebSearch.Connection.ApiKey=="dummy-search-key","search settings shared across snapshots");
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"search-settings-"+Guid.NewGuid().ToString("N"),"settings.json");var store=new SettingsStore(path);store.Save(original);Check(!File.ReadAllText(path).Contains("dummy-search-key"),"plaintext secret persisted");Check(store.Load().WebSearch.Connection.ApiKey=="dummy-search-key","search key did not round trip");await Task.FromResult(0);
            });
            Run("streaming search retains reasoning, text and citations",async delegate{
                string first="data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"s1\",\"type\":\"function\",\"function\":{\"name\":\"web_search\",\"arguments\":\"{\\\"query\\\":\\\"news\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\ndata: [DONE]\n\n";
                string final="data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"Checked\",\"content\":\"Stream answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
                using(var search=new ApiTests.Fixture(Results))using(var model=new SequenceServer(first,final))using(var api=new ApiClient()){
                    string text="",reasoning="";var events=new List<AgentToolEvent>();string answer=await api.ChatSearchAsync(Model(model.Url),new[]{new ChatMessage("user","news")},Tavily(search.Url),d=>text+=d,d=>reasoning+=d,e=>events.Add(e),true,CancellationToken.None);
                    Check(text==answer&&reasoning=="Checked"&&events.Any(e=>e.Name=="web_search"&&e.Status=="complete"),"streaming output, reasoning or tool status lost");
                }
            });
            Run("conversation search rejects model-invented local tools",async delegate{
                string call="{\"choices\":[{\"message\":{\"content\":null,\"tool_calls\":[{\"id\":\"bad\",\"type\":\"function\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{\\\"path\\\":\\\"should-not-exist.txt\\\",\\\"content\\\":\\\"bad\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
                using(var model=new SequenceServer(call,"{\"choices\":[{\"message\":{\"content\":\"Cannot write\"}}]}"))using(var api=new ApiClient()){
                    var options=Tavily("http://127.0.0.1:1");string answer=await api.ChatSearchAsync(Model(model.Url),new[]{new ChatMessage("user","search")},options,null,null,null,false,CancellationToken.None);Check(answer=="Cannot write"&&model.Bodies[1].Contains("web_search")&&!File.Exists("should-not-exist.txt"),"invented local tool was not rejected");
                }
            });
            Run("work mode search is advertised only when enabled and obeys network denial",async delegate{
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"search-work-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                using(var workspace=new AgentWorkspace(folder))using(var api=new ApiClient()){
                    Check(!workspace.AvailableToolNames.Split(',').Any(n=>n.Trim()=="web_search"),"unconfigured search advertised");
                    workspace.SearchSession=new WebSearchSession(api,Tavily("http://127.0.0.1:1"),Model("http://127.0.0.1:1"));Check(workspace.AvailableToolNames.Split(',').Any(n=>n.Trim()=="web_search"),"configured search absent in work mode");
                    var permissions=new AgentPermissionRuntime();permissions.Add(new AgentPermissionRule{Action="network",Resource="**",Effect=AgentPermissionEffect.Deny});workspace.ConfigureKernel(permissions,new AgentAuditLog(folder));
                    string result=await workspace.ExecuteAsync("web_search","{\"query\":\"news\"}",CancellationToken.None);Check(TextResources.IsToolError(result),"search bypassed network denial");
                }
            });
            Run("search count, source toggle, cancellation and HTTP errors are bounded",async delegate{
                using(var server=new SequenceServer(Results,Results,Results,Results))using(var api=new ApiClient()){
                    var options=Tavily(server.Url);options.ShowSources=false;var session=new WebSearchSession(api,options,Model(server.Url));for(int i=0;i<4;i++)Check(!TextResources.IsToolError(await session.ExecuteAsync("web_search","{\"query\":\"news\"}",CancellationToken.None)),"valid search failed");Check(TextResources.IsToolError(await session.ExecuteAsync("web_search","{\"query\":\"news\"}",CancellationToken.None))&&server.Bodies.Count==4,"fifth paid search was sent");Check(session.SourceFooter("answer")=="","citation toggle ignored");
                }
                using(var server=new ApiTests.Fixture("private dummy-search-key detail",401))using(var api=new ApiClient()){
                    string result=await new WebSearchSession(api,Tavily(server.Url),Model(server.Url)).ExecuteAsync("web_search","{\"query\":\"news\"}",CancellationToken.None);Check(TextResources.IsToolError(result)&&result.Contains("401")&&!result.Contains("dummy-search-key")&&!result.Contains("private"),"unsafe HTTP error");
                }
                using(var server=new ApiTests.Fixture(Results))using(var api=new ApiClient())using(var cancel=new CancellationTokenSource(100)){
                    server.DelayMilliseconds=1000;bool cancelled=false;try{await api.SearchWebAsync(Tavily(server.Url),Model(server.Url),"news",cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancel did not stop search");
                }
            });
            Run("disabled, unsupported and oversized searches fail without network",async delegate{
                using(var api=new ApiClient()){
                    foreach(var options in new[]{new WebSearchSettings(),new WebSearchSettings{Enabled=true}}){bool rejected=false;try{await api.SearchWebAsync(options,Model("https://unsupported.invalid"),"query",CancellationToken.None);}catch(ApiException){rejected=true;}Check(rejected,"unavailable search accepted");}
                    bool invalid=false;try{await api.SearchWebAsync(Tavily("http://127.0.0.1:1"),Model("http://127.0.0.1:1"),new string('q',1001),CancellationToken.None);}catch(ApiException){invalid=true;}Check(invalid,"oversized query accepted");
                }
            });
            Run("partial native responses cannot be reported as successful search",async delegate{
                const string payload="{\"status\":\"incomplete\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"partial\",\"annotations\":[{\"url\":\"https://example.com/news\",\"title\":\"News\"}]}]}]}";
                using(var server=new ApiTests.Fixture(payload))using(var api=new ApiClient()){
                    bool rejected=false;try{await api.SearchWebAsync(new WebSearchSettings{Enabled=true,NativeProtocol="openai-responses"},Model(server.Url),"news",CancellationToken.None);}catch(ApiException){rejected=true;}Check(rejected,"incomplete search marked successful");
                }
            });
            Run("search requests respect the selected model context limit",async delegate{
                using(var api=new ApiClient()){
                    var model=Model("http://127.0.0.1:1");model.RequestContextCapacity=1024;bool rejected=false;
                    try{await api.ChatSearchAsync(model,new[]{new ChatMessage("user",new string('x',12000))},Tavily(model.BaseUrl),null,null,null,false,CancellationToken.None);}catch(ApiException error){rejected=error.Message==TextResources.Get("agentContextBudget.prepareAsync.text");}
                    Check(rejected,"oversized search context was sent to the provider");
                }
            });
            Console.WriteLine("Web search tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
        sealed class SilentSpeech:IReplySpeech{public Task SpeakAsync(string text,PetSettings cfg,CancellationToken cancel){return Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
        sealed class SequenceServer:IDisposable
        {
            readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);internal readonly List<string> Bodies=new List<string>();internal readonly string Url;internal readonly Task Done;
            internal SequenceServer(params string[] replies){listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;Done=Task.Run(async delegate{
                foreach(string reply in replies)using(var client=await listener.AcceptTcpClientAsync())using(var stream=client.GetStream()){
                    client.ReceiveTimeout=5000;var bytes=new List<byte>();int match=0;byte[] end={13,10,13,10};while(match<4){int b=stream.ReadByte();if(b<0)throw new IOException();bytes.Add((byte)b);match=b==end[match]?match+1:0;}
                    string headers=Encoding.ASCII.GetString(bytes.ToArray());int size=Int32.Parse(headers.Split(new[]{"\r\n"},StringSplitOptions.None).Single(l=>l.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Substring(15).Trim());
                    if(headers.IndexOf("100-continue",StringComparison.OrdinalIgnoreCase)>=0){byte[] interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");await stream.WriteAsync(interim,0,interim.Length);}
                    byte[] body=new byte[size];int read=0;while(read<size){int n=await stream.ReadAsync(body,read,size-read);if(n==0)throw new IOException();read+=n;}Bodies.Add(Encoding.UTF8.GetString(body));
                    byte[] payload=Encoding.UTF8.GetBytes(reply),head=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: "+(reply.StartsWith("data:")?"text/event-stream":"application/json")+"\r\nContent-Length: "+payload.Length+"\r\nConnection: close\r\n\r\n");await stream.WriteAsync(head,0,head.Length);await stream.WriteAsync(payload,0,payload.Length);
                }
            });}
            public void Dispose(){listener.Stop();}
        }
    }
}
