using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class AgentJobCatalogGuard
    {
        readonly AgentJobApplicationService services;
        public AgentJobCatalogGuard(AgentJobApplicationService services)
        {if(services==null)throw new ArgumentNullException("services");this.services=services;}
        public Task RunAsync(IEnumerable<string> projectIds,string conversationId,bool stopActive,Func<Task> mutation,CancellationToken token)
        {
            if(projectIds==null||mutation==null)throw new ArgumentNullException("mutation");
            var projects=projectIds.Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
            if(projects.Length==0||projects.Any(String.IsNullOrWhiteSpace))throw new ArgumentException("projectIds");
            return services.RunMutationAsync(manager=>EnterAsync(manager,projects,0,conversationId,stopActive,mutation,token),token);
        }
        public bool TryRun(IEnumerable<string> projectIds,string conversationId,Action mutation)
        {
            if(projectIds==null||mutation==null)throw new ArgumentNullException("mutation");
            var projects=projectIds.Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
            if(projects.Length==0||projects.Any(String.IsNullOrWhiteSpace))throw new ArgumentException("projectIds");
            return services.TryRunMutation(manager=>TryEnter(manager,projects,0,conversationId,mutation));
        }
        static bool TryEnter(AgentJobManager manager,string[] projects,int index,string conversationId,Action mutation)
        {
            if(index==projects.Length){mutation();return true;}
            bool entered=false;
            return manager.TryRunOwnerMutation(projects[index],conversationId,()=>entered=TryEnter(manager,projects,index+1,conversationId,mutation))&&entered;
        }
        static Task EnterAsync(AgentJobManager manager,string[] projects,int index,string conversationId,bool stopActive,Func<Task> mutation,CancellationToken token)
        {
            if(index==projects.Length){token.ThrowIfCancellationRequested();return mutation();}
            return manager.RunOwnerMutationAsync(projects[index],conversationId,stopActive,
                ()=>EnterAsync(manager,projects,index+1,conversationId,stopActive,mutation,token),token);
        }
    }
}
