using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class AgentQuestionRequest
    {
        public string SessionId,TaskId,CallId,Question;
        public string[] Options;
        public bool AllowFreeInput=true;
        internal AgentQuestionRequest Copy(){return new AgentQuestionRequest{SessionId=SessionId,TaskId=TaskId,CallId=CallId,Question=Question,Options=Options==null?new string[0]:(string[])Options.Clone(),AllowFreeInput=AllowFreeInput};}
    }
    public sealed class AgentQuestionAnswer {public string Text;public int? OptionIndex;}
    public interface IAgentQuestionProvider {Task<AgentQuestionAnswer> AskAsync(AgentQuestionRequest request,CancellationToken cancel);}
    // Separate from permission approval: an answer supplies task input only.
    public sealed class AgentQuestionController:IAgentQuestionProvider,IDisposable
    {
        sealed class Pending {internal AgentQuestionRequest Request;internal CancellationToken Cancel;internal TaskCompletionSource<AgentQuestionAnswer> Result=new TaskCompletionSource<AgentQuestionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);}
        readonly object gate=new object();readonly List<Pending> pending=new List<Pending>();bool disposed;
        public event Action Changed;
        public IList<AgentQuestionRequest> Requests {get{lock(gate)return pending.Where(p=>!p.Result.Task.IsCompleted).Select(p=>p.Request.Copy()).ToArray();}}
        void Announce(){var changed=Changed;if(changed!=null)changed();}
        public async Task<AgentQuestionAnswer> AskAsync(AgentQuestionRequest request,CancellationToken cancel)
        {
            if(request==null||String.IsNullOrWhiteSpace(request.CallId)||String.IsNullOrWhiteSpace(request.SessionId)||String.IsNullOrWhiteSpace(request.TaskId))throw new ArgumentException("问题缺少会话、任务或调用归属。");
            cancel.ThrowIfCancellationRequested();var item=new Pending{Request=request.Copy(),Cancel=cancel};
            lock(gate){if(disposed)throw new OperationCanceledException();if(pending.Any(p=>p.Request.SessionId==request.SessionId&&p.Request.TaskId==request.TaskId&&p.Request.CallId==request.CallId))throw new InvalidOperationException("问题调用已在等待回答。");pending.Add(item);}
            try{using(cancel.Register(()=>item.Result.TrySetCanceled())){Announce();return await item.Result.Task.ConfigureAwait(false);}}
            finally{lock(gate)pending.Remove(item);Announce();}
        }
        public bool Submit(string sessionId,string taskId,string callId,AgentQuestionAnswer answer)
        {
            lock(gate){var item=pending.FirstOrDefault(p=>p.Request.SessionId==sessionId&&p.Request.TaskId==taskId&&p.Request.CallId==callId);if(item==null||item.Cancel.IsCancellationRequested||answer==null)return false;
                string text=answer.Text;int? index=answer.OptionIndex;
                if(index.HasValue){if(index.Value<0||index.Value>=item.Request.Options.Length)return false;text=item.Request.Options[index.Value];}
                else if(!item.Request.AllowFreeInput)return false;
                if(String.IsNullOrWhiteSpace(text)||text.Length>8000)return false;
                return item.Result.TrySetResult(new AgentQuestionAnswer{Text=text.Trim(),OptionIndex=index});}
        }
        public void CancelSession(string sessionId){Cancel(p=>p.SessionId==sessionId);}
        public void CancelTask(string taskId){Cancel(p=>p.TaskId==taskId);}
        void Cancel(Func<AgentQuestionRequest,bool> match){lock(gate)foreach(var p in pending.Where(p=>match(p.Request)))p.Result.TrySetCanceled();Announce();}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;foreach(var p in pending)p.Result.TrySetCanceled();}Announce();}
    }
}
