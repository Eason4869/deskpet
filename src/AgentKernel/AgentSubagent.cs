using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    public sealed class AgentSubagentState
    {
        public string Id,ParentId,ProfileId,Input,Status="running",Result;public DateTime CreatedUtc=DateTime.UtcNow,UpdatedUtc=DateTime.UtcNow;
    }
    public sealed class AgentSubagentManager
    {
        readonly object gate=new object();readonly Dictionary<string,AgentSubagentState> states=new Dictionary<string,AgentSubagentState>();readonly ExecutionClaimStore claims;
        public AgentSubagentManager(ExecutionClaimStore claims){if(claims==null)throw new ArgumentNullException("claims");this.claims=claims;}
        public AgentSubagentState Spawn(string parentId,AgentProfile profile,string input)
        {
            if(profile==null || profile.Mode!="subagent")throw new ArgumentException("只有 subagent Profile 可以作为子 Agent 启动。");
            var state=new AgentSubagentState{Id=Guid.NewGuid().ToString("N"),ParentId=parentId,ProfileId=profile.Id,Input=input??""};lock(gate)states[state.Id]=state;claims.TryClaim(state.Id,state.Id);return state;
        }
        internal AgentSubagentState StartQueued(AgentSubagentState state)
        {
            if(state==null || String.IsNullOrWhiteSpace(state.Id))throw new ArgumentException("排队子 Agent 状态无效。");
            var profile=new AgentProfileRegistry().Get(state.ProfileId);if(profile==null || profile.Mode!="subagent")throw new ArgumentException("排队子 Agent Profile 无效。");
            lock(gate){if(states.ContainsKey(state.Id))throw new InvalidOperationException("子 Agent ID 已存在。");state.Status="running";state.UpdatedUtc=DateTime.UtcNow;states[state.Id]=state;claims.TryClaim(state.Id,state.Id);return state;}
        }
        public bool Interrupt(string id){lock(gate){AgentSubagentState state;if(!states.TryGetValue(id,out state)||state.Status!="running")return false;state.Status="interrupted";state.UpdatedUtc=DateTime.UtcNow;return true;}}
        public bool Complete(string id,string result){lock(gate){AgentSubagentState state;if(!states.TryGetValue(id,out state)||(state.Status!="running"&&state.Status!="interrupted"))return false;state.Status="complete";state.Result=result??"";state.UpdatedUtc=DateTime.UtcNow;claims.Complete(id,id);return true;}}
        public bool Fail(string id,string error){lock(gate){AgentSubagentState state;if(!states.TryGetValue(id,out state)||state.Status!="running")return false;state.Status="failed";state.Result=error??"";state.UpdatedUtc=DateTime.UtcNow;claims.Fail(id,id);return true;}}
        public IList<AgentSubagentState> List(string parentId){lock(gate)return states.Values.Where(s=>s.ParentId==parentId).OrderBy(s=>s.CreatedUtc).ToArray();}
    }
}
