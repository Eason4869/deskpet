using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class LiveTests
    {
        static int passed;
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Live tests: "+passed+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL Live: "+e.Message);return 1;}}
        static async Task Run()
        {
            Check(AudioLevel.Pcm16(null)==0 && AudioLevel.Pcm16(new byte[100])==0,"silence and absent PCM do not animate");
            Check(Math.Abs(AudioLevel.Pcm16(new byte[]{0,32,0,224})-.75)<.001,"signed 16-bit amplitude uses RMS across both polarities");
            Check(AudioLevel.Pcm16(new byte[]{0,128})==1,"loud PCM is safely bounded");
            var socket=new FakeSocket();var mic=new FakeMic();var speaker=new FakeOutput();var session=new GeminiLiveSession(()=>socket,()=>mic,()=>speaker);
            string user="",reply="";session.TurnCompleted+=(a,b)=>{user=a;reply=b;};
            var cfg=new PetSettings();cfg.Live.ApiKey="dummy-key";socket.Push("{\"setupComplete\":{}}");await session.StartAsync(cfg,"E包人格");
            var setup=(Dictionary<string,object>)((Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(socket.Sent[0]))["setup"];
            Check(((string)setup["model"]).StartsWith("models/") && socket.Sent[0].Contains("E包人格") && socket.Sent[0].Contains("inputAudioTranscription") && socket.Sent[0].Contains("speechConfig"),"Live setup includes persona, voice and transcriptions");
            Check(mic.Started && session.IsActive,"microphone starts after setup acknowledgement");
            mic.Emit(new byte[]{1,2,3,4});Check(session.InputLevel>0,"captured PCM drives microphone meter");await Task.Delay(380);Check(session.InputLevel==0,"stale microphone sample decays to silence");await Eventually(()=>socket.Sent.Count==2);Check(socket.Sent[1].Contains("audio/pcm;rate=16000") && socket.Sent[1].Contains("AQIDBA=="),"raw 16k PCM is sent to realtimeInput");
            socket.Push("{\"serverContent\":{\"inputTranscription\":{\"text\":\"你好\"},\"outputTranscription\":{\"text\":\"在呢\"},\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AQIDBA==\"}}]}}}");
            await Eventually(()=>speaker.Chunks==1);Check(speaker.Rate==24000 && speaker.Volume==50,"returned 24k PCM is played at saved volume");
            session.SetVolume(20);Check(speaker.Volume==20,"Live volume changes immediately");
            socket.Push("{\"serverContent\":{\"turnComplete\":true}}");await Eventually(()=>reply.Length>0);Check(user=="你好" && reply=="在呢","Live turn commits input and output subtitles together");
            socket.Push("{\"serverContent\":{\"interrupted\":true}}");await Eventually(()=>speaker.Interruptions>0);Check(speaker.Interruptions==1,"barge-in clears queued playback");
            await session.StopAsync();Check(session.InputLevel==0 && session.OutputLevel==0,"stopping clears audio meters");Check(!session.IsActive && mic.Disposed && speaker.Disposed && socket.Disposed,"stop releases socket, microphone and speaker");session.Dispose();
            socket=new FakeSocket();mic=new FakeMic();speaker=new FakeOutput();session=new GeminiLiveSession(()=>socket,()=>mic,()=>speaker);socket.Push("{\"setupComplete\":{}}");await session.StartAsync(cfg,"角色");
            speaker.ReportFailure("Live 输出设备没有播放进展。");await Eventually(()=>!session.IsActive);
            Check(session.Status=="Live 输出设备没有播放进展。" && mic.Disposed && speaker.Disposed,"asynchronous output failure ends the call with its device error and releases the microphone");session.Dispose();
            socket=new FakeSocket();mic=new FakeMic();speaker=new FakeOutput();session=new GeminiLiveSession(()=>socket,()=>mic,()=>speaker);socket.Push("{\"error\":{\"code\":401,\"message\":\"dummy-key provider private details\"}}");
            try{await session.StartAsync(cfg,"角色");throw new Exception("server failure accepted");}catch(ApiException e){Check(!e.Message.Contains("dummy-key") && !e.Message.Contains("private") && e.Message.Contains("Live"),"Live service error is actionable without leaking response details");}
            await session.StopAsync();Check(!mic.Started && socket.Disposed,"failed setup never starts a microphone");Check(session.SuccessfulConnections==0,"failed setup cannot mark guide connection test passed");session.Dispose();
            foreach(bool binary in new[]{false,true})
            using(var server=new WebSocketFixture(binary))
            {
                mic=new FakeMic();speaker=new FakeOutput();session=new GeminiLiveSession(()=>new GeminiLiveSocket(),()=>mic,()=>speaker);
                var local=new PetSettings();local.Live=new ConnectionSettings{BaseUrl=server.Url,Model="live-test",ApiKey="dummy-key"};string output="";session.TurnCompleted+=(a,b)=>output=b;
                await session.StartAsync(local,"真实握手角色");mic.Emit(new byte[]{1,2,3,4});await Eventually(()=>output=="真实 WS 回复");
                Check(server.Setup.Contains("真实握手角色") && server.Audio.Contains("audio/pcm;rate=16000") && speaker.Chunks==1,"actual WebSocket "+(binary?"binary":"text")+" JSON, UTF-8 fragments and bidirectional PCM work");await session.StopAsync();Check(mic.Disposed && speaker.Disposed,"actual WebSocket cancellation frees audio devices");session.Dispose();
            }
            foreach(string ending in new[]{"interrupted","stopped"})
            using(var server=new WebSocketFixture(false,ending))
            using(var conversation=new ConversationSession(new NoopApi(),new NoopSpeech(),()=>null))
            {
                conversation.SelectPersona("e-bao");mic=new FakeMic();speaker=new FakeOutput();session=new GeminiLiveSession(()=>new GeminiLiveSocket(),()=>mic,()=>speaker);
                // The legacy event must mean a successful pair to existing consumers.
                int completed=0;session.TurnCompleted+=(a,b)=>completed++;
                session.TurnCommitted+=(a,b,status)=>conversation.AddLiveTurn("e-bao",a,b,status);
                var local=new PetSettings {Live=new ConnectionSettings{BaseUrl=server.Url,Model="live-test",ApiKey="dummy-key"}};
                await session.StartAsync(local,"部分回复角色");mic.Emit(new byte[]{1,2,3,4});await Eventually(()=>speaker.Chunks==1);
                if(ending=="interrupted")await Eventually(()=>speaker.Interruptions==1);
                await session.StopAsync();Check(completed==0 && conversation.History.ForRequest("","next").Count==1,"actual WebSocket "+ending+" partial is never advertised as a successful legacy turn");
                Check(conversation.History.Messages.Count==2 && conversation.History.Messages[1].Content=="真实 WS 回复" && conversation.History.Messages[1].Status==(ending=="interrupted"?"interrupted":"cancelled") && conversation.History.Messages[1].Source=="live","actual WebSocket "+ending+" subtitles retain their incomplete status in history exactly once");session.Dispose();
            }
            foreach(string url in new[]{"ws://example.com/live","wss://user:pass@example.com/live","wss://example.com/live?key=secret"})
            {try{GeminiLiveSocket.Endpoint(new ConnectionSettings{BaseUrl=url,Model="live",ApiKey="dummy-key"});throw new Exception("unsafe Live URL accepted");}catch(ApiException){passed++;}}
        }
        static async Task Eventually(Func<bool> test){for(int i=0;i<100;i++){if(test())return;await Task.Delay(20);}throw new Exception("event timed out");}
        static void Check(bool yes,string description){if(!yes)throw new Exception(description);passed++;Console.WriteLine("PASS "+description);}
        sealed class FakeSocket : ILiveSocket
        {
            readonly Queue<string> received=new Queue<string>();readonly SemaphoreSlim ready=new SemaphoreSlim(0);public readonly List<string> Sent=new List<string>();public bool Disposed;
            public void Push(string value){lock(received)received.Enqueue(value);ready.Release();}
            public Task ConnectAsync(ConnectionSettings cfg,CancellationToken token){return Task.FromResult(0);}
            public Task SendTextAsync(string text,CancellationToken token){lock(Sent)Sent.Add(text);return Task.FromResult(0);}
            public async Task<string> ReceiveTextAsync(CancellationToken token){await ready.WaitAsync(token);lock(received)return received.Dequeue();}
            public void Abort(){} public void Dispose(){Disposed=true;}
        }
        sealed class FakeMic : IPcmMicrophone
        {public event Action<byte[]> PcmAvailable;public event Action<string> Failed;public bool Started,Disposed;public void Start(){Started=true;}public void Emit(byte[] bytes){PcmAvailable(bytes);}public void Dispose(){Disposed=true;}}
        sealed class FakeOutput : ILiveAudioOutput,ILiveAudioFailureSource
        {public event Action<string> Failed;public void ReportFailure(string message){if(Failed!=null)Failed(message);}public int Chunks,Rate,Volume,Interruptions;public bool Disposed;public void Enqueue(byte[] bytes,int rate){Chunks++;Rate=rate;}public void Interrupt(){Interruptions++;}public void SetVolume(int value){Volume=value;}public void Dispose(){Disposed=true;}}
        sealed class NoopApi:IConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> messages,CancellationToken token){return Task.FromResult("unused");}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] wav,CancellationToken token){return Task.FromResult("unused");}
            public Task<byte[]> SpeechAsync(ConnectionSettings c,string voice,string text,CancellationToken token){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class NoopSpeech:IReplySpeech
        {public Task SpeakAsync(string reply,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){} }
        internal sealed class WebSocketFixture : IDisposable
        {
            readonly System.Net.Sockets.TcpListener listener;System.Net.Sockets.TcpClient client;public string Url,Setup,Audio;readonly Task work;readonly int opcode;readonly string ending;
            public WebSocketFixture(bool binary=false,string terminal="complete"){ending=terminal;opcode=binary?2:1;listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();Url="ws://127.0.0.1:"+((System.Net.IPEndPoint)listener.LocalEndpoint).Port+"/live";work=Task.Run((Func<Task>)Serve);}
            async Task Serve()
            {
                try
                {
                    client=await listener.AcceptTcpClientAsync();client.ReceiveTimeout=4000;using(var stream=client.GetStream())
                    {
                        var bytes=new System.IO.MemoryStream();int marker=0;byte[] end={13,10,13,10};while(marker<4){int next=stream.ReadByte();if(next<0 || bytes.Length>32768)throw new System.IO.IOException();bytes.WriteByte((byte)next);marker=next==end[marker]?marker+1:0;}
                        string headers=System.Text.Encoding.ASCII.GetString(bytes.ToArray());string key="";foreach(string line in headers.Split(new[]{"\r\n"},StringSplitOptions.None))if(line.StartsWith("Sec-WebSocket-Key:",StringComparison.OrdinalIgnoreCase))key=line.Substring(18).Trim();
                        string accept;using(var sha=System.Security.Cryptography.SHA1.Create())accept=Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.ASCII.GetBytes(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                        byte[] response=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "+accept+"\r\n\r\n");await stream.WriteAsync(response,0,response.Length);Setup=await ReadFrame(stream);
                        byte[] setup=System.Text.Encoding.UTF8.GetBytes("{\"setupComplete\":{},\"note\":\"鱼\"}");int split=setup.Length-(opcode==2?4:3);await Frame(stream,opcode,false,setup,0,split);await Frame(stream,0,true,setup,split,setup.Length-split);
                        Audio=await ReadFrame(stream);string body="{\"serverContent\":{\"inputTranscription\":{\"text\":\"真实麦克风输入\"},\"outputTranscription\":{\"text\":\"真实 WS 回复\"},\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AQIDBA==\"}}]}"+(ending=="complete"?",\"turnComplete\":true":"")+"}}";byte[] turn=System.Text.Encoding.UTF8.GetBytes(body);await Frame(stream,opcode,true,turn,0,turn.Length);
                        if(ending=="interrupted"){byte[] interrupt=System.Text.Encoding.UTF8.GetBytes("{\"serverContent\":{\"interrupted\":true}}");await Frame(stream,opcode,true,interrupt,0,interrupt.Length);}
                        byte[] buffer=new byte[8192];while(await stream.ReadAsync(buffer,0,buffer.Length)>0){}
                    }
                }
                catch(System.IO.IOException){}catch(System.Net.Sockets.SocketException){}catch(ObjectDisposedException){}
            }
            static async Task<string> ReadFrame(System.IO.Stream stream)
            {
                byte[] first=await Read(stream,2);int length=first[1]&127;if(length==126){var ext=await Read(stream,2);length=ext[0]*256+ext[1];}if(length>65535)throw new System.IO.IOException();bool masked=(first[1]&128)!=0;byte[] mask=masked?await Read(stream,4):new byte[4];byte[] body=await Read(stream,length);if(masked)for(int i=0;i<body.Length;i++)body[i]^=mask[i%4];return System.Text.Encoding.UTF8.GetString(body);
            }
            static async Task<byte[]> Read(System.IO.Stream stream,int length){byte[] bytes=new byte[length];int offset=0;while(offset<length){int count=await stream.ReadAsync(bytes,offset,length-offset);if(count==0)throw new System.IO.IOException();offset+=count;}return bytes;}
            static async Task Frame(System.IO.Stream stream,int opcode,bool final,byte[] bytes,int offset,int length){var header=length<126?new byte[]{(byte)(opcode|(final?128:0)),(byte)length}:new byte[]{(byte)(opcode|(final?128:0)),126,(byte)(length>>8),(byte)length};await stream.WriteAsync(header,0,header.Length);await stream.WriteAsync(bytes,offset,length);}
            public void Dispose(){listener.Stop();if(client!=null)client.Close();}
        }
    }
}
