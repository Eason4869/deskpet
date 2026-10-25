using System;
using System.Collections.Concurrent;
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
            Run("held text drains after its UI context stops",async delegate {
                var api=new HeldApi {ChatGate=new Gate<string>()};var cfg=Configured();cfg.ReadReplies=false;
                using(var s=HeldSession(api,new HeldSpeech()))await DrainAfterContextStops(s,()=>s.SendTextAsync("held text",cfg,"E包"),()=>api.ChatGate.Entered.Task.IsCompleted,()=>api.ChatGate.Release.TrySetResult("late reply"),"text");
            });
            Run("held voice and recording drain after their UI context stops",async delegate {
                var cfg=Configured();cfg.ReadReplies=false;var wav=WaveData.EncodePcm(new byte[16000]);
                var api=new HeldApi {RecognitionGate=new Gate<string>()};
                using(var s=HeldSession(api,new HeldSpeech()))await DrainAfterContextStops(s,()=>s.SendVoiceAsync(wav,cfg,"E包"),()=>api.RecognitionGate.Entered.Task.IsCompleted,()=>api.RecognitionGate.Release.TrySetResult("识别文字"),"voice");
                api=new HeldApi {ChatGate=new Gate<string>()};
                using(var s=HeldSession(api,new HeldSpeech()))await DrainAfterContextStops(s,()=>s.SendVoiceAsync(wav,cfg,"E包"),()=>api.ChatGate.Entered.Task.IsCompleted,()=>api.ChatGate.Release.TrySetResult("late voice reply"),"voice reply");
                var recorder=new HeldRecorder {FinishGate=new Gate<byte[]>()};
                using(var s=HeldSession(new HeldApi(),new HeldSpeech(),recorder)){
                    s.StartRecording(cfg);
                    await DrainAfterContextStops(s,()=>s.FinishRecordingAsync("E包"),()=>recorder.FinishGate.Entered.Task.IsCompleted,()=>recorder.FinishGate.Release.TrySetResult(wav),"recording");
                    Check(recorder.Disposed,"held recording did not release microphone");
                }
                api=new HeldApi {ChatGate=new Gate<string>()};recorder=new HeldRecorder();
                using(var s=HeldSession(api,new HeldSpeech(),recorder)){
                    s.StartRecording(cfg);
                    await DrainAfterContextStops(s,()=>s.FinishRecordingAsync("E包"),()=>api.ChatGate.Entered.Task.IsCompleted,()=>api.ChatGate.Release.TrySetResult("late recorded reply"),"recorded reply");
                    Check(recorder.Disposed,"held recorded reply did not release microphone");
                }
            });
            Run("held regeneration drains after its UI context stops",async delegate {
                var api=new HeldApi();var cfg=Configured();cfg.ReadReplies=false;
                using(var s=HeldSession(api,new HeldSpeech())){
                    await s.SendTextAsync("seed",cfg,"E包");var id=s.History.Messages.Last().Id;api.ChatGate=new Gate<string>();
                    await DrainAfterContextStops(s,()=>s.RegenerateAsync(id,cfg,"E包"),()=>api.ChatGate.Entered.Task.IsCompleted,()=>api.ChatGate.Release.TrySetResult("late regeneration"),"regeneration");
                }
            });
            Run("held response and standalone speech drain after their UI context stops",async delegate {
                var cfg=Configured();cfg.ReadReplies=true;
                var speech=new HeldSpeech {Gate=new Gate<bool>()};
                using(var s=HeldSession(new HeldApi(),speech))await DrainAfterContextStops(s,()=>s.SendTextAsync("read reply",cfg,"E包"),()=>speech.Gate.Entered.Task.IsCompleted,()=>speech.Gate.Release.TrySetResult(true),"response speech");
                cfg.ReadReplies=false;speech=new HeldSpeech();
                using(var s=HeldSession(new HeldApi(),speech)){
                    await s.SendTextAsync("seed",cfg,"E包");var id=s.History.Messages.Last().Id;speech.Gate=new Gate<bool>();
                    await DrainAfterContextStops(s,()=>s.SpeakReplyAsync(id,cfg),()=>speech.Gate.Entered.Task.IsCompleted,()=>speech.Gate.Release.TrySetResult(true),"standalone speech");
                }
                speech=new HeldSpeech {Gate=new Gate<bool>()};
                using(var s=HeldSession(new HeldApi(),speech))await DrainAfterContextStops(s,()=>s.PreviewSpeechAsync(cfg,"E包"),()=>speech.Gate.Entered.Task.IsCompleted,()=>speech.Gate.Release.TrySetResult(true),"preview speech");
            });
            Console.WriteLine("Conversation tests: "+passed+" passed, "+failed+" failed");return failed==0 ? 0:1;
        }
        static ConversationSession Create(FakeApi api,FakeSpeech speech){return new ConversationSession(api,speech,delegate{return new FakeRecorder();});}
        static PetSettings Configured(){return new PetSettings {AgentEnabled=false,Text=new ConnectionSettings {BaseUrl="http://127.0.0.1:9001/v1",Model="chat"},Transcription=new ConnectionSettings {BaseUrl="http://127.0.0.1:9002/v1",Model="stt"}};}
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        static void Run(string name,Func<Task> test){try{test().GetAwaiter().GetResult();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        static ConversationSession HeldSession(HeldApi api,HeldSpeech speech,HeldRecorder recorder=null){return new ConversationSession(api,speech,delegate{return recorder??new HeldRecorder();});}
        static async Task DrainAfterContextStops(ConversationSession session,Func<Task> begin,Func<bool> entered,Action release,string name)
        {
            var previous=SynchronizationContext.Current;var context=new StoppedUiContext();Task operation=null,stop=null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                try{operation=begin();context.PumpUntil(entered,name+" did not enter held service");Check(session.IsBusy,name+" was not active");}
                finally{context.Stop();SynchronizationContext.SetSynchronizationContext(previous);}
                stop=session.StopAsync();Check(!stop.IsCompleted,name+" stop did not wait for held service");release();
                var completed=Task.WhenAll(Observe(operation),stop);var winner=await Task.WhenAny(completed,Task.Delay(3000)).ConfigureAwait(false);
                Check(ReferenceEquals(winner,completed),name+" needs stopped UI context to finish");await completed.ConfigureAwait(false);
                Check(operation.IsCompleted && !operation.IsFaulted && stop.IsCompleted && !session.IsBusy && session.Stage==ConversationStage.Idle,name+" did not end cleanly");
            }
            finally
            {
                release();context.Resume();var current=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(context);
                try{context.PumpUntil(()=>(operation==null||operation.IsCompleted)&&(stop==null||stop.IsCompleted),name+" cleanup could not drain queued continuations");}
                catch(TimeoutException){}finally{SynchronizationContext.SetSynchronizationContext(current);}
            }
        }
        static async Task Observe(Task operation){try{await operation.ConfigureAwait(false);}catch(OperationCanceledException){}}
        sealed class StoppedUiContext:SynchronizationContext
        {
            readonly ConcurrentQueue<KeyValuePair<SendOrPostCallback,object>> pending=new ConcurrentQueue<KeyValuePair<SendOrPostCallback,object>>();bool stopped;
            public override void Post(SendOrPostCallback callback,object state){pending.Enqueue(new KeyValuePair<SendOrPostCallback,object>(callback,state));}
            public void Stop(){stopped=true;}public void Resume(){stopped=false;}
            public void PumpUntil(Func<bool> ready,string failure)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){
                    if(stopped)throw new InvalidOperationException("stopped UI context was pumped");
                    KeyValuePair<SendOrPostCallback,object> work;if(pending.TryDequeue(out work))work.Key(work.Value);else Thread.Sleep(1);
                    if(watch.ElapsedMilliseconds>3000)throw new TimeoutException(failure);
                }
            }
        }
        sealed class Gate<T>
        {
            public readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<T> Release=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<T> Arrive(){Entered.TrySetResult(true);return Release.Task;}
        }
        sealed class HeldApi:IConversationApi
        {
            public Gate<string> ChatGate,RecognitionGate;
            public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){return ChatGate==null?Task.FromResult("模型回答"):ChatGate.Arrive();}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] wav,CancellationToken c){return RecognitionGate==null?Task.FromResult("识别文字"):RecognitionGate.Arrive();}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string voice,string text,CancellationToken c){return Task.FromResult(WaveData.EncodePcm(new byte[16000]));}
            public void Dispose(){}
        }
        sealed class HeldSpeech:IReplySpeech
        {
            public Gate<bool> Gate;
            public Task SpeakAsync(string reply,PetSettings cfg,CancellationToken c){return Gate==null?Task.FromResult(true):Gate.Arrive();}
            public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}
        }
        sealed class HeldRecorder:IAudioRecorder
        {
            public Gate<byte[]> FinishGate;public bool Disposed;public bool IsRecording{get{return !Disposed;}}
            public void Start(){}public Task<byte[]> FinishAsync(){return FinishGate==null?Task.FromResult(WaveData.EncodePcm(new byte[16000])):FinishGate.Arrive();}
            public void Dispose(){Disposed=true;}
        }
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
