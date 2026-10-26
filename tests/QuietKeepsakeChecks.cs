using System;
using System.IO;
using System.Linq;
using Dafeiyu;
using Dafeiyu.Maintenance;
public static class QuietKeepsakeChecks
{
    public static int Run(string root,IHolidayKeepsakeSceneValidator validator)
    {
        int count=0;Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);count++;};
        var currentCatalog=new HolidayCatalog(Path.GetFullPath("assets/holidays"));
        foreach(var role in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}){
            string directory=Path.Combine(root,role),companion="official:"+role;
            var provider=new RelationshipSceneProvider(a=>File.OpenRead(Path.Combine("assets",a.RelativePath)));
            var runtime=new HolidayKeepsakeRuntime(directory,new DataMaintenanceGate(),new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,provider,validator);
            var oldCatalog=new HolidayCatalog(new[]{new OutfitDefinition(role,"halloween-2026","halloween","reviewed old fixture","1.0.0",true,false,new OutfitClipDefinition[0],null,null,null,null)});
            var time=new RelationshipTime(new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.Zero);
            var encounter=new HolidayKeepsakeEncounter(companion,role,"Captured "+role,time);
            var old=runtime.ObserveAsync(encounter,oldCatalog).GetAwaiter().GetResult().Results.Single();
            check(old.Status==HolidayKeepsakeStatus.Saved&&old.Record.OutfitVersion=="1.0.0","old reviewed source acquisition "+role);
            var saved=runtime.ReadSceneAsync(companion,"halloween","halloween:20261031").GetAwaiter().GetResult().CopyPngBytes();
            var again=runtime.ObserveAsync(encounter,currentCatalog).GetAwaiter().GetResult().Results.Single();
            check(again.Status==HolidayKeepsakeStatus.AlreadyOwned&&again.Record.OutfitVersion=="1.0.0","same year upgrade never rewrites old receipt "+role);
            var next=new HolidayKeepsakeEncounter(companion,role,"Next "+role,new RelationshipTime(new DateTime(2027,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.Zero));
            var revised=runtime.ObserveAsync(next,currentCatalog).GetAwaiter().GetResult().Results.Single();
            check(revised.Status==HolidayKeepsakeStatus.Saved&&revised.Record.OutfitVersion=="1.0.4","actual current catalog acquisition records1.0.4 "+role);
            runtime.ShutdownAsync().GetAwaiter().GetResult();
            var unavailable=new RelationshipSceneProvider(a=>{throw new IOException("Removed source fixture");});
            var reopened=new HolidayKeepsakeRuntime(directory,new DataMaintenanceGate(),new HolidayCalendar(),new HolidayKeepsakeRegistry(new HolidayKeepsakeDefinition[0]),unavailable,validator);
            var records=reopened.LoadAsync(companion).GetAwaiter().GetResult().Records;
            check(records.Count==2&&records.Any(r=>r.OutfitVersion=="1.0.0")&&records.Any(r=>r.OutfitVersion=="1.0.4"),"both historic revisions survive registry/source removal "+role);
            check(reopened.ReadSceneAsync(companion,"halloween","halloween:20261031").GetAwaiter().GetResult().CopyPngBytes().SequenceEqual(saved),"old scene export bytes remain exact "+role);
            check(reopened.ReadSceneAsync(companion,"halloween","halloween:20271031").GetAwaiter().GetResult().CopyPngBytes().SequenceEqual(saved),"new costume binds same original scene "+role);
            reopened.ShutdownAsync().GetAwaiter().GetResult();
        }return count;
    }
}
