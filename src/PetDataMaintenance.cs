namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        readonly Maintenance.DataMaintenanceGate dataMaintenance=new Maintenance.DataMaintenanceGate();
        System.IDisposable exitMaintenanceActivity;
        public Maintenance.DataMaintenanceGate DataMaintenance {get{return dataMaintenance;}}
        bool AdmitMaintenanceExit()
        {
            if(DataMaintenance.TryEnterActivity(out exitMaintenanceActivity))return true;
            Notice=TextResources.Get("dataMaintenance.busy");return false;
        }
        void ReleaseMaintenanceExit()
        {var admission=System.Threading.Interlocked.Exchange(ref exitMaintenanceActivity,null);if(admission!=null)admission.Dispose();}
    }
}
