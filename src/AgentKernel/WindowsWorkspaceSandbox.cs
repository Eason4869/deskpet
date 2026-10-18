using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
namespace Dafeiyu
{
    internal static partial class WindowsCommandRunner
    {
        internal static CommandResult RunSandboxed(AgentCommand command,int timeout,CancellationToken cancel)
        {return RunSandboxedAtRoot(command,timeout,cancel,command.Workspace);}
        internal static CommandResult RunSandboxedAtRoot(AgentCommand command,int timeout,CancellationToken cancel,string root)
        {return RunSandboxedWithTools(command,timeout,cancel,root,new string[0]);}
        internal static CommandResult RunSandboxedWithTools(AgentCommand command,int timeout,CancellationToken cancel,string root,string[] readDirectories)
        {
            cancel.ThrowIfCancellationRequested();
            using(var sandbox=new WindowsSandboxLease(root,readDirectories,cancel)){
                var result=Run(command,timeout,cancel,true,sandbox);result.IsolationDescription="Windows AppContainer; project write access; no network capabilities; isolated temporary profile";return result;
            }
        }
        // One identity per command prevents concurrent projects from sharing grants.
        internal sealed class WindowsSandboxLease:IDisposable
        {
            readonly string name="DeskPet.Command."+Guid.NewGuid().ToString("N");
            readonly List<Tuple<string,FileSystemAccessRule>> granted=new List<Tuple<string,FileSystemAccessRule>>();SecurityIdentifier identity;IntPtr sid;
            internal string TemporaryDirectory {get;private set;}
            readonly CancellationToken cancellation;
            internal WindowsSandboxLease(string root,string[] readDirectories,CancellationToken cancel=default(CancellationToken))
            {
                cancellation=cancel;
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("Windows workspace sandbox unavailable.");
                root=Path.GetFullPath(root);if(!Directory.Exists(root)||root.TrimEnd('\\')==Path.GetPathRoot(root).TrimEnd('\\'))throw new ArgumentException("沙箱需要项目文件夹，不能授权整个磁盘。");
                ValidateGrantPath(root);ValidateTree(root);
                try{
                    CheckHResult(CreateAppContainerProfile(name,name,"DeskPet workspace command",IntPtr.Zero,0,out sid));identity=new SecurityIdentifier(sid);
                    TemporaryDirectory=Path.Combine(Path.GetTempPath(),name);Directory.CreateDirectory(TemporaryDirectory);
                    // Seal host-owned records before the first inheritable project
                    // grant. Package-SID deny ACEs do not reliably block lowbox
                    // access; these directories must never inherit package allows.
                    using(new AclMutationLock()){
                        foreach(string reserved in new[]{".deskpet-agent-kernel",".deskpet-agent-backups"}){string path=Path.Combine(root,reserved);Directory.CreateDirectory(path);Protect(path);}
                        Grant(root);
                    }
                    Grant(TemporaryDirectory);
                    foreach(string directory in readDirectories??new string[0]){ValidateGrantPath(directory);ValidateTree(directory);AddRule(directory,new FileSystemAccessRule(identity,FileSystemRights.ReadAndExecute|FileSystemRights.Synchronize,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));}
                }catch{Dispose();throw;}
            }
            static void NativeCheck(bool success,string action){if(!success)throw new Win32Exception(Marshal.GetLastWin32Error(),action);}
            static void CheckHResult(int result){if(result<0)Marshal.ThrowExceptionForHR(result);}
            FileSystemAccessRule Rule(){return new FileSystemAccessRule(identity,FileSystemRights.Modify|FileSystemRights.Synchronize,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow);}
            void Grant(string path)
            {
                // Validate each ancestor before granting; do not follow directory junctions.
                ValidateGrantPath(path);
                ValidateTree(path);
                AddRule(path,Rule());
            }
            static void ValidateGrantPath(string path)
            {
                AgentSandboxDirectories.Validate(path);
                for(var info=new DirectoryInfo(Path.GetFullPath(path));info!=null;info=info.Parent){if(info.Name.Equals(".deskpet-agent-kernel",StringComparison.OrdinalIgnoreCase)||info.Name.Equals(".deskpet-agent-backups",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("宿主执行记录与备份目录不能授权给终端。");if((info.Attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("沙箱目录不能经过重解析点。");}
            }
            void ValidateTree(string root)
            {
                var pending=new Stack<string>();pending.Push(root);int count=0;
                while(pending.Count>0){cancellation.ThrowIfCancellationRequested();string path=pending.Pop();foreach(var item in new DirectoryInfo(path).EnumerateFileSystemInfos()){cancellation.ThrowIfCancellationRequested();if(++count>100000)throw new IOException("沙箱目录超过 100000 项，请选择更小的项目或工具目录。");if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("沙箱目录含符号链接或重解析点，请选择不含链接的目录。");if((item.Attributes&FileAttributes.Directory)!=0)pending.Push(item.FullName);else AgentSandboxDirectories.ValidateFile(item.FullName);}}
            }
            void Protect(string path)
            {
                Seal(new DirectoryInfo(path),true);
                var pending=new Stack<string>();pending.Push(path);
                while(pending.Count>0){cancellation.ThrowIfCancellationRequested();foreach(var item in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos()){Seal(item,false);if(item is DirectoryInfo)pending.Push(item.FullName);}}
            }
            static void Seal(FileSystemInfo item,bool root)
            {
                if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("沙箱记录目录不能是重解析点。");
                var directory=item as DirectoryInfo;FileSystemSecurity acl=directory==null?(FileSystemSecurity)((FileInfo)item).GetAccessControl():directory.GetAccessControl();
                var packages=new List<FileSystemAccessRule>();foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))if(rule.AccessControlType==AccessControlType.Allow&&rule.IdentityReference.Value.StartsWith("S-1-15-2-",StringComparison.Ordinal))packages.Add(rule);
                if(!root&&packages.Count==0)return;acl.SetAccessRuleProtection(true,true);foreach(var rule in packages)acl.RemoveAccessRuleSpecific(new FileSystemAccessRule((SecurityIdentifier)rule.IdentityReference,rule.FileSystemRights,rule.InheritanceFlags,rule.PropagationFlags,AccessControlType.Allow));
                if(directory==null)((FileInfo)item).SetAccessControl((FileSecurity)acl);else directory.SetAccessControl((DirectorySecurity)acl);
            }
            void AddRule(string path,FileSystemAccessRule rule){using(new AclMutationLock()){var directory=new DirectoryInfo(path);var acl=directory.GetAccessControl();acl.AddAccessRule(rule);directory.SetAccessControl(acl);}granted.Add(Tuple.Create(path,rule));}
            // Parent ACL propagation also changes child descriptors, so one per-user
            // mutex covers all grants and withdrawals, including other app instances.
            sealed class AclMutationLock:IDisposable
            {
                readonly Mutex mutex;bool entered;
                internal AclMutationLock(){mutex=new Mutex(false,"Local\\DeskPet.SandboxAcl."+WindowsIdentity.GetCurrent().User.Value);try{try{entered=mutex.WaitOne(10000);}catch(AbandonedMutexException){entered=true;}if(!entered)throw new IOException("沙箱目录权限正在更新，请稍后重试。");}catch{mutex.Dispose();throw;}}
                public void Dispose(){if(entered){entered=false;mutex.ReleaseMutex();}mutex.Dispose();}
            }
            internal void CreateProcess(string executable,StringBuilder command,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process)
            {CreateWithHandles(executable,command,environment,directory,ref startup,out process,sid);}
            internal void CreatePseudoConsoleProcess(string executable,StringBuilder command,IntPtr environment,string directory,IntPtr console,out ProcessInfo process)
            {CreateWithPseudoConsole(executable,command,environment,directory,console,out process,sid);}
            internal static void CreateWithPseudoConsole(string executable,StringBuilder command,IntPtr environment,string directory,IntPtr console,out ProcessInfo process,IntPtr packageSid=default(IntPtr))
            {
                IntPtr attributes=IntPtr.Zero,size=IntPtr.Zero,capabilities=IntPtr.Zero;
                try{
                    int count=packageSid==IntPtr.Zero?1:2;InitializeProcThreadAttributeList(IntPtr.Zero,count,0,ref size);attributes=Marshal.AllocHGlobal(size);NativeCheck(InitializeProcThreadAttributeList(attributes,count,0,ref size),"initialize terminal attributes");
                    NativeCheck(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20016),console,new IntPtr(IntPtr.Size),IntPtr.Zero,IntPtr.Zero),"set pseudoconsole");
                    if(packageSid!=IntPtr.Zero){var caps=new SecurityCapabilities{AppContainerSid=packageSid};capabilities=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SecurityCapabilities)));Marshal.StructureToPtr(caps,capabilities,false);NativeCheck(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20009),capabilities,new IntPtr(Marshal.SizeOf(typeof(SecurityCapabilities))),IntPtr.Zero,IntPtr.Zero),"set terminal security capabilities");}
                    var extended=new StartupInfoEx{Attributes=attributes};extended.Startup.Size=Marshal.SizeOf(typeof(StartupInfoEx));
                    // Explicit null standard handles suppress CreateProcess's
                    // legacy duplication of redirected parent standard handles.
                    // Console initialization replaces nulls with ConPTY handles.
                    extended.Startup.Flags=0x100;
                    // ConPTY supplies the console handles. No host handle is
                    // inherited, including otherwise-inheritable unrelated pipes.
                    NativeCheck(CreateSandboxProcess(executable,command,IntPtr.Zero,IntPtr.Zero,false,0x00080404,environment,directory,ref extended,out process),packageSid==IntPtr.Zero?"create terminal process":"create AppContainer terminal process");
                }finally{if(attributes!=IntPtr.Zero){DeleteProcThreadAttributeList(attributes);Marshal.FreeHGlobal(attributes);}if(capabilities!=IntPtr.Zero)Marshal.FreeHGlobal(capabilities);}
            }
            internal static void CreateWithHandles(string executable,StringBuilder command,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process,IntPtr packageSid)
            {
                IntPtr attributes=IntPtr.Zero,size=IntPtr.Zero,capabilities=IntPtr.Zero,handles=IntPtr.Zero;
                try{
                    int count=packageSid==IntPtr.Zero?1:2;InitializeProcThreadAttributeList(IntPtr.Zero,count,0,ref size);attributes=Marshal.AllocHGlobal(size);NativeCheck(InitializeProcThreadAttributeList(attributes,count,0,ref size),"initialize attributes");
                    if(packageSid!=IntPtr.Zero){var caps=new SecurityCapabilities{AppContainerSid=packageSid};capabilities=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SecurityCapabilities)));Marshal.StructureToPtr(caps,capabilities,false);NativeCheck(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20009),capabilities,new IntPtr(Marshal.SizeOf(typeof(SecurityCapabilities))),IntPtr.Zero,IntPtr.Zero),"set security capabilities");}
                    handles=Marshal.AllocHGlobal(IntPtr.Size*3);Marshal.WriteIntPtr(handles,0,startup.Input);Marshal.WriteIntPtr(handles,IntPtr.Size,startup.Output);Marshal.WriteIntPtr(handles,IntPtr.Size*2,startup.Error);NativeCheck(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20002),handles,new IntPtr(IntPtr.Size*3),IntPtr.Zero,IntPtr.Zero),"set inherited standard handles");
                    var extended=new StartupInfoEx{Startup=startup,Attributes=attributes};extended.Startup.Size=Marshal.SizeOf(typeof(StartupInfoEx));
                    NativeCheck(CreateSandboxProcess(executable,command,IntPtr.Zero,IntPtr.Zero,true,0x08080404,environment,directory,ref extended,out process),packageSid==IntPtr.Zero?"create command process":"create AppContainer process");
                }finally{if(attributes!=IntPtr.Zero){DeleteProcThreadAttributeList(attributes);Marshal.FreeHGlobal(attributes);}if(capabilities!=IntPtr.Zero)Marshal.FreeHGlobal(capabilities);if(handles!=IntPtr.Zero)Marshal.FreeHGlobal(handles);}
            }
            public void Dispose()
            {
                Exception failure=null;
                foreach(var grant in granted.AsReadOnly()){try{using(new AclMutationLock()){var dir=new DirectoryInfo(grant.Item1);if(!dir.Exists)continue;var acl=dir.GetAccessControl();acl.RemoveAccessRuleSpecific(grant.Item2);dir.SetAccessControl(acl);}}catch(Exception e){failure=e;}}
                granted.Clear();
                if(sid!=IntPtr.Zero){FreeSid(sid);sid=IntPtr.Zero;int hr=DeleteAppContainerProfile(name);if(hr<0)failure=new Win32Exception(hr);}
                if(TemporaryDirectory!=null){try{Directory.Delete(TemporaryDirectory,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
                if(failure!=null)throw new IOException("沙箱授权撤回失败，请检查目录权限。",failure);
            }
            [StructLayout(LayoutKind.Sequential)]struct SecurityCapabilities{internal IntPtr AppContainerSid,Capabilities;internal uint CapabilityCount,Reserved;}
            [StructLayout(LayoutKind.Sequential)]struct StartupInfoEx{internal StartupInfo Startup;internal IntPtr Attributes;}
            [DllImport("userenv.dll",CharSet=CharSet.Unicode)]static extern int CreateAppContainerProfile(string name,string display,string description,IntPtr capabilities,uint count,out IntPtr sid);
            [DllImport("userenv.dll",CharSet=CharSet.Unicode)]static extern int DeleteAppContainerProfile(string name);
            [DllImport("advapi32.dll")]static extern IntPtr FreeSid(IntPtr sid);
            [DllImport("kernel32.dll",SetLastError=true)]static extern bool InitializeProcThreadAttributeList(IntPtr attributes,int count,int flags,ref IntPtr size);
            [DllImport("kernel32.dll",SetLastError=true)]static extern bool UpdateProcThreadAttribute(IntPtr attributes,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
            [DllImport("kernel32.dll")]static extern void DeleteProcThreadAttributeList(IntPtr attributes);
            [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode,EntryPoint="CreateProcessW")]static extern bool CreateSandboxProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfoEx startup,out ProcessInfo process);
        }
    }
}
