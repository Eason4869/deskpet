#if NET10_0
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu.Tests
{
    // All state belongs to a fresh fixture directory. The only artwork reads are
    // checked-in originals. No host clock, real wardrobe, settings or UI changes.
    public static class HolidayKeepsakeContractChecks
    {
        static int assertions;static string root;static IHolidayKeepsakeSceneValidator native;
        static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
        static string Fresh(string name){return Path.Combine(root,name+"-"+Guid.NewGuid().ToString("N"));}
        static HolidayKeepsakeEncounter Encounter(string date,string id="e-bao",int offset=0)
        {return new HolidayKeepsakeEncounter("official:"+id,id,"Frozen "+id,new RelationshipTime(DateTime.SpecifyKind(DateTime.Parse(date,CultureInfo.InvariantCulture),DateTimeKind.Utc),TimeSpan.FromMinutes(offset)));}
        static OutfitDefinition Outfit(string holiday,string character="e-bao",string version="1.0.0",bool ready=true)
        {return new OutfitDefinition(character,holiday+"-2026",holiday,"fixture",version,ready,false,new OutfitClipDefinition[0],null,null,null,null);}
        static HolidayCatalog Catalog(params string[] holidays){return new HolidayCatalog(holidays.Select(h=>Outfit(h)));}
        static HolidayKeepsakeDefinition Definition(string holiday,string calendar=null,int first=2025)
        {return new HolidayKeepsakeDefinition(holiday,calendar??holiday,first,"fixture.title","fixture.message","fixture",holiday,true);}
        static HolidayKeepsakeRegistry Overlap(){return new HolidayKeepsakeRegistry(new[]{Definition("national-day"),Definition("mid-autumn")});}
        static HolidayKeepsakeRuntime Runtime(string directory,Source source=null,HolidayKeepsakeRegistry registry=null,DataMaintenanceGate gate=null)
        {return new HolidayKeepsakeRuntime(directory,gate??new DataMaintenanceGate(),new HolidayCalendar(),registry??HolidayKeepsakeRegistry.BuiltIn,source??new Source(),native);}
        static HolidayKeepsakeObservationResult Observe(HolidayKeepsakeRuntime runtime,string date,HolidayCatalog catalog=null)
        {return runtime.ObserveAsync(Encounter(date),catalog??Catalog("halloween")).GetAwaiter().GetResult();}
        static void Reject(Action action,string message)
        {bool rejected=false;try{action();}catch(Exception e){if(!(e is InvalidDataException||e is IOException||e is ArgumentException||e is InvalidOperationException||e is NotSupportedException))throw;rejected=true;}Check(rejected,message);}
        public static int Run(string directory,IHolidayKeepsakeSceneValidator validator)
        {
            root=Path.GetFullPath(directory);native=validator;assertions=0;Directory.CreateDirectory(root);
            RegistryAndDates();TrustAndOriginals();AggregateAndPublication();ReadIndependence();QueueAndMaintenance();Corruption();StorageBoundaries();NativePixels();ProcessRace();
            return assertions;
        }
        static void RegistryAndDates()
        {
            Reject(()=>new HolidayKeepsakeRegistry(new[]{Definition("a"),Definition("a")}),"Duplicate holiday rejected");
            Reject(()=>new HolidayKeepsakeRegistry(new[]{Definition("a","halloween"),Definition("b","halloween")}),"Duplicate calendar rejected");
            Reject(()=>new HolidayKeepsakeRegistry(Enumerable.Range(0,17).Select(n=>Definition("h"+n))),"Registry bounded");
            Reject(()=>new HolidayKeepsakeDefinition("x","halloween",2026,"t","m","s","other",true),"Scene set aliases must not silently bypass mapping");
            foreach(string date in new[]{"2025-10-31","2026-10-30","2026-11-01"}){
                var r=Observe(Runtime(Fresh("outside")),date);Check(r.Status==HolidayKeepsakeObservationStatus.Completed&&r.Results.Count==0,"No outside/prehistoric claim "+date);
            }
            string path=Fresh("years");var runtime=Runtime(path);
            var first=Observe(runtime,"2026-10-31");Check(first.Results.Single().Status==HolidayKeepsakeStatus.Saved,"First eligible year saved");
            Check(Observe(Runtime(path),"2026-10-31").Results.Single().Status==HolidayKeepsakeStatus.AlreadyOwned,"Restart deduplicates");
            Check(Observe(runtime,"2027-10-31").Results.Single().Status==HolidayKeepsakeStatus.Saved,"Later year independent");
            Check(runtime.LoadAsync("official:e-bao").Result.Records.Count==2,"Annual history retained");
            path=Fresh("watermark");runtime=Runtime(path);
            runtime.ObserveAsync(new HolidayKeepsakeEncounter("custom:untrusted",null,"Other",Encounter("2026-11-01").Time),Catalog("halloween")).Wait();
            Check(Observe(Runtime(path),"2026-10-31").Status==HolidayKeepsakeObservationStatus.ClockRollback,"Unsupported nonholiday observation advances global watermark across restart");
            path=Fresh("offset");runtime=Runtime(path);
            var offset=runtime.ObserveAsync(Encounter("2026-10-30T16:00:00","e-bao",480),Catalog("halloween")).Result.Results.Single().Record;
            Check(offset.EarnedLocalDate=="2026-10-31"&&offset.EarnedUtcTicks==new DateTime(2026,10,30,16,0,0,DateTimeKind.Utc).Ticks&&offset.OffsetMinutes==480,"One captured UTC offset sample");
            var previewPath=Fresh("preview");runtime=Runtime(previewPath);
            Check(runtime.LoadAsync("official:e-bao").Result.Records.Count==0,"Empty read works");
            Check(runtime.PreviewAsync("official:e-bao","e-bao","name","halloween",2026,Catalog("halloween")).Result.Availability==HolidayKeepsakeAvailability.Available,"Read-only preview validates original");
            Check(runtime.PreviewAsync("official:e-bao","e-bao","name","halloween",2025,Catalog("halloween")).Result.Availability==HolidayKeepsakeAvailability.NotEnabled,"First-year preview unavailable");
            Check(!Directory.Exists(previewPath),"Read APIs never create directory or lease");
            runtime=Runtime(Fresh("calendar-preview"),null,new HolidayKeepsakeRegistry(new[]{Definition("fixture","christmas")}));
            Check(runtime.PreviewAsync("official:e-bao","e-bao","name","fixture",2026,Catalog("fixture")).Result.InstanceKey=="christmas:20261224","Preview resolves CalendarId, not October 31");
        }
        static void TrustAndOriginals()
        {
            foreach(var tuple in new[]{new[]{"custom:e-bao","e-bao"},new[]{"official:e-bao","marfu"},new[]{"official:unknown","unknown"}}){
                var r=Runtime(Fresh("unsupported")).ObserveAsync(new HolidayKeepsakeEncounter(tuple[0],tuple[1],"e-bao",Encounter("2026-10-31").Time),Catalog("halloween")).Result;
                Check(r.Results.Single().Status==HolidayKeepsakeStatus.UnsupportedCompanion,"Display name or claimed identity does not authorize art");
            }
            foreach(var catalog in new[]{new HolidayCatalog(new OutfitDefinition[0]),new HolidayCatalog(new[]{Outfit("halloween",version:"2.0.0")}),new HolidayCatalog(new[]{Outfit("halloween",ready:false)})}){
                Check(Observe(Runtime(Fresh("outfit")),"2026-10-31",catalog).Results.Single().Status==HolidayKeepsakeStatus.ResourceUnavailable,"Missing/not-ready/wrong exact outfit fails");
            }
            var timing=Stopwatch.StartNew();
            foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}){
                var p=new RelationshipSceneProvider(asset=>File.OpenRead(Path.Combine("assets",asset.RelativePath)));
                var runtime=new HolidayKeepsakeRuntime(Fresh("official"),new DataMaintenanceGate(),new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,p,native);
                var r=runtime.ObserveAsync(Encounter("2026-10-31",id),new HolidayCatalog(new[]{Outfit("halloween",id)})).Result.Results.Single();
                Check(r.Status==HolidayKeepsakeStatus.Saved,"Official original full native pixels "+id);
                var scene=runtime.ReadSceneAsync("official:"+id,"halloween","halloween:20261031").Result;
                Check(scene.Status==HolidayKeepsakeReadStatus.Available&&scene.CopyPngBytes().SequenceEqual(File.ReadAllBytes(Path.Combine("assets",p.Capture("official:"+id,id,"halloween","halloween-2026","1.0.0").Descriptor.RelativePath))),"Original bytes unchanged "+id);
            }
            Console.WriteLine("Five official scenes: acquisition + retained read + native pixels = "+timing.ElapsedMilliseconds+" ms");
        }
        static void AggregateAndPublication()
        {
            var catalog=Catalog("national-day","mid-autumn");string path=Fresh("aggregate");string publicationPath=path;
            var aggregateSource=new Source{BeforeOpen=holiday=>Check(new HolidayKeepsakeStore(publicationPath).Load().Records.Count==0,"No intermediate JSON receipt before every candidate finishes: "+holiday)};
            var runtime=Runtime(path,aggregateSource,Overlap());int notifications=0;
            runtime.Changed+=id=>{throw new Exception("fixture observer");};runtime.Changed+=id=>{Check(id=="official:e-bao"&&new HolidayKeepsakeStore(publicationPath).Load().Records.Count==2,"Notification keeps admitted owner and observes complete merged publication");notifications++;};
            var result=Observe(runtime,"2025-10-06",catalog);
            Check(result.Results.Count==2&&result.Results.All(x=>x.Status==HolidayKeepsakeStatus.Saved),"One encounter saves two real overlapping calendar holidays");
            Check(notifications==1,"Aggregate emits one postcommit notification despite throwing subscriber");
            Check(Observe(runtime,"2025-10-06",catalog).Results.All(x=>x.Status==HolidayKeepsakeStatus.AlreadyOwned)&&notifications==1,"Overlap deduplicates without repeated notification");
            var source=new Source{MissingHoliday="mid-autumn"};runtime=Runtime(Fresh("partial"),source,Overlap());result=Observe(runtime,"2025-10-06",catalog);
            Check(result.Results.Single(x=>x.HolidayId=="mid-autumn").Status==HolidayKeepsakeStatus.ResourceUnavailable&&result.Results.Single(x=>x.HolidayId=="national-day").Status==HolidayKeepsakeStatus.Saved,"One missing source cannot suppress valid candidate");
            source=new Source{BadHoliday="mid-autumn"};runtime=Runtime(Fresh("bad-partial"),source,Overlap());result=Observe(runtime,"2025-10-06",catalog);
            Check(result.Results.Count(x=>x.Status==HolidayKeepsakeStatus.Saved)==1,"One malformed scene cannot suppress valid candidate");
            path=Fresh("json-failure");runtime=Runtime(path,null,Overlap());Observe(runtime,"2025-10-05",new HolidayCatalog(new OutfitDefinition[0]));
            string state=Path.Combine(path,"commemorations/holiday-state.json"),before=File.ReadAllText(state);notifications=0;runtime.Changed+=id=>notifications++;
            using(var held=new FileStream(state,FileMode.Open,FileAccess.Read,FileShare.Read))result=Observe(runtime,"2025-10-06",catalog);
            Check(result.Status==HolidayKeepsakeObservationStatus.StorageUnavailable&&result.Results.Count==0&&notifications==0,"Atomic JSON failure reports no Saved or notifications");
            Check(File.ReadAllText(state)==before&&runtime.LoadAsync("official:e-bao").Result.Records.Count==0,"JSON failure preserves prior state");
            Check(Observe(runtime,"2025-10-07",catalog).Results.Single().HolidayId=="national-day","Fresh retry cannot backfill ended mid-autumn");
            Check(runtime.LoadAsync("official:e-bao").Result.Records.All(x=>x.HolidayId=="national-day"),"Only currently eligible activity retried");
            // Read-open old generation remains coherent while an atomic writer publishes.
            path=Fresh("reader-sharing");runtime=Runtime(path);Observe(runtime,"2026-10-30");state=Path.Combine(path,"commemorations/holiday-state.json");
            using(var held=new FileStream(state,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                Check(Observe(runtime,"2026-10-31").Results.Single().Status==HolidayKeepsakeStatus.Saved,"Concurrent read snapshot does not block publication");
                using(var reader=new StreamReader(held))Check(!reader.ReadToEnd().Contains("Frozen e-bao"),"Old open snapshot stays complete");
            }
        }
        static void ReadIndependence()
        {
            string path=Fresh("historical");var runtime=Runtime(path);Observe(runtime,"2026-10-31");
            var missing=new Source{MissingHoliday="halloween"};var removed=Runtime(path,missing,new HolidayKeepsakeRegistry(new HolidayKeepsakeDefinition[0]));
            Check(removed.LoadAsync("official:e-bao").Result.Records.Count==1,"Removed registry definition does not hide earned record");
            var scene=removed.ReadSceneAsync("official:e-bao","halloween","halloween:20261031").Result;Check(scene.Status==HolidayKeepsakeReadStatus.Available,"Saved scene independent of source/outfit/definition");
            byte[] snapshot=scene.CopyPngBytes();snapshot[0]=0;Check(scene.CopyPngBytes()[0]==137,"Scene byte snapshot is defensive");
            Check(Observe(Runtime(path,missing),"2026-10-31",new HolidayCatalog(new OutfitDefinition[0])).Results.Single().Status==HolidayKeepsakeStatus.AlreadyOwned,"Already owned before current outfit/source checks");
            File.WriteAllBytes(Path.Combine(path,"commemorations/scenes/"+scene.Record.SceneSha256+".png"),new byte[]{1});
            Check(removed.ReadSceneAsync("official:e-bao","halloween","halloween:20261031").Result.Status==HolidayKeepsakeReadStatus.ResourceUnavailable&&removed.LoadAsync("official:e-bao").Result.Records.Count==1,"Bad blob preserves per-card metadata");
            var changed=new Source{Bytes=Png(800,480,1)};
            Check(Observe(Runtime(path,changed),"2027-10-31").Results.Single().Status==HolidayKeepsakeStatus.Saved,"Bad old blob does not corrupt entire store");
        }
        static void QueueAndMaintenance()
        {
            string path=Fresh("queued");var gate=new DataMaintenanceGate();var opened=new ManualResetEvent(false);var release=new ManualResetEvent(false);
            var source=new Source{OnOpen=()=>{opened.Set();if(!release.WaitOne(10000))throw new IOException("Fixture timed out");}};var runtime=Runtime(path,source,null,gate);
            var first=runtime.ObserveAsync(Encounter("2026-10-31"),Catalog("halloween"));Check(opened.WaitOne(10000),"Worker reached original read");
            source.Version="scene-v2";source.Bytes=Png(800,480,1);source.OnOpen=null;
            var second=runtime.ObserveAsync(Encounter("2026-10-31","marfu"),new HolidayCatalog(new[]{Outfit("halloween","marfu")}));
            source.Version="scene-v3";source.Bytes=Png(800,480,2);
            Check(source.Opens==1,"Queued capture performs no I/O");Reject(()=>gate.BeginAsync().GetAwaiter().GetResult(),"Activity admitted before queue blocks maintenance");
            var shutdown=runtime.ShutdownAsync();Check(!shutdown.IsCompleted,"Shutdown drains admitted work asynchronously");
            Check(runtime.ObserveAsync(Encounter("2026-10-31"),Catalog("halloween")).Result.Status==HolidayKeepsakeObservationStatus.Closed,"Shutdown closes new admission");
            release.Set();Task.WaitAll(first,second,shutdown);
            Check(first.Result.Results.Single().Record.SceneVersion=="scene-v1"&&second.Result.Results.Single().Record.SceneVersion=="scene-v2","Admission freezes source generation/version and owner through switch");
            Check(second.Result.Results.Single().Record.CompanionId=="official:marfu","Queued owner unchanged");
            Check(source.Opens==source.Disposals,"Every captured stream disposed");
            using(gate.BeginAsync().Result){Check(Runtime(Fresh("busy"),null,null,gate).ObserveAsync(Encounter("2026-10-31"),Catalog("halloween")).Result.Status==HolidayKeepsakeObservationStatus.MaintenanceBusy,"Maintenance rejects admission");}
            var stable=Runtime(path);var before=Directory.GetFiles(Path.Combine(path,"commemorations"),"*",SearchOption.AllDirectories).ToDictionary(f=>f,File.ReadAllBytes);
            var cleared=new HistoryMaintenanceService(path,gate).ClearAllAsync().Result;Check(cleared.Status==HistoryMaintenanceStatus.Complete,"Actual history maintenance completes");
            var cache=new CacheMaintenanceService(path);cache.Clean(cache.Scan(new DateTime(2026,11,1,0,0,0,DateTimeKind.Utc)),new DateTime(2026,11,1,0,0,0,DateTimeKind.Utc));
            Task.Run(()=>new SettingsStore(Path.Combine(path,"settings.json")).RestoreDefaultPreferencesAsync(new PetSettings(),gate)).Wait();
            Check(before.All(p=>File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Real maintenance preserves entire commemorations directory");
            Check(stable.LoadAsync("official:e-bao").Result.Records.Count==1,"Maintenance preserved readable record");
            path=Fresh("resource-replaced");opened.Reset();release.Reset();source=new Source{OnOpen=()=>{opened.Set();if(!release.WaitOne(10000))throw new IOException("Fixture timeout");}};runtime=Runtime(path,source);
            first=runtime.ObserveAsync(Encounter("2026-10-31"),Catalog("halloween"));Check(opened.WaitOne(10000),"Replacement fixture paused first worker");
            source.OnOpen=null;source.MutableResource=true;second=runtime.ObserveAsync(Encounter("2026-10-31","marfu"),new HolidayCatalog(new[]{Outfit("halloween","marfu")}));
            source.Bytes=Png(800,480,3);release.Set();Task.WaitAll(first,second);
            Check(first.Result.Results.Single().Status==HolidayKeepsakeStatus.Saved&&second.Result.Results.Single().Status==HolidayKeepsakeStatus.ResourceUnavailable,"Queued resource replacement fails captured hash instead of adopting new art");
            Check(runtime.LoadAsync("official:marfu").Result.Records.Count==0,"Resource replacement cannot fabricate ownership");
        }
        static void StorageBoundaries()
        {
            string path=Fresh("state-limit"),directory=Path.Combine(path,"commemorations");Directory.CreateDirectory(directory);string file=Path.Combine(directory,"holiday-state.json");
            using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write))stream.SetLength(32L*1024*1024+1);
            Check(Observe(Runtime(path),"2026-10-31").Status==HolidayKeepsakeObservationStatus.StoreIncompatible&&new FileInfo(file).Length==32L*1024*1024+1,"Oversized state preserved and blocked");
            path=Fresh("write-limit");Observe(Runtime(path),"2026-10-31");var store=new HolidayKeepsakeStore(path);var data=store.Load();file=Path.Combine(path,"commemorations/holiday-state.json");byte[] original=File.ReadAllBytes(file);var template=data.Records.Single();
            var clone=typeof(object).GetMethod("MemberwiseClone",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            for(int n=0;n<60000;n++){var record=(HolidayKeepsakeRecord)clone.Invoke(template,null);record.CharacterId="future-character-"+n;record.CompanionId="official:"+record.CharacterId;data.Records.Add(record);}
            Reject(()=>store.Save(data),"Full state publication refuses oversized history rather than trimming");Check(File.ReadAllBytes(file).SequenceEqual(original),"Full store preserves prior bytes");
            Check(!Directory.GetFiles(Path.Combine(path,"commemorations"),"*.tmp").Any(),"Only owned state temporary cleaned on failure");
            path=Fresh("path-junction");Directory.CreateDirectory(path);string outside=Fresh("protected-target");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"marker"),"untouched");
            if(Environment.OSVersion.Platform==PlatformID.Win32NT){
                using(var process=Process.Start(new ProcessStartInfo("cmd.exe","/c mklink /J \""+Path.Combine(path,"commemorations")+"\" \""+outside+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
                    process.WaitForExit();Check(process.ExitCode==0,"Owned junction fixture created: "+process.StandardError.ReadToEnd());
                }
                Check(Observe(Runtime(path),"2026-10-31").Status==HolidayKeepsakeObservationStatus.StorageUnavailable,"Reparse root blocks acquisition");
                Check(Directory.GetFiles(outside).Length==1&&File.ReadAllText(Path.Combine(outside,"marker"))=="untouched","Reparse rejection writes nothing to linked target");
            }
        }
        static void Corruption()
        {
            foreach(string content in new[]{"{\"Schema\":2,\"DateHighWater\":\"\",\"Records\":[]}","broken","{\"Schema\":1}"}){
                string path=Fresh("corrupt"),directory=Path.Combine(path,"commemorations");Directory.CreateDirectory(directory);string state=Path.Combine(directory,"holiday-state.json");File.WriteAllText(state,content);
                Check(Observe(Runtime(path),"2026-10-31").Status==HolidayKeepsakeObservationStatus.StoreIncompatible,"Unknown/corrupt state blocks mutation");Check(File.ReadAllText(state)==content,"Unknown/corrupt original state preserved");
            }
            string good=Fresh("schema");Observe(Runtime(good),"2026-10-31");string goodPath=Path.Combine(good,"commemorations/holiday-state.json");
            string valid=File.ReadAllText(goodPath);
            foreach(var pair in new[]{new[]{"halloween:20261031","halloween:20251031"},new[]{"\"OccurrenceYear\":2026","\"OccurrenceYear\":2025"},new[]{"\"OffsetMinutes\":0","\"OffsetMinutes\":900"}}){
                File.WriteAllText(goodPath,valid.Replace(pair[0],pair[1]));Check(Observe(Runtime(good),"2027-10-31").Status==HolidayKeepsakeObservationStatus.StoreIncompatible,"Historical structural facts independently validated");
            }
            File.WriteAllText(goodPath,valid);var store=new HolidayKeepsakeStore(good);var data=store.Load();data.Records.Add(data.Records[0]);Reject(()=>HolidayKeepsakeStore.Validate(data),"Duplicate annual receipt rejected");
            // Read-only snapshots remain detached from any in-memory mutable persistence object.
            var r=Runtime(good);var album=r.LoadAsync("official:e-bao").Result;album.Records[0].CompanionName="tampered";
            Check(r.LoadAsync("official:e-bao").Result.Records[0].CompanionName=="Frozen e-bao","Album metadata snapshot detached");
            data=store.Load();var other=(HolidayKeepsakeRecord)typeof(object).GetMethod("MemberwiseClone",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(data.Records[0],null);
            other.HolidayId="replacement-holiday";data.Records.Add(other);store.Save(data);
            Check(r.ReadSceneAsync("official:e-bao","replacement-holiday","halloween:20261031").Result.Record.HolidayId=="replacement-holiday","Full identity resolves historical definitions sharing a calendar instance");
            Check(r.ReadSceneAsync("official:e-bao","missing-holiday","halloween:20261031").Result.Status==HolidayKeepsakeReadStatus.NotFound,"Wrong holiday cannot read another record with same calendar instance");
        }
        static void NativePixels()
        {
            var inputType=typeof(HolidayKeepsakePng).GetNestedType("ExactDeflateInput",System.Reflection.BindingFlags.NonPublic);
            using(var bounded=(Stream)Activator.CreateInstance(inputType,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{new byte[]{1},0,1},CultureInfo.InvariantCulture)){
                Reject(()=>bounded.Read(new byte[1],0,2),"Bounded inflater input preserves Stream argument checks before clipping");
                Check(bounded.Read(new byte[1],0,1)==1&&bounded.Read(new byte[0],0,0)==0,"Zero-length read at compressed EOF remains legal");
            }
            byte[] opaque=Png(800,480,0);native.Validate(opaque,800,480);Check(true,"Opaque static original accepted");
            foreach(var shape in new[]{new[]{799,480},new[]{3201,1920},new[]{3200,2001},new[]{800,800}})Reject(()=>native.Validate(Png(shape[0],shape[1],0),shape[0],shape[1]),"Geometry limits enforced");
            Reject(()=>native.Validate(opaque,805,483),"Descriptor geometry mismatch rejected despite acceptable ratio");
            byte[] bad=(byte[])opaque.Clone();bad[bad.Length-1]^=1;Reject(()=>native.Validate(bad,800,480),"CRC rejects corruption");
            bad=(byte[])opaque.Clone();int idat=FindChunk(bad,"IDAT");bad[idat+8]=0;WriteBig(bad,idat+8+ReadBig(bad,idat),Crc(bad,idat+4,(int)ReadBig(bad,idat)+4));
            Reject(()=>native.Validate(bad,800,480),"CRC-valid invalid IDAT rejected");
            Reject(()=>native.Validate(Png(800,480,0,5),800,480),"Complete pixels reject illegal scanline filter");
            foreach(int count in new[]{1,2,4096,8192}){byte[] trailing=AddCompressedJunk(opaque,count);Reject(()=>native.Validate(trailing,800,480),"Trailing compressed garbage rejected despite valid CRC and pixel checksum: "+count);}
            Reject(()=>native.Validate(RemoveCompressedEnd(opaque),800,480),"Truncated final DEFLATE byte rejected with retained Adler and valid chunk CRC");
            var source=new Source{Bytes=bad};Check(Observe(Runtime(Fresh("bad-idat"),source),"2026-10-31").Results.Single().Status==HolidayKeepsakeStatus.ResourceUnavailable,"Bad IDAT cannot earn a card");
            source=new Source{WrongHash=true};Check(Observe(Runtime(Fresh("bad-hash"),source),"2026-10-31").Results.Single().Status==HolidayKeepsakeStatus.ResourceUnavailable,"Wrong exact hash rejected");
            source=new Source{WrongWidth=true};Check(Observe(Runtime(Fresh("bad-dim"),source),"2026-10-31").Results.Single().Status==HolidayKeepsakeStatus.ResourceUnavailable,"Wrong descriptor dimensions rejected");
            source=new Source{Bytes=new byte[12*1024*1024+1]};Check(Observe(Runtime(Fresh("too-large"),source),"2026-10-31").Results.Single().Status==HolidayKeepsakeStatus.ResourceUnavailable,"Oversized original rejected before decode");
        }
        public static int Child(string directory,string version,IHolidayKeepsakeSceneValidator validator)
        {native=validator;var result=Observe(Runtime(directory,new Source{Version=version},Overlap()),"2025-10-06",Catalog("national-day","mid-autumn"));Console.WriteLine(result.Status+":"+String.Join(",",result.Results.Select(x=>x.Status.ToString()).ToArray()));return result.Status==HolidayKeepsakeObservationStatus.Completed?0:1;}
        static void ProcessRace()
        {
            string path=Fresh("process-race");string host=Process.GetCurrentProcess().MainModule.FileName,assembly=typeof(HolidayKeepsakeContractChecks).Assembly.Location;
            string prefix=host.EndsWith("dotnet.exe",StringComparison.OrdinalIgnoreCase)?"\""+assembly+"\" ":"";
            var children=new List<Process>();
            foreach(string version in new[]{"race-v1","race-v2"}){
                var start=new ProcessStartInfo(host,prefix+"--holiday-keepsake-child \""+path+"\" "+version){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};children.Add(Process.Start(start));
            }
            foreach(var child in children){if(!child.WaitForExit(30000)){child.Kill();throw new Exception("Fixture child timeout");}string output=child.StandardOutput.ReadToEnd()+child.StandardError.ReadToEnd();Check(child.ExitCode==0,"Cross-process acquisition completes: "+output);child.Dispose();}
            var records=Runtime(path).LoadAsync("official:e-bao").Result.Records;Check(records.Count==2&&records.Select(r=>r.SceneVersion).Distinct().Count()==1,"Cross-process aggregate first publisher wins without lost/duplicate records");
        }
        sealed class Source:IRelationshipSceneProvider
        {
            internal byte[] Bytes=Png(800,480,0);internal string Version="scene-v1",MissingHoliday,BadHoliday;internal bool WrongHash,WrongWidth,MutableResource;
            internal Action OnOpen;internal Action<string> BeforeOpen;internal int Opens,Disposals;
            public RelationshipSceneCapture Capture(string companion,string character,string holiday,string outfit,string version)
            {
                if(holiday==MissingHoliday||version!="1.0.0")return null;
                var bytes=holiday==BadHoliday?new byte[55]:Bytes;string capturedVersion=Version;var action=OnOpen;var before=BeforeOpen;bool mutable=MutableResource;
                var descriptor=new RelationshipStageArtAsset(companion,holiday,outfit,version,"holiday:"+holiday+":"+character,capturedVersion,"fixture.png",WrongWidth?805:800,480,bytes.Length,WrongHash?new string('a',64):HolidayKeepsakePng.Hash(bytes),holiday);
                return new RelationshipSceneCapture(descriptor,capturedVersion,delegate{Interlocked.Increment(ref Opens);if(before!=null)before(holiday);if(action!=null)action();return new OwnedStream(mutable?Bytes:bytes,()=>Interlocked.Increment(ref Disposals));});
            }
        }
        sealed class OwnedStream:MemoryStream
        {readonly Action dispose;bool disposed;internal OwnedStream(byte[] bytes,Action onDispose):base(bytes,false){dispose=onDispose;}protected override void Dispose(bool disposing){if(!disposed){disposed=true;dispose();}base.Dispose(disposing);}}
        static byte[] Png(int width,int height,byte value,byte filter=0)
        {
            var raw=new byte[(width*3+1)*height];for(int y=0;y<height;y++){raw[y*(width*3+1)]=filter;for(int x=1;x<=width*3;x++)raw[y*(width*3+1)+x]=value;}
            byte[] compressed;using(var memory=new MemoryStream()){using(var deflate=new DeflateStream(memory,CompressionMode.Compress,true))deflate.Write(raw,0,raw.Length);compressed=memory.ToArray();}
            uint a=1,b=0;foreach(byte item in raw){a=(a+item)%65521;b=(b+a)%65521;}
            using(var stream=new MemoryStream()){
                stream.Write(new byte[]{137,80,78,71,13,10,26,10},0,8);var header=new byte[13];WriteBig(header,0,(uint)width);WriteBig(header,4,(uint)height);header[8]=8;header[9]=2;Chunk(stream,"IHDR",header);
                var z=new byte[compressed.Length+6];z[0]=120;z[1]=156;Buffer.BlockCopy(compressed,0,z,2,compressed.Length);WriteBig(z,z.Length-4,(b<<16)|a);Chunk(stream,"IDAT",z);Chunk(stream,"IEND",new byte[0]);return stream.ToArray();
            }
        }
        static void Chunk(Stream stream,string kind,byte[] bytes){var chunk=new byte[bytes.Length+12];WriteBig(chunk,0,(uint)bytes.Length);Encoding.ASCII.GetBytes(kind).CopyTo(chunk,4);bytes.CopyTo(chunk,8);WriteBig(chunk,bytes.Length+8,Crc(chunk,4,bytes.Length+4));stream.Write(chunk,0,chunk.Length);}
        static int FindChunk(byte[] png,string kind){for(int at=8;at<png.Length;at+=12+(int)ReadBig(png,at))if(Encoding.ASCII.GetString(png,at+4,4)==kind)return at;throw new Exception("Fixture chunk missing");}
        static byte[] AddCompressedJunk(byte[] png,int count)
        {
            int at=FindChunk(png,"IDAT"),length=(int)ReadBig(png,at);var result=new byte[png.Length+count];
            Buffer.BlockCopy(png,0,result,0,at+8+length-4);
            Buffer.BlockCopy(png,at+8+length-4,result,at+8+length-4+count,png.Length-(at+8+length-4));
            WriteBig(result,at,(uint)(length+count));WriteBig(result,at+8+length+count,Crc(result,at+4,length+count+4));return result;
        }
        static byte[] RemoveCompressedEnd(byte[] png)
        {
            int at=FindChunk(png,"IDAT"),length=(int)ReadBig(png,at),cut=at+8+length-5;var result=new byte[png.Length-1];
            Buffer.BlockCopy(png,0,result,0,cut);Buffer.BlockCopy(png,cut+1,result,cut,png.Length-cut-1);
            WriteBig(result,at,(uint)(length-1));WriteBig(result,at+8+length-1,Crc(result,at+4,length+3));return result;
        }
        static uint ReadBig(byte[] bytes,int at){return ((uint)bytes[at]<<24)|((uint)bytes[at+1]<<16)|((uint)bytes[at+2]<<8)|bytes[at+3];}
        static void WriteBig(byte[] bytes,long at,uint value){bytes[at]=(byte)(value>>24);bytes[at+1]=(byte)(value>>16);bytes[at+2]=(byte)(value>>8);bytes[at+3]=(byte)value;}
        static uint Crc(byte[] bytes,int at,int length){uint c=0xffffffffU;for(int i=at;i<at+length;i++){c^=bytes[i];for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320U^(c>>1):c>>1;}return c^0xffffffffU;}
    }
}
