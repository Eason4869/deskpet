using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    internal static class WpfCompanionPerformance
    {
        sealed class Offline:HttpMessageHandler
        {
            internal int Requests;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
            {Interlocked.Increment(ref Requests);await Task.Delay(Timeout.Infinite,cancel);throw new OperationCanceledException(cancel);}
        }
        static FieldInfo Field(string name){return typeof(PetWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);}
        static void Tick(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}
        static void Until(Func<bool> ready){var clock=Stopwatch.StartNew();while(!ready()){if(clock.Elapsed.TotalSeconds>30)throw new TimeoutException("Real-package switch did not complete.");Tick();}}
        static HashSet<object> Decoded()
        {
            var cache=typeof(WpfPreparedCompanion).GetField("cache",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            var entries=(IDictionary)cache.GetType().GetField("entries",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cache);
            return new HashSet<object>(entries.Values.Cast<object>().Select(e=>e.GetType().GetField("Value",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(e)));
        }
        static object Probe(OfficialCompanionDownloads downloads,string data)
        {
            var clock=Stopwatch.StartNew();var snapshot=downloads.CaptureLocalSnapshot("dafeiyu");double capture=clock.Elapsed.TotalMilliseconds;clock.Restart();
            SkinManifest manifest;using(var input=new MemoryStream(snapshot.ReadVerifiedBytes("pet.json")))manifest=SkinCatalog.ReadManifest(input);double manifestMs=clock.Elapsed.TotalMilliseconds;
            var skin=new SkinDefinition("dafeiyu",snapshot.Installation.RootDirectory,manifest){BuiltinCharacterId="dafeiyu",Snapshot=snapshot};
            clock.Restart();var bytes=snapshot.ReadVerifiedBytes(manifest.Gaze.File);double gazeVerify=clock.Elapsed.TotalMilliseconds;clock.Restart();
            var gaze=IllustratedPack.ReadImage(new MemoryStream(bytes),1920,2080);double gazeDecode=clock.Elapsed.TotalMilliseconds;clock.Restart();
            var atlas=new SpriteAtlas(skin);double gazeAtlas=clock.Elapsed.TotalMilliseconds;clock.Restart();
            var spec=manifest.Animations["idle"];bytes=snapshot.ReadVerifiedBytes(spec.File);double idleVerify=clock.Elapsed.TotalMilliseconds;clock.Restart();
            var idle=IllustratedPack.ReadImage(new MemoryStream(bytes),3840,520);double idleDecode=clock.Elapsed.TotalMilliseconds;clock.Restart();
            var motion=new MotionClip(idle,spec.Frames,spec.Frames/spec.Fps,spec.Loop,480,520,spec.Columns);double idleFrames=clock.Elapsed.TotalMilliseconds;clock.Restart();
            var wardrobe=new HolidayCatalog(snapshot);wardrobe.WithCharacter("dafeiyu",wardrobe);double wardrobeMs=clock.Elapsed.TotalMilliseconds;
            snapshot.Dispose();var result=new{captureMs=capture,manifestMs,gazeVerifyMs=gazeVerify,gazePngDecodeMs=gazeDecode,gazeAtlasIncludingSecondVerifiedDecodeMs=gazeAtlas,idleVerifyMs=idleVerify,idlePngDecodeMs=idleDecode,idleFrameMaskMs=idleFrames,wardrobeMs};
            File.WriteAllBytes(Path.Combine(data,"phase-probe.json"),OfficialJson.Bytes(result));Console.WriteLine("PHASE "+System.Text.Encoding.UTF8.GetString(OfficialJson.Bytes(result)));return result;
        }
        internal static void Run(string index,string output,string fixtures=null,bool phaseOnly=false,bool natural=false)
        {
            index=Path.GetFullPath(index);output=Path.GetFullPath(output);
            if(Directory.Exists(output)||File.Exists(output))throw new IOException("Benchmark needs a new owned output directory.");Directory.CreateDirectory(output);
            var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            if(OfficialCompanionBundle.Ebao==null||OfficialCompanionBundle.Ebao.Paths.Count()!=354)throw new InvalidDataException("Benchmark must embed the complete production Ebao bundle.");
            var rows=new List<object>();
            string marker=Path.Combine(fixtures??output,"performance-fixture-index.sha256");string identity=OfficialCompanionStorage.HashFile(index);
            if(fixtures!=null&&(!File.Exists(marker)||File.ReadAllText(marker)!=identity))throw new InvalidDataException("Only an explicitly marked owned benchmark fixture root may be reused.");
            if(fixtures==null)File.WriteAllText(marker,identity);
            foreach(bool olderE in new[]{false,true}){
                string data=Path.Combine(fixtures??output,olderE?"older-managed-E":"normal-bundle");Directory.CreateDirectory(data);
                var d=CompanionPerformancePackages.Install(data,index,"dafeiyu");if(olderE)CompanionPerformancePackages.Install(data,index,"e-bao");
                using(var handler=new Offline())using(var transport=new GitHubUpdates(new Uri("https://api.github.com/"),handler)){
                    int nativeCalls=0;var native=new WpfOfficialNativeValidator();var downloads=new OfficialCompanionDownloads(data,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,(installed,cancel)=>{Interlocked.Increment(ref nativeCalls);return native.ValidateAsync(installed,cancel);},OfficialCompanionReadMode.Snapshots);
                    // Reserve only background admission. Foreground local selection must succeed
                    // while audits/transfers cannot run, proving it does not await either.
                    var slots=(OfficialCompanionTransferAdmission)typeof(OfficialCompanionDownloads).GetField("slots",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(downloads);
                    using(var first=natural?null:slots.AcquireAsync(()=>false,CancellationToken.None).Result)using(var second=natural?null:slots.AcquireAsync(()=>false,CancellationToken.None).Result){
                        if(phaseOnly){Probe(downloads,output);downloads.Dispose();return;}
                        var settings=new PetSettings{OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,NotificationSoundsEnabled=false,UpdateUseMirror=false,UpdateUseProxy=false,HideOnFullscreen=false,Wander=false,FollowCursor=false,LastCompanionUpgradeVersion=ReleaseInfo.DisplayVersion};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                        var pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
                        try{
                            var previous=(OfficialCompanionDownloads)Field("officialDownloads").GetValue(pet);if(previous!=null)previous.Dispose();Field("officialDownloads").SetValue(pet,downloads);Field("officialStartupStarted").SetValue(pet,true);Field("officialFirstFrameRendered").SetValue(pet,true);
                            pet.Show();Tick();
                            foreach(string mode in new[]{"cold-decoded-source","warm-retained"})foreach(string target in new[]{"dafeiyu","e-bao"})for(int n=0;n<20;n++){
                                string other=target=="e-bao"?"dafeiyu":"e-bao";
                                Select(pet,other);Select(pet,target);Select(pet,other);
                                if(mode=="cold-decoded-source"){
                                    var skin=pet.Catalog.FindOfficial(target);WpfPreparedCompanion.Retire(skin.Snapshot==null?skin.NativeSourceKey:skin.Snapshot.Generation);
                                    ((IDictionary)Field("officialSources").GetValue(pet)).Remove(target);
                                    var snapshots=(List<OfficialCompanionSnapshot>)Field("retainedSnapshots").GetValue(pet);
                                    foreach(var old in snapshots.Where(s=>skin.Snapshot!=null&&s.Generation==skin.Snapshot.Generation).ToArray()){snapshots.Remove(old);old.Dispose();}
                                }
                                var before=Decoded();long hashes=downloads.Storage.VerificationCount;int natives=nativeCalls;var stages=new List<CompanionSelectionUpdate>();var observed=new Dictionary<string,double>();var clock=Stopwatch.StartNew();Action<CompanionSelectionUpdate> receive=u=>{observed[u.Stage]=clock.Elapsed.TotalMilliseconds;stages.Add(u);};pet.CompanionSelectionChanged+=receive;
                                var task=pet.RequestSkinAsync(pet.Catalog.FindOfficial(target),CompanionSelectionOrigin.Settings,null);double admission=clock.Elapsed.TotalMilliseconds;
                                Until(()=>task.IsCompleted);if(!task.GetAwaiter().GetResult())throw new InvalidOperationException("Real local selection rejected.");
                                Until(()=>stages.Any(u=>u.Stage=="completed"));pet.CompanionSelectionChanged-=receive;
                                var prepared=stages.Single(u=>u.Stage=="prepared");Console.WriteLine("SAMPLE "+olderE+" "+mode+" "+target+" "+n+" prepared="+prepared.Milliseconds+" admission="+admission);int decoded=Decoded().Count(v=>!before.Contains(v));
                                if(!natural&&nativeCalls!=natives)throw new InvalidOperationException("Gated background native validation unexpectedly ran.");
                                if(mode=="warm-retained"&&decoded!=0)throw new InvalidOperationException("Warm real-pack switch decoded new native resources: "+target);
                                var row=new{scenario=olderE?"older-managed-E":"normal-bundle",mode,target,sample=n+1,requestedMs=0,feedbackMs=observed["requested"],admissionMs=admission,preparedMs=observed["prepared"],committedMs=observed["committed"],firstRenderMs=observed["completed"],cacheHit=prepared.CacheHit,newDecodedEntries=decoded,allThreadFullPackHashes=downloads.Storage.VerificationCount-hashes,allThreadNativeValidations=nativeCalls-natives,networkWaits=0,backgroundRequestAttempts=handler.Requests};rows.Add(row);File.AppendAllText(Path.Combine(output,"samples.jsonl"),System.Text.Encoding.UTF8.GetString(OfficialJson.Bytes(row))+"\n");
                            }
                        }finally{pet.Close();Tick();downloads.Dispose();}
                    }
                }
            }
            File.WriteAllBytes(Path.Combine(output,"timings.json"),OfficialJson.Bytes(new{backend="Windows WPF actual OnRender completion",backgroundGated=!natural,definition="Cold clears target decoded/source caches in an already running process; OS filesystem cache is uncontrolled. Warm retains both complete sources. When backgroundGated=true transfer/audit slots are held; otherwise actual background auditing/maintenance may compete. Metadata transport intentionally unresolved; no real network. Full-hash/native counters include every thread, not foreground-only. Setup integrity/publication excluded.",index=index,bundleGeneration=OfficialCompanionBundle.Ebao.Generation,bundleZipSha256=OfficialCompanionBundle.Ebao.ZipSha256,samples=rows}));
            Console.WriteLine("PASS real WPF switching: "+rows.Count+" samples; "+output);
        }
        static void Select(PetWindow pet,string id){var task=pet.RequestSkinAsync(pet.Catalog.FindOfficial(id),CompanionSelectionOrigin.StartupRestore,null);Until(()=>task.IsCompleted);if(!task.Result)throw new InvalidOperationException("Benchmark setup selection failed: "+id);Tick();}
    }
}
