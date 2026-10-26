using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        public DataMaintenanceGate Maintenance {get;private set;}
        IDisposable maintenanceRegistration;
        internal string MaintenanceHistoryDirectory {get{return store==null?null:store.DirectoryPath;}}
        internal string MaintenanceStorageWarning {get{return String.Join(" ",new[]{HistoryWarning,AgentTaskWarning}.Where(s=>!String.IsNullOrEmpty(s)));}}
        internal async Task SaveForUninstallAsync(UninstallOwner owner)
        {
            owner.Require(Maintenance);
            FlushDraft();
            await WaitForIdleAsync().ConfigureAwait(false);
            // The owner blocks all writers. Save cached personas as well as the visible one.
            KeyValuePair<string,ConversationLog>[] logs;lock(gate)logs=histories.ToArray();
            foreach(var entry in logs){owner.Require(Maintenance);Save(entry.Key,entry.Value,owner);}
            await FlushHistoryAsync().ConfigureAwait(false);owner.Require(Maintenance);
            if(!String.IsNullOrEmpty(MaintenanceStorageWarning)||(taskStore!=null&&taskStore.MaintenancePending().Length!=0))throw new System.IO.IOException(TextResources.Get("dataMaintenance.storageFailure"));
        }
        internal IList<ChatMessage> MaintenanceMessages()
        {lock(gate)return histories.Values.SelectMany(log=>log.Messages).ToList();}
        internal AgentTaskState[] MaintenanceTasks()
        {lock(gate)return agentTasks.Values.Select(s=>s.Copy()).Concat(taskStore==null?new AgentTaskState[0]:taskStore.MaintenancePending()).ToArray();}
        // Called only by the exclusive maintenance owner, after the storage tail
        // and attachment preservation. Does not reacquire producer admission.
        internal void ResetContextsForMaintenance()
        {
            lock(gate){agentTasks.Clear();if(taskStore!=null)taskStore.ForgetForMaintenance();contextUsage.Clear();predictionCacheKey=null;predictionCache=new string[0];LastNotice="";
                if(kernelSession!=null){kernelSession.Pending.Clear();kernelSession.Delivered.Clear();kernelSession.Events.Clear();kernelSession.Step=kernelSession.Sequence=0;kernelSession.RecoveryState="idle";}}
            CancelPredictions();
        }
        internal void ResetHistoriesForMaintenance(ISet<string> removed)
        {
            lock(gate)foreach(var pair in histories){string path=store==null?null:store.MaintenancePath(pair.Key);if(path!=null&&!removed.Contains(path))continue;pair.Value.Clear();pair.Value.ReconcileImageUsage();if(store!=null)store.ForgetRemovedForMaintenance(path);}
        }
        internal void MarkAbsentHistoriesForMaintenance(ISet<string> removed)
        {lock(gate)if(store!=null)foreach(string persona in histories.Keys){string path=store.MaintenancePath(persona);if(!System.IO.File.Exists(path))removed.Add(path);}}
        internal void NotifyMaintenanceChanged()
        {if(HistoryChanged!=null)HistoryChanged();if(AgentTaskChanged!=null)AgentTaskChanged();if(ContextUsageChanged!=null)ContextUsageChanged();}
        async Task DrainMaintenanceAsync()
        {
            await WaitForIdleAsync().ConfigureAwait(false);
            await Task.WhenAll(store==null?Task.FromResult(0):store.FlushAsync(),taskStore==null?Task.FromResult(0):taskStore.FlushAsync()).ConfigureAwait(false);
        }
        void RetireMaintenanceRegistration()
        {
            // Disposed sessions may still own an enqueued checkpoint or a late
            // cancelled operation. Keep the participant visible until both join.
            DrainMaintenanceAsync().ContinueWith(done=>{var observed=done.Exception;maintenanceRegistration.Dispose();},TaskScheduler.Default);
        }
    }
}
