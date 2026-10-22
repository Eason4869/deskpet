using System;
using System.Collections.Generic;
using System.IO;
namespace Dafeiyu.Tests
{
    public static class HolidayRuntimeTests
    {
        static int passed;
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;}
        internal static HolidayCatalog Catalog()
        {
            var clip=new OutfitClipDefinition("egg.png",8,8,4,false,1,1,0,0,"easter-egg","default");
            return new HolidayCatalog(new[]{new OutfitDefinition("e-bao","halloween-v1","halloween","万圣节","1",true,true,new[]{clip},null,clip,null,"讨糖"),new OutfitDefinition("marfu","halloween-v1","halloween","万圣节","1",true,true,new[]{clip},null,clip,null,"讨糖")});
        }
        internal static HolidayContext Context(DateTime time,double monotonic)
        {return new HolidayContext{PersonaId="persona",CharacterId="e-bao",LocalTime=time,MonotonicSeconds=monotonic,CanPlayEasterEgg=true,CanAnimateTransition=true,ResourceVersion="v1"};}
        public static int Main()
        {
            try {
                var state=new HolidayState();int saves=0;
                var c=new HolidayController(new HolidayCalendar(),Catalog(),state,s=>saves++);
                var x=Context(new DateTime(2026,10,30,23,59,59),0);c.Refresh(x);long revision=c.Revision;
                c.Refresh(x);Check(c.Revision==revision,"unchanged refresh keeps revision");
                x.LocalTime=x.LocalTime.AddSeconds(1);c.Refresh(x);
                Check(c.EffectiveOutfit!=null && !c.WardrobeVisible && c.OwnedOutfits.Count==0,"midnight temporary outfit does not unlock");
                revision=c.Revision;x.MonotonicSeconds=1;c.Refresh(x);Check(c.Revision==revision,"monotonic refresh keeps revision");
                Check(c.TransitionRequested,"entering event requests transition");c.AcknowledgeTransition();c.Refresh(x);Check(!c.TransitionRequested,"transition once per instance");
                var resumed=new HolidayController(new HolidayCalendar(),Catalog(),state,s=>{});resumed.Refresh(x);Check(!resumed.TransitionRequested && resumed.EffectiveOutfit!=null,"restart catches appearance without replaying transition");
                state.Unlock("e-bao","halloween-v1");c.SelectOwnedOutfit(null);Check(c.EffectiveOutfit==null,"manual original overrides current event");
                resumed=new HolidayController(new HolidayCalendar(),Catalog(),state,s=>{});resumed.Refresh(x);Check(resumed.EffectiveOutfit==null,"manual override survives restart");
                c.SetEnabled(false);x.LocalTime=new DateTime(2027,10,31);c.Refresh(x);Check(c.EffectiveOutfit==null && !c.Enabled,"disabled remains disabled next event");
                c.SetEnabled(true);Check(c.EffectiveOutfit!=null,"enabled next event uses temporary appearance");
                var greeting=c.CreateGreetingContext(0.9);Check(greeting!=null && greeting.UseHolidayPool,"first holiday greeting prioritized");
                c.MarkGreetingShown(greeting);Check(!c.CreateGreetingContext(0.9).UseHolidayPool,"later greeting pool probability");
                x.LocalTime=new DateTime(2027,11,1);c.Refresh(x);Check(!c.IsGreetingCurrent(greeting) && c.EffectiveOutfit==null,"midnight rejects stale greeting and restores preference");
                x.LocalTime=new DateTime(2027,10,31);x.CanAnimateTransition=false;c.Refresh(x);Check(!c.TransitionRequested,"rollback and busy do not replay transition");
                var t=new HolidayTransition();t.Start("halloween-v1");t.Advance(.249);Check(!t.CommitPending,"transition covers before commit");t.Advance(.101);Check(t.TakeCommit() && !t.TakeCommit(),"transition commit once");t.Advance(1);Check(!t.Active && !t.TakeCommit(),"transition ends");
                t.Start(null);t.FinishImmediately();Check(t.TakeCommit() && !t.Active && t.Clouds.Count==0,"interrupted transition commits latest target and removes clouds");
                var egg=Catalog().Outfits[0].EasterEgg;
                var multiCatalog=new HolidayCatalog(new[]{new OutfitDefinition("e-bao","national","national-day","国庆","1",true,true,new[]{egg},null,egg,null,"动作"),new OutfitDefinition("e-bao","autumn","mid-autumn","中秋","1",true,true,new[]{egg},null,egg,null,"动作")});
                var multi=new HolidayController(new HolidayCalendar(),multiCatalog,new HolidayState(),s=>{});
                var civil=Context(new DateTime(2020,10,1),0);multi.Refresh(civil);Check(multi.EffectiveOutfit.OutfitId=="autumn","overlap prefers ready higher priority");multi.AcknowledgeTransition();
                civil.LocalTime=civil.LocalTime.AddDays(1);multi.Refresh(civil);Check(multi.EffectiveOutfit.OutfitId=="national","overlap returns to longer event");multi.AcknowledgeTransition();
                civil.LocalTime=civil.LocalTime.AddDays(1);multi.Refresh(civil);Check(!multi.TransitionRequested,"multi-day event does not transform every midnight");
                civil.CanAnimateTransition=false;civil.CanPlayEasterEgg=false;civil.LocalTime=new DateTime(2020,10,8);multi.Refresh(civil);Check(multi.EffectiveOutfit==null && !multi.WardrobeVisible,"busy resume after event does not unlock");
                var noArt=new HolidayController(new HolidayCalendar(),new HolidayCatalog(new[]{new OutfitDefinition("e-bao","halloween-v1","halloween","missing","1",false,false,new[]{egg},null,egg,null,"动作")}),state,s=>{});noArt.Refresh(Context(new DateTime(2026,10,31),0));
                Check(noArt.EffectiveOutfit==null && noArt.OwnedOutfits.Count==1 && !noArt.OwnedOutfits[0].Ready,"missing artwork preserves unavailable ownership");
                var trusted=Context(new DateTime(2026,10,31),0);trusted.CharacterId=null;c.Refresh(trusted);Check(c.EffectiveOutfit==null && c.OwnedOutfits.Count==0 && !c.Enabled,"untrusted custom character cannot wear builtin outfit");
                RuntimeUnavailablePreservesOwnership();
                RuntimeUnavailableDoesNotChooseAnotherOutfit();
                Check(saves>0,"state changes saved");Console.WriteLine("Holiday runtime tests: "+passed+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL holiday runtime: "+e);return 1;}
        }
        static void RuntimeUnavailablePreservesOwnership()
        {
            var catalog=Catalog();var owned=new HolidayState();owned.Unlock("e-bao","halloween-v1");owned.ForCharacter("e-bao").PreferredOutfitId="halloween-v1";
            int writes=0,changes=0;var controller=new HolidayController(new HolidayCalendar(),catalog,owned,s=>writes++);
            var input=Context(new DateTime(2026,10,31),0);controller.Refresh(input);controller.Changed+=(sender,args)=>changes++;
            var outfit=controller.EffectiveOutfit;var greeting=controller.CreateGreetingContext(0);var pending=controller.TryBeginHover(0);
            Check(pending!=null && controller.MarkHoverFirstFrame(pending),"runtime failure test starts rendered playback");
            long revision=controller.Revision;int saved=writes;
            Check(!controller.ReportUnavailable(Catalog().Find("e-bao","halloween-v1")) && !controller.ReportUnavailable(null),"foreign same-version and null definitions cannot mark unavailable");
            Check(controller.EffectiveOutfit==outfit && outfit.Ready && changes==0 && controller.Revision==revision,"foreign report has no observable side effects");
            Check(controller.ReportUnavailable(outfit),"current catalog definition can report native decode rejection");
            Check(!outfit.Ready && controller.EffectiveOutfit==null && !controller.TransitionRequested,"runtime unavailable immediately falls back to original without transition");
            Check(controller.Revision==revision+1 && changes==1,"active runtime rejection updates revision and observers once");
            Check(pending.Cancelled && !controller.MarkHoverFirstFrame(pending) && !controller.CompleteHover(pending),"runtime rejection cancels outstanding playback ticket");
            Check(!controller.IsGreetingCurrent(greeting),"runtime rejection invalidates old outfit greeting");
            var actual=controller.CreateGreetingContext(0);Check(actual!=null && actual.HolidayId=="halloween" && actual.OutfitId==null && actual.OutfitName==null && !actual.IsOwned,"new greeting describes original appearance on actual holiday");
            Check(controller.WardrobeVisible && controller.OwnedOutfits.Count==1 && !controller.OwnedOutfits[0].Ready && owned.ForCharacter("e-bao").UnlockedOutfits.Contains(outfit.OutfitId),"unavailable picker retains disabled owned entry and discovery");
            Check(controller.PreferredOutfitId==outfit.OutfitId && writes==saved,"runtime unavailable does not persist or erase preference");
            Check(!controller.SelectOwnedOutfit(outfit.OutfitId),"unavailable owned outfit cannot be selected");
            revision=controller.Revision;int notifications=changes;
            Check(!controller.ReportUnavailable(outfit),"repeated native rejection is idempotent");
            for(int i=1;i<=3;i++){input.MonotonicSeconds=i;controller.Refresh(input);controller.LeaveHover();Check(controller.TryBeginHover(0)==null,"refresh does not retry failed outfit");}
            Check(controller.EffectiveOutfit==null && controller.Revision==revision && changes==notifications,"unavailable refresh has no event or revision retry loop");
            Check(catalog.Find("marfu","halloween-v1").Ready,"one rejection does not disable another character");
            var recovered=new HolidayController(new HolidayCalendar(),Catalog(),owned,s=>{});recovered.Refresh(input);Check(recovered.EffectiveOutfit!=null && recovered.EffectiveOutfit.Ready,"fresh catalog can recover native availability without losing ownership");
        }
        static void RuntimeUnavailableDoesNotChooseAnotherOutfit()
        {
            var primary=Catalog().Find("e-bao","halloween-v1");var alternate=new OutfitDefinition("e-bao","alternate-owned","halloween","another outfit","2",true,true,primary.Clips,null,primary.EasterEgg,null,"action");
            var catalog=new HolidayCatalog(new[]{primary,alternate});var state=new HolidayState();state.Unlock("e-bao",alternate.OutfitId);state.ForCharacter("e-bao").PreferredOutfitId=alternate.OutfitId;
            var c=new HolidayController(new HolidayCalendar(),catalog,state,s=>{});var input=Context(new DateTime(2026,10,31),0);c.Refresh(input);
            Check(c.EffectiveOutfit==primary,"active outfit temporarily overrides different owned preference");
            var foreignVersion=new OutfitDefinition(primary.CharacterId,primary.OutfitId,primary.HolidayId,primary.Name,"old",true,true,primary.Clips,null,primary.EasterEgg,null,"action");
            Check(!c.ReportUnavailable(foreignVersion) && primary.Ready,"stale definition version cannot invalidate current catalog instance");
            Check(c.ReportUnavailable(primary) && c.EffectiveOutfit==null,"failed active outfit immediately uses original even with valid alternate preference");
            c.Refresh(input);Check(c.EffectiveOutfit==null,"ordinary refresh cannot replace rejection fallback with automatic alternative");
            Check(c.SelectOwnedOutfit(alternate.OutfitId) && c.EffectiveOutfit==alternate,"explicit selection can leave original fallback for valid owned outfit");
            var pending=c.TryBeginHover(0);long revision=c.Revision;
            Check(!c.ReportUnavailable(primary) && c.EffectiveOutfit==alternate && c.Revision==revision && c.MarkHoverFirstFrame(pending),"duplicate earlier rejection does not interrupt different valid outfit");
        }
    }
}
