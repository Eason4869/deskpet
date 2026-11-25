using System;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        internal void AcknowledgeRunJournals(string taskId)
        {
            OpenRunJournal(RunJournalPath(Root)).Acknowledge(taskId);
            OpenRunJournal(JobRunJournalPath(Root)).Acknowledge(taskId);
        }
    }
    public sealed partial class ConversationSession
    {
        public async Task AcknowledgeAgentExecutionsAsync(PetSettings settings)
        {
            if(IsBusy)throw new InvalidOperationException(TextResources.Get("agentExecutionReconciliation.acknowledgeAgentExecutionsAsync.text"));
            var op=Begin(settings,ConversationStage.Thinking);
            try
            {
                AgentTaskState state;
                lock(gate)
                {
                    if(!agentTasks.TryGetValue(op.PersonaId,out state))return;
                    state=state.Copy();
                }
                ValidateTaskProject(state,op.Snapshot);
                bool restrict=op.Snapshot.AgentApprovalMode==AgentApprovalMode.Legacy&&!op.Snapshot.AgentFullAccess;
                await Task.Run(()=>
                {
                    using(var workspace=new AgentWorkspace(state.Workspace,restrict))
                        workspace.AcknowledgeRunJournals(state.Id);
                }).ConfigureAwait(false);
                state.Status="interrupted";
                state.Notice=TextResources.Get("agentExecutionReconciliation.acknowledgeAgentExecutionsAsync.stateNotice");
                lock(gate)
                {
                    agentTasks[op.PersonaId]=state;
                    op.History.UpdateAgentTaskSummary(state);
                    Save(op.PersonaId,op.History);
                }
                if(taskStore!=null)await taskStore.SaveAsync(state).ConfigureAwait(false);
                await FlushHistoryAsync().ConfigureAwait(false);
                var changed=AgentTaskChanged;
                if(changed!=null)changed();
            }
            finally{End(op);}
        }
    }
}
