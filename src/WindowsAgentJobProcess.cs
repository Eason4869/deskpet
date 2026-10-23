using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
namespace Dafeiyu
{
    public sealed class WindowsAgentJobProcessFactory : IAgentJobProcessFactory
    {
        readonly IAgentJobClock clock;
        readonly Action<IntPtr,IntPtr> assign;
        public WindowsAgentJobProcessFactory(IAgentJobClock clock):this(clock,WindowsCommandRunner.AssignBackgroundProcess){}
        internal WindowsAgentJobProcessFactory(IAgentJobClock clock,Action<IntPtr,IntPtr> assign)
        {if(clock==null||assign==null)throw new ArgumentNullException("services");this.clock=clock;this.assign=assign;}
        public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("background_jobs_unavailable");
            if(spec==null||policy==null||output==null)throw new ArgumentNullException("job");
            return Task.Run<IAgentJobProcess>(()=>new WindowsCommandRunner.BackgroundJobProcess(spec,policy,output,clock,assign,token),token);
        }
    }
    internal static partial class WindowsCommandRunner
    {
        internal static void AssignBackgroundProcess(IntPtr job,IntPtr process){Check(AssignProcessToJobObject(job,process));}
        [StructLayout(LayoutKind.Sequential)]struct JobAccounting
        {internal long UserTime,KernelTime,PeriodUserTime,PeriodKernelTime;internal uint Faults,Total,Active,Terminated;}
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool QueryInformationJobObject(IntPtr job,int informationClass,out JobAccounting accounting,uint length,IntPtr returned);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool PeekNamedPipe(SafeFileHandle pipe,IntPtr buffer,uint size,IntPtr read,out uint available,IntPtr remaining);
        internal sealed class BackgroundJobProcess : IAgentJobProcess,IAgentJobPreparationMetrics,IAgentJobCompletionMetrics
        {
            readonly object nativeGate=new object();
            readonly IAgentJobClock clock;
            readonly Action<AgentJobOutputChunk> output;
            readonly CancellationTokenSource drainCancellation=new CancellationTokenSource();
            readonly TaskCompletionSource<AgentJobProcessExit> completion=new TaskCompletionSource<AgentJobProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
            volatile AgentJobProcessExit status=new AgentJobProcessExit(false,null,false,false,1);
            Task stdout,stderr;
            volatile Task<bool> resourceCleanup;
            SafeFileHandle stdoutPipe,stderrPipe;
            IntPtr job,process,thread;
            WindowsSandboxLease sandbox;
            string scriptDirectory;
            bool ownScriptDirectory,resumed,disposed,assigned,stdoutEof,stderrEof,terminationRequested;
            int? naturalExitBeforeStop;
            long eofDrainMilliseconds=-1;
            public long SandboxPreparationMilliseconds {get;private set;}
            public long? EofDrainMilliseconds {get{var value=Interlocked.Read(ref eofDrainMilliseconds);return value<0?null:(long?)value;}}
            public AgentJobProcessExit Status {get{return status;}}
            public Task<AgentJobProcessExit> Completion {get{return completion.Task;}}
            internal int OwnedResourceCount
            {
                get{lock(nativeGate)return (job==IntPtr.Zero?0:1)+(process==IntPtr.Zero?0:1)+(thread==IntPtr.Zero?0:1)+(stdoutPipe==null||stdoutPipe.IsClosed?0:1)+(stderrPipe==null||stderrPipe.IsClosed?0:1)+(sandbox==null?0:1)+(scriptDirectory!=null&&Directory.Exists(scriptDirectory)?1:0)+(disposed?0:1);}
            }
            internal BackgroundJobProcess(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,IAgentJobClock clock,Action<IntPtr,IntPtr> assign,CancellationToken token)
            {
                this.output=output;this.clock=clock;
                SafeFileHandle readOut=null,writeOut=null,readError=null,writeError=null;IntPtr input=IntPtr.Zero,environment=IntPtr.Zero;
                try
                {
                    token.ThrowIfCancellationRequested();
                    if(!policy.TerminalAllowed||policy.ReadOnly)throw new UnauthorizedAccessException("job_permission_denied");
                    if(policy.Sandbox)
                    {
                        var preparation=Stopwatch.StartNew();sandbox=new WindowsSandboxLease(policy.Root,policy.ReadDirectories.ToArray(),token);SandboxPreparationMilliseconds=preparation.ElapsedMilliseconds;
                    }
                    scriptDirectory=sandbox==null?Path.Combine(Path.GetTempPath(),"DeskPet.Job."+Guid.NewGuid().ToString("N")):sandbox.TemporaryDirectory;ownScriptDirectory=sandbox==null;
                    AgentInstructions.RejectLinks(scriptDirectory);Directory.CreateDirectory(scriptDirectory);
                    string script=AgentCommandExecutor.PowerShellBootstrap(policy.Sandbox,spec.WorkingDirectory)+spec.Command+"\r\n$deskpetCommandSucceeded = $?\r\nif ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\r\nif (-not $deskpetCommandSucceeded) { exit 1 }";
                    // A file avoids the Win32 command-line ceiling for a valid 16 KiB command.
                    string scriptPath=Path.Combine(scriptDirectory,"command.ps1");File.WriteAllText(scriptPath,script,new UTF8Encoding(true));
                    var security=new SecurityAttributes{Length=Marshal.SizeOf(typeof(SecurityAttributes)),Inherit=true};
                    Check(CreatePipe(out readOut,out writeOut,ref security,0));Check(CreatePipe(out readError,out writeError,ref security,0));
                    Check(SetHandleInformation(readOut,1,0));Check(SetHandleInformation(readError,1,0));
                    input=CreateFile("NUL",0x80000000,3,ref security,3,0,IntPtr.Zero);if(input==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
                    job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                    var limits=new JobLimits();limits.Basic.LimitFlags=0x2000;Check(SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(JobLimits))));
                    var start=new StartupInfo{Size=Marshal.SizeOf(typeof(StartupInfo)),Flags=0x100,Input=input,Output=writeOut.DangerousGetHandle(),Error=writeError.DangerousGetHandle()};
                    string shell=AgentCommandExecutor.PowerShellPath();var line=new StringBuilder(Quote(shell));
                    foreach(var argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-OutputFormat","Text","-ExecutionPolicy","Bypass","-File",scriptPath})line.Append(' ').Append(Quote(argument));
                    environment=BuildEnvironment(sandbox);ProcessInfo created;
                    if(sandbox==null)WindowsSandboxLease.CreateWithHandles(shell,line,environment,spec.WorkingDirectory,ref start,out created,IntPtr.Zero);
                    else sandbox.CreateProcess(shell,line,environment,spec.WorkingDirectory,ref start,out created);
                    process=created.Process;thread=created.Thread;assign(job,process);assigned=true;
                    writeOut.Dispose();writeOut=null;writeError.Dispose();writeError=null;CloseHandle(input);input=IntPtr.Zero;
                    var stdoutHandle=readOut;readOut=null;var stderrHandle=readError;readError=null;
                    stdoutPipe=stdoutHandle;stderrPipe=stderrHandle;
                    stdout=Task.Factory.StartNew(()=>DrainRaw(stdoutHandle,"stdout"),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                    stderr=Task.Factory.StartNew(()=>DrainRaw(stderrHandle,"stderr"),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                    token.ThrowIfCancellationRequested();WatchAsync();
                }
                catch{Dispose();throw;}
                finally
                {
                    if(environment!=IntPtr.Zero)Marshal.FreeHGlobal(environment);
                    if(input!=IntPtr.Zero&&input!=new IntPtr(-1))CloseHandle(input);
                    if(writeOut!=null)writeOut.Dispose();if(writeError!=null)writeError.Dispose();if(readOut!=null)readOut.Dispose();if(readError!=null)readError.Dispose();
                }
            }
            public void Resume()
            {
                lock(nativeGate)
                {
                    if(disposed)throw new ObjectDisposedException("background_job");if(resumed)throw new InvalidOperationException("job_already_resumed");
                    if(ResumeThread(thread)==UInt32.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());resumed=true;CloseHandle(thread);thread=IntPtr.Zero;
                }
            }
            void DrainRaw(SafeFileHandle handle,string streamName)
            {
                using(handle)using(var stream=new FileStream(handle,FileAccess.Read))
                {
                    var buffer=new byte[8192];
                    while(!drainCancellation.IsCancellationRequested)
                    {
                        uint available;
                        if(!PeekNamedPipe(handle,IntPtr.Zero,0,IntPtr.Zero,out available,IntPtr.Zero))
                        {
                            int error=Marshal.GetLastWin32Error();if(error==109||error==233){if(streamName=="stdout")stdoutEof=true;else stderrEof=true;return;}throw new Win32Exception(error);
                        }
                        if(available==0){drainCancellation.Token.WaitHandle.WaitOne(10);continue;}
                        int count=stream.Read(buffer,0,(int)Math.Min((uint)buffer.Length,available));if(count==0){if(streamName=="stdout")stdoutEof=true;else stderrEof=true;return;}
                        try{output(new AgentJobOutputChunk(streamName,buffer,count,clock.UtcNow));}catch(Exception error){Debug.WriteLine(error);}
                    }
                }
            }
            AgentJobProcessExit QueryStatus()
            {
                lock(nativeGate)
                {
                    if(disposed)return status;
                    JobAccounting accounting;Check(QueryInformationJobObject(job,1,out accounting,(uint)Marshal.SizeOf(typeof(JobAccounting)),IntPtr.Zero));
                    uint state=WaitForSingleObject(process,0);if(state!=0&&state!=258)throw new Win32Exception(Marshal.GetLastWin32Error());
                    int? code=null;if(state==0){uint raw;Check(GetExitCodeProcess(process,out raw));code=terminationRequested?naturalExitBeforeStop:(int?)unchecked((int)raw);}
                    status=new AgentJobProcessExit(state==0,code,accounting.Active==0&&state==0,stdoutEof&&stderrEof,(int)accounting.Active);return status;
                }
            }
            async void WatchAsync()
            {
                try
                {
                    while(!QueryStatus().TreeExited)await Task.Delay(20).ConfigureAwait(false);
                    var elapsed=Stopwatch.StartNew();var drains=Task.WhenAll(stdout,stderr);if(await Task.WhenAny(drains,Task.Delay(2000)).ConfigureAwait(false)!=drains)
                    {Interlocked.CompareExchange(ref eofDrainMilliseconds,elapsed.ElapsedMilliseconds,-1);drainCancellation.Cancel();throw new IOException("job_stream_drain_timeout");}
                    Interlocked.CompareExchange(ref eofDrainMilliseconds,elapsed.ElapsedMilliseconds,-1);
                    await drains.ConfigureAwait(false);completion.TrySetResult(QueryStatus());
                }
                catch(Exception error){completion.TrySetException(error);}
            }
            public async Task<AgentJobCleanupResult> StopAsync(CancellationToken token)
            {
                try
                {
                    using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        deadline.CancelAfter(4500);
                        var pending=resourceCleanup;
                        if(pending!=null&&pending.IsFaulted)
                        {Debug.WriteLine(pending.Exception);lock(nativeGate){if(ReferenceEquals(pending,resourceCleanup))resourceCleanup=null;pending=resourceCleanup;}}
                        if(pending!=null){await AgentAuthorizationService.AwaitCancellation(pending,deadline.Token).ConfigureAwait(false);return new AgentJobCleanupResult(true,null);}
                        lock(nativeGate){if(disposed)return new AgentJobCleanupResult(true,null);RequestTermination();}
                        while(!QueryStatus().TreeExited){deadline.Token.ThrowIfCancellationRequested();await Task.Delay(20,deadline.Token).ConfigureAwait(false);}
                        var eofElapsed=Stopwatch.StartNew();
                        await AgentAuthorizationService.AwaitCancellation(Task.WhenAll(stdout,stderr).ContinueWith(task=>{if(task.Exception!=null)Debug.WriteLine(task.Exception);return true;},TaskScheduler.Default),deadline.Token).ConfigureAwait(false);
                        Interlocked.CompareExchange(ref eofDrainMilliseconds,eofElapsed.ElapsedMilliseconds,-1);
                        lock(nativeGate){if(resourceCleanup==null)resourceCleanup=Task.Run(delegate{Dispose();return true;});pending=resourceCleanup;}
                        await AgentAuthorizationService.AwaitCancellation(pending,deadline.Token).ConfigureAwait(false);
                    }
                    return new AgentJobCleanupResult(true,null);
                }
                catch(Exception error){Debug.WriteLine(error);return new AgentJobCleanupResult(false,"job_cleanup_unconfirmed");}
            }
            public void Dispose()
            {
                lock(nativeGate)
                {
                    if(disposed)return;
                    if(job!=IntPtr.Zero&&process!=IntPtr.Zero&&assigned)RequestTermination();
                    else if(job!=IntPtr.Zero)TerminateJobObject(job,130);
                    if(process!=IntPtr.Zero&&!assigned)TerminateProcess(process,130);
                }
                drainCancellation.Cancel();
                var drains=new[]{stdout,stderr}.Where(task=>task!=null).ToArray();
                try{if(drains.Length>0&&!Task.WaitAll(drains,2000))throw new IOException("job_stream_cleanup_unconfirmed");}catch(AggregateException error){Debug.WriteLine(error);}
                lock(nativeGate)
                {
                    if(disposed)return;
                    if(stdoutPipe!=null)stdoutPipe.Dispose();if(stderrPipe!=null)stderrPipe.Dispose();
                    if(process!=IntPtr.Zero)
                    {
                        if(WaitForSingleObject(process,2000)!=0)throw new IOException("job_tree_cleanup_unconfirmed");
                        if(assigned&&job!=IntPtr.Zero&&!QueryStatus().TreeExited)throw new IOException("job_tree_cleanup_unconfirmed");
                    }
                    // Retain native handles if withdrawing the sandbox grants fails.
                    if(sandbox!=null){sandbox.Dispose();sandbox=null;}
                    if(ownScriptDirectory&&scriptDirectory!=null&&Directory.Exists(scriptDirectory)){AgentInstructions.RejectLinks(scriptDirectory);Directory.Delete(scriptDirectory,true);}
                    if(process!=IntPtr.Zero){CloseHandle(process);process=IntPtr.Zero;}
                    if(thread!=IntPtr.Zero){CloseHandle(thread);thread=IntPtr.Zero;}
                    if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}
                    disposed=true;drainCancellation.Dispose();completion.TrySetResult(status);
                }
            }
            void RequestTermination()
            {
                if(!terminationRequested){naturalExitBeforeStop=QueryStatus().RootExitCode;terminationRequested=true;}
                Check(TerminateJobObject(job,130));
            }
        }
    }
}
