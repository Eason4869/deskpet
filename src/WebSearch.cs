using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    [DataContract]
    public sealed class WebSearchSettings
    {
        [DataMember] public bool Enabled;
        [DataMember] public string Provider="native";
        [DataMember] public string NativeProtocol="auto";
        [DataMember] public string CustomProtocol="tavily";
        [DataMember] public bool ShowSources=true;
        [DataMember] public ConnectionSettings Connection=new ConnectionSettings{BaseUrl="https://api.tavily.com",Model="search"};
        [DataMember] public Dictionary<string,ConnectionSettings> ProviderConnections=new Dictionary<string,ConnectionSettings>();
        [OnDeserializing] void BeforeDeserialize(StreamingContext context){Provider="native";NativeProtocol="auto";CustomProtocol="tavily";ShowSources=true;Connection=new ConnectionSettings{BaseUrl="https://api.tavily.com",Model="search"};ProviderConnections=new Dictionary<string,ConnectionSettings>();}
        public WebSearchSettings Copy(){return new WebSearchSettings{Enabled=Enabled,Provider=Provider,NativeProtocol=NativeProtocol,CustomProtocol=CustomProtocol,ShowSources=ShowSources,Connection=Connection==null?null:Connection.Copy(),ProviderConnections=(ProviderConnections??new Dictionary<string,ConnectionSettings>()).Where(p=>p.Value!=null).ToDictionary(p=>p.Key,p=>p.Value.Copy())};}
        public ConnectionSettings ReadConnection(string id){ConnectionSettings value;return ProviderConnections!=null&&ProviderConnections.TryGetValue(id,out value)&&value!=null?value.Copy():new ConnectionSettings{BaseUrl=WebSearchProviders.DefaultBaseUrl(id),Model="search",Protocol="openai"};}
        public void SetConnection(string id,ConnectionSettings value){if(!WebSearchProviders.IsExternal(id))return;if(ProviderConnections==null)ProviderConnections=new Dictionary<string,ConnectionSettings>();var copy=(value??ReadConnection(id)).Copy();copy.Normalize();copy.Model="search";copy.Protocol="openai";ProviderConnections[id]=copy;if(Provider==id)Connection=copy;}
        public void Normalize()
        {
            if(!WebSearchProviders.Ids.Contains(Provider))Provider="native";
            if(!new[]{"auto","openai-responses","anthropic","qwen"}.Contains(NativeProtocol))NativeProtocol="auto";
            if(!WebSearchProviders.SchemaIds.Contains(CustomProtocol))CustomProtocol="tavily";
            var clean=new Dictionary<string,ConnectionSettings>();
            if(ProviderConnections!=null)foreach(var pair in ProviderConnections)if(WebSearchProviders.IsExternal(pair.Key)&&pair.Value!=null){var value=pair.Value.Copy();value.Normalize();value.Model="search";value.Protocol="openai";clean[pair.Key]=value;}
            // Old settings stored just one Tavily connection, including while native mode was selected.
            bool unusedDefault=Provider=="custom"&&Connection!=null&&Connection.BaseUrl==WebSearchProviders.DefaultBaseUrl("tavily")&&String.IsNullOrEmpty(Connection.EncryptedKey);
            if(clean.Count==0&&Connection!=null&&!unusedDefault)clean[Provider=="native"?"tavily":Provider]=Connection.Copy();
            ProviderConnections=clean;
            if(WebSearchProviders.IsExternal(Provider)){Connection=ReadConnection(Provider);Connection.Normalize();Connection.Model="search";Connection.Protocol="openai";ProviderConnections[Provider]=Connection;}
            else {Connection=Connection??ReadConnection("tavily");Connection.Normalize();Connection.Model="search";Connection.Protocol="openai";}
        }
        internal string ResolveProtocol(ConnectionSettings model)
        {
            if(NativeProtocol!="auto")return NativeProtocol;
            string brand=ProviderIdentity.Brand(model.BaseUrl);
            return brand=="openai"?"openai-responses":brand=="qwen"?"qwen":brand=="anthropic"||model.Protocol=="anthropic"?"anthropic":"";
        }
        public void Validate(ConnectionSettings model)
        {
            if(!Enabled)throw new ApiException(TextResources.Get("webSearch.disabled"));
            if(WebSearchProviders.IsExternal(Provider))WebSearchProviders.ValidateConnection(Provider=="custom"?CustomProtocol:Provider,Connection,Provider=="custom");
            else if(Provider!="native"||String.IsNullOrEmpty(ResolveProtocol(model)))throw new ApiException(TextResources.Get("webSearch.unsupported"));
        }
    }
    internal sealed class WebSearchSource
    {
        internal string Title,Url;
        internal string Markdown {get{return "["+(Title??Url).Replace("[","(").Replace("]",")").Replace("\r"," ").Replace("\n"," ")+"]("+Url.Replace("(","%28").Replace(")","%29")+")";}}
    }
    internal sealed class WebSearchResult
    {
        internal string Text="";internal readonly List<WebSearchSource> Sources=new List<WebSearchSource>();
        internal void AddSource(string title,string address)
        {
            Uri uri;if(Sources.Count>=10||!Uri.TryCreate(address,UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https")||uri.UserInfo.Length>0||address.Length>2048||Sources.Any(s=>s.Url==uri.AbsoluteUri))return;
            Sources.Add(new WebSearchSource{Title=Clip(String.IsNullOrWhiteSpace(title)?uri.Host:title,200),Url=uri.AbsoluteUri});
        }
        internal static string Clip(string value,int max){return value.Length>max?value.Substring(0,max):value;}
        internal string ForModel(){return "<untrusted_web_search_results>\n"+Text+"\n"+String.Join("\n",Sources.Select(s=>s.Markdown))+"\n</untrusted_web_search_results>";}
    }
    public interface IWebSearchConversationApi
    {
        Task<string> ChatSearchAsync(ConnectionSettings model,IList<ChatMessage> messages,WebSearchSettings search,Action<string> onDelta,Action<string> onReasoning,Action<AgentToolEvent> onToolEvent,bool stream,CancellationToken cancel);
    }
    internal sealed class WebSearchSession
    {
        readonly ApiClient api;readonly WebSearchSettings settings;readonly ConnectionSettings model;
        readonly List<WebSearchSource> sources=new List<WebSearchSource>();int calls;
        internal WebSearchSession(ApiClient api,WebSearchSettings settings,ConnectionSettings model){this.api=api;this.settings=settings.Copy();this.model=model.Copy();this.settings.Normalize();this.settings.Validate(model);}
        internal string Instruction {get{return TextResources.Get("webSearch.toolPrompt")+(settings.ShowSources?"":"\n"+TextResources.Get("webSearch.noSourcePrompt"));}}
        internal async Task<string> ExecuteAsync(string name,string arguments,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();if(name!="web_search")return Error(TextResources.Get("webSearch.onlySearch"));
            if(Interlocked.Increment(ref calls)>4)return Error(TextResources.Get("webSearch.limit"));
            try{
                var args=new JavaScriptSerializer{MaxJsonLength=4096,RecursionLimit=8}.DeserializeObject(arguments??"{}") as Dictionary<string,object>;object raw;
                if(args==null||!args.TryGetValue("query",out raw)||!(raw is string))return Error(TextResources.Get("webSearch.invalidQuery"));
                var result=await api.SearchWebAsync(settings,model,(string)raw,cancel).ConfigureAwait(false);
                foreach(var source in result.Sources)if(sources.Count<10&&!sources.Any(s=>s.Url==source.Url))sources.Add(source);
                return result.ForModel();
            }
            catch(ArgumentException){return Error(TextResources.Get("webSearch.invalidQuery"));}
            catch(ApiException error){return TextResources.Get("webSearch.errorPrefix")+error.Message;}
        }
        static string Error(string message){return TextResources.Get("webSearch.errorPrefix")+message;}
        internal string SourceFooter(string answer){if(!settings.ShowSources)return "";var missing=sources.Where(s=>answer.IndexOf(s.Url,StringComparison.Ordinal)<0).ToArray();return missing.Length==0?"":"\n\n"+TextResources.Get("webSearch.sources")+"\n"+String.Join("\n",missing.Select(s=>"- "+s.Markdown));}
        internal static void Schemas(bool anthropic,out object[] tools){object[] openai,claude;AgentToolCatalog.Schemas(false,null,null,out openai,out claude,n=>n=="web_search");tools=anthropic?claude:openai;}
    }
    public sealed partial class ApiClient : IWebSearchConversationApi
    {
        public Task<string> ChatSearchAsync(ConnectionSettings model,IList<ChatMessage> messages,WebSearchSettings search,Action<string> onDelta,Action<string> onReasoning,Action<AgentToolEvent> onToolEvent,bool stream,CancellationToken cancel)
        {return ChatAgentCoreAsync(model,messages,null,new WebSearchSession(this,search,model),onDelta,onReasoning,onToolEvent,stream,cancel);}

        internal async Task<WebSearchResult> SearchWebAsync(WebSearchSettings search,ConnectionSettings model,string query,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();search.Validate(model);query=(query??"").Trim();if(query.Length==0||query.Length>1000)throw new ApiException(TextResources.Get("webSearch.invalidQuery"));
            if(WebSearchProviders.IsExternal(search.Provider))return await SearchExternalWebAsync(search,model,query,cancel).ConfigureAwait(false);
            string protocol=search.ResolveProtocol(model);var connection=model.Copy();
            connection.UseProxy=model.UseProxy;connection.ProxyAddress=model.ProxyAddress;
            string path;Dictionary<string,object> body;
            string prompt=TextResources.Get("webSearch.nativePrompt")+"\n"+query;
            connection.Protocol=protocol=="anthropic"?"anthropic":"openai";
            if(protocol=="openai-responses"){
                path="responses";body=new Dictionary<string,object>{{"model",connection.Model},{"input",prompt},{"tools",new[]{new{type="web_search"}}},{"tool_choice","required"},{"include",new[]{"web_search_call.action.sources"}},{"max_output_tokens",1600},{"store",false}};
            }else{
                path=ChatPath(connection);body=new Dictionary<string,object>{{"model",connection.Model},{"messages",new[]{new{role="user",content=prompt}}},{"stream",false}};
                if(protocol=="anthropic"){body["max_tokens"]=1600;body["tools"]=new[]{new{type="web_search_20250305",name="web_search",max_uses=3}};}
                else {body["enable_search"]=true;body["search_options"]=new{forced_search=true,enable_source=true,enable_citation=true};}
            }
            using(UsageTracking.BeginScope("web-search"))
            using(var request=Request(connection,path,Json(body)))
            using(var usage=UsageTracking.BeginRequest(connection,cancel)){
                byte[] bytes=await SendAsync(connection,request,MaxJsonBytes,cancel).ConfigureAwait(false);
                try{
                    var root=ObjectJson(bytes);UsageTracking.Observe(root,protocol!="qwen");
                    var result=ParseWebSearch(root,protocol);usage.Complete();return result;
                }catch(Exception error){if(!Malformed(error))throw;throw new ApiException(TextResources.Get("webSearch.invalidResponse"));}
            }
        }
        static WebSearchResult ParseWebSearch(Dictionary<string,object> root,string protocol)
        {
            var result=new WebSearchResult();var text=new StringBuilder();object value;
            if(root.TryGetValue("error",out value)&&value!=null)throw new FormatException();
            if(protocol=="openai-responses"&&root.TryGetValue("status",out value)&&!Object.Equals(value,"completed"))throw new FormatException();
            string field=protocol=="openai-responses"?"output":protocol=="anthropic"?"content":"choices";
            if(!root.TryGetValue(field,out value)||!(value is object[]))throw new FormatException();
            CollectSearchData(root,result,text,0);
            if(result.Sources.Count==0)throw new ApiException(TextResources.Get("webSearch.noEvidence"));
            result.Text=WebSearchResult.Clip(text.ToString().Trim(),16000);if(result.Text.Length==0)result.Text=TextResources.Get("webSearch.noResults");return result;
        }
        static string SearchString(Dictionary<string,object> item,string key){object value;return item.TryGetValue(key,out value)?value as string??"":"";}
        static void CollectSearchData(object value,WebSearchResult result,StringBuilder text,int depth)
        {
            if(depth>16)return;var map=value as Dictionary<string,object>;
            if(map!=null){
                string type=SearchString(map,"type");if(type=="text"||type=="output_text")text.AppendLine(SearchString(map,"text"));
                if(map.ContainsKey("url"))result.AddSource(SearchString(map,"title"),SearchString(map,"url"));
                // Chat Completions places the answer in message.content.
                object content;if(map.TryGetValue("content",out content)&&content is string)text.AppendLine((string)content);
                foreach(var pair in map)if(pair.Value is IDictionary||pair.Value is object[])CollectSearchData(pair.Value,result,text,depth+1);
            }else {var list=value as object[];if(list!=null)foreach(var item in list)CollectSearchData(item,result,text,depth+1);}
        }
    }
}
