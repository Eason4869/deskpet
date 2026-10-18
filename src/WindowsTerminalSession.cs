using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Dafeiyu
{
    // Output and Exited run on background workers; subscribers should dispatch
    // promptly to the UI. Startup callbacks are installed before the shell runs.
    internal sealed class WindowsTerminalSession:IDisposable
    {
        readonly WindowsCommandRunner.TerminalProcess process;
        WindowsTerminalSession(WindowsCommandRunner.TerminalProcess process){this.process=process;}
        internal static WindowsTerminalSession Start(string workspace,bool sandbox,string[] readDirectories,Action<string> output=null,Action<int> exited=null)
        {
            var native=new WindowsCommandRunner.TerminalProcess();native.Output+=output;native.Exited+=exited;
            native.Start(workspace,sandbox,readDirectories,AgentCommandExecutor.PowerShellPath());return new WindowsTerminalSession(native);
        }
        internal event Action<string> Output{add{process.Output+=value;}remove{process.Output-=value;}}
        internal event Action<int> Exited{add{process.Exited+=value;}remove{process.Exited-=value;}}
        internal bool IsRunning{get{return process.IsRunning;}}
        internal int ProcessId{get{return process.ProcessId;}}
        internal int? ExitCode{get{return process.ExitCode;}}
        internal void Input(string data){process.Input(data);}
        internal void Resize(short cols,short rows){process.Resize(cols,rows);}
        public void Dispose(){process.Dispose();}
    }

    internal static partial class WindowsCommandRunner
    {
        internal sealed class TerminalProcess:IDisposable
        {
            readonly object gate=new object();
            readonly BlockingCollection<byte[]> input=new BlockingCollection<byte[]>(32);
            readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>();
            IntPtr job,console,process;
            WindowsSandboxLease sandbox;
            SafeFileHandle inputWrite,outputRead;
            Task reader,writer,watcher;
            int running,readerThread,writerThread;
            int? exitCode;
            internal event Action<string> Output;
            internal event Action<int> Exited;
            internal bool IsRunning{get{return Volatile.Read(ref running)!=0;}}
            internal int ProcessId{get;private set;}
            internal int? ExitCode{get{lock(gate)return exitCode;}}

            internal void Start(string workspace,bool isolated,string[] readDirectories,string executable)
            {
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("内置终端需要支持 ConPTY 的 Windows 10 或更新系统。");
                workspace=Path.GetFullPath(workspace);if(!Directory.Exists(workspace))throw new DirectoryNotFoundException("终端项目目录不存在。");
                SafeFileHandle inputRead=null,outputWrite=null;IntPtr environment=IntPtr.Zero;ProcessInfo child=new ProcessInfo();
                try
                {
                    if(isolated)sandbox=new WindowsSandboxLease(workspace,readDirectories);
                    var security=new SecurityAttributes{Length=Marshal.SizeOf(typeof(SecurityAttributes)),Inherit=false};
                    Check(CreatePipe(out inputRead,out inputWrite,ref security,0));Check(CreatePipe(out outputRead,out outputWrite,ref security,0));
                    HResult(CreatePseudoConsole(new TerminalSize{Columns=100,Rows=28},inputRead.DangerousGetHandle(),outputWrite.DangerousGetHandle(),0,out console));
                    reader=Task.Factory.StartNew(ReadOutput,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                    job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());var limits=new JobLimits();limits.Basic.LimitFlags=0x2000;Check(SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(JobLimits))));
                    environment=BuildEnvironment(sandbox);
                    string setup="$ProgressPreference='SilentlyContinue'; $OutputEncoding=[Console]::InputEncoding=[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); if(Get-Command Set-PSReadLineOption -ErrorAction SilentlyContinue){Set-PSReadLineOption -HistorySaveStyle SaveNothing}; ";
                    if(isolated)setup+="New-PSDrive -Name workspace -PSProvider FileSystem -Root "+PowerShellLiteral(workspace)+" -Scope Global | Out-Null; Set-Location workspace:\\;";
                    else setup+="Set-Location -LiteralPath "+PowerShellLiteral(workspace)+";";
                    var command=new StringBuilder(Quote(executable)).Append(" -NoLogo -NoProfile -NoExit -OutputFormat Text -EncodedCommand ").Append(Convert.ToBase64String(Encoding.Unicode.GetBytes(setup)));
                    if(sandbox==null)WindowsSandboxLease.CreateWithPseudoConsole(executable,command,environment,workspace,console,out child);
                    else sandbox.CreatePseudoConsoleProcess(executable,command,environment,workspace,console,out child);
                    process=child.Process;ProcessId=unchecked((int)child.ProcessId);Check(AssignProcessToJobObject(job,process));
                    // Keep draining while ClosePseudoConsole executes: its final
                    // screen update can block until the output pipe is consumed.
                    inputRead.Dispose();inputRead=null;outputWrite.Dispose();outputWrite=null;
                    writer=Task.Factory.StartNew(WriteInput,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                    Volatile.Write(ref running,1);
                    if(ResumeThread(child.Thread)==UInt32.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
                    watcher=Task.Factory.StartNew(WatchExit,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                }
                catch
                {
                    // No retries in a less restricted mode. The process remains
                    // suspended until assignment succeeds, so failure is closed.
                    if(process!=IntPtr.Zero)TerminateProcess(process,130);
                    // Close our copies before waiting for the drain. Otherwise
                    // the still-open output writer prevents EOF on failed spawn.
                    if(inputRead!=null){inputRead.Dispose();inputRead=null;}if(outputWrite!=null){outputWrite.Dispose();outputWrite=null;}
                    Cleanup(130);throw;
                }
                finally
                {
                    if(child.Thread!=IntPtr.Zero)CloseHandle(child.Thread);if(environment!=IntPtr.Zero)Marshal.FreeHGlobal(environment);
                    if(inputRead!=null)inputRead.Dispose();if(outputWrite!=null)outputWrite.Dispose();
                }
            }

            static string PowerShellLiteral(string text){return "'"+text.Replace("'","''")+"'";}
            static void HResult(int code){if(code<0)Marshal.ThrowExceptionForHR(code);}
            internal void Input(string data)
            {
                if(data==null)throw new ArgumentNullException("data");if(data.Length==0)return;
                byte[] bytes=Encoding.UTF8.GetBytes(data);if(bytes.Length>1024*1024)throw new ArgumentException("单次终端输入不能超过 1 MiB。");
                lock(gate){if(!IsRunning||input.IsAddingCompleted)throw new ObjectDisposedException("WindowsTerminalSession");if(!input.TryAdd(bytes))throw new IOException("终端输入队列已满，请稍后重试。");}
            }
            internal void Resize(short cols,short rows)
            {
                if(cols<2||rows<1||cols>4096||rows>4096)throw new ArgumentOutOfRangeException("cols","终端尺寸需要在 2–4096 列、1–4096 行以内。");
                lock(gate){if(!IsRunning||console==IntPtr.Zero)throw new ObjectDisposedException("WindowsTerminalSession");HResult(ResizePseudoConsole(console,new TerminalSize{Columns=cols,Rows=rows}));}
            }
            void ReadOutput()
            {
                readerThread=Thread.CurrentThread.ManagedThreadId;
                try
                {
                    using(var stream=new FileStream(outputRead,FileAccess.Read))
                    {
                        var decoder=new UTF8Encoding(false,false).GetDecoder();byte[] bytes=new byte[4096];char[] chars=new char[4096];int count;
                        while((count=stream.Read(bytes,0,bytes.Length))>0){int length=decoder.GetChars(bytes,0,count,chars,0,false);if(length>0)Emit(new string(chars,0,length));}
                    }
                }
                catch(IOException){RequestStop();}catch(ObjectDisposedException){}
            }
            void WriteInput()
            {
                writerThread=Thread.CurrentThread.ManagedThreadId;
                try{using(var stream=new FileStream(inputWrite,FileAccess.Write))foreach(var bytes in input.GetConsumingEnumerable()){stream.Write(bytes,0,bytes.Length);stream.Flush();}}
                catch(IOException){RequestStop();}catch(ObjectDisposedException){}
            }
            void Emit(string text)
            {
                var output=Output;if(output==null)return;foreach(Action<string> callback in output.GetInvocationList())try{callback(text);}catch(Exception e){System.Diagnostics.Trace.WriteLine("Terminal output subscriber: "+e.GetType().Name);}
            }
            void RequestStop()
            {
                lock(gate){if(job!=IntPtr.Zero)TerminateJobObject(job,130);}
            }
            void WatchExit()
            {
                int code=130;
                try{uint state=WaitForSingleObject(process,UInt32.MaxValue);if(state!=0)throw new Win32Exception(Marshal.GetLastWin32Error());uint nativeCode;Check(GetExitCodeProcess(process,out nativeCode));code=unchecked((int)nativeCode);}
                catch(Exception e){Emit("\r\n终端已停止："+e.Message+"\r\n");}
                Cleanup(code);
            }
            void Cleanup(int code)
            {
                Exception failure=null;IntPtr ownedConsole;
                lock(gate)
                {
                    Volatile.Write(ref running,0);exitCode=code;input.CompleteAdding();
                    if(job!=IntPtr.Zero){TerminateJobObject(job,130);CloseHandle(job);job=IntPtr.Zero;}
                    if(process!=IntPtr.Zero){WaitForSingleObject(process,2000);CloseHandle(process);process=IntPtr.Zero;}
                    ownedConsole=console;console=IntPtr.Zero;
                }
                try{if(ownedConsole!=IntPtr.Zero)ClosePseudoConsole(ownedConsole);}catch(Exception e){failure=e;}
                foreach(var task in new[]{reader,writer})if(task!=null)try{if(!task.Wait(5000))failure=new IOException("终端 I/O 尚未退出。");}catch(AggregateException e){failure=e;}
                if(outputRead!=null)outputRead.Dispose();if(inputWrite!=null)inputWrite.Dispose();
                if(sandbox!=null)try{sandbox.Dispose();}catch(Exception e){failure=e;}finally{sandbox=null;}
                if(failure==null)completion.TrySetResult(true);else{Emit("\r\n终端清理失败："+failure.Message+"\r\n");completion.TrySetException(failure);}
                var exited=Exited;if(exited!=null)foreach(Action<int> callback in exited.GetInvocationList())try{callback(code);}catch(Exception e){System.Diagnostics.Trace.WriteLine("Terminal exit subscriber: "+e.GetType().Name);}
            }
            public void Dispose()
            {
                RequestStop();
                // A subscriber may close its tab from an output callback. Let
                // the watcher finish cleanup without waiting on that same reader.
                int thread=Thread.CurrentThread.ManagedThreadId;
                if(thread!=readerThread&&thread!=writerThread)completion.Task.GetAwaiter().GetResult();
            }
        }
        [StructLayout(LayoutKind.Sequential)]struct TerminalSize{internal short Columns,Rows;}
        [DllImport("kernel32.dll")]static extern int CreatePseudoConsole(TerminalSize size,IntPtr input,IntPtr output,uint flags,out IntPtr console);
        [DllImport("kernel32.dll")]static extern int ResizePseudoConsole(IntPtr console,TerminalSize size);
        [DllImport("kernel32.dll")]static extern void ClosePseudoConsole(IntPtr console);
    }
}
