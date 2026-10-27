using System;
using System.Windows.Threading;

namespace Dafeiyu
{
    // UI-owned dirty state only. The callback reads current settings at commit time.
    internal sealed class PreferenceSaveScheduler:IDisposable
    {
        readonly Func<bool> save;readonly DispatcherTimer timer;
        bool pending,disposed;long revision;int suspended;
        internal PreferenceSaveScheduler(Func<bool> save,TimeSpan delay)
        {if(save==null)throw new ArgumentNullException("save");this.save=save;timer=new DispatcherTimer(DispatcherPriority.Background){Interval=delay};timer.Tick+=Tick;}
        internal bool HasPendingChanges{get{return pending;}}
        internal void Request(){if(disposed)throw new ObjectDisposedException("PreferenceSaveScheduler");pending=true;revision++;timer.Stop();if(suspended==0)timer.Start();}
        internal void Suspend(){timer.Stop();suspended++;}
        internal void Resume(){if(suspended>0)suspended--;if(suspended==0&&pending){timer.Stop();timer.Start();}}
        internal void MarkSaved(){timer.Stop();pending=false;}
        internal bool Flush()
        {
            timer.Stop();if(!pending)return true;long current=revision;
            bool saved=save();if(saved&&current==revision)pending=false;return saved;
        }
        void Tick(object sender,EventArgs args){Flush();}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;}
    }
}
