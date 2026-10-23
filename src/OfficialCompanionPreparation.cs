using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public enum OfficialCompanionPreparationState { Idle, Running, Completed, Failed, Cancelled }
    // Owned by the application. Opening or closing a view never starts or cancels a transfer.
    public sealed class OfficialCompanionPreparation : IDisposable
    {
        readonly object gate=new object();
        readonly Func<IProgress<OfficialCompanionProgress>,CancellationToken,Task> download;
        readonly CancellationToken lifetime;
        CancellationTokenSource cancellation;TaskCompletionSource<bool> running;
        OfficialCompanionPreparationState state;OfficialCompanionProgress progress;bool disposed;
        public event Action Changed;
        public OfficialCompanionPreparation(Func<IProgress<OfficialCompanionProgress>,CancellationToken,Task> download,CancellationToken lifetime)
        {if(download==null)throw new ArgumentNullException("download");this.download=download;this.lifetime=lifetime;}
        public OfficialCompanionPreparationState State {get{lock(gate)return state;}}
        public OfficialCompanionProgress Progress {get{lock(gate)return progress;}}
        public Task StartAsync()
        {
            TaskCompletionSource<bool> completion;CancellationToken token;
            lock(gate){if(disposed)throw new ObjectDisposedException("OfficialCompanionPreparation");if(state==OfficialCompanionPreparationState.Running)return running.Task;
                lifetime.ThrowIfCancellationRequested();if(cancellation!=null)cancellation.Dispose();cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime);token=cancellation.Token;
                state=OfficialCompanionPreparationState.Running;progress=null;completion=running=new TaskCompletionSource<bool>();}
            Notify();var ignored=RunAsync(completion,token);return completion.Task;
        }
        async Task RunAsync(TaskCompletionSource<bool> completion,CancellationToken token)
        {
            var result=OfficialCompanionPreparationState.Completed;
            try{await download(new OfficialProgress<OfficialCompanionProgress>(value=>Report(completion,value)),token).ConfigureAwait(false);token.ThrowIfCancellationRequested();}
            catch(OperationCanceledException){result=OfficialCompanionPreparationState.Cancelled;}
            catch(Exception){result=OfficialCompanionPreparationState.Failed;}
            lock(gate){if(Object.ReferenceEquals(running,completion))state=result;}
            Notify();completion.TrySetResult(true);
        }
        void Report(TaskCompletionSource<bool> operation,OfficialCompanionProgress value)
        {lock(gate){if(disposed||state!=OfficialCompanionPreparationState.Running||!Object.ReferenceEquals(running,operation))return;progress=value;}Notify();}
        void Notify(){var changed=Changed;if(changed!=null)changed();}
        public void Cancel(){CancellationTokenSource current;lock(gate)current=cancellation;if(current!=null)current.Cancel();}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;}Cancel();}
        public string StatusText
        {
            get{
                switch(State){case OfficialCompanionPreparationState.Running:var value=Progress;return TextResources.Format("onboarding.prepare.running",value==null?0:(int)value.Percent)+(value!=null&&value.TotalBytes>0?String.Format(" ({0:0.0} / {1:0.0} MB)",value.CompletedBytes/1048576d,value.TotalBytes/1048576d):"");
                    case OfficialCompanionPreparationState.Completed:return TextResources.Get("onboarding.prepare.complete");
                    case OfficialCompanionPreparationState.Failed:return TextResources.Get("onboarding.prepare.failed");
                    case OfficialCompanionPreparationState.Cancelled:return TextResources.Get("onboarding.prepare.cancelled");
                    default:return TextResources.Get("onboarding.prepare.packsDetail");}
            }
        }
    }
}
