using System;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        public HolidayKeepsakeRuntime HolidayKeepsakes {get;private set;}
        internal HolidayKeepsakeEncounters HolidayEncounters {get;private set;}
        internal Func<RelationshipTime> KeepsakeTime=RelationshipTime.Current;
        double keepsakeFrameAt;
        void ProbeKeepsakeFrame(){double now=watch.Elapsed.TotalSeconds;if(IsPetVisible&&now>=keepsakeFrameAt){keepsakeFrameAt=now+1;image.InvalidateVisual();}}
        void InitializeHolidayKeepsakes()
        {
            HolidayKeepsakes=new HolidayKeepsakeRuntime(Store.DirectoryPath,DataMaintenance,new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,
                RelationshipStageArtSource.CreateProvider(),new HolidayKeepsakeSceneValidator(),action=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(action);});
            HolidayEncounters=new HolidayKeepsakeEncounters(HolidayKeepsakes);
        }
        internal Task ObserveHolidayEncounter(bool shown,HolidayEncounterKind kind,bool retry=false)
        {
            if(HolidayEncounters==null||closing)return Task.FromResult(true);
            return HolidayEncounters.ObserveAsync(shown,!sessionLocked,RelationshipCompanionId,Skin.IsOfficial?Skin.BuiltinCharacterId:null,Skin.DisplayName,kind,KeepsakeTime(),Holidays.Catalog,retry);
        }
        internal bool KeepsakeInteractive {get{return !sessionLocked&&!closing;}}
    }
}
