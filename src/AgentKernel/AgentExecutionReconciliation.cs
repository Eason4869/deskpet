using System;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        public async Task AcknowledgeAgentExecutionsAsync(PetSettings settings)
        {
            if(IsBusy)throw new InvalidOperationException(TextResources.Get("agentExecutionReconciliation.acknowledgeAgentExecutionsAsync.text"));var state=CurrentAgentTask;if(state==null)return;ValidateTaskProject(state,settings);
            await Task.Run(()=>new AgentRunJournal(AgentWorkspace.RunJournalPath(state.Workspace)).Acknowledge(state.Id)).ConfigureAwait(false);
            state.Status="interrupted";state.Notice=TextResources.Get("agentExecutionReconciliation.acknowledgeAgentExecutionsAsync.stateNotice");
            lock(gate)agentTasks[PersonaId]=state;if(taskStore!=null){await taskStore.SaveAsync(state).ConfigureAwait(false);await taskStore.FlushAsync().ConfigureAwait(false);}var changed=AgentTaskChanged;if(changed!=null)changed();
        }
    }
}
