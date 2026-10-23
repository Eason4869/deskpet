using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentJobSandboxTests
    {
        static int checks;
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static IAgentJobProcessFactory Factory(IAgentJobClock clock)
        {var type=typeof(AgentJobStore).Assembly.GetType("Dafeiyu.WindowsAgentJobProcessFactory");if(type==null)throw new Exception("Windows background process factory is missing");return (IAgentJobProcessFactory)Activator.CreateInstance(type,clock);}
        static string Quote(string value){return "'"+value.Replace("'","''")+"'";}
        static void AppContainerAllowsProjectAndDeniesOutsideWrites()
        {
            using(var project=new AgentJobTestSupport.TemporaryDirectory())using(var outside=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();var output=new List<byte>();string allowed=Path.Combine(project.Path,"allowed 中文.txt"),denied=Path.Combine(outside.Path,"must-not-write.txt"),secret=Path.Combine(outside.Path,"private.txt");File.WriteAllText(secret,"private fixture");
                string command="[IO.File]::WriteAllText("+Quote(allowed)+",'project'); try { $null = [IO.File]::ReadAllText("+Quote(secret)+"); [Console]::Out.Write('READ_ESCAPE'); exit 8 } catch { [Console]::Out.Write('denied-read;') }; try { [IO.File]::WriteAllText("+Quote(denied)+",'escaped'); [Console]::Out.Write('WRITE_ESCAPE'); exit 9 } catch { [Console]::Out.Write('denied-write'); exit 0 }";
                var policy=new AgentJobPolicySnapshot(project.Path,"fixture",1,1800,true,false,true,AgentApprovalMode.Ask,null);
                using(var process=Factory(clock).PrepareAsync(new AgentJobSpec(command,null,project.Path,1800),policy,chunk=>{if(chunk.Stream=="stdout")lock(output)output.AddRange(chunk.Bytes);},CancellationToken.None).GetAwaiter().GetResult())
                {
                    Check(process is IAgentJobPreparationMetrics,"sandbox adapter exposes independently measured preparation time");process.Resume();AgentJobProcessExit result;
                    using(var timeout=new CancellationTokenSource(15000))result=AgentAuthorizationService.AwaitCancellation(process.Completion,timeout.Token).GetAwaiter().GetResult();
                    Check(result.RootExitCode==0&&File.ReadAllText(allowed)=="project"&&!File.Exists(denied),"real AppContainer permits project writes and rejects writes outside the granted root");
                    lock(output)Check(Encoding.UTF8.GetString(output.ToArray())=="denied-read;denied-write","sandbox read and write denials are observed from the confined process without host fallback");
                }
            }
        }
        static void SandboxPreparationFailureDoesNotRunOnHost()
        {
            using(var project=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string marker=Path.Combine(project.Path,"must-not-run.txt");bool failed=false;
                var policy=new AgentJobPolicySnapshot(Path.Combine(project.Path,"missing-root"),"fixture",1,1800,true,false,true,AgentApprovalMode.Ask,null);
                try{using(var process=Factory(clock).PrepareAsync(new AgentJobSpec("[IO.File]::WriteAllText("+Quote(marker)+",'bad')",null,project.Path,1800),policy,chunk=>{},CancellationToken.None).GetAwaiter().GetResult())process.Resume();}catch(ArgumentException){failed=true;}
                Check(failed&&!File.Exists(marker),"sandbox setup failure does not fall back to host command execution");
            }
        }
        static void ExplicitReadDirectoryRemainsReadOnly()
        {
            using(var project=new AgentJobTestSupport.TemporaryDirectory())using(var read=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string file=Path.Combine(read.Path,"read.txt");File.WriteAllText(file,"authorized");var output=new List<byte>();
                var policy=new AgentJobPolicySnapshot(project.Path,"fixture",1,1800,true,false,true,AgentApprovalMode.Ask,new[]{read.Path});
                string command="[Console]::Out.Write([IO.File]::ReadAllText("+Quote(file)+")); try { [IO.File]::WriteAllText("+Quote(file)+",'overwritten'); exit 9 } catch { exit 0 }";
                using(var process=Factory(clock).PrepareAsync(new AgentJobSpec(command,null,project.Path,1800),policy,chunk=>{if(chunk.Stream=="stdout")lock(output)output.AddRange(chunk.Bytes);},CancellationToken.None).GetAwaiter().GetResult())
                {
                    process.Resume();AgentJobProcessExit result;using(var deadline=new CancellationTokenSource(15000))result=AgentAuthorizationService.AwaitCancellation(process.Completion,deadline.Token).GetAwaiter().GetResult();
                    lock(output)Check(result.RootExitCode==0&&File.ReadAllText(file)=="authorized"&&Encoding.UTF8.GetString(output.ToArray())=="authorized","explicitly authorized directories permit reads while still denying writes");
                }
            }
        }
        static void SandboxLeaseOutlivesRootProcess()
        {
            using(var project=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string gate=Path.Combine(project.Path,"continue"),marker=Path.Combine(project.Path,"child-wrote.txt"),scriptFile=Path.Combine(project.Path,"child.ps1");
                string child=AgentCommandExecutor.PowerShellBootstrap(true,project.Path)+"while(-not [IO.File]::Exists("+Quote(gate)+")){ Start-Sleep -Milliseconds 20 }; [IO.File]::WriteAllText("+Quote(marker)+",'lease alive'); Start-Sleep -Seconds 30";
                File.WriteAllText(scriptFile,child,new UTF8Encoding(true));
                string command="$info = [Diagnostics.ProcessStartInfo]::new(); $info.FileName = "+Quote(AgentCommandExecutor.PowerShellPath())+"; $info.Arguments = "+Quote("-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+scriptFile+"\"")+"; $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $null = [Diagnostics.Process]::Start($info); exit 0";
                var policy=new AgentJobPolicySnapshot(project.Path,"fixture",1,1800,true,false,true,AgentApprovalMode.Ask,null);
                using(var process=Factory(clock).PrepareAsync(new AgentJobSpec(command,null,project.Path,1800),policy,chunk=>{},CancellationToken.None).GetAwaiter().GetResult())
                {
                    process.Resume();var budget=System.Diagnostics.Stopwatch.StartNew();while(!process.Status.RootExited&&budget.ElapsedMilliseconds<10000)Task.Delay(20).GetAwaiter().GetResult();
                    Check(process.Status.RootExited&&process.Status.RootExitCode==0&&!process.Status.TreeExited,"sandbox root exits while its confined child remains owned");
                    File.WriteAllText(gate,"continue");budget.Restart();while(!File.Exists(marker)&&budget.ElapsedMilliseconds<5000)Task.Delay(20).GetAwaiter().GetResult();
                    Check(File.Exists(marker)&&File.ReadAllText(marker)=="lease alive","sandbox grants remain available to the child after the root exits");
                    using(var deadline=new CancellationTokenSource(5000))Check(process.StopAsync(deadline.Token).GetAwaiter().GetResult().CleanupConfirmed,"sandbox child and grant lease are cleaned together on stop");
                }
            }
        }
        static void FailedGrantWithdrawalCanBeRetried()
        {
            using(var project=new AgentJobTestSupport.TemporaryDirectory())
            {
                var type=typeof(AgentJobStore).Assembly.GetType("Dafeiyu.WindowsCommandRunner+WindowsSandboxLease");
                var callbackType=typeof(Action<DirectoryInfo,System.Security.AccessControl.FileSystemAccessRule>);
                var constructor=type.GetConstructor(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(string),typeof(string[]),typeof(CancellationToken),callbackType},null);
                if(constructor==null)throw new Exception("Sandbox grant withdrawal dependency is missing");bool fail=true;
                Action<DirectoryInfo,System.Security.AccessControl.FileSystemAccessRule> revoke=(directory,rule)=>
                {
                    if(fail){fail=false;throw new IOException("Injected grant withdrawal failure");}
                    var acl=directory.GetAccessControl();acl.RemoveAccessRuleSpecific(rule);directory.SetAccessControl(acl);
                };
                var lease=(IDisposable)constructor.Invoke(new object[]{project.Path,new string[0],CancellationToken.None,revoke});
                var identity=(System.Security.Principal.SecurityIdentifier)type.GetField("identity",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(lease);
                Func<bool> hasGrant=()=>new DirectoryInfo(project.Path).GetAccessControl().GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().Any(rule=>rule.IdentityReference.Value==identity.Value);
                try
                {
                    bool failed=false;try{lease.Dispose();}catch(IOException){failed=true;}
                    Check(failed&&hasGrant(),"failed grant withdrawal reports unconfirmed cleanup while retaining its cleanup record");
                    lease.Dispose();Check(!hasGrant(),"retry withdraws the previously failed grant instead of silently forgetting it");
                }
                finally{lease.Dispose();}
            }
        }
        public static int Main()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.Error.WriteLine("Windows AppContainer background jobs are unsupported on this platform");return 2;}
            checks=0;int failures=0;foreach(var test in new Action[]{AppContainerAllowsProjectAndDeniesOutsideWrites,SandboxPreparationFailureDoesNotRunOnHost,ExplicitReadDirectoryRemainsReadOnly,SandboxLeaseOutlivesRootProcess,FailedGrantWithdrawalCanBeRetried})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobSandboxTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
