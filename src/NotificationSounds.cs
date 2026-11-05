using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public enum NotificationCue { Complete,Review,Permission,Failure }

    // Cues are short, optional feedback. No device operation runs on the caller.
    public sealed class NotificationSoundPlayer:IDisposable
    {
        readonly object gate=new object();
        readonly Func<PetSettings> settings;
        readonly Func<ILiveAudioOutput> createOutput;
        readonly Func<bool> audioBusy;
        readonly Func<DateTime> clock;
        readonly Queue<string> recent=new Queue<string>();
        readonly HashSet<string> known=new HashSet<string>(StringComparer.Ordinal);
        readonly SemaphoreSlim playback=new SemaphoreSlim(1,1);
        Request pending;
        DateTime lastPlayed;
        bool active,disposed,hasPlayed;

        public NotificationSoundPlayer(Func<PetSettings> settings,Func<ILiveAudioOutput> createOutput,Func<bool> audioBusy=null,Func<DateTime> clock=null)
        {
            if(settings==null)throw new ArgumentNullException("settings");
            if(createOutput==null)throw new ArgumentNullException("createOutput");
            this.settings=settings;this.createOutput=createOutput;this.audioBusy=audioBusy??(()=>false);this.clock=clock??(()=>DateTime.UtcNow);
        }

        public bool TryPlay(NotificationCue cue,string eventId)
        {
            if(String.IsNullOrWhiteSpace(eventId)||eventId.Length>512||cue<NotificationCue.Complete||cue>NotificationCue.Failure)return false;
            lock(gate)
            {
                if(disposed||!known.Add(eventId))return false;
                // Remember suppressed events as well: enabling sounds never replays old notices.
                recent.Enqueue(eventId);while(recent.Count>128)known.Remove(recent.Dequeue());
                try
                {
                    var current=settings();var now=clock();
                    if(active||!Allowed(current)||audioBusy()||(hasPlayed&&now-lastPlayed<TimeSpan.FromSeconds(3)))return false;
                    active=true;hasPlayed=true;lastPlayed=now;
                    var request=new Request();pending=request;
                    Task.Run(()=>PlayAsync(cue,request));return true;
                }
                catch{return false;}
            }
        }

        static bool Allowed(PetSettings value){return value!=null&&value.NotificationSoundsEnabled&&!value.QuietMode&&value.Volume>0;}

        async Task PlayAsync(NotificationCue cue,Request request)
        {
            ILiveAudioOutput output=null;bool entered=false;
            try
            {
                await playback.WaitAsync().ConfigureAwait(false);entered=true;
                request.ThrowIfStopped();
                int rate;byte[] pcm=ReadPcm(cue,out rate);
                if(!Allowed(settings())||audioBusy())return;
                request.ThrowIfStopped();
                output=createOutput();
                // A device factory can be slow. Recheck after it returns so Stop/Dispose
                // also cancel a request that has not reached the device yet.
                request.ThrowIfStopped();var current=settings();
                if(output==null||!Allowed(current)||audioBusy())return;
                output.SetVolume(Math.Max(0,Math.Min(100,current.Volume)));
                request.ThrowIfStopped();
                output.Enqueue(pcm,rate);
                await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(pcm.Length/(rate*2.0)+.05)),request.Stopped.Task).ConfigureAwait(false);
            }
            catch { /* Optional sound must never fail a task or a permission flow. */ }
            finally
            {
                lock(gate){if(pending==request){pending=null;active=false;}}
                if(output!=null){try{output.Interrupt();}catch{}try{output.Dispose();}catch{}}
                if(entered)playback.Release();
            }
        }

        static byte[] ReadPcm(NotificationCue cue,out int rate)
        {
            string[] names={"task-complete","review-needed","permission-needed","attention-failed"};
            using(var stream=typeof(NotificationSoundPlayer).Assembly.GetManifestResourceStream("Dafeiyu.Sounds."+names[(int)cue]+".wav"))
            {
                if(stream==null)throw new InvalidDataException("Notification cue is unavailable.");
                using(var reader=new BinaryReader(stream))
                {
                    if(new string(reader.ReadChars(4))!="RIFF")throw new InvalidDataException();reader.ReadInt32();
                    if(new string(reader.ReadChars(4))!="WAVE")throw new InvalidDataException();
                    rate=0;bool format=false;
                    while(stream.Position+8<=stream.Length)
                    {
                        string chunk=new string(reader.ReadChars(4));int size=reader.ReadInt32();long end=stream.Position+size;
                        if(size<0||end>stream.Length)throw new InvalidDataException();
                        if(chunk=="fmt ")
                        {
                            if(size<16||reader.ReadInt16()!=1||reader.ReadInt16()!=1)throw new InvalidDataException();
                            rate=reader.ReadInt32();int bytesPerSecond=reader.ReadInt32();
                            if(rate!=48000||bytesPerSecond!=rate*2||reader.ReadInt16()!=2||reader.ReadInt16()!=16)throw new InvalidDataException();
                            format=true;
                        }
                        else if(chunk=="data")
                        {
                            if(!format||size==0||size%2!=0||size>rate*2*2)throw new InvalidDataException();
                            return reader.ReadBytes(size);
                        }
                        stream.Position=end+(size%2);
                    }
                    throw new InvalidDataException();
                }
            }
        }

        public void Stop(){lock(gate){CancelPending();}}
        void CancelPending(){if(pending!=null){pending.Stopped.TrySetResult(true);pending=null;}active=false;}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;CancelPending();}}
        sealed class Request
        {
            // Stop may run on the UI thread. Continuations must not interrupt or
            // dispose a platform device inline on that caller.
            internal readonly TaskCompletionSource<bool> Stopped=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal void ThrowIfStopped(){if(Stopped.Task.IsCompleted)throw new OperationCanceledException();}
        }
    }
}
