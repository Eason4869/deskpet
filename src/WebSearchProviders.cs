using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    internal static class WebSearchProviders
    {
        internal static string[] Ids {get{return new[]{"native","serpapi","deepseek","tavily","baidu","bocha","brave","firecrawl","exa","anysearch","doubao","custom"};}}
        internal static string[] SchemaIds {get{return Ids.Where(id=>id!="native"&&id!="custom").ToArray();}}
        internal static bool IsExternal(string id){return id!="native"&&Ids.Contains(id);}
        internal static string DisplayName(string id)
        {
            switch(id){case "serpapi":return "SerpApi";case "deepseek":return "DeepSeek Search API";case "tavily":return "Tavily";case "baidu":return "Baidu AI Search";case "bocha":return "Bocha";case "brave":return "Brave";case "firecrawl":return "Firecrawl";case "exa":return "Exa";case "anysearch":return "AnySearch";case "doubao":return "Doubao Search";case "custom":return "Custom API";default:return "Native";}
        }
        internal static string DefaultBaseUrl(string id)
        {
            switch(id){case "serpapi":return "https://serpapi.com";case "deepseek":return "https://api.deepseek.com";case "baidu":return "https://qianfan.baidubce.com";case "bocha":return "https://api.bocha.cn";case "brave":return "https://api.search.brave.com";case "firecrawl":return "https://api.firecrawl.dev";case "exa":return "https://api.exa.ai";case "anysearch":return "https://api.anysearch.com";case "doubao":return "https://open.feedcoopapi.com";case "custom":return "";default:return "https://api.tavily.com";}
        }
        internal static string Path(string id)
        {
            switch(id){case "serpapi":return "search.json";case "deepseek":return "v1/tools/search";case "baidu":return "v2/ai_search/web_search";case "bocha":return "v1/web-search";case "brave":return "res/v1/web/search";case "firecrawl":return "v2/search";case "anysearch":return "v1/search";case "doubao":return "search_api/web_search";default:return "search";}
        }
        internal static void ValidateConnection(string id,ConnectionSettings connection,bool allowAnonymous=false)
        {
            if(!SchemaIds.Contains(id))throw new ApiException(TextResources.Get("webSearch.unsupported"));
            if(connection!=null&&connection.ApiKey.Any(Char.IsControl))throw new ApiException(TextResources.Get("apiClient.validateConnection.text.alternativeA"));
            if(allowAnonymous||id=="anysearch")ApiClient.Endpoint(connection,Path(id));
            else ApiClient.ValidateConnection(connection,Path(id));
        }
    }
    public sealed partial class ApiClient
    {
        internal async Task<WebSearchResult> SearchExternalWebAsync(WebSearchSettings search,ConnectionSettings model,string query,CancellationToken cancel)
        {
            bool custom=search.Provider=="custom";string id=custom?search.CustomProtocol:search.Provider;
            int queryLimit=id=="deepseek"?128:id=="doubao"?100:1000;
            int characters=0;for(int i=0;i<query.Length;i++,characters++)if(Char.IsHighSurrogate(query[i])&&i+1<query.Length&&Char.IsLowSurrogate(query[i+1]))i++;
            if(characters>queryLimit)throw new ApiException(TextResources.Get("webSearch.invalidQuery"));
            var connection=search.Connection.Copy();connection.Model="search";connection.Protocol="openai";connection.UseProxy=model.UseProxy;connection.ProxyAddress=model.ProxyAddress;
            WebSearchProviders.ValidateConnection(id,connection,custom);
            using(UsageTracking.BeginScope("web-search"))
            using(var request=ExternalSearchRequest(id,connection,query,custom)){
                byte[] bytes=await SendAsync(connection,request,MaxJsonBytes,cancel).ConfigureAwait(false);
                try{return ParseExternalSearch(ObjectJson(bytes),id);}catch(Exception error){if(!Malformed(error))throw;throw new ApiException(TextResources.Get("webSearch.invalidResponse"));}
            }
        }
        static HttpRequestMessage ExternalSearchRequest(string id,ConnectionSettings connection,string query,bool custom)
        {
            string path=WebSearchProviders.Path(id);Uri endpoint=custom?new Uri(connection.BaseUrl.Trim(),UriKind.Absolute):ExternalSearchEndpoint(connection,path);string key=connection.ApiKey;
            bool get=id=="serpapi"||id=="brave";
            if(get){
                var parameters=new Dictionary<string,string>{{"q",query}};
                if(id=="serpapi"){parameters["engine"]="google";parameters["num"]="5";if(key.Length>0)parameters["api_key"]=key;}
                else {parameters["count"]="5";parameters["extra_snippets"]="true";}
                endpoint=new Uri(endpoint.AbsoluteUri+"?"+String.Join("&",parameters.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value))));
            }
            var request=new HttpRequestMessage(get?HttpMethod.Get:HttpMethod.Post,endpoint);
            try{
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if(key.Length>0){if(id=="brave")request.Headers.Add("X-Subscription-Token",key);else if(id=="exa")request.Headers.Add("x-api-key",key);else if(id!="serpapi")request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);}
                if(!get){
                    var body=new Dictionary<string,object>{{"query",query}};
                    switch(id){
                        case "tavily":body["search_depth"]="basic";body["max_results"]=5;body["include_answer"]=false;body["include_raw_content"]=false;break;
                        case "deepseek":body["count"]=5;body["need_content"]=false;break;
                        case "baidu":body.Clear();body["messages"]=new[]{new{role="user",content=query}};body["search_source"]="baidu_search_v2";body["resource_type_filter"]=new[]{new{type="web",top_k=5}};break;
                        case "bocha":body["count"]=5;body["summary"]=true;body["freshness"]="noLimit";break;
                        case "firecrawl":body["limit"]=5;body["sources"]=new[]{new{type="web"}};break;
                        case "exa":body["numResults"]=5;body["type"]="auto";body["contents"]=new{highlights=true};break;
                        case "anysearch":body["max_results"]=5;body["format"]="json";break;
                        case "doubao":body.Clear();body["Query"]=query;body["SearchType"]="web";body["Count"]=5;body["Filter"]=new{NeedUrl=true,NeedContent=false};break;
                    }
                    request.Content=Json(body);
                }
                return request;
            }catch{request.Dispose();throw;}
        }
        static Uri ExternalSearchEndpoint(ConnectionSettings connection,string path)
        {
            // Validate the original address before removing an already supplied version prefix.
            Uri endpoint=Endpoint(connection,path);string address=connection.BaseUrl.Trim().TrimEnd('/');
            if(address.EndsWith("/"+path,StringComparison.OrdinalIgnoreCase))return endpoint;
            string[] parts=path.Split('/');
            for(int count=parts.Length-1;count>0;count--){string prefix=String.Join("/",parts.Take(count));if(address.EndsWith("/"+prefix,StringComparison.OrdinalIgnoreCase))return Endpoint(connection,String.Join("/",parts.Skip(count)));}
            return endpoint;
        }
        static WebSearchResult ParseExternalSearch(Dictionary<string,object> root,string id)
        {
            object value;
            if(id=="serpapi"){
                object rawMetadata,rawInformation;
                var metadata=root.TryGetValue("search_metadata",out rawMetadata)?rawMetadata as Dictionary<string,object>:null;
                var information=root.TryGetValue("search_information",out rawInformation)?rawInformation as Dictionary<string,object>:null;
                // SerpApi explicitly returns an error string for successful searches with no matches.
                if(metadata!=null&&information!=null&&SearchString(metadata,"status")=="Success"&&SearchString(information,"organic_results_state")=="Fully empty")return new WebSearchResult{Text=TextResources.Get("webSearch.noResults")};
            }
            if(root.TryGetValue("error",out value)&&value!=null)throw new FormatException();
            if(id=="bocha"&&(!root.TryGetValue("code",out value)||Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!="200"))throw new FormatException();
            if(id=="anysearch"&&(!root.TryGetValue("code",out value)||Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!="0"))throw new FormatException();
            if(id=="baidu"&&root.ContainsKey("code"))throw new FormatException();
            if(id=="firecrawl"&&(!root.TryGetValue("success",out value)||!Object.Equals(value,true)))throw new FormatException();
            if(id=="doubao"){
                var metadata=SearchMap(root,"ResponseMetadata");if(metadata.TryGetValue("Error",out value)&&value!=null)throw new FormatException();
            }
            object[] entries;
            switch(id){
                case "serpapi":entries=SearchArray(root,"organic_results");break;
                case "baidu":entries=SearchArray(root,"references");break;
                case "bocha":entries=SearchArray(SearchMap(SearchMap(root,"data"),"webPages"),"value");break;
                case "brave":
                    if(root.TryGetValue("web",out value)&&value!=null)entries=SearchArray(SearchMap(root,"web"),"results");
                    else {if(SearchString(root,"type")!="search"||String.IsNullOrWhiteSpace(SearchString(SearchMap(root,"query"),"original")))throw new FormatException();entries=new object[0];}
                    break;
                case "firecrawl":entries=SearchArray(SearchMap(root,"data"),"web");break;
                case "anysearch":entries=SearchArray(SearchMap(root,"data"),"results");break;
                case "doubao":
                    var data=SearchMap(root,"Result");
                    if(data.TryGetValue("WebResults",out value)&&value!=null)entries=SearchArray(data,"WebResults");
                    else {if(!data.TryGetValue("ResultCount",out value)||!(value is int||value is long||value is double||value is decimal)||Convert.ToDecimal(value,System.Globalization.CultureInfo.InvariantCulture)!=0)throw new FormatException();entries=new object[0];}
                    break;
                default:entries=SearchArray(root,"results");break;
            }
            var result=new WebSearchResult();var text=new StringBuilder();
            foreach(var entry in entries.Take(5)){
                var item=entry as Dictionary<string,object>;if(item==null)continue;
                string title=SearchString(item,id=="bocha"?"name":id=="doubao"?"Title":"title"),url=SearchString(item,id=="serpapi"?"link":id=="doubao"?"Url":"url");int before=result.Sources.Count;result.AddSource(title,url);if(result.Sources.Count==before)continue;
                string content=id=="deepseek"||id=="bocha"?SearchFirstString(item,"summary","snippet","content"):id=="doubao"?SearchFirstString(item,"Summary","Content","Snippet"):id=="tavily"?SearchString(item,"content"):id=="brave"||id=="firecrawl"?SearchFirstString(item,"description","markdown"):id=="anysearch"?SearchFirstString(item,"snippet","content"):id=="baidu"?SearchString(item,"content"):id=="exa"?SearchFirstString(item,"summary","text"):SearchString(item,"snippet");
                if(id=="exa"&&item.TryGetValue("highlights",out value)&&value is object[]){string highlights=String.Join("\n",((object[])value).OfType<string>().Take(5));if(!String.IsNullOrWhiteSpace(highlights))content=highlights;}
                // Include source titles in the context even when a provider has no snippet.
                text.AppendLine(WebSearchResult.Clip(title,200));text.AppendLine(WebSearchResult.Clip(content,3000));
            }
            result.Text=WebSearchResult.Clip(text.ToString().Trim(),16000);if(result.Text.Length==0)result.Text=TextResources.Get("webSearch.noResults");return result;
        }
        static Dictionary<string,object> SearchMap(Dictionary<string,object> parent,string key){object value;if(!parent.TryGetValue(key,out value)||!(value is Dictionary<string,object>))throw new FormatException();return (Dictionary<string,object>)value;}
        static object[] SearchArray(Dictionary<string,object> parent,string key){object value;if(!parent.TryGetValue(key,out value)||!(value is object[]))throw new FormatException();return (object[])value;}
        static string SearchFirstString(Dictionary<string,object> item,params string[] keys){foreach(string key in keys){string value=SearchString(item,key);if(!String.IsNullOrWhiteSpace(value))return value;}return "";}
    }
}
