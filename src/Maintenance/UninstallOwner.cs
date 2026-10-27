using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Maintenance
{
    // An explicit, non-transferable capability for this one exclusive transaction.
    // It never changes ordinary producer admission and contains no host/UI types.
    internal sealed class UninstallOwner:IDisposable
    {
        readonly DataMaintenanceGate gate;IDisposable lease;bool closed;
        UninstallOwner(DataMaintenanceGate gate,IDisposable lease){this.gate=gate;this.lease=lease;}
        internal static async Task<UninstallOwner> AcquireAsync(DataMaintenanceGate gate)
        {return new UninstallOwner(gate,await gate.BeginAsync());}
        internal void Require(DataMaintenanceGate expected,bool afterClose=false)
        {if(!ReferenceEquals(gate,expected)||lease==null||!gate.IsMaintenanceActive||(afterClose&&!closed))throw new InvalidOperationException("Invalid uninstall owner or phase.");}
        internal void MarkClosed(){Require(gate);closed=true;}
        internal bool IsClosed {get{return closed;}}
        public void Dispose(){var owned=Interlocked.Exchange(ref lease,null);if(owned!=null)owned.Dispose();}
    }
}
