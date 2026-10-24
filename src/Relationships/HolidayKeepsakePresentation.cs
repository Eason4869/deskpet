using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
namespace Dafeiyu
{
    // One app-owned producer merges frames/home pulses, never retaining host/UI pointers.
    public sealed class HolidayKeepsakeEncounters
    {
        readonly HolidayKeepsakeRuntime runtime;
        string completedKey,flightKey,retryKey; DateTime retryUtc;
        Task<HolidayKeepsakeObservationResult> flight;
        public HolidayKeepsakeEncounters(HolidayKeepsakeRuntime runtime){this.runtime=runtime;}
        public Task<HolidayKeepsakeObservationResult> ObserveAsync(bool shown,bool interactive,
            string id,string character,string name,HolidayEncounterKind kind,RelationshipTime time,HolidayCatalog catalog,bool retry=false)
        {
            if(!shown||!interactive)return Task.FromResult<HolidayKeepsakeObservationResult>(null);
            string key=id+"|"+time.LocalDate+"|"+String.Join(",",catalog.Outfits.Select(x=>x.CharacterId+":"+x.OutfitId+":"+x.Version+":"+x.Ready+":"+x.SourceKey));
            if(flight!=null&&!flight.IsCompleted)return flightKey==key?flight:Task.FromResult<HolidayKeepsakeObservationResult>(null);
            if(!retry&&(completedKey==key||(retryKey==key&&DateTime.UtcNow<retryUtc)))return Task.FromResult<HolidayKeepsakeObservationResult>(null);
            // Construct before the async boundary: owner, time and resources cannot follow a later selection.
            var encounter=new HolidayKeepsakeEncounter(id,character,name,kind,time);
            flightKey=key;return flight=ObserveCoreAsync(encounter,catalog,key);
        }
        async Task<HolidayKeepsakeObservationResult> ObserveCoreAsync(HolidayKeepsakeEncounter encounter,HolidayCatalog catalog,string key)
        {
            var result=await runtime.ObserveAsync(encounter,catalog);
            if(result.Status==HolidayKeepsakeObservationStatus.Completed&&result.Results.All(x=>x.Status!=HolidayKeepsakeStatus.ResourceUnavailable))completedKey=key;
            else{retryKey=key;retryUtc=DateTime.UtcNow.AddSeconds(5);}
            return result;
        }
    }
    public sealed class HolidayKeepsakeCardData
    {
        public const string LayoutVersion="holiday-card-1";
        public HolidayKeepsakeRecord Record {get;private set;}
        public string CompanionId {get;private set;}
        public string HolidayId {get;private set;}
        public string InstanceKey {get;private set;}
        public string Name {get;private set;}
        public int Year {get;private set;}
        public string Style {get;private set;}
        readonly bool halloween;
        HolidayKeepsakeCardData(string id,string holiday,string instance,string name,int year,HolidayKeepsakeRegistry registry)
        {
            CompanionId=id;HolidayId=holiday;InstanceKey=instance;Name=name;Year=year;
            var definition=registry.Find(holiday);
            halloween=definition!=null&&definition.TitleKey=="relationship.card.halloweenTitle"&&definition.MessageKey=="holidayKeepsake.halloweenMessage";
            Style=definition!=null&&definition.StyleKey=="halloween"?"halloween":"history";
        }
        public static HolidayKeepsakeCardData Owned(HolidayKeepsakeRecord record,HolidayKeepsakeRegistry registry)
        {var data=new HolidayKeepsakeCardData(record.CompanionId,record.HolidayId,record.InstanceKey,record.CompanionName,record.OccurrenceYear,registry);data.Record=record;return data;}
        public static HolidayKeepsakeCardData Preview(string id,string name,int year,HolidayKeepsakePreviewResult preview,HolidayKeepsakeRegistry registry)
        {return new HolidayKeepsakeCardData(id,preview.HolidayId,preview.InstanceKey,name,year,registry);}
        // Literal cases deliberately keep localization review/checking bounded.
        public string Title {get{return halloween?TextResources.Get("relationship.card.halloweenTitle"):TextResources.Get("holidayKeepsake.historyTitle");}}
        public string Message {get{return halloween?TextResources.Get("holidayKeepsake.halloweenMessage"):TextResources.Get("holidayKeepsake.historyMessage");}}
        public string DateText {get{return Record==null?"":RelationshipCardData.FormatDate(Record.EarnedLocalDate);}}
        public string YearText {get{return Year.ToString(CultureInfo.InvariantCulture);}}
        public string Identity {get{return CompanionId+"|"+HolidayId+"|"+InstanceKey;}}
        public string CaptureKey(bool dark){return Identity+"|"+Name+"|"+(Record==null?"preview":Record.EarnedLocalDate+"|"+Record.SceneSha256+"|"+Record.SceneVersion)+"|"+Style+"|"+Title+"|"+Message+"|"+TextResources.CurrentLanguage+"|"+dark+"|"+LayoutVersion+"|"+RelationshipTitleFonts.Version+"|"+RelationshipCardDecoration.Version;}
        public string FileName {get{return RelationshipCardData.SafeFileName(Name,HolidayId,Record==null?YearText:Record.EarnedLocalDate);}}
    }
    public sealed class HolidayKeepsakePage
    {
        public HolidayKeepsakeAlbum Album {get;internal set;}
        public int Page {get;internal set;}
        public int PageCount {get;internal set;}
        public IList<HolidayKeepsakeSceneResult> Scenes {get;internal set;}
    }
    public sealed class HolidayKeepsakeAlbumModel
    {
        public const int PageSize=6;
        readonly HolidayKeepsakeRuntime runtime;
        public HolidayKeepsakeAlbumModel(HolidayKeepsakeRuntime runtime){this.runtime=runtime;}
        public async Task<HolidayKeepsakePage> LoadAsync(string id,int page)
        {
            var album=await runtime.LoadAsync(id);int pages=Math.Max(1,(album.Records.Count+PageSize-1)/PageSize);page=Math.Max(0,Math.Min(page,pages-1));
            var scenes=new List<HolidayKeepsakeSceneResult>();
            foreach(var record in album.Records.OrderByDescending(x=>x.OccurrenceYear).ThenBy(x=>x.HolidayId,StringComparer.Ordinal).Skip(page*PageSize).Take(PageSize)){
                var scene=await runtime.ReadSceneAsync(record.CompanionId,record.HolidayId,record.InstanceKey);
                // The independent scene read can lose access to the state after
                // the album succeeded. Keep that saved record, never a blank slot.
                scenes.Add(scene.Record==null?new HolidayKeepsakeSceneResult(scene.Status,record):scene);
            }
            return new HolidayKeepsakePage{Album=album,Page=page,PageCount=pages,Scenes=scenes.AsReadOnly()};
        }
    }
}
