using System;
using System.IO;
using System.Linq;

namespace Dafeiyu.Tests
{
    public static class HolidayStateTests
    {
        static int passed;
        static void Check(bool value,string label){if(!value)throw new Exception(label);passed++;}
        public static int Main()
        {
            string root=Path.Combine(Path.GetTempPath(),"deskpet-holiday-state-"+Guid.NewGuid().ToString("N"));
            try {
                passed=0;Directory.CreateDirectory(root);string path=Path.Combine(root,"holiday-state.json");
                var store=new HolidayStateStore(path);var state=store.Load();
                Check(!state.WardrobeDiscovered && state.Characters.Count==0,"fresh install hides wardrobe");
                var ebao=state.ForCharacter("e-bao");Check(ebao.Enabled,"new character defaults enabled");
                state.Unlock("e-bao","halloween-v1");ebao.Enabled=false;ebao.PreferredOutfitId="halloween-v1";
                ebao.Activities.Add(new HolidayActivityState{InstanceKey="halloween:20261031",StartDate=new DateTime(2026,10,31),ManualOverride=true,TransitionShown=true,GreetingShown=true});
                store.Save(state);var loaded=new HolidayStateStore(path).Load();
                Check(loaded.WardrobeDiscovered && loaded.ForCharacter("e-bao").UnlockedOutfits.Contains("halloween-v1"),"unlock and discovery persist together");
                Check(!loaded.ForCharacter("e-bao").Enabled && loaded.ForCharacter("e-bao").PreferredOutfitId=="halloween-v1","toggle and owned preference persist");
                Check(loaded.ForCharacter("e-bao").Activities.Single().ManualOverride && loaded.ForCharacter("e-bao").Activities.Single().GreetingShown,"activity preferences survive restart");
                Check(loaded.ForCharacter("marfu").UnlockedOutfits.Count==0,"ownership does not leak across characters");
                loaded.Unlock("e-bao","halloween-v1");Check(loaded.ForCharacter("e-bao").UnlockedOutfits.Count==1,"repeated unlock is idempotent");
                store.Save(loaded);Check(File.Exists(path+".bak"),"atomic replacement retains backup");
                File.WriteAllText(path,"{broken");var recoveredStore=new HolidayStateStore(path);var recovered=recoveredStore.Load();
                Check(recovered.WardrobeDiscovered && recovered.ForCharacter("e-bao").UnlockedOutfits.Count==1,"corrupt current state recovers backup");
                Check(!String.IsNullOrEmpty(recoveredStore.Warning) && Directory.GetFiles(root,"holiday-state.json.invalid-*").Length==1,"damaged file is preserved with warning");
                recoveredStore.Save(recovered);File.WriteAllText(path,"{broken again");
                Check(new HolidayStateStore(path).Load().WardrobeDiscovered,"saving recovered state preserves good backup");
                var fresh=new HolidayState();var role=fresh.ForCharacter("xiaobei");fresh.Unlock("xiaobei","halloween-v1");
                for(int i=0;i<4100;i++)role.Activities.Add(new HolidayActivityState{InstanceKey="event:"+i,StartDate=new DateTime(2000,1,1).AddDays(i)});
                string large=Path.Combine(root,"pruned.json");new HolidayStateStore(large).Save(fresh);var pruned=new HolidayStateStore(large).Load();
                Check(pruned.ForCharacter("xiaobei").Activities.Count==4096 && pruned.ForCharacter("xiaobei").Activities.All(x=>x.StartDate>=new DateTime(2000,1,5)),"only oldest activity records are pruned");
                Check(pruned.ForCharacter("xiaobei").UnlockedOutfits.Contains("halloween-v1"),"pruning never forgets ownership");
                string blocker=Path.Combine(root,"not-a-directory");File.WriteAllText(blocker,"keep");bool failed=false;
                try{new HolidayStateStore(Path.Combine(blocker,"state.json")).Save(fresh);}catch(IOException){failed=true;}
                Check(failed && fresh.WardrobeDiscovered,"failed write is reported and in-memory unlock remains");
                string future=Path.Combine(root,"future.json");string futureJson="{\"SchemaVersion\":3,\"WardrobeDiscovered\":true}";File.WriteAllText(future,futureJson);
                var futureStore=new HolidayStateStore(future);futureStore.Load();failed=false;try{futureStore.Save(new HolidayState());}catch(InvalidOperationException){failed=true;}
                Check(failed && File.ReadAllText(future)==futureJson,"unknown future schema is not overwritten");
                string futureShape=Path.Combine(root,"future-shape.json");string changedJson="{\"SchemaVersion\":3,\"Characters\":\"new-format\"}";File.WriteAllText(futureShape,changedJson);
                var changedStore=new HolidayStateStore(futureShape);changedStore.Load();failed=false;try{changedStore.Save(new HolidayState());}catch(InvalidOperationException){failed=true;}
                Check(failed && File.Exists(futureShape) && File.ReadAllText(futureShape)==changedJson,"future field shapes are checked only after schema version");
                string locked=Path.Combine(root,"locked.json");var lockedStore=new HolidayStateStore(locked);lockedStore.Save(state);
                string originalLocked=File.ReadAllText(locked);
                // Unix FileStream uses an exclusive advisory lock only for None; Delete uses a shared lock.
                // Both streams cooperate with that lock, producing an actual read IOException on either platform.
                using(var held=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
                    bool readDenied=false;
                    try{using(var attempt=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.Read)){} }catch(IOException){readDenied=true;}
                    Check(readDenied,"exclusive fixture genuinely prevents the production read mode");
                    var unreadable=lockedStore.Load();
                    Check(!unreadable.WardrobeDiscovered && !String.IsNullOrEmpty(lockedStore.Warning),"transient read failure reports safe defaults and a warning");
                    Check(File.Exists(locked) && Directory.GetFiles(root,"locked.json.invalid-*").Length==0,"sharing violation is not treated as corrupt data");
                }
                failed=false;try{lockedStore.Save(new HolidayState());}catch(InvalidOperationException){failed=true;}
                Check(failed && File.ReadAllText(locked)==originalLocked,"transient read failure blocks overwrite after lock release until a successful reload");
                var reloaded=lockedStore.Load();
                Check(reloaded.WardrobeDiscovered && String.IsNullOrEmpty(lockedStore.Warning),"successful reload on the same store restores ownership and clears warning");
                lockedStore.Save(reloaded);
                Check(new HolidayStateStore(locked).Load().WardrobeDiscovered,"successful reload on the same store permits saving again");
                string invalid=Path.Combine(root,"invalid.json");File.WriteAllText(invalid,"{broken");var invalidStore=new HolidayStateStore(invalid);
                Check(!invalidStore.Load().WardrobeDiscovered && !String.IsNullOrEmpty(invalidStore.Warning),"no backup gives safe defaults and warning");
                string oversize=Path.Combine(root,"oversize.json");File.WriteAllText(oversize,new string(' ',1024*1024+1));var oversizeStore=new HolidayStateStore(oversize);
                Check(!oversizeStore.Load().WardrobeDiscovered && !String.IsNullOrEmpty(oversizeStore.Warning),"oversized state is not parsed");
                var invalidState=new HolidayState();invalidState.ForCharacter("e-bao").PreferredOutfitId="unowned";failed=false;
                try{new HolidayStateStore(Path.Combine(root,"bad-selection.json")).Save(invalidState);}catch(InvalidDataException){failed=true;}
                Check(failed,"unowned preferred outfit is rejected");
                Console.WriteLine("Holiday state tests: "+passed+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL holiday state: "+error);return 1;}
            finally {
                string resolved=Path.GetFullPath(root),temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(resolved.StartsWith(temporary,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("deskpet-holiday-state-",StringComparison.Ordinal) && Directory.Exists(resolved))Directory.Delete(resolved,true);
            }
        }
    }
}
