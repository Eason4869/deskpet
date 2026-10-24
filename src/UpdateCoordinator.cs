using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed class UpdateCoordinator : IDisposable
    {
        readonly PetWindow pet;
        readonly IUpdateFeed feed;
        readonly Func<UpdateJob,Task> apply;
        readonly string installedVersion;
        readonly DispatcherTimer startup,readyTimer;
        CancellationTokenSource cancel;
        UpdateJob pending;
        IDisposable cacheLease;
        bool checking,applying,disposed,pendingManual,checkingManual,automaticStarted,retryAutomatic;
        DateTime? restartAt;bool immediate;UpdateChannel pendingChannel;
        public string Status {get;private set;}
        public string NetworkStatus {get;private set;}
        public double Progress {get;private set;}
        public bool IsBusy {get{return checking || pending!=null || applying;}}
        public bool CanCancel {get{return !applying && (checking || pending!=null);}}
        public bool IsDownloading {get;private set;}
        public bool IsApplying {get{return applying;}}
        public bool IsReadyToInstall {get{return pending!=null && !applying;}}
        public bool HasRestartCountdown {get{return restartAt.HasValue && !applying;}}
        public int RestartSeconds {get{return restartAt.HasValue?Math.Max(0,(int)Math.Ceiling((restartAt.Value-DateTime.UtcNow).TotalSeconds)):0;}}
        public event Action Changed;
        public UpdateCoordinator(PetWindow companion) : this(companion,new GitHubUpdates(),null,ReleaseInfo.DisplayVersion){}
        internal UpdateCoordinator(PetWindow companion,IUpdateFeed updateFeed,Func<UpdateJob,Task> applyUpdate,string version)
        {
            pet=companion;feed=updateFeed;apply=applyUpdate ?? StartWorkerAsync;installedVersion=version;Status=TextResources.Get("updateCoordinator.updateCoordinator.status");
            string resultPath=Path.Combine(pet.Store.DirectoryPath,"update-result.json");
            if(File.Exists(resultPath))try{var result=UpdateInstall.Read<UpdateResult>(resultPath);Status=result.Message??Status;}catch(Exception e){if(!(e is IOException || e is InvalidOperationException || e is System.Runtime.Serialization.SerializationException))throw;}
            startup=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(30)};
            startup.Tick+=async delegate
            {
                startup.Stop();if(!pet.Settings.AutoUpdate)return;
                if(IsBusy){ScheduleAutomatic(TimeSpan.FromMinutes(1));return;}
                var now=DateTime.UtcNow;long ticks=pet.Settings.LastUpdateCheckUtcTicks;
                if(!retryAutomatic && ticks>0 && ticks<=now.Ticks)
                {
                    var last=new DateTime(ticks,DateTimeKind.Utc);var remaining=TimeSpan.FromHours(4)-(now-last);
                    if(remaining>TimeSpan.Zero){ScheduleAutomatic(remaining);Status=TextResources.Format("updateCoordinator.updateCoordinator.statusLastToLocalTime",last.ToLocalTime().ToString("MM-dd HH:mm"));Notify();return;}
                }
                await CheckAndUpdateAsync(false);
            };
            readyTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};readyTimer.Tick+=ApplyWhenReady;
        }
        public void StartAutomaticChecks(){if(disposed)return;automaticStarted=true;if(pet.Settings.AutoUpdate)ScheduleAutomatic(TimeSpan.FromSeconds(30));}
        void ScheduleAutomatic(TimeSpan delay)
        {
            startup.Stop();if(disposed || !automaticStarted || !pet.Settings.AutoUpdate)return;
            startup.Interval=delay<TimeSpan.FromSeconds(1)?TimeSpan.FromSeconds(1):delay;startup.Start();
        }
        void SuccessfulCheck(){pet.Settings.LastUpdateCheckUtcTicks=DateTime.UtcNow.Ticks;pet.SaveSettings();}
        public void AutomaticChanged()
        {
            if(!pet.Settings.AutoUpdate && ((pending!=null && !pendingManual) || (checking && !checkingManual)))Cancel();
            if(pet.Settings.AutoUpdate){automaticStarted=true;ScheduleAutomatic(TimeSpan.FromSeconds(1));}else{startup.Stop();}
            pet.SaveSettings();Notify();
        }
        public async Task CheckAndUpdateAsync(bool manual)
        {
            if(disposed)return;
            if(manual && IsReadyToInstall)
            {
                await InstallPendingNowAsync();return;
            }
            if(IsBusy)return;checking=true;checkingManual=manual;cancel=new CancellationTokenSource();var token=cancel.Token;
            var channel=pet.Settings.UpdateChannel;bool succeeded=false;Progress=0;Status=TextResources.Get("updateCoordinator.checkAndUpdateAsync.status")+(channel==UpdateChannel.Stable?TextResources.Get("updateCoordinator.checkAndUpdateAsync.status.alternativeA"):TextResources.Get("updateCoordinator.checkAndUpdateAsync.status.alternativeB"))+TextResources.Get("updateCoordinator.checkAndUpdateAsync.status.alternativeC");Notify();
            try
            {
                bool useProxy=pet.Settings.UpdateUseProxy;string proxyAddress=pet.Settings.UpdateProxyAddress;
                NetworkStatus=useProxy?TextResources.Get("updateCoordinator.checkAndUpdateAsync.networkStatusUseProxy"):TextResources.Get("updateCoordinator.checkAndUpdateAsync.networkStatusUseProxy.alternativeA");
                var routing=feed as IUpdateDownloadRouting;if(routing!=null)routing.ConfigureMirror(pet.Settings.UpdateUseMirror,pet.Settings.UpdateMirrorAddress,new Progress<string>(value=>{if(!disposed && !token.IsCancellationRequested){NetworkStatus=value;Notify();}}));
                Notify();
                string directory=Path.GetDirectoryName(pet.Store.DirectoryPath);
                bool portable=!UpdateInstall.IsInstalledDirectory(directory);
                var release=await feed.CheckAsync(ReleaseInfo.RepositoryUrl,useProxy,proxyAddress,portable,channel,token);
                if(!release.IsNewerThan(installedVersion)){Status=channel==UpdateChannel.Stable&&ReleaseVersion.Parse(installedVersion).IsPreview?TextResources.Format("updateCoordinator.checkAndUpdateAsync.stableReleaseVersionParse",installedVersion):TextResources.Format("updateCoordinator.checkAndUpdateAsync.stableReleaseVersionParse.alternativeA",installedVersion);SuccessfulCheck();succeeded=true;return;}
                string version=release.VersionTag.TrimStart('v','V');
                if(!ReleaseInfo.WindowsUpdateAsset(release.AssetName,version,portable) || String.IsNullOrEmpty(release.Digest) || !release.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Format("updateCoordinator.checkAndUpdateAsync.template",release.VersionTag));
                string work=Path.Combine(pet.Store.DirectoryPath,"updates",Guid.NewGuid().ToString("N"));
                cacheLease=Maintenance.CacheResourceLease.Acquire(pet.Store.DirectoryPath,work);
                IsDownloading=true;Status=TextResources.Format("updateCoordinator.checkAndUpdateAsync.statusReleaseVersionTag",release.VersionTag);Notify();
                var reporter=new Progress<double>(value=>{if(!disposed && !token.IsCancellationRequested){Progress=value;Notify();}});
                string package=await feed.DownloadAsync(release,work,useProxy,proxyAddress,reporter,token);
                token.ThrowIfCancellationRequested();if(disposed)return;if(channel!=pet.Settings.UpdateChannel)throw new OperationCanceledException();
                using(var process=Process.GetCurrentProcess())pending=new UpdateJob{InstallationDirectory=directory,WorkDirectory=work,PackagePath=package,Mode=portable?"portable":"installed",InstalledVersion=installedVersion,TargetVersion=version,Digest=release.Digest.Substring(7),Size=release.Size,AssetUrl=release.AssetUrl,ParentPid=process.Id,ParentStartedUtcTicks=process.StartTime.ToUniversalTime().Ticks};
                if(release.CommitSha!=null){
                    var identity=ReleaseIdentity.Read(Path.Combine(directory,UpdateInstall.ExecutableName));pending.ProtocolVersion=2;
                    pending.InstalledAssemblyVersion=identity.AssemblyVersion.ToString();pending.InstalledBuildId=identity.CommitSha==null?null:identity.InformationalVersion;
                    pending.TargetAssemblyVersion=release.TargetAssemblyVersion;pending.TargetBuildId=version+"+"+release.CommitSha;
                }
                pendingChannel=channel;immediate=false;UpdateInstall.Validate(pending);pendingManual=manual;Status=TextResources.Get("updateCoordinator.checkAndUpdateAsync.status.alternativeD");SuccessfulCheck();succeeded=true;readyTimer.Start();
            }
            catch(Exception e)
            {
                pending=null;Status=e is OperationCanceledException?TextResources.Get("updateCoordinator.checkAndUpdateAsync.invalidOperationExceptionEMessage"):e is ApiException || e is InvalidOperationException?e.Message:TextResources.Get("updateCoordinator.checkAndUpdateAsync.invalidOperationExceptionEMessage.alternativeA");
            }
            finally{checking=false;IsDownloading=false;cancel.Dispose();cancel=null;if(pending==null)ReleaseCache();retryAutomatic=!succeeded;ScheduleAutomatic(succeeded?TimeSpan.FromHours(4):TimeSpan.FromMinutes(10));Notify();}
        }
        public void ChangeChannel(UpdateChannel channel)
        {
            if(IsBusy||!Enum.IsDefined(typeof(UpdateChannel),channel))return;
            pet.Settings.UpdateChannel=channel;pet.Settings.LastUpdateCheckUtcTicks=0;pet.SaveSettings();Status=TextResources.Get("updateCoordinator.changeChannel.status")+(channel==UpdateChannel.Stable?TextResources.Get("updateCoordinator.changeChannel.status.alternativeA"):TextResources.Get("updateCoordinator.changeChannel.status.alternativeB"))+TextResources.Get("updateCoordinator.changeChannel.status.alternativeC");Notify();
        }
        public Task InstallPendingNowAsync()
        {
            if(!IsReadyToInstall)return Task.FromResult(true);pendingManual=true;immediate=true;restartAt=null;return TryApplyPendingAsync();
        }
        async void ApplyWhenReady(object sender,EventArgs args){await TryApplyPendingAsync();}
        async Task TryApplyPendingAsync()
        {
            if(disposed || pending==null || applying)return;
            if(pendingChannel!=pet.Settings.UpdateChannel){Cancel();return;}
            if(!pet.CanApplyUpdate(pendingManual))
            {restartAt=null;Status=pet.HasHistorySaveWarning?pet.PendingSaveWarning:pendingManual?TextResources.Get("updateCoordinator.tryApplyPendingAsync.petPendingSaveWarningPendingManual"):TextResources.Get("updateCoordinator.tryApplyPendingAsync.petPendingSaveWarningPendingManual.alternativeA");Notify();return;}
            if(!restartAt.HasValue)restartAt=DateTime.UtcNow.AddSeconds(immediate?0:5);
            int seconds=Math.Max(0,(int)Math.Ceiling((restartAt.Value-DateTime.UtcNow).TotalSeconds));
            Status=TextResources.Format("updateCoordinator.tryApplyPendingAsync.statusSeconds",seconds);Notify();if(seconds>0)return;
            applying=true;readyTimer.Stop();Status=TextResources.Get("updateCoordinator.tryApplyPendingAsync.status");Notify();
            var job=pending;
            try
            {
                await pet.PrepareUpdateAsync();
                if(disposed || !pet.CanApplyUpdate(pendingManual))throw new InvalidOperationException(TextResources.Get("updateCoordinator.tryApplyPendingAsync.text"));
                await apply(job);
                if(disposed || !pet.CanApplyUpdate(pendingManual))
                {
                    Abort(job);if(!disposed){applying=false;pending=null;ReleaseCache();restartAt=null;Status=TextResources.Get("updateCoordinator.tryApplyPendingAsync.status.alternativeA");Notify();}return;
                }
                pet.Close();
            }
            catch(Exception e){Abort(job);applying=false;pending=null;ReleaseCache();restartAt=null;Status=e is InvalidOperationException?e.Message:TextResources.Get("updateCoordinator.tryApplyPendingAsync.invalidOperationExceptionEMessage");Notify();}
        }
        async Task StartWorkerAsync(UpdateJob job)
        {
            bool acknowledged=false;
            try
            {
            UpdateInstall.Validate(job);string source=Path.Combine(job.InstallationDirectory,UpdateInstall.WorkerName);
            if(!File.Exists(source))throw new InvalidOperationException(TextResources.Get("updateCoordinator.startWorkerAsync.text"));
            UpdateInstall.CheckWorker(job,source);
            string helper=Path.Combine(job.WorkDirectory,UpdateInstall.WorkerName);File.Copy(source,helper,false);
            if(File.Exists(source+".config"))File.Copy(source+".config",helper+".config",false);
            string path=Path.Combine(job.WorkDirectory,"job.json");UpdateInstall.Write(path,job);
            using(var worker=Process.Start(new ProcessStartInfo(helper,UpdateInstall.Quote(path)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=job.WorkDirectory}))
            {
                var deadline=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<deadline)
                {if(File.Exists(Path.Combine(job.WorkDirectory,"ready"))){acknowledged=true;return;}if(worker.HasExited)throw new InvalidOperationException(TextResources.Get("updateCoordinator.startWorkerAsync.text.alternativeA"));await Task.Delay(80);}
                throw new InvalidOperationException(TextResources.Get("updateCoordinator.startWorkerAsync.text.alternativeB"));
            }
            }
            finally
            {
                if(!acknowledged)Abort(job);
            }
        }
        public void Cancel()
        {
            if(applying)return;if(cancel!=null)cancel.Cancel();pending=null;if(!checking)ReleaseCache();restartAt=null;immediate=false;readyTimer.Stop();Status=TextResources.Get("updateCoordinator.cancel.status");Notify();
        }
        public void ReportValidation(string text){Status=text;Notify();}
        static void Abort(UpdateJob job){try{File.WriteAllText(Path.Combine(job.WorkDirectory,"abort"),"aborted");}catch(IOException){}catch(UnauthorizedAccessException){}}
        void Notify(){if(disposed)return;if(!pet.Dispatcher.CheckAccess()){pet.Dispatcher.BeginInvoke(new Action(Notify));return;}pet.UpdateStateChanged(this);if(Changed!=null)Changed();}
        void ReleaseCache(){var lease=cacheLease;cacheLease=null;if(lease!=null)lease.Dispose();}
        public void Dispose(){if(disposed)return;disposed=true;startup.Stop();readyTimer.Stop();if(cancel!=null)cancel.Cancel();if(!checking)ReleaseCache();feed.Dispose();Changed=null;}
    }
}
