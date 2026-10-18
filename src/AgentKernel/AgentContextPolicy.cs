using System;
namespace Dafeiyu
{
    public sealed class AgentContextPolicy
    {
        public int Capacity {get;private set;}public int OutputReserve {get;private set;}public int SafetyReserve {get;private set;}public int ToolOverhead {get;private set;}public int InputBudget {get;private set;}public int CompactAt {get;private set;}
        public static AgentContextPolicy Resolve(int capacity,int toolOverhead=0)
        {
            capacity=Math.Max(1024,Math.Min(ModelContextCapacity.Maximum,capacity<=0?32768:capacity));
            var policy=new AgentContextPolicy{Capacity=capacity,OutputReserve=Math.Min(8192,Math.Max(256,capacity/8)),SafetyReserve=Math.Min(2048,Math.Max(128,capacity/32)),ToolOverhead=Math.Max(0,toolOverhead)};
            policy.InputBudget=Math.Max(0,capacity-policy.OutputReserve-policy.SafetyReserve-policy.ToolOverhead);policy.CompactAt=(int)(policy.InputBudget*.9);return policy;
        }
    }
}
