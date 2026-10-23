using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        readonly object agentJobServicesGate=new object();
        AgentJobApplicationService agentJobServices;
        internal Action<string> AgentJobExitNotice;
        internal AgentJobApplicationService AgentJobs
        {
            get
            {
                lock(agentJobServicesGate)
                    return agentJobServices??(agentJobServices=new AgentJobApplicationService(Store.DirectoryPath,clock=>new WindowsAgentJobProcessFactory(clock),new ApplicationJobClock()));
            }
        }
        void InitializeAgentJobs(){var ignored=AgentJobs.InitializeAsync();}
        async Task<bool> StopAgentJobsForExitAsync()
        {
            AgentJobApplicationService services;lock(agentJobServicesGate)services=agentJobServices;
            if(services==null)return true;
            var report=await services.StopAllForExitAsync(CancellationToken.None);
            if(report.CleanupConfirmed)return true;
            Notice=TextResources.Get("agentJobs.exitBlocked");
            if(AgentJobExitNotice!=null)AgentJobExitNotice(Notice);
            else MessageBox.Show(Notice,TextResources.Get("app.displayName"),MessageBoxButton.OK,MessageBoxImage.Warning);
            return false;
        }
        void DisposeAgentJobs()
        {lock(agentJobServicesGate)if(agentJobServices!=null)agentJobServices.Dispose();}
        sealed class ApplicationJobClock : IAgentJobClock
        {
            readonly Stopwatch elapsed=Stopwatch.StartNew();
            public DateTime UtcNow {get{return DateTime.UtcNow;}}
            public long ElapsedMilliseconds {get{return elapsed.ElapsedMilliseconds;}}
        }
    }
}
