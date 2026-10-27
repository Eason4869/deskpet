using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Dafeiyu.Maintenance
{
    public static class DataManagementPresentation
    {
        public static string Describe(HistoryMaintenanceResult result)
        {
            if(result.Status==HistoryMaintenanceStatus.Busy)return TextResources.Get("dataMaintenance.busy");
            if(result.Status==HistoryMaintenanceStatus.Partial)return TextResources.Format("dataManagement.historyPartial",result.RemovedFiles,result.Failures.Count);
            return TextResources.Format("dataManagement.historyComplete",result.RemovedFiles);
        }
        public static Task<bool> HasAttachmentsAsync(string directory)
        {
            return Task.Run(delegate{CachePaths.Check(directory);return Directory.Exists(directory)&&Directory.EnumerateFiles(directory).Any();});
        }
    }
}
