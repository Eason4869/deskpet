using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
namespace Dafeiyu.Tests
{
    public static class WindowsTerminalTests
    {
        static int checks;
        static void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static string Q(string text){return "'"+text.Replace("'","''")+"'";}
        sealed class Transcript
        {
            readonly StringBuilder text=new StringBuilder();readonly object gate=new object();
            internal void Add(string value){lock(gate)text.Append(value);}
            internal int Mark{get{lock(gate)return text.Length;}}
            internal string Since(int mark){lock(gate)return Plain(text.ToString(mark,text.Length-mark));}
            static string Plain(string value){return Regex.Replace(value,@"\x1b\[[0-?]*[ -/]*[@-~]|\x1b\][^\a]*(?:\a|\x1b\\)","").Replace("\r","");}
            internal string Diagnostic{get{lock(gate)return String.Join(" ",text.ToString().Select(c=>((int)c).ToString("X4")));}}
            internal string Text{get{lock(gate)return Plain(text.ToString());}}
        }
        static void Wait(Func<bool> condition,string text,int milliseconds=12000)
        {
            var clock=Stopwatch.StartNew();while(!condition()&&clock.ElapsedMilliseconds<milliseconds)Thread.Sleep(20);if(!condition())throw new Exception("Timed out: "+text);
        }
        static void Expect(Transcript output,string expected,int milliseconds=12000)
        {
            try{Wait(()=>output.Text.Contains(expected),expected,milliseconds);}catch{Console.Error.WriteLine(output.Text);throw;}
        }
        static bool Gone(int pid){try{using(var process=Process.GetProcessById(pid))return process.HasExited;}catch(ArgumentException){return true;}}
        // SetSecurityInfo may add AI when it normalizes inherited ACL metadata.
        // Ignore only that bit; retain protection and every ordered permission ACE.
        static string NormalizeAcl(string sddl){var acl=new RawSecurityDescriptor(sddl);acl.SetFlags(acl.ControlFlags & ~ControlFlags.DiscretionaryAclAutoInherited);return acl.GetSddlForm(AccessControlSections.Access);}
        static string Acl(string path){return NormalizeAcl(new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));}
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetEvent(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetHandleInformation(IntPtr handle,out uint flags);
        public static int Main(string[] args)
        {
            if(args.Length==1&&args[0]=="--noop")return 0;
            if(args.Length==2&&args[0]=="--handle-probe"){var handle=new IntPtr(Int64.Parse(args[1]));uint flags;return GetHandleInformation(handle,out flags)&&SetEvent(handle)?9:0;}
            WindowsTerminalSession first=null,second=null,restricted=null;TcpListener listener=null;
            string fixture=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","terminal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
            try
            {
                string root=Path.Combine(fixture,"project");string sub=Path.Combine(root,"子目录");Directory.CreateDirectory(sub);
                Environment.SetEnvironmentVariable("DESKPET_TERMINAL_TEST_SECRET","host-secret");
                var one=new Transcript();int firstExit=Int32.MinValue;first=WindowsTerminalSession.Start(root,false,null,one.Add,code=>Volatile.Write(ref firstExit,code));
                first.Input("Write-Host ('启动'+'就绪')\r");
                // Fresh hosted runners perform first-use PowerShell/module initialization.
                // Keep command deadlines short, but allow bounded cold shell startup.
                var startup=Stopwatch.StartNew();
                try{Expect(one,"启动就绪",60000);Console.WriteLine("Initial PowerShell readiness: "+startup.ElapsedMilliseconds+" ms");}catch{
                    Console.Error.WriteLine("Terminal readiness: running="+first.IsRunning+", pid="+first.ProcessId+", exit="+first.ExitCode+", raw UTF-16="+one.Diagnostic);
                    if(!Gone(first.ProcessId))using(var shell=Process.GetProcessById(first.ProcessId))Console.Error.WriteLine("Shell CPU="+shell.TotalProcessorTime+", threads="+shell.Threads.Count);
                    throw;
                }
                Check(first.IsRunning&&first.ProcessId>0&&one.Text.Contains("PS "),"ConPTY starts persistent interactive PowerShell and captures the initial prompt");
                first.Input("Write-Host ('中文'+'持续终端')\r");Expect(one,"中文持续终端");Check(true,"UTF-8 Chinese input and output survive the real ConPTY pipes");
                first.Input("$persist=41;$env:DESKPET_TERMINAL_STATE='first';Set-Location -LiteralPath "+Q(sub)+";Write-Host ('STATE:'+ $persist+':'+$env:DESKPET_TERMINAL_STATE+':'+(Get-Location).ProviderPath)\r");Expect(one,"STATE:41:first:"+sub);
                first.Input("$persist++;Write-Host ('NEXT:'+ $persist+':'+$env:DESKPET_TERMINAL_STATE+':'+(Get-Location).ProviderPath)\r");Expect(one,"NEXT:42:first:"+sub);Check(true,"separate input calls retain current directory shell variables and environment");
                var two=new Transcript();second=WindowsTerminalSession.Start(root,false,null,two.Add);second.Input("Write-Host ('SECOND:'+ [string]$persist+':'+[string]$env:DESKPET_TERMINAL_STATE+':'+[string]$env:DESKPET_TERMINAL_TEST_SECRET+':'+(Get-Location).ProviderPath)\r");Expect(two,"SECOND::::"+root,60000);Check(first.ProcessId!=second.ProcessId,"two live sessions have independent shells and do not inherit host secrets");
                first.Resize(113,37);first.Input("Write-Host ('SIZE:'+ [Console]::WindowWidth+':'+[Console]::WindowHeight)\r");Expect(one,"SIZE:113:37");Check(true,"resizing ConPTY changes the actual console dimensions");
                bool invalidSize=false;try{first.Resize(0,24);}catch(ArgumentOutOfRangeException){invalidSize=true;}Check(invalidSize,"invalid terminal sizes fail before a native call");
                string started=Path.Combine(root,"interrupt.started");first.Input("[IO.File]::WriteAllText("+Q(started)+",'running');Start-Sleep -Seconds 30;Write-Host ('INTERRUPT-'+'FAILED')\r");Wait(()=>File.Exists(started),"long command started");int interruptMark=one.Mark;first.Input("\x03");try{Wait(()=>Regex.Replace(one.Since(interruptMark),@"\s","").Contains(Regex.Replace("PS "+sub+">",@"\s","")),"fresh prompt after Ctrl+C");}catch{Console.Error.WriteLine("AFTER CTRL+C: "+one.Since(interruptMark));throw;}first.Input("Write-Host ('AFTER-'+'INTERRUPT')\r");Expect(one,"AFTER-INTERRUPT",5000);Check(!one.Text.Contains("INTERRUPT-FAILED")&&first.IsRunning,"Ctrl+C interrupts the running command while keeping the PowerShell session alive");
                using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset))
                {
                    first.Input("& "+Q(Assembly.GetExecutingAssembly().Location)+" '--noop';Write-Host ('NOOP:'+ $LASTEXITCODE)\r");Expect(one,"NOOP:0");
                    var handle=signal.SafeWaitHandle.DangerousGetHandle();Check(SetHandleInformation(handle,1,1),"host inheritable event fixture created");
                    first.Input("& "+Q(Assembly.GetExecutingAssembly().Location)+" '--handle-probe' '"+handle.ToInt64()+"';Write-Host ('HANDLE:'+ $LASTEXITCODE)\r");Expect(one,"HANDLE:0");Check(!signal.WaitOne(0),"unrelated inheritable host handles cannot enter the terminal or its children");
                }
                string childFile=Path.Combine(root,"child.pid");first.Input("$psi=New-Object Diagnostics.ProcessStartInfo;$psi.FileName="+Q(AgentCommandExecutor.PowerShellPath())+";$psi.Arguments='-NoLogo -NoProfile -NonInteractive -Command Start-Sleep -Seconds 90';$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$child=[Diagnostics.Process]::Start($psi);[IO.File]::WriteAllText("+Q(childFile)+",[string]$child.Id)\r");Wait(()=>File.Exists(childFile)&&File.ReadAllText(childFile).Length>0,"owned child started");int childPid=Int32.Parse(File.ReadAllText(childFile));Check(!Gone(childPid),"terminal fixture owns a real live descendant");int firstPid=first.ProcessId;first.Dispose();Wait(()=>Gone(firstPid)&&Gone(childPid),"owned job tree terminated");Wait(()=>Volatile.Read(ref firstExit)!=Int32.MinValue,"exit callback");Check(!first.IsRunning&&first.ExitCode.HasValue,"closing a session kills its own shell and descendant tree and reports exit");first.Dispose();first=null;
                second.Input("Write-Host ('SURVIVOR-'+'OK')\r");Expect(two,"SURVIVOR-OK");Check(second.IsRunning,"closing one session leaves the other terminal running");
                int secondPid=second.ProcessId;second.Input("exit 17\r");Wait(()=>!second.IsRunning,"natural shell exit");second.Dispose();Check(second.ExitCode==17&&Gone(secondPid),"natural shell exit preserves its actual exit code");second=null;
                if(args.Contains("--full-only")){Console.WriteLine("Windows terminal tests: "+checks+" passed (full-access subset)");return 0;}
                string originalAcl=Acl(root),outside=Path.Combine(fixture,"private.txt"),tools=Path.Combine(fixture,"tools");File.WriteAllText(outside,"private-sentinel");Directory.CreateDirectory(tools);string toolFile=Path.Combine(tools,"read.txt");File.WriteAllText(toolFile,"tool-sentinel");string toolAcl=Acl(tools);
                var isolated=new Transcript();int isolatedExit=Int32.MinValue;restricted=WindowsTerminalSession.Start(root,true,new[]{tools},isolated.Add,code=>Volatile.Write(ref isolatedExit,code));
                restricted.Input("[Console]::WriteLine('SANDBOX-PSHOME:'+ $PSHOME);[Console]::WriteLine('SANDBOX-MODULEPATH:'+ $env:PSModulePath);$manifest=$PSHOME+'\\Modules\\Microsoft.PowerShell.Utility\\Microsoft.PowerShell.Utility.psd1';try{[Console]::WriteLine('SANDBOX-MANIFEST:'+ [IO.File]::ReadAllText($manifest).Length)}catch{[Console]::WriteLine('SANDBOX-MANIFEST-ERROR:'+ $_.Exception.Message)}\r");
                restricted.Input("[IO.File]::WriteAllText((Join-Path $pwd.ProviderPath 'sandbox-inside.txt'),'沙箱中文');Write-Host ('SANDBOX-'+'WRITE-OK')\r");Expect(isolated,"SANDBOX-WRITE-OK",60000);Check(File.ReadAllText(Path.Combine(root,"sandbox-inside.txt"))=="沙箱中文","AppContainer ConPTY writes UTF-8 content inside its workspace");
                restricted.Input("try{[IO.File]::ReadAllText("+Q(outside)+");Write-Host ('OUTSIDE-'+'READ-FAILED')}catch{Write-Host ('OUTSIDE-'+'READ-BLOCKED')};try{[IO.File]::WriteAllText("+Q(outside)+",'bad');Write-Host ('OUTSIDE-'+'WRITE-FAILED')}catch{Write-Host ('OUTSIDE-'+'WRITE-BLOCKED')}\r");Expect(isolated,"OUTSIDE-READ-BLOCKED");Expect(isolated,"OUTSIDE-WRITE-BLOCKED");Check(!isolated.Text.Contains("OUTSIDE-READ-FAILED")&&!isolated.Text.Contains("OUTSIDE-WRITE-FAILED")&&File.ReadAllText(outside)=="private-sentinel","actual AppContainer token denies reads and writes outside the workspace");
                restricted.Input("Write-Host ('TOOLS:'+ [IO.File]::ReadAllText("+Q(toolFile)+"));try{[IO.File]::WriteAllText("+Q(toolFile)+",'bad')}catch{Write-Host ('TOOLS-'+'WRITE-BLOCKED')}\r");Expect(isolated,"TOOLS:tool-sentinel");Expect(isolated,"TOOLS-WRITE-BLOCKED");Check(File.ReadAllText(toolFile)=="tool-sentinel","explicit read directories stay readable and cannot be modified");
                listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
                restricted.Input("$net=New-Object Net.Sockets.TcpClient;try{$connect=$net.ConnectAsync('127.0.0.1',"+port+");if($connect.Wait(1200)-and $net.Connected){Write-Host ('NETWORK-'+'FAILED')}else{Write-Host ('NETWORK-'+'BLOCKED')}}catch{Write-Host ('NETWORK-'+'BLOCKED')}finally{$net.Dispose()}\r");Expect(isolated,"NETWORK-BLOCKED");Check(!isolated.Text.Contains("NETWORK-FAILED")&&!listener.Pending(),"sandbox terminal has no loopback network capability");
                restricted.Input("Write-Host ('PROFILE:'+ [string]($env:USERPROFILE -like '*DeskPet.Command.*')+':'+[string]$env:DESKPET_TERMINAL_TEST_SECRET)\r");Expect(isolated,"PROFILE:True:");Check(true,"sandbox terminal keeps its temporary profile and filtered environment");
                restricted.Dispose();restricted=null;Wait(()=>Volatile.Read(ref isolatedExit)!=Int32.MinValue,"sandbox exit callback");string restoredRootAcl=Acl(root),restoredToolAcl=Acl(tools);if(restoredRootAcl!=originalAcl||restoredToolAcl!=toolAcl){Console.Error.WriteLine("Root ACL before="+originalAcl+" after="+restoredRootAcl);Console.Error.WriteLine("Tool ACL before="+toolAcl+" after="+restoredToolAcl);}Check(restoredRootAcl==originalAcl&&restoredToolAcl==toolAcl,"closing a sandbox terminal withdraws workspace and read-directory ACL grants");
                bool failedClosed=false;try{using(var broken=WindowsTerminalSession.Start(root,true,new[]{Path.Combine(fixture,"missing-tools")}))broken.Input("'unsafe'\r");}catch(DirectoryNotFoundException){failedClosed=true;}catch(UnauthorizedAccessException){failedClosed=true;}catch(ArgumentException){failedClosed=true;}
                Check(failedClosed&&Acl(root)==originalAcl&&File.ReadAllText(outside)=="private-sentinel","failed sandbox startup restores grants and never falls back to full access");
                using(var native=new WindowsCommandRunner.TerminalProcess())
                {
                    var failedStart=System.Threading.Tasks.Task.Run(()=>{try{native.Start(root,true,new[]{tools},Path.Combine(root,"missing-shell.exe"));return false;}catch(System.ComponentModel.Win32Exception){return true;}});
                    Check(failedStart.Wait(10000)&&failedStart.Result,"CreateProcess failure after ConPTY creation drains and closes without deadlock");
                    Check(Acl(root)==originalAcl&&Acl(tools)==toolAcl,"post-ConPTY startup failure restores all sandbox directory ACLs");
                }
                Console.WriteLine("Windows terminal tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally
            {
                Environment.SetEnvironmentVariable("DESKPET_TERMINAL_TEST_SECRET",null);if(listener!=null)listener.Stop();foreach(var terminal in new[]{first,second,restricted})if(terminal!=null)try{terminal.Dispose();}catch(Exception e){Console.Error.WriteLine("Cleanup: "+e);}
            }
        }
    }
}
