using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dafeiyu
{
    // Compiled, reviewed definitions only. The five-argument scene provider uses
    // HolidayId as its set key, so aliases are deliberately not accepted here.
    public sealed class HolidayKeepsakeDefinition
    {
        public string HolidayId { get; private set; }
        public string CalendarId { get; private set; }
        public int FirstYear { get; private set; }
        public string TitleKey { get; private set; }
        public string MessageKey { get; private set; }
        public string StyleKey { get; private set; }
        public string SceneSetId { get; private set; }
        public bool EnabledForAcquisition { get; private set; }
        internal HolidayKeepsakeDefinition(string holiday, string calendar, int firstYear,
            string title, string message, string style, string sceneSet, bool enabled)
        {
            if(!ValidKey(holiday)||!ValidKey(calendar)||!ValidKey(title)||!ValidKey(message)||!ValidKey(style)
                ||sceneSet!=holiday||firstYear<1970||firstYear>9998)throw new ArgumentException("Invalid reviewed holiday definition.");
            HolidayId=holiday;CalendarId=calendar;FirstYear=firstYear;TitleKey=title;
            MessageKey=message;StyleKey=style;SceneSetId=sceneSet;EnabledForAcquisition=enabled;
        }
        internal static bool ValidKey(string value)
        {return value!=null&&value.Length<=128&&Regex.IsMatch(value,@"\A[a-zA-Z0-9][a-zA-Z0-9_.:-]*\z");}
    }
    public sealed class HolidayKeepsakeRegistry
    {
        static readonly HolidayKeepsakeRegistry builtIn=new HolidayKeepsakeRegistry(new[]{
            new HolidayKeepsakeDefinition("halloween","halloween",2026,"relationship.card.halloweenTitle",
                "holidayKeepsake.halloweenMessage","halloween","halloween",true),
            new HolidayKeepsakeDefinition("christmas","christmas",2026,"holidayKeepsake.christmasTitle","holidayKeepsake.christmasMessage","christmas","christmas",true),
            new HolidayKeepsakeDefinition("spring-festival","spring-festival",2026,"holidayKeepsake.springFestivalTitle","holidayKeepsake.springFestivalMessage","spring-festival","spring-festival",true),
            new HolidayKeepsakeDefinition("lantern","lantern",2026,"holidayKeepsake.lanternTitle","holidayKeepsake.lanternMessage","lantern","lantern",true),
            new HolidayKeepsakeDefinition("valentines-day","valentines-day",2026,"holidayKeepsake.valentinesDayTitle","holidayKeepsake.valentinesDayMessage","valentines-day","valentines-day",true),
            new HolidayKeepsakeDefinition("qingming","qingming",2026,"holidayKeepsake.qingmingTitle","holidayKeepsake.qingmingMessage","qingming","qingming",true),
            new HolidayKeepsakeDefinition("dragon-boat","dragon-boat",2026,"holidayKeepsake.dragonBoatTitle","holidayKeepsake.dragonBoatMessage","dragon-boat","dragon-boat",true),
            new HolidayKeepsakeDefinition("mid-autumn","mid-autumn",2026,"holidayKeepsake.midAutumnTitle","holidayKeepsake.midAutumnMessage","mid-autumn","mid-autumn",true),
            new HolidayKeepsakeDefinition("national-day","national-day",2026,"holidayKeepsake.nationalDayTitle","holidayKeepsake.nationalDayMessage","national-day","national-day",true),
            new HolidayKeepsakeDefinition("qixi","qixi",2026,"holidayKeepsake.qixiTitle","holidayKeepsake.qixiMessage","qixi","qixi",true)});
        public static HolidayKeepsakeRegistry BuiltIn {get{return builtIn;}}
        public ReadOnlyCollection<HolidayKeepsakeDefinition> Definitions {get;private set;}
        internal HolidayKeepsakeRegistry(IEnumerable<HolidayKeepsakeDefinition> definitions)
        {
            if(definitions==null)throw new ArgumentNullException("definitions");
            var items=definitions.Take(17).ToList();
            if(items.Count>16||items.Any(x=>x==null)||items.Select(x=>x.HolidayId).Distinct(StringComparer.Ordinal).Count()!=items.Count
                ||items.Select(x=>x.CalendarId).Distinct(StringComparer.Ordinal).Count()!=items.Count)throw new ArgumentException("Invalid holiday registry.");
            Definitions=items.OrderBy(x=>x.HolidayId,StringComparer.Ordinal).ToList().AsReadOnly();
        }
        public HolidayKeepsakeDefinition Find(string holidayId){return Definitions.FirstOrDefault(x=>x.HolidayId==holidayId);}
        internal HolidayOccurrence FindOccurrence(HolidayCalendar calendar,HolidayKeepsakeDefinition definition,int year)
        {
            if(definition==null||!definition.EnabledForAcquisition||year<definition.FirstYear||year>9998)return null;
            HolidayOccurrence found=null;var end=new DateTime(year+1,1,1);
            for(var date=new DateTime(year,1,1);date<end;date=date.AddDays(1))
                foreach(var occurrence in calendar.Resolve(date))if(occurrence.Id==definition.CalendarId&&occurrence.StartDate.Year==year){
                    if(found!=null&&found.InstanceKey!=occurrence.InstanceKey)return null;
                    found=occurrence;
                }
            return found;
        }
    }
}
