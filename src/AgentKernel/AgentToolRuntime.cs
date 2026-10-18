using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public delegate Task<string> AgentToolHandler(Dictionary<string,object> args,AgentToolExecutionContext context,CancellationToken cancel);
    public sealed class AgentToolExecutionContext { public string SessionId,ProjectId,AgentId,MessageId,CallId,TaskId; }
    public sealed class AgentToolRegistration { public AgentToolDefinition Definition;public AgentToolHandler Handler; }
    public sealed class AgentToolRuntime
    {
        readonly object gate=new object();readonly Dictionary<string,AgentToolRegistration> tools=new Dictionary<string,AgentToolRegistration>(StringComparer.Ordinal);
        readonly AgentPermissionRuntime permissions;readonly IAgentApprovalProvider approval;readonly AgentAuditLog audit;
        readonly AgentExecutionGuard executionGuard=new AgentExecutionGuard();
        public AgentToolRuntime(AgentPermissionRuntime permissions,AgentAuditLog audit,IAgentApprovalProvider approval=null){this.permissions=permissions??new AgentPermissionRuntime();this.audit=audit;this.approval=approval;}
        public void Register(AgentToolDefinition definition,AgentToolHandler handler)
        {
            if(definition==null || String.IsNullOrWhiteSpace(definition.Name) || handler==null)throw new ArgumentException("工具注册不完整。");lock(gate)tools[definition.Name]=new AgentToolRegistration{Definition=definition,Handler=handler};
        }
        public bool Has(string name){lock(gate)return !String.IsNullOrWhiteSpace(name)&&tools.ContainsKey(name);}public bool IsReadOnly(string name){lock(gate){AgentToolRegistration item;return tools.TryGetValue(name??"" ,out item)&&item.Definition.ReadOnly;}}

        public Task<string> ExecuteAsync(string name,Dictionary<string,object> args,AgentToolExecutionContext context,CancellationToken cancel,Action beforeEffect=null)
        {
            cancel.ThrowIfCancellationRequested();AgentToolRegistration registration;lock(gate)if(!tools.TryGetValue(name,out registration))throw new ArgumentException("未知工具："+name);var invocation=new AgentToolInvocation(name,args,context,registration.Definition.ApprovalResource);
            var frozen=new AgentToolExecutionContext{SessionId=invocation.SessionId,ProjectId=invocation.ProjectId,AgentId=invocation.AgentId,CallId=invocation.CallId,TaskId=invocation.TaskId};
            return executionGuard.RunAsync(invocation,()=>ExecuteOnceAsync(invocation,frozen,cancel,beforeEffect));
        }
        async Task<string> ExecuteOnceAsync(AgentToolInvocation invocation,AgentToolExecutionContext context,CancellationToken cancel,Action beforeEffect)
        {
            string name=invocation.ToolName;var args=invocation.ReadArguments();
            if(context==null || String.IsNullOrWhiteSpace(context.CallId))throw new ArgumentException("工具调用必须具有稳定 CallId。");
            AgentToolRegistration registration;lock(gate)if(!tools.TryGetValue(name,out registration))throw new ArgumentException("未知工具："+name);
            string action=AgentPermissionRuntime.ActionForTool(name),resource=AgentPermissionRuntime.ResourceForTool(name,args);
            var decision=await new AgentAuthorizationService(permissions,approval).AuthorizeAsync(invocation,cancel).ConfigureAwait(false);
            if(decision.Effect!=AgentPermissionEffect.Allow){Record(context,name,action,resource,"denied",0);throw new UnauthorizedAccessException("权限拒绝："+decision.Reason);}
            cancel.ThrowIfCancellationRequested();
            if(beforeEffect!=null)beforeEffect();
            var watch=Stopwatch.StartNew();string result;string status="completed";
            try{result=await registration.Handler(args??new Dictionary<string,object>(),context,cancel).ConfigureAwait(false);}
            catch(Exception){status="error";throw;}
            finally{watch.Stop();Record(context,name,action,resource,status,watch.ElapsedMilliseconds);}
            result=result??"";return result.Length>24000?result.Substring(0,24000)+"\n[工具输出已截断]":result;
        }
        void Record(AgentToolExecutionContext context,string name,string action,string resource,string result,long duration){if(audit!=null)audit.Record(new AgentAuditEvent{CallId=context.CallId,TaskId=context.TaskId,Tool=name,Action=action,Resource=resource,Result=result,DurationMs=duration,CreatedUtc=DateTime.UtcNow});}
    }
}
