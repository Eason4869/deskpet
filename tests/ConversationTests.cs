using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ConversationTests
    {
        static int passed,failed;
        public static int Main()
        {
            Run("text completes a pair, resolves pet name and reads reply",async delegate {
                var api=new FakeApi(); var speech=new FakeSpeech(); using(var s=Create(api,speech)) {
                    var cfg=Configured(); cfg.SystemPrompt="你是{pet_name}"; await s.SendTextAsync("你好",cfg,"E包");
                    Check(s.History.Messages.Count==2 && api.Messages[0].Content.StartsWith("你是E包\n") && api.Messages[0].Content.Contains("纯对话模式") && speech.Calls==1 && !s.IsBusy,"bad successful flow"); }
            });
            Run("cancelled model retains incomplete display without successful context",async delegate {
                var api=new FakeApi {Delay=1000}; var speech=new FakeSpeech(); using(var s=Create(api,speech)) {
                    var task=s.SendTextAsync("stop",Configured(),"大肥鱼"); await Task.Delay(30); await s.StopAsync();
                    try{await task;}catch(OperationCanceledException){} Check(s.History.Messages.Count==2 && s.History.Messages.Last().Status=="cancelled" && s.History.ForRequest("","next").Count==1 && speech.Calls==0 && !s.IsBusy,"cancel leaked into model context"); }
            });
            Run("recording is explicit and follows recognition-chat-speech",async delegate {
                var api=new FakeApi();var speech=new FakeSpeech();var mic=new FakeRecorder(); using(var s=new ConversationSession(api,speech,delegate{return mic;})) {
                    Check(mic.Starts==0,"automatic microphone probe");s.StartRecording(Configured());Check(s.Stage==ConversationStage.Recording,"not recording");
                    await s.FinishRecordingAsync("大肥鱼");Check(api.RecognitionCalls==1 && api.ChatCalls==1 && speech.Calls==1 && mic.Disposed && api.Messages.Last().Content=="识别文字","voice pipeline wrong"); }
            });
            Run("recording cancellation releases mic and sends nothing",async delegate {
                var api=new FakeApi();var mic=new FakeRecorder();using(var s=new ConversationSession(api,new FakeSpeech(),delegate{return mic;})) {
                    s.StartRecording(Configured());await s.StopAsync();Check(mic.Disposed && api.RecognitionCalls==0 && !s.IsBusy,"cancel sent audio"); }
            });
            Run("missing voice configuration never opens mic",async delegate {
                var mic=new FakeRecorder();using(var s=new ConversationSession(new FakeApi(),new FakeSpeech(),delegate{return mic;})) {
                    var cfg=Configured();cfg.Transcription=new ConnectionSettings();bool denied=false;try{s.StartRecording(cfg);}catch(ApiException){denied=true;}
                    Check(denied && mic.Starts==0 && !s.IsBusy,"unconfigured mic opened");await Task.FromResult(0); }
            });
            Run("speech failure preserves model reply",async delegate {
                using(var s=Create(new FakeApi(),new FakeSpeech {Fail=true})) {await s.SendTextAsync("test",Configured(),"大肥鱼");Check(s.History.Messages.Count==2 && s.LastNotice.Contains("朗读") && !s.IsBusy,"lost model answer");}
            });
            Run("disabling reading before reply prevents unwanted speech",async delegate {
                var speech=new FakeSpeech();var cfg=Configured();using(var s=Create(new FakeApi {Delay=100},speech)) {var t=s.SendTextAsync("test",cfg,"E包");cfg.ReadReplies=false;await t;Check(speech.Calls==0,"reading setting ignored");}
            });
            Run("active request blocks a concurrent send",async delegate {
                using(var s=Create(new FakeApi {Delay=1000},new FakeSpeech())) {var t=s.SendTextAsync("one",Configured(),"E包");bool denied=false;
                    try{await s.SendTextAsync("two",Configured(),"E包");}catch(InvalidOperationException){denied=true;}await s.StopAsync();try{await t;}catch(OperationCanceledException){}Check(denied,"parallel send accepted");}
            });
            Run("late provider result cannot escape disposed session",async delegate {
                var s=Create(new FakeApi {Delay=100,IgnoreCancel=true},new FakeSpeech());var t=s.SendTextAsync("late",Configured(),"E包");int events=0;s.MessageAdded+=delegate{events++;};s.Dispose();
                try{await t;}catch(OperationCanceledException){}Check(events==0 && s.History.Messages.Count==2 && s.History.Messages.Last().Status=="interrupted" && s.History.ForRequest("","next").Count==1,"late result committed");
            });
            Run("voice samples use the independently configured service",async delegate {
                var api=new FakeApi();using(var s=Create(api,new FakeSpeech())) {var cfg=Configured();cfg.Transcription.BaseUrl="http://127.0.0.1:9002/voice";await s.SendVoiceAsync(WaveData.EncodePcm(new byte[16000]),cfg,"E包");Check(api.RecognitionUrl.EndsWith("/voice"),"wrong speech service");}
            });
            Run("invalid inputs do not start a request",async delegate {
                using(var s=Create(new FakeApi(),new FakeSpeech())) {bool denied=false;try{await s.SendTextAsync(new string('x',8001),Configured(),"E包");}catch(ApiException){denied=true;}Check(denied && !s.IsBusy,"long input accepted");
                    denied=false;try{await s.SendVoiceAsync(new byte[5],Configured(),"E包");}catch(ApiException){denied=true;}Check(denied && !s.IsBusy,"bad wave accepted");}
            });
            Run("clear resets completed context",async delegate {using(var s=Create(new FakeApi(),new FakeSpeech())) {await s.SendTextAsync("clear",Configured(),"E包");s.Clear();Check(s.History.Messages.Count==0,"clear retained history");}});
            Console.WriteLine("Conversation tests: "+passed+" passed, "+failed+" failed");return failed==0 ? 0:1;
        }
        static ConversationSession Create(FakeApi api,FakeSpeech speech){return new ConversationSession(api,speech,delegate{return new FakeRecorder();});}
        static PetSettings Configured(){return new PetSettings {AgentEnabled=false,Text=new ConnectionSettings {BaseUrl="http://127.0.0.1:9001/v1",Model="chat"},Transcription=new ConnectionSettings {BaseUrl="http://127.0.0.1:9002/v1",Model="stt"}};}
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        static void Run(string name,Func<Task> test){try{test().GetAwaiter().GetResult();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        sealed class FakeApi:IConversationApi {
            public int Delay,ChatCalls,RecognitionCalls;public bool IgnoreCancel;public IList<ChatMessage> Messages;public string RecognitionUrl;
            public async Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){ChatCalls++;Messages=m;if(Delay>0)await Task.Delay(Delay,IgnoreCancel ? CancellationToken.None:c);return "模型回答";}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] wav,CancellationToken c){RecognitionCalls++;RecognitionUrl=s.BaseUrl;return Task.FromResult("识别文字");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string voice,string text,CancellationToken c){return Task.FromResult(WaveData.EncodePcm(new byte[16000]));}
            public void Dispose(){}
        }
        sealed class FakeSpeech:IReplySpeech {
            public int Calls;public bool Fail;public Task SpeakAsync(string reply,PetSettings cfg,CancellationToken c){Calls++;if(Fail)throw new InvalidOperationException("test speech failure");return Task.FromResult(0);}
            public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}
        }
        sealed class FakeRecorder:IAudioRecorder {
            public int Starts;public bool Disposed;public bool IsRecording{get{return Starts>0 && !Disposed;}}
            public void Start(){Starts++;}public Task<byte[]> FinishAsync(){return Task.FromResult(WaveData.EncodePcm(new byte[16000]));}public void Dispose(){Disposed=true;}
        }
    }
}
