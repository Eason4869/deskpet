using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class AgentToolInvocation
    {
        public string SessionId {get;private set;}public string ProjectId {get;private set;}public string TaskId {get;private set;}public string AgentId {get;private set;}public string CallId {get;private set;}
        public string ToolName {get;private set;}public string Action {get;private set;}public string Resource {get;private set;}public string Arguments {get;private set;}public string PayloadHash {get;private set;}
        public string Key {get{return Canonical(new[]{SessionId,ProjectId,TaskId,AgentId,CallId});}}
        public AgentToolInvocation(string tool,Dictionary<string,object> args,AgentToolExecutionContext context,string resourceOverride=null)
        {
            if(context==null || String.IsNullOrWhiteSpace(context.CallId))throw new ArgumentException("工具调用必须具有稳定 CallId。");
            SessionId=context.SessionId??"";ProjectId=context.ProjectId??"";TaskId=context.TaskId??"";AgentId=context.AgentId??"";CallId=context.CallId;
            ToolName=tool??"";Action=AgentPermissionRuntime.ActionForTool(ToolName);Arguments=Canonical(args??new Dictionary<string,object>());
            Resource=resourceOverride??AgentPermissionRuntime.ResourceForTool(ToolName,ReadArguments());
            using(var hash=SHA256.Create())PayloadHash=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Canonical(new[]{ToolName,Action,Resource,Arguments})))).Replace("-","").ToLowerInvariant();
        }
        public Dictionary<string,object> ReadArguments(){return new JavaScriptSerializer{MaxJsonLength=8*1024*1024,RecursionLimit=20}.DeserializeObject(Arguments) as Dictionary<string,object>;}
        static string Canonical(object value)
        {
            var dictionary=value as IDictionary<string,object>;
            if(dictionary!=null)return "{"+String.Join(",",dictionary.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>Canonical(p.Key)+":"+Canonical(p.Value)))+"}";
            if(value is string || value==null || !(value is IEnumerable))return new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(value);
            var items=new List<string>();foreach(var item in (IEnumerable)value)items.Add(Canonical(item));return "["+String.Join(",",items)+"]";
        }
    }
    public interface IAgentAuthorizationService {Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken cancel);}
    public interface ICancellableAgentApprovalProvider:IAgentApprovalProvider {Task<bool> ApproveAsync(AgentApprovalRequest request,CancellationToken cancel);}
    public sealed class AgentAuthorizationService:IAgentAuthorizationService
    {
        readonly AgentPermissionRuntime permissions;readonly IAgentApprovalProvider approval;
        public AgentAuthorizationService(AgentPermissionRuntime permissions,IAgentApprovalProvider approval){this.permissions=permissions;this.approval=approval;}
        public async Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();int version=permissions.Version;
            var decision=permissions.Decide(invocation.Action,invocation.Resource);if(decision.Effect!=AgentPermissionEffect.Ask)return decision;
            if(approval==null)return Denied(invocation,"当前入口不支持确认");
            var request=new AgentApprovalRequest{Id=Guid.NewGuid().ToString("N"),Action=invocation.Action,Resource=invocation.Resource,Risk="仅本次",PayloadHash=invocation.PayloadHash,SessionId=invocation.SessionId,ProjectId=invocation.ProjectId,TaskId=invocation.TaskId,CallId=invocation.CallId,ToolName=invocation.ToolName,Arguments=invocation.Arguments};
            var cancellable=approval as ICancellableAgentApprovalProvider;
            bool allowed=await AwaitCancellation(cancellable!=null?cancellable.ApproveAsync(request,cancel):approval.ApproveAsync(request),cancel).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();if(permissions.Version!=version)return Denied(invocation,"等待期间权限已改变，请重新发起调用");
            return new AgentPermissionDecision{Action=invocation.Action,Resource=invocation.Resource,Effect=allowed?AgentPermissionEffect.Allow:AgentPermissionEffect.Deny,Reason=allowed?"approved":"rejected"};
        }
        static AgentPermissionDecision Denied(AgentToolInvocation i,string why){return new AgentPermissionDecision{Action=i.Action,Resource=i.Resource,Effect=AgentPermissionEffect.Deny,Reason=why};}
        internal static async Task<T> AwaitCancellation<T>(Task<T> operation,CancellationToken cancel)
        {
            var interrupted=new TaskCompletionSource<bool>();using(cancel.Register(()=>interrupted.TrySetResult(true))){if(await Task.WhenAny(operation,interrupted.Task).ConfigureAwait(false)!=operation){Observe(operation);cancel.ThrowIfCancellationRequested();}cancel.ThrowIfCancellationRequested();return await operation.ConfigureAwait(false);}
        }
        static async void Observe(Task task){try{await task.ConfigureAwait(false);}catch(Exception){}}
    }
    internal sealed class AgentExecutionGuard
    {
        sealed class Entry{internal string Hash;internal readonly TaskCompletionSource<string> Completion=new TaskCompletionSource<string>();}
        readonly object gate=new object();readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        internal Task<string> RunAsync(AgentToolInvocation invocation,Func<Task<string>> execute)
        {
            Entry entry;lock(gate){if(entries.TryGetValue(invocation.Key,out entry)){if(entry.Hash!=invocation.PayloadHash)throw new InvalidOperationException("同一 CallId 的参数已改变，旧审批无效。");return entry.Completion.Task;}if(entries.Count>=4096)throw new InvalidOperationException("调用记录已达容量，请保存进度并新建任务。");entry=new Entry{Hash=invocation.PayloadHash};entries.Add(invocation.Key,entry);}
            Complete(entry,execute);return entry.Completion.Task;
        }
        static async void Complete(Entry entry,Func<Task<string>> execute){try{entry.Completion.TrySetResult(await execute().ConfigureAwait(false));}catch(OperationCanceledException){entry.Completion.TrySetCanceled();}catch(Exception e){entry.Completion.TrySetException(e);}}
    }
}
