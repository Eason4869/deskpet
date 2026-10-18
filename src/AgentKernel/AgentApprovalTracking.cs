using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    internal sealed class AgentApprovalTracking:ICancellableAgentApprovalProvider
    {
        readonly IAgentApprovalProvider inner;readonly Action<string> activity;
        internal AgentApprovalTracking(IAgentApprovalProvider inner,Action<string> activity){this.inner=inner;this.activity=activity;}
        public Task<bool> ApproveAsync(AgentApprovalRequest request){return ApproveAsync(request,CancellationToken.None);}
        public async Task<bool> ApproveAsync(AgentApprovalRequest request,CancellationToken cancel)
        {
            activity("waiting");try{var provider=inner as ICancellableAgentApprovalProvider;return await AgentAuthorizationService.AwaitCancellation(provider==null?inner.ApproveAsync(request):provider.ApproveAsync(request,cancel),cancel).ConfigureAwait(false);}finally{activity("running");}
        }
    }
}
