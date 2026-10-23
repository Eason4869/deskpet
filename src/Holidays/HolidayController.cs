using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Dafeiyu
{
    public sealed class HolidayContext
    {
        public string PersonaId {get;set;} public string CharacterId {get;set;} public DateTime LocalTime {get;set;}
        public double MonotonicSeconds {get;set;} public bool CanPlayEasterEgg {get;set;} public bool CanAnimateTransition {get;set;}
        public string ResourceVersion {get;set;}
        internal HolidayContext Copy(){return (HolidayContext)MemberwiseClone();}
    }
    public sealed class HolidayPlaybackTicket
    {
        public Guid Token {get;private set;} public string CharacterId {get;private set;} public string PersonaId {get;private set;}
        public string OutfitId {get;private set;} public string InstanceKey {get;private set;} public DateTime Date {get;private set;}
        public long Revision {get;private set;} public string ResourceVersion {get;private set;} public bool CanUnlock {get;private set;}
        public OutfitClipDefinition Clip {get;private set;} public bool FirstFrameRendered {get;internal set;} public bool PlaybackCompleted {get;internal set;}
        internal bool Cancelled;internal bool CooldownRecorded;
        internal HolidayPlaybackTicket(HolidayContext context,OutfitDefinition outfit,HolidayOccurrence occurrence,long revision,bool unlock)
        {Token=Guid.NewGuid();CharacterId=context.CharacterId;PersonaId=context.PersonaId;OutfitId=outfit.OutfitId;InstanceKey=occurrence==null?null:occurrence.InstanceKey;Date=context.LocalTime.Date;Revision=revision;ResourceVersion=context.ResourceVersion;CanUnlock=unlock;Clip=outfit.EasterEgg;}
    }
    public sealed class HolidayGreetingContext
    {
        public string PersonaId {get;internal set;} public string CharacterId {get;internal set;} public DateTime Date {get;internal set;}
        public string HolidayId {get;internal set;} public string InstanceKey {get;internal set;} public string Stage {get;internal set;}
        public bool UseHolidayPool {get;internal set;} public string OutfitId {get;internal set;} public string OutfitName {get;internal set;}
        public bool IsOwned {get;internal set;} public long Revision {get;internal set;} public HolidayOccurrence Occurrence {get;internal set;}
        internal HolidayGreetingContext(HolidayContext context,HolidayOccurrence occurrence,OutfitDefinition outfit,bool owned,long revision,bool useHolidayPool)
        {PersonaId=context.PersonaId;CharacterId=context.CharacterId;Date=context.LocalTime.Date;Occurrence=occurrence;HolidayId=occurrence.Id;InstanceKey=occurrence.InstanceKey;Stage=occurrence.Stage;OutfitId=outfit==null?null:outfit.OutfitId;OutfitName=outfit==null?null:outfit.Name;IsOwned=owned;Revision=revision;UseHolidayPool=useHolidayPool;}
    }
    public sealed class HolidayController
    {
        readonly HolidayCalendar calendar;HolidayCatalog catalog;readonly HolidayState state;readonly Action<HolidayState> save;
        readonly Dictionary<string,double> cooldowns=new Dictionary<string,double>(StringComparer.Ordinal);
        HolidayContext context;IList<HolidayOccurrence> events=new List<HolidayOccurrence>();HolidayPlaybackTicket ticket;
        bool hoverConsumed;bool runtimeOriginalFallback;string fingerprint;string pendingUnlockNotice;
        OutfitDefinition rejectedOutfit;
        public event EventHandler Changed;
        public HolidayCatalog Catalog {get{return catalog;}}
        public OutfitDefinition EffectiveOutfit {get;private set;} public HolidayOccurrence ActiveOccurrence {get;private set;}
        public long Revision {get;private set;} public bool TransitionRequested {get;private set;}
        public bool PersistencePending {get;private set;} public string PersistenceWarning {get;private set;} public string UnlockNotice {get;private set;}
        public bool WardrobeVisible {get{return state.WardrobeDiscovered;}}
        public bool Enabled {get{return context!=null && ValidCharacter(context.CharacterId) && Role().Enabled;}}
        public string PreferredOutfitId {get{return context!=null && ValidCharacter(context.CharacterId)?Role().PreferredOutfitId:null;}}
        public IList<OutfitDefinition> OwnedOutfits
        {
            get {
                var result=new List<OutfitDefinition>();
                if(context!=null && ValidCharacter(context.CharacterId))foreach(var id in Role().UnlockedOutfits){var outfit=catalog.Find(context.CharacterId,id);if(outfit!=null)result.Add(outfit);}
                return new ReadOnlyCollection<OutfitDefinition>(result);
            }
        }
        public HolidayController(HolidayCalendar calendar,HolidayCatalog catalog,HolidayStateStore store):this(calendar,catalog,Load(store),store.Save){PersistenceWarning=store.Warning;}
        static HolidayState Load(HolidayStateStore store){if(store==null)throw new ArgumentNullException("store");return store.Load();}
        public HolidayController(HolidayCalendar calendar,HolidayCatalog catalog,HolidayState state,Action<HolidayState> save)
        {if(calendar==null || catalog==null || state==null || save==null)throw new ArgumentNullException("Holiday dependencies");this.calendar=calendar;this.catalog=catalog;this.state=state;this.save=save;}
        public void ReplaceCatalog(HolidayCatalog replacement)
        {
            if(replacement==null)throw new ArgumentNullException("replacement");
            if(Object.ReferenceEquals(catalog,replacement))return;
            catalog=replacement;
            if(runtimeOriginalFallback && rejectedOutfit!=null) {
                var repaired=catalog.Find(rejectedOutfit.CharacterId,rejectedOutfit.OutfitId);
                if(repaired!=null && repaired.Ready && !Object.ReferenceEquals(repaired,rejectedOutfit)){runtimeOriginalFallback=false;rejectedOutfit=null;}
            }
            if(context==null)Notify();else Recompute(true);
        }
        bool ValidCharacter(string id){return id=="e-bao" || id=="dafeiyu" || id=="marfu" || id=="xiaobei" || id=="gugugaga";}
        HolidayCharacterState Role(){return state.ForCharacter(context.CharacterId);}
        HolidayActivityState Activity(HolidayOccurrence occurrence)
        {
            var role=Role();var activity=role.Activities.FirstOrDefault(x=>x.InstanceKey==occurrence.InstanceKey);
            if(activity==null){activity=new HolidayActivityState{InstanceKey=occurrence.InstanceKey,StartDate=occurrence.StartDate};role.Activities.Add(activity);}return activity;
        }
        public void Refresh(HolidayContext value)
        {
            if(value==null)throw new ArgumentNullException("value");
            if(Double.IsNaN(value.MonotonicSeconds) || Double.IsInfinity(value.MonotonicSeconds))throw new ArgumentException("Invalid monotonic clock.");
            if(context!=null && (context.PersonaId!=value.PersonaId || context.CharacterId!=value.CharacterId || context.LocalTime.Date!=value.LocalTime.Date || context.ResourceVersion!=value.ResourceVersion)){runtimeOriginalFallback=false;rejectedOutfit=null;}
            context=value.Copy();events=calendar.Resolve(context.LocalTime.Date);
            Recompute();
            if(ticket!=null && (!Current(ticket) || !context.CanPlayEasterEgg))CancelHover();
        }
        void Recompute(bool notifyEvenIfUnchanged=false)
        {
            bool saved=false;var previous=EffectiveOutfit;HolidayOccurrence active=null;OutfitDefinition desired=null;
            if(ValidCharacter(context.CharacterId)) {
                var role=Role();var automatic=catalog.Active(context.CharacterId,events);
                if(automatic!=null)active=events.First(x=>x.Id==automatic.HolidayId);
                if(role.Enabled && !runtimeOriginalFallback) {
                    if(active!=null) {
                        var activity=Activity(active);
                        if(!activity.ManualOverride)desired=automatic;
                        if(!activity.TransitionShown){activity.TransitionShown=true;saved=true;if(desired!=null && context.CanAnimateTransition)TransitionRequested=true;}
                    }
                    if(desired==null && !String.IsNullOrEmpty(role.PreferredOutfitId) && role.UnlockedOutfits.Contains(role.PreferredOutfitId)) {
                        var preferred=catalog.Find(context.CharacterId,role.PreferredOutfitId);if(preferred!=null && preferred.Ready)desired=preferred;
                    }
                }
            }
            EffectiveOutfit=desired;ActiveOccurrence=active;
            // Busy/hidden/paused frames must never leave a delayed celebration queued.
            if(!context.CanAnimateTransition)TransitionRequested=false;
            string next=context.PersonaId+"|"+context.CharacterId+"|"+context.LocalTime.Date.Ticks+"|"+String.Join(",",events.Select(x=>x.InstanceKey+":"+x.Stage))+"|"+Enabled+"|"+(desired==null?"":desired.OutfitId+":"+desired.Version+":"+desired.SourceKey+":"+IsOwned(desired.OutfitId))+"|"+context.ResourceVersion;
            bool changed=next!=fingerprint;
            if(changed){fingerprint=next;Revision++;if(ticket!=null && !Current(ticket))CancelHover();}
            if(saved)Persist();
            if(changed || saved || notifyEvenIfUnchanged)Notify();
        }
        public void AcknowledgeTransition(){TransitionRequested=false;}
        public bool ReportUnavailable(OutfitDefinition outfit)
        {
            if(!catalog.ReportUnavailable(outfit))return false;
            if(Object.ReferenceEquals(EffectiveOutfit,outfit)) {
                runtimeOriginalFallback=true;rejectedOutfit=outfit;TransitionRequested=false;CancelHover();Recompute();
            }else Notify();
            return true;
        }
        public void SetEnabled(bool enabled)
        {
            if(context==null || !ValidCharacter(context.CharacterId) || Role().Enabled==enabled)return;
            Role().Enabled=enabled;runtimeOriginalFallback=false;rejectedOutfit=null;CancelHover();if(!enabled)TransitionRequested=false;Recompute();Persist();Notify();
        }
        public bool SelectOwnedOutfit(string outfitId)
        {
            if(context==null || !ValidCharacter(context.CharacterId))return false;
            var role=Role();if(!String.IsNullOrEmpty(outfitId)) {var selected=catalog.Find(context.CharacterId,outfitId);if(!role.UnlockedOutfits.Contains(outfitId) || selected==null || !selected.Ready || !role.Enabled)return false;}
            runtimeOriginalFallback=false;rejectedOutfit=null;
            role.PreferredOutfitId=String.IsNullOrEmpty(outfitId)?null:outfitId;
            if(ActiveOccurrence!=null)Activity(ActiveOccurrence).ManualOverride=true;
            CancelHover();Recompute();TransitionRequested=context.CanAnimateTransition && role.Enabled;Persist();Notify();return true;
        }
        bool IsOwned(string outfitId){return ValidCharacter(context.CharacterId) && Role().UnlockedOutfits.Contains(outfitId);}
        public void LeaveHover(){hoverConsumed=false;}
        public HolidayPlaybackTicket TryBeginHover(double randomValue)
        {
            if(hoverConsumed)return null;hoverConsumed=true;
            if(Double.IsNaN(randomValue) || randomValue<0 || randomValue>=1 || context==null || !context.CanPlayEasterEgg || !Enabled || EffectiveOutfit==null || !EffectiveOutfit.Ready || EffectiveOutfit.EasterEgg==null || ticket!=null)return null;
            bool inEvent=ActiveOccurrence!=null && ActiveOccurrence.Id==EffectiveOutfit.HolidayId;
            if(randomValue>=(inEvent?.30:.20) || !inEvent && !IsOwned(EffectiveOutfit.OutfitId))return null;
            double last;if(cooldowns.TryGetValue(context.CharacterId+":"+EffectiveOutfit.OutfitId,out last) && context.MonotonicSeconds-last<60)return null;
            ticket=new HolidayPlaybackTicket(context,EffectiveOutfit,inEvent?ActiveOccurrence:null,Revision,inEvent && !IsOwned(EffectiveOutfit.OutfitId));return ticket;
        }
        bool Current(HolidayPlaybackTicket value)
        {return value!=null && !value.Cancelled && context!=null && value.Revision==Revision && value.PersonaId==context.PersonaId && value.CharacterId==context.CharacterId && value.Date==context.LocalTime.Date && value.ResourceVersion==context.ResourceVersion && EffectiveOutfit!=null && EffectiveOutfit.OutfitId==value.OutfitId;}
        bool IsTicket(HolidayPlaybackTicket value){return ticket!=null && Object.ReferenceEquals(ticket,value) && Current(value);}
        public bool MarkHoverFirstFrame(HolidayPlaybackTicket value)
        {
            if(!IsTicket(value) || !context.CanPlayEasterEgg)return false;
            if(!value.FirstFrameRendered){value.FirstFrameRendered=true;}
            // Playback may set FirstFrameRendered before the controller observes it.
            string key=value.CharacterId+":"+value.OutfitId;
            if(!value.CooldownRecorded){cooldowns[key]=context.MonotonicSeconds;value.CooldownRecorded=true;}
            return true;
        }
        public bool CompleteHover(HolidayPlaybackTicket value)
        {
            if(!IsTicket(value) || !context.CanPlayEasterEgg || !value.FirstFrameRendered || !value.CooldownRecorded || !value.PlaybackCompleted)return false;
            ticket=null;
            if(!value.CanUnlock || ActiveOccurrence==null || ActiveOccurrence.InstanceKey!=value.InstanceKey || ActiveOccurrence.Id!=EffectiveOutfit.HolidayId)return false;
            state.Unlock(value.CharacterId,value.OutfitId);pendingUnlockNotice=TextResources.Format("holidayController.completeHover.pendingUnlockNoticeEffectiveOutfitName",EffectiveOutfit.Name);Persist();Recompute();Notify();return true;
        }
        public void CancelHover(){if(ticket!=null)ticket.Cancelled=true;ticket=null;}
        public string TakeUnlockNotice(){string result=UnlockNotice;UnlockNotice=null;return result;}
        void Persist()
        {
            try {save(state);PersistencePending=false;PersistenceWarning=null;if(pendingUnlockNotice!=null){UnlockNotice=pendingUnlockNotice;pendingUnlockNotice=null;}}
            catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is System.Runtime.Serialization.SerializationException))throw;PersistencePending=true;PersistenceWarning=TextResources.Format("holidayController.persist.persistenceWarningEMessage",e.Message);}
        }
        public bool RetrySave(){if(PersistencePending){Persist();Notify();}return !PersistencePending;}
        void Notify(){var handler=Changed;if(handler!=null)handler(this,EventArgs.Empty);}
        HolidayOccurrence GreetingOccurrence()
        {if(context==null || !ValidCharacter(context.CharacterId))return null;return events.FirstOrDefault(e=>catalog.Outfits.Any(o=>o.CharacterId==context.CharacterId && o.HolidayId==e.Id && o.GreetingsReady));}
        public HolidayGreetingContext CreateGreetingContext(double randomValue)
        {
            var occurrence=GreetingOccurrence();if(occurrence==null)return null;
            bool shown=Role().Activities.Any(a=>a.InstanceKey==occurrence.InstanceKey && a.GreetingShown);
            return new HolidayGreetingContext(context,occurrence,EffectiveOutfit,EffectiveOutfit!=null && IsOwned(EffectiveOutfit.OutfitId),Revision,!shown || randomValue<.5);
        }
        public bool IsGreetingCurrent(HolidayGreetingContext value)
        {
            var occurrence=GreetingOccurrence();return value!=null && context!=null && occurrence!=null && value.Revision==Revision && value.PersonaId==context.PersonaId && value.CharacterId==context.CharacterId && value.Date==context.LocalTime.Date && value.InstanceKey==occurrence.InstanceKey && value.Stage==occurrence.Stage;
        }
        public bool MarkGreetingShown(HolidayGreetingContext value)
        {if(!IsGreetingCurrent(value))return false;var occurrence=GreetingOccurrence();var activity=Activity(occurrence);if(!activity.GreetingShown){activity.GreetingShown=true;Persist();Notify();}return true;}
    }
}
