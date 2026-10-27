using Dafeiyu.Maintenance;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    public DataMaintenanceGate DataMaintenance {get;}=new DataMaintenanceGate();
}
