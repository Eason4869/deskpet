using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public interface ILiveSocket : IDisposable
    {
        Task ConnectAsync(ConnectionSettings settings,CancellationToken token);
        Task SendTextAsync(string text,CancellationToken token);
        Task<string> ReceiveTextAsync(CancellationToken token);
        void Abort();
    }
    public sealed class GeminiLiveSocket : ILiveSocket
    {
        readonly ClientWebSocket socket=new ClientWebSocket(); readonly SemaphoreSlim sendGate=new SemaphoreSlim(1,1);
        public static Uri Endpoint(ConnectionSettings settings)
        {
            Uri uri;
            if(settings==null || String.IsNullOrWhiteSpace(settings.Model) || !Uri.TryCreate(settings.BaseUrl,UriKind.Absolute,out uri) || !(uri.Scheme=="wss" || (uri.Scheme=="ws" && uri.IsLoopback)) || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0) throw new ApiException("Live 需要完整的 WSS 地址与模型；本机测试可使用 ws://127.0.0.1。地址不要带密钥。");
            string key=settings.ApiKey;if(key.Length==0 && !uri.IsLoopback)throw new ApiException("请填写 Gemini Live 的 API 密钥。");
            if(key.IndexOfAny(new[]{'\r','\n'})>=0)throw new ApiException("Live 密钥含换行，请重新粘贴。");
            return new Uri(uri.AbsoluteUri+(key.Length==0 ? "" : "?key="+Uri.EscapeDataString(key)));
        }
        public async Task ConnectAsync(ConnectionSettings settings,CancellationToken token)
        {socket.Options.Proxy=settings.UseProxy ? new WebProxy(ProxyConfiguration.Endpoint(settings.ProxyAddress),false) : null;socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(20);await socket.ConnectAsync(Endpoint(settings),token).ConfigureAwait(false);}
        public async Task SendTextAsync(string text,CancellationToken token)
        {
            await sendGate.WaitAsync(token).ConfigureAwait(false);
            try{byte[] bytes=Encoding.UTF8.GetBytes(text);await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,token).ConfigureAwait(false);}finally{sendGate.Release();}
        }
        public async Task<string> ReceiveTextAsync(CancellationToken token)
        {
            using(var message=new MemoryStream())
            {
                byte[] buffer=new byte[8192];WebSocketReceiveResult result;
                // Gemini sends JSON in binary WebSocket messages as well as text messages.
                // Assemble all fragments before decoding UTF-8; PCM remains base64 inside JSON.
                do{result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),token).ConfigureAwait(false);if(result.MessageType==WebSocketMessageType.Close)throw new ApiException("Live 连接已关闭，请检查模型权限后重新连接。");if((result.MessageType!=WebSocketMessageType.Text && result.MessageType!=WebSocketMessageType.Binary) || message.Length+result.Count>2*1024*1024)throw new ApiException("Live 返回的消息格式不受支持或内容过大。");message.Write(buffer,0,result.Count);}while(!result.EndOfMessage);
                return Encoding.UTF8.GetString(message.ToArray());
            }
        }
        public void Abort(){socket.Abort();} public void Dispose(){socket.Dispose();sendGate.Dispose();}
    }
    public sealed class GeminiLiveSession : IDisposable
    {
        readonly Func<ILiveSocket> sockets;readonly Func<IPcmMicrophone> microphones;readonly Func<ILiveAudioOutput> speakers;readonly object gate=new object();
        CancellationTokenSource cancellation;ILiveSocket socket;IPcmMicrophone microphone;ILiveAudioOutput speaker;Task running;TaskCompletionSource<bool> configured;bool disposed;int volume;
        int successfulConnections;
        internal int SuccessfulConnections {get{return Volatile.Read(ref successfulConnections);}}
        public bool IsActive {get{lock(gate)return running!=null;}} public string Status {get;private set;}
        public event Action StateChanged;public event Action<string,string> PartialTranscript;public event Action<string,string> TurnCompleted;
        public event Action<string,string,string> TurnCommitted;
        double inputLevel;long inputAt;
        public double InputLevel {get{return DateTime.UtcNow.Ticks-Interlocked.Read(ref inputAt)>TimeSpan.TicksPerSecond/3?0:Volatile.Read(ref inputLevel);}}
        public double OutputLevel {get{lock(gate){var meter=speaker as IAudioLevelSource;return meter==null?0:meter.AudioLevel;}}}
#if !PORTABLE
        public GeminiLiveSession() : this(()=>new GeminiLiveSocket(),()=>new AudioRecorder(true),()=>new PcmOutput()){}
#endif
        public GeminiLiveSession(Func<ILiveSocket> transport,Func<IPcmMicrophone> capture,Func<ILiveAudioOutput> output){sockets=transport;microphones=capture;speakers=output;Status="Live 尚未连接。";}
        public async Task StartAsync(PetSettings settings,string persona)
        {
            GeminiLiveSocket.Endpoint(settings.Live);
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("GeminiLiveSession");if(running!=null)throw new InvalidOperationException("Live 已在连接或对话中。");
                cancellation=new CancellationTokenSource();configured=new TaskCompletionSource<bool>();volume=settings.Volume;
                var snapshot=settings.Copy();running=Task.Run(()=>Run(snapshot,persona,cancellation.Token));
            }
            Task timeout=Task.Delay(TimeSpan.FromSeconds(25));Task first=await Task.WhenAny(configured.Task,timeout);
            if(first==timeout){await StopAsync();throw new ApiException("Live 初始化超时，请检查地址、模型及代理。");}
            Exception failure=null;try{await configured.Task;}catch(Exception e){failure=e;}
            if(failure!=null){await StopAsync();throw failure;}
        }
        void SetStatus(string status){Status=status;if(!disposed && StateChanged!=null)StateChanged();}
        static string Json(object value){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Serialize(value);}
        async Task Run(PetSettings settings,string persona,CancellationToken token)
        {
            Task sending=null;var queue=new BlockingCollection<byte[]>(32);string user="",reply="";
            try
            {
                SetStatus("正在连接 Live…");socket=sockets();
                using(var connect=CancellationTokenSource.CreateLinkedTokenSource(token)){connect.CancelAfter(TimeSpan.FromSeconds(20));await socket.ConnectAsync(settings.Live,connect.Token).ConfigureAwait(false);}
                string model=settings.Live.Model.StartsWith("models/",StringComparison.Ordinal) ? settings.Live.Model : "models/"+settings.Live.Model;
                await socket.SendTextAsync(Json(new {setup=new {model=model,generationConfig=new {responseModalities=new[]{"AUDIO"},speechConfig=new {voiceConfig=new {prebuiltVoiceConfig=new {voiceName=settings.LiveVoice}}}},systemInstruction=new {parts=new[]{new {text=persona}}},inputAudioTranscription=new {},outputAudioTranscription=new {}}}),token).ConfigureAwait(false);
                bool ready=false;
                while(!token.IsCancellationRequested)
                {
                    string message=await socket.ReceiveTextAsync(token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
                    var json=new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.DeserializeObject(message) as Dictionary<string,object>;
                    if(json==null)throw new ApiException("Live 返回了无法解析的消息。");
                    if(json.ContainsKey("error"))throw new ApiException("Live 服务拒绝请求，请检查密钥、模型名称与 Live 权限。");
                    if(json.ContainsKey("setupComplete") && !ready)
                    {
                        ready=true;speaker=speakers();speaker.SetVolume(volume);microphone=microphones();
                        var failures=speaker as ILiveAudioFailureSource;
                        if(failures!=null)failures.Failed+=delegate(string failure){if(token.IsCancellationRequested)return;SetStatus(failure);cancellation.Cancel();socket.Abort();};
                        microphone.PcmAvailable+=delegate(byte[] bytes){if(token.IsCancellationRequested)return;Volatile.Write(ref inputLevel,AudioLevel.Pcm16(bytes));Interlocked.Exchange(ref inputAt,DateTime.UtcNow.Ticks);if(!queue.TryAdd(bytes)){SetStatus("Live 上传缓冲已满，请检查网络后重连。");cancellation.Cancel();socket.Abort();}};
                        microphone.Failed+=delegate(string failure){SetStatus(failure);cancellation.Cancel();socket.Abort();};
                        sending=Task.Run(async delegate {try{foreach(byte[] bytes in queue.GetConsumingEnumerable(token))await socket.SendTextAsync(Json(new {realtimeInput=new {audio=new {data=Convert.ToBase64String(bytes),mimeType="audio/pcm;rate=16000"}}}),token).ConfigureAwait(false);}catch(OperationCanceledException){}catch(Exception){SetStatus("Live 音频上传中断，请检查网络或代理。");cancellation.Cancel();socket.Abort();}});
                        microphone.Start();Interlocked.Increment(ref successfulConnections);SetStatus("Live 已连接 · 正在听 · 再次点击结束");configured.TrySetResult(true);
                    }
                    object raw;if(!json.TryGetValue("serverContent",out raw))continue;
                    if(!ready)throw new ApiException("Live 未完成初始化便返回音频，请检查服务协议。");
                    var content=(Dictionary<string,object>)raw;
                    bool interrupted=content.ContainsKey("interrupted") && Object.Equals(content["interrupted"],true);
                    user=Transcript(content,"inputTranscription",user,"user");reply=Transcript(content,"outputTranscription",reply,"assistant");
                    if(interrupted){speaker.Interrupt();Commit(user,reply,"interrupted");user=reply="";}
                    if(!interrupted && content.TryGetValue("modelTurn",out raw))
                    {
                        var turn=(Dictionary<string,object>)raw;object parts;
                        if(turn.TryGetValue("parts",out parts))foreach(object item in (object[])parts)
                        {
                            var part=(Dictionary<string,object>)item;object inline;
                            if(part.TryGetValue("inlineData",out inline))
                            {
                                var audio=(Dictionary<string,object>)inline;string mime=audio["mimeType"] as string;int rate=24000;
                                if(mime==null || !mime.StartsWith("audio/pcm",StringComparison.OrdinalIgnoreCase))throw new ApiException("Live 返回了不支持的音频类型。");
                                int marker=mime.IndexOf("rate=",StringComparison.OrdinalIgnoreCase);if(marker>=0 && !Int32.TryParse(mime.Substring(marker+5).Split(';')[0],out rate))throw new ApiException("Live 音频采样率无效。");
                                speaker.Enqueue(Convert.FromBase64String((string)audio["data"]),rate);
                            }
                        }
                    }
                    if(!interrupted && content.ContainsKey("turnComplete") && Object.Equals(content["turnComplete"],true)){Commit(user,reply,"complete");user=reply="";SetStatus("Live 已连接 · 正在听 · 再次点击结束");}
                }
            }
            catch(OperationCanceledException){configured.TrySetCanceled();}
            catch(Exception e)
            {
                if(token.IsCancellationRequested)configured.TrySetCanceled();
                else{string safe=e is ApiException || e is InvalidOperationException ? e.Message : "Live 连接或音频处理失败，请检查 WSS 地址、模型、密钥与代理。";SetStatus(safe);configured.TrySetException(new ApiException(safe));}
            }
            finally
            {
                Commit(user,reply,token.IsCancellationRequested?"cancelled":"error");
                if(cancellation!=null)cancellation.Cancel();if(socket!=null)socket.Abort();
                // Dispose the native recorder off its callback thread before completing its queue.
                if(microphone!=null)microphone.Dispose();queue.CompleteAdding();
            }
            if(sending!=null)await sending.ConfigureAwait(false);queue.Dispose();
            try
            {
                if(speaker!=null)speaker.Dispose();if(socket!=null)socket.Dispose();
            }
            finally
            {
                lock(gate){running=null;microphone=null;speaker=null;socket=null;Volatile.Write(ref inputLevel,0);if(cancellation!=null)cancellation.Dispose();cancellation=null;}
                if(!disposed && StateChanged!=null)StateChanged();
            }
        }
        string Transcript(Dictionary<string,object> content,string field,string current,string role)
        {
            object raw;if(!content.TryGetValue(field,out raw))return current;var data=(Dictionary<string,object>)raw;object text;
            if(data.TryGetValue("text",out text) && text is string){current+=(string)text;if(current.Length>50000)throw new ApiException("Live 字幕过长，请重新连接。");if(!disposed && PartialTranscript!=null)PartialTranscript(role,current);}return current;
        }
        void Commit(string user,string reply,string status)
        {
            if(disposed || (user.Length==0 && reply.Length==0))return;
            if(TurnCommitted!=null)TurnCommitted(user,reply,status);
            // Existing consumers receive only successful turns; incomplete subtitles use the status event.
            if(status=="complete" && TurnCompleted!=null)TurnCompleted(user,reply);
        }
        public void SetVolume(int value){lock(gate){volume=Math.Max(0,Math.Min(100,value));if(speaker!=null)speaker.SetVolume(volume);}}
        public async Task StopAsync()
        {
            Task task;lock(gate){task=running;if(task==null)return;cancellation.Cancel();if(socket!=null)socket.Abort();}
            await task.ConfigureAwait(false);SetStatus("Live 已结束，麦克风已关闭。");
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;if(cancellation!=null)cancellation.Cancel();if(socket!=null)socket.Abort();}}
    }
}
