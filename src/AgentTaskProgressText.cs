using System.Linq;

namespace Dafeiyu
{
    internal static class AgentTaskProgressText
    {
        internal static string Status(AgentTaskState task)
        {
            switch(task.Status)
            {
                case "running":
                    if(task.ContextStatus=="compacting")
                        return TextResources.Get("agentTaskPanel.refresh.contextCompactionsTaskContextCompactions.alternativeA");
                    if(task.Tools.Any(tool=>tool.Status=="running"))
                        return TextResources.Get("agentTaskPanel.refresh.taskToolsLastOrDefault.alternativeA");
                    return TextResources.Get("agentTaskPanel.refresh.taskToolsLastOrDefault");
                case "complete":return TextResources.Get("agentTaskPanel.refresh.statusTaskStatus");
                case "cancelled":return TextResources.Get("agentTaskPanel.refresh.statusTaskStatus.alternativeA");
                case "interrupted":return TextResources.Get("agentTaskPanel.refresh.statusTaskStatus.alternativeB");
                default:return TextResources.Get("agentTaskPanel.refresh.statusTaskStatus.alternativeC");
            }
        }

        internal static string RequestCount(AgentTaskState task)
        {
            return TextResources.Get("usageStatisticsPage.renderMetrics.metric.alternativeI")+" · "+task.ModelRequests;
        }

        internal static string TokenEstimate(AgentTaskState task)
        {
            return TextResources.Format("agentTaskPanel.refresh.template.alternativeA",task.EstimatedTotalTokens,task.ChildEstimatedTotalTokens);
        }

        internal static string Notice(AgentTaskState task)
        {
            return task.Status=="needs_reconciliation"?TextResources.Get("agentTaskState.beginTask.taskNotice"):task.Notice??"";
        }
    }
}
