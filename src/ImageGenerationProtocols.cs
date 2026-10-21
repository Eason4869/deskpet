using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    internal sealed class ImageProtocolResult
    {
        public string Base64;
        public string Url;
        public string MediaType;
    }
    public static class ImageGenerationProtocols
    {
        public static string[] SizeOptions(string protocol)
        {
            switch(protocol){
                case "gemini":return new[]{"","1:1","16:9","9:16","4:3","3:4","3:2","2:3","4:5","5:4","21:9","1:4","4:1","1:8","8:1","1K","2K","4K"};
                case "doubao":return new[]{"","2K","4K","2048x2048","2560x1440","1440x2560","4096x4096"};
                case "minimax":return new[]{"","1:1","16:9","4:3","3:2","2:3","3:4","9:16","21:9"};
                case "qwen":return new[]{"","auto","1024x1024","1536x1024","1024x1536","2048x2048"};
                case "grok":return new[]{"","auto","1:1","16:9","9:16","4:3","3:4","3:2","2:3","2:1","1:2","19.5:9","9:19.5","20:9","9:20","21:9","5:2"};
                default:return new[]{"","1024x1024","1536x1024","1024x1536"};
            }
        }
        public static string[] QualityOptions(string protocol){return protocol=="grok"?new[]{"","auto","low","medium"}:protocol=="openai" || String.IsNullOrEmpty(protocol)?new[]{"","auto","low","medium","high"}:new[]{""};}
        internal static HttpRequestMessage CreateRequest(ImageGenerationProvider provider,Uri endpoint,string prompt,string size,string quality,byte[] reference,string referenceType)
        {
            string protocol=provider.Protocol;string suffix="/images/generations";HttpContent content=null;
            var payload=new Dictionary<string,object>{{"model",provider.Connection.Model},{"prompt",prompt}};
            if(protocol=="openai"){
                if(reference==null){Optional(payload,"size",size);Optional(payload,"quality",quality);}
                else{
                    suffix="/images/edits";var multipart=new MultipartFormDataContent();content=multipart;
                    multipart.Add(new StringContent(provider.Connection.Model),"model");multipart.Add(new StringContent(prompt),"prompt");if(size.Length>0)multipart.Add(new StringContent(size),"size");if(quality.Length>0)multipart.Add(new StringContent(quality),"quality");
                    var file=new ByteArrayContent(reference);file.Headers.ContentType=new MediaTypeHeaderValue(referenceType);multipart.Add(file,"image","reference"+(referenceType=="image/jpeg"?".jpg":referenceType=="image/webp"?".webp":referenceType=="image/gif"?".gif":".png"));
                }
            }else if(protocol=="gemini"){
                NoQuality(quality);suffix="/models/"+Uri.EscapeDataString(provider.Connection.Model)+":generateContent";
                var parts=new List<object>{new Dictionary<string,object>{{"text",prompt}}};if(reference!=null)parts.Add(new Dictionary<string,object>{{"inlineData",new Dictionary<string,object>{{"mimeType",referenceType},{"data",Convert.ToBase64String(reference)}}}});
                var config=new Dictionary<string,object>{{"responseModalities",new[]{"TEXT","IMAGE"}}};
                if(size.Length>0){var imageConfig=new Dictionary<string,object>();
                    if(size=="1K" || size=="2K" || size=="4K" || size=="512"){
                        if(provider.Connection.Model=="gemini-2.5-flash-image" || (provider.Connection.Model=="gemini-3.1-flash-lite-image" && size!="1K") || (size=="512" && provider.Connection.Model!="gemini-3.1-flash-image"))throw new InvalidOperationException("当前 Gemini 模型不支持所选图片分辨率，请使用服务默认或该模型支持的尺寸。");
                        imageConfig["imageSize"]=size;
                    }else imageConfig["aspectRatio"]=AspectRatio(size,SizeOptions(protocol));config["imageConfig"]=imageConfig;
                }
                payload=new Dictionary<string,object>{{"contents",new[]{new Dictionary<string,object>{{"role","user"},{"parts",parts}}}},{"generationConfig",config}};
            }else if(protocol=="doubao"){
                NoQuality(quality);if(size.Length>0){ValidateSeedreamSize(size,provider.Connection.Model);payload["size"]=size;}
                // Only these documented families accept the group-generation switch.
                // Pro/Flash generate one image by default and reject this parameter.
                if(provider.Connection.Model.StartsWith("doubao-seedream-4-5-",StringComparison.Ordinal) || provider.Connection.Model.StartsWith("doubao-seedream-4-0-",StringComparison.Ordinal) || provider.Connection.Model.StartsWith("doubao-seedream-5-0-lite-",StringComparison.Ordinal))payload["sequential_image_generation"]="disabled";
                if(reference!=null)payload["image"]=DataUri(reference,referenceType);
            }else if(protocol=="qwen"){
                NoQuality(quality);if(size.Length>0 && size!="auto"){int w,h;Pixels(size,out w,out h);long area=(long)w*h;if(area<512L*512 || area>2048L*2048 || (double)w/h>8 || (double)h/w>8)throw new InvalidOperationException("千问图片尺寸须为 WxH，总像素在 512×512 至 2048×2048 之间，宽高比不超过 8:1。");}Optional(payload,"size",size);payload["n"]=1;
                if(reference!=null){var format=GeneratedImageValidation.Validate(reference);if(reference.Length>10*1024*1024 || format.Width>8000 || format.Height>8000 || (double)format.Width/format.Height>10 || (double)format.Height/format.Width>10)throw new InvalidOperationException("千问参考图须不超过 10 MB、边长不超过 8000 像素，宽高比不超过 10:1。");payload["image"]=DataUri(reference,referenceType);}
            }else if(protocol=="minimax"){
                NoQuality(quality);if(prompt.Length>1500)throw new InvalidOperationException("MiniMax 图片提示词最多 1500 字，请缩短后重试。");suffix="/image_generation";payload["n"]=1;payload["response_format"]="base64";
                if(size.Length>0){
                    if(size.IndexOf('x')>=0 || size.IndexOf('X')>=0){int w,h;Pixels(size,out w,out h);if(provider.Connection.Model=="image-01-live" || w<512 || w>2048 || h<512 || h>2048 || w%8!=0 || h%8!=0)throw new InvalidOperationException("MiniMax 图片尺寸须为 512–2048 的 8 倍数像素；image-01-live 请使用宽高比。");payload["width"]=w;payload["height"]=h;}
                    else{string ratio=AspectRatio(size,SizeOptions(protocol));if(provider.Connection.Model=="image-01-live" && ratio=="21:9")throw new InvalidOperationException("MiniMax image-01-live 不支持 21:9 图片尺寸。");payload["aspect_ratio"]=ratio;}
                }
                if(reference!=null){if(reference.Length>10*1024*1024 || (referenceType!="image/png" && referenceType!="image/jpeg" && referenceType!="image/webp"))throw new InvalidOperationException("MiniMax 人物参考图须为 PNG、JPEG 或 WebP，且不超过 10 MB。");payload["subject_reference"]=new[]{new Dictionary<string,object>{{"type","character"},{"image_file",DataUri(reference,referenceType)}}};}
            }else if(protocol=="grok"){
                if(size.Length>0)payload["aspect_ratio"]=size=="auto"?"auto":AspectRatio(size,SizeOptions(protocol));
                if(quality.Length>0){if(quality!="auto" && quality!="low" && quality!="medium")throw new InvalidOperationException("Grok 图片质量仅支持 auto、low、medium 或服务默认。");if(provider.Connection.Model!="grok-imagine-image-2.0")throw new InvalidOperationException("所选 Grok 模型的图片质量参数未确认支持，请使用服务默认。");payload["quality"]=quality;}
                payload["n"]=1;if(reference!=null){suffix="/images/edits";payload["image"]=new Dictionary<string,object>{{"url",DataUri(reference,referenceType)},{"type","image_url"}};}
            }else throw new InvalidOperationException("不支持此图片协议，请在图片服务配置中选择受支持的协议。");
            // Reference uploads have their own 50 MB limit; JSON serializers default to a much smaller cap.
            if(content==null)content=new StringContent(new JavaScriptSerializer{MaxJsonLength=72*1024*1024,RecursionLimit=32}.Serialize(payload),Encoding.UTF8,"application/json");
            var request=new HttpRequestMessage(HttpMethod.Post,new Uri(endpoint.AbsoluteUri.TrimEnd('/')+suffix)){Content=content};
            string key=provider.Connection.ApiKey;
            if(!String.IsNullOrEmpty(provider.Connection.EncryptedKey) && key.Length==0){request.Dispose();throw new InvalidOperationException("图片服务密钥无法解密，请在模型配置中重新填写。");}
            try{if(key.Length>0){if(protocol=="gemini")request.Headers.Add("x-goog-api-key",key);else request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);}}
            catch(FormatException){request.Dispose();throw new InvalidOperationException("图片服务密钥格式无效，请重新填写。");}
            return request;
        }
        internal static ImageProtocolResult Parse(string protocol,Dictionary<string,object> response)
        {
            if(response==null || response.ContainsKey("error"))throw new InvalidDataException("图片服务返回错误或无效响应。");
            if(protocol=="gemini"){
                var feedback=Map(Value(response,"promptFeedback"));if(feedback!=null && !String.IsNullOrEmpty(Text(feedback,"blockReason")))throw new InvalidDataException("图片生成被服务安全策略阻止，请调整提示词或参考图。");
                foreach(object candidateValue in Array(Value(response,"candidates"))){var candidate=Map(candidateValue);string finish=Text(candidate,"finishReason");if(finish=="SAFETY" || finish=="IMAGE_SAFETY" || finish=="BLOCKLIST" || finish=="PROHIBITED_CONTENT")continue;
                    foreach(object partValue in Array(Value(Map(Value(candidate,"content")),"parts"))){var part=Map(partValue);object thought=Value(part,"thought");if(thought is bool && (bool)thought)continue;var inline=Map(Value(part,"inlineData"))??Map(Value(part,"inline_data"));if(inline==null)continue;
                        string data=Text(inline,"data"),mime=Text(inline,"mimeType");if(mime.Length==0)mime=Text(inline,"mime_type");if(data.Length>0){if(mime!="image/png" && mime!="image/jpeg" && mime!="image/gif" && mime!="image/webp")throw new InvalidDataException("Gemini 图片响应类型无效。");return new ImageProtocolResult{Base64=data,MediaType=mime};}
                    }
                }
                throw new InvalidDataException("Gemini 响应未返回图片，可能被安全策略阻止或所选模型不支持生图。");
            }
            if(protocol=="minimax"){
                var status=Map(Value(response,"base_resp"));object raw=Value(status,"status_code");int code;if(raw!=null && (!Int32.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),out code) || code!=0))throw new InvalidDataException("MiniMax 图片生成失败，请检查密钥、额度、提示词和人物参考图。");
                var data=Map(Value(response,"data"));string base64=FirstString(Value(data,"image_base64")),url=FirstString(Value(data,"image_urls"));if(base64.Length>0 || url.Length>0)return new ImageProtocolResult{Base64=base64,Url=url};
                throw new InvalidDataException("MiniMax 图片响应未返回可保存的图片。");
            }
            foreach(object item in Array(Value(response,"data"))){var entry=Map(item);string base64=Text(entry,"b64_json"),url=Text(entry,"url");if(base64.Length>0 || url.Length>0)return new ImageProtocolResult{Base64=base64,Url=url};break;}
            throw new InvalidDataException("图片服务响应没有有效图片。");
        }
        static object Value(Dictionary<string,object> map,string key){object value;return map!=null && map.TryGetValue(key,out value)?value:null;}
        static Dictionary<string,object> Map(object value){return value as Dictionary<string,object>;}
        static string Text(Dictionary<string,object> map,string key){return Value(map,key) as string??"";}
        static IEnumerable Array(object value){return value is string || !(value is IEnumerable)?new object[0]:(IEnumerable)value;}
        static string FirstString(object value){foreach(object item in Array(value))return item as string??"";return "";}
        static void Optional(Dictionary<string,object> payload,string key,string value){if(value.Length>0)payload[key]=value;}
        static string DataUri(byte[] bytes,string mediaType){return "data:"+mediaType+";base64,"+Convert.ToBase64String(bytes);}
        static void NoQuality(string quality){if(quality.Length>0)throw new InvalidOperationException("此图片协议不支持质量参数，请选择服务默认。");}
        static void Pixels(string value,out int width,out int height)
        {
            string[] parts=value.ToLowerInvariant().Split('x');width=height=0;if(parts.Length!=2 || !Int32.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out width) || !Int32.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out height) || width<=0 || height<=0)throw new InvalidOperationException("图片尺寸应为正整数 WxH，例如 1024x1024；或选择服务支持的宽高比。");
        }
        static string AspectRatio(string value,string[] choices)
        {
            int w,h;double width,height;if(value.IndexOf('x')>=0 || value.IndexOf('X')>=0){Pixels(value,out w,out h);width=w;height=h;}
            else{string[] parts=value.Split(':');if(parts.Length!=2 || !Double.TryParse(parts[0],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out width) || !Double.TryParse(parts[1],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out height) || width<=0 || height<=0)throw new InvalidOperationException("图片尺寸应为服务支持的宽高比，例如 16:9。");}
            foreach(string choice in choices){string[] parts=choice.Split(':');double a,b;if(parts.Length==2 && Double.TryParse(parts[0],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out a) && Double.TryParse(parts[1],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out b) && Math.Abs(width/height-a/b)<0.000000001)return choice;}
            throw new InvalidOperationException("所选图片尺寸宽高比不受此协议支持，请选择列表中的尺寸或使用服务默认。");
        }
        static void ValidateSeedreamSize(string size,string model)
        {
            if(size=="1K" || size=="1.5K" || size=="2K" || size=="3K" || size=="4K"){
                bool supported=true;if(model.Contains("seedream-4-5"))supported=size=="2K" || size=="4K";else if(model.Contains("seedream-4-0"))supported=size=="1K" || size=="2K" || size=="4K";else if(model.Contains("seedream-5-0-pro"))supported=size=="1K" || size=="1.5K" || size=="2K";
                if(!supported)throw new InvalidOperationException("所选 Seedream 模型不支持此图片尺寸档位，请选择该模型支持的尺寸。");return;
            }
            int width,height;Pixels(size,out width,out height);long pixels=(long)width*height;long minimum=model.Contains("seedream-4-5")?2560L*1440:512L*512;if(pixels<minimum || pixels>4096L*4096 || (double)width/height>16 || (double)height/width>16)throw new InvalidOperationException("Seedream 图片尺寸超出当前模型的总像素或宽高比限制，请选择支持的尺寸档位。");
        }
    }
}
