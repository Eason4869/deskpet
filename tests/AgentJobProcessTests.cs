using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentJobProcessTests
    {
        static int checks;
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static IAgentJobProcessFactory Factory(IAgentJobClock clock)
        {
            var type=typeof(AgentJobStore).Assembly.GetType("Dafeiyu.WindowsAgentJobProcessFactory");
            if(type==null)throw new Exception("Windows background process factory is missing");return (IAgentJobProcessFactory)Activator.CreateInstance(type,clock);
        }
        static AgentJobPolicySnapshot Policy(string root,bool sandbox)
        {return new AgentJobPolicySnapshot(root,"fixture",1,1800,true,false,sandbox,AgentApprovalMode.Full,new string[0]);}
        static string Quote(string value){return "'"+value.Replace("'","''")+"'";}
        sealed class UnrelatedFixture : IDisposable
        {
            internal readonly Process Process;
            internal UnrelatedFixture(string root)
            {
                string script=AgentCommandExecutor.PowerShellBootstrap(false,root)+"Start-Sleep -Seconds 30";
                Process=System.Diagnostics.Process.Start(new ProcessStartInfo(AgentCommandExecutor.PowerShellPath(),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true});
            }
            public void Dispose(){if(!Process.HasExited){Process.Kill();Process.WaitForExit(5000);}Process.Dispose();}
        }
        static void SuspendedLaunchPreservesRawStreamsAndExitCode()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();var output=new List<AgentJobOutputChunk>();string marker=Path.Combine(temporary.Path,"resumed.txt");
                var spec=new AgentJobSpec("[IO.File]::WriteAllText("+Quote(marker)+",'ready'); [Console]::Out.Write('中文🙂'); [Console]::Error.Write('stderr 中文'); exit 7",null,temporary.Path,1800);
                using(var process=Factory(clock).PrepareAsync(spec,Policy(temporary.Path,false),chunk=>{lock(output)output.Add(chunk);},CancellationToken.None).GetAwaiter().GetResult())
                {
                    Check(!File.Exists(marker)&&!process.Completion.IsCompleted,"prepared process is suspended before user code executes");process.Resume();
                    AgentJobProcessExit result;using(var timeout=new CancellationTokenSource(15000))result=AgentAuthorizationService.AwaitCancellation(process.Completion,timeout.Token).GetAwaiter().GetResult();
                    Check(result.RootExited&&result.RootExitCode==7&&result.TreeExited&&result.StreamsDrained,"native completion retains actual nonzero root exit code and confirms tree/EOF");
                    lock(output)
                    {
                        Func<string,string> text=stream=>Encoding.UTF8.GetString(output.Where(chunk=>chunk.Stream==stream).SelectMany(chunk=>chunk.Bytes).ToArray());
                        Check(text("stdout")=="中文🙂"&&text("stderr")=="stderr 中文","stdout and stderr retain raw UTF-8 without labels or cross-stream contamination");
                    }
                }
            }
        }
        static void SuspendedStopNeverExecutesCommand()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string marker=Path.Combine(temporary.Path,"must-not-run.txt");
                using(var process=Factory(clock).PrepareAsync(new AgentJobSpec("[IO.File]::WriteAllText("+Quote(marker)+",'bad')",null,temporary.Path,1800),Policy(temporary.Path,false),chunk=>{},CancellationToken.None).GetAwaiter().GetResult())
                {
                    AgentJobCleanupResult stopped;using(var timeout=new CancellationTokenSource(5000))stopped=process.StopAsync(timeout.Token).GetAwaiter().GetResult();
                    Check(stopped.CleanupConfirmed&&!File.Exists(marker),"stopping a prepared process confirms cleanup without executing its command");
                    Check(process.Status.RootExitCode==null,"forced termination is not reported as a natural root exit code");
                }
            }
        }
        static void RootExitRetainsLiveDescendant()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var unrelated=new UnrelatedFixture(temporary.Path))
            {
                var clock=new AgentJobTestSupport.Clock();var spec=new AgentJobSpec("$child = Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList '-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"' -PassThru; [Console]::Out.WriteLine($child.Id); exit 0",null,temporary.Path,1800);
                using(var process=Factory(clock).PrepareAsync(spec,Policy(temporary.Path,false),chunk=>{},CancellationToken.None).GetAwaiter().GetResult())
                {
                    process.Resume();var budget=System.Diagnostics.Stopwatch.StartNew();
                    while(!process.Status.RootExited&&budget.ElapsedMilliseconds<10000)Task.Delay(20).GetAwaiter().GetResult();
                    var status=process.Status;Check(status.RootExited&&status.RootExitCode==0&&!status.TreeExited&&status.ActiveProcessCount>0&&!process.Completion.IsCompleted,"native root exit leaves live descendants owned and the background job incomplete");
                    AgentJobCleanupResult stopped;using(var timeout=new CancellationTokenSource(5000))stopped=process.StopAsync(timeout.Token).GetAwaiter().GetResult();
                    Check(stopped.CleanupConfirmed&&process.Status.TreeExited&&process.Status.ActiveProcessCount==0,"stop confirms the entire owned descendant tree has exited");
                    Check(!unrelated.Process.HasExited,"stopping the owned job leaves an unrelated same-name process alive");
                }
            }
        }
        static void MaximumCommandDoesNotHitWindowsArgumentLimit()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string command="[Console]::Out.Write('long-command'); #"+new string('x',16000);var output=new List<byte>();
                using(var process=Factory(clock).PrepareAsync(new AgentJobSpec(command,null,temporary.Path,1800),Policy(temporary.Path,false),chunk=>{if(chunk.Stream=="stdout")lock(output)output.AddRange(chunk.Bytes);},CancellationToken.None).GetAwaiter().GetResult())
                {
                    process.Resume();AgentJobProcessExit result;using(var timeout=new CancellationTokenSource(15000))result=AgentAuthorizationService.AwaitCancellation(process.Completion,timeout.Token).GetAwaiter().GetResult();
                    lock(output)Check(result.RootExitCode==0&&Encoding.UTF8.GetString(output.ToArray())=="long-command","a valid command near 16 KiB runs without exceeding the Win32 command-line ceiling");
                }
            }
        }
        sealed class FixtureLease : IDisposable {public void Dispose(){}}
        sealed class CrashFixtureFactory : IAgentJobProcessFactory
        {
            readonly IAgentJobProcessFactory inner;readonly string root;
            internal CrashFixtureFactory(IAgentJobProcessFactory inner,string root){this.inner=inner;this.root=root;}
            public async Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)
            {
                var process=await inner.PrepareAsync(spec,policy,output,token).ConfigureAwait(false);
                try
                {
                    var field=process.GetType().GetField("scriptDirectory",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    if(field==null)throw new Exception("Native fixture staging diagnostics are missing");
                    File.WriteAllText(Path.Combine(root,"owned-staging.txt"),(string)field.GetValue(process));return process;
                }
                catch{process.Dispose();throw;}
            }
        }
        static void DeleteCrashFixtureStaging(string root)
        {
            string record=Path.Combine(root,"owned-staging.txt");if(!File.Exists(record))return;
            string path=Path.GetFullPath(File.ReadAllText(record));string leaf=Path.GetFileName(path);Guid nonce;
            if(!String.Equals(Path.GetDirectoryName(path),Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||!leaf.StartsWith("DeskPet.Job.",StringComparison.Ordinal)||!Guid.TryParseExact(leaf.Substring("DeskPet.Job.".Length),"N",out nonce))throw new Exception("Unexpected crash-fixture staging path");
            if(Directory.Exists(path)){AgentInstructions.RejectLinks(path);Directory.Delete(path,true);}
        }
        public static int RunFixture(string[] args)
        {
            if(args.Length!=3||args[0]!="--agent-job-fixture"||args[1]!="crash-host"||Environment.OSVersion.Platform!=PlatformID.Win32NT)return 2;
            string root=Path.GetFullPath(args[2]),parent=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"agent-job-fixtures"))+Path.DirectorySeparatorChar;
            if(!root.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(root))return 2;
            AgentInstructions.RejectLinks(root);var clock=new AgentJobTestSupport.Clock();
            using(var store=new AgentJobStore(root,clock))
            {
                var manager=new AgentJobManager(store,new CrashFixtureFactory(Factory(clock),root),clock);manager.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();
                var spec=new AgentJobSpec("[IO.File]::WriteAllText("+Quote(Path.Combine(root,"child.pid"))+",[string]$PID); Start-Sleep -Seconds 60",null,root,1800);
                var invocation=new AgentToolInvocation("start_job",new Dictionary<string,object>{{"command",spec.Command},{"title",spec.Title},{"working_directory",root},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId="fixture-project",AgentId="primary",TaskId="fixture-task",CallId="fixture-call"});
                var context=new AgentJobStartContext(new AgentJobOwner("fixture-project","fixture-conversation","primary"),invocation,new AgentAuthorizationService(AgentPermissionRuntime.DefaultWorkspacePolicy(),null),()=>Policy(root,false),()=>new FixtureLease(),()=>{});
                var job=manager.StartAsync(context,spec,CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("ready:"+job.Id);Console.Out.Flush();using(var hold=new ManualResetEventSlim(false))hold.Wait(60000);return 0;
            }
        }
        static void KillingIndependentHostClosesOwnedJobAndRecoveryDoesNotRun()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                string assembly=typeof(AgentJobProcessTests).Assembly.Location;
                var start=new ProcessStartInfo{UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                if(assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)){start.FileName=Process.GetCurrentProcess().MainModule.FileName;start.Arguments="\""+assembly+"\" ";}else start.FileName=assembly;
                start.Arguments+="--agent-job-fixture crash-host \""+temporary.Path+"\"";
                using(var host=Process.Start(start))
                {
                    try
                    {
                        string ready;using(var deadline=new CancellationTokenSource(10000))ready=AgentAuthorizationService.AwaitCancellation(host.StandardOutput.ReadLineAsync(),deadline.Token).GetAwaiter().GetResult();
                        Check(ready!=null&&ready.StartsWith("ready:",StringComparison.Ordinal),"independent fixture host commits a background job before the crash");
                        string pidPath=Path.Combine(temporary.Path,"child.pid");var budget=Stopwatch.StartNew();while(!File.Exists(pidPath)&&budget.ElapsedMilliseconds<10000)Task.Delay(20).GetAwaiter().GetResult();
                        int pid=Int32.Parse(File.ReadAllText(pidPath));using(var child=Process.GetProcessById(pid))
                        {
                            host.Kill();Check(host.WaitForExit(5000)&&child.WaitForExit(5000),"killing only the independent host closes its Job handle and reaps the owned child");
                        }
                        using(var store=new AgentJobStore(temporary.Path,new AgentJobTestSupport.Clock()))
                        {
                            var records=store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                            Check(records.Count==1&&records[0].State==AgentJobState.Interrupted&&records[0].MayHaveExecuted,"recovery records the interrupted job without constructing a factory or rerunning its command");
                        }
                    }
                    finally{if(!host.HasExited){host.Kill();if(!host.WaitForExit(5000))throw new Exception("Fixture host did not exit");}DeleteCrashFixtureStaging(temporary.Path);}
                }
            }
        }
        static void ConcurrentLargeOutputDoesNotDeadlockEitherPipe()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();long stdout=0,stderr=0;
                string command="$bytes = [Text.Encoding]::UTF8.GetBytes(('x' * 65536)); $o = [Console]::OpenStandardOutput(); $e = [Console]::OpenStandardError(); for($i=0;$i -lt 128;$i++){ $o.Write($bytes,0,$bytes.Length); $e.Write($bytes,0,$bytes.Length) }; $o.Flush(); $e.Flush(); exit 0";
                using(var process=Factory(clock).PrepareAsync(new AgentJobSpec(command,null,temporary.Path,1800),Policy(temporary.Path,false),chunk=>{if(chunk.Stream=="stdout")Interlocked.Add(ref stdout,chunk.Count);else Interlocked.Add(ref stderr,chunk.Count);},CancellationToken.None).GetAwaiter().GetResult())
                {
                    process.Resume();AgentJobProcessExit result;using(var timeout=new CancellationTokenSource(15000))result=AgentAuthorizationService.AwaitCancellation(process.Completion,timeout.Token).GetAwaiter().GetResult();
                    Check(result.RootExitCode==0&&stdout==8L*1024*1024&&stderr==8L*1024*1024,"large alternating stdout/stderr output drains both pipes completely without deadlock");
                }
            }
        }
        static void EofDrainDeadlineReportsWithoutWaitingForBlockedConsumer()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var entered=new ManualResetEventSlim(false))using(var release=new ManualResetEventSlim(false))
            {
                var clock=new AgentJobTestSupport.Clock();
                using(var process=Factory(clock).PrepareAsync(new AgentJobSpec("[Console]::Out.Write('held'); exit 0",null,temporary.Path,1800),Policy(temporary.Path,false),chunk=>{entered.Set();release.Wait();},CancellationToken.None).GetAwaiter().GetResult())
                {
                    try
                    {
                        process.Resume();Check(entered.Wait(5000),"EOF fixture holds an actual output consumer after receiving native bytes");bool reported=false;
                        using(var deadline=new CancellationTokenSource(3500))try{AgentAuthorizationService.AwaitCancellation(process.Completion,deadline.Token).GetAwaiter().GetResult();}catch(IOException error){reported=error.Message=="job_stream_drain_timeout";}
                        Check(reported,"EOF deadline reports an unconfirmed drain without waiting indefinitely for a held consumer");
                    }
                    finally{release.Set();using(var deadline=new CancellationTokenSource(5000))Check(process.StopAsync(deadline.Token).GetAwaiter().GetResult().CleanupConfirmed,"held EOF consumer can be released and native cleanup confirmed");}
                }
            }
        }
        static void AssignmentFailureNeverExecutesMarker()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var type=typeof(AgentJobStore).Assembly.GetType("Dafeiyu.WindowsAgentJobProcessFactory");
                var constructor=type.GetConstructor(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(IAgentJobClock),typeof(Action<IntPtr,IntPtr>)},null);
                if(constructor==null)throw new Exception("Native assignment dependency is missing");
                var factory=(IAgentJobProcessFactory)constructor.Invoke(new object[]{new AgentJobTestSupport.Clock(),new Action<IntPtr,IntPtr>((job,process)=>{throw new System.ComponentModel.Win32Exception(5,"Injected assignment failure");})});
                string marker=Path.Combine(temporary.Path,"must-not-run.txt");bool failed=false;
                try{using(var process=factory.PrepareAsync(new AgentJobSpec("[IO.File]::WriteAllText("+Quote(marker)+",'bad')",null,temporary.Path,1800),Policy(temporary.Path,false),chunk=>{},CancellationToken.None).GetAwaiter().GetResult())process.Resume();}catch(System.ComponentModel.Win32Exception error){failed=error.NativeErrorCode==5;}
                Check(failed&&!File.Exists(marker),"native assignment failure retains its Win32 error and never runs the suspended command");
            }
        }
        public static int Main(string[] args)
        {return args.Length==0?RunAll():args.Length==1&&args[0]=="--resources-only"?RunResourceChecks():RunFixture(args);}
        public static int RunResourceChecks()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.Error.WriteLine("Windows native resource checks are unsupported on this platform");return 2;}
            checks=0;
            try
            {
                using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
                {
                    var clock=new AgentJobTestSupport.Clock();var factory=Factory(clock);var samples=new List<int>();
                    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();int before;using(var current=Process.GetCurrentProcess())before=current.HandleCount;
                    for(int i=0;i<100;i++)
                    {
                        using(var process=factory.PrepareAsync(new AgentJobSpec("exit 0",null,temporary.Path,1800),Policy(temporary.Path,false),chunk=>{},CancellationToken.None).GetAwaiter().GetResult())
                        {
                            process.Resume();AgentJobProcessExit result;using(var deadline=new CancellationTokenSource(15000))result=AgentAuthorizationService.AwaitCancellation(process.Completion,deadline.Token).GetAwaiter().GetResult();process.Dispose();
                            var property=process.GetType().GetProperty("OwnedResourceCount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);if(property==null)throw new Exception("Owned resource diagnostics are missing");
                            Check(result.RootExitCode==0&&result.TreeExited&&result.ActiveProcessCount==0&&(int)property.GetValue(process,null)==0,"short job "+(i+1)+" leaves no owned native resources or live descendants");
                        }
                        if((i+1)%25==0){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();using(var current=Process.GetCurrentProcess())samples.Add(current.HandleCount);Console.WriteLine("Collected runtime handle sample: jobs="+(i+1)+" handles="+samples.Last());}
                    }
                    Check(samples.Last()<=samples.First()+16,"collected runtime handles do not keep growing after the first resource-check batch");
                    Console.WriteLine("Native resource diagnostics: jobs=100 handles_before="+before+" collected_handle_samples="+String.Join(",",samples)+" owned_resources=0 live_descendants=0");
                }
                Console.WriteLine("AgentJobProcessResourceTests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.WriteLine("FAIL native resource checks: "+error);return 1;}
        }
        public static int RunAll()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.Error.WriteLine("Windows native background jobs are unsupported on this platform");return 2;}
            checks=0;int failures=0;foreach(var test in new Action[]{SuspendedLaunchPreservesRawStreamsAndExitCode,SuspendedStopNeverExecutesCommand,RootExitRetainsLiveDescendant,MaximumCommandDoesNotHitWindowsArgumentLimit,KillingIndependentHostClosesOwnedJobAndRecoveryDoesNotRun,ConcurrentLargeOutputDoesNotDeadlockEitherPipe,EofDrainDeadlineReportsWithoutWaitingForBlockedConsumer,AssignmentFailureNeverExecutesMarker})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobProcessTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
