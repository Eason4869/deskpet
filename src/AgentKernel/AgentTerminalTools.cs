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
        internal bool TerminalAvailable {get{return terminalPolicy==AgentTerminalPolicy.ConfirmEach && kernelApproval!=null && Environment.OSVersion.Platform==PlatformID.Win32NT;}}
        internal void ConfigureTerminal(AgentTerminalPolicy policy)
        {
            terminalPolicy=policy;dynamicTools.RemoveAll(d=>d.Name=="exec_command");
            if(TerminalAvailable){kernelPermissions.Add(new AgentPermissionRule{Action="shell",Resource="**",Effect=AgentPermissionEffect.Ask});dynamicTools.Add(new AgentToolDefinition{Name="exec_command",Description="在每次用户确认后执行 PowerShell 命令，返回真实退出码与有界输出。当前账户执行，文件和网络均未隔离。",ReadOnly=false,Required=new[]{"command"},Properties=new Dictionary<string,object>{{"command",new{type="string",description="PowerShell 命令正文，最多 8000 字符"}},{"working_directory",new{type="string",description="已有工作目录，默认为当前项目"}},{"timeout_seconds",new{type="integer",description="1–600 秒，不得超过项目设置"}}}});}
            BuildSchemas();
        }
        void NormalizeTerminalArguments(Dictionary<string,object> args)
        {
            if(!TerminalAvailable || readOnly)return;
            string directory=Resolve(GetString(args,"working_directory",Root));if(!Directory.Exists(directory))throw new DirectoryNotFoundException("命令工作目录不存在。");
            args["working_directory"]=directory;args["timeout_seconds"]=GetInt(args,"timeout_seconds",CommandTimeoutSeconds,1,Math.Min(600,CommandTimeoutSeconds));
        }
        async Task<string> ExecuteTerminalAsync(AgentToolInvocation invocation,CancellationToken cancel,Action beforeStart=null)
        {
            if(!TerminalAvailable || readOnly)return "错误：当前入口未启用终端或处于只读模式。";
            var journal=new AgentTerminalJournal(Path.Combine(Root,".deskpet-agent-kernel","executions"));var previous=journal.Read(invocation);
            if(previous!=null){if(previous.PayloadHash!=invocation.PayloadHash)return "错误：调用参数与原执行记录不符，需人工核对。";if(previous.Result!=null)return PresentTerminalResult(previous.Result);return "错误：needs_reconciliation：存在未确认结果的命令记录，不会自动重复执行。";}
            var record=new AgentTerminalRecord{ExecutionId=Guid.NewGuid().ToString("N"),PayloadHash=invocation.PayloadHash,State="prepared"};journal.Save(invocation,record);
            try{
                var executor=new AgentCommandExecutor(new AgentAuthorizationService(kernelPermissions,kernelApproval));
                var progress=new InlineProcessProgress(item=>{record.State=item.State;journal.Save(invocation,record);if(beforeStart!=null)beforeStart();});
                var result=await executor.ExecuteAsync(new AgentCommandRequest{ExecutionId=record.ExecutionId,Invocation=invocation,WorkingDirectory=Root,TimeoutSeconds=CommandTimeoutSeconds},progress,cancel).ConfigureAwait(false);
                record.Result=result;record.State=result.Cancelled?"cancelled":result.TimedOut||result.ExitCode!=0?"failed":"completed";journal.Save(invocation,record);
                lock(commandResults){commandResults.Add(new CommandResult{CommandId=result.OutputId,ExecutionId=result.ExecutionId,CommandText=Convert.ToString(invocation.ReadArguments()["command"]),IsolationDescription=result.IsolationDescription,Workspace=Convert.ToString(invocation.ReadArguments()["working_directory"]),Executable=AgentCommandExecutor.PowerShellPath(),Arguments=new string[0],Output=result.Output,ExitCode=result.ExitCode,Truncated=result.Truncated,TimedOut=result.TimedOut,Cancelled=result.Cancelled,ElapsedMilliseconds=(long)(result.FinishedUtc-result.StartedUtc).TotalMilliseconds});if(commandResults.Count>8)commandResults.RemoveAt(0);}
                PublishTask();return PresentTerminalResult(result);
            }catch(UnauthorizedAccessException){record.State="denied";journal.Save(invocation,record);return "错误：命令未获得本次确认，不会执行。";}
            catch(OperationCanceledException){if(record.State=="prepared"){record.State="cancelled";journal.Save(invocation,record);}throw;}
        }
        static string PresentTerminalResult(AgentCommandResult result)
        {string output=result.Output??"";bool cut=output.Length>12000;if(cut)output=output.Substring(output.Length-12000);return (result.TimedOut?"错误：命令超时。":result.Cancelled?"错误：命令已停止。":result.ExitCode!=0?"错误：命令返回非零退出码。":"")+new JavaScriptSerializer().Serialize(new{result.ExecutionId,result.OutputId,result.ExitCode,result.TimedOut,result.Cancelled,result.IsolationDescription,result.StartedUtc,result.FinishedUtc,Output=output,Truncated=result.Truncated||cut});}
        sealed class InlineProcessProgress:IProgress<AgentProcessEvent>{readonly Action<AgentProcessEvent> callback;internal InlineProcessProgress(Action<AgentProcessEvent> callback){this.callback=callback;}public void Report(AgentProcessEvent value){callback(value);}}
    }
}
