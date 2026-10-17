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
        bool checking,applying,disposed,pendingManual,checkingManual,automaticStarted,retryAutomatic;
        DateTime? restartAt;
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
            pet=companion;feed=updateFeed;apply=applyUpdate ?? StartWorkerAsync;installedVersion=version;Status="启动后自动检查更新，也可手动检查。更新会保留设置、密钥和导入的外观。";
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
                    if(remaining>TimeSpan.Zero){ScheduleAutomatic(remaining);Status="上次成功检查："+last.ToLocalTime().ToString("MM-dd HH:mm")+"，已安排下次自动检查。";Notify();return;}
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
                pendingManual=true;restartAt=null;Status="新版已就绪，空闲后将自动退出、安装并重启。";readyTimer.Start();Notify();return;
            }
            if(IsBusy)return;checking=true;checkingManual=manual;cancel=new CancellationTokenSource();var token=cancel.Token;
            bool succeeded=false;Progress=0;Status="正在检查 GitHub 正式版本…";Notify();
            try
            {
                bool useProxy=pet.Settings.UpdateUseProxy;string proxyAddress=pet.Settings.UpdateProxyAddress;
                NetworkStatus=useProxy?"版本检查：GitHub · 网络代理":"版本检查：GitHub 直连";
                var routing=feed as IUpdateDownloadRouting;if(routing!=null)routing.ConfigureMirror(pet.Settings.UpdateUseMirror,pet.Settings.UpdateMirrorAddress,new Progress<string>(value=>{if(!disposed && !token.IsCancellationRequested){NetworkStatus=value;Notify();}}));
                Notify();
                string directory=Path.GetDirectoryName(pet.Store.DirectoryPath);
                bool portable=!UpdateInstall.IsInstalledDirectory(directory);
                var release=await feed.CheckAsync(ReleaseInfo.RepositoryUrl,useProxy,proxyAddress,portable,token);
                if(!release.IsNewerThan(new Version(installedVersion))){Status="当前已是最新版本 "+installedVersion+"。";SuccessfulCheck();succeeded=true;return;}
                string version=release.VersionTag.TrimStart('v','V');
                if(!ReleaseInfo.WindowsUpdateAsset(release.AssetName,version,portable) || String.IsNullOrEmpty(release.Digest) || !release.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("发现 "+release.VersionTag+"，但没有可验证的自动更新包。请到 GitHub Release 查看。");
                string work=Path.Combine(pet.Store.DirectoryPath,"updates",Guid.NewGuid().ToString("N"));
                IsDownloading=true;Status="正在下载 "+release.VersionTag+"…";Notify();
                var reporter=new Progress<double>(value=>{if(!disposed && !token.IsCancellationRequested){Progress=value;Notify();}});
                string package=await feed.DownloadAsync(release,work,useProxy,proxyAddress,reporter,token);
                token.ThrowIfCancellationRequested();if(disposed)return;
                using(var process=Process.GetCurrentProcess())pending=new UpdateJob{InstallationDirectory=directory,WorkDirectory=work,PackagePath=package,Mode=portable?"portable":"installed",InstalledVersion=installedVersion,TargetVersion=version,Digest=release.Digest.Substring(7),Size=release.Size,AssetUrl=release.AssetUrl,ParentPid=process.Id,ParentStartedUtcTicks=process.StartTime.ToUniversalTime().Ticks};
                UpdateInstall.Validate(pending);pendingManual=manual;Status="下载与校验完成，等待空闲后自动更新。";SuccessfulCheck();succeeded=true;readyTimer.Start();
            }
            catch(Exception e)
            {
                pending=null;Status=e is OperationCanceledException?"更新已取消，当前版本继续运行。":e is ApiException || e is InvalidOperationException?e.Message:"自动更新未完成，请检查网络、代理和目录权限。";
            }
            finally{checking=false;IsDownloading=false;cancel.Dispose();cancel=null;retryAutomatic=!succeeded;ScheduleAutomatic(succeeded?TimeSpan.FromHours(4):TimeSpan.FromMinutes(10));Notify();}
        }
        async void ApplyWhenReady(object sender,EventArgs args)
        {
            if(disposed || pending==null || applying)return;
            if(!pet.CanApplyUpdate(pendingManual))
            {restartAt=null;Status=pet.HasHistorySaveWarning?pet.PendingSaveWarning:pendingManual?"新版已就绪。请保存设置草稿、发送或清空输入，并等待对话或语音结束；空闲后会自动退出、安装并重启。":"新版已就绪，等待对话、设置和桌面操作空闲后自动更新。";Notify();return;}
            if(!restartAt.HasValue)restartAt=DateTime.UtcNow.AddSeconds(5);
            int seconds=Math.Max(0,(int)Math.Ceiling((restartAt.Value-DateTime.UtcNow).TotalSeconds));
            Status="新版已就绪，"+seconds+" 秒后自动更新并重启。可点击“取消更新”。";Notify();if(seconds>0)return;
            applying=true;readyTimer.Stop();Status="正在退出并安装更新…";Notify();
            var job=pending;
            try
            {
                await pet.PrepareUpdateAsync();
                if(disposed || !pet.CanApplyUpdate(pendingManual))throw new InvalidOperationException("活动状态已变化，本次更新交接已停止，当前版本继续运行。");
                await apply(job);
                if(disposed || !pet.CanApplyUpdate(pendingManual))
                {
                    Abort(job);if(!disposed){applying=false;pending=null;restartAt=null;Status="活动状态已变化，本次更新交接已停止，当前版本继续运行。";Notify();}return;
                }
                pet.Close();
            }
            catch(Exception e){Abort(job);applying=false;pending=null;restartAt=null;Status=e is InvalidOperationException?e.Message:"更新助手无法启动，当前版本继续运行。";Notify();}
        }
        async Task StartWorkerAsync(UpdateJob job)
        {
            bool acknowledged=false;
            try
            {
            UpdateInstall.Validate(job);string source=Path.Combine(job.InstallationDirectory,UpdateInstall.WorkerName);
            if(!File.Exists(source))throw new InvalidOperationException("找不到更新助手，请完整解压运行包或重新安装伙伴。");
            string helper=Path.Combine(job.WorkDirectory,UpdateInstall.WorkerName);File.Copy(source,helper,false);
            if(File.Exists(source+".config"))File.Copy(source+".config",helper+".config",false);
            string path=Path.Combine(job.WorkDirectory,"job.json");UpdateInstall.Write(path,job);
            using(var worker=Process.Start(new ProcessStartInfo(helper,UpdateInstall.Quote(path)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=job.WorkDirectory}))
            {
                var deadline=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<deadline)
                {if(File.Exists(Path.Combine(job.WorkDirectory,"ready"))){acknowledged=true;return;}if(worker.HasExited)throw new InvalidOperationException("更新助手拒绝了此任务，当前版本继续运行。");await Task.Delay(80);}
                throw new InvalidOperationException("更新助手未能准备好，当前版本继续运行。");
            }
            }
            finally
            {
                if(!acknowledged)Abort(job);
            }
        }
        public void Cancel()
        {
            if(applying)return;if(cancel!=null)cancel.Cancel();pending=null;restartAt=null;readyTimer.Stop();Status="更新已取消，当前版本继续运行。";Notify();
        }
        public void ReportValidation(string text){Status=text;Notify();}
        static void Abort(UpdateJob job){try{File.WriteAllText(Path.Combine(job.WorkDirectory,"abort"),"aborted");}catch(IOException){}catch(UnauthorizedAccessException){}}
        void Notify(){if(disposed)return;pet.UpdateStateChanged(this);if(Changed!=null)Changed();}
        public void Dispose(){if(disposed)return;disposed=true;startup.Stop();readyTimer.Stop();if(cancel!=null)cancel.Cancel();feed.Dispose();Changed=null;}
    }
}
