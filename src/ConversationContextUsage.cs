using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace Dafeiyu
{
    internal sealed class ContextUsageSnapshot
    {
        internal string Model="",Endpoint="",Status="";internal int Tokens,Capacity,Budget,Compactions;
        internal ContextUsageSnapshot Copy(){return (ContextUsageSnapshot)MemberwiseClone();}
    }
    public sealed partial class ConversationSession
    {
        readonly Dictionary<string,ContextUsageSnapshot> contextUsage=new Dictionary<string,ContextUsageSnapshot>();
        internal event Action ContextUsageChanged;
        internal ContextUsageSnapshot CurrentContextUsage{get{lock(gate){ContextUsageSnapshot value;return contextUsage.TryGetValue(personaId,out value)?value.Copy():null;}}}
        void PublishContextUsage(Operation op,int tokens,int capacity,int budget,string status,int compactions)
        {
            lock(gate)contextUsage[op.PersonaId]=new ContextUsageSnapshot{Model=op.Snapshot.Text.Model,Endpoint=op.Snapshot.Text.BaseUrl,Tokens=tokens,Capacity=capacity,Budget=budget,Status=status,Compactions=compactions};var changed=ContextUsageChanged;if(changed!=null)changed();
        }
        async Task PreparePlainContextAsync(Operation op,List<ChatMessage> request)
        {
            bool anthropic=op.Snapshot.Text.Protocol=="anthropic";string system=anthropic?String.Join("\n",request.Where(m=>m.Role=="system").Select(m=>m.Content)).Trim():"";var included=request.Where(m=>!anthropic||m.Role!="system").ToList();var history=included.Select(m=>(object)new Dictionary<string,object>{{"role",m.Role},{"content",ChatAttachment.Content(m,anthropic)}}).ToList();
            var plainOriginals=new Dictionary<object,ChatMessage>();for(int i=0;i<history.Count;i++)plainOriginals[history[i]]=included[i];
            int budget=ModelContextCapacity.Budget(op.Snapshot.Text,op.Snapshot.AgentContextTokenLimit);
            var context=AgentWorkspace.ForContextBudget(budget);context.BeginTask(new AgentTaskState{Workspace=context.Root,Goal=request.Last().Content});
            context.TaskChanged=task=>PublishContextUsage(op,task.EstimatedContextTokens,op.Snapshot.Text.ContextWindowTokens,AgentContextPolicy.Resolve(budget).InputBudget,task.ContextStatus,task.ContextCompactions);
            bool compacted=await AgentContextBudget.PrepareAsync(history,anthropic,context,async (prior,ct)=>{using(UsageTracking.BeginScope("compaction"))return await api.ChatAsync(op.Snapshot.Text,new[]{new ChatMessage("system",ReplyLanguagePolicy.Apply(TextResources.Get("conversationContextUsage.preparePlainContextAsync.apiChatAsync"))),new ChatMessage("user",prior)},ct).ConfigureAwait(false);},op.Cancel.Token,items=>AgentContextBudget.FitsWireBytes(ApiClient.PlainRequestBody(op.Snapshot.Text,items,system,op.Snapshot.Text.StreamReplies)),AgentContextBudget.EstimateTokens(new object[]{ApiClient.PlainRequestBody(op.Snapshot.Text,new List<object>(),system,false)})).ConfigureAwait(false);
            if(compacted){request.Clear();if(anthropic&&!String.IsNullOrEmpty(system))request.Add(new ChatMessage("system",system));foreach(Dictionary<string,object> item in history){string role=(string)item["role"];object content=item["content"];if(content is string)request.Add(new ChatMessage(role,(string)content));else{ChatMessage retained; if(!plainOriginals.TryGetValue(item,out retained))throw new InvalidOperationException(TextResources.Get("conversationContextUsage.preparePlainContextAsync.text"));request.Add(retained.Copy());}}}
            PublishContextUsage(op,AgentContextBudget.EstimateTokens(history),op.Snapshot.Text.ContextWindowTokens,AgentContextPolicy.Resolve(budget).InputBudget,context.TaskState.ContextStatus,context.TaskState.ContextCompactions);
        }
    }
}
