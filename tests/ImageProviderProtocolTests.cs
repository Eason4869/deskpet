using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu.Tests
{
    public static class ImageProviderProtocolTests
    {
        const string Png="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=";
        static int passed;static string directory;
        public static int Main()
        {
            passed=0;directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"image-protocol-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{Presets();Protocols().GetAwaiter().GetResult();Console.WriteLine("ImageProviderProtocolTests: "+passed+" passed");return 0;}
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{Directory.Delete(directory,true);}
        }
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static ImageServicePreset Preset(string id){foreach(var preset in ImageServicePresets.All)if(preset.Id==id)return preset;throw new Exception("Missing preset: "+id);}
        static void Presets()
        {
            var ids=new HashSet<string>();foreach(var preset in ImageServicePresets.All){ids.Add(preset.Id);Check(!String.IsNullOrWhiteSpace(preset.Name)&&preset.ToString()==preset.Name,"preset exposes readable name: "+preset.Id);}
            Check(ids.SetEquals(new[]{"openai","gemini","doubao","minimax","qwen","grok","custom"}),"presets include all six requested providers and custom service");
            Check(new HashSet<string>(ImageServicePresets.Protocols).SetEquals(new[]{"openai","gemini","doubao","minimax","qwen","grok"}),"protocol keys remain explicit and independent of text settings");
            var gemini=Preset("gemini").CreateProvider();var second=Preset("gemini").CreateProvider();gemini.Connection.Model="custom-native-image";gemini.Connection.ApiKey="native-private-key";
            Check(gemini.Id!=second.Id&&second.Connection.Model=="gemini-3.1-flash-image"&&second.Connection.ApiKey==""&&gemini.Protocol=="gemini","preset instances keep independently editable models IDs and credentials");
            var settings=new PetSettings();settings.ImageProviders.Add(gemini);settings.ActiveImageProviderId=gemini.Id;var store=new SettingsStore(Path.Combine(directory,"settings.json"));store.Save(settings);var loaded=store.Load();
            Check(loaded.SelectedImageProvider().Protocol=="gemini"&&loaded.Copy().ImageProviders[0].Protocol=="gemini"&&loaded.SelectedImageProvider().Connection.Model=="custom-native-image","native protocol and custom model survive settings reload and copy");
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{\"Name\":\"legacy\"}"))){var legacy=(ImageGenerationProvider)new DataContractJsonSerializer(typeof(ImageGenerationProvider)).ReadObject(stream);legacy.Normalize();Check(legacy.Protocol=="openai","legacy image providers default to original OpenAI protocol");}
        }
        static async Task Protocols()
        {
            using(var store=new GeneratedImageStore(Path.Combine(directory,"assets"))){
                foreach(string protocol in new[]{"openai","gemini","doubao","minimax","qwen","grok"}){
                    var provider=Preset(protocol).CreateProvider();provider.Connection.ApiKey="native-private-key";string body="",path="",contentType="",bearer="",googleKey="";string output=Output(protocol);
                    var handler=new Handler(async delegate(HttpRequestMessage message,CancellationToken token){body=await message.Content.ReadAsStringAsync();path=message.RequestUri.AbsolutePath;contentType=message.Content.Headers.ContentType.MediaType;bearer=message.Headers.Authorization==null?"":message.Headers.Authorization.ToString();IEnumerable<string> keys;if(message.Headers.TryGetValues("x-goog-api-key",out keys))googleKey=String.Join("",keys);return Json(output);});
                    using(var client=new ImageGenerationClient(store,handler,null)){
                        var image=await client.GenerateAsync(new ImageGenerationRequest{Provider=provider,Prompt="draw the moon",SessionId="native-session",TaskId="native-task",MessageId="native-message"},CancellationToken.None);
                        Check(image.Width==1&&image.SessionId=="native-session"&&image.TaskId=="native-task"&&image.MessageId=="native-message"&&store.Get(image.Id)!=null,"protocol result commits validated asset with original ownership: "+protocol);
                        Check(contentType=="application/json"&&!body.Contains("native-private-key")&&!body.Contains("quality"),"default protocol JSON omits secrets and unspecified quality: "+protocol);
                        if(protocol=="gemini"){Check(path=="/v1beta/models/gemini-3.1-flash-image:generateContent"&&googleKey=="native-private-key"&&bearer=="","Gemini native endpoint uses key header without Bearer or URL key");Check(body.Contains("contents")&&body.Contains("parts")&&body.Contains("responseModalities")&&body.Contains("IMAGE"),"Gemini request carries native contents and response modalities");}
                        else{Check(bearer=="Bearer native-private-key"&&googleKey=="","native provider uses independent bearer authentication: "+protocol);Check(path==(protocol=="minimax"?"/v1/image_generation":protocol=="doubao"?"/api/v3/images/generations":protocol=="qwen"?"/compatible-mode/v1/images/generations":"/v1/images/generations"),"native request endpoint matches protocol: "+protocol);}
                        if(protocol!="openai"){
                            await client.GenerateAsync(new ImageGenerationRequest{Provider=provider,Prompt="make it blue",ReferenceImageBytes=Convert.FromBase64String(Png),ReferenceMediaType="image/png"},CancellationToken.None);
                            Check(contentType=="application/json"&&body.Contains(Png),"native reference editing uses documented JSON shape: "+protocol);
                            if(protocol=="gemini")Check(body.Contains("inlineData")&&body.Contains("mimeType")&&path.EndsWith(":generateContent"),"Gemini reference is inlineData on native generateContent");
                            if(protocol=="doubao"||protocol=="qwen")Check(body.Contains("data:image/png;base64,")&&body.Contains("\"image\"")&&path.EndsWith("/images/generations"),"JSON image editing stays on generations endpoint: "+protocol);
                            if(protocol=="minimax")Check(body.Contains("subject_reference")&&body.Contains("character")&&body.Contains("image_file")&&path=="/v1/image_generation","MiniMax reference uses documented character subject shape");
                            if(protocol=="grok")Check(path=="/v1/images/edits"&&body.Contains("image_url")&&body.Contains("\"url\""),"Grok edits use JSON image URL object rather than multipart");
                        }
                    }
                }
                await Parameters(store);
                await NativeResults(store);
                foreach(var entry in new[]{new[]{"gemini","{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}"},new[]{"gemini","{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"native-private-key\"}]}}]}"},new[]{"minimax","{\"base_resp\":{\"status_code\":1008,\"status_msg\":\"native-private-key\"},\"data\":{\"image_base64\":[\""+Png+"\"]}}"}}){
                    using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json(entry[1]));}),null))await Fails(client,new ImageGenerationRequest{Provider=Preset(entry[0]).CreateProvider(),Prompt="bad response"},"图片","native error or image-less safety response never becomes completed asset");
                }
            }
        }
        static async Task Parameters(GeneratedImageStore store)
        {
            foreach(var entry in new[]{new[]{"doubao-seedream-5-0-flash-260915","absent"},new[]{"doubao-seedream-5-0-pro-260628","absent"},new[]{"doubao-seedream-5-0-lite-260128","disabled"},new[]{"doubao-seedream-4-5-251128","disabled"},new[]{"doubao-seedream-4-0-250828","disabled"}}){
                string body="";var provider=Preset("doubao").CreateProvider();provider.Connection.Model=entry[0];using(var client=new ImageGenerationClient(store,new Handler(async delegate(HttpRequestMessage request,CancellationToken token){body=await request.Content.ReadAsStringAsync();return Json(Output("doubao"));}),null)){
                    await client.GenerateAsync(new ImageGenerationRequest{Provider=provider,Prompt="one image"},CancellationToken.None);var payload=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(body);object sequential;bool hasSequential=payload.TryGetValue("sequential_image_generation",out sequential);
                    Check(entry[1]=="absent"?!hasSequential:hasSequential&&String.Equals(sequential,"disabled"),"Seedream single-image option respects documented model capability: "+entry[0]);
                }
            }
            foreach(string protocol in new[]{"gemini","doubao","minimax","qwen"}){
                int calls=0;using(var client=new ImageGenerationClient(store,new Handler(delegate{calls++;return Task.FromResult(Json(Output(protocol)));}),null)){
                    await Fails(client,new ImageGenerationRequest{Provider=Preset(protocol).CreateProvider(),Prompt="unsupported",Quality="high"},"质量","unsupported native quality rejected before any service request: "+protocol);Check(calls==0,"unsupported parameter never charges a provider request: "+protocol);
                }
            }
            foreach(var entry in new[]{new[]{"gemini","1024x1536","aspectRatio","2:3"},new[]{"gemini","2K","imageSize","2K"},new[]{"doubao","2K","size","2K"},new[]{"minimax","16:9","aspect_ratio","16:9"},new[]{"qwen","1024x1024","size","1024x1024"},new[]{"grok","1536x1024","aspect_ratio","3:2"}}){
                string body="";using(var client=new ImageGenerationClient(store,new Handler(async delegate(HttpRequestMessage message,CancellationToken token){body=await message.Content.ReadAsStringAsync();return Json(Output(entry[0]));}),null)){
                    await client.GenerateAsync(new ImageGenerationRequest{Provider=Preset(entry[0]).CreateProvider(),Prompt="dimensions",Size=entry[1]},CancellationToken.None);Check(body.Contains("\""+entry[2]+"\":\""+entry[3]+"\""),"native size maps to documented field: "+entry[0]+" "+entry[1]);
                }
            }
            string grokBody="";using(var client=new ImageGenerationClient(store,new Handler(async delegate(HttpRequestMessage message,CancellationToken token){grokBody=await message.Content.ReadAsStringAsync();return Json(Output("grok"));}),null)){await client.GenerateAsync(new ImageGenerationRequest{Provider=Preset("grok").CreateProvider(),Prompt="quality",Quality="medium"},CancellationToken.None);Check(grokBody.Contains("\"quality\":\"medium\""),"Grok sends explicitly supported quality");}
            foreach(var entry in new[]{new[]{"gemini","7:5"},new[]{"doubao","1K"},new[]{"minimax","1000x999"},new[]{"qwen","32x32"},new[]{"grok","7:5"}}){int calls=0;using(var client=new ImageGenerationClient(store,new Handler(delegate{calls++;return Task.FromResult(Json(Output(entry[0])));}),null)){await Fails(client,new ImageGenerationRequest{Provider=Preset(entry[0]).CreateProvider(),Prompt="bad dimensions",Size=entry[1]},"尺寸","unsupported native dimensions rejected before service request: "+entry[0]);Check(calls==0,"invalid dimension cannot charge a provider request: "+entry[0]);}}
            var unknown=Preset("custom").CreateProvider();unknown.Connection.BaseUrl="https://unknown.example/v1";unknown.Connection.Model="image";unknown.Protocol="unsupported-native";
            using(var client=new ImageGenerationClient(store,new Handler(delegate{throw new Exception("Unsupported protocol reached service");}),null))await Fails(client,new ImageGenerationRequest{Provider=unknown,Prompt="unknown"},"协议","unknown saved protocol fails explicitly instead of silently choosing OpenAI");
        }
        static async Task NativeResults(GeneratedImageStore store)
        {
            bool downloaded=false;var remote="{\"base_resp\":{\"status_code\":0},\"data\":{\"image_urls\":[\"https://93.184.216.34/image.png\"]}}";
            using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json(remote));}),new Handler(delegate(HttpRequestMessage request,CancellationToken token){downloaded=true;Check(request.Headers.Authorization==null&&!request.Headers.Contains("x-goog-api-key")&&!request.Headers.Contains("Cookie"),"native response URL download receives no provider credentials or cookies");var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Convert.FromBase64String(Png))};response.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");return Task.FromResult(response);}))){var result=await client.GenerateAsync(new ImageGenerationRequest{Provider=Preset("minimax").CreateProvider(),Prompt="remote native"},CancellationToken.None);Check(downloaded&&result.Width==1,"MiniMax nested URL response shares safe bounded download and local commit");}
            string thoughts="{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"invalid-thought-image\"}},{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\""+Png+"\"}}]}}]}";
            using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json(thoughts));}),null)){var result=await client.GenerateAsync(new ImageGenerationRequest{Provider=Preset("gemini").CreateProvider(),Prompt="native thoughts"},CancellationToken.None);Check(result.Width==1,"Gemini ignores internal thought images and accepts snake-case inline image response");}
            using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json(Output("gemini").Replace("image/png","image/jpeg")));}),null))await Fails(client,new ImageGenerationRequest{Provider=Preset("gemini").CreateProvider(),Prompt="wrong inline MIME"},"类型","native inline MIME must match actual validated image bytes");
            var gemini=Preset("gemini").CreateProvider();using(var cancel=new CancellationTokenSource())using(var client=new ImageGenerationClient(store,new Handler(async delegate(HttpRequestMessage request,CancellationToken token){await Task.Delay(10000,token);return Json(Output("gemini"));}),null)){cancel.CancelAfter(30);bool canceled=false;try{await client.GenerateAsync(new ImageGenerationRequest{Provider=gemini,Prompt="cancel native"},cancel.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled,"native generation shares user cancellation without automatic retry");}
        }
        static async Task Fails(ImageGenerationClient client,ImageGenerationRequest request,string fragment,string name){try{await client.GenerateAsync(request,CancellationToken.None);}catch(Exception e){Check(e.Message.Contains(fragment)&&!e.ToString().Contains("native-private-key"),name);return;}throw new Exception("Expected failure: "+name);}
        static string Output(string protocol){return protocol=="gemini"?"{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"description\"},{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\""+Png+"\"}}]}}]}":protocol=="minimax"?"{\"base_resp\":{\"status_code\":0},\"data\":{\"image_base64\":[\""+Png+"\"]}}":"{\"data\":[{\"b64_json\":\""+Png+"\"}]}";}
        static HttpResponseMessage Json(string value){return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(value,Encoding.UTF8,"application/json")};}
        sealed class Handler:HttpMessageHandler{readonly Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send;public Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send){this.send=send;}protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return send(request,token);}}
    }
}
