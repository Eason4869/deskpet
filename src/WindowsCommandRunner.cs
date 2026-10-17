using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
namespace Dafeiyu
{
    internal static class WindowsCommandRunner
    {
        // Suspended creation closes the spawn/assign race: no project code runs
        // until it belongs to a kill-on-close Job, including all descendants.
        internal static CommandResult Run(AgentCommand command,int timeoutSeconds,CancellationToken token)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("项目命令目前仅支持 Windows。");
            token.ThrowIfCancellationRequested();IntPtr job=IntPtr.Zero,input=IntPtr.Zero;ProcessInfo process=new ProcessInfo();
            SafeFileHandle readOut=null,writeOut=null,readError=null,writeError=null;Task stdout=null,stderr=null;
            var captured=new BoundedOutput();var watch=Stopwatch.StartNew();bool timedOut=false,cancelled=false;
            try{
                var security=new SecurityAttributes{Length=Marshal.SizeOf(typeof(SecurityAttributes)),Inherit=true};
                Check(CreatePipe(out readOut,out writeOut,ref security,0));Check(CreatePipe(out readError,out writeError,ref security,0));
                Check(SetHandleInformation(readOut,1,0));Check(SetHandleInformation(readError,1,0));
                input=CreateFile("NUL",0x80000000,3,ref security,3,0,IntPtr.Zero);if(input==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
                job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                var limits=new JobLimits();limits.Basic.LimitFlags=0x2000;Check(SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(JobLimits))));
                var start=new StartupInfo{Size=Marshal.SizeOf(typeof(StartupInfo)),Flags=0x100,Input=input,Output=writeOut.DangerousGetHandle(),Error=writeError.DangerousGetHandle()};
                var line=new StringBuilder(Quote(command.Executable));foreach(string arg in command.Arguments)line.Append(' ').Append(Quote(arg));
                if(line.Length>30000)throw new ArgumentException("命令参数超过 Windows 长度限制。");
                Check(CreateProcess(command.Executable,line,IntPtr.Zero,IntPtr.Zero,true,0x08000004,IntPtr.Zero,command.Workspace,ref start,out process));
                // On assignment failure the child is still suspended; finally kills it.
                Check(AssignProcessToJobObject(job,process.Process));
                writeOut.Dispose();writeOut=null;writeError.Dispose();writeError=null;CloseHandle(input);input=IntPtr.Zero;
                var outputHandle=readOut;readOut=null;var errorHandle=readError;readError=null;
                stdout=Task.Factory.StartNew(()=>Drain(outputHandle,captured),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                stderr=Task.Factory.StartNew(()=>Drain(errorHandle,captured),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                if(ResumeThread(process.Thread)==UInt32.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
                while(true){
                    uint state=WaitForSingleObject(process.Process,50);if(state==0)break;if(state!=258)throw new Win32Exception(Marshal.GetLastWin32Error());
                    cancelled=token.IsCancellationRequested;timedOut=watch.Elapsed.TotalSeconds>=timeoutSeconds;
                    if(cancelled || timedOut){Check(TerminateJobObject(job,130));WaitForSingleObject(process.Process,2000);break;}
                }
                uint code;Check(GetExitCodeProcess(process.Process,out code));
                // A foreground command may leave background descendants holding pipes.
                Check(TerminateJobObject(job,0));CloseHandle(job);job=IntPtr.Zero;
                Task.WaitAll(new[]{stdout,stderr});
                return new CommandResult{CommandId=command.Id,Executable=command.Executable,Arguments=(string[])command.Arguments.Clone(),Workspace=command.Workspace,ExitCode=unchecked((int)code),Output=captured.Text,Truncated=captured.Truncated,TimedOut=timedOut,Cancelled=cancelled || token.IsCancellationRequested,ElapsedMilliseconds=watch.ElapsedMilliseconds};
            }
            finally{
                if(job!=IntPtr.Zero){TerminateJobObject(job,130);CloseHandle(job);}
                if(process.Process!=IntPtr.Zero){if(WaitForSingleObject(process.Process,0)==258)TerminateProcess(process.Process,130);WaitForSingleObject(process.Process,2000);CloseHandle(process.Process);}
                if(process.Thread!=IntPtr.Zero)CloseHandle(process.Thread);if(input!=IntPtr.Zero && input!=new IntPtr(-1))CloseHandle(input);
                if(writeOut!=null)writeOut.Dispose();if(writeError!=null)writeError.Dispose();if(readOut!=null)readOut.Dispose();if(readError!=null)readError.Dispose();
                if(stdout!=null && stderr!=null)Task.WaitAll(new[]{stdout,stderr});
            }
        }
        static void Drain(SafeFileHandle handle,BoundedOutput output)
        {
            using(handle)using(var stream=new FileStream(handle,FileAccess.Read))using(var reader=new StreamReader(stream,new UTF8Encoding(false,false),true)){
                char[] block=new char[4096];int n;while((n=reader.Read(block,0,block.Length))>0)output.Add(block,n);
            }
        }
        sealed class BoundedOutput
        {
            readonly object gate=new object();readonly StringBuilder text=new StringBuilder();public bool Truncated;
            public void Add(char[] value,int length){lock(gate){text.Append(value,0,length);if(text.Length>256*1024){text.Remove(0,text.Length-256*1024);Truncated=true;}}}
            public string Text{get{lock(gate)return text.ToString();}}
        }
        internal static string Quote(string value)
        {
            if(value==null || value.IndexOf('\0')>=0)throw new ArgumentException("命令参数无效。");
            var text=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')text.Append('\\',slashes*2+1).Append(c);else text.Append('\\',slashes).Append(c);slashes=0;}
            return text.Append('\\',slashes*2).Append('"').ToString();
        }
        static void Check(bool ok){if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error());}
        [StructLayout(LayoutKind.Sequential)]struct SecurityAttributes{public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct StartupInfo{public int Size;public string Reserved,Desktop,Title;public int X,Y,XSize,YSize,XChars,YChars,Fill,Flags;public short Show,ReservedSize;public IntPtr ReservedData,Input,Output,Error;}
        [StructLayout(LayoutKind.Sequential)]struct ProcessInfo{public IntPtr Process,Thread;public uint ProcessId,ThreadId;}
        [StructLayout(LayoutKind.Sequential)]struct BasicLimits{public long ProcessTime,JobTime;public uint LimitFlags;public UIntPtr MinimumWorkingSet,MaximumWorkingSet;public uint ActiveProcesses;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
        [StructLayout(LayoutKind.Sequential)]struct IoCounters{public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)]struct JobLimits{public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool CreatePipe(out SafeFileHandle read,out SafeFileHandle write,ref SecurityAttributes security,uint size);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetHandleInformation(SafeFileHandle handle,uint mask,uint flags);
        [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode,EntryPoint="CreateFileW")]static extern IntPtr CreateFile(string name,uint access,uint share,ref SecurityAttributes security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode,EntryPoint="CreateProcessW")]static extern bool CreateProcess(string application,StringBuilder line,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
        [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode,EntryPoint="CreateJobObjectW")]static extern IntPtr CreateJobObject(IntPtr security,string name);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int infoClass,ref JobLimits limits,uint length);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateJobObject(IntPtr job,uint code);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateProcess(IntPtr process,uint code);
        [DllImport("kernel32.dll",SetLastError=true)]static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr process,out uint code);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    }
}
