using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class WorkspaceSandboxTests
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);Interlocked.Increment(ref checks);}
        static string Q(string s){return "'"+s.Replace("'","''")+"'";}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string path,string existing,IntPtr security);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetEvent(IntPtr handle);
        static void HandleIsolation(string root)
        {
            using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset)){
                IntPtr handle=signal.SafeWaitHandle.DangerousGetHandle();Check(SetHandleInformation(handle,1,1),"inheritable event fixture created");
                var probe=new AgentCommand{Executable=Assembly.GetExecutingAssembly().Location,Workspace=root,Arguments=new[]{"--handle-probe",handle.ToInt64().ToString()}};
                var output=WindowsCommandRunner.Run(probe,10,CancellationToken.None,true);Check(output.ExitCode==0&&!signal.WaitOne(0),"unrelated inheritable host handles cannot enter Full/Legacy commands");
                using(var stdio=WindowsCommandRunner.StartStdio(probe)){stdio.Output.ReadToEnd();Check(!signal.WaitOne(0),"MCP stdio launch cannot inherit unrelated command pipes or host handles");}
            }
        }
        static CommandResult Run(string root,string script,int timeout,CancellationToken cancel,string[] tools=null)
        {
            var method=typeof(WindowsCommandRunner).GetMethod(tools==null?"RunSandboxed":"RunSandboxedWithTools",BindingFlags.Static|BindingFlags.NonPublic);
            Check(method!=null,"native workspace sandbox executor is missing");
            var command=new AgentCommand{Id=Guid.NewGuid().ToString("N"),Executable=AgentCommandExecutor.PowerShellPath(),Workspace=root,Arguments=new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(AgentCommandExecutor.SandboxBootstrap(root)+"$OutputEncoding=[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); $ProgressPreference='SilentlyContinue'; "+script))}};
            try{return (CommandResult)method.Invoke(null,tools==null?new object[]{command,timeout,cancel}:new object[]{command,timeout,cancel,root,tools});}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        sealed class Allow:IAgentAuthorizationService{public Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation i,CancellationToken c){return Task.FromResult(new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow});}}
        // SetSecurityInfo may add AI when it normalizes inherited ACL metadata.
        // Ignore only that bit; retain protection and every ordered permission ACE.
        static string NormalizeAcl(string sddl){var acl=new RawSecurityDescriptor(sddl);acl.SetFlags(acl.ControlFlags & ~ControlFlags.DiscretionaryAclAutoInherited);return acl.GetSddlForm(AccessControlSections.Access);}
        static string Acl(string path){return NormalizeAcl(new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));}
        public static int Main(string[] args)
        {
            if(args.Length==2&&args[0]=="--handle-probe"){SetEvent(new IntPtr(Int64.Parse(args[1])));return 0;}
            string fixture=Path.Combine(Directory.GetCurrentDirectory(),"work","sandbox-"+Guid.NewGuid().ToString("N"));string root=Path.Combine(fixture,"project");TcpListener listener=null;
            try{
                Check(NormalizeAcl("D:(A;;FR;;;BA)")==NormalizeAcl("D:AI(A;;FR;;;BA)"),"automatic inheritance completion metadata does not change permissions");
                Check(NormalizeAcl("D:(A;;FR;;;BA)")!=NormalizeAcl("D:P(A;;FR;;;BA)")&&NormalizeAcl("D:(A;;FR;;;BA)")!=NormalizeAcl("D:(A;;FA;;;BA)")&&NormalizeAcl("D:(A;;FR;;;BA)")!=NormalizeAcl("D:(A;;FR;;;SY)"),"ACL comparisons retain protection permission masks and identities");
                Directory.CreateDirectory(root);string originalAcl=Acl(root);string outside=Path.Combine(fixture,"private.txt");File.WriteAllText(outside,"private-sentinel");
                // Reproduce unavailable module discovery without changing host ACLs.
                string recovery="$PSModuleAutoloadingPreference='None'; Remove-Module Microsoft.PowerShell.Utility,Microsoft.PowerShell.Management -Force -ErrorAction SilentlyContinue; [Console]::WriteLine('NO-AUTOLOAD:'+($null -eq $ExecutionContext.InvokeCommand.GetCommand('New-Object',[System.Management.Automation.CommandTypes]::Cmdlet))); "+AgentCommandExecutor.SandboxBootstrap(root)+"if ((Get-Item workspace:\\).FullName.TrimEnd([char]92) -ne "+Q(root)+") { exit 9 }; Write-Output 'BOOTSTRAP_OK'";
                var recovered=WindowsCommandRunner.Run(new AgentCommand{Executable=AgentCommandExecutor.PowerShellPath(),Workspace=root,Arguments=new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(recovery))}},60,CancellationToken.None,true);
                Console.WriteLine(recovered.Output);Check(recovered.ExitCode==0&&recovered.Output.Contains("NO-AUTOLOAD:True")&&recovered.Output.Contains("BOOTSTRAP_OK"),"trusted built-in assemblies restore working cmdlets when module auto-discovery is unavailable");
                HandleIsolation(root);if(args.Contains("--inheritance-only")){Console.WriteLine("Handle inheritance regression: "+checks+" passed");return 0;}
                var result=Run(root,"$ErrorActionPreference='Stop'; [IO.File]::WriteAllText((Join-Path $pwd.ProviderPath 'inside.txt'),'中文'); Write-Output 'SANDBOX_OK'",20,CancellationToken.None);
                Console.WriteLine("startup exit="+result.ExitCode+" "+result.Output);Check(result.ExitCode==0&&result.Output.Contains("SANDBOX_OK")&&File.ReadAllText(Path.Combine(root,"inside.txt"))=="中文","sandbox PowerShell must actually write inside project");
                result=Run(root,"try { [IO.File]::ReadAllText("+Q(outside)+"); exit 9 } catch { 'READ_BLOCKED' }; try { [IO.File]::WriteAllText("+Q(outside)+",'bad'); exit 10 } catch { 'WRITE_BLOCKED' }",20,CancellationToken.None);
                Check(result.ExitCode==0&&result.Output.Contains("READ_BLOCKED")&&result.Output.Contains("WRITE_BLOCKED")&&File.ReadAllText(outside)=="private-sentinel","outside reads and writes must be denied by Windows");
                string journal=Path.Combine(root,".deskpet-agent-kernel","journal.txt");File.WriteAllText(journal,"private-journal");
                var exposed=File.GetAccessControl(journal);exposed.SetAccessRuleProtection(true,true);exposed.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-15-2-1"),FileSystemRights.ReadAndExecute,AccessControlType.Allow));File.SetAccessControl(journal,exposed);
                result=Run(root,"try { [IO.File]::ReadAllText("+Q(journal)+"); exit 9 } catch { 'JOURNAL_BLOCKED' }; try { [IO.File]::WriteAllText("+Q(journal)+",'bad'); exit 10 } catch { 'JOURNAL_WRITE_BLOCKED' }",20,CancellationToken.None);
                Check(result.ExitCode==0&&result.Output.Contains("JOURNAL_BLOCKED")&&result.Output.Contains("JOURNAL_WRITE_BLOCKED")&&File.ReadAllText(journal)=="private-journal","host run records with explicit package access must be inaccessible to sandbox commands");
                bool protectedRoot=false,protectedTools=false,alias=false;
                try{Run(Path.GetDirectoryName(journal),"'UNSAFE'",20,CancellationToken.None);}catch{protectedRoot=true;}
                try{Run(root,"'UNSAFE'",20,CancellationToken.None,new[]{Path.GetDirectoryName(journal)});}catch{protectedTools=true;}
                try{Run(root,"'UNSAFE'",20,CancellationToken.None,new[]{Path.GetDirectoryName(journal)+".\\"});}catch{alias=true;}
                Check(protectedRoot&&protectedTools&&alias,"reserved records cannot be regranted as a project, tool directory or trailing-dot alias");
                string toolRoot=Path.Combine(fixture,"tools");Directory.CreateDirectory(toolRoot);string toolFile=Path.Combine(toolRoot,"tool.txt");File.WriteAllText(toolFile,"tool-sentinel");string toolAcl=Acl(toolRoot);
                result=Run(root,"[IO.File]::ReadAllText("+Q(toolFile)+"); try { [IO.File]::WriteAllText("+Q(toolFile)+",'bad'); exit 10 } catch { 'TOOL_READONLY' }",20,CancellationToken.None,new[]{toolRoot});
                Check(result.ExitCode==0&&result.Output.Contains("tool-sentinel")&&result.Output.Contains("TOOL_READONLY")&&File.ReadAllText(toolFile)=="tool-sentinel"&&Acl(toolRoot)==toolAcl,"explicit tool directory grants read access and withdraws it after execution");
                Environment.SetEnvironmentVariable("DESKPET_FIXTURE_SECRET","private-environment");
                result=Run(root,"if ($env:DESKPET_FIXTURE_SECRET) { exit 11 }; if ($env:USERPROFILE -notlike '*DeskPet.Command.*') { exit 12 }; 'CLEAN_PROFILE'",20,CancellationToken.None);
                Environment.SetEnvironmentVariable("DESKPET_FIXTURE_SECRET",null);Check(result.ExitCode==0&&result.Output.Contains("CLEAN_PROFILE"),"sandbox must not inherit host secrets or host user profile");
                result=Run(root,"& $env:COMSPEC /d /c "+Q("echo hacked>\""+outside+"\"")+"; if ($LASTEXITCODE -eq 0) { exit 12 }; 'CHILD_BLOCKED'",20,CancellationToken.None);
                Check(result.ExitCode==0&&result.Output.Contains("CHILD_BLOCKED")&&File.ReadAllText(outside)=="private-sentinel","native child must inherit file restrictions");
                listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
                result=Run(root,"$c=New-Object Net.Sockets.TcpClient; try { $t=$c.ConnectAsync('127.0.0.1',"+port+"); if ($t.Wait(1500) -and $c.Connected) { exit 13 } } catch {}; 'NETWORK_BLOCKED'; $c.Dispose()",20,CancellationToken.None);
                Check(result.ExitCode==0&&result.Output.Contains("NETWORK_BLOCKED")&&!listener.Pending(),"loopback network must be denied, not just proxy-disabled");
                var parallel=Enumerable.Range(0,6).Select(i=>Task.Run(()=>Run(root,"Start-Sleep -Milliseconds 250; [IO.File]::WriteAllText("+Q(Path.Combine(root,"parallel-"+i+".txt"))+",'ok'); 'PARALLEL_OK'",25,CancellationToken.None))).ToArray();Task.WaitAll(parallel);Check(parallel.All(t=>t.Result.ExitCode==0&&t.Result.Output.Contains("PARALLEL_OK")),"parallel leases must not overwrite each other's filesystem grants");
                result=Run(root,"Start-Sleep 20",1,CancellationToken.None);Check(result.TimedOut,"sandbox timeout must terminate owned tree");
                using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(1500);result=Run(root,"Start-Sleep 20",20,cancel.Token);Check(result.Cancelled,"sandbox cancellation must terminate owned tree");}
                var invocation=new AgentToolInvocation("exec_command",new JavaScriptSerializer().DeserializeObject(new JavaScriptSerializer().Serialize(new{command="[IO.File]::WriteAllText((Join-Path $pwd.ProviderPath 'production.txt'),'ok'); 'PRODUCTION_OK'",working_directory=root,timeout_seconds=20})) as Dictionary<string,object>,new AgentToolExecutionContext{CallId="production",TaskId="sandbox"});
                var production=new AgentCommandExecutor(new Allow()).ExecuteAsync(new AgentCommandRequest{Invocation=invocation,Sandbox=true,SandboxRoot=root,TimeoutSeconds=20},null,CancellationToken.None).Result;
                Check(production.ExitCode==0&&production.Output.Contains("PRODUCTION_OK")&&File.Exists(Path.Combine(root,"production.txt"))&&production.IsolationDescription.Contains("AppContainer"),"production command executor must use actual native isolation");
                bool failed=false;try{Run(root,"[IO.File]::WriteAllText("+Q(outside)+",'fallback')",20,CancellationToken.None,new[]{Path.Combine(fixture,"missing-tool")});}catch{failed=true;}
                Check(failed&&File.ReadAllText(outside)=="private-sentinel","failed sandbox startup must not retry without isolation");
                Check(Acl(root)==originalAcl,"concurrent, failed, canceled and completed commands must restore the original project ACL");
                string link=Path.Combine(root,"hardlink.txt");Check(CreateHardLink(link,outside,IntPtr.Zero),"hard-link fixture created");bool linked=false;try{Run(root,"'UNSAFE'",20,CancellationToken.None);}catch(UnauthorizedAccessException){linked=true;}finally{File.Delete(link);}Check(linked&&Acl(root)==originalAcl,"existing hard links must fail closed before granting project access");
                Check(!AgentExecutionIsolation.Resolve(AgentTerminalPolicy.Disabled).Available,"disabled remains disabled");
                Check(AgentExecutionIsolation.Resolve(AgentTerminalPolicy.ConfirmEach).Description.Contains("not isolated"),"legacy mode accurately described");
                Console.WriteLine("Native workspace isolation tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(listener!=null)listener.Stop();}
        }
    }
}
