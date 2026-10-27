using System;
using Dafeiyu;
using System.Threading.Tasks;
namespace DeskPet.Desktop
{
    internal sealed partial class DesktopController
    {
        public HolidayKeepsakeRuntime HolidayKeepsakes {get;private set;}=null!;
        internal HolidayKeepsakeEncounters HolidayEncounters {get;private set;}=null!;
        internal Func<RelationshipTime> KeepsakeTime=RelationshipTime.Current;
        void InitializeHolidayKeepsakes()
        {
            HolidayKeepsakes=new HolidayKeepsakeRuntime(DataDirectory,DataMaintenance,new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,
                RelationshipStageArtSource.CreateProvider(),new HolidayKeepsakeSceneValidator(),action=>Avalonia.Threading.Dispatcher.UIThread.Post(action));
            HolidayEncounters=new HolidayKeepsakeEncounters(HolidayKeepsakes);
        }
        internal Task ObserveHolidayEncounter(bool shown,HolidayEncounterKind kind,bool retry=false)
        {
            if(HolidayEncounters==null||exiting)return Task.FromResult(true);
            return HolidayEncounters.ObserveAsync(shown,Pet.KeepsakeInteractive,RelationshipCompanionId,Skin.OfficialCharacterId,Skin.DisplayName,kind,KeepsakeTime(),Holidays.Catalog,retry);
        }

    }
}
