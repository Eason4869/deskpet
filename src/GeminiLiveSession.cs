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
            if(settings==null || String.IsNullOrWhiteSpace(settings.Model) || !Uri.TryCreate(settings.BaseUrl,UriKind.Absolute,out uri) || !(uri.Scheme=="wss" || (uri.Scheme=="ws" && uri.IsLoopback)) || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0) throw new ApiException(TextResources.Get("geminiLiveSession.endpoint.text"));
            string key=settings.ApiKey;if(key.Length==0 && !uri.IsLoopback)throw new ApiException(TextResources.Get("geminiLiveSession.endpoint.text.alternativeA"));
            if(key.IndexOfAny(new[]{'\r','\n'})>=0)throw new ApiException(TextResources.Get("geminiLiveSession.endpoint.text.alternativeB"));
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
                do{result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),token).ConfigureAwait(false);if(result.MessageType==WebSocketMessageType.Close)throw new ApiException(TextResources.Get("geminiLiveSession.receiveTextAsync.text"));if((result.MessageType!=WebSocketMessageType.Text && result.MessageType!=WebSocketMessageType.Binary) || message.Length+result.Count>2*1024*1024)throw new ApiException(TextResources.Get("geminiLiveSession.receiveTextAsync.text.alternativeA"));message.Write(buffer,0,result.Count);}while(!result.EndOfMessage);
                return Encoding.UTF8.GetString(message.ToArray());
            }
        }
        public void Abort(){socket.Abort();} public void Dispose(){socket.Dispose();sendGate.Dispose();}
    }
    public sealed class GeminiLiveSession : IDisposable
    {
        public Dafeiyu.Maintenance.DataMaintenanceGate Maintenance {get;private set;}
        readonly Func<ILiveSocket> sockets;readonly Func<IPcmMicrophone> microphones;readonly Func<ILiveAudioOutput> speakers;readonly object gate=new object();
        CancellationTokenSource cancellation;ILiveSocket socket;IPcmMicrophone microphone;ILiveAudioOutput speaker;Task running;TaskCompletionSource<bool> configured;bool disposed;int volume;
        int successfulConnections;
        internal int SuccessfulConnections {get{return Volatile.Read(ref successfulConnections);}}
        public bool IsActive {get{lock(gate)return running!=null;}} public string Status {get;private set;}
        public event Action StateChanged;public event Action<string,string> PartialTranscript;public event Action<string,string> TurnCompleted;
        public event Action<string,string,string> TurnCommitted;
        public Func<string> RelationshipCompanion {get;set;}
        public event Action<RelationshipLiveTurn> LogicalTurnCommitted;
        double inputLevel;long inputAt;
        public double InputLevel {get{return DateTime.UtcNow.Ticks-Interlocked.Read(ref inputAt)>TimeSpan.TicksPerSecond/3?0:Volatile.Read(ref inputLevel);}}
        public double OutputLevel {get{lock(gate){var meter=speaker as IAudioLevelSource;return meter==null?0:meter.AudioLevel;}}}
#if !PORTABLE
        public GeminiLiveSession() : this(null){}
        public GeminiLiveSession(Dafeiyu.Maintenance.DataMaintenanceGate maintenance) : this(()=>new GeminiLiveSocket(),()=>new AudioRecorder(true),()=>new PcmOutput(),maintenance){}
#endif
        public GeminiLiveSession(Func<ILiveSocket> transport,Func<IPcmMicrophone> capture,Func<ILiveAudioOutput> output,Dafeiyu.Maintenance.DataMaintenanceGate maintenance=null){Maintenance=maintenance??new Dafeiyu.Maintenance.DataMaintenanceGate();sockets=transport;microphones=capture;speakers=output;Status=TextResources.Get("geminiLiveSession.geminiLiveSession.status");}
        public async Task StartAsync(PetSettings settings,string persona)
        {
            GeminiLiveSocket.Endpoint(settings.Live);
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("GeminiLiveSession");if(running!=null)throw new InvalidOperationException(TextResources.Get("geminiLiveSession.startAsync.text"));
                var admission=Maintenance.EnterActivity();try{
                cancellation=new CancellationTokenSource();configured=new TaskCompletionSource<bool>();volume=settings.Volume;
                var snapshot=settings.Copy();string prompt=ReplyLanguagePolicy.Apply(PersonaProfiles.LocalizeDefaultPrompt(persona));running=Task.Run(async()=>{try{await Run(snapshot,prompt,cancellation.Token).ConfigureAwait(false);}finally{admission.Dispose();}});
                }catch{admission.Dispose();throw;}
            }
            Task timeout=Task.Delay(TimeSpan.FromSeconds(25));Task first=await Task.WhenAny(configured.Task,timeout);
            if(first==timeout){await StopAsync();throw new ApiException(TextResources.Get("geminiLiveSession.startAsync.text.alternativeA"));}
            Exception failure=null;try{await configured.Task;}catch(Exception e){failure=e;}
            if(failure!=null){await StopAsync();throw failure;}
        }
        void SetStatus(string status){Status=status;if(!disposed && StateChanged!=null)StateChanged();}
        static string Json(object value){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Serialize(value);}
        async Task Run(PetSettings settings,string persona,CancellationToken token)
        {
            Task sending=null;var queue=new BlockingCollection<byte[]>(32);string user="",reply="",logicalTurnId=null,companionId=null;
            try
            {
                SetStatus(TextResources.Get("geminiLiveSession.run.setStatus"));socket=sockets();
                using(var connect=CancellationTokenSource.CreateLinkedTokenSource(token)){connect.CancelAfter(TimeSpan.FromSeconds(20));await socket.ConnectAsync(settings.Live,connect.Token).ConfigureAwait(false);}
                string model=settings.Live.Model.StartsWith("models/",StringComparison.Ordinal) ? settings.Live.Model : "models/"+settings.Live.Model;
                await socket.SendTextAsync(Json(new {setup=new {model=model,generationConfig=new {responseModalities=new[]{"AUDIO"},speechConfig=new {voiceConfig=new {prebuiltVoiceConfig=new {voiceName=settings.LiveVoice}}}},systemInstruction=new {parts=new[]{new {text=persona}}},inputAudioTranscription=new {},outputAudioTranscription=new {}}}),token).ConfigureAwait(false);
                bool ready=false;
                while(!token.IsCancellationRequested)
                {
                    string message=await socket.ReceiveTextAsync(token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
                    var json=new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.DeserializeObject(message) as Dictionary<string,object>;
                    if(json==null)throw new ApiException(TextResources.Get("geminiLiveSession.run.text"));
                    if(json.ContainsKey("error"))throw new ApiException(TextResources.Get("geminiLiveSession.run.text.alternativeA"));
                    if(json.ContainsKey("setupComplete") && !ready)
                    {
                        ready=true;speaker=speakers();speaker.SetVolume(volume);microphone=microphones();
                        var failures=speaker as ILiveAudioFailureSource;
                        if(failures!=null)failures.Failed+=delegate(string failure){if(token.IsCancellationRequested)return;SetStatus(failure);cancellation.Cancel();socket.Abort();};
                        microphone.PcmAvailable+=delegate(byte[] bytes){if(token.IsCancellationRequested)return;Volatile.Write(ref inputLevel,AudioLevel.Pcm16(bytes));Interlocked.Exchange(ref inputAt,DateTime.UtcNow.Ticks);if(!queue.TryAdd(bytes)){SetStatus(TextResources.Get("geminiLiveSession.run.setStatus.alternativeA"));cancellation.Cancel();socket.Abort();}};
                        microphone.Failed+=delegate(string failure){SetStatus(failure);cancellation.Cancel();socket.Abort();};
                        sending=Task.Run(async delegate {try{foreach(byte[] bytes in queue.GetConsumingEnumerable(token))await socket.SendTextAsync(Json(new {realtimeInput=new {audio=new {data=Convert.ToBase64String(bytes),mimeType="audio/pcm;rate=16000"}}}),token).ConfigureAwait(false);}catch(OperationCanceledException){}catch(Exception){SetStatus(TextResources.Get("geminiLiveSession.run.setStatus.alternativeB"));cancellation.Cancel();socket.Abort();}});
                        microphone.Start();Interlocked.Increment(ref successfulConnections);SetStatus(TextResources.Get("geminiLiveSession.run.setStatus.alternativeC"));configured.TrySetResult(true);
                    }
                    object raw;if(!json.TryGetValue("serverContent",out raw))continue;
                    if(!ready)throw new ApiException(TextResources.Get("geminiLiveSession.run.text.alternativeB"));
                    var content=(Dictionary<string,object>)raw;
                    bool interrupted=content.ContainsKey("interrupted") && Object.Equals(content["interrupted"],true);
                    if(logicalTurnId==null && content.ContainsKey("inputTranscription")){logicalTurnId="live-"+Guid.NewGuid().ToString("N");companionId=RelationshipCompanion==null?null:RelationshipCompanion();}
                    user=Transcript(content,"inputTranscription",user,"user");reply=Transcript(content,"outputTranscription",reply,"assistant");
                    if(interrupted){speaker.Interrupt();Commit(user,reply,"interrupted",logicalTurnId,companionId);user=reply="";logicalTurnId=companionId=null;}
                    if(!interrupted && content.TryGetValue("modelTurn",out raw))
                    {
                        var turn=(Dictionary<string,object>)raw;object parts;
                        if(turn.TryGetValue("parts",out parts))foreach(object item in (object[])parts)
                        {
                            var part=(Dictionary<string,object>)item;object inline;
                            if(part.TryGetValue("inlineData",out inline))
                            {
                                var audio=(Dictionary<string,object>)inline;string mime=audio["mimeType"] as string;int rate=24000;
                                if(mime==null || !mime.StartsWith("audio/pcm",StringComparison.OrdinalIgnoreCase))throw new ApiException(TextResources.Get("geminiLiveSession.run.text.alternativeC"));
                                int marker=mime.IndexOf("rate=",StringComparison.OrdinalIgnoreCase);if(marker>=0 && !Int32.TryParse(mime.Substring(marker+5).Split(';')[0],out rate))throw new ApiException(TextResources.Get("geminiLiveSession.run.text.alternativeD"));
                                speaker.Enqueue(Convert.FromBase64String((string)audio["data"]),rate);
                            }
                        }
                    }
                    if(!interrupted && content.ContainsKey("turnComplete") && Object.Equals(content["turnComplete"],true)){Commit(user,reply,"complete",logicalTurnId,companionId);user=reply="";logicalTurnId=companionId=null;SetStatus(TextResources.Get("geminiLiveSession.run.setStatus.alternativeC"));}
                }
            }
            catch(OperationCanceledException){configured.TrySetCanceled();}
            catch(Exception e)
            {
                if(token.IsCancellationRequested)configured.TrySetCanceled();
                else{string safe=e is ApiException || e is InvalidOperationException ? e.Message : TextResources.Get("geminiLiveSession.run.invalidOperationExceptionEMessage");SetStatus(safe);configured.TrySetException(new ApiException(safe));}
            }
            finally
            {
                Commit(user,reply,token.IsCancellationRequested?"cancelled":"error",logicalTurnId,companionId);
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
            if(data.TryGetValue("text",out text) && text is string){current+=(string)text;if(current.Length>50000)throw new ApiException(TextResources.Get("geminiLiveSession.transcript.text"));if(!disposed && PartialTranscript!=null)PartialTranscript(role,current);}return current;
        }
        void Commit(string user,string reply,string status,string logicalTurnId,string companionId)
        {
            if(disposed || (user.Length==0 && reply.Length==0))return;
            if(TurnCommitted!=null)TurnCommitted(user,reply,status);
            if(LogicalTurnCommitted!=null)LogicalTurnCommitted(new RelationshipLiveTurn{User=user,Reply=reply,Status=status,LogicalTurnId=logicalTurnId,CompanionId=companionId,CompletedTime=RelationshipTime.Current()});
            // Existing consumers receive only successful turns; incomplete subtitles use the status event.
            if(status=="complete" && TurnCompleted!=null)TurnCompleted(user,reply);
        }
        public void SetVolume(int value){lock(gate){volume=Math.Max(0,Math.Min(100,value));if(speaker!=null)speaker.SetVolume(volume);}}
        public async Task StopAsync()
        {
            Task task;lock(gate){task=running;if(task==null)return;cancellation.Cancel();if(socket!=null)socket.Abort();}
            await task.ConfigureAwait(false);SetStatus(TextResources.Get("geminiLiveSession.stopAsync.setStatus"));
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;if(cancellation!=null)cancellation.Cancel();if(socket!=null)socket.Abort();}}
    }
}
