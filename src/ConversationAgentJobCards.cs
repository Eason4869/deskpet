using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal sealed partial class ChatMessageView
    {
        readonly StackPanel agentJobsHost=new StackPanel{Name="ConversationAgentJobCards"};
        AgentJobsPanel agentJobsPanel;string agentJobReply;
        void RefreshAgentJobCards(ChatMessage message)
        {
            if(user||message==null||!(message.ToolEvents??new System.Collections.Generic.List<AgentToolEvent>()).Any(item=>item.Name=="start_job"))return;
            if(agentJobsPanel!=null&&agentJobReply==message.Id)return;
            var workspace=pet.Workspaces.AllOwnedWorkspaces.FirstOrDefault(owner=>ReferenceEquals(owner.Session,session));if(workspace==null)return;
            if(agentJobsPanel!=null)agentJobsPanel.Dispose();agentJobsHost.Children.Clear();agentJobReply=message.Id;
            agentJobsPanel=new AgentJobsPanel(pet.AgentJobs,workspace.ProjectId,workspace.Id,message.Id,true,uri=>{var chat=host as ChatWindow;if(chat!=null)chat.OpenBrowserLink(uri);else pet.OpenChatBrowserLink(uri);});agentJobsHost.Children.Add(agentJobsPanel);
        }
    }
}
