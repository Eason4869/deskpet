using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Maintenance
{
    // One instance is shared by every producer owned by an application data root.
    // Never invoke foreign code or wait for I/O while holding this gate's lock.
    public sealed class DataMaintenanceGate
    {
        readonly object sync=new object();
        readonly List<Participant> participants=new List<Participant>();
        int activities;bool maintaining;
        public bool IsMaintenanceActive {get{lock(sync)return maintaining;}}
        static InvalidOperationException Busy(){return new InvalidOperationException(TextResources.Get("dataMaintenance.busy"));}
        public IDisposable EnterActivity()
        {
            IDisposable activity;if(!TryEnterActivity(out activity))throw Busy();return activity;
        }
        public bool TryEnterActivity(out IDisposable activity)
        {lock(sync){activity=null;if(maintaining)return false;activities++;activity=new ReleaseOnce(delegate{lock(sync)activities--;});return true;}}
        public IDisposable Register(Func<Task> drain,Func<bool> busy=null,object owner=null)
        {
            if(drain==null)throw new ArgumentNullException("drain");
            lock(sync){if(maintaining)throw Busy();var participant=new Participant{Drain=drain,Busy=busy,Owner=owner};participants.Add(participant);return new ReleaseOnce(delegate{lock(sync)participants.Remove(participant);});}
        }
        // A successful maintenance lease makes this snapshot stable against new
        // participants. Retiring participants may disappear only after draining.
        public T[] GetParticipants<T>() where T:class
        {lock(sync)return participants.Select(p=>p.Owner).OfType<T>().Distinct().ToArray();}
        // Reservation happens synchronously, before yielding to a worker. New
        // producers cannot enter during the busy recheck or asynchronous drain.
        // Success transfers the lease to the caller; every failure releases it.
        public Task<IDisposable> BeginAsync()
        {
            Participant[] snapshot;IDisposable lease;
            lock(sync)
            {
                if(maintaining||activities!=0)throw Busy();
                maintaining=true;snapshot=participants.ToArray();
                lease=new ReleaseOnce(delegate{lock(sync)maintaining=false;});
            }
            return Task.Run(async delegate
            {
                try
                {
                    foreach(var participant in snapshot)if(participant.Busy!=null&&participant.Busy())throw Busy();
                    await Task.WhenAll(snapshot.Select(p=>p.Drain())).ConfigureAwait(false);
                    return lease;
                }
                catch{lease.Dispose();throw;}
            });
        }
        sealed class Participant {internal Func<Task> Drain;internal Func<bool> Busy;internal object Owner;}
        sealed class ReleaseOnce:IDisposable
        {
            Action release;internal ReleaseOnce(Action action){release=action;}
            public void Dispose(){var action=Interlocked.Exchange(ref release,null);if(action!=null)action();}
        }
    }
}
