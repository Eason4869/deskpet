using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    [DataContract] public enum AgentTerminalPolicy { [EnumMember] Disabled,[EnumMember] ConfirmEach,[EnumMember] Sandboxed }
    public sealed class AgentCommandRequest
    {
        public string ExecutionId=Guid.NewGuid().ToString("N"),Command,WorkingDirectory,Shell;
        public int TimeoutSeconds=120;public AgentToolInvocation Invocation;
    }
    public sealed class AgentCommandResult
    {
        public string ExecutionId,OutputId,Output,IsolationDescription;public int ExitCode;public bool TimedOut,Cancelled,Truncated;
        public DateTime StartedUtc,FinishedUtc;
    }
    public sealed class AgentProcessEvent {public string ExecutionId,State,Output;}
    public interface IAgentCommandExecutor {Task<AgentCommandResult> ExecuteAsync(AgentCommandRequest request,IProgress<AgentProcessEvent> progress,CancellationToken cancel);}
    public sealed class AgentCommandExecutor:IAgentCommandExecutor
    {
        readonly IAgentAuthorizationService authorization;
        public AgentCommandExecutor(IAgentAuthorizationService authorization){this.authorization=authorization;}
        public async Task<AgentCommandResult> ExecuteAsync(AgentCommandRequest request,IProgress<AgentProcessEvent> progress,CancellationToken cancel)
        {
            if(request==null || request.Invocation==null)throw new ArgumentException("命令缺少归属信息。");
            // Freeze everything before the first await; process arguments come only from the approved payload.
            var invocation=request.Invocation;int maximumTimeout=request.TimeoutSeconds;
            var args=invocation.ReadArguments();object raw;
            string command=args.TryGetValue("command",out raw)?raw as string:null;
            string directory=args.TryGetValue("working_directory",out raw)?raw as string:null;
            if(!args.TryGetValue("timeout_seconds",out raw))throw new ArgumentException("命令缺少绑定审批的超时参数。");int timeout=Convert.ToInt32(raw);
            string executionId=request.ExecutionId;
            if(String.IsNullOrWhiteSpace(command)||command.Length>8000||command.IndexOf('\0')>=0)throw new ArgumentException("命令应为 1–8000 字符。");
            if(String.IsNullOrWhiteSpace(directory)||!Path.IsPathRooted(directory))throw new ArgumentException("命令需要绑定审批的绝对工作目录。");directory=Path.GetFullPath(directory);if(!Directory.Exists(directory))throw new DirectoryNotFoundException("命令工作目录不存在。");
            if(timeout<1||timeout>Math.Min(600,maximumTimeout))throw new ArgumentException("命令超时超过当前设置。");
            string shell=PowerShellPath();
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("终端执行目前仅支持 Windows。");
            var decision=await authorization.AuthorizeAsync(invocation,cancel).ConfigureAwait(false);
            if(decision.Effect!=AgentPermissionEffect.Allow)throw new UnauthorizedAccessException("命令未获得本次确认："+decision.Reason);
            cancel.ThrowIfCancellationRequested();var started=DateTime.UtcNow;
            if(progress!=null)progress.Report(new AgentProcessEvent{ExecutionId=executionId,State="started"});
            // Explicit UTF-8 affects this process only and avoids console codepage dependence.
            string script="$OutputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)\r\n"+command+"\r\n$deskpetCommandSucceeded = $?\r\nif ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\r\nif (-not $deskpetCommandSucceeded) { exit 1 }";
            var process=new AgentCommand{Id=executionId,Executable=shell,Workspace=directory,Arguments=new[]{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}};
            var result=await Task.Run(()=>WindowsCommandRunner.Run(process,timeout,cancel,true),CancellationToken.None).ConfigureAwait(false);
            return new AgentCommandResult{ExecutionId=executionId,OutputId=executionId,ExitCode=result.ExitCode,Output=result.Output,Truncated=result.Truncated,TimedOut=result.TimedOut,Cancelled=result.Cancelled,StartedUtc=started,FinishedUtc=DateTime.UtcNow,IsolationDescription=AgentExecutionIsolation.Resolve(AgentTerminalPolicy.ConfirmEach).Description};
        }
        internal static string PowerShellPath(){string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");if(!File.Exists(path))throw new PlatformNotSupportedException("未找到系统 PowerShell。");return path;}
    }
}
