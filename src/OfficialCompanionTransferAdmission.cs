using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    // Two active jobs; foreground waiters always precede background waiters.
    internal sealed class OfficialCompanionTransferAdmission
    {
        readonly object gate=new object();readonly List<Waiter> waiting=new List<Waiter>();int active;
        sealed class Waiter
        {
            internal Func<bool> Background;internal readonly TaskCompletionSource<IDisposable> Ready=new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal CancellationTokenRegistration Registration;
        }
        sealed class Permit : IDisposable
        {
            OfficialCompanionTransferAdmission owner;internal Permit(OfficialCompanionTransferAdmission owner){this.owner=owner;}
            public void Dispose(){var current=Interlocked.Exchange(ref owner,null);if(current!=null)current.Release();}
        }
        internal Task<IDisposable> AcquireAsync(Func<bool> background,CancellationToken cancel)
        {
            lock(gate){cancel.ThrowIfCancellationRequested();if(active<2){active++;return Task.FromResult<IDisposable>(new Permit(this));}
                var waiter=new Waiter{Background=background};waiting.Add(waiter);
                waiter.Registration=cancel.Register(()=>{lock(gate){if(waiting.Remove(waiter))waiter.Ready.TrySetCanceled();}});
                // Dispose outside the cancellation callback and admission lock.
                waiter.Ready.Task.ContinueWith(t=>waiter.Registration.Dispose(),CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);return waiter.Ready.Task;
            }
        }
        void Release()
        {
            lock(gate){if(waiting.Count==0){active--;return;}var next=waiting.FirstOrDefault(x=>!x.Background())??waiting[0];waiting.Remove(next);next.Ready.TrySetResult(new Permit(this));}
        }
    }
}
