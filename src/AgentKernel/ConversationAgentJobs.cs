using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        AgentJobManager jobsManager;
        AgentJobOwner jobsOwner;
        AgentRunJournal jobsJournal;
        int jobsTimeoutCeiling;
        internal void ConfigureJobs(AgentJobManager manager,AgentJobOwner owner,int maximumTimeout)
        {
            if(manager==null||owner==null||owner.AgentId!="primary")throw new ArgumentException("owner");
            AgentJobPolicy.ResolveTimeout(null,maximumTimeout);
            if(kernelSessionModel==null||owner.ProjectId!=kernelSessionModel.ProjectId||owner.ConversationId!=kernelSessionModel.ConversationId||String.IsNullOrWhiteSpace(kernelTaskId))throw new ArgumentException("persistent_owner");
            if(jobsOwner!=null&&!jobsOwner.Matches(owner))throw new InvalidOperationException("job_owner_frozen");
            jobsManager=manager;jobsOwner=owner;jobsTimeoutCeiling=maximumTimeout;
            string path=Path.Combine(RunJournalPath(Root),"background-jobs");jobsJournal=new AgentRunJournal(path,()=>LockDirectories(path,true));
            dynamicTools.RemoveAll(tool=>AgentJobTools.IsJobTool(tool.Name));dynamicTools.AddRange(AgentJobTools.Definitions);BuildSchemas();
        }
        bool JobsAvailable(string name)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT||disposed||jobsManager==null||jobsOwner==null||kernelSessionModel==null||jobsOwner.ProjectId!=kernelSessionModel.ProjectId||jobsOwner.ConversationId!=kernelSessionModel.ConversationId||String.IsNullOrWhiteSpace(kernelTaskId))return false;
            if(kernelProfile!=null&&kernelProfile.Mode=="subagent")return false;
            return name=="get_job"||(!readOnly&&TerminalAvailable&&(kernelProfile==null||!kernelProfile.ReadOnly));
        }
        string JobDirectoryIdentity(string directory)
        {
            using(var handle=Open(directory,0,7,3,0x02200000))
            {VerifyFinalPath(handle,directory);var info=Inspect(handle);return info.Volume.ToString(CultureInfo.InvariantCulture)+":"+info.IndexHigh.ToString(CultureInfo.InvariantCulture)+":"+info.IndexLow.ToString(CultureInfo.InvariantCulture);}
        }
        AgentJobPolicySnapshot JobPolicy(string directory)
        {
            string identity=JobDirectoryIdentity(Root)+"/"+JobDirectoryIdentity(directory);
            return new AgentJobPolicySnapshot(Root,identity,kernelPermissions.Version,jobsTimeoutCeiling,JobsAvailable("start_job")&&(providerCapabilities==null||providerCapabilities.CanUseTool("start_job")),readOnly,accessPolicy!=null&&accessPolicy.Sandbox,accessPolicy==null?AgentApprovalMode.Legacy:accessPolicy.Mode,sandboxReadDirectories);
        }
        async Task<string> ExecuteJobAsync(string name,string argumentsJson,string callId,CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return AgentJobTools.Error("unsupported_platform");
                if(jobsManager==null||jobsOwner==null)return AgentJobTools.Error("workspace_required");
                if(!JobsAvailable(name)||(providerCapabilities!=null&&!providerCapabilities.CanUseTool(name)))return AgentJobTools.Error("permission_denied");
                if(String.IsNullOrWhiteSpace(callId)||callId.Length>256)return AgentJobTools.Error("invalid_arguments");
                Dictionary<string,object> args;
                try
                {
                    var source=new JavaScriptSerializer{MaxJsonLength=128*1024,RecursionLimit=8}.DeserializeObject(argumentsJson??"{}") as Dictionary<string,object>;
                    args=AgentJobTools.Normalize(name,source,Root,jobsTimeoutCeiling);
                    if(name=="start_job")args["working_directory"]=Resolve((string)args["working_directory"]);
                }
                catch(ArgumentException){return AgentJobTools.Error("invalid_arguments");}
                catch(InvalidOperationException){return AgentJobTools.Error("invalid_arguments");}
                var invocation=new AgentToolInvocation(name,args,new AgentToolExecutionContext{SessionId=jobsOwner.ConversationId,ProjectId=jobsOwner.ProjectId,AgentId="primary",TaskId=kernelTaskId,CallId=callId});
                return await executionGuard.RunAsync(invocation,()=>ExecuteJobRecordedAsync(invocation,token)).ConfigureAwait(false);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception error){Debug.WriteLine(error);return AgentJobTools.Error(JobErrorCode(error));}
        }
        async Task<string> ExecuteJobRecordedAsync(AgentToolInvocation invocation,CancellationToken token)
        {
            if(invocation.ToolName=="get_job")return await ExecuteJobAuthorizedAsync(invocation,token,null).ConfigureAwait(false);
            string response=await Task.Run(()=>jobsJournal.ExecuteAsync(invocation,before=>ExecuteJobAuthorizedAsync(invocation,token,before)),token).ConfigureAwait(false);
            // The durable journal prevents reruns even when short-lived metadata is
            // retired. An old success must not resurrect an absent job record.
            var data=new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(response) as Dictionary<string,object>;object ok;
            if(data!=null&&data.TryGetValue("ok",out ok)&&ok is bool&&(bool)ok)
            {
                var wire=data["job"] as Dictionary<string,object>;AgentJobSnapshot current;
                if(wire==null||!jobsManager.TrySnapshot(jobsOwner,wire["id"] as string,out current))return AgentJobTools.Error("execution_reconciliation_required");
                return AgentJobTools.Success(current,data.ContainsKey("replayed")&&(bool)data["replayed"]);
            }
            return response;
        }
        async Task<string> ExecuteJobAuthorizedAsync(AgentToolInvocation invocation,CancellationToken token,Action beforeEffect)
        {
            if(!JobsAvailable(invocation.ToolName))return AgentJobTools.Error("permission_denied");
            var args=invocation.ReadArguments();var permissions=kernelPermissions;var approval=kernelApproval;var authorization=new AgentAuthorizationService(permissions,approval);
            if(invocation.ToolName=="start_job")
            {
                var spec=new AgentJobSpec((string)args["command"],(string)args["title"],(string)args["working_directory"],(int)args["timeout_seconds"]);
                var context=new AgentJobStartContext(jobsOwner,invocation,authorization,()=>{
                    if(!Object.ReferenceEquals(permissions,kernelPermissions)||!Object.ReferenceEquals(approval,kernelApproval))throw new InvalidOperationException("job_policy_changed");
                    return JobPolicy(spec.WorkingDirectory);
                },()=>LockDirectories(spec.WorkingDirectory,false),beforeEffect);
                bool replayed;var operation=jobsManager.StartForToolAsync(context,spec,token,out replayed);return AgentJobTools.Success(await operation.ConfigureAwait(false),replayed);
            }
            var decision=await authorization.AuthorizeAsync(invocation,token).ConfigureAwait(false);
            if(decision==null||decision.Effect!=AgentPermissionEffect.Allow)return AgentJobTools.Error("permission_denied");
            if(invocation.ToolName=="get_job")return AgentJobTools.Page(await jobsManager.GetAsync(jobsOwner,(string)args["id"],(string)args["cursor"],(int)args["limit_bytes"],token).ConfigureAwait(false));
            token.ThrowIfCancellationRequested();if(beforeEffect==null)return AgentJobTools.Error("execution_reconciliation_required");beforeEffect();
            return AgentJobTools.Success(await jobsManager.StopAsync(jobsOwner,(string)args["id"],AgentJobStopReason.User,token).ConfigureAwait(false),false);
        }
        static string JobErrorCode(Exception error)
        {
            var missing=error as KeyNotFoundException;if(missing!=null)return "job_not_found";
            if(error is UnauthorizedAccessException)return "permission_denied";
            if(error is DirectoryNotFoundException||error is FileNotFoundException)return "directory_changed";
            if(error is ArgumentException)return error.Message.StartsWith("invalid_cursor",StringComparison.Ordinal)?"invalid_cursor":"invalid_arguments";
            if(error is InvalidOperationException&&error.Message==TextResources.Get("agentAuthorizationService.runAsync.text"))return "idempotency_conflict";
            switch(error.Message)
            {
                case "job_idempotency_conflict":return "idempotency_conflict";
                case "execution_reconciliation_required":return "execution_reconciliation_required";
                case "job_permission_denied":case "job_invocation_mismatch":case "job_directory_denied":return "permission_denied";
                case "job_policy_changed":return "policy_changed";
                case "job_directory_changed":return "directory_changed";
                case "job_application_capacity":case "job_project_capacity":return "capacity_exceeded";
                case "jobs_recovering":return "recovery_in_progress";
                case "jobs_exiting":return "application_exiting";
                case "job_cleanup_unconfirmed":return "cleanup_failed";
                case "job_log_storage_failed":return "log_storage_failed";
            }
            return error is IOException||error is ObjectDisposedException?"storage_unavailable":"start_failed";
        }
    }
    public sealed partial class ConversationSession
    {
        AgentJobManager conversationJobs;
        AgentJobOwner conversationJobsOwner;
        internal void ConfigureJobs(AgentJobManager manager,AgentJobOwner owner)
        {
            if(manager==null||owner==null||owner.AgentId!="primary"||owner.ProjectId==ProjectCatalogStore.UnassignedProjectId||owner.ProjectId==ProjectCatalogStore.QuickProjectId||kernelSession==null||owner.ProjectId!=kernelSession.ProjectId||owner.ConversationId!=kernelSession.ConversationId)throw new ArgumentException("persistent_owner");
            lock(gate){if(active!=null)throw new InvalidOperationException("conversation_busy");conversationJobs=manager;conversationJobsOwner=owner;}
        }
        void ConfigureJobTools(Operation op)
        {
            if(conversationJobs==null||op.Workspace==null||!op.Snapshot.AgentEnabled)return;
            if(kernelSession==null||conversationJobsOwner.ProjectId!=kernelSession.ProjectId||conversationJobsOwner.ConversationId!=kernelSession.ConversationId)throw new InvalidOperationException("job_owner_changed");
            op.Workspace.ConfigureJobs(conversationJobs,conversationJobsOwner,op.Snapshot.AgentBackgroundJobTimeoutSeconds);
        }
    }
}
