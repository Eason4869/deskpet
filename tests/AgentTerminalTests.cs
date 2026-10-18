using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentTerminalTests
    {
        static int count;static void Check(bool value,string why){if(!value)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        sealed class Approval:IAgentApprovalProvider {public bool Allow;public AgentApprovalRequest Last;public Task<bool> ApproveAsync(AgentApprovalRequest r){Last=r;return Task.FromResult(Allow);}}
        static string Args(string command,string root,int timeout){return new JavaScriptSerializer().Serialize(new{command,working_directory=root,timeout_seconds=timeout});}
        static AgentWorkspace Workspace(string root,Approval approval,AgentTerminalPolicy policy){var w=new AgentWorkspace(root);w.CommandTimeoutSeconds=10;w.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,approval,null,"terminal-task");w.ConfigureTerminal(policy);w.ConfigureDesktopSession();return w;}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent terminal tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"terminal 中文 "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var approval=new Approval();
            using(var w=Workspace(root,approval,AgentTerminalPolicy.Disabled)){Check(!w.AvailableToolNames.Contains("exec_command"),"terminal disabled removes schema");Check((await w.ExecuteAsync("exec_command",Args("Set-Content blocked.txt no",root,3),"disabled",CancellationToken.None)).StartsWith("错误：")&&!File.Exists(Path.Combine(root,"blocked.txt")),"disabled direct invocation cannot start process");}
            using(var w=Workspace(root,approval,AgentTerminalPolicy.ConfirmEach)){
                Check(w.AvailableToolNames.Contains("exec_command"),"confirm-each publishes real terminal");string denied=await w.ExecuteAsync("exec_command",Args("Set-Content denied.txt no",root,3),"denied",CancellationToken.None);Check(denied.StartsWith("错误：")&&!File.Exists(Path.Combine(root,"denied.txt")),"rejection never starts fixture process");
                approval.Allow=true;string result=await w.ExecuteAsync("exec_command",Args("Write-Output '中文测试'; [Console]::Error.WriteLine('stderr'); exit 7",root,3),"exit7",CancellationToken.None);
                Check(result.Contains("\"ExitCode\":7") && result.Contains("中文测试")&&result.Contains("[stderr]"),"terminal returns Unicode, stream labels and actual exit 7");
                Check(approval.Last.Resource==root && approval.Last.Arguments.Contains("working_directory"),"approval shows exact execution directory");
                result=await w.ExecuteAsync("exec_command",Args("[Console]::Write(('x' * 400000))",root,3),"large",CancellationToken.None);Check(result.Contains("\"Truncated\":true")&&result.Length<14000,"large terminal output is bounded and explicitly truncated");
                string outputId=w.CommandResults[w.CommandResults.Count-1].CommandId;string page=await w.ExecuteAsync("read_output",new JavaScriptSerializer().Serialize(new{output_id=outputId,offset=20000,limit=100}),"page",CancellationToken.None);Check(page.Contains("xxxxxxxxxx"),"terminal output can be read by output ID beyond initial preview");
                result=await w.ExecuteAsync("exec_command",Args("Start-Sleep -Seconds 20",root,1),"timeout",CancellationToken.None);Check(result.Contains("\"TimedOut\":true"),"timeout returns explicit terminal status");
                using(var cancel=new CancellationTokenSource()){var run=w.ExecuteAsync("exec_command",Args("Start-Sleep -Seconds 20",root,10),"cancel",cancel.Token);await Task.Delay(250);cancel.Cancel();try{result=await run;}catch(OperationCanceledException){result="cancelled";}Check(result=="cancelled" || result.Contains("\"Cancelled\":true"),"stop terminates active terminal");}
                string sentinel="fixture-secret-"+Guid.NewGuid().ToString("N");Environment.SetEnvironmentVariable("DESKPET_FIXTURE_SECRET",sentinel);try{result=await w.ExecuteAsync("exec_command",Args("Write-Output ('key=' + $env:DESKPET_FIXTURE_SECRET)",root,3),"env",CancellationToken.None);Check(!result.Contains(sentinel),"terminal child receives only approved environment variables");}finally{Environment.SetEnvironmentVariable("DESKPET_FIXTURE_SECRET",null);}
                result=await w.ExecuteAsync("exec_command",Args("Add-Content once.txt x",root,3),"once",CancellationToken.None);await w.ExecuteAsync("exec_command",Args("Add-Content once.txt x",root,3),"once",CancellationToken.None);Check(File.ReadAllLines(Path.Combine(root,"once.txt")).Length==1,"same call ID cannot repeat terminal effects");
            }
            using(var restored=Workspace(root,approval,AgentTerminalPolicy.ConfirmEach)){await restored.ExecuteAsync("exec_command",Args("Add-Content once.txt x",root,3),"once",CancellationToken.None);Check(File.ReadAllLines(Path.Combine(root,"once.txt")).Length==1,"persisted completed call does not repeat after reopen");}
            using(var sandbox=Workspace(root,approval,AgentTerminalPolicy.Sandboxed)){Check(!sandbox.AvailableToolNames.Contains("exec_command"),"unavailable sandbox never falls back to current account");}
        }
    }
}
