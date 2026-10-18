using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed class ApiException : Exception
    {
        public ApiException(string safeMessage) : base(safeMessage) { }
    }
    public interface IConversationApi : IDisposable
    {
        Task<string> ChatAsync(ConnectionSettings settings, IList<ChatMessage> messages, CancellationToken cancel);
        Task<string> TranscribeAsync(ConnectionSettings settings, byte[] wav, CancellationToken cancel);
        Task<byte[]> SpeechAsync(ConnectionSettings settings, string voice, string text, CancellationToken cancel);
    }
    public interface IStreamingConversationApi
    {
        Task<string> ChatStreamAsync(ConnectionSettings settings, IList<ChatMessage> messages, Action<string> onDelta, CancellationToken cancel);
    }
    public interface IReasoningConversationApi
    {
        Task<string> ChatWithReasoningAsync(ConnectionSettings settings,IList<ChatMessage> messages,Action<string> onDelta,Action<string> onReasoning,bool stream,CancellationToken cancel);
    }
    public sealed partial class ApiClient : IConversationApi, IStreamingConversationApi, IReasoningConversationApi
    {
        private readonly HttpClient http;
        private readonly TimeSpan requestTimeout;
        private const int MaxJsonBytes = 2 * 1024 * 1024;
        public ApiClient() : this(TimeSpan.FromSeconds(120)) { }
        public ApiClient(TimeSpan timeout)
        {
            if(timeout<=TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            requestTimeout=timeout;
#if !PORTABLE
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy=false });
            http.Timeout = Timeout.InfiniteTimeSpan;
        }
        public static bool IsMiMo(ConnectionSettings settings) { return settings.Protocol=="mimo" || ((String.IsNullOrEmpty(settings.Protocol) || settings.Protocol=="auto") && (settings.Model ?? "").StartsWith("mimo-",StringComparison.OrdinalIgnoreCase)); }
        public static string ChatPath(ConnectionSettings settings) { return settings.Protocol=="anthropic" ? "messages" : "chat/completions"; }
        public static string AsrPath(ConnectionSettings settings) { return IsMiMo(settings) ? "chat/completions" : "audio/transcriptions"; }
        public static string SpeechPath(ConnectionSettings settings) { return IsMiMo(settings) ? "chat/completions" : "audio/speech"; }
        public static Uri Endpoint(ConnectionSettings settings, string suffix)
        {
            if (settings == null || String.IsNullOrWhiteSpace(settings.Model)) throw new ApiException("请先在设置中填写服务地址和模型名称。");
            Uri uri;
            if (!Uri.TryCreate((settings.BaseUrl ?? "").Trim(), UriKind.Absolute, out uri) ||
                !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)) ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new ApiException("服务地址需要是 HTTPS；本机服务可以使用 http://127.0.0.1。地址中不要带密钥或查询参数。");
            string address = uri.AbsoluteUri.TrimEnd('/');
            foreach(string known in new[]{"chat/completions","audio/transcriptions","audio/speech","messages"})
                if(address.EndsWith("/"+known,StringComparison.OrdinalIgnoreCase)) { address=address.Substring(0,address.Length-known.Length-1); break; }
            if (!address.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase)) address += "/" + suffix;
            return new Uri(address);
        }
        public static void ValidateConnection(ConnectionSettings settings,string suffix)
        {
            Uri endpoint=Endpoint(settings,suffix); string key=settings.ApiKey;
            if(key.Length==0 && !endpoint.IsLoopback) throw new ApiException("请在设置中填写此服务的 API 密钥。");
            if(key.IndexOfAny(new char[]{'\r','\n'})>=0) throw new ApiException("API 密钥中有换行，请重新粘贴完整密钥。");
        }
        private static HttpRequestMessage Request(ConnectionSettings settings, string suffix, HttpContent content)
        {
            ValidateConnection(settings,suffix);
            Uri endpoint = Endpoint(settings, suffix);
            string key = settings.ApiKey;
            if (key.Length == 0 && !endpoint.IsLoopback) throw new ApiException("请在设置中填写此服务的 API 密钥。");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if(settings.Protocol=="anthropic")
            { if(key.Length>0) request.Headers.Add("x-api-key",key); request.Headers.Add("anthropic-version","2023-06-01"); }
            else if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = content; return request;
        }
        private static StringContent Json(object value)
        {
            return new StringContent(new JavaScriptSerializer { MaxJsonLength = 32*1024*1024 }.Serialize(value), Encoding.UTF8, "application/json");
        }
        public async Task<IList<string>> ListModelsAsync(ConnectionSettings settings,CancellationToken cancel,Dictionary<string,int> contextWindows=null,Dictionary<string,ModelCapabilitySetting> capabilities=null)
        {
            // Listing models must work before the user has selected a model.
            var probe=settings.Copy();probe.Model="model-catalog";
            var models=new SortedSet<string>(StringComparer.Ordinal);var cursors=new HashSet<string>();string after=null;
            using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                bounded.CancelAfter(requestTimeout);
                try
                {
                    for(int page=0;page<10;page++)
                    {
                        using(var request=Request(probe,"models",null))
                        {
                            request.Method=HttpMethod.Get;
                            if(after!=null)request.RequestUri=new Uri(request.RequestUri.AbsoluteUri+"?after_id="+Uri.EscapeDataString(after));
                            byte[] bytes=await SendAsync(probe,request,MaxJsonBytes,bounded.Token).ConfigureAwait(false);
                            try
                            {
                                var json=ObjectJson(bytes);object raw;
                                if(!json.TryGetValue("data",out raw) || !(raw is object[]))throw new FormatException();
                                foreach(object entry in (object[])raw)
                                {
                                    var item=entry as Dictionary<string,object>;object value;
                                    if(item==null || !item.TryGetValue("id",out value) || !(value is string))continue;
                                    string id=((string)value).Trim();
                                    if(id.Length==0 || id.Length>256 || id.IndexOfAny(new[]{'\r','\n','\t'})>=0)continue;
                                    if(contextWindows!=null){int capacity=ModelContextCapacity.Read(item);if(capacity>0)contextWindows[id]=capacity;}
                                    if(capabilities!=null)capabilities[id]=ModelCapabilitySetting.Read(item);
                                    models.Add(id);if(models.Count>=1000)return new List<string>(models);
                                }
                                if(!json.TryGetValue("has_more",out raw) || !Object.Equals(raw,true))return new List<string>(models);
                                if(!json.TryGetValue("last_id",out raw) || !(raw is string) || String.IsNullOrWhiteSpace((string)raw) || !cursors.Add((string)raw))throw new FormatException();
                                after=(string)raw;
                            }
                            catch(Exception e){if(!Malformed(e))throw;throw new ApiException("此服务没有返回兼容的模型列表，可继续手动添加模型。");}
                        }
                    }
                    throw new ApiException("模型列表分页过多，请缩小服务端可用模型范围后重试，或手动添加。");
                }
                catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;throw new ApiException("获取模型列表超时，可点击刷新重试或手动添加。");}
            }
        }
        internal static Dictionary<string,object> PlainRequestBody(ConnectionSettings settings,IList<object> messages,string system,bool stream)
        {
            var body=new Dictionary<string,object>{{"model",settings.Model},{"messages",messages},{"stream",stream}};
            if(stream && settings.Protocol!="anthropic")body["stream_options"]=new {include_usage=true};
            if(settings.Protocol=="anthropic"){body["system"]=system;body["max_tokens"]=AgentContextPolicy.Resolve(ModelContextCapacity.Budget(settings,settings.RequestContextCapacity>0?settings.RequestContextCapacity:8192)).OutputReserve;}return body;
        }
        public async Task<string> ChatAsync(ConnectionSettings settings, IList<ChatMessage> messages, CancellationToken cancel)
        { return await ChatReplyAsync(settings,messages,null,cancel).ConfigureAwait(false); }
        public Task<string> ChatWithReasoningAsync(ConnectionSettings settings,IList<ChatMessage> messages,Action<string> onDelta,Action<string> onReasoning,bool stream,CancellationToken cancel)
        { return stream?ReadChatStreamAsync(settings,messages,onDelta,cancel,onReasoning):ChatReplyAsync(settings,messages,onReasoning,cancel); }
        private async Task<string> ChatReplyAsync(ConnectionSettings settings, IList<ChatMessage> messages, Action<string> onReasoning, CancellationToken cancel)
        {
            var serialized = new List<object>();
            string system=""; bool anthropic=settings.Protocol=="anthropic";
            foreach (ChatMessage message in messages)
            { if(anthropic && message.Role=="system") system+=message.Content+"\n"; else serialized.Add(new { role=message.Role,content=ChatAttachment.Content(message,anthropic) }); }
            object body=PlainRequestBody(settings,serialized,system.Trim(),false);
            if(!AgentContextBudget.FitsWireBytes(body))throw new ApiException("对话请求超过附件或上下文字节预算，请减少附件或压缩历史。");
            using (var request = Request(settings, ChatPath(settings), Json(body)))
            using(var usage=UsageTracking.BeginRequest(settings,cancel))
            {
                byte[] bytes = await SendAsync(settings,request, MaxJsonBytes, cancel).ConfigureAwait(false);
                string result=ParseChatReply(bytes, anthropic,onReasoning);usage.Complete();return result;
            }
        }
        internal static string ParseChatReply(byte[] bytes, bool anthropic,Action<string> onReasoning=null)
        {
            try
            {
                var json = ObjectJson(bytes); UsageTracking.Observe(json,anthropic);string content;var reasoning=new StringBuilder();
                if(anthropic)
                {
                    var text = new StringBuilder();
                    foreach(object block in (object[])json["content"])
                    { var item = (Dictionary<string,object>)block; if((item["type"] as string) == "text") text.Append(item["text"] as string);else if((item["type"] as string)=="thinking")reasoning.Append(item["thinking"] as string); }
                    content = text.ToString();
                }
                else {var message=ChoiceMessage(json);content=message["content"] as string;object value;if(message.TryGetValue("reasoning_content",out value) || message.TryGetValue("reasoning",out value))reasoning.Append(value as string);}
                if(String.IsNullOrWhiteSpace(content) || content.Length > 50000) throw new FormatException();
                if(reasoning.Length>50000)throw new FormatException();
                if(onReasoning!=null && reasoning.Length>0)onReasoning(reasoning.ToString());
                return content.Trim();
            }
            catch(Exception e)
            {
                if(!Malformed(e)) throw;
                throw new ApiException("模型没有返回可显示的文字，请检查模型名称与接口兼容性。");
            }
        }
        public async Task<string> ChatStreamAsync(ConnectionSettings settings, IList<ChatMessage> messages, Action<string> onDelta, CancellationToken cancel)
        { return await ReadChatStreamAsync(settings,messages,onDelta,cancel,null).ConfigureAwait(false); }
        private async Task<string> ReadChatStreamAsync(ConnectionSettings settings, IList<ChatMessage> messages, Action<string> onDelta, CancellationToken cancel,Action<string> onReasoning)
        {
            var serialized = new List<object>(); string system = ""; bool anthropic = settings.Protocol == "anthropic";
            foreach(ChatMessage message in messages)
            { if(anthropic && message.Role == "system") system += message.Content + "\n"; else serialized.Add(new { role = message.Role, content = ChatAttachment.Content(message,anthropic) }); }
            object body=PlainRequestBody(settings,serialized,system.Trim(),true);
            if(!AgentContextBudget.FitsWireBytes(body))throw new ApiException("对话请求超过附件或上下文字节预算，请减少附件或压缩历史。");
            using(var request = Request(settings, ChatPath(settings), Json(body)))
            using(var usage=UsageTracking.BeginRequest(settings,cancel))
            using(var proxyHttp = settings.UseProxy ? ProxyConfiguration.CreateHttpClient(settings.ProxyAddress, false) : null)
            using(var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                request.Headers.Accept.Clear(); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                bounded.CancelAfter(requestTimeout); CancellationToken token = bounded.Token;
                try
                {
                    using(var response = await (settings.UseProxy ? proxyHttp : http).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    using(token.Register(response.Dispose))
                    {
                        CheckResponse(response, true);
                        if(response.Content.Headers.ContentLength > MaxJsonBytes) throw new ApiException("服务返回的内容过大，请缩短本次请求。");
                        using(Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {string result=await ChatStream.ReadAsync(stream, anthropic, onDelta, token, MaxJsonBytes,onReasoning).ConfigureAwait(false);usage.Complete();return result;}
                    }
                }
                catch(OperationCanceledException)
                { if(cancel.IsCancellationRequested) throw; throw new ApiException("请求超时，可以重试或缩短本次提问。"); }
                catch(Exception e)
                {
                    if(!(e is HttpRequestException || e is IOException || e is ObjectDisposedException)) throw;
                    if(cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
                    if(token.IsCancellationRequested) throw new ApiException("请求超时，可以重试或缩短本次提问。");
                    throw new ApiException(e is HttpRequestException ? (settings.UseProxy ? "无法通过已配置的代理连接服务，请检查代理地址及代理是否运行。" : "无法连接此服务，请检查地址及网络；需要代理时请在服务设置中开启。") : "连接中断，请稍后重试。");
                }
            }
        }
        private static void CheckResponse(HttpResponseMessage response, bool streaming)
        {
            if(response.IsSuccessStatusCode) return;
            int code = (int)response.StatusCode;
            string detail = code == 401 || code == 403 ? "请检查密钥与服务权限。" :
                code == 429 ? "请求太频繁或额度不足，请稍后重试。" :
                code >= 300 && code < 400 ? "服务发生重定向，请填写最终接口地址。" :
                code == 404 ? "请检查服务地址及模型名称。" :
                streaming ? "服务可能不支持流式回复，请在文字服务设置中关闭流式回复后重试。" : "服务暂时无法完成请求，请检查配置后重试。";
            throw new ApiException("服务返回 HTTP " + code + "。" + detail);
        }
        public async Task<string> TranscribeAsync(ConnectionSettings settings, byte[] wav, CancellationToken cancel)
        {
            if (!WaveData.IsWave(wav)) throw new ApiException("录音文件不完整，请重新录音。");
            if(wav.Length>10*1024*1024) throw new ApiException("录音超过 10 MB，请缩短录音。");
            if(IsMiMo(settings))
            {
                using(var request=Request(settings,"chat/completions",Json(new {model=settings.Model,messages=new[]{new {role="user",content=new[]{new {type="input_audio",input_audio=new {data="data:audio/wav;base64,"+Convert.ToBase64String(wav)}}}}},asr_options=new {language="auto"},stream=false})))
                {
                    byte[] bytes=await SendAsync(settings,request,MaxJsonBytes,cancel).ConfigureAwait(false);
                    try { return RecognizedText(ChoiceMessage(ObjectJson(bytes))["content"] as string); }
                    catch(Exception e) { if(!Malformed(e)) throw; throw new ApiException("MiMo 语音识别没有返回文字，请检查 ASR 模型与当前服务权限。"); }
                }
            }
            using (var multipart = new MultipartFormDataContent())
            {
                multipart.Add(new StringContent(settings.Model ?? ""), "model");
                multipart.Add(new StringContent("json"), "response_format");
                var file = new ByteArrayContent(wav); file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                multipart.Add(file, "file", "speech.wav");
                using (var request = Request(settings, "audio/transcriptions", multipart))
                {
                    byte[] bytes = await SendAsync(settings,request, MaxJsonBytes, cancel).ConfigureAwait(false);
                    try
                    {
                        return RecognizedText(ObjectJson(bytes)["text"] as string);
                    }
                    catch (Exception e)
                    {
                        if (!Malformed(e)) throw;
                        throw new ApiException("没有识别到文字，请再说一次，或检查语音识别模型。");
                    }
                }
            }
        }
        public async Task<byte[]> SpeechAsync(ConnectionSettings settings, string voice, string text, CancellationToken cancel)
        {
            if (String.IsNullOrWhiteSpace(voice)) throw new ApiException("请在设置中填写语音合成的音色名称。");
            if(IsMiMo(settings))
            {
                using(var request=Request(settings,"chat/completions",Json(new {model=settings.Model,messages=new[]{new {role="assistant",content=text}},audio=new {format="wav",voice=voice.Trim()},stream=false})))
                {
                    byte[] bytes=await SendAsync(settings,request,32*1024*1024,cancel).ConfigureAwait(false);
                    try
                    {
                        var audio=(Dictionary<string,object>)ChoiceMessage(ObjectJson(bytes))["audio"];
                        string data=audio["data"] as string; if(String.IsNullOrEmpty(data) || data.Length>28*1024*1024) throw new FormatException();
                        byte[] wav=Convert.FromBase64String(data); if(!WaveData.IsWave(wav) || wav.Length>20*1024*1024) throw new FormatException(); return wav;
                    }
                    catch(Exception e) { if(!Malformed(e)) throw; throw new ApiException("MiMo 语音合成没有返回 WAV 音频，请检查 TTS 模型、音色与服务权限。"); }
                }
            }
            using (var request = Request(settings, "audio/speech", Json(new { model = settings.Model, voice = voice.Trim(), input = text, response_format = "wav" })))
            {
                request.Headers.Accept.Clear();
                byte[] bytes = await SendAsync(settings,request, 20 * 1024 * 1024, cancel).ConfigureAwait(false);
                if (!WaveData.IsWave(bytes)) throw new ApiException("语音服务没有返回 WAV 音频，请检查语音合成设置。");
                return bytes;
            }
        }
        private static Dictionary<string, object> ObjectJson(byte[] bytes)
        {
            var result=new JavaScriptSerializer { MaxJsonLength = 32*1024*1024 }.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string,object>;
            if(result==null) throw new FormatException(); return result;
        }
        private static Dictionary<string,object> ChoiceMessage(Dictionary<string,object> json)
        { return (Dictionary<string,object>)((Dictionary<string,object>)((object[])json["choices"])[0])["message"]; }
        private static string RecognizedText(string text) { if(String.IsNullOrWhiteSpace(text) || text.Length>8000) throw new FormatException(); return text.Trim(); }
        private static bool Malformed(Exception e) { return e is ArgumentException || e is InvalidCastException || e is KeyNotFoundException || e is FormatException || e is IndexOutOfRangeException || e is NullReferenceException; }
        private async Task<byte[]> SendAsync(ConnectionSettings settings,HttpRequestMessage request, int maximum, CancellationToken cancel)
        {
            using(var proxyHttp=settings.UseProxy?ProxyConfiguration.CreateHttpClient(settings.ProxyAddress,false):null)
            using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
            bounded.CancelAfter(requestTimeout); CancellationToken token=bounded.Token;
            try
            {
                using (HttpResponseMessage response = await (settings.UseProxy ? proxyHttp : http).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                using(token.Register(response.Dispose))
                {
                    CheckResponse(response, false);
                    if (response.Content.Headers.ContentLength > maximum) throw new ApiException("服务返回的内容过大，请缩短本次请求。");
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        byte[] block = new byte[8192]; int read;
                        while ((read = await stream.ReadAsync(block, 0, block.Length, token).ConfigureAwait(false)) > 0)
                        {
                            if (buffer.Length + read > maximum) throw new ApiException("服务返回的内容过大，请缩短本次请求。");
                            buffer.Write(block, 0, read);
                        }
                        token.ThrowIfCancellationRequested(); return buffer.ToArray();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancel.IsCancellationRequested) throw;
                throw new ApiException("请求超时，可以重试或缩短本次提问。");
            }
            catch (Exception e)
            {
                if(!(e is HttpRequestException || e is IOException || e is ObjectDisposedException)) throw;
                if(cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
                if(token.IsCancellationRequested) throw new ApiException("请求超时，可以重试或缩短本次提问。");
                throw new ApiException(e is HttpRequestException ? (settings.UseProxy ? "无法通过已配置的代理连接服务，请检查代理地址及代理是否运行。" : "无法连接此服务，请检查地址及网络；需要代理时请在服务设置中开启。") : "连接中断，请稍后重试。");
            }
            }
        }
        public void Dispose() { http.Dispose(); }
    }
}
