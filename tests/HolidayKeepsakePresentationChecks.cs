using System;
using System.IO;
using System.Linq;
using System.Threading;
using Dafeiyu;
using Dafeiyu.Maintenance;
public static class HolidayKeepsakePresentationChecks
{
    public static byte[] AddHistoricalTwin(string directory,bool badScene)
    {
        string path=Path.Combine(directory,"commemorations/holiday-state.json");
        HolidayKeepsakeData data;var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(HolidayKeepsakeData));
        using(var input=File.OpenRead(path)){var loaded=serializer.ReadObject(input);if(loaded==null)throw new InvalidDataException("Fixture state");data=(HolidayKeepsakeData)loaded;}
        var original=data.Records.First(x=>x.CompanionId=="official:e-bao");
        HolidayKeepsakeRecord twin;var recordSerializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(HolidayKeepsakeRecord));
        using(var memory=new MemoryStream()){recordSerializer.WriteObject(memory,original);memory.Position=0;var loaded=recordSerializer.ReadObject(memory);if(loaded==null)throw new InvalidDataException("Fixture record");twin=(HolidayKeepsakeRecord)loaded;}
        twin.HolidayId="historic-halloween";twin.CompanionName="Historical companion";
        if(badScene)twin.SceneSha256=new string('0',64);
        data.Records.RemoveAll(x=>x.HolidayId==twin.HolidayId);data.Records.Add(twin);
        byte[] bytes;using(var memory=new MemoryStream()){serializer.WriteObject(memory,data);bytes=memory.ToArray();}File.WriteAllBytes(path,bytes);return bytes;
    }
    static int count;
    static void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
    static readonly string[] Characters={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};
    static HolidayCatalog Catalog(){return new HolidayCatalog(Characters.Select(c=>new OutfitDefinition(c,"halloween-2026","halloween","fixture","1.0.0",true,false,new OutfitClipDefinition[0],null,null,null,null)));}
    public static int Run(string directory,IHolidayKeepsakeSceneValidator validator)
    {
        Directory.CreateDirectory(directory);var gate=new DataMaintenanceGate();var catalog=Catalog();var ready=new ManualResetEventSlim(false);var release=new ManualResetEventSlim(false);
        var provider=new RelationshipSceneProvider(asset=>{ready.Set();release.Wait();return File.OpenRead(Path.Combine("assets",asset.RelativePath));});
        var runtime=new HolidayKeepsakeRuntime(directory,gate,new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,provider,validator);var producer=new HolidayKeepsakeEncounters(runtime);
        var day=new RelationshipTime(new DateTime(2026,10,30,15,59,59,DateTimeKind.Utc),TimeSpan.FromHours(8));
        Check(producer.ObserveAsync(false,true,"official:e-bao","e-bao","frozen",HolidayEncounterKind.VisibleBuddy,day,catalog).Result==null,"hidden frame excluded");
        Check(producer.ObserveAsync(true,false,"official:e-bao","e-bao","frozen",HolidayEncounterKind.CompanionHome,day,catalog).Result==null,"locked shown home excluded");
        Check(!Directory.Exists(Path.Combine(directory,"commemorations")),"excluded frames never create holiday storage");
        Check(producer.ObserveAsync(true,true,"official:e-bao","e-bao","frozen",HolidayEncounterKind.VisibleBuddy,day,catalog).Result.Results.Count==0,"actual visible nonholiday observation advances date without a receipt");
        day=day.AddSeconds(1);
        var first=producer.ObserveAsync(true,true,"official:e-bao","e-bao","frozen",HolidayEncounterKind.VisibleBuddy,day,catalog);Check(ready.Wait(10000),"actual event enters captured source worker");
        var duplicate=producer.ObserveAsync(true,true,"official:e-bao","e-bao","later name",HolidayEncounterKind.CompanionHome,day,catalog);
        Check(Object.ReferenceEquals(first,duplicate),"simultaneous frame/home share one aggregate observation");
        bool maintenanceRefused=false;try{gate.BeginAsync();}catch(InvalidOperationException){maintenanceRefused=true;}Check(maintenanceRefused,"the application maintenance gate covers queued original-scene work");
        Check(producer.ObserveAsync(true,true,"official:marfu","marfu","switched",HolidayEncounterKind.VisibleBuddy,day,catalog).Result==null,"other owner retries after in-flight capture instead of changing admitted ownership");
        release.Set();Check(first.Result.Results.Single().Status==HolidayKeepsakeStatus.Saved,"actual visible midnight boundary earns holiday");
        var saved=runtime.LoadAsync("official:e-bao").Result.Records.Single();Check(saved.CompanionName=="frozen"&&saved.EarnedLocalDate=="2026-10-31","origin name/date frozen across later callbacks");
        Check(producer.ObserveAsync(true,true,"official:e-bao","e-bao","renamed",HolidayEncounterKind.VisibleBuddy,day,catalog).Result==null,"successful day/generation coalesces subsequent visible frames");
        var other=producer.ObserveAsync(true,true,"official:marfu","marfu","switched",HolidayEncounterKind.VisibleBuddy,day,catalog).Result;Check(other.Results.Single().Record.CompanionId=="official:marfu","new owner receives its own fresh encounter");
        var page=new HolidayKeepsakeAlbumModel(runtime).LoadAsync("official:e-bao",0).Result;Check(page.Album.Records.Count==1&&page.Scenes.Single().Record.HolidayId=="halloween","album consumes saved full holiday identity");
        var bytes=page.Scenes.Single().CopyPngBytes();var original=(byte[])bytes.Clone();bytes[8]^=1;Check(page.Scenes.Single().CopyPngBytes().SequenceEqual(original),"display copy cannot mutate retained scene");
        var historic=new HolidayKeepsakeRecord{CompanionId=saved.CompanionId,HolidayId="removed-halloween",InstanceKey=saved.InstanceKey,CompanionName="old name",OccurrenceYear=saved.OccurrenceYear,EarnedLocalDate=saved.EarnedLocalDate,SceneSha256=saved.SceneSha256,SceneVersion=saved.SceneVersion};
        var current=HolidayKeepsakeCardData.Owned(saved,HolidayKeepsakeRegistry.BuiltIn);var removed=HolidayKeepsakeCardData.Owned(historic,HolidayKeepsakeRegistry.BuiltIn);
        Check(current.Identity!=removed.Identity&&current.CaptureKey(false)!=removed.CaptureKey(false),"same calendar/instance historical holidays cannot share preview/cache/save identity");
        Check(removed.Title==TextResources.Get("holidayKeepsake.historyTitle")&&removed.Style=="history"&&removed.Name=="old name","removed definition uses localized neutral history with saved name");
        AddHistoricalTwin(directory,false);page=new HolidayKeepsakeAlbumModel(runtime).LoadAsync("official:e-bao",0).Result;
        Check(page.Album.Records.Count==2&&page.Scenes.Select(x=>x.Record.HolidayId).Distinct().Count()==2&&page.Scenes.All(x=>x.Status==HolidayKeepsakeReadStatus.Available),"actual full-key UI model separately reads historical definitions sharing an instance");
        AddHistoricalTwin(directory,true);page=new HolidayKeepsakeAlbumModel(runtime).LoadAsync("official:e-bao",0).Result;
        Check(page.Album.Records.Count==2&&page.Scenes.Count(x=>x.Status==HolidayKeepsakeReadStatus.ResourceUnavailable)==1&&page.Scenes.Count(x=>x.Status==HolidayKeepsakeReadStatus.Available)==1,"bad saved scene keeps its record and does not replace or break the other card");
        AddHistoricalTwin(directory,false);
        for(int year=2027;year<2034;year++){
            var date=new RelationshipTime(new DateTime(year,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.Zero);
            Check(runtime.ObserveAsync(new HolidayKeepsakeEncounter("official:e-bao","e-bao","year "+year,date),catalog).Result.Results.Single().Status==HolidayKeepsakeStatus.Saved,"independent saved year "+year);
        }
        var firstPage=new HolidayKeepsakeAlbumModel(runtime).LoadAsync("official:e-bao",0).Result;var lastPage=new HolidayKeepsakeAlbumModel(runtime).LoadAsync("official:e-bao",99).Result;
        Check(firstPage.Album.Records.Count==9&&firstPage.Scenes.Count==6&&firstPage.PageCount==2&&lastPage.Scenes.Count==3&&lastPage.Page==1,"actual saved count and bounded six-record pages");
        runtime.ShutdownAsync().Wait();
        var replacement=new HolidayKeepsakeRuntime(directory,gate,new HolidayCalendar(),new HolidayKeepsakeRegistry(new HolidayKeepsakeDefinition[0]),new RelationshipSceneProvider(asset=>{throw new IOException("source removed");}),validator);
        var retained=replacement.ReadSceneAsync(saved.CompanionId,saved.HolidayId,saved.InstanceKey).Result;Check(retained.CopyPngBytes().SequenceEqual(original),"removed registry/source still read exact saved scene");replacement.ShutdownAsync().Wait();
        Retry(Path.Combine(directory,"retry"),validator);Drain(Path.Combine(directory,"drain"),validator);return count;
    }
    static void Drain(string directory,IHolidayKeepsakeSceneValidator validator)
    {
        var opened=new ManualResetEventSlim(false);var release=new ManualResetEventSlim(false);var gate=new DataMaintenanceGate();
        var runtime=new HolidayKeepsakeRuntime(directory,gate,new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,new RelationshipSceneProvider(asset=>{opened.Set();release.Wait();return File.OpenRead(Path.Combine("assets",asset.RelativePath));}),validator);
        var encounter=new HolidayKeepsakeEncounter("official:e-bao","e-bao","before shutdown",new RelationshipTime(new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.Zero));
        var accepted=runtime.ObserveAsync(encounter,Catalog());Check(opened.Wait(10000),"shutdown fixture has actual admitted original-scene work");var shutdown=runtime.ShutdownAsync();
        Check(!shutdown.IsCompleted&&runtime.ObserveAsync(encounter,Catalog()).Result.Status==HolidayKeepsakeObservationStatus.Closed,"shutdown closes new admission and waits for the admitted capture");
        release.Set();shutdown.Wait();Check(accepted.Result.Results.Single().Status==HolidayKeepsakeStatus.Saved&&File.Exists(Path.Combine(directory,"commemorations/holiday-state.json")),"awaited shutdown preserves the admitted owner's saved card before source teardown");
    }
    static void Retry(string directory,IHolidayKeepsakeSceneValidator validator)
    {
        bool broken=true;var runtime=new HolidayKeepsakeRuntime(directory,new DataMaintenanceGate(),new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,new RelationshipSceneProvider(asset=>broken?new MemoryStream(new byte[8]):(Stream)File.OpenRead(Path.Combine("assets",asset.RelativePath))),validator);
        var pump=new HolidayKeepsakeEncounters(runtime);var date=new RelationshipTime(new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.Zero);
        var result=pump.ObserveAsync(true,true,"official:e-bao","e-bao","retry",HolidayEncounterKind.CompanionHome,date,Catalog()).Result;
        Check(result.Results.Single().Status==HolidayKeepsakeStatus.ResourceUnavailable&&runtime.LoadAsync("official:e-bao").Result.Records.Count==0,"bad scene never becomes an owned UI receipt");
        broken=false;result=pump.ObserveAsync(true,true,"official:e-bao","e-bao","retry",HolidayEncounterKind.CompanionHome,date,Catalog(),true).Result;
        Check(result.Results.Single().Status==HolidayKeepsakeStatus.Saved,"explicit retry takes fresh eligible time/source after failure");
        runtime.ShutdownAsync().Wait();
    }
}
