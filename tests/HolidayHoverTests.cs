using System;
using System.IO;
namespace Dafeiyu.Tests
{
    public static class HolidayHoverTests
    {
        static int passed;
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;}
        static void Finish(HolidayController c,HolidayPlaybackTicket ticket)
        {var p=new HolidayPlayback();p.Start(ticket);p.MarkFirstFrameRendered();c.MarkHoverFirstFrame(ticket);for(int i=0;i<8;i++)p.Advance(.25);c.CompleteHover(ticket);}
        public static int Main()
        {
            try {
                var state=new HolidayState();bool fail=false;int writes=0;
                var c=new HolidayController(new HolidayCalendar(),HolidayRuntimeTests.Catalog(),state,s=>{writes++;if(fail)throw new IOException("blocked");});
                var x=HolidayRuntimeTests.Context(new DateTime(2026,10,31),0);c.Refresh(x);
                Check(c.TryBeginHover(.30)==null,"30 percent upper boundary ordinary");Check(c.TryBeginHover(0)==null,"one roll per entry");c.LeaveHover();
                Check(c.TryBeginHover(.8)==null,"ordinary roll consumes entry");x.CharacterId="marfu";c.Refresh(x);Check(c.TryBeginHover(0)==null,"switching role does not create a new pointer entry");x.CharacterId="e-bao";c.Refresh(x);c.LeaveHover();
                var ticket=c.TryBeginHover(.299);Check(ticket!=null,"holiday probability below boundary");c.CancelHover();c.LeaveHover();
                ticket=c.TryBeginHover(0);Check(ticket!=null,"preparation failure has no cooldown");c.MarkHoverFirstFrame(ticket);c.CancelHover();
                Check(!c.WardrobeVisible,"interrupted first frame does not unlock");c.LeaveHover();x.MonotonicSeconds=59;c.Refresh(x);Check(c.TryBeginHover(0)==null,"first frame starts 60 second cooldown");
                x.MonotonicSeconds=60;c.Refresh(x);Check(c.TryBeginHover(0)==null,"cooldown expiry does not reroll");c.LeaveHover();ticket=c.TryBeginHover(0);
                Check(ticket!=null,"reentry after cooldown can play");c.CompleteHover(ticket);Check(!c.WardrobeVisible,"unplayed completion cannot unlock");
                Finish(c,ticket);Check(c.WardrobeVisible && c.OwnedOutfits.Count==1 && c.UnlockNotice!=null,"full playback durably unlocks current character");
                int previous=writes;c.CompleteHover(ticket);Check(writes==previous,"duplicate completion idempotent");
                Check(state.ForCharacter("marfu").UnlockedOutfits.Count==0,"no other character unlocked");
                c.LeaveHover();x.MonotonicSeconds=120;c.Refresh(x);ticket=c.TryBeginHover(0);x.CharacterId="marfu";c.Refresh(x);Finish(c,ticket);Check(state.ForCharacter("marfu").UnlockedOutfits.Count==0,"late callback cannot unlock new role");
                c.LeaveHover();ticket=c.TryBeginHover(0);x.CanPlayEasterEgg=false;c.Refresh(x);Finish(c,ticket);Check(state.ForCharacter("marfu").UnlockedOutfits.Count==0,"busy preempts holiday");
                c.LeaveHover();x.CanPlayEasterEgg=true;x.MonotonicSeconds=240;c.Refresh(x);ticket=c.TryBeginHover(0);x.LocalTime=new DateTime(2026,11,1);c.Refresh(x);Finish(c,ticket);Check(state.ForCharacter("marfu").UnlockedOutfits.Count==0,"midnight cancels unlock");
                x.CharacterId="e-bao";c.Refresh(x);c.SelectOwnedOutfit("halloween-v1");c.LeaveHover();Check(c.TryBeginHover(.20)==null,"ordinary date 20 percent upper boundary");c.LeaveHover();Check(c.TryBeginHover(.199)!=null,"ordinary date below boundary");c.CancelHover();
                var pendingState=new HolidayState();var pending=new HolidayController(new HolidayCalendar(),HolidayRuntimeTests.Catalog(),pendingState,s=>{if(fail)throw new IOException("blocked");});x=HolidayRuntimeTests.Context(new DateTime(2026,10,31),0);pending.Refresh(x);fail=true;ticket=pending.TryBeginHover(0);Finish(pending,ticket);
                Check(pending.PersistencePending && pending.UnlockNotice==null && pending.WardrobeVisible,"failed save pending without permanent-unlock notice");fail=false;pending.RetrySave();Check(!pending.PersistencePending && pending.UnlockNotice!=null,"retry announces only durable unlock");
                var p=new HolidayPlayback();p.Start(ticket);p.MarkFirstFrameRendered();p.Advance(20);Check(p.Active && p.Frame==1,"stalled UI does not skip every pose");p.Cancel();Check(!p.Completed && !p.Active,"cancel never completes");
                Console.WriteLine("Holiday hover tests: "+passed+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL holiday hover: "+e);return 1;}
        }
    }
}
