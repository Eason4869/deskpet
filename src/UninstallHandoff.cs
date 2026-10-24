using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    [DataContract]
    public sealed class UninstallJob
    {
        [DataMember] public int ProtocolVersion=1,ParentPid;
        [DataMember] public long ParentStartedUtcTicks;
        [DataMember] public string InstallationDirectory,WorkDirectory,UninstallerPath,UninstallerDigest,UninstallerDataDigest;
        [DataMember] public string InstalledVersion,InstalledAssemblyVersion,InstalledBuildId;
    }

    // The host owns the reversible save/maintenance transaction. Preparing this
    // ticket does not close the host or authorize removal. Commit ONLY from the
    // actual Closed notification, after every owning drain has succeeded.
    public sealed class UninstallTicket:IDisposable
    {
        readonly UninstallJob job;
        readonly Process worker;
        readonly object sync=new object();
        bool committed,disposed,aborted;
        internal UninstallTicket(UninstallJob value,Process process){job=value;worker=process;}
        public string WorkDirectory {get{return job.WorkDirectory;}}
        public void Commit()
        {
            lock(sync)
            {
                if(disposed)throw new ObjectDisposedException("UninstallTicket");
                if(committed)return;
                if(aborted)throw UninstallHandoff.Failure();
                try
                {
                    if(worker.HasExited)throw UninstallHandoff.Failure();
                    UninstallHandoff.CheckRecordPath(Path.Combine(job.WorkDirectory,"commit"));
                    // CreateNew prevents accidentally authorizing a reused job.
                    string stage=Path.Combine(job.WorkDirectory,"commit-stage");
                    UninstallHandoff.CheckRecordPath(stage);
                    using(var file=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                    {file.WriteByte(1);file.Flush(true);}
                    File.Move(stage,Path.Combine(job.WorkDirectory,"commit"));
                    committed=true;
                }
                catch
                {
                    // Closed cannot be undone. A failed durable commit is never
                    // success: helper sees abort/missing commit and retains app.
                    aborted=true;UninstallHandoff.Abort(job);
                    UninstallHandoff.Result(job,false,TextResources.Get("uninstall.handoffFailed"));
                    throw;
                }
            }
        }
        public void Abort(){lock(sync){if(!committed&&!disposed){aborted=true;UninstallHandoff.Abort(job);}}}
        public void Dispose(){lock(sync){if(disposed)return;if(!committed)UninstallHandoff.Abort(job);disposed=true;worker.Dispose();}}
    }

    public static class UninstallHandoff
    {
        internal static InvalidOperationException Failure(){return new InvalidOperationException(TextResources.Get("uninstall.handoffFailed"));}
        static bool Same(string left,string right){return String.Equals(Path.GetFullPath(left).TrimEnd('\\','/'),Path.GetFullPath(right).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);}
        static void NoLinks(string path)
        {
            for(string current=Path.GetFullPath(path);!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw Failure();
        }
        internal static void CheckRecordPath(string path){NoLinks(path);NoLinks(path+".tmp");}
        static void Identity(UninstallJob job,string path)
        {ReleaseIdentity.Read(path).Require(job.InstalledVersion,job.InstalledAssemblyVersion,job.InstalledBuildId);}
        static void Validate(UninstallJob job)
        {
            if(job==null||job.ProtocolVersion!=1||job.ParentPid<=0||job.ParentStartedUtcTicks<=0)throw Failure();
            string root=Path.GetFullPath(job.InstallationDirectory),work=Path.GetFullPath(job.WorkDirectory);
            if(Same(root,Path.GetPathRoot(root))||!Same(Path.GetDirectoryName(work),Path.Combine(root,"data","uninstall"))||
                !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(work),"\\A[a-f0-9]{32}\\z"))throw Failure();
            NoLinks(work);NoLinks(Path.Combine(root,UpdateInstall.ExecutableName));NoLinks(Path.Combine(root,UpdateInstall.WorkerName));
            string registered=UpdateInstall.GetUninstallerPath(root);
            if(registered==null||!Same(registered,job.UninstallerPath))throw Failure();
            NoLinks(registered);NoLinks(Path.ChangeExtension(registered,".dat"));
            if(UpdateInstall.Hash(registered)!=job.UninstallerDigest||UpdateInstall.Hash(Path.ChangeExtension(registered,".dat"))!=job.UninstallerDataDigest)throw Failure();
            Identity(job,Path.Combine(root,UpdateInstall.ExecutableName));Identity(job,Path.Combine(root,UpdateInstall.WorkerName));
        }
        public static Task<UninstallTicket> PrepareAsync(string installationDirectory)
        {
            return Task.Run(async delegate
            {
                var job=new UninstallJob{InstallationDirectory=Path.GetFullPath(installationDirectory)};
                string registered=UpdateInstall.GetUninstallerPath(job.InstallationDirectory);
                if(registered==null)throw new InvalidOperationException(TextResources.Get("aboutPanel.uninstall.updatesReportValidation.alternativeB"));
                var identity=ReleaseIdentity.Read(Path.Combine(job.InstallationDirectory,UpdateInstall.ExecutableName));
                job.InstalledVersion=identity.Version.ToString();job.InstalledAssemblyVersion=identity.AssemblyVersion.ToString();job.InstalledBuildId=identity.InformationalVersion;
                job.WorkDirectory=Path.Combine(job.InstallationDirectory,"data","uninstall",Guid.NewGuid().ToString("N"));
                job.UninstallerPath=registered;job.UninstallerDigest=UpdateInstall.Hash(registered);job.UninstallerDataDigest=UpdateInstall.Hash(Path.ChangeExtension(registered,".dat"));
                using(var parent=Process.GetCurrentProcess())
                {
                    if(!Same(parent.MainModule.FileName,Path.Combine(job.InstallationDirectory,UpdateInstall.ExecutableName)))throw Failure();
                    job.ParentPid=parent.Id;job.ParentStartedUtcTicks=parent.StartTime.ToUniversalTime().Ticks;
                }
                Process worker=null;bool transferred=false;
                try
                {
                    Validate(job);Directory.CreateDirectory(job.WorkDirectory);Validate(job);
                    string source=Path.Combine(job.InstallationDirectory,UpdateInstall.WorkerName),helper=Path.Combine(job.WorkDirectory,UpdateInstall.WorkerName);
                    File.Copy(source,helper,false);if(File.Exists(source+".config"))File.Copy(source+".config",helper+".config",false);
                    string path=Path.Combine(job.WorkDirectory,"uninstall.json");CheckRecordPath(path);UpdateInstall.Write(path,job);
                    worker=Process.Start(new ProcessStartInfo(helper,"--uninstall "+UpdateInstall.Quote(path)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=job.WorkDirectory});
                    var deadline=DateTime.UtcNow.AddSeconds(15);
                    while(DateTime.UtcNow<deadline)
                    {
                        if(worker.HasExited)throw Failure();
                        if(File.Exists(Path.Combine(job.WorkDirectory,"ready"))){transferred=true;return new UninstallTicket(job,worker);}
                        await Task.Delay(80).ConfigureAwait(false);
                    }
                    throw Failure();
                }
                finally{if(!transferred){Abort(job);if(worker!=null)worker.Dispose();}}
            });
        }
        internal static void Abort(UninstallJob job)
        {try{string path=Path.Combine(job.WorkDirectory,"abort");CheckRecordPath(path);File.WriteAllText(path,"abort");}catch(Exception){}}
        static void CheckAbort(UninstallJob job)
        {if(File.Exists(Path.Combine(job.WorkDirectory,"abort"))||Directory.Exists(Path.Combine(job.WorkDirectory,"abort")))throw Failure();}
        internal static void Result(UninstallJob job,bool success,string message)
        {
            var result=new UpdateResult{Success=success,Version=job.InstalledVersion,Message=message};
            try{string path=Path.Combine(job.WorkDirectory,"result.json");CheckRecordPath(path);UpdateInstall.Write(path,result);}catch(Exception){}
            try{string path=Path.Combine(job.InstallationDirectory,"data","uninstall-result.json");CheckRecordPath(path);UpdateInstall.Write(path,result);}catch(Exception){}
        }
        internal static int RunWorker(string path)
        {
            UninstallJob job=null;bool trusted=false;
            try
            {
                job=UpdateInstall.Read<UninstallJob>(path);Validate(job);
                if(!Same(path,Path.Combine(job.WorkDirectory,"uninstall.json")))throw Failure();
                string executable=System.Reflection.Assembly.GetExecutingAssembly().Location;
                if(!Same(executable,Path.Combine(job.WorkDirectory,UpdateInstall.WorkerName)))throw Failure();
                NoLinks(executable);Identity(job,executable);
                if(UpdateInstall.Hash(executable)!=UpdateInstall.Hash(Path.Combine(job.InstallationDirectory,UpdateInstall.WorkerName)))throw Failure();
                TextResources.ConfigureStartupLanguage(UiLanguages.ReadSavedLanguage(Path.Combine(job.InstallationDirectory,"data","settings.json")));
                CheckAbort(job);
                // Unlike an update recovery, an absent parent never establishes
                // uninstall authority. Retain this exact Process handle throughout.
                using(var parent=Process.GetProcessById(job.ParentPid))
                {
                    if(parent.StartTime.ToUniversalTime().Ticks!=job.ParentStartedUtcTicks||!Same(parent.MainModule.FileName,Path.Combine(job.InstallationDirectory,UpdateInstall.ExecutableName))||parent.HasExited)throw Failure();
                    trusted=true;string ready=Path.Combine(job.WorkDirectory,"ready");CheckRecordPath(ready);File.WriteAllText(ready,"ready");
                    var deadline=DateTime.UtcNow.AddMinutes(2);
                    while(!parent.WaitForExit(80)){CheckAbort(job);if(DateTime.UtcNow>=deadline)throw Failure();}
                }
                CheckAbort(job);
                string commit=Path.Combine(job.WorkDirectory,"commit");NoLinks(commit);
                if(!File.Exists(commit)||!System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(commit),new byte[]{1}))throw Failure();
                WaitForInstallationToDisappear(job);
                CheckAbort(job);Validate(job);
                string uninstallLog=Path.Combine(job.WorkDirectory,"inno-uninstall.log");CheckRecordPath(uninstallLog);
                if(File.Exists(uninstallLog)||Directory.Exists(uninstallLog))throw Failure();
                // Inno suppresses the second startup confirmation with SILENT.
                // Never force-close apps, request a reboot, or delete data here.
                using(var uninstaller=Process.Start(new ProcessStartInfo(job.UninstallerPath,"/SILENT /NORESTART /LOG="+UpdateInstall.Quote(uninstallLog)){UseShellExecute=false,WorkingDirectory=job.InstallationDirectory}))
                {uninstaller.WaitForExit();if(uninstaller.ExitCode!=0)throw Failure();}
                if(UpdateInstall.GetUninstallerPath(job.InstallationDirectory)!=null||File.Exists(Path.Combine(job.InstallationDirectory,UpdateInstall.ExecutableName)))throw Failure();
                Result(job,true,"");return 0;
            }
            catch(Exception error)
            {
                if(trusted){try{string log=Path.Combine(job.WorkDirectory,"error.log");CheckRecordPath(log);File.WriteAllText(log,error.ToString());}catch(Exception){}Result(job,false,error.Message);}
                return 1;
            }
        }
        static void WaitForInstallationToDisappear(UninstallJob job)
        {
            var deadline=DateTime.UtcNow.AddMinutes(2);
            while(true)
            {
                CheckAbort(job);
                // Program creates Mutex(false) and never owns it. WaitOne would
                // succeed while the app is alive. Dispose EVERY successful probe:
                // retaining it would itself prevent named-object disappearance.
                try{using(Mutex.OpenExisting(ReleaseInfo.InstallationMutex)){}}
                catch(WaitHandleCannotBeOpenedException){return;}
                // Access denied and unexpected errors propagate: not proof of exit.
                if(DateTime.UtcNow>=deadline)throw Failure();Thread.Sleep(80);
            }
        }
    }
}
