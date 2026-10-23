using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        AgentTerminalPolicy terminalPolicy;
        AgentAccessPolicy accessPolicy;string[] sandboxReadDirectories=new string[0];
        internal void ConfigureAccessPolicy(PetSettings settings,string intent=null,IAgentRiskReviewer reviewer=null)
        {
            accessPolicy=new AgentAccessPolicy(settings,intent);sandboxReadDirectories=accessPolicy.Sandbox?AgentSandboxDirectories.Parse(settings.AgentSandboxReadDirectories):new string[0];kernelPermissions.AccessPolicy=accessPolicy;
            if(settings.AgentApprovalMode!=AgentApprovalMode.Legacy)kernelApproval=new AgentPolicyApprovalProvider(kernelApproval,reviewer??new ApiAgentRiskReviewer(settings.Text),accessPolicy,(r,result)=>{if(kernelAudit!=null)kernelAudit.Record(new AgentAuditEvent{CallId=r.CallId,TaskId=r.TaskId,Tool=r.ToolName,Action=r.Action,Resource=r.Resource,Result=result,Reason=r.ReviewReason??r.Risk,PayloadHash=r.PayloadHash,SessionId=r.SessionId,ProjectId=r.ProjectId,CreatedUtc=DateTime.UtcNow});});
            kernelTools=new AgentToolRuntime(kernelPermissions,kernelAudit,kernelApproval);RegisterMcpTools();
        }
        internal bool TerminalAvailable {get{return Environment.OSVersion.Platform==PlatformID.Win32NT && (accessPolicy!=null&&accessPolicy.Mode!=AgentApprovalMode.Legacy?terminalPolicy!=AgentTerminalPolicy.Disabled:terminalPolicy==AgentTerminalPolicy.ConfirmEach&&kernelApproval!=null);}}
        internal void ConfigureTerminal(AgentTerminalPolicy policy)
        {
            terminalPolicy=policy;dynamicTools.RemoveAll(d=>d.Name=="exec_command");
            if(TerminalAvailable){kernelPermissions.Add(new AgentPermissionRule{Action="shell",Resource="**",Effect=AgentPermissionEffect.Ask});dynamicTools.Add(new AgentToolDefinition{Name="exec_command",Description=accessPolicy!=null&&accessPolicy.Sandbox?TextResources.Get("agentTerminalTools.configureTerminal.nullAccessPolicySandbox"):TextResources.Get("agentTerminalTools.configureTerminal.nullAccessPolicySandbox.alternativeA"),ReadOnly=false,Required=new[]{"command"},Properties=new Dictionary<string,object>{{"command",new{type="string",description=TextResources.Get("agentTerminalTools.configureTerminal.descriptionTypeDescription")}},{"working_directory",new{type="string",description=TextResources.Get("agentTerminalTools.configureTerminal.descriptionTypeDescription.alternativeA")}},{"timeout_seconds",new{type="integer",description=TextResources.Get("agentTerminalTools.configureTerminal.descriptionTypeDescription.alternativeB")}}}});}
            BuildSchemas();
        }
        void NormalizeTerminalArguments(Dictionary<string,object> args)
        {
            if(!TerminalAvailable || readOnly)return;
            string directory=Resolve(GetString(args,"working_directory",Root));if(!Directory.Exists(directory))throw new DirectoryNotFoundException(TextResources.Get("agentTerminalTools.normalizeTerminalArguments.text"));
            if(accessPolicy!=null&&accessPolicy.Sandbox&&!accessPolicy.Inside(directory))throw new UnauthorizedAccessException(TextResources.Get("agentTerminalTools.normalizeTerminalArguments.text.alternativeA"));
            args["working_directory"]=directory;args["timeout_seconds"]=GetInt(args,"timeout_seconds",CommandTimeoutSeconds,1,Math.Min(600,CommandTimeoutSeconds));
        }
        async Task<string> ExecuteTerminalAsync(AgentToolInvocation invocation,CancellationToken cancel,Action beforeStart=null)
        {
            if(!TerminalAvailable || readOnly)return TextResources.Get("agentTerminalTools.executeTerminalAsync.text");
            var journal=new AgentTerminalJournal(Path.Combine(Root,".deskpet-agent-kernel","executions"));var previous=journal.Read(invocation);
            if(previous!=null){if(previous.PayloadHash!=invocation.PayloadHash)return TextResources.Get("agentTerminalTools.executeTerminalAsync.text.alternativeA");if(previous.Result!=null)return PresentTerminalResult(previous.Result);return TextResources.Get("agentTerminalTools.executeTerminalAsync.text.alternativeB");}
            var record=new AgentTerminalRecord{ExecutionId=Guid.NewGuid().ToString("N"),PayloadHash=invocation.PayloadHash,State="prepared"};journal.Save(invocation,record);
            try{
                var executor=new AgentCommandExecutor(new AgentAuthorizationService(kernelPermissions,kernelApproval));
                var progress=new InlineProcessProgress(item=>{record.State=item.State;journal.Save(invocation,record);if(beforeStart!=null)beforeStart();});
                var result=await executor.ExecuteAsync(new AgentCommandRequest{ExecutionId=record.ExecutionId,Invocation=invocation,WorkingDirectory=Root,TimeoutSeconds=CommandTimeoutSeconds,Sandbox=accessPolicy!=null&&accessPolicy.Sandbox,SandboxRoot=Root,SandboxReadDirectories=sandboxReadDirectories},progress,cancel).ConfigureAwait(false);
                record.Result=result;record.State=result.Cancelled?"cancelled":result.TimedOut||result.ExitCode!=0?"failed":"completed";journal.Save(invocation,record);
                lock(commandResults){commandResults.Add(new CommandResult{CommandId=result.OutputId,ExecutionId=result.ExecutionId,CommandText=Convert.ToString(invocation.ReadArguments()["command"]),IsolationDescription=result.IsolationDescription,Workspace=Convert.ToString(invocation.ReadArguments()["working_directory"]),Executable=AgentCommandExecutor.PowerShellPath(),Arguments=new string[0],Output=result.Output,ExitCode=result.ExitCode,Truncated=result.Truncated,TimedOut=result.TimedOut,Cancelled=result.Cancelled,ElapsedMilliseconds=(long)(result.FinishedUtc-result.StartedUtc).TotalMilliseconds});if(commandResults.Count>8)commandResults.RemoveAt(0);}
                PublishTask();return PresentTerminalResult(result);
            }catch(UnauthorizedAccessException){record.State="denied";journal.Save(invocation,record);return TextResources.Get("agentTerminalTools.executeTerminalAsync.text.alternativeC");}
            catch(OperationCanceledException){if(record.State=="prepared"){record.State="cancelled";journal.Save(invocation,record);}throw;}
        }
        static string PresentTerminalResult(AgentCommandResult result)
        {string output=result.Output??"";bool cut=output.Length>12000;if(cut)output=output.Substring(output.Length-12000);return (result.TimedOut?TextResources.Get("agentTerminalTools.presentTerminalResult.text"):result.Cancelled?TextResources.Get("agentTerminalTools.presentTerminalResult.text.alternativeA"):result.ExitCode!=0?TextResources.Get("agentTerminalTools.presentTerminalResult.text.alternativeB"):"")+new JavaScriptSerializer().Serialize(new{result.ExecutionId,result.OutputId,result.ExitCode,result.TimedOut,result.Cancelled,result.IsolationDescription,result.StartedUtc,result.FinishedUtc,Output=output,Truncated=result.Truncated||cut});}
        sealed class InlineProcessProgress:IProgress<AgentProcessEvent>{readonly Action<AgentProcessEvent> callback;internal InlineProcessProgress(Action<AgentProcessEvent> callback){this.callback=callback;}public void Report(AgentProcessEvent value){callback(value);}}
    }
}
