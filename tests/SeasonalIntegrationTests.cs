using System;
using System.Globalization;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class SeasonalIntegrationTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);checks++;}
        public static int Main()
        {
            try {
                var calendar=new HolidayCalendar();
                Func<DateTime,string,bool> on=(date,id)=>calendar.Resolve(date).Any(x=>x.Id==id);
                Check(on(new DateTime(2027,2,14),"valentines-day"),"Valentine only on February 14");
                Check(!on(new DateTime(2027,2,13),"valentines-day")&&!on(new DateTime(2027,2,15),"valentines-day"),"Valentine boundaries");
                Check(on(new DateTime(2027,10,1),"national-day")&&on(new DateTime(2027,10,7),"national-day")&&!on(new DateTime(2027,10,8),"national-day"),"National day inclusive October 1 through 7");
                Check(!on(new DateTime(2027,12,23),"christmas")&&on(new DateTime(2027,12,24),"christmas")&&on(new DateTime(2027,12,25),"christmas")&&!on(new DateTime(2027,12,26),"christmas"),"Christmas December 24 through 25");
                var lunar=new ChineseLunisolarCalendar();
                foreach(int year in new[]{2026,2027,2028,2030}) {
                    DateTime first=lunar.ToDateTime(year,1,1,0,0,0,0);
                    Check(!on(first.AddDays(-2),"spring-festival")&&on(first.AddDays(-1),"spring-festival")&&on(first.AddDays(13),"spring-festival")&&!on(first.AddDays(14),"spring-festival"),"Spring eve through lunar January 14: "+year);
                    Check(on(first.AddDays(14),"lantern")&&!on(first.AddDays(15),"lantern"),"Lantern day only: "+year);
                }
                var state=new HolidayState();
                state.Unlock("official:e-bao","halloween-2026");
                var official=state.ForCharacter("official:e-bao");
                var local=state.ForCharacter("custom-pack:e-bao");
                Check(local.UnlockedOutfits.Count==0,"local and official unlocks isolated");
                Check(official.UnlockedOutfits.Count==1&&!official.UnlockedOutfits.Contains("christmas-classic"),"Halloween never unlocks Christmas");
                official.PreferredOutfitId="halloween-2026";local.Enabled=false;
                Check(official.Enabled&&local.PreferredOutfitId==null,"toggles and manual choices isolated");
                Check(Object.ReferenceEquals(official,state.ForCharacter("e-bao")),"legacy official caller retains same state");
                Check(state.SchemaVersion==2,"source-scoped state schema");
                var egg=new OutfitClipDefinition("egg.png",8,8,4,false,480,520,0,0,"egg","default");
                var halloween=new OutfitDefinition("e-bao","halloween-2026","halloween","Halloween","1.0.4",true,true,new[]{egg},egg,egg,"preview.png","Celebrate");
                var christmas=new OutfitDefinition("e-bao","christmas-classic","christmas","Christmas","1.0.1",true,false,new[]{egg},egg,egg,"preview.png","Celebrate");
                var controller=new HolidayController(calendar,new HolidayCatalog(new[]{halloween,christmas}),state,_=>{});
                var context=new HolidayContext{PersonaId="pet",CharacterId="e-bao",StateKey="official:e-bao",LocalTime=new DateTime(2027,12,23,23,59,59),CanPlayEasterEgg=true,CanAnimateTransition=true,ResourceVersion="one",MonotonicSeconds=100};
                controller.Refresh(context);Check(controller.EffectiveOutfit==halloween,"outside event uses owned preferred outfit");
                context.LocalTime=new DateTime(2027,12,24,0,0,0);controller.Refresh(context);
                Check(controller.EffectiveOutfit==christmas,"local midnight automatically selects event outfit");
                Check(controller.OwnedOutfits.Count==1&&!controller.SelectOwnedOutfit("christmas-classic"),"temporary wearing never implies unlocked permanent choice");
                var ticket=controller.TryBeginHover(0);Check(ticket!=null&&ticket.CanUnlock,"current event alone can offer its own unlock");
                controller.MarkHoverFirstFrame(ticket);ticket.PlaybackCompleted=true;
                Check(controller.CompleteHover(ticket)&&controller.OwnedOutfits.Count==2,"completed visible current event unlocks exactly one outfit");
                context.LocalTime=new DateTime(2027,12,26,0,0,0);context.CanAnimateTransition=false;controller.Refresh(context);
                Check(controller.EffectiveOutfit==halloween,"event end restores owned preference even with animation paused");
                context.StateKey="custom-pack:e-bao";controller.Refresh(context);
                Check(!controller.Enabled&&controller.OwnedOutfits.Count==0&&controller.EffectiveOutfit==null,"same persona and character cannot share another source state");
                Console.WriteLine("Seasonal integration tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
