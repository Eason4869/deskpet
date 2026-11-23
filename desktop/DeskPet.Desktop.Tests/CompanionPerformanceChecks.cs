using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;

internal static class CompanionPerformanceChecks
{
    sealed class Source : IOfficialCompanionSource
    {
        readonly GitHubUpdates transport=new();
        internal readonly OfficialCompanionDownloads Downloads;
        readonly OfficialCompanionNativeValidator validator=new();
        internal int Updates,Native;
        readonly TaskCompletionSource<OfficialCompanionResult?> update=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Source(string data){Downloads=new(data,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,(i,c)=>{Native++;return validator.ValidateAsync(i,c);},OfficialCompanionReadMode.Snapshots);}
        public long BeginSelection()=>Downloads.BeginSelection();
        public bool OwnsSelection(long token)=>Downloads.OwnsSelection(token);
        public OfficialCompanionInstallation? Installed(string id)=>Downloads.Installed(id);
        public OfficialCompanionSnapshot? CaptureLocalSnapshot(string id,CancellationToken cancel)=>Downloads.CaptureLocalSnapshot(id,cancel);
        public Task<bool> AuditAsync(OfficialCompanionSnapshot snapshot,CancellationToken cancel)=>Downloads.ValidateSnapshotInBackgroundAsync(snapshot,cancel);
        public Task<OfficialCompanionResult?> UpdateAsync(string id,OfficialDownloadRouting routing,CancellationToken cancel,OfficialCompanionCheckMode mode=OfficialCompanionCheckMode.Cached,string? availableVersion=null){Updates++;return update.Task;}
        public Task<OfficialCompanionResult> EnsureAsync(string id,OfficialDownloadRouting routing,OfficialCompanionCheckMode mode,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel)=>throw new InvalidOperationException("Real local benchmark unexpectedly required download/repair.");
        public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel)=>throw new InvalidOperationException();
        public void Dispose(){update.TrySetCanceled();Downloads.Dispose();transport.Dispose();validator.Dispose();}
    }
    static FieldInfo Field(string name)=>typeof(DesktopController).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!;
    static void Until(Func<bool> ready){var clock=Stopwatch.StartNew();while(!ready()){if(clock.Elapsed.TotalSeconds>30)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
    static void Complete(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    internal static void Run(string index,string output,string? fixtures=null,bool natural=false)
    {
        index=Path.GetFullPath(index);output=Path.GetFullPath(output);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Benchmark requires a new owned output directory.");Directory.CreateDirectory(output);
        if(OfficialCompanionBundle.Ebao==null||OfficialCompanionBundle.Ebao.Paths.Count()!=354)throw new InvalidDataException("Real benchmark requires complete embedded Ebao.");
        var rows=new List<object>();
        string marker=Path.Combine(fixtures??output,"performance-fixture-index.sha256"),identity=OfficialCompanionStorage.HashFile(index);
        if(fixtures!=null&&(!File.Exists(marker)||File.ReadAllText(marker)!=identity))throw new InvalidDataException("Only explicitly marked owned benchmark fixtures may be reused.");
        if(fixtures==null)File.WriteAllText(marker,identity);
        foreach(bool olderE in new[]{false,true}){
            string data=Path.Combine(fixtures??output,olderE?"oldE":"bundle");Directory.CreateDirectory(data);
            CompanionPerformancePackages.Install(data,index,"dafeiyu");if(olderE)CompanionPerformancePackages.Install(data,index,"e-bao");
            new SettingsStore(Path.Combine(data,"settings.json")).Save(new PetSettings{OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,NotificationSoundsEnabled=false,HideOnFullscreen=false,FollowCursor=false,LastCompanionUpgradeVersion=ReleaseInfo.DisplayVersion});
            var source=new Source(data);var assets=new SpriteAssets();
            var slots=(OfficialCompanionTransferAdmission)typeof(OfficialCompanionDownloads).GetField("slots",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(source.Downloads)!;
            using var first=natural?null:slots.AcquireAsync(()=>false,CancellationToken.None).Result;using var second=natural?null:slots.AcquireAsync(()=>false,CancellationToken.None).Result;
            using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(data),spriteAssetsFactory:()=>assets,officialSourceFactory:_=>source);
            var sprite=(SpriteView)typeof(PetWindow).GetField("sprite",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(owner.Pet)!;
            void Render(){using var image=new RenderTargetBitmap(new PixelSize(192,208));using var context=image.CreateDrawingContext();sprite.Render(context);}
            void Select(string id){Complete(owner.SelectSkinAsync(owner.Skins.Single(s=>s.OfficialCharacterId==id),CompanionSelectionOrigin.StartupRestore));Render();}
            foreach(string mode in new[]{"cold-decoded-source","warm-retained"})foreach(string target in new[]{"dafeiyu","e-bao"})for(int n=0;n<20;n++){
                string other=target=="e-bao"?"dafeiyu":"e-bao";Select(other);Select(target);Select(other);
                if(mode=="cold-decoded-source"){
                    var skin=owner.Skins.Single(s=>s.OfficialCharacterId==target);assets.RetireSource(skin.Snapshot?.Generation??skin.SourceKey);
                    ((System.Collections.IDictionary)Field("preparedCompanions").GetValue(owner)!).Remove(target);
                    var snapshots=(List<OfficialCompanionSnapshot>)Field("companionSnapshots").GetValue(owner)!;
                    foreach(var old in snapshots.Where(s=>s.Generation==skin.Snapshot?.Generation).ToArray()){snapshots.Remove(old);old.Dispose();}
                }
                int decodes=assets.DecodeCount,native=source.Native;long hashes=source.Downloads.Storage.VerificationCount;
                var stages=new List<CompanionSelectionUpdate>();var observed=new Dictionary<string,double>();var clock=Stopwatch.StartNew();void Observe(CompanionSelectionUpdate u){observed[u.Stage]=clock.Elapsed.TotalMilliseconds;stages.Add(u);}owner.CompanionSelectionChanged+=Observe;
                var task=owner.SelectSkinAsync(owner.Skins.Single(s=>s.OfficialCharacterId==target),CompanionSelectionOrigin.Settings);double admission=clock.Elapsed.TotalMilliseconds;Complete(task);Render();owner.CompanionSelectionChanged-=Observe;
                var prepared=stages.Single(u=>u.Stage=="prepared");
                if(!natural&&source.Native!=native)throw new InvalidOperationException("Gated background native validation unexpectedly ran.");
                if(mode=="warm-retained"&&assets.DecodeCount!=decodes)throw new InvalidOperationException("Warm real-pack selection decoded new resources.");
                var row=new{scenario=olderE?"older-managed-E":"normal-bundle",mode,target,sample=n+1,requestedMs=0,feedbackMs=observed["requested"],admissionMs=admission,preparedMs=observed["prepared"],committedMs=observed["committed"],firstRenderMs=observed["completed"],cacheHit=prepared.CacheHit,newDecodedEntries=assets.DecodeCount-decodes,allThreadFullPackHashes=source.Downloads.Storage.VerificationCount-hashes,allThreadNativeValidations=source.Native-native,networkWaits=0,backgroundUpdateAttempts=source.Updates,retainedPixelBytes=assets.RetainedPixelBytes};rows.Add(row);File.AppendAllText(Path.Combine(output,"samples.jsonl"),JsonSerializer.Serialize(row)+"\n");if(n==0)Console.WriteLine("SAMPLE "+olderE+" "+mode+" "+target+" "+prepared.Milliseconds);
            }
            owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);
        }
        File.WriteAllText(Path.Combine(output,"timings.json"),JsonSerializer.Serialize(new{backend="Avalonia Skia Headless on "+System.Runtime.InteropServices.RuntimeInformation.OSDescription,backgroundGated=!natural,definition="Cold clears target decoded/source caches in an already running process; OS filesystem cache uncontrolled. Warm retains both complete sources. When backgroundGated=true audit slots are held; otherwise real native background audit/cleanup may compete. Updates intentionally unresolved. Full-hash/native counters include every thread, not foreground-only. Setup integrity/publication excluded. First render explicitly invokes SpriteView.Render into Skia RenderTargetBitmap; not a physical Mac/display measurement.",index,bundleGeneration=OfficialCompanionBundle.Ebao.Generation,bundleZipSha256=OfficialCompanionBundle.Ebao.ZipSha256,samples=rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS real Avalonia switching: "+rows.Count+" samples; "+output);
    }
}
