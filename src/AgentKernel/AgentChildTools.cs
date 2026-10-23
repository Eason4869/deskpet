using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        AgentSubagentRunner childWorkers;int childToolCalls;
        public int ChildToolBudget {get;set;}
        internal Task StopChildWorkersAsync(){return childWorkers==null?Task.FromResult(0):childWorkers.StopAsync();}
        internal void ConfigureWorkers(Func<IAgentConversationApi> factory,ConnectionSettings model)
        {
            if(factory==null||subagentScheduler==null)return;
            childWorkers=new AgentSubagentRunner(this,subagentScheduler,factory,model);childWorkers.Changed+=PublishChildren;
            kernelPermissions.Add(new AgentPermissionRule{Action="subagent",Resource="**",Effect=AgentPermissionEffect.Allow});
            dynamicTools.Add(new AgentToolDefinition{Name="get_subagent",Description=TextResources.Get("agentChildTools.configureWorkers.description"),ReadOnly=true,Properties=new Dictionary<string,object>{{"id",new{type="string"}}},Required=new[]{"id"}});
            dynamicTools.Add(new AgentToolDefinition{Name="wait_subagents",Description=TextResources.Get("agentChildTools.configureWorkers.description.alternativeA"),ReadOnly=true,Properties=new Dictionary<string,object>{{"ids",new{type="array",items=new{type="string"},maxItems=64}},{"timeout_seconds",new{type="integer"}}},Required=new[]{"ids"}});BuildSchemas();
        }
        void PublishChildren(){EnsureTask();lock(taskGate){task.Children=childWorkers.List().ToList();task.ChildEstimatedTotalTokens=task.Children.Sum(c=>c.EstimatedTotalTokens);}PublishTask();}
        async Task<string> ExecuteChildToolAsync(string name,Dictionary<string,object> args,CancellationToken cancel)
        {
            if(childWorkers==null)throw new InvalidOperationException(TextResources.Get("agentChildTools.executeChildToolAsync.text"));object result;
            if(name=="spawn_subagent"){
                string profile=GetString(args,"profile","explore");if(profile!="explore")throw new UnauthorizedAccessException(TextResources.Get("agentChildTools.executeChildToolAsync.text.alternativeA"));
                result=childWorkers.Spawn(kernelTaskId,GetString(args,"input",null));PublishChildren();
            }else if(name=="interrupt_subagent"){result=new{stopped=childWorkers.Interrupt(GetString(args,"id",null))};PublishChildren();}
            else if(name=="get_subagent"){string id=GetString(args,"id",null);result=childWorkers.List().SingleOrDefault(c=>c.Id==id);if(result==null)throw new UnauthorizedAccessException(TextResources.Get("agentChildTools.executeChildToolAsync.text.alternativeB"));}
            else{object raw;if(!args.TryGetValue("ids",out raw)||!(raw is object[]))throw new ArgumentException(TextResources.Get("agentChildTools.executeChildToolAsync.text.alternativeC"));var ids=((object[])raw).Select(v=>v as string).ToArray();result=await childWorkers.WaitAsync(ids,GetInt(args,"timeout_seconds",10,0,30),cancel).ConfigureAwait(false);}
            PublishChildren();return new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(result);
        }
    }
}
