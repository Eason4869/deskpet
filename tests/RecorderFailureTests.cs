using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class RecorderFailureTests
    {
        static int passed;
        public static int Main()
        {
            passed=0;
            try { Run().GetAwaiter().GetResult();NativeAdapterFailures().GetAwaiter().GetResult();Console.WriteLine("Recorder failure tests: "+passed+" passed");return 0; }
            catch(Exception error){Console.WriteLine("FAIL recorder failure: "+error.Message);return 1;}
        }
        static async Task Run()
        {
            var settings=new PetSettings{Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"},Transcription=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};
            var recorder=new Recorder();
            using(var session=new ConversationSession(new UnusedApi(),new UnusedSpeech(),()=>recorder))
            {
                session.StartRecording(settings);recorder.Fail();
                // End releases the active operation before publishing the final Idle stage.
                await Until(()=>!session.IsBusy && session.Stage==ConversationStage.Idle && recorder.Disposed);
                Check(session.Stage==ConversationStage.Idle && recorder.Disposed,"conversation recording failure releases ownership and microphone");
                Check(session.LastNotice.Contains("device disconnected"),"conversation recording failure remains visible in status");
                recorder.Fail();Check(!session.IsBusy && session.History.Messages.Count==0,"late recording failure cannot create a turn");
            }
            recorder=new Recorder();object owner=new object();
            using(var dictation=new DictationSession(new UnusedApi(),()=>recorder,()=>settings))
            {
                dictation.Start(owner);recorder.Fail();
                await Until(()=>!dictation.IsBusy && dictation.Stage==DictationStage.Idle && !dictation.Owns(owner) && recorder.Disposed);
                Check(dictation.Stage==DictationStage.Idle && !dictation.Owns(owner) && recorder.Disposed,"dictation device failure releases ownership without calling ASR");
                // An event racing with Finish must not prevent a subsequent fresh recording.
                recorder=new Recorder();dictation.Start(owner);await dictation.CancelAsync(owner);
                Check(!dictation.IsBusy,"new dictation can start after a failed device");
            }
        }
        static async Task NativeAdapterFailures()
        {
            foreach(bool streaming in new[]{false,true})using(var recorder=new NativeFailureRecorder(streaming,true)) {
                int failures=0;recorder.Failed+=message=>Interlocked.Increment(ref failures);recorder.Start();
                await Until(()=>Volatile.Read(ref failures)>0);
                Check(failures==1,"real WinMM adapter publishes an invalid-device failure exactly once, streaming="+streaming);
                Check(!recorder.IsRecording,"failed WinMM adapter stops advertising recording, streaming="+streaming);
            }
            foreach(bool streaming in new[]{false,true}) {
                int failures=0;using(var recorder=new NativeFailureRecorder(streaming,false)) {
                    recorder.Failed+=message=>Interlocked.Increment(ref failures);recorder.Start();recorder.Dispose();
                }
                await Task.Delay(50);Check(failures==0,"normal WinMM cancellation does not publish failure, streaming="+streaming);
            }
            using(var recorder=new NativeFailureRecorder(false,false)) {
                int failures=0;recorder.Failed+=message=>Interlocked.Increment(ref failures);recorder.Start();recorder.SeedPcm(new byte[16000]);
                byte[] wav=await recorder.FinishAsync();await Task.Delay(50);
                Check(failures==0 && WaveData.IsWave(wav),"normal WinMM finish returns WAV without publishing a device failure");
            }
            var settings=new PetSettings{Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"},Transcription=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};
            var actual=new NativeFailureRecorder(false,true);
            using(var session=new ConversationSession(new UnusedApi(),new UnusedSpeech(),()=>actual)) {
                session.StartRecording(settings);await Until(()=>!session.IsBusy && session.Stage==ConversationStage.Idle && actual.Disposed);
                Check(actual.Disposed && session.Stage==ConversationStage.Idle && session.LastNotice.Contains("麦克风"),"ordinary real WinMM failure reaches conversation cleanup and visible notice");
            }
            actual=new NativeFailureRecorder(false,true);
            using(var dictation=new DictationSession(new UnusedApi(),()=>actual,()=>settings)) {
                dictation.Start(new object());await Until(()=>!dictation.IsBusy && dictation.Stage==DictationStage.Idle && actual.Disposed);
                Check(actual.Disposed && dictation.Stage==DictationStage.Idle && dictation.LastNotice.Contains("麦克风"),"ordinary real WinMM failure reaches dictation cleanup and visible notice");
            }
        }
        static async Task Until(Func<bool> ready){for(int i=0;i<200;i++){if(ready())return;await Task.Delay(10);}throw new Exception("device failure was not reported or left recording busy");}
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        // Drive the production Drain method and real waveInAddBuffer with a null
        // handle. WinMM returns MMSYSERR_INVALHANDLE; no microphone is opened.
        // All reflection and unmanaged fixture memory live in this test utility.
        sealed class NativeFailureRecorder:IAudioRecorder,IPcmMicrophone
        {
            readonly AudioRecorder adapter;readonly bool fault;
            public bool Disposed;public bool IsRecording{get{return adapter.IsRecording;}}
            public event Action<byte[]> PcmAvailable{add{adapter.PcmAvailable+=value;}remove{adapter.PcmAvailable-=value;}}
            public event Action<string> Failed{add{adapter.Failed+=value;}remove{adapter.Failed-=value;}}
            public NativeFailureRecorder(bool streaming,bool fault){adapter=new AudioRecorder(streaming);this.fault=fault;}
            public void Start()
            {
                var recorderType=typeof(AudioRecorder);var bufferType=recorderType.GetNestedType("Buffer",BindingFlags.NonPublic);
                var buffer=Activator.CreateInstance(bufferType,new object[]{64});
                var data=(IntPtr)bufferType.GetField("Data").GetValue(buffer);Marshal.Copy(new byte[]{1,2},0,data,2);
                var header=(IntPtr)bufferType.GetField("Header").GetValue(buffer);
                var headerType=recorderType.GetNestedType("WaveHeader",BindingFlags.NonPublic);
                object value=Marshal.PtrToStructure(header,headerType);headerType.GetField("BytesRecorded").SetValue(value,(uint)2);headerType.GetField("Flags").SetValue(value,fault?(uint)1:0);Marshal.StructureToPtr(value,header,false);
                ((IList)recorderType.GetField("buffers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(adapter)).Add(buffer);
                var drain=recorderType.GetMethod("Drain",BindingFlags.Instance|BindingFlags.NonPublic);
                var worker=new Thread(()=>drain.Invoke(adapter,null)){IsBackground=true};
                recorderType.GetField("worker",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(adapter,worker);worker.Start();
                ((AutoResetEvent)recorderType.GetField("ready",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(adapter)).Set();
            }
            public Task<byte[]> FinishAsync(){return adapter.FinishAsync();}
            public void SeedPcm(byte[] bytes){((System.IO.MemoryStream)typeof(AudioRecorder).GetField("pcm",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(adapter)).Write(bytes,0,bytes.Length);}
            public void Dispose(){adapter.Dispose();Disposed=true;}
        }
        sealed class Recorder:IAudioRecorder,IPcmMicrophone
        {
            public bool Disposed;public bool IsRecording{get;private set;}
            public event Action<byte[]> PcmAvailable{add{}remove{}}
            public event Action<string> Failed;
            public void Start(){IsRecording=true;}
            public void Fail(){IsRecording=false;if(Failed!=null)Failed("device disconnected");}
            public Task<byte[]> FinishAsync(){throw new Exception("failed device must not be sent to ASR");}
            public void Dispose(){Disposed=true;IsRecording=false;}
        }
        sealed class UnusedApi:IConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> messages,CancellationToken t){throw new Exception("unexpected chat request");}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] bytes,CancellationToken t){throw new Exception("unexpected ASR request");}
            public Task<byte[]> SpeechAsync(ConnectionSettings c,string voice,string text,CancellationToken t){throw new Exception("unexpected speech request");}
            public void Dispose(){}
        }
        sealed class UnusedSpeech:IReplySpeech
        {
            public Task SpeakAsync(string reply,PetSettings settings,CancellationToken token){throw new Exception("unexpected speech");}
            public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}
        }
    }
}
