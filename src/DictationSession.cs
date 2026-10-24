using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public enum DictationStage { Idle,Recording,Recognizing }
    // Dictation only transcribes. It never submits text to the conversation.
    public sealed class DictationSession : IDisposable
    {
        readonly IConversationApi api;readonly Func<IAudioRecorder> factory;readonly Func<PetSettings> settings;readonly object gate=new object();
        public Dafeiyu.Maintenance.DataMaintenanceGate Maintenance {get;private set;}
        Operation active;bool disposed;double level;long levelAt;
        sealed class Operation{public IDisposable Admission;public object Owner;public IAudioRecorder Recorder;public ConnectionSettings Connection;public CancellationTokenSource Cancel=new CancellationTokenSource();public bool Finishing;public Action<string> Failure;}
        public event Action Changed;
        public DictationStage Stage {get;private set;}
        public string LastNotice {get;private set;}
        int successfulRecognitions;
        internal int SuccessfulRecognitions {get{return Volatile.Read(ref successfulRecognitions);}}
        public bool IsBusy{get{lock(gate)return active!=null;}}
        public double InputLevel{get{return DateTime.UtcNow.Ticks-Interlocked.Read(ref levelAt)>TimeSpan.TicksPerSecond/3?0:Volatile.Read(ref level);}}
        public DictationSession(IConversationApi api,Func<IAudioRecorder> factory,Func<PetSettings> settings,Dafeiyu.Maintenance.DataMaintenanceGate maintenance=null){Maintenance=maintenance??new Dafeiyu.Maintenance.DataMaintenanceGate();this.api=api;this.factory=factory;this.settings=settings;}
        public bool Owns(object owner){lock(gate)return active!=null && ReferenceEquals(active.Owner,owner);}
        void Notify(){if(!disposed && Changed!=null)Changed();}
        public void Start(object owner)
        {
            Operation op;
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("DictationSession");if(active!=null)throw new InvalidOperationException(TextResources.Get("dictationSession.start.text"));
                var connection=settings().Copy().TranscriptionConnection();ApiClient.ValidateConnection(connection,ApiClient.AsrPath(connection));
                var admission=Maintenance.EnterActivity();try{op=new Operation{Admission=admission,Owner=owner,Connection=connection,Recorder=factory()};}catch{admission.Dispose();throw;}active=op;LastNotice="";
            }
            try
            {
                var pcm=op.Recorder as IPcmMicrophone;
                if(pcm!=null) {
                    pcm.PcmAvailable+=delegate(byte[] data){if(op.Cancel.IsCancellationRequested)return;Volatile.Write(ref level,AudioLevel.Pcm16(data));Interlocked.Exchange(ref levelAt,DateTime.UtcNow.Ticks);};
                    op.Failure=message=>RecordingFailed(op,message);pcm.Failed+=op.Failure;
                }
                op.Recorder.Start();lock(gate){if(active==op && !op.Finishing)Stage=DictationStage.Recording;}Notify();
            }
            catch{Release(op);throw;}
        }
        void RecordingFailed(Operation op,string message)
        {
            lock(gate) {
                if(disposed || active!=op || op.Finishing)return;
                op.Finishing=true;LastNotice=String.IsNullOrWhiteSpace(message)?TextResources.Get("dictationSession.recordingFailed.lastNoticeStringIsNullOrWhiteSpace"):message;op.Cancel.Cancel();
            }
            Task.Run(delegate {try{Release(op);}finally{op.Cancel.Dispose();op.Admission.Dispose();}});
        }
        public async Task<string> FinishAsync(object owner)
        {
            Operation op;lock(gate){op=active;if(op==null || !ReferenceEquals(op.Owner,owner) || op.Finishing)throw new InvalidOperationException(TextResources.Get("dictationSession.finishAsync.text"));op.Finishing=true;}
            Stage=DictationStage.Recognizing;Notify();
            try{byte[] wav=await op.Recorder.FinishAsync();op.Cancel.Token.ThrowIfCancellationRequested();string result=await api.TranscribeAsync(op.Connection,wav,op.Cancel.Token);op.Cancel.Token.ThrowIfCancellationRequested();if(!String.IsNullOrWhiteSpace(result))Interlocked.Increment(ref successfulRecognitions);return result;}
            finally{try{Release(op);op.Cancel.Dispose();}finally{op.Admission.Dispose();}}
        }
        public async Task CancelAsync(object owner=null)
        {
            Operation op;lock(gate){op=active;if(op==null || (owner!=null && !ReferenceEquals(op.Owner,owner)))return;op.Cancel.Cancel();}
            await Task.Run(()=>op.Recorder.Dispose());Release(op);
        }
        void Release(Operation op)
        {
            var pcm=op.Recorder as IPcmMicrophone;if(pcm!=null && op.Failure!=null)pcm.Failed-=op.Failure;
            op.Recorder.Dispose();lock(gate){if(active!=op)return;active=null;Stage=DictationStage.Idle;}Volatile.Write(ref level,0);Notify();
            // A cancelled provider may still be observing its token; dispose only
            // after FinishAsync exits, or when no recognition was started.
            if(!op.Finishing){op.Cancel.Dispose();op.Admission.Dispose();}
        }
        public void Dispose(){disposed=true;Operation op;lock(gate){op=active;active=null;Stage=DictationStage.Idle;}if(op!=null){op.Cancel.Cancel();op.Recorder.Dispose();if(!op.Finishing){op.Cancel.Dispose();op.Admission.Dispose();}}api.Dispose();}
    }
}
