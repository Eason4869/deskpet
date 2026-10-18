using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class DictationTests
    {
        static int passed;
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Dictation tests: "+passed+" passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
        static async Task Run()
        {
            var cfg=new PetSettings{Transcription=new ConnectionSettings{BaseUrl="http://127.0.0.1/v1",Model="asr"}};var api=new Api();Recorder recorder=null;int creates=0;var owner=new object();
            using(var d=new DictationSession(api,()=>{creates++;return recorder=new Recorder();},()=>cfg))
            {
                d.Start(owner);Check(d.IsBusy && d.Stage==DictationStage.Recording && recorder.Started,"ASR can start without text-model configuration");
                bool denied=false;try{d.Start(new object());}catch(InvalidOperationException){denied=true;}Check(denied && creates==1,"only one microphone owner is allowed");
                await d.CancelAsync(new object());Check(d.IsBusy,"another window cannot cancel the recording owner");
                string text=await d.FinishAsync(owner);Check(d.SuccessfulRecognitions==1,"only a nonempty completed ASR result advances guide test evidence");Check(text=="听写结果" && api.ChatCalls==0 && !d.IsBusy && recorder.Disposed,"finish only transcribes and releases microphone");
                d.Start(owner);await d.CancelAsync(owner);Check(api.AsrCalls==1 && recorder.Disposed && !d.IsBusy,"cancel before finish does not contact ASR");
                api.Delay=true;d.Start(owner);var late=d.FinishAsync(owner);await Task.Delay(15);await d.CancelAsync(owner);denied=false;try{await late;}catch(OperationCanceledException){denied=true;}Check(d.SuccessfulRecognitions==1,"cancelled transcription never advances guide test evidence");Check(denied && !d.IsBusy,"late provider result cannot revive a cancelled dictation");
                api.Delay=false;api.Fail=true;d.Start(owner);denied=false;try{await d.FinishAsync(owner);}catch(ApiException){denied=true;}Check(denied && !d.IsBusy && recorder.Disposed,"ASR failure releases ownership and recording resources");
                cfg.Transcription=new ConnectionSettings();denied=false;int before=creates;try{d.Start(owner);}catch(ApiException){denied=true;}Check(denied && before==creates && !d.IsBusy,"missing ASR config never opens microphone");
            }
        }
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        sealed class Recorder:IAudioRecorder{public bool Started,Disposed;public bool IsRecording{get{return Started&&!Disposed;}}public void Start(){Started=true;}public Task<byte[]> FinishAsync(){return Task.FromResult(WaveData.EncodePcm(new byte[16000]));}public void Dispose(){Disposed=true;}}
        sealed class Api:IConversationApi
        {public int ChatCalls,AsrCalls;public bool Delay,Fail;public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken t){ChatCalls++;throw new NotSupportedException();}public async Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken t){AsrCalls++;if(Delay)await Task.Delay(120);if(Fail)throw new ApiException("fixture failure");return "听写结果";}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string x,CancellationToken t){throw new NotSupportedException();}public void Dispose(){}}
    }
}
