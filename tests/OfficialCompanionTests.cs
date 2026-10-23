using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class OfficialCompanionTests
    {
        public static int Main()
        {
            try {
                ProgressNoticeGate();Run().GetAwaiter().GetResult();Console.WriteLine("Official companion tests: "+passed+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL OfficialCompanion: "+error);return 1;}
        }
        static int passed;
        static void Check(bool result,string name){if(!result)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static readonly OfficialDownloadRouting Direct=new OfficialDownloadRouting(false,"",false,"");
        static void ProgressNoticeGate()
        {
            var property=typeof(OfficialCompanionProgress).GetProperty("RequiresUpdateNotice");
            Check(property!=null,"progress distinguishes actual package work from metadata/cache reuse");
            foreach(var stage in new[]{"metadata","download","validate","install"})
            {
                var progress=new OfficialCompanionProgress("dafeiyu",stage,"",0,100,stage=="metadata"?0:50);
                Check((bool)property.GetValue(progress,null)==(stage!="metadata"),"notice stage "+stage);
            }
            Check(!(bool)property.GetValue(new OfficialCompanionProgress("dafeiyu","install","",100,100,100),null),"unchanged cached installation makes no update notice");
        }
        static async Task Run()
        {
            using(var fixture=new OfficialCompanionFixture())using(var transport=fixture.Transport())using(var downloads=new OfficialCompanionDownloads(fixture.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,transport,(i,c)=>Task.FromResult(0))) {
                var result=await downloads.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);
                Check(result.InstalledNow&&result.MetadataChecked&&!result.UsedOfflineFallback&&File.Exists(result.Installation.PetManifestPath),"verified complete package is published and returned");
                Check(downloads.Installed("dafeiyu")!=null,"published receipt verifies offline");
                var again=await downloads.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);
                Check(!again.InstalledNow&&!again.MetadataChecked&&fixture.Packages==1&&fixture.Metadata==1,"cached feed and matching receipt skip redundant network and transfer");
                fixture.Offline=true;var offline=await downloads.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Force,null,CancellationToken.None);
                Check(offline.UsedOfflineFallback&&!offline.MetadataChecked&&!String.IsNullOrEmpty(offline.Warning),"failed refresh preserves offline package without claiming newest");
                fixture.Intercept=(r,c)=>{throw new HttpRequestException("private proxy address");};var disconnected=await downloads.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Force,null,CancellationToken.None);Check(disconnected.UsedOfflineFallback&&!disconnected.MetadataChecked&&!disconnected.Warning.Contains("private"),"network socket failure also retains verified offline package with sanitized warning");fixture.Intercept=null;
                File.AppendAllText(result.Installation.PetManifestPath," ");Check(downloads.Installed("dafeiyu")==null,"tampered installed file invalidates official receipt");
                fixture.Offline=false;await downloads.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Force,null,CancellationToken.None);Check(fixture.Packages==2&&downloads.Installed("dafeiyu")!=null,"tampered same-digest root is cleanly reinstalled");
                long a=downloads.BeginSelection(),b=downloads.BeginSelection();Check(!downloads.OwnsSelection(a)&&downloads.OwnsSelection(b),"superseded selection token cannot activate");
            }
            await MetadataSecurity();await Routing();await NativePublication();await Batch();await PackageSecurity();await FeedFallback();await Leases();await CacheAndRepublish();await PublicationRollback();await MetadataBudget();await StalePublication();await AppRoutingSnapshot();await FinalPathReadability();
        }
        static async Task Reject(Func<Task> action,string name)
        {bool rejected=false;try{await action();}catch(Exception error){if(!(error is ApiException||error is InvalidDataException||error is AggregateException))throw;rejected=true;}Check(rejected,name);}
        static async Task MetadataSecurity()
        {
            using(var f=new OfficialCompanionFixture())using(var t=new GitHubUpdates(new Uri("https://foreign-api.example/"),f)) {
                var snapshot=await new OfficialCompanionFeed(t,OfficialCompanionFixture.Version,UpdateChannel.Preview,Path.Combine(f.Root,"feed")).RefreshAsync(Direct,null,CancellationToken.None);
                Check(snapshot.Assets.Count==5&&f.Requests[0].StartsWith("https://api.github.com/repos/Eason4869/deskpet/releases?",StringComparison.Ordinal)&&!f.Requests.Any(x=>x.Contains("foreign-api.example")),"public custom updater API root cannot redirect trusted official metadata");
            }
            foreach(string mode in new[]{"size","digest","url","incomplete","incompatible","stable"})using(var f=new OfficialCompanionFixture())using(var t=f.Transport())using(var s=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,mode=="stable"?UpdateChannel.Stable:UpdateChannel.Preview,t,(i,c)=>Task.FromResult(0))) {
                f.WrongSize=mode=="size";f.WrongDigest=mode=="digest";f.ForeignUrl=mode=="url";f.IndexIncomplete=mode=="incomplete";f.AllIncompatible=mode=="incompatible";
                await Reject(async()=>{await s.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Force,null,CancellationToken.None);},"metadata rejects "+mode);
                Check(f.Packages==0,"untrusted metadata cannot start ZIP transfer: "+mode);
            }
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                var feed=new OfficialCompanionFeed(t,OfficialCompanionFixture.Version,UpdateChannel.Preview,Path.Combine(f.Root,"probe"));int pages=0;
                f.Intercept=(request,c)=>{if(request.RequestUri.Host!="api.github.com")return Task.FromResult<HttpResponseMessage>(null);pages++;return Task.FromResult(OfficialCompanionFixture.Response(OfficialCompanionFixture.Json(Enumerable.Range(0,20).Select(x=>new {tag_name="unknown-"+x}).ToArray())));};
                await Reject(async()=>{await feed.RefreshAsync(Direct,null,CancellationToken.None);},"bounded release scan stops on unrecognized releases");Check(pages==3,"release scan never exceeds three pages");
            }
        }
        static async Task Routing()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport())using(var s=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,(i,c)=>Task.FromResult(0))) {
                f.CorruptMirror=true;int proxy=0;t.AssetClientFactory=(enabled,address)=>{proxy++;return new HttpClient(new OfficialDelegateHandler(f.Dispatch));};
                var routing=new OfficialDownloadRouting(true," https://mirror.example/prefix/ ",true,"http://proxy.example");
                var routeKind=typeof(OfficialCompanionProgress).GetProperty("RouteKind");
                Check(routeKind!=null,"transport route identity must be independent of translated route text");
                var routes=new List<string>();
                var reporter=new OfficialProgress<OfficialCompanionProgress>(p=>{if(p.Stage=="download")routes.Add(routeKind.GetValue(p,null).ToString());});
                var result=await s.EnsureAsync("dafeiyu",routing,OfficialCompanionCheckMode.Cached,reporter,CancellationToken.None);
                Check(routes.Contains("Mirror")&&routes.Contains("Direct"),"mirror fallback reports explicit route identities without parsing display labels");
                Check(result.InstalledNow&&f.Packages==2,"mirror wrong digest falls back to verified direct bytes");
                Check(f.Requests.Any(x=>x.StartsWith("https://mirror.example/prefix/https://github.com/",StringComparison.Ordinal))&&proxy==1,"mirror address normalizes and assets bypass proxy while metadata uses it");
                Check(!Directory.GetFiles(f.Root,"*.part",SearchOption.AllDirectories).Any(),"successful fallback removes only owned partial files");
            }
        }
        static async Task NativePublication()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                var entered=new TaskCompletionSource<OfficialCompanionInstallation>();var release=new TaskCompletionSource<bool>();int calls=0;
                using(var s=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,async(i,c)=>{calls++;entered.TrySetResult(i);await release.Task;})) {
                    var task=s.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);var staging=await Timeout(entered.Task);
                    Check(staging.RootDirectory.Contains(".stage-")&&s.Installed("dafeiyu")==null&&!task.IsCompleted,"native callback observes staging and blocks pointer publication");
                    release.SetResult(true);var result=await Timeout(task);Check(!result.Installation.RootDirectory.Contains(".stage-")&&!Directory.Exists(staging.RootDirectory)&&calls==1,"returned installation binds final immutable root after asynchronous native validation");
                }
                using(var s=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,(i,c)=>{throw new FormatException("private path secret");})) {
                    await Reject(async()=>{await s.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);},"native codec failure remains a sanitized domain error");Check(s.Installed("dafeiyu")!=null,"failed process native validation preserves previous installed pointer");
                }
            }
        }
        static async Task Batch()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                int active=0,maximum=0;using(var s=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,async(i,c)=>{int n=Interlocked.Increment(ref active);int old;do{old=maximum;if(old>=n)break;}while(Interlocked.CompareExchange(ref maximum,n,old)!=old);await Task.Yield();Interlocked.Decrement(ref active);if(i.Asset.Id=="marfu")throw new NotSupportedException("secret codec path");})) {
                    long selection=s.BeginSelection();AggregateException failure=null;try{await s.EnsureAllAsync(Direct,null,CancellationToken.None);}catch(AggregateException error){failure=error;}
                    bool expected=failure!=null&&failure.InnerExceptions.Count==1&&failure.InnerExceptions[0].Message.StartsWith("marfu：")&&!failure.ToString().Contains("secret codec path");
                    if(!expected)throw new Exception("Unexpected bulk failures at fixture root length "+f.Root.Length+": "+(failure==null?"none":String.Join(" | ",failure.InnerExceptions.Select(e=>e.Message))));
                    Check(true,"bulk identifies failed role with sanitized codec reason");
                    Check(OfficialCompanionFixture.Ids.Count(id=>s.Installed(id)!=null)==4&&s.OwnsSelection(selection),"bulk keeps four completed installations and leaves selection unchanged");Check(maximum<=2,"native validations share the two-role concurrency bound");
                }
            }
        }
        static async Task PackageSecurity()
        {
            foreach(string mode in new[]{"traversal","backslash","case","symlink","palette","crc","missing","hold"})using(var f=new OfficialCompanionFixture())using(var t=f.Transport())using(var s=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,(i,c)=>Task.FromResult(0))) {
                f.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=OfficialCompanionFixture.Package("dafeiyu",files=>{
                    if(mode=="traversal")files["../escape.json"]=new byte[]{0};if(mode=="backslash")files["animations\\bad.png"]=files["preview.png"];if(mode=="case")files["PET.JSON"]=files["pet.json"];
                    if(mode=="palette")files["preview.png"]=OfficialCompanionFixture.Png(480,520,3);if(mode=="crc")files["preview.png"][29]^=1;if(mode=="missing")files.Remove("holidays/catalog.json");
                    if(mode=="hold"){var pet=OfficialJson.Object(OfficialJson.Parse(files["pet.json"],65536));var clip=OfficialJson.Object(OfficialJson.Object(pet["animations"])["idle"]);clip["hold_last_seconds"]=1;files["pet.json"]=OfficialCompanionFixture.Json(pet);}
                });
                if(mode=="symlink")f.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=OfficialCompanionFixture.WithSymlink(f.Files[OfficialCompanionFixture.PackageName("dafeiyu")]);
                await Reject(async()=>{await s.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);},"official package rejects "+mode);Check(s.Installed("dafeiyu")==null,"rejected package cannot publish: "+mode);
            }
        }
        static async Task FeedFallback()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                byte[] oldIndex=f.Index();var newer=OfficialJson.Object(OfficialJson.Parse(oldIndex,1024*1024));foreach(var entry in (object[])newer["companions"])if((string)OfficialJson.Object(entry)["id"]=="dafeiyu")OfficialJson.Object(entry)["min_app_version"]="99.0.0";byte[] newIndex=OfficialCompanionFixture.Json(newer);string newTag="companions-v0.10.4-dev.1";
                f.Intercept=(request,c)=>{if(request.RequestUri.Host=="api.github.com")return Task.FromResult(OfficialCompanionFixture.Response(OfficialCompanionFixture.Json(new[]{f.Release(newTag,newIndex),f.Release(OfficialCompanionFixture.Tag,oldIndex)})));if(request.RequestUri.AbsolutePath.EndsWith(".json"))return Task.FromResult(OfficialCompanionFixture.Response(request.RequestUri.AbsoluteUri.Contains(newTag)?newIndex:oldIndex));return Task.FromResult<HttpResponseMessage>(null);};
                var snapshot=await new OfficialCompanionFeed(t,OfficialCompanionFixture.Version,UpdateChannel.Preview,Path.Combine(f.Root,"feed")).RefreshAsync(Direct,null,CancellationToken.None);
                Check(snapshot.Assets["dafeiyu"].Tag==OfficialCompanionFixture.Tag&&snapshot.Assets["marfu"].Tag==newTag,"newer incompatible role falls back independently to older compatible complete snapshot");
            }
        }
        static async Task<T> Timeout<T>(Task<T> task){if(await Task.WhenAny(task,Task.Delay(10000))!=task)throw new Exception("test barrier timeout");return await task;}
        static async Task WaitUntil(Func<bool> condition){for(int i=0;i<1000;i++){if(condition())return;await Task.Delay(10);}throw new Exception("condition barrier timeout");}
        static int Subscribers(OfficialCompanionDownloads service)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;object gate=typeof(OfficialCompanionDownloads).GetField("gate",flags).GetValue(service);
            lock(gate){var work=(System.Collections.IDictionary)typeof(OfficialCompanionDownloads).GetField("installs",flags).GetValue(service);int total=0;foreach(object value in work.Values){var listeners=(System.Collections.IDictionary)value.GetType().GetField("Listeners",flags).GetValue(value);total+=listeners.Count;}return total;}
        }
        static async Task Canceled(Task task,string name){bool canceled=false;try{await task;}catch(OperationCanceledException){canceled=true;}Check(canceled,name);}
        static async Task Leases()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                var entered=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();
                using(var service=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,async(i,c)=>{entered.TrySetResult(true);await release.Task;}))using(var cancel=new CancellationTokenSource()) {
                    var first=service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,cancel.Token);await Timeout(entered.Task);
                    var second=service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);await WaitUntil(()=>Subscribers(service)==2);cancel.Cancel();await Canceled(first,"canceling one lease cancels only its wait");
                    release.TrySetResult(true);var installed=await Timeout(second);Check(installed.Installation!=null&&f.Packages==1,"remaining subscriber completes one shared transfer and install");
                }
            }
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                var entered=new TaskCompletionSource<bool>();var stopped=new TaskCompletionSource<bool>();bool block=true;
                using(var service=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,async(i,c)=>{if(block){entered.TrySetResult(true);using(c.Register(()=>stopped.TrySetResult(true)))await stopped.Task;c.ThrowIfCancellationRequested();}}))using(var cancel=new CancellationTokenSource()) {
                    var first=service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,cancel.Token);await Timeout(entered.Task);cancel.Cancel();await Canceled(first,"last subscriber cancellation preserves OperationCanceledException");await Timeout(stopped.Task);
                    await WaitUntil(()=>!Directory.GetDirectories(Path.Combine(f.Root,"official-companions","dafeiyu"),".*-*").Any());Check(service.Installed("dafeiyu")==null&&!Directory.GetFiles(f.Root,"*.part",SearchOption.AllDirectories).Any(),"last lease cancellation cleans staging and leaves no pointer");
                    block=false;var retry=await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);Check(retry.InstalledNow,"last canceled lease can retry cleanly");
                }
            }
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                var entered=new TaskCompletionSource<bool>();var stopped=new TaskCompletionSource<bool>();var service=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,async(i,c)=>{entered.TrySetResult(true);using(c.Register(()=>stopped.TrySetResult(true)))await stopped.Task;c.ThrowIfCancellationRequested();});
                var task=service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);await Timeout(entered.Task);service.Dispose();await Canceled(task,"service disposal cancels in-flight owned work");await Timeout(stopped.Task);await WaitUntil(()=>!Directory.GetDirectories(Path.Combine(f.Root,"official-companions","dafeiyu"),".*-*").Any());Check(!File.Exists(Path.Combine(f.Root,"official-companions","dafeiyu","current.txt")),"disposed service cannot publish after callback cancellation");
            }
        }
        static async Task CacheAndRepublish()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport())using(var service=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,(i,c)=>Task.FromResult(0))) {
                DateTime now=new DateTime(2026,10,23,0,0,0,DateTimeKind.Utc);service.UtcNow=()=>now;
                var first=await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);now=now.AddMinutes(6);await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);Check(f.Metadata==2&&f.Packages==1,"expired TTL refreshes metadata while identical ZIP is reused");
                byte[] index=f.Index();string tag="companions-v0.10.4-dev.1";f.Intercept=(r,c)=>Task.FromResult(r.RequestUri.Host=="api.github.com"?OfficialCompanionFixture.Response(OfficialCompanionFixture.Json(new[]{f.Release(tag,index)})):null);
                var second=await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Force,null,CancellationToken.None);Check(!second.InstalledNow&&f.Packages==1&&second.Installation.RootDirectory==first.Installation.RootDirectory,"same content published under another feed tag preserves immutable active root");
                f.Intercept=null;f.Offline=true;await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.AfterApplicationUpdate,null,CancellationToken.None);string marker=Path.Combine(f.Root,"official-companions","last-checked-app-version.txt");Check(!File.Exists(marker),"failed app-upgrade metadata check leaves success marker absent");
                f.Offline=false;await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.AfterApplicationUpdate,null,CancellationToken.None);int requests=f.Metadata;var cached=await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.AfterApplicationUpdate,null,CancellationToken.None);Check(File.ReadAllText(marker)==OfficialCompanionFixture.Version&&f.Metadata==requests&&!cached.MetadataChecked,"successful app-upgrade refresh marks version once and shares process result");
            }
        }
        static async Task PublicationRollback()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport())using(var service=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,(i,c)=>Task.FromResult(0))) {
                var old=await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);string pointer=Path.Combine(f.Root,"official-companions","dafeiyu","current.txt"),before=File.ReadAllText(pointer);
                f.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=OfficialCompanionFixture.Package("dafeiyu",files=>files["README.txt"]=new byte[]{65});byte[] index=f.Index();string tag="companions-v0.10.4-dev.1";f.Intercept=(r,c)=>Task.FromResult(r.RequestUri.Host=="api.github.com"?OfficialCompanionFixture.Response(OfficialCompanionFixture.Json(new[]{f.Release(tag,index)})):null);
                service.Storage.Commit=(a,b)=>{throw new IOException("simulated private atomic failure");};await Reject(async()=>{await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Force,null,CancellationToken.None);},"atomic pointer failure faults new installation");Check(File.ReadAllText(pointer)==before&&service.Installed("dafeiyu").RootDirectory==old.Installation.RootDirectory,"failed atomic commit preserves old pointer and immutable package");
            }
        }
        static async Task MetadataBudget()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                f.AllIncompatible=true;byte[] index=f.Index();int api=0,assets=0;
                f.Intercept=(r,c)=>{
                    if(r.RequestUri.Host=="api.github.com"){int page=++api;return Task.FromResult(OfficialCompanionFixture.Response(OfficialCompanionFixture.Json(Enumerable.Range(0,20).Select(n=>f.Release("companions-v0.11."+(page*20+n)+"-dev.1",index)).ToArray())));}
                    assets++;return Task.FromResult(OfficialCompanionFixture.Response(r.RequestUri.Host=="mirror.example"?new byte[]{0}:index));
                };
                await Reject(async()=>{await new OfficialCompanionFeed(t,OfficialCompanionFixture.Version,UpdateChannel.Preview,Path.Combine(f.Root,"feed")).RefreshAsync(new OfficialDownloadRouting(true,"https://mirror.example",false,""),null,CancellationToken.None);},"incompatible releases exhaust strict bounded index search");
                Check(api==3&&assets==16&&f.Requests.Count==19,"three API pages plus eight mirror/direct indexes never exceed nineteen metadata attempts");
            }
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                await Reject(async()=>{await t.CheckAsync(ReleaseInfo.RepositoryUrl,false,null,true,UpdateChannel.Preview,CancellationToken.None);},"resource-only tags cannot become application updates");
            }
        }
        static async Task StalePublication()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                byte[] oldZip=f.Files[OfficialCompanionFixture.PackageName("dafeiyu")],oldIndex=f.Index();string newer="companions-v0.10.4-dev.1";var entered=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();
                bool serveNew=false;f.Intercept=async(r,c)=>{
                    if(r.RequestUri.Host=="api.github.com")return OfficialCompanionFixture.Response(OfficialCompanionFixture.Json(new[]{f.Release(serveNew?newer:OfficialCompanionFixture.Tag,serveNew?f.Index():oldIndex)}));
                    if(r.RequestUri.AbsolutePath.EndsWith(".json"))return OfficialCompanionFixture.Response(serveNew?f.Index():oldIndex);
                    if(r.RequestUri.AbsoluteUri.Contains("/"+OfficialCompanionFixture.Tag+"/")&&r.RequestUri.AbsolutePath.EndsWith(OfficialCompanionFixture.PackageName("dafeiyu"))){entered.TrySetResult(true);await release.Task;return OfficialCompanionFixture.Response(oldZip);}return null;
                };
                using(var s=new OfficialCompanionDownloads(f.Root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,(i,c)=>Task.FromResult(0))) {
                    var slow=s.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);await Timeout(entered.Task);
                    f.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=OfficialCompanionFixture.Package("dafeiyu",files=>files["README.txt"]=new byte[]{66});serveNew=true;
                    var fresh=await Timeout(s.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Force,null,CancellationToken.None));release.TrySetResult(true);var older=await Timeout(slow);
                    Check(fresh.Installation.Asset.Sha256==s.Installed("dafeiyu").Asset.Sha256&&older.Installation.RootDirectory==fresh.Installation.RootDirectory,"slow old digest cannot replace a newer completed immutable package or return stale root");
                }
            }
        }
        static async Task AppRoutingSnapshot()
        {
            using(var f=new OfficialCompanionFixture()) {
                var entered=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();var requests=new List<string>();byte[] bytes={77,90,3,4};
                using(var handler=new OfficialDelegateHandler(async(r,c)=>{lock(requests)requests.Add(r.RequestUri.Host);if(r.RequestUri.Host=="mirror-a.example"){entered.TrySetResult(true);await release.Task;return OfficialCompanionFixture.Response(new byte[]{0,0,0,0});}return OfficialCompanionFixture.Response(bytes);}))using(var t=new GitHubUpdates(new Uri("https://api.github.com/"),handler)) {
                    t.ConfigureMirror(true,"https://mirror-a.example",null);var task=t.DownloadAsync(new UpdateRelease{AssetName="DeskPet-Windows-0.10.3-dev.1.zip",AssetUrl="https://github.com/Eason4869/deskpet/releases/download/v0.10.3-dev.1/DeskPet-Windows-0.10.3-dev.1.zip",Size=4,Digest="sha256:"+OfficialCompanionFixture.Hash(bytes)},f.Root,true,"http://unused-proxy.example",null,CancellationToken.None);
                    await Timeout(entered.Task);t.ConfigureMirror(true,"https://mirror-b.example",null);release.TrySetResult(true);var path=await Timeout(task);
                    Check(File.ReadAllBytes(path).SequenceEqual(bytes)&&requests.SequenceEqual(new[]{"mirror-a.example","github.com"}),"application transfer freezes mirror and proxy routing across await and ConfigureMirror mutation");
                }
            }
        }
        // Explicit opt-in local acceptance helper. CI's synthetic tests do not depend on ignored art.
        public static int RealPackageQa(string materials,string candidates,string dataRoot)
        {
            try {
                var index=OfficialJson.File(Path.Combine(candidates,"DeskPet-Companions-0.10.3-dev.1.json"),1024*1024);var store=new OfficialCompanionStorage(dataRoot);int count=0;
                foreach(object raw in OfficialJson.Array(index["companions"])) {
                    var record=OfficialJson.Object(raw);string id=(string)record["id"],name=(string)record["asset_name"],zip=Path.Combine(candidates,name);
                    var outfits=OfficialJson.Array(record["outfits"]).Select(x=>{var outfit=OfficialJson.Object(x);return new OfficialCompanionOutfit((string)outfit["outfit_id"],(string)outfit["version"]);}).ToArray();
                    var asset=new OfficialCompanionAsset(id,(string)record["name"],(string)record["version"],(string)record["min_app_version"],OfficialCompanionFixture.Tag,name,OfficialJson.Integer(record,"size"),(string)record["sha256"],outfits);
                    if(new FileInfo(zip).Length!=asset.Size||OfficialCompanionStorage.HashFile(zip)!=asset.Sha256)throw new Exception("candidate ZIP digest mismatch "+id);
                    OfficialCompanionPackageValidation.Validate(Path.Combine(materials,id),asset,CancellationToken.None);
                    string parent=store.CharacterRoot(id);Directory.CreateDirectory(parent);string stage=Path.Combine(parent,".qa-"+Guid.NewGuid().ToString("N"));
                    try{OfficialCompanionPackageValidation.Extract(zip,stage,CancellationToken.None);OfficialCompanionPackageValidation.Validate(stage,asset,CancellationToken.None);store.WriteReceipt(stage,asset,CancellationToken.None);var published=store.Publish(stage,asset,CancellationToken.None,()=>{},action=>action());if(store.Installed(id)==null)throw new Exception("published real package unreadable "+id);Console.WriteLine("PASS real pure package / ZIP / final-root receipt "+id+" bytes="+asset.Size+" root="+published.RootDirectory);count++;}
                    finally{if(Directory.Exists(stage))OfficialCompanionStorage.DeleteOwned(stage);}
                }
                if(count!=5)throw new Exception("real candidate set must contain five roles");Console.WriteLine("Real package pure/install checks: 5 passed; native decoder not exercised");return 0;
            }catch(Exception error){Console.WriteLine("FAIL real package QA "+error);return 1;}
        }
        static async Task FinalPathReadability()
        {
            using(var f=new OfficialCompanionFixture())using(var t=f.Transport()) {
                string root=f.Root;bool native=false,failed=false;
                int finalRootLength=Path.Combine(root,"official-companions","dafeiyu","v-"+OfficialCompanionFixture.Version+"-"+new string('0',64)).Length;
                string longRelative="a/"+new string('x',Math.Max(1,263-finalRootLength-7))+".png";
                f.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=OfficialCompanionFixture.Package("dafeiyu",files=>files[longRelative]=files["preview.png"]);
                using(var service=new OfficialCompanionDownloads(root,OfficialCompanionFixture.Version,UpdateChannel.Preview,t,(i,c)=>{native=true;return Task.FromResult(0);})) {
                    try{await service.EnsureAsync("dafeiyu",Direct,OfficialCompanionCheckMode.Cached,null,CancellationToken.None);}catch(ApiException){failed=true;}
                    string role=Path.Combine(root,"official-companions","dafeiyu");
                    if(failed){Check(native&&service.Installed("dafeiyu")==null&&!File.Exists(Path.Combine(role,"current.txt")),"unsupported final long path fails before pointer publication after successful short staging");Check(!Directory.GetDirectories(role).Any(),"failed long final root moves back to short staging and cleans all owned files");}
                    else Check(native&&service.Installed("dafeiyu")!=null,"platform supporting long paths verifies final published receipt");
                }
            }
        }
    }
}
