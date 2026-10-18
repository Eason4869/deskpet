using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class AgentRunRequest
    {
        public ConnectionSettings Model;public IList<ChatMessage> Messages;public AgentWorkspace Workspace;
        public Action<string> Text,Reasoning;public Action<AgentToolEvent> Tool;public bool Stream;
    }
    public sealed class AgentRunResult
    {
        public string Status,Text,Error;public int ToolCalls;public Exception Exception;
    }
    public interface IAgentTaskRunner {Task<AgentRunResult> RunAsync(AgentRunRequest request,CancellationToken cancel);}
    public sealed class AgentTaskRunner:IAgentTaskRunner
    {
        readonly IAgentConversationApi api;
        public AgentTaskRunner(IAgentConversationApi api){if(api==null)throw new ArgumentNullException("api");this.api=api;}
        public async Task<AgentRunResult> RunAsync(AgentRunRequest request,CancellationToken cancel)
        {
            if(request==null||request.Model==null||request.Messages==null||request.Workspace==null)throw new ArgumentException("Agent 运行请求不完整。");
            var model=request.Model.Copy();var messages=request.Messages.Select(m=>m.Copy()).ToArray();var workspace=request.Workspace;var text=request.Text;var reasoning=request.Reasoning;var tool=request.Tool;bool stream=request.Stream;int count=0;
            AgentRunResult outcome;int closed=0;
            Action<string> safeText=delta=>{if(!cancel.IsCancellationRequested&&Interlocked.CompareExchange(ref closed,0,0)==0&&text!=null)text(delta);};
            Action<string> safeReasoning=delta=>{if(!cancel.IsCancellationRequested&&Interlocked.CompareExchange(ref closed,0,0)==0&&reasoning!=null)reasoning(delta);};
            Action<AgentToolEvent> safeTool=item=>{if(cancel.IsCancellationRequested||Interlocked.CompareExchange(ref closed,0,0)!=0)return;if(item.Status=="running")Interlocked.Increment(ref count);if(tool!=null)tool(item.Copy());};
            try{
                cancel.ThrowIfCancellationRequested();string result=await api.ChatAgentAsync(model,messages,workspace,safeText,safeReasoning,safeTool,stream,cancel).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();
                outcome=new AgentRunResult{Status="completed",Text=result??"",ToolCalls=count};
            }catch(OperationCanceledException e){outcome=new AgentRunResult{Status="cancelled",Text="",ToolCalls=count,Exception=e};}
            catch(Exception e){outcome=new AgentRunResult{Status="failed",Text="",Error=e is ApiException?e.Message:"任务执行失败，请核对已完成操作。",ToolCalls=count,Exception=e};}
            Interlocked.Exchange(ref closed,1);
            try{await workspace.StopChildWorkersAsync().ConfigureAwait(false);}catch(Exception e){outcome.Status="failed";outcome.Error=e is ApiException?e.Message:"独立 worker 未能结束，请核对任务状态。";}
            workspace.FinishTask(workspace.NeedsReconciliation?"needs_reconciliation":outcome.Status=="completed"?"complete":outcome.Status=="cancelled"?"cancelled":"error",outcome.Error??"");return outcome;
        }
        internal static async Task<string> RunConversationAsync(IAgentConversationApi api,AgentRunRequest request,CancellationToken cancel)
        {var result=await new AgentTaskRunner(api).RunAsync(request,cancel).ConfigureAwait(false);if(result.Status=="cancelled")throw new OperationCanceledException(cancel);if(result.Status!="completed")throw new ApiException(result.Error);return result.Text;}
    }
}
