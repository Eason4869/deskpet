using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    public sealed class AgentSubagentScheduler
    {
        readonly AgentSubagentManager manager;readonly int maxConcurrent;readonly object gate=new object();readonly List<AgentSubagentState> queued=new List<AgentSubagentState>();
        public AgentSubagentScheduler(AgentSubagentManager manager,int maxConcurrent){if(manager==null)throw new ArgumentNullException("manager");this.manager=manager;this.maxConcurrent=Math.Max(1,maxConcurrent);}
        public int MaxConcurrent {get{return maxConcurrent;}}
        public event Action<AgentSubagentState> Started;
        public AgentSubagentState Schedule(string parentId,AgentProfile profile,string input)
        {
            if(profile==null || profile.Mode!="subagent")throw new ArgumentException("只有 subagent Profile 可以作为子 Agent 启动。");
            lock(gate)
            {
                if(manager.List(parentId).Count(s=>s.Status=="running")<maxConcurrent){var running=manager.Spawn(parentId,profile,input);if(Started!=null)Started(running);return running;}
                var state=new AgentSubagentState{Id=Guid.NewGuid().ToString("N"),ParentId=parentId??"",ProfileId=profile.Id,Input=input??"",Status="queued"};queued.Add(state);return state;
            }
        }
        public bool Complete(string id,string result)
        {
            bool ok;
            lock(gate)
            {
                var pending=queued.FirstOrDefault(s=>s.Id==id);
                if(pending!=null){queued.Remove(pending);return true;}
                var child=manager.ListAll().FirstOrDefault(s=>s.Id==id);ok=manager.Complete(id,result);
                if(ok && child!=null)PromoteNext(child.ParentId);
            }
            return ok;
        }
        public bool Fail(string id,string error)
        {
            lock(gate)
            {
                var pending=queued.FirstOrDefault(s=>s.Id==id);if(pending!=null){queued.Remove(pending);return true;}
                var child=manager.ListAll().FirstOrDefault(s=>s.Id==id);bool ok=manager.Fail(id,error);if(ok && child!=null)PromoteNext(child.ParentId);return ok;
            }
        }
        public bool Interrupt(string id)
        {
            lock(gate)
            {
                var pending=queued.FirstOrDefault(s=>s.Id==id);if(pending!=null){queued.Remove(pending);return true;}
                var child=manager.ListAll().FirstOrDefault(s=>s.Id==id);bool ok=manager.Interrupt(id);if(ok && child!=null)PromoteNext(child.ParentId);return ok;
            }
        }
        public IList<AgentSubagentState> List(string parentId)
        {
            lock(gate)
            {
                return manager.List(parentId).Concat(queued.Where(s=>s.ParentId==(parentId??""))).OrderBy(s=>s.CreatedUtc).ToArray();
            }
        }
        AgentSubagentState PromoteNext(string parentId)
        {
            if(manager.List(parentId).Count(s=>s.Status=="running")>=maxConcurrent)return null;
            var next=queued.FirstOrDefault(s=>s.ParentId==(parentId??""));if(next==null)return null;queued.Remove(next);
            var running=manager.StartQueued(next);if(Started!=null)Started(running);return running;
        }
    }
}
