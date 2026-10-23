using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class OfficialCompanionDownloads : IDisposable
    {
        readonly object gate=new object();readonly OfficialCompanionStorage store;readonly OfficialCompanionFeed feed;
        readonly object publicationGate=new object();readonly Dictionary<string,object> characterGates=OfficialCompanionRules.Ids.ToDictionary(x=>x,x=>new object());
        readonly GitHubUpdates transport;readonly Func<OfficialCompanionInstallation,CancellationToken,Task> validateNative;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();readonly SemaphoreSlim slots=new SemaphoreSlim(2,2);
        readonly Dictionary<string,Work<OfficialCompanionFeed.Snapshot>> refreshes=new Dictionary<string,Work<OfficialCompanionFeed.Snapshot>>();
        readonly Dictionary<string,Cache> cache=new Dictionary<string,Cache>();
        readonly Dictionary<string,Work<InstallResult>> installs=new Dictionary<string,Work<InstallResult>>();
        readonly HashSet<string> nativeValidated=new HashSet<string>(StringComparer.Ordinal),upgradeChecked=new HashSet<string>(StringComparer.Ordinal);
        readonly string appVersion;bool disposed;long selection;
        internal Func<DateTime> UtcNow=()=>DateTime.UtcNow;
        internal OfficialCompanionStorage Storage {get{return store;}}
        sealed class Cache {internal OfficialCompanionFeed.Snapshot Snapshot;internal DateTime Expires;}
        sealed class Metadata {internal OfficialCompanionFeed.Snapshot Snapshot;internal bool Checked;}
        sealed class InstallResult {internal OfficialCompanionInstallation Installation;internal bool Installed;}
        sealed class Work<T>
        {
            internal CancellationTokenSource Cancel;internal Task<T> Task;internal readonly Dictionary<long,IProgress<OfficialCompanionProgress>> Listeners=new Dictionary<long,IProgress<OfficialCompanionProgress>>();internal long Next;
            internal void Report(OfficialCompanionProgress progress,object sync){IProgress<OfficialCompanionProgress>[] listeners;lock(sync)listeners=Listeners.Values.Where(x=>x!=null).ToArray();foreach(var listener in listeners)try{listener.Report(progress);}catch{} }
        }
        public OfficialCompanionDownloads(string dataRoot,string appVersion,UpdateChannel channel,GitHubUpdates transport,Func<OfficialCompanionInstallation,CancellationToken,Task> validateNative)
        {
            if(transport==null)throw new ArgumentNullException("transport");if(validateNative==null)throw new ArgumentNullException("validateNative");
            OfficialCompanionRules.Version(appVersion);if(channel!=UpdateChannel.Stable&&channel!=UpdateChannel.Preview)throw new ArgumentException(TextResources.Get("officialCompanionDownloads.officialCompanionDownloads.text"));
            this.appVersion=appVersion;this.transport=transport;this.validateNative=validateNative;store=new OfficialCompanionStorage(dataRoot);feed=new OfficialCompanionFeed(transport,appVersion,channel,store.Root);
        }
        void Alive(){if(disposed)throw new ObjectDisposedException("OfficialCompanionDownloads");}
        void CheckAlive(){lock(gate){Alive();lifetime.Token.ThrowIfCancellationRequested();}}
        bool Compatible(OfficialCompanionInstallation installation){return installation!=null&&ReleaseVersion.Parse(installation.Asset.MinimumAppVersion).CompareTo(ReleaseVersion.Parse(appVersion))<=0;}
        OfficialCompanionInstallation Usable(string id){var installed=store.Installed(id);return Compatible(installed)?installed:null;}
        public OfficialCompanionInstallation Installed(string id){lock(gate)Alive();return Usable(id);}
        public long BeginSelection(){lock(gate){Alive();return ++selection;}}
        public bool OwnsSelection(long token){lock(gate)return !disposed&&token==selection;}
        public Task<OfficialCompanionResult> EnsureAsync(string id,OfficialDownloadRouting routing,OfficialCompanionCheckMode mode,IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            OfficialCompanionRules.Id(id);if(routing==null)throw new ArgumentNullException("routing");if(mode<OfficialCompanionCheckMode.Cached||mode>OfficialCompanionCheckMode.AfterApplicationUpdate)throw new ArgumentException(TextResources.Get("officialCompanionDownloads.ensureAsync.text"));CheckAlive();
            return Task.Run(async()=>{try{return await EnsureCoreAsync(id,routing,mode,progress,cancel).ConfigureAwait(false);}catch(OperationCanceledException){throw;}catch(Exception){cancel.ThrowIfCancellationRequested();lifetime.Token.ThrowIfCancellationRequested();throw new ApiException(id+"："+OfficialCompanionRules.SafeMessage);}},cancel);
        }
        async Task<OfficialCompanionResult> EnsureCoreAsync(string id,OfficialDownloadRouting routing,OfficialCompanionCheckMode mode,IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(cancel,lifetime.Token)) {
                var token=linked.Token;Metadata metadata=null;bool failed=false;
                try{metadata=await MetadataAsync(routing,mode,progress,token).ConfigureAwait(false);}
                catch(OperationCanceledException){throw;}
                catch(Exception error){token.ThrowIfCancellationRequested();if(!Expected(error))throw;failed=true;}
                if(failed){var old=Usable(id);if(old==null)throw new ApiException(id+"："+OfficialCompanionRules.SafeMessage);var fallback=await InstallAsync(old.Asset,routing,progress,token).ConfigureAwait(false);return new OfficialCompanionResult(fallback.Installation,true,false,false,OfficialCompanionRules.SafeMessage);}
                OfficialCompanionAsset asset;if(!metadata.Snapshot.Assets.TryGetValue(id,out asset)) {
                    var old=Usable(id);if(old!=null){var retained=await InstallAsync(old.Asset,routing,progress,token).ConfigureAwait(false);return new OfficialCompanionResult(retained.Installation,true,metadata.Checked,false,TextResources.Get("officialCompanionDownloads.ensureCoreAsync.text"));}
                    throw new ApiException(TextResources.Format("officialCompanionDownloads.ensureCoreAsync.template",id));
                }
                var installed=await InstallAsync(asset,routing,progress,token).ConfigureAwait(false);
                return new OfficialCompanionResult(installed.Installation,false,metadata.Checked,installed.Installed,null);
            }
        }
        async Task<Metadata> MetadataAsync(OfficialDownloadRouting routing,OfficialCompanionCheckMode mode,IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            string key=routing.Key;Work<OfficialCompanionFeed.Snapshot> work;long lease;
            lock(gate) {
                Alive();cancel.ThrowIfCancellationRequested();Cache entry;bool upgrade=mode==OfficialCompanionCheckMode.AfterApplicationUpdate;
                if(cache.TryGetValue(key,out entry)&&((mode==OfficialCompanionCheckMode.Cached&&entry.Expires>UtcNow())||(upgrade&&upgradeChecked.Contains(key))))return new Metadata{Snapshot=entry.Snapshot,Checked=false};
                if(!refreshes.TryGetValue(key,out work)) {
                    work=new Work<OfficialCompanionFeed.Snapshot>{Cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)};refreshes.Add(key,work);var captured=work;
                    work.Task=Task.Run(async()=> {
                        try {
                            var snapshot=await feed.RefreshAsync(routing,new OfficialProgress<OfficialCompanionProgress>(p=>captured.Report(p,gate)),captured.Cancel.Token).ConfigureAwait(false);
                            lock(gate){Alive();captured.Cancel.Token.ThrowIfCancellationRequested();cache[key]=new Cache{Snapshot=snapshot,Expires=UtcNow().AddMinutes(5)};}
                            return snapshot;
                        }finally{lock(gate){Work<OfficialCompanionFeed.Snapshot> current;if(refreshes.TryGetValue(key,out current)&&ReferenceEquals(current,captured))refreshes.Remove(key);}}
                    });Observe(work.Task);DisposeWhenUnleased(work);
                }
                lease=++work.Next;work.Listeners.Add(lease,progress);
            }
            var result=await LeaseAsync(work,lease,cancel,()=>{Work<OfficialCompanionFeed.Snapshot> current;if(refreshes.TryGetValue(key,out current)&&ReferenceEquals(current,work))refreshes.Remove(key);}).ConfigureAwait(false);
            if(mode==OfficialCompanionCheckMode.AfterApplicationUpdate) {
                lock(gate){Alive();cancel.ThrowIfCancellationRequested();upgradeChecked.Add(key);}
                string marker=OfficialCompanionStorage.SafePath(store.Root,"last-checked-app-version.txt"),temporary=OfficialCompanionStorage.SafePath(store.Root,".checked-"+Guid.NewGuid().ToString("N"));
                try{System.IO.File.WriteAllText(temporary,appVersion);store.Commit(temporary,marker);}catch{lock(gate)upgradeChecked.Remove(key);throw;}finally{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}
            }
            return new Metadata{Snapshot=result,Checked=true};
        }
        static bool Expected(Exception error){return error is System.IO.IOException||error is System.IO.InvalidDataException||error is ApiException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is OverflowException||error is FormatException||error is NotSupportedException||error is System.Net.Http.HttpRequestException||error is System.Net.WebException;}
        static void Observe(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
        async Task<T> LeaseAsync<T>(Work<T> work,long lease,CancellationToken cancel,Action remove)
        {
            try {
                var signal=new TaskCompletionSource<bool>();using(cancel.Register(()=>signal.TrySetResult(true))) {
                    if(await Task.WhenAny(work.Task,signal.Task).ConfigureAwait(false)!=work.Task)throw new OperationCanceledException(cancel);
                    cancel.ThrowIfCancellationRequested();return await work.Task.ConfigureAwait(false);
                }
            }finally {lock(gate){work.Listeners.Remove(lease);if(work.Listeners.Count==0){if(!work.Task.IsCompleted){remove();work.Cancel.Cancel();}else work.Cancel.Dispose();}}}
        }
        void DisposeWhenUnleased<T>(Work<T> work){work.Task.ContinueWith(t=>{lock(gate)if(work.Listeners.Count==0)work.Cancel.Dispose();},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
        Task<InstallResult> InstallAsync(OfficialCompanionAsset asset,OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            string key=asset.Id+":"+asset.Sha256;Work<InstallResult> work;long lease;
            lock(gate) {
                Alive();cancel.ThrowIfCancellationRequested();
                if(!installs.TryGetValue(key,out work)) {
                    work=new Work<InstallResult>{Cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)};installs.Add(key,work);var captured=work;
                    work.Task=Task.Run(async()=>{try{return await InstallCoreAsync(asset,routing,captured,captured.Cancel.Token).ConfigureAwait(false);}finally{lock(gate){Work<InstallResult> current;if(installs.TryGetValue(key,out current)&&ReferenceEquals(current,captured))installs.Remove(key);}}});Observe(work.Task);DisposeWhenUnleased(work);
                }
                lease=++work.Next;work.Listeners.Add(lease,progress);
            }
            return LeaseAsync(work,lease,cancel,()=>{Work<InstallResult> current;if(installs.TryGetValue(key,out current)&&ReferenceEquals(current,work))installs.Remove(key);});
        }
        async Task<InstallResult> InstallCoreAsync(OfficialCompanionAsset asset,OfficialDownloadRouting routing,Work<InstallResult> work,CancellationToken cancel)
        {
            await slots.WaitAsync(cancel).ConfigureAwait(false);string stage=null,transfer=null;
            Action<string,long,double> report=(step,bytes,percent)=>work.Report(new OfficialCompanionProgress(asset.Id,step,"",bytes,asset.Size,percent),gate);
            try {
                CheckAlive();cancel.ThrowIfCancellationRequested();var existing=Usable(asset.Id);
                if(existing!=null&&(OfficialCompanionStorage.SameAsset(existing.Asset,asset)||CompareContent(existing.Asset,asset)>0)) {
                    await NativeAsync(existing,cancel).ConfigureAwait(false);report("install",existing.Asset.Size,100);return new InstallResult{Installation=existing,Installed=false};
                }
                string root=store.CharacterRoot(asset.Id);System.IO.Directory.CreateDirectory(root);stage=OfficialCompanionStorage.SafePath(root,".stage-"+Guid.NewGuid().ToString("N"));transfer=OfficialCompanionStorage.SafePath(root,".transfer-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(stage);System.IO.Directory.CreateDirectory(transfer);
                string route="";var routeProgress=new OfficialProgress<string>(message=>{route=message;work.Report(new OfficialCompanionProgress(asset.Id,"download",route,0,asset.Size,0),gate);});
                string zip=await transport.OfficialAssetAsync(asset.AssetUrl,asset.AssetName,asset.Size,asset.Sha256,OfficialCompanionRules.MaximumPackage,transfer,routing,new OfficialProgress<double>(p=>work.Report(new OfficialCompanionProgress(asset.Id,"download",route,(long)(Math.Min(p,99)*asset.Size/100),asset.Size,Math.Min(90,p*.9)),gate)),routeProgress,cancel).ConfigureAwait(false);
                report("validate",asset.Size,92);OfficialCompanionPackageValidation.Extract(zip,stage,cancel);OfficialCompanionPackageValidation.Validate(stage,asset,cancel);store.WriteReceipt(stage,asset,cancel);
                await ValidateNativeAsync(new OfficialCompanionInstallation(asset,stage),cancel).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();CheckAlive();
                report("install",asset.Size,99);OfficialCompanionInstallation installed;bool installedNow;
                lock(characterGates[asset.Id]) {
                    existing=Usable(asset.Id);
                    if(existing!=null&&(OfficialCompanionStorage.SameAsset(existing.Asset,asset)||CompareContent(existing.Asset,asset)>0)){installed=existing;installedNow=false;}
                    else{CheckAlive();cancel.ThrowIfCancellationRequested();installed=store.Publish(stage,asset,cancel,CheckAlive,action=>{lock(publicationGate){CheckAlive();action();}});stage=null;lock(gate)nativeValidated.Add(installed.RootDirectory+":"+asset.Sha256);installedNow=true;}
                }
                if(!installedNow)await NativeAsync(installed,cancel).ConfigureAwait(false);report("install",installed.Asset.Size,100);return new InstallResult{Installation=installed,Installed=installedNow};
            }finally {if(stage!=null)OfficialCompanionStorage.DeleteOwned(stage);if(transfer!=null)OfficialCompanionStorage.DeleteOwned(transfer);slots.Release();}
        }
        // Package version wins first; for replacement bytes at the same version the owning feed version wins.
        static int CompareContent(OfficialCompanionAsset left,OfficialCompanionAsset right){int value=ReleaseVersion.Parse(left.Version).CompareTo(ReleaseVersion.Parse(right.Version));if(value!=0)return value;string a=left.Tag.StartsWith("companions-v",StringComparison.Ordinal)?left.Tag.Substring(12):left.Tag.Substring(1),b=right.Tag.StartsWith("companions-v",StringComparison.Ordinal)?right.Tag.Substring(12):right.Tag.Substring(1);return ReleaseVersion.Parse(a).CompareTo(ReleaseVersion.Parse(b));}
        async Task NativeAsync(OfficialCompanionInstallation installation,CancellationToken cancel)
        {
            string key=installation.RootDirectory+":"+installation.Asset.Sha256;lock(gate){Alive();if(nativeValidated.Contains(key))return;}
            await ValidateNativeAsync(installation,cancel).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();lock(gate){Alive();nativeValidated.Add(key);}
        }
        async Task ValidateNativeAsync(OfficialCompanionInstallation installation,CancellationToken cancel)
        {
            try{await validateNative(installation,cancel).ConfigureAwait(false);}catch(OperationCanceledException){throw;}catch(Exception){cancel.ThrowIfCancellationRequested();throw new ApiException(TextResources.Get("officialCompanionDownloads.validateNativeAsync.text"));}
        }
        public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            if(routing==null)throw new ArgumentNullException("routing");CheckAlive();return Task.Run(()=>EnsureAllCoreAsync(routing,progress,cancel),cancel);
        }
        async Task<IList<OfficialCompanionResult>> EnsureAllCoreAsync(OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(cancel,lifetime.Token)) {
                var token=linked.Token;var metadata=await MetadataAsync(routing,OfficialCompanionCheckMode.Force,progress,token).ConfigureAwait(false);
                var results=new OfficialCompanionResult[5];var errors=new List<Exception>();var completed=new Dictionary<string,OfficialCompanionProgress>();var bulkGate=new object();long total=metadata.Snapshot.Assets.Values.Sum(x=>x.Size);
                var reporter=new OfficialProgress<OfficialCompanionProgress>(p=>{lock(bulkGate){completed[p.CharacterId]=p;long bytes=completed.Values.Sum(x=>x.CompletedBytes);bool done=completed.Count==5&&completed.Values.All(x=>x.Percent==100);double percent=total==0?0:completed.Values.Sum(x=>x.TotalBytes*x.Percent)/total;if(progress!=null)progress.Report(new OfficialCompanionProgress(p.CharacterId,p.Stage,p.Route,bytes,total,done?100:Math.Min(99,percent)));}});
                var tasks=OfficialCompanionRules.Ids.Select(async(id,index)=> {
                    try {
                        OfficialCompanionAsset asset;if(!metadata.Snapshot.Assets.TryGetValue(id,out asset))throw new ApiException(TextResources.Get("officialCompanionDownloads.ensureAllCoreAsync.officialCompanionRulesIdsSelect"));
                        var result=await InstallAsync(asset,routing,reporter,token).ConfigureAwait(false);results[index]=new OfficialCompanionResult(result.Installation,false,metadata.Checked,result.Installed,null);
                    }catch(OperationCanceledException){throw;}catch(Exception error){if(!Expected(error))throw;lock(errors)errors.Add(new ApiException(id+"："+OfficialCompanionRules.SafeMessage));}
                }).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);token.ThrowIfCancellationRequested();if(errors.Count!=0)throw new AggregateException(TextResources.Get("officialCompanionDownloads.ensureAllCoreAsync.text"),errors);return new ReadOnlyCollection<OfficialCompanionResult>(results);
            }
        }
        public void Dispose(){lock(publicationGate)lock(gate){if(disposed)return;disposed=true;lifetime.Cancel();}}
    }
}
