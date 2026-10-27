using System;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    // UI-owned revisions; each callback submits a detached immutable patch.
    internal sealed class PreferenceSaveScheduler:IDisposable
    {
        readonly Func<Task<bool>> save;readonly DispatcherTimer timer;
        bool pending,disposed;long revision;int suspended;Task<bool> active;
        internal PreferenceSaveScheduler(Func<Task<bool>> save,TimeSpan delay)
        {if(save==null)throw new ArgumentNullException("save");this.save=save;timer=new DispatcherTimer(DispatcherPriority.Background){Interval=delay};timer.Tick+=Tick;}
        internal bool HasPendingChanges{get{return pending;}}
        internal void Request(){if(disposed)throw new ObjectDisposedException("PreferenceSaveScheduler");pending=true;revision++;timer.Stop();if(suspended==0)timer.Start();}
        internal void Suspend(){timer.Stop();suspended++;}
        internal void Resume(){if(suspended>0)suspended--;if(suspended==0&&pending){timer.Stop();timer.Start();}}
        internal void MarkSaved(){timer.Stop();revision++;pending=false;}
        internal async Task<bool> FlushAsync()
        {
            timer.Stop();if(active!=null&&!await active)return false;
            while(pending){if(!await StartSaveAsync())return false;}return true;
        }
        Task<bool> StartSaveAsync()
        {
            if(active!=null)return active;
            var completion=new TaskCompletionSource<bool>();active=completion.Task;
            RunSaveAsync(completion,revision);return completion.Task;
        }
        async void RunSaveAsync(TaskCompletionSource<bool> completion,long current)
        {
            try{bool saved=await save();if(saved&&current==revision)pending=false;active=null;completion.SetResult(saved);}
            catch(Exception error){active=null;completion.SetException(error);}
        }
        async void Tick(object sender,EventArgs args){timer.Stop();if(suspended==0&&pending&&await StartSaveAsync()&&pending&&suspended==0&&!disposed)timer.Start();}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;}
    }
}
