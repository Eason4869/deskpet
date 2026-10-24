using System;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        public DataMaintenanceGate Maintenance {get;private set;}
        IDisposable maintenanceRegistration;
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
