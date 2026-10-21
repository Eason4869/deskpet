using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Dafeiyu
{
    // Requests outlive the view. A decision always completes its original call,
    // even when the user switches chats while approval is pending.
    internal sealed class AgentApprovalController:ICancellableAgentApprovalProvider
    {
        internal sealed class Pending
        {
            internal readonly AgentApprovalRequest Request;
            internal readonly TaskCompletionSource<bool> Result=new TaskCompletionSource<bool>();
            internal readonly CancellationToken Cancel;
            internal Pending(AgentApprovalRequest request,CancellationToken cancel){Request=request;Cancel=cancel;}
            internal void Decide(bool allow){if(Cancel.IsCancellationRequested)Result.TrySetCanceled();else Result.TrySetResult(allow);}
        }
        readonly Window owner;readonly List<Pending> pending=new List<Pending>();bool ownerClosed;
        internal event Action Changed;
        internal IList<Pending> Requests{get{return pending.Where(p=>!p.Result.Task.IsCompleted).ToArray();}}
        internal AgentApprovalController(Window owner){this.owner=owner;owner.Closed+=delegate{ownerClosed=true;foreach(var item in pending.ToArray())item.Result.TrySetCanceled();};}
        void Announce(){if(Changed!=null)Changed();}
        public Task<bool> ApproveAsync(AgentApprovalRequest request){return ApproveAsync(request,CancellationToken.None);}
        public async Task<bool> ApproveAsync(AgentApprovalRequest request,CancellationToken cancel)
        {
            if(request==null)throw new ArgumentNullException("request");cancel.ThrowIfCancellationRequested();
            var snapshot=new AgentApprovalRequest{Id=request.Id,Action=request.Action,Resource=request.Resource,Risk=request.Risk,PayloadHash=request.PayloadHash,SessionId=request.SessionId,ProjectId=request.ProjectId,TaskId=request.TaskId,CallId=request.CallId,ToolName=request.ToolName,Arguments=request.Arguments,UserIntent=request.UserIntent,IsolationDescription=request.IsolationDescription,ReviewReason=request.ReviewReason};
            var item=new Pending(snapshot,cancel);
            var scheduled=owner.Dispatcher.BeginInvoke(new Action(delegate
            {
                if(cancel.IsCancellationRequested||ownerClosed){item.Result.TrySetCanceled();return;}
                pending.Add(item);Announce();
            }));
            try{using(cancel.Register(()=>item.Result.TrySetCanceled()))return await item.Result.Task.ConfigureAwait(false);}
            finally
            {
                if(!owner.Dispatcher.HasShutdownStarted){var cleanup=owner.Dispatcher.BeginInvoke(new Action(delegate{pending.Remove(item);Announce();}));}
            }
        }
    }
}
