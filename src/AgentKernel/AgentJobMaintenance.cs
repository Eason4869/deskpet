using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dafeiyu
{
    // Callers may stop waiting at a deadline; synchronous storage still owns its
    // lifetime. Register real tasks before releasing the producer's admission.
    internal sealed class AgentJobPendingStorage
    {
        readonly object gate=new object();
        readonly HashSet<Task> pending=new HashSet<Task>();
        internal Task Track(Task task)
        {
            lock(gate)pending.Add(task);
            task.ContinueWith(done=>{var observed=done.Exception;lock(gate)pending.Remove(done);},TaskScheduler.Default);
            return task;
        }
        internal async Task DrainAsync()
        {
            while(true)
            {
                Task[] snapshot;lock(gate)snapshot=pending.ToArray();
                if(snapshot.Length==0)return;
                // Failures remain the original caller's result. Maintenance only
                // joins settlement here; it never changes the preserved job store.
                try{await Task.WhenAll(snapshot).ConfigureAwait(false);}catch(Exception){}
                lock(gate)foreach(var task in snapshot)if(task.IsCompleted)pending.Remove(task);
            }
        }
    }
    public sealed partial class AgentJobStore
    {
        internal bool HasUninstallStorageFailure
        {get{AgentJobLogStore[] opened;lock(ioGate){if(warnings.Count!=0)return true;opened=logs.Values.ToArray();}return opened.Any(log=>log.Failure!=null);}}
        readonly AgentJobPendingStorage maintenanceWrites=new AgentJobPendingStorage();
        internal Task TrackMaintenance(Task task){return maintenanceWrites.Track(task);}
        internal async Task DrainMaintenanceAsync()
        {
            await maintenanceWrites.DrainAsync().ConfigureAwait(false);
            // Dispose and metadata commits can own ioGate. Never acquire it on
            // the UI thread or while holding the manager / admission locks.
            var opened=await Task.Run(()=>{lock(ioGate)return logs.Values.ToArray();}).ConfigureAwait(false);
            await Task.WhenAll(opened.Select(log=>log.DrainMaintenanceAsync())).ConfigureAwait(false);
        }
    }
    public sealed partial class AgentJobLogStore
    {
        readonly AgentJobPendingStorage maintenanceFlushes=new AgentJobPendingStorage();
        internal async Task DrainMaintenanceAsync()
        {
            Task queued;bool closing;lock(appendGate){queued=drained.Task;closing=stopping;}
            try{await queued.ConfigureAwait(false);}catch(Exception){}
            await maintenanceFlushes.DrainAsync().ConfigureAwait(false);
            if(closing)try{await writer.ConfigureAwait(false);}catch(Exception){}
        }
    }
}
