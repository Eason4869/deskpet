using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    public static class WpfOfficialCompanionTests
    {
        static int passed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static void DecodedBudget()
        {
            var cache=new CompanionDecodedCache<object>(64);var a=new object();var b=new object();var c=new object();
            cache.Put("a",a,60);cache.Pin("a");cache.Put("b",b,40);cache.Put("c",c,40);
            Check(cache.Get("a")==a&&cache.Get("b")==null&&cache.Get("c")==c,"decoded LRU retains current pin and limits noncurrent bytes");
            Check(cache.RetainedBytes==100 && cache.UnpinnedBytes==40 && cache.PinnedBytes==60 && cache.PinnedResourceCount==1 && cache.PinLeaseCount==1,"decoded ledger separates pinned and noncurrent bytes");
            cache.Pin("a");cache.Pin("pending");
            Check(cache.PinnedResourceCount==2 && cache.PinLeaseCount==3 && cache.PinnedBytes==60,"pin ledger includes multiple holders and pending preparation without a cache entry");
            cache.Unpin("a");cache.Unpin("pending");
            cache.Unpin("a");Check(cache.UnpinnedBytes<=64,"unpin enforces decoded budget immediately");
            Check(cache.RetainedBytes==40 && cache.PinnedBytes==0 && cache.PinnedResourceCount==0 && cache.PinLeaseCount==0,"unpin removes the pin ledger and evicts the retired resource");
            cache.Remove("c");Check(cache.RetainedBytes==0 && cache.UnpinnedBytes==0 && cache.PinnedBytes==0 && cache.PinnedResourceCount==0 && cache.PinLeaseCount==0,"removing final decoded entry clears the actual byte and pin ledger");
        }
        static void VerifiedDecode()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verified-wpf-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                string path=Path.Combine(root,"gaze.png");WriteNative(path,4,4,16);var bytes=File.ReadAllBytes(path);
                var files=new Dictionary<string,OfficialCompanionFile>{{"gaze.png",new OfficialCompanionFile(bytes.Length,OfficialCompanionSnapshot.Digest(bytes))}};
                var asset=new OfficialCompanionAsset("e-bao","E","1.0.0","1.0.0","v1.0.0","e.zip",1,new string('a',64),new OfficialCompanionOutfit[0]);
                using(var snapshot=new OfficialCompanionSnapshot(new OfficialCompanionInstallation(asset,root),files,"verified",root,null)) {
                    var image=IllustratedPack.ReadImage(snapshot,"gaze.png",1920,2080);
                    Check(image.IsFrozen&&image.PixelWidth==1920,"snapshot decoder consumes verified bytes into frozen native image");
                    bytes[bytes.Length-1]^=1;File.WriteAllBytes(path,bytes);bool rejected=false;
                    try{IllustratedPack.ReadImage(snapshot,"gaze.png",1920,2080);}catch(InvalidDataException){rejected=true;}
                    Check(rejected,"snapshot decoder rejects changed bytes even after prior native success");
                }
            } finally {Directory.Delete(root,true);}
        }
        static void Hold(IllustratedAnimation clip,double hold)
        {var property=typeof(IllustratedAnimation).GetProperty("HoldLastSeconds");Check(property!=null,"illustrated jump hold metadata is available");property.SetValue(clip,hold,null);}
        [STAThread] public static int Main(string[] args)
        {
            try {if(args.Contains("--selection-admission")){SelectionAdmissionChecks();Console.WriteLine("WPF selection admission: "+passed+" passed");return 0;}if(args.Any(a=>a.StartsWith("--real-performance="))){WpfCompanionPerformance.Run(args.Single(a=>a.StartsWith("--performance-index=")).Substring("--performance-index=".Length),args.Single(a=>a.StartsWith("--real-performance=")).Substring("--real-performance=".Length),args.Where(a=>a.StartsWith("--owned-fixtures=")).Select(a=>a.Substring("--owned-fixtures=".Length)).FirstOrDefault(),args.Contains("--phase-probe"),args.Contains("--natural-background"));return 0;}if(args.Contains("--operation-sounds-ui")){OperationSoundChecks();Console.WriteLine("WPF operation sounds: "+passed+" passed");return 0;}if(args.Contains("--bulk-reader-lifetime")){BulkRecoveryReaderLifetime();Console.WriteLine("WPF bulk reader lifetime: "+passed+" passed");return 0;}if(args.Contains("--installation-status")){InstallationStatusChecks();Console.WriteLine("Installation status: "+passed+" passed");return 0;}if(args.Contains("--bundled-e")){BundledEbaoChecks();Console.WriteLine("Bundled Ebao: "+passed+" passed");return 0;}if(args.Contains("--review-fixes")){ReviewFailurePaths();Console.WriteLine("WPF review fixes: "+passed+" passed");return 0;}DecodedBudget();VerifiedDecode();JumpHold();NativePixels();CatalogTrust();if(args.Contains("--thin-ui"))ThinStartupAndGuide(args.FirstOrDefault(a=>a.StartsWith("--legacy-assets=",StringComparison.Ordinal)));Console.WriteLine("WPF official companion tests: "+passed+" passed");return 0;}
            catch(Exception error){Console.WriteLine("FAIL WPF official companion: "+error);return 1;}
        }
        static void SelectionAdmissionChecks()
        {
            var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"admission-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var settings=new PetSettings{OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false};var store=new SettingsStore(Path.Combine(root,"settings.json"));var pet=new PetWindow(null,settings,store,null);
            try{
                Field("officialStartupStarted").SetValue(pet,true);store.Save(settings);string path=Path.Combine(root,"settings.json"),selected=settings.SelectedCompanionKey;
                var time=new DateTime(2001,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(path,time);
                var begin=typeof(PetWindow).GetMethod("BeginCompanionSelection",BindingFlags.NonPublic|BindingFlags.Instance);
                begin.Invoke(pet,new object[]{pet.Skin});
                Check(File.GetLastWriteTimeUtc(path)==time,"admission does not write unchanged settings before requested feedback");
                settings.PendingOfficialCompanionId="dafeiyu";begin.Invoke(pet,new object[]{pet.Skin});var persisted=store.Load();
                Check(persisted.PendingOfficialCompanionId==""&&persisted.SelectedCompanionKey==selected,"admission still persists a legacy pending clear without changing last successful selection");
            }finally{pet.Close();Pump();Directory.Delete(root,true);}
        }
        sealed class OperationOutput:ILiveAudioOutput
        {
            internal int Plays,Disposals,LastBytes,Volume;public void SetVolume(int v){Volume=v;}public void Enqueue(byte[] bytes,int rate){LastBytes=bytes.Length;Interlocked.Increment(ref Plays);}public void Interrupt(){}public void Dispose(){Interlocked.Increment(ref Disposals);}
        }
        static void OperationSoundChecks()
        {
            var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"operation-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);PetWindow pet=null;Window preferences=null;
            try{
                var settings=new PetSettings{OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,HideOnFullscreen=false,Wander=false,FollowCursor=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);Field("officialStartupStarted").SetValue(pet,true);Field("officialFirstFrameRendered").SetValue(pet,true);
                var output=new OperationOutput();DateTime now=DateTime.UtcNow;var player=new NotificationSoundPlayer(()=>settings,()=>output,clock:()=>now);Field("notificationSounds").SetValue(pet,player);pet.EnableNotificationAudio();
                Action<int> wait=count=>{var end=DateTime.UtcNow.AddSeconds(8);while(output.Plays<count&&DateTime.UtcNow<end){Pump();Thread.Sleep(5);}Check(output.Plays==count,"native sound count "+count);player.Stop();SpinWait.SpinUntil(()=>output.Disposals>=count,4000);now=now.AddSeconds(2);};
                pet.Show();Pump();wait(1);Check(output.LastBytes==36480,"visible launch emits approved Appear");pet.HidePet();wait(2);Check(!pet.IsPetVisible&&output.LastBytes==38400,"explicit hide emits approved Hide");pet.HidePet();Pump();Check(output.Plays==2,"already hidden emits no cue");pet.RestorePet();wait(3);Check(pet.IsPetVisible&&output.LastBytes==36480,"explicit restore emits Appear");pet.RestorePet();Pump();Check(output.Plays==3,"already visible emits no cue");
                Wait(pet.RequestSkinAsync(pet.Skin,CompanionSelectionOrigin.Settings,null));Pump();Check(output.Plays==3,"same role does not switch");
                string source=Path.Combine(data,"fixture");Directory.CreateDirectory(source);WriteNative(Path.Combine(source,"action.png"),8,1,8);WriteNative(Path.Combine(source,"gaze.png"),4,4,16);WriteNative(Path.Combine(source,"preview.png"),1,1,1);
                var manifest=new SkinManifest{Version=2,PackageId="sound-fixture",Name="Sound fixture",Format="illustrated-atlases-v1",CellWidth=480,CellHeight=520,Preview="preview.png",Gaze=new IllustratedGaze{File="gaze.png",Columns=4,Frames=16,Interpolation="none",Order="clockwise-from-up"},Animations=new Dictionary<string,IllustratedAnimation>()};foreach(string action in IllustratedPack.Actions)manifest.Animations[action]=new IllustratedAnimation{File="action.png",Columns=8,Frames=8,Fps=8,Loop=action!="jumping"};string json=Path.Combine(source,"pet.json");using(var stream=File.Create(json))SkinCatalog.Serializer().WriteObject(stream,manifest);var custom=pet.Catalog.Import(json);
                Wait(pet.RequestSkinAsync(custom,CompanionSelectionOrigin.Settings,null));wait(4);Check(output.LastBytes==41280,"actual changed target first render emits Switch");
                pet.HidePet();wait(5);Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("e-bao"),CompanionSelectionOrigin.Settings,null));Pump();Check(output.Plays==5,"hidden selection stays silent");pet.RestorePet();wait(6);Check(output.LastBytes==36480,"Show wins over hidden selection completion");Pump();Check(output.Plays==6,"hidden completion never replays later");
                pet.SetQuietMode(true);pet.HidePet();Pump();Check(output.Plays==6,"quiet explicit hide suppressed");pet.SetQuietMode(false);now=now.AddSeconds(2);Check(output.Plays==6,"quiet exit does not replay");
                settings.NotificationSoundsEnabled=false;var panel=new QuietPanel(pet);preferences=new Window{Content=panel,Width=780,Height=1000};preferences.Show();Pump();Check(Descendants(panel).OfType<CheckBox>().Count(c=>c.Name=="NotificationSoundsEnabled")==1&&!Descendants(panel).OfType<CheckBox>().Any(c=>c.Name=="CompanionOperationSoundsEnabled"),"one visible sound master");var preview=Descendants(panel).OfType<Button>().Single(b=>b.Name=="PreviewCompanionSwitch");var volume=Descendants(panel).OfType<Slider>().Single(v=>v.Name=="CompanionOperationSoundVolume");volume.Value=47;preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));wait(7);Check(output.Volume==47&&settings.CompanionOperationSoundVolume==47,"preview uses displayed independent volume");Check(output.LastBytes==41280&&!settings.NotificationSoundsEnabled,"actual preview button works while disabled");Check(Descendants(panel).OfType<Slider>().Single(v=>v.Name=="CompanionOperationSoundVolume").Maximum==100,"native volume range");
                CaptureBubble(preferences,"task-4-wpf-operation-settings.png");
                foreach(var language in UiLanguages.Choices.Where(c=>c.Code!="system")){TextResources.ApplyLanguage(TextResources.PrepareLanguage(language.Code));Pump();Check(System.Windows.Automation.AutomationProperties.GetName(preview)==TextResources.Get("operationSounds.switch"),"live localized accessible preview "+language.Code);}
            }finally{if(preferences!=null)preferences.Close();if(pet!=null)pet.Close();Pump();Directory.Delete(data,true);}
        }
        static void InstallationStatusChecks()
        {
            var status=new CompanionInstallationStatus();Action<long,string,string> send=(r,key,stage)=>status.Accept(new CompanionSelectionUpdate(r,key,"Settings",stage,0));
            send(1,"official:dafeiyu","requested");Check(!status.Active,"usable local selection has no installation presentation");
            send(1,"official:dafeiyu","installing");Check(status.Visible(true)&&!status.Visible(false)&&status.CanCancel&&!status.CanRetry,"install shows passive state only while pet visible");
            status.Dismiss();send(1,"official:dafeiyu","progress");Check(!status.Visible(true)&&status.CanCancel,"close hides request without cancelling it");
            send(1,"official:dafeiyu","failed");Check(status.CanRetry&&!status.CanCancel&&!status.Visible(true),"failure exposes retry but does not reopen dismissed bubble");
            send(2,"official:marfu","requested");send(2,"official:marfu","installing");send(1,"official:dafeiyu","completed");Check(status.Request==2&&status.Visible(true),"new intent resets dismissal and rejects stale completion");
            send(2,"official:marfu","reselect-required");Check(status.Active&&!status.CanCancel&&!status.CanRetry&&status.Text.Length>0,"busy completion explains explicit reselect without queuing");
            send(3,"official:marfu","requested");send(3,"official:marfu","installing");send(3,"official:marfu","committed");Check(!status.Active,"committed install dismisses progress before sound owned render completion");
            status.Accept(new CompanionSelectionUpdate(4,"official:marfu","StartupRestore","requested",0));status.Accept(new CompanionSelectionUpdate(4,"official:marfu","StartupRestore","installing",0));Check(!status.Active,"startup restore never presents installation bubble");
            send(5,"official:marfu","requested");send(5,"official:marfu","installing");send(5,"official:marfu","cancelled");Check(!status.Active,"cancel removes install presentation");
            var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bubble-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);PetWindow pet=null;CompanionInstallationBubble bubble=null;
            try {
                pet=new PetWindow(null,new PetSettings{OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false},new SettingsStore(Path.Combine(data,"settings.json")),null);Field("officialStartupStarted").SetValue(pet,true);
                bubble=new CompanionInstallationBubble(pet);bubble.Accept(new CompanionSelectionUpdate(1,"official:dafeiyu","Settings","requested",0));bubble.Accept(new CompanionSelectionUpdate(1,"official:dafeiyu","Settings","installing",0));Check(!bubble.IsVisible,"hidden pet does not expose installation window");
                Check(pet.Notice==bubble.Status.Text&&pet.Notice.Length>0,"hidden installation updates initiating selector immediately without a timer tick");
                pet.Show();Pump();bubble.Accept(new CompanionSelectionUpdate(2,"official:dafeiyu","Settings","requested",0));Check(!bubble.IsVisible&&pet.Notice=="","new request immediately removes previous presentation");
                bubble.Accept(new CompanionSelectionUpdate(2,"official:dafeiyu","Settings","installing",0));Check(bubble.IsVisible&&bubble.ShowActivated==false&&pet.Notice==bubble.Status.Text,"Accept immediately shows nonactivating installation and selector status");
                var message=Descendants(bubble).OfType<TextBlock>().First(t=>t.Name=="CompanionInstallationMessage");string last=message.Text;int refreshes=0,sequence=0;var clock=System.Diagnostics.Stopwatch.StartNew();
                while(clock.ElapsedMilliseconds<1050){
                    bubble.Accept(new CompanionSelectionUpdate(2,"official:dafeiyu","Settings","progress",0,false,new OfficialCompanionProgress("dafeiyu","download","",++sequence,1000,sequence%90)));
                    var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
                    if(message.Text!=last){refreshes++;last=message.Text;}Thread.Sleep(5);
                }
                Check(refreshes>=1&&refreshes<=4,"continuous progress is coalesced to at most four updates per second");CaptureBubble(bubble,"task-3-installing.png");
                bubble.Accept(new CompanionSelectionUpdate(2,"official:dafeiyu","Settings","committed",0));Check(!bubble.IsVisible&&pet.Notice=="","commit immediately removes bubble and selector status without a timer tick");
                bubble.Accept(new CompanionSelectionUpdate(3,"official:marfu","Settings","requested",0));bubble.Accept(new CompanionSelectionUpdate(3,"official:marfu","Settings","installing",0));
                var close=Descendants(bubble).OfType<Button>().First(b=>b.Name=="DismissCompanionInstallation");close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));bubble.Accept(new CompanionSelectionUpdate(3,"official:marfu","Settings","progress",0));Check(!bubble.IsVisible&&bubble.Status.CanCancel,"UI close preserves request through later progress");
                bubble.Accept(new CompanionSelectionUpdate(3,"official:marfu","Settings","failed",0));Check(!bubble.IsVisible&&bubble.Status.CanRetry,"terminal failure does not reopen dismissed presentation");
                bubble.Accept(new CompanionSelectionUpdate(4,"official:marfu","Settings","requested",0));bubble.Accept(new CompanionSelectionUpdate(4,"official:marfu","Settings","installing",0));bubble.Accept(new CompanionSelectionUpdate(4,"official:marfu","Settings","failed",0));Check(bubble.IsVisible&&Descendants(bubble).OfType<Button>().First(b=>b.Name=="RetryCompanionInstallation").IsVisible,"new failed install immediately offers retry in native bubble");CaptureBubble(bubble,"task-3-failed.png");
                bubble.Close();bubble.Accept(new CompanionSelectionUpdate(5,"official:marfu","Settings","requested",0));bubble.Accept(new CompanionSelectionUpdate(5,"official:marfu","Settings","installing",0));Check(bubble.IsVisible,"system close hides presentation and permits a later new request");
                bubble.Accept(new CompanionSelectionUpdate(5,"official:marfu","Settings","cancelled",0));Check(!bubble.IsVisible&&pet.Notice=="","cancel immediately removes bubble and selector status without a timer tick");
            }finally{if(bubble!=null)bubble.Shutdown();if(pet!=null)pet.Close();Pump();Directory.Delete(data,true);}
        }
        static void CaptureBubble(Window window,string name){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name)))png.Save(output);}
        static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
        static void BundledEbaoChecks()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                var catalog=new SkinCatalog(root);var skin=catalog.FindOfficial("e-bao");Check(skin.Illustrated&&skin.Bundle!=null,"production E uses separate illustrated resources");
                var bundle=skin.Bundle;Check(bundle.Paths.Count()==354&&skin.PackageHolidayCatalog.Outfits.Count==10,"complete embedded source contains ten matching outfits");
                Check(bundle.PrefersBundle("0.11.0-dev.3")&&!bundle.PrefersBundle("9.0.0"),"bundle floor rejects older and preserves newer compatible versions");
                using(WpfPreparedCompanion.Hold(skin)){skin.PreparedOfficialAtlas=skin.Load();var clip=WpfPreparedCompanion.Load(skin,skin.Manifest.Animations["idle"]);Check(skin.PreparedOfficialAtlas.DecodedBytes<32L*1024*1024&&clip.Count==8,"E uses bounded idle and gaze instead of legacy full atlas");}
                Check(!Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Any(p=>p.Contains("versions")||p.Contains("bundled-skins")),"embedded selection extracts no package into user cache");
                using(var fixture=new OfficialCompanionFixture())using(var transport=fixture.Transport())
                using(var downloads=new OfficialCompanionDownloads(fixture.Root,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,new WpfOfficialNativeValidator().ValidateAsync,OfficialCompanionReadMode.Snapshots)){
                    fixture.Files[OfficialCompanionFixture.PackageName("e-bao")]=NativePackage("e-bao",100);
                    var install=downloads.EnsureAsync("e-bao",new OfficialDownloadRouting(false,"",false,""),OfficialCompanionCheckMode.Cached,null,CancellationToken.None);Wait(install);
                    using(var reader=downloads.CaptureLocalSnapshot("e-bao")){
                    string transaction=Path.Combine(downloads.Storage.CharacterRoot("e-bao"),"bundle-transaction.json");
                    File.WriteAllBytes(transaction,OfficialJson.Bytes(new{schema_version=99,old_current=Path.GetFileName(install.Result.Installation.RootDirectory),bundle=bundle.Generation+"|"+bundle.Version+"|"+bundle.MinimumAppVersion+"|"+bundle.ZipSha256}));
                    bool rejected=false;try{Wait(downloads.ConfirmBundleReadyAsync(bundle,CancellationToken.None));}catch(IOException){rejected=true;}
                    Check(rejected&&downloads.Storage.CurrentRoot("e-bao")==install.Result.Installation.RootDirectory,"unknown bundle transaction preserves old pointer and complete source");File.Delete(transaction);
                    downloads.Storage.Commit=(from,to)=>{throw new IOException("fixture interrupted bundle handoff");};
                    rejected=false;try{Wait(downloads.ConfirmBundleReadyAsync(bundle,CancellationToken.None));}catch(IOException){rejected=true;}
                    Check(rejected&&downloads.Storage.CurrentRoot("e-bao")==install.Result.Installation.RootDirectory&&Directory.Exists(install.Result.Installation.RootDirectory),"failed bundle pointer commit preserves working managed E");downloads.Storage.Commit=AtomicFile.Commit;
                    var handoff=downloads.ConfirmBundleReadyAsync(bundle,CancellationToken.None);Wait(handoff);Wait(downloads.HistoryMaintenance);
                    Check(reader!=null&&Directory.Exists(install.Result.Installation.RootDirectory)&&reader.ReadVerifiedBytes("pet.json").Length>0,"full bundle handoff protects an outstanding old E reader");
                    Check(downloads.CaptureLocalSnapshot("e-bao")==null,"bundle lifecycle handoff clears managed pointer without fake filesystem source");
                    string bundleProof=OfficialJson.Text(OfficialJson.File(Path.Combine(downloads.Storage.CharacterRoot("e-bao"),"versions.json"),1024*1024),"bundle",400);
                    Check(bundleProof.Contains(bundle.Version)&&bundleProof.Contains(bundle.MinimumAppVersion)&&bundleProof.Contains(bundle.PayloadSha256)&&bundleProof.Contains(bundle.ZipSha256),"durable bundle handoff binds version minimum payload and actual archive identity");
                    reader.Dispose();Wait(downloads.HistoryMaintenance);
                    Check(!Directory.Exists(install.Result.Installation.RootDirectory)&&bundle.CreateHolidayCatalog().Outfits.Count==10,"last old E reader release reclaims managed E while complete bundle stays usable");
                    var next=downloads.EnsureAsync("e-bao",new OfficialDownloadRouting(false,"",false,""),OfficialCompanionCheckMode.Cached,null,CancellationToken.None);Wait(next);
                    using(var resumed=downloads.CaptureLocalSnapshot("e-bao"))Check(next.Result.InstalledNow&&resumed!=null,"later explicit publication can resume from bundle lifecycle state");
                    }
                }
                var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                using(var fixture=new OfficialCompanionFixture())using(var transport=fixture.Transport())using(var downloads=new OfficialCompanionDownloads(root,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,new WpfOfficialNativeValidator().ValidateAsync)){
                    fixture.Files[OfficialCompanionFixture.PackageName("e-bao")]=NativePackage("e-bao",100);
                    var install=downloads.EnsureAsync("e-bao",new OfficialDownloadRouting(false,"",false,""),OfficialCompanionCheckMode.Cached,null,CancellationToken.None);Wait(install);
                    var pet=new PetWindow(null,new PetSettings{SkinId="e-bao",SelectedCompanionKey="official:e-bao",OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false},new SettingsStore(Path.Combine(root,"settings.json")),null);
                    try {
                        Field("officialStartupStarted").SetValue(pet,true);Check(pet.Holidays.Catalog.Outfits.Count(o=>o.CharacterId=="e-bao")==10,"startup exposes all bundled E outfits");Check(pet.Skin.Bundle!=null,"startup chooses bundle before caching an older installed E");
                        var sources=(System.Collections.IDictionary)Field("officialSources").GetValue(pet);Check(!sources.Contains("e-bao")||((SkinDefinition)sources["e-bao"]).Bundle!=null,"older installed E is never retained in prepared source cache");
                        var select=pet.RequestSkinAsync(pet.Catalog.FindOfficial("e-bao"),CompanionSelectionOrigin.StartupRestore,null);Wait(select);Check(select.Result&&pet.Skin.Bundle!=null,"explicit E reselect cannot reactivate rejected older installation");
                        var method=typeof(PetWindow).GetMethod("PrepareOfficialAsync",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(OfficialCompanionInstallation),typeof(bool)},null);
                        var background=(Task<SkinDefinition>)method.Invoke(pet,new object[]{install.Result.Installation,true});Wait(background);Check(background.Result.Bundle!=null,"background publication also honors bundled version floor");
                        ((OfficialCompanionDownloads)Field("officialDownloads").GetValue(pet)).Dispose();Field("officialDownloads").SetValue(pet,downloads);
                        int packages=fixture.Packages;long token=downloads.BeginSelection();
                        var refresh=(Task<bool>)typeof(PetWindow).GetMethod("RefreshOfficialAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{"e-bao",token,false,CompanionSelectionOrigin.StartupRestore,null,OfficialCompanionCheckMode.AfterApplicationUpdate});Wait(refresh);
                        Check(refresh.Result&&fixture.Packages==packages&&pet.Settings.LastCompanionUpgradeVersion==ReleaseInfo.DisplayVersion,"WPF background check uses bundle baseline without downloading older remote E");
                        var installed=install.Result.Installation;string json=Path.Combine(installed.RootDirectory,"pet.json");File.WriteAllText(json,File.ReadAllText(json).Replace(OfficialCompanionFixture.Version,"9.0.0"));
                        var entries=Directory.GetFiles(installed.RootDirectory,"*",SearchOption.AllDirectories).Where(f=>!f.EndsWith("receipt.json")).ToDictionary(f=>f.Substring(installed.RootDirectory.Length+1).Replace('\\','/'),f=>new OfficialCompanionFile(new FileInfo(f).Length,OfficialCompanionSnapshot.Digest(File.ReadAllBytes(f))));
                        var asset=new OfficialCompanionAsset("e-bao","E","9.0.0","0.10.3-dev.1","v9.0.0","new.zip",1,new string('a',64),new[]{new OfficialCompanionOutfit("halloween-2026","1.0.0")});
                        var snapshot=new OfficialCompanionSnapshot(new OfficialCompanionInstallation(asset,installed.RootDirectory),entries,"newer-E",root,null);
                        var prepare=(Task<SkinDefinition>)typeof(PetWindow).GetMethod("PrepareSnapshotAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{snapshot,CancellationToken.None});Wait(prepare);Check(prepare.Result.Snapshot==snapshot&&prepare.Result.Bundle==null,"newer compatible installed E wins over application bundle");
                    }finally{pet.Close();Pump();}
                }

            }finally{try{Directory.Delete(root,true);}catch(IOException){}}
        }
        static void ReviewFailurePaths()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"rv",Guid.NewGuid().ToString("N").Substring(0,8));PetWindow pet=null;
            try{
                using(var fixture=new OfficialCompanionFixture())using(var transport=fixture.Transport())using(var service=new OfficialCompanionDownloads(data,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,new WpfOfficialNativeValidator().ValidateAsync)){
                    fixture.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=NativePackage("dafeiyu",100,true);
                    var install=service.EnsureAsync("dafeiyu",new OfficialDownloadRouting(false,"",false,""),OfficialCompanionCheckMode.Cached,null,CancellationToken.None);Wait(install);
                    var installed=install.Result.Installation;
                    int repairCalls=0;bool offline=true;fixture.Intercept=(request,cancel)=>{if(request.RequestUri.AbsolutePath.EndsWith(".zip",StringComparison.Ordinal)){repairCalls++;if(offline)return Task.FromResult(OfficialCompanionFixture.Response(new byte[0],HttpStatusCode.NotFound));}return Task.FromResult<System.Net.Http.HttpResponseMessage>(null);};
                    foreach(var path in new[]{"holidays/catalog.json","holidays/outfit.json"}){
                        string file=Path.Combine(installed.RootDirectory,path.Replace('/',Path.DirectorySeparatorChar));if(!File.Exists(file))file=Directory.GetFiles(installed.HolidayRootDirectory,"outfit.json",SearchOption.AllDirectories).Single();
                        var original=File.ReadAllBytes(file);var damaged=(byte[])original.Clone();damaged[0]^=1;File.WriteAllBytes(file,damaged);
                        var settings=new PetSettings{SkinId="e-bao",SelectedCompanionKey="official:e-bao",OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false};
                        pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);var oldService=Field("officialDownloads").GetValue(pet) as OfficialCompanionDownloads;if(oldService!=null)oldService.Dispose();Field("officialDownloads").SetValue(pet,service);Field("officialStartupStarted").SetValue(pet,true);
                        var old=pet.Skin;var frame=((Image)pet.Content).Source;string persona=pet.Settings.SystemPrompt,key=pet.Settings.SelectedCompanionKey,id=pet.Settings.SkinId;pet.SaveSettings();
                        var select=pet.RequestSkinAsync(pet.Catalog.FindOfficial("dafeiyu"),CompanionSelectionOrigin.StartupRestore,null);Wait(select);
                        Check(!select.Result&&ReferenceEquals(pet.Skin,old)&&ReferenceEquals(((Image)pet.Content).Source,frame)&&pet.Settings.SystemPrompt==persona&&pet.Settings.SkinId==id&&pet.Settings.SelectedCompanionKey==key&&new SettingsStore(Path.Combine(data,"settings.json")).Load().SelectedCompanionKey==key,"corrupt wardrobe "+path+" keeps visual persona and saved identity coherent");
                        File.WriteAllBytes(file,original);Field("officialDownloads").SetValue(pet,null);pet.Close();Pump();pet=null;
                    }
                    var cfg=new PetSettings{SkinId="e-bao",SelectedCompanionKey="official:e-bao",OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false};
                    pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(data,"settings.json")),null);var unused=Field("officialDownloads").GetValue(pet) as OfficialCompanionDownloads;if(unused!=null)unused.Dispose();Field("officialDownloads").SetValue(pet,service);Field("officialStartupStarted").SetValue(pet,true);
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("dafeiyu"),CompanionSelectionOrigin.StartupRestore,null));
                    var active=pet.Skin;Wait(service.ValidateSnapshotInBackgroundAsync(active.Snapshot));
                    string cold=Path.Combine(active.Source,"cold.png");var coldBytes=File.ReadAllBytes(cold);coldBytes[0]^=1;File.WriteAllBytes(cold,coldBytes);
                    int before=repairCalls;var currentFrame=((Image)pet.Content).Source;var motion=active.Manifest.Animations["running"];
                    for(int n=0;n<8;n++)WpfPreparedCompanion.Ready(active,motion);
                    var deadline=DateTime.UtcNow.AddSeconds(10);while(repairCalls==before&&DateTime.UtcNow<deadline)Pump();
                    Check(repairCalls==before+1,"cold default corruption requests one silent repair after completed audit");
                    var repairs=(System.Collections.IDictionary)Field("officialRepairs").GetValue(pet);deadline=DateTime.UtcNow.AddSeconds(15);while(repairs.Count!=0&&DateTime.UtcNow<deadline)Pump();
                    for(int n=0;n<8;n++)WpfPreparedCompanion.Ready(active,motion);Pump();
                    Check(repairCalls==before+1&&ReferenceEquals(pet.Skin,active)&&ReferenceEquals(((Image)pet.Content).Source,currentFrame),"offline lazy repair is throttled and retains working frame");
                    ((System.Collections.IDictionary)Field("officialRepairAfter").GetValue(pet))[active.Snapshot.Generation]=DateTime.MinValue;offline=false;
                    var busy=pet.QuestionController.AskAsync(new AgentQuestionRequest{SessionId="repair-busy",TaskId="task",CallId="call",Question="wait"},CancellationToken.None);
                    WpfPreparedCompanion.Ready(active,motion);deadline=DateTime.UtcNow.AddSeconds(30);while((repairCalls<before+2||repairs.Count!=0)&&DateTime.UtcNow<deadline)Pump();
                    var pending=Field("deferredOfficial").GetValue(pet);Check(pending!=null&&ReferenceEquals(pet.Skin,active),"successful repair remains queued while business is busy");
                    var prepared=(SkinDefinition)pending.GetType().GetField("Skin",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pending);
                    ((System.Collections.IDictionary)Field("officialRepairAfter").GetValue(pet))[active.Snapshot.Generation]=DateTime.MinValue;
                    for(int n=0;n<8;n++)WpfPreparedCompanion.Ready(active,motion);Pump();
                    Check(repairCalls==before+2&&ReferenceEquals(Field("deferredOfficial").GetValue(pet),pending)&&ReferenceEquals(pet.Skin,active),"pending successful repair suppresses another transfer after retry deadline expires");
                    pet.QuestionController.CancelSession("repair-busy");typeof(PetWindow).GetMethod("TryApplyDeferredOfficial",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                    deadline=DateTime.UtcNow.AddSeconds(10);while(ReferenceEquals(pet.Skin,active)&&DateTime.UtcNow<deadline)Pump();
                    Check(ReferenceEquals(pet.Skin,prepared)&&pet.Settings.SelectedCompanionKey==active.PickerKey&&pet.CurrentCompanionSelection.Stage!="completed","safe idle applies the original successful pending repair without selection cue");
                    Check(WpfPreparedCompanion.Load(pet.Skin,pet.Skin.Manifest.Animations["running"])!=null,"repaired cold default action decodes");
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("e-bao"),CompanionSelectionOrigin.StartupRestore,null));int settled=repairCalls;
                    WpfPreparedCompanion.Ready(active,motion);Pump();Check(pet.Skin.BuiltinCharacterId=="e-bao"&&repairCalls==settled,"late failure from superseded source cannot repair or reclaim user selection");
                    Field("officialDownloads").SetValue(pet,null);pet.Close();Pump();pet=null;
                }
            }finally{if(pet!=null)pet.Close();app.Shutdown();}
        }
        static void JumpHold()
        {
            foreach(double hold in new[]{.05,.35}) {
                var manifest=new SkinManifest{Version=2,Animations=new Dictionary<string,IllustratedAnimation>()};
                foreach(string action in AnimationPlayer.States)manifest.Animations[action]=new IllustratedAnimation{Frames=action=="jumping"?5:8,Fps=action=="jumping"?1/.13:8,Loop=action!="jumping"};
                Hold(manifest.Animations["jumping"],hold);
                var skin=new SkinDefinition("skin-0123456789abcdef0123456789abcdef","unused",manifest);
                var clock=skin.CreateAnimation();clock.Play("jumping",.7);clock.Advance(.65+hold-.0001);
                Check(clock.State=="jumping","manifest completion includes final hold for persona alias");
                clock.Advance(.0002);Check(clock.State=="idle","jump completes once after final hold");
                var image=BitmapSource.Create(3840,520,96,96,PixelFormats.Bgra32,null,new byte[3840*520*4],3840*4);
                var clip=new MotionClip(image,5,.65,false,480,520,8);
                Check(clip.Sample(.129)==0 && clip.Sample(.13)==1 && clip.Sample(.519)==3 && clip.Sample(.52)==4 && clip.Sample(.99)==4,"pose sampler preserves .13 cadence and final pose");
            }
        }
        static void NativePixels()
        {
            var type=typeof(SkinCatalog).Assembly.GetType("Dafeiyu.WpfOfficialNativeValidator");Check(type!=null,"official installation has a native validation adapter");
            var method=type.GetMethod("ValidatePng",BindingFlags.Static|BindingFlags.NonPublic);
            Check(method!=null,"native adapter exposes bounded raster materialization");
            string path=Path.Combine(Path.GetTempPath(),"deskpet-native-"+Guid.NewGuid().ToString("N")+".png");
            try {
                foreach(bool visible in new[]{false,true}) {
                    var pixels=new byte[480*520*4];if(visible){pixels[3]=255;pixels[0]=100;}
                    var image=BitmapSource.Create(480,520,96,96,PixelFormats.Bgra32,null,pixels,480*4);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(path))png.Save(output);
                    bool rejected=false;
                    try{method.Invoke(null,new object[]{path,480,520,1,1,true,System.Threading.CancellationToken.None});}
                    catch(TargetInvocationException e){if(!(e.InnerException is InvalidDataException))throw;rejected=true;}
                    Check(rejected!=visible,visible?"native visible transparent frame is usable":"native fully transparent frame is rejected");
                }
                var broken=File.ReadAllBytes(path);int position=8;
                while(position<broken.Length){int size=broken[position]<<24|broken[position+1]<<16|broken[position+2]<<8|broken[position+3];if(Encoding.ASCII.GetString(broken,position+4,4)=="IDAT"){broken[position+8]=0;uint crc=0xffffffff;for(int i=position+4;i<position+8+size;i++){crc^=broken[i];for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}crc^=0xffffffff;for(int i=0;i<4;i++)broken[position+8+size+i]=(byte)(crc>>(24-i*8));break;}position+=size+12;}
                File.WriteAllBytes(path,broken);bool corruptRejected=false;
                try{method.Invoke(null,new object[]{path,480,520,1,1,true,CancellationToken.None});}catch(TargetInvocationException error){if(!(error.InnerException is InvalidDataException))throw;corruptRejected=true;}
                Check(corruptRejected,"native decoder rejects corrupt IDAT even with repaired chunk CRC");
            }finally{if(File.Exists(path))File.Delete(path);}
        }
        static void CatalogTrust()
        {
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"deskpet-official-test-"+Guid.NewGuid().ToString("N"));
            try {
                var catalog=new SkinCatalog(data);Check(catalog.Skins.Count==5,"thin catalog keeps all five official entries without optional full resources");
                bool fallbackReserved=false;try{catalog.Remove(catalog.FindOfficial("e-bao"));}catch(InvalidOperationException){fallbackReserved=true;}Check(fallbackReserved,"official Ebao fallback cannot be hidden or deleted");
                var key=typeof(SkinDefinition).GetProperty("PickerKey");Check(key!=null,"catalog separates picker provenance from persona identity");
                foreach(var skin in catalog.Skins)Check((string)key.GetValue(skin,null)=="official:"+skin.BuiltinCharacterId,"official picker uses fixed provenance identity");
                string source=Path.Combine(data,"fixture");Directory.CreateDirectory(source);
                WriteNative(Path.Combine(source,"action.png"),8,1,8);WriteNative(Path.Combine(source,"gaze.png"),4,4,16);WriteNative(Path.Combine(source,"preview.png"),1,1,1);
                var manifest=new SkinManifest{Version=2,PackageId="dafeiyu",Name="自定义",Format="illustrated-atlases-v1",CellWidth=480,CellHeight=520,Preview="preview.png",Gaze=new IllustratedGaze{File="gaze.png",Columns=4,Frames=16,Interpolation="none",Order="clockwise-from-up"},Animations=new Dictionary<string,IllustratedAnimation>()};
                foreach(string action in IllustratedPack.Actions)manifest.Animations[action]=new IllustratedAnimation{File="action.png",Columns=8,Frames=8,Fps=8,Loop=action!="jumping"};
                string json=Path.Combine(source,"pet.json");using(var output=File.Create(json))SkinCatalog.Serializer().WriteObject(output,manifest);
                var custom=catalog.Import(json);Check(custom.BuiltinCharacterId==null,"self-declared official ID does not confer provenance");
                Check((string)key.GetValue(custom,null)=="custom-pack:dafeiyu","custom package has its own picker identity");
                var official=catalog.Skins[1];Check(official.BuiltinCharacterId=="dafeiyu" && official.Id!=custom.Id,"new custom collision retains official entry and allocates distinct persona");
                Check(new SkinCatalog(data).Skins.Count==6,"custom and official survive reload independently");
                string oldData=Path.Combine(data,"old");Directory.CreateDirectory(oldData);string personal=Path.Combine(oldData,"settings.json");File.WriteAllText(personal,"preserved personal settings");
                CompanionPackStorage.Install(source,oldData,"dafeiyu",IllustratedPack.Files(manifest),folder=>IllustratedPack.Validate(folder,manifest,true));
                var previous=new SkinCatalog(oldData);var retained=previous.Skins.Single(s=>!s.IsOfficial);
                Check(retained.Id=="dafeiyu"&&previous.FindOfficial("dafeiyu").Id.StartsWith("skin-",StringComparison.Ordinal),"preexisting untrusted custom preserves exact persona while official gets separate alias");
                Check(previous.Find("dafeiyu")==retained&&File.ReadAllText(personal)=="preserved personal settings","legacy selected custom ID resolves without rewriting user settings");
                previous.Remove(retained);Check(new SkinCatalog(oldData).FindOfficial("dafeiyu")!=null,"hiding a spoofed custom package does not hide official placeholder");
                var inventory=OfficialJson.Object(OfficialJson.Parse(Encoding.UTF8.GetBytes(OfficialCompanionLegacyData.Json),65536));
                var record=OfficialJson.Array(inventory["roles"]).Select(OfficialJson.Object).Single(r=>(string)r["id"]=="xiaobei");
                string corrupt=Path.Combine(data,"bundled-skins","xiaobei-"+((string)record["cache_version"]).Substring(0,24));Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"pet.json"),new string('x',65537));
                Check(new SkinCatalog(data).FindOfficial("e-bao")!=null,"oversized corrupt canonical legacy metadata cannot abort catalog fallback");
            }finally{if(Directory.Exists(data))Directory.Delete(data,true);}
        }
        static void WriteNative(string path,int columns,int rows,int count)
        {
            int width=columns*480,height=rows*520;var pixels=new byte[width*height*4];
            for(int i=0;i<count;i++){int p=(i/columns*520*width+i%columns*480)*4;pixels[p]=100;pixels[p+3]=255;}
            var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(path))encoder.Save(output);
        }
        static void ThinStartupAndGuide(string legacyAssets)
        {
            Check(typeof(PetWindow).Assembly.GetManifestResourceStream("Dafeiyu.Atlas")==null,"thin acceptance really excludes Dafeiyu full default artwork");
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tu",Guid.NewGuid().ToString("N").Substring(0,8));
            try {
                var settings=new PetSettings{SkinId="dafeiyu",SystemPrompt="保留原伙伴人格",PersonaProfileVersion=1,AutoUpdate=false,GreetingEnabled=false,Wander=false,FollowCursor=false,HideOnFullscreen=false,SaveChatHistory=false};
                foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
                Check(pet.Skin.BuiltinCharacterId=="e-bao"&&settings.PendingOfficialCompanionId==""&&settings.SelectedCompanionKey=="official:dafeiyu","thin startup preserves legacy selected role without pending network intent");
                Check(settings.PersonaOverrides["dafeiyu"]=="保留原伙伴人格","startup fallback preserves previous persona before activation");
                Check(pet.Catalog.Skins.Count(s=>s.IsPlaceholder)==4,"thin startup has exactly four download placeholders");
                foreach(var skin in pet.Catalog.Skins)Check(SkinCatalog.GetAvatar(skin)!=null,"every thin official avatar decodes without default role artwork");
                settings.OnboardingLessonId="pet.select";pet.ShowOnboarding();Pump();
                var guide=(OnboardingWindow)typeof(PetWindow).GetField("guide",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                Check(guide!=null&&guide.IsVisible,"first guide opens on the companion lesson");
                string before=settings.SkinId,picker=settings.SelectedCompanionKey,pending=settings.PendingOfficialCompanionId,prompt=settings.SystemPrompt;
                Check(!pet.SelectSkin(pet.Catalog.FindOfficial("marfu")),"guide companion attempt is notice-only");
                pet.DownloadAllOfficialAsync(guide).GetAwaiter().GetResult();
                Check(settings.SkinId==before&&settings.SelectedCompanionKey==picker&&settings.PendingOfficialCompanionId==pending&&settings.SystemPrompt==prompt,"guide attempts preserve selection persona and pending restore");
                Check(pet.CurrentCompanionSelection==null&&!app.Windows.OfType<OfficialCompanionProgressWindow>().Any(),"guide attempts never start a selection or download UI");
                Check(Walk(guide).OfType<Button>().Single(b=>b.Name=="GuideNext").IsEnabled,"notice-only companion lesson remains advanceable");
                guide.Close();Pump();Check(pet.SelectSkin(pet.Skin),"explicit already-visible Ebao selection succeeds");
                Check(settings.PendingOfficialCompanionId=="","explicit Ebao selection clears older automatic restore");
                NativeSelectionRaces(pet);BulkRecoveryReaderLifetime();
                if(legacyAssets!=null)LegacyCacheWithoutEmbeddedPngs(legacyAssets.Substring("--legacy-assets=".Length));
            }finally {
                if(pet!=null){pet.Close();Pump();}foreach(var window in app.Windows.Cast<Window>().ToArray())window.Close();Pump();app.Shutdown();
                if(Directory.Exists(data))Directory.Delete(data,true);
            }
        }
        static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
        {yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Walk(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Pump()
        {var until=DateTime.UtcNow.AddMilliseconds(350);while(DateTime.UtcNow<until){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);System.Threading.Thread.Sleep(5);}}
        static void Wait(Task task)
        {var end=DateTime.UtcNow.AddSeconds(60);while(!task.IsCompleted&&DateTime.UtcNow<end)Pump();Check(task.IsCompleted,"controlled asynchronous operation completes within bounded test time");task.GetAwaiter().GetResult();}
        static FieldInfo Field(string name){return typeof(PetWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);}
        static void BulkRecoveryReaderLifetime()
        {
            var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            using(var fixture=new OfficialCompanionFixture())using(var transport=fixture.Transport())
            using(var service=new OfficialCompanionDownloads(fixture.Root,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,(i,c)=>Task.FromResult(0),OfficialCompanionReadMode.Snapshots)){
                fixture.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=NativePackage("dafeiyu",100);
                var install=service.EnsureAsync("dafeiyu",new OfficialDownloadRouting(false,"",false,""),OfficialCompanionCheckMode.Force,null,CancellationToken.None);Wait(install);Wait(service.HistoryMaintenance);
                var original=install.Result.Installation;
                var pet=new PetWindow(null,new PetSettings{SkinId="e-bao",SelectedCompanionKey="official:e-bao",OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false},new SettingsStore(Path.Combine(fixture.Root,"settings.json")),null);
                var native=(WpfOfficialNativeValidator)Field("officialNative").GetValue(pet);
                var gate=(SemaphoreSlim)typeof(WpfOfficialNativeValidator).GetField("gate",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(native);
                var waiters=typeof(SemaphoreSlim).GetField("m_asyncHead",BindingFlags.NonPublic|BindingFlags.Instance);
                Task recovery=null;bool held=false;
                try{
                    var previous=(OfficialCompanionDownloads)Field("officialDownloads").GetValue(pet);if(previous!=null)previous.Dispose();Field("officialDownloads").SetValue(pet,service);Field("officialStartupStarted").SetValue(pet,true);
                    string selected=pet.Settings.SelectedCompanionKey;gate.Wait();held=true;
                    recovery=(Task)typeof(PetWindow).GetMethod("RefreshBulkInstallations",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(pet,new object[]{pet.Skin,0L,null});
                    var until=DateTime.UtcNow.AddSeconds(10);while(waiters.GetValue(gate)==null&&!recovery.IsCompleted&&DateTime.UtcNow<until)Pump();
                    Check(waiters.GetValue(gate)!=null&&!recovery.IsCompleted,"actual WPF bulk recovery is queued at native validation semaphore");
                    fixture.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=NativePackage("dafeiyu",112);
                    var replace=service.EnsureAsync("dafeiyu",new OfficialDownloadRouting(false,"",false,""),OfficialCompanionCheckMode.Force,null,CancellationToken.None,true,true);Wait(replace);Wait(service.ScheduleMaintenance());
                    Check(replace.Result.Installation.RootDirectory!=original.RootDirectory&&File.Exists(original.PetManifestPath),"WPF bulk native reader keeps exact old root through concurrent publication and cleanup");
                    gate.Release();held=false;Wait(recovery);Wait(service.HistoryMaintenance);
                    var validated=(System.Collections.Generic.HashSet<string>)typeof(WpfOfficialNativeValidator).GetField("validated",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(native);
                    Check(validated.Contains(original.Asset.Id+":"+original.Asset.Sha256),"WPF bulk recovery finishes actual native decoding of its captured generation");
                    Check(!Directory.Exists(original.RootDirectory)&&Directory.Exists(replace.Result.Installation.RootDirectory)&&pet.Settings.SelectedCompanionKey==selected,"WPF bulk reader release reclaims old root without changing selected role");
                }finally{if(held)gate.Release();if(recovery!=null)Wait(recovery);pet.Close();Pump();}
            }
        }
        static void NativeSelectionRaces(PetWindow pet)
        {
            using(var fixture=new OfficialCompanionFixture())using(var transport=fixture.Transport()) {
                foreach(string id in OfficialCompanionFixture.Ids)fixture.Files[OfficialCompanionFixture.PackageName(id)]=NativePackage(id,100);
                var old=(OfficialCompanionDownloads)Field("officialDownloads").GetValue(pet);if(old!=null)old.Dispose();
                var native=(WpfOfficialNativeValidator)Field("officialNative").GetValue(pet);
                using(var service=new OfficialCompanionDownloads(pet.Store.DirectoryPath,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,native.ValidateAsync)) {
                    Field("officialDownloads").SetValue(pet,service);Field("officialRequest").SetValue(pet,0L);Field("officialStartupStarted").SetValue(pet,true);
                    var started=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();bool hold=true,failMarfu=false;
                    fixture.Intercept=async (request,cancel)=>{
                        if(request.RequestUri.AbsolutePath.EndsWith(OfficialCompanionFixture.PackageName("dafeiyu"),StringComparison.Ordinal)&&hold){started.TrySetResult(true);await release.Task.ConfigureAwait(false);}
                        if(request.RequestUri.AbsolutePath.EndsWith(OfficialCompanionFixture.PackageName("marfu"),StringComparison.Ordinal)&&failMarfu)return OfficialCompanionFixture.Response(new byte[0],HttpStatusCode.NotFound);
                        return null;
                    };
                    var selection=pet.RequestSkinAsync(pet.Catalog.FindOfficial("dafeiyu"),CompanionSelectionOrigin.StartupRestore,null);Wait(started.Task);
                    Check(pet.Skin.BuiltinCharacterId=="e-bao"&&pet.Settings.PendingOfficialCompanionId==""&&pet.CurrentCompanionSelection.PickerKey=="official:dafeiyu","missing request retains old visible source with transient download state");
                    var shared=service.UpdateInBackgroundAsync("dafeiyu",new OfficialDownloadRouting(false,"",false,""),CancellationToken.None);
                    pet.Catalog.Remove(pet.Catalog.FindOfficial("dafeiyu"));Check(pet.Settings.PendingOfficialCompanionId==""&&!pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu"),"hiding pending noncurrent role invalidates restore intent without cancelling shared installation");
                    Check(pet.SelectSkin(pet.Skin),"already-visible Ebao supersedes in-flight requested role");hold=false;release.TrySetResult(true);Wait(selection);Wait(shared);
                    Check(service.Installed("dafeiyu")!=null&&pet.Skin.BuiltinCharacterId=="e-bao"&&pet.Settings.PendingOfficialCompanionId=="","late completed package installs without taking over latest explicit choice");
                    Check(!pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu")&&new SettingsStore(Path.Combine(pet.Store.DirectoryPath,"settings.json")).Load().PendingOfficialCompanionId=="","late hidden-role completion cannot unhide or resurrect pending selection after restart");
                    pet.Catalog.RestoreRemoved();pet.Catalog.PreserveActive(pet.Skin);
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("dafeiyu"),CompanionSelectionOrigin.StartupRestore,null));
                    Check(pet.Skin.BuiltinCharacterId=="dafeiyu"&&!pet.Skin.Embedded,"native installed full package activates through manifest path");
                    var selected=pet.Skin;string persona=pet.Settings.SkinId,key=pet.Settings.SelectedCompanionKey;long token=(long)Field("officialRequest").GetValue(pet);
                    var unavailable=pet.Holidays.Catalog.Find("dafeiyu","halloween-2026");pet.Holidays.Catalog.ReportUnavailable(unavailable);
                    failMarfu=true;Wait(pet.DownloadAllOfficialAsync(null));
                    Check(pet.Skin.Source==selected.Source&&pet.Settings.SkinId==persona&&pet.Settings.SelectedCompanionKey==key&&(long)Field("officialRequest").GetValue(pet)==token,"partial bulk preserves current root selection persona and ownership token");
                    Check(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026").Ready&&!ReferenceEquals(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026"),unavailable)&&pet.Skin.NativeSourceKey!=selected.NativeSourceKey,"partial bulk same-root verified repair replaces rejected catalog and native source generation");
                    Check(!pet.Catalog.FindOfficial("gugugaga").IsPlaceholder&&pet.Catalog.FindOfficial("marfu").IsPlaceholder,"partial bulk publishes successful roles and leaves missing role retryable");
                    failMarfu=false;Wait(pet.DownloadAllOfficialAsync(null));Check(pet.Catalog.Skins.Count(s=>s.IsPlaceholder)==0,"bulk retry completes missing role without losing finished packages");
                    string oldSource=pet.Skin.Source;var oldVisual=pet.Skin;var oldInstallation=service.Installed("dafeiyu");var oldOutfit=pet.Holidays.Catalog.Find("dafeiyu","halloween-2026");
                    PersonaProfiles.Save(pet.Settings,pet.Skin,"保留当前自定义人格");pet.Settings.VoiceName="private-test-voice";PersonaProfiles.SaveVoices(pet.Settings);
                    var preferences=(SettingsPage)Field("preferences").GetValue(pet);if(preferences!=null)typeof(SettingsPage).GetMethod("RefreshAvatar",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(preferences,null);
                    fixture.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=NativePackage("dafeiyu",190);
                    Field("completionUntil").SetValue(pet,Double.MaxValue);Wait(pet.DownloadAllOfficialAsync(null));
                    Check(pet.Skin.Source==oldSource&&ReferenceEquals(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026"),oldOutfit),"busy bulk source update retains old immutable current default and holiday objects");
                    typeof(PetWindow).GetMethod("QueueOfficial",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{oldVisual,token,false});
                    Field("completionUntil").SetValue(pet,0d);typeof(PetWindow).GetMethod("TryApplyDeferredOfficial",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                    var commitDeadline=DateTime.UtcNow.AddSeconds(30);while(pet.Skin.Source==oldSource&&DateTime.UtcNow<commitDeadline)Pump();
                    Check(pet.Skin.Source!=oldSource&&pet.Settings.SkinId==persona&&pet.Settings.SelectedCompanionKey==key&&pet.Settings.SystemPrompt=="保留当前自定义人格"&&pet.Settings.VoiceName=="private-test-voice","safe same-role update refreshes source without persona voice or history activation");
                    var shown=(MotionClip)Field("shownMotion").GetValue(pet);int sample=(int)Field("shownSample").GetValue(pet);
                    Check(shown!=null&&shown.AlphaAt(sample,11,0)>16&&shown.AlphaAt(sample,0,0)==0,"source refresh publishes new frame and matching hit mask rather than stale same-persona cache");
                    Check(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026").SourceKey!=oldOutfit.SourceKey,"same outfit version uses refreshed immutable source identity");
                    var delayed=(Task<SkinDefinition>)typeof(PetWindow).GetMethod("PrepareOfficialAsync",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(OfficialCompanionInstallation),typeof(bool)},null).Invoke(pet,new object[]{oldInstallation,false});Wait(delayed);
                    Check(delayed.Result.Source==pet.Skin.Source,"delayed older background result cannot replace newer bulk source cache for unchanged selection token");
                    var cache=(System.Collections.IDictionary)Field("officialCatalogs").GetValue(pet);Check(cache.Count<=6,"catalog cache bounds retired sources across updates");
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("e-bao"),CompanionSelectionOrigin.StartupRestore,null));
                    string eSource=pet.Skin.Source,hiddenPersona=pet.Catalog.FindOfficial("dafeiyu").Id;
                    Check(!pet.Skin.Embedded&&pet.Skin.OfficialInstallation!=null,"bulk-installed newer Ebao is a valid selected external source");
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("dafeiyu"),CompanionSelectionOrigin.StartupRestore,null));
                    fixture.Files[OfficialCompanionFixture.PackageName("e-bao")]=NativePackage("e-bao",210);
                    Console.WriteLine("TRACE switched-role metadata fixture setup");var metadataEntered=new TaskCompletionSource<bool>();var metadataRelease=new TaskCompletionSource<bool>();bool holdMetadata=true;var originalIntercept=fixture.Intercept;
                    fixture.Intercept=async(request,cancel)=>{if(request.RequestUri.Host=="api.github.com"&&holdMetadata){metadataEntered.TrySetResult(true);await metadataRelease.Task.ConfigureAwait(false);}return originalIntercept==null?null:await originalIntercept(request,cancel).ConfigureAwait(false);};
                    Console.WriteLine("TRACE opening preparation guide");pet.Settings.OnboardingState="deferred";pet.Settings.OnboardingLessonId="pet.packages";pet.ShowOnboarding();Pump();var bulkGuide=(OnboardingWindow)Field("guide").GetValue(pet);
                    Walk(bulkGuide).OfType<Button>().Single(button=>button.Name=="GuidePreparePacks").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var batch=pet.OfficialPreparation.StartAsync();Console.WriteLine("TRACE waiting for forced metadata");Wait(metadataEntered.Task);
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("e-bao"),CompanionSelectionOrigin.Onboarding,null));Check(pet.Skin.Source==eSource&&bulkGuide.IsVisible,"pending full refresh allows cached last-choice role while guidance continues");
                    string selectedPersona=pet.Skin.Id;var selectedHistory=pet.Conversation.History;
                    Console.WriteLine("TRACE releasing forced metadata");holdMetadata=false;metadataRelease.TrySetResult(true);Wait(batch);var reconcileDeadline=DateTime.UtcNow.AddSeconds(15);while(pet.Skin.Source==eSource&&DateTime.UtcNow<reconcileDeadline)Pump();
                    Check(pet.OfficialPreparation.State==OfficialCompanionPreparationState.Completed&&pet.Skin.Source!=eSource&&pet.Skin.BuiltinCharacterId=="e-bao"&&pet.Skin.Id==selectedPersona&&Object.ReferenceEquals(pet.Conversation.History,selectedHistory),"bulk completion hotloads the last cached role selected during metadata wait without changing persona or history");
                    eSource=pet.Skin.Source;bulkGuide.Close();Pump();fixture.Intercept=originalIntercept;
                    pet.Catalog.Remove(pet.Catalog.FindOfficial("dafeiyu"));Wait(pet.DownloadAllOfficialAsync(null));
                    Check(!pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu")&&pet.Catalog.FindOfficial("dafeiyu").Id==hiddenPersona,"bulk refreshes hidden official registry without unhiding role or losing persona");
                    var reloaded=new SkinCatalog(pet.Store.DirectoryPath);Check(!reloaded.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu")&&reloaded.FindOfficial("dafeiyu").Id==hiddenPersona,"hidden official identity persists across catalog restart");
                    if(preferences!=null){preferences.RefreshSkinChoices();((Button)preferences.FindControl("RestoreCompanions")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                    Check(pet.Skin.Source==eSource&&pet.Catalog.FindOfficial("e-bao").Source==eSource&&pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu"),"explicit restore unhides role while retaining selected installed Ebao source");
                    // Even a legacy/manual hidden flag cannot remove the required fallback.
                    CompanionPackStorage.SetHidden(pet.Store.DirectoryPath,"official:e-bao",true);Check(new SkinCatalog(pet.Store.DirectoryPath).Skins.Any(s=>s.BuiltinCharacterId=="e-bao"),"persisted Ebao hidden flag cannot remove offline fallback");CompanionPackStorage.SetHidden(pet.Store.DirectoryPath,"official:e-bao",false);
                    var fallback=pet.Catalog.EmbeddedFallback();Wait(pet.RequestSkinAsync(fallback,CompanionSelectionOrigin.StartupRestore,null));Check(pet.Skin.Source==eSource&&!pet.Skin.Embedded,"cached embedded Ebao option prefers receipt-verified installed source without metadata delay");
                    foreach(var window in Application.Current.Windows.OfType<OfficialCompanionProgressWindow>().ToArray())window.Close();
                }
                Field("officialDownloads").SetValue(pet,null);
            }
        }
        internal static byte[] NativePackage(string id,byte color,bool coldAction=false)
        {
            return OfficialCompanionFixture.Package(id,files=>{
                foreach(string path in files.Keys.Where(k=>k.EndsWith(".png",StringComparison.Ordinal)).ToArray()) {
                    int columns=path.EndsWith("gaze.png",StringComparison.Ordinal)?4:path.EndsWith("preview.png",StringComparison.Ordinal)?1:8;
                    files[path]=NativePng(columns,columns==4?4:1,columns==4?16:columns,color);
                }
                var manifest=OfficialJson.Object(OfficialJson.Parse(files["pet.json"],65536));manifest["avatar"]="avatar.png";if(coldAction){var running=OfficialJson.Object(OfficialJson.Object(manifest["animations"])["running"]);files["cold.png"]=files[(string)running["file"]];running["file"]="cold.png";}files["pet.json"]=OfficialCompanionFixture.Json(manifest);
                var pixels=new byte[64*64*4];pixels[0]=color;pixels[3]=255;var image=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,pixels,64*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=new MemoryStream()){encoder.Save(output);files["avatar.png"]=output.ToArray();}
            });
        }
        static byte[] NativePng(int columns,int rows,int count,byte color)
        {
            int width=480*columns,height=520*rows;var pixels=new byte[width*height*4];for(int i=0;i<count;i++){int p=(i/columns*520*width+i%columns*480+(color==190?11:0))*4;pixels[p]=color;pixels[p+3]=255;}
            var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=new MemoryStream()){encoder.Save(output);return output.ToArray();}
        }
        static void CanonicalStoreOwnership(string assets)
        {
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"co",Guid.NewGuid().ToString("N").Substring(0,8));
            try {
                string source=Path.Combine(assets,"gugugaga");SkinManifest canonical;using(var input=File.OpenRead(Path.Combine(source,"pet.json")))canonical=SkinCatalog.ReadManifest(input);
                CompanionPackStorage.Install(source,data,"gugugaga",IllustratedPack.Files(canonical),folder=>IllustratedPack.Validate(folder,canonical,true));
                var catalog=new SkinCatalog(data);var official=catalog.FindOfficial("gugugaga");
                Check(official.IsOfficial&&official.Id=="gugugaga"&&!catalog.Skins.Any(s=>s.PickerKey=="custom-pack:gugugaga"),"pre-upgrade canonical stored package is trusted ownership rather than custom ownership");
                var personal=new PetSettings();PersonaProfiles.Activate(personal,official);PersonaProfiles.Save(personal,official,"保留官方旧包人格");personal.VoiceName="old-official-private-voice";PersonaProfiles.SaveVoices(personal);
                string replacement=Path.Combine(data,"replacement");foreach(string path in IllustratedPack.Files(canonical)){string destination=Path.Combine(replacement,path);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(Path.Combine(source,path),destination);}
                canonical.SystemPrompt+="这是新导入的非官方自定义设计。";string json=Path.Combine(replacement,"pet.json");using(var output=File.Create(json))SkinCatalog.Serializer().WriteObject(output,canonical);
                var custom=catalog.Import(json);
                Check(custom.PickerKey=="custom-pack:gugugaga"&&custom.Id!=official.Id&&!custom.IsOfficial,"new custom replacement of canonical stored package receives a separate persisted persona");
                PersonaProfiles.Activate(personal,custom);PersonaProfiles.Save(personal,custom,"保留自定义人格");PersonaProfiles.Activate(personal,official);
                Check(personal.SystemPrompt=="保留官方旧包人格"&&personal.VoiceName=="old-official-private-voice","custom replacement cannot take over prior official persona or voice profile");
                catalog.Remove(custom);catalog=new SkinCatalog(data);var updated=catalog.Import(json);var reopened=new SkinCatalog(data);
                Check(updated.Id==custom.Id&&reopened.FindPicker(updated.PickerKey).Id==custom.Id&&reopened.FindOfficial("gugugaga").Id==official.Id,"hidden custom update and restart retain separate official and custom identities");
            }finally{if(Directory.Exists(data))Directory.Delete(data,true);}
        }
        static void LegacyCacheWithoutEmbeddedPngs(string assets)
        {
            CanonicalStoreOwnership(assets);
            var inventory=OfficialJson.Object(OfficialJson.Parse(Encoding.UTF8.GetBytes(OfficialCompanionLegacyData.Json),65536));
            foreach(string id in new[]{"xiaobei","gugugaga"}) {
                string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lg",Guid.NewGuid().ToString("N").Substring(0,8));PetWindow previous=null;
                try {
                    var record=OfficialJson.Array(inventory["roles"]).Select(OfficialJson.Object).Single(r=>(string)r["id"]==id);
                    string cache=Path.Combine(data,"bundled-skins",id+"-"+((string)record["cache_version"]).Substring(0,24));
                    string source=Path.Combine(assets,id);foreach(string path in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {
                        string relative=path.Substring(source.Length+1);if(Path.GetExtension(relative)!=".png"&&relative!="pet.json")continue;
                        string destination=Path.Combine(cache,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(path,destination);
                    }
                    string alias="skin-"+Guid.NewGuid().ToString("N");CompanionPackStorage.RetainIdentity(data,id,alias);
                    var settings=new PetSettings{SkinId=alias,AutoUpdate=false,GreetingEnabled=false,Wander=false,SaveChatHistory=false};
                    foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                    previous=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
                    Check(previous.Skin.BuiltinCharacterId==id&&previous.Skin.Id==alias&&previous.Skin.Source==cache,"thin upgrade retains native-validated canonical old cache and persona alias for "+id);
                    Check(!previous.Skin.Embedded&&!previous.Skin.Bundled&&!previous.Skin.IsPlaceholder&&String.IsNullOrEmpty(settings.PendingOfficialCompanionId),"old default cache stays manifest-owned usable source without premature fallback for "+id);
                    Check(previous.Skin.Load().HasGaze&&SkinCatalog.GetPreview(previous.Skin).PixelWidth==480,"retained old cache native gaze and preview remain readable for "+id);
                    Check(Field("officialDownloads").GetValue(previous)==null,"loading canonical cache needs no metadata or download for "+id);
                }finally{if(previous!=null){previous.Close();Pump();}if(Directory.Exists(data))Directory.Delete(data,true);}
            }
        }
    }
}
