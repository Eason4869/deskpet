using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    // Owned by the greeting host, independent of conversation/history and preview speech.
    // Call on the host UI context, like the underlying platform speech outputs.
    public sealed class ProactiveGreetingSpeech : IDisposable
    {
        readonly Func<PetSettings> settings;
        readonly Func<IReplySpeech> createSpeech;
        readonly Func<bool> busy;
        CancellationTokenSource active;
        IReplySpeech output;
        bool disposed;

        public ProactiveGreetingSpeech(Func<PetSettings> settings,Func<IReplySpeech> createSpeech,Func<bool> busy)
        {this.settings=settings;this.createSpeech=createSpeech;this.busy=busy;}
        public bool IsSpeaking {get{return active!=null;}}
        bool Allowed(PetSettings current)
        {return !disposed&&current!=null&&current.ReadReplies&&current.GreetingAutoReadEnabled&&!current.QuietMode&&current.Volume>0&&(busy==null||!busy());}

        public async Task SpeakAsync(string text)
        {
            Stop();
            var current=settings();
            if(!Allowed(current)||String.IsNullOrWhiteSpace(text))return;
            var snapshot=current.Copy();
            var cancellation=new CancellationTokenSource();
            var cancelled=new TaskCompletionSource<bool>();
            IReplySpeech speech=null;
            active=cancellation;
            using(cancellation.Token.Register(()=>cancelled.TrySetResult(true)))
            try
            {
                cancellation.CancelAfter(TimeSpan.FromSeconds(45));
                speech=createSpeech();output=speech;
                if(cancellation.IsCancellationRequested||!Allowed(settings()))return;
                var speaking=speech.SpeakAsync(text,snapshot,cancellation.Token);
                if(await Task.WhenAny(speaking,cancelled.Task)==speaking)await speaking;
                else ObserveFailure(speaking);
            }
            catch(OperationCanceledException){}
            catch(Exception){ /* Proactive speech is optional; the bubble remains usable. */ }
            finally
            {
                if(Object.ReferenceEquals(active,cancellation)){active=null;output=null;}
                if(speech!=null){try{speech.Stop();}catch(Exception){}try{speech.Dispose();}catch(Exception){}}
                cancellation.Dispose();
            }
        }
        static void ObserveFailure(Task task)
        {task.ContinueWith(t=>{var ignored=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
        public void Refresh()
        {
            var current=settings();
            if(!Allowed(current)){Stop();return;}
            if(output!=null)output.SetVolume(current.Volume);
        }
        public void Stop()
        {
            var cancellation=active;var speech=output;active=null;output=null;
            if(cancellation!=null)try{cancellation.Cancel();}catch(ObjectDisposedException){}
            if(speech!=null)try{speech.Stop();}catch(Exception){}
        }
        public void Dispose(){if(disposed)return;disposed=true;Stop();}
    }
}
