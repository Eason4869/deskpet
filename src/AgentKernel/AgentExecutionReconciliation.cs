using System;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        public async Task AcknowledgeAgentExecutionsAsync(PetSettings settings)
        {
            if(IsBusy)throw new InvalidOperationException("请先停止任务，再核对执行记录。");var state=CurrentAgentTask;if(state==null)return;ValidateTaskProject(state,settings);
            await Task.Run(()=>new AgentRunJournal(AgentWorkspace.RunJournalPath(state.Workspace)).Acknowledge(state.Id)).ConfigureAwait(false);
            state.Status="interrupted";state.Notice="用户已人工核对上次执行结果；继续前仍需读取实际文件与外部状态，不重复旧 CallId。";
            lock(gate)agentTasks[PersonaId]=state;if(taskStore!=null){await taskStore.SaveAsync(state).ConfigureAwait(false);await taskStore.FlushAsync().ConfigureAwait(false);}var changed=AgentTaskChanged;if(changed!=null)changed();
        }
    }
}
