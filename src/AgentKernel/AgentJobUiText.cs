using System;
using System.IO;
namespace Dafeiyu
{
    public static class AgentJobUiText
    {
        public static bool IsActive(AgentJobSnapshot job){return !AgentJobStore.IsTerminal(job.State);}
        public static string State(AgentJobState state)
        {
            switch(state)
            {
                case AgentJobState.Starting:return TextResources.Get("agent.jobs.ui.state.starting");
                case AgentJobState.Running:return TextResources.Get("agent.jobs.ui.state.running");
                case AgentJobState.Stopping:return TextResources.Get("agent.jobs.ui.state.stopping");
                case AgentJobState.Succeeded:return TextResources.Get("agent.jobs.ui.state.succeeded");
                case AgentJobState.Failed:return TextResources.Get("agent.jobs.ui.state.failed");
                case AgentJobState.Stopped:return TextResources.Get("agent.jobs.ui.state.stopped");
                case AgentJobState.TimedOut:return TextResources.Get("agent.jobs.ui.state.timedOut");
                case AgentJobState.Interrupted:return TextResources.Get("agent.jobs.ui.state.interrupted");
                case AgentJobState.CleanupFailed:return TextResources.Get("agent.jobs.ui.state.cleanupFailed");
                default:return TextResources.Get("agent.jobs.ui.state.startFailed");
            }
        }
        public static string Error(Exception error)
        {
            switch(error.Message)
            {
                case "job_owner_busy":return TextResources.Get("agent.jobs.ui.bindingBusy");
                case "job_catalog_changed":return TextResources.Get("agent.jobs.ui.bindingChanged");
                case "jobs_recovering":return TextResources.Get("agent.jobs.ui.recovering");
                case "job_storage_unavailable":return TextResources.Get("agent.jobs.ui.unavailable");
                case "jobs_exiting":return TextResources.Get("agentJobs.exitBlocked");
                case "job_cleanup_unconfirmed":return TextResources.Get("agent.jobs.ui.stopError");
                default:return error.Message;
            }
        }
    }
}
