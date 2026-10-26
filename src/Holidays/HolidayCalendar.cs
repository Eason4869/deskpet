using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Dafeiyu
{
    public sealed class HolidayOccurrence
    {
        public string Id {get;internal set;}
        public DateTime StartDate {get;internal set;}
        public DateTime EndExclusive {get;internal set;}
        public string InstanceKey {get;internal set;}
        public string Stage {get;internal set;}
    }
    public sealed class HolidayCalendar
    {
        readonly ChineseLunisolarCalendar lunar=new ChineseLunisolarCalendar();
        readonly Dictionary<int,SolarDates> solar=new Dictionary<int,SolarDates>();
        public string SolarTermWarning {get;private set;}
        [DataContract] sealed class SolarTable
        {
            [DataMember(Name="schema_version")]public int Version {get;set;}
            [DataMember(Name="years")]public List<SolarYear> Years {get;set;}
        }
        [DataContract] sealed class SolarYear
        {
            [DataMember(Name="year")]public int Year {get;set;}
            [DataMember(Name="qingming")]public string Qingming {get;set;}
            [DataMember(Name="winter_solstice")]public string WinterSolstice {get;set;}
        }
        sealed class SolarDates {public DateTime Qingming,WinterSolstice;}
        public HolidayCalendar()
        {
            try {
                using(var input=typeof(HolidayCalendar).Assembly.GetManifestResourceStream("Dafeiyu.Holidays.SolarTerms")) {
                    if(input==null)throw new InvalidDataException("Offline solar-term resource is missing.");
                    var table=(SolarTable)new DataContractJsonSerializer(typeof(SolarTable)).ReadObject(input);
                    if(table==null || table.Version!=1 || table.Years==null || table.Years.Count>200)throw new InvalidDataException("Invalid solar-term table.");
                    foreach(var year in table.Years) {
                        if(year==null || year.Year<1901 || year.Year>2100 || solar.ContainsKey(year.Year))throw new InvalidDataException("Invalid solar-term year.");
                        var dates=new SolarDates{Qingming=Parse(year.Qingming),WinterSolstice=Parse(year.WinterSolstice)};
                        if(dates.Qingming.Year!=year.Year || dates.Qingming.Month!=4 || dates.WinterSolstice.Year!=year.Year || dates.WinterSolstice.Month!=12)throw new InvalidDataException("Invalid solar-term dates.");
                        solar.Add(year.Year,dates);
                    }
                }
            }catch(Exception error) {
                if(!(error is IOException || error is InvalidDataException || error is SerializationException || error is FormatException || error is ArgumentException || error is System.Xml.XmlException))throw;
                solar.Clear();SolarTermWarning=error.Message;
            }
        }
        static DateTime Parse(string value){return DateTime.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None);}
        public bool HasSolarTermDates(int year){return solar.ContainsKey(year);}

        // Input is a civil date in the caller's selected local timezone, not UTC.
        public IList<HolidayOccurrence> Resolve(DateTime localDate)
        {
            var date=localDate.Date;var result=new List<HolidayOccurrence>();
            if(date>=lunar.MinSupportedDateTime.Date && date<=lunar.MaxSupportedDateTime.Date) {
                int year=lunar.GetYear(date),rawMonth=lunar.GetMonth(date),leap=lunar.GetLeapMonth(year);
                int month=leap>0 && rawMonth>=leap ? rawMonth-1 : rawMonth;
                int day=lunar.GetDayOfMonth(date);bool isLeap=leap>0 && rawMonth==leap;
                // Eve belongs to the previous lunar year; the next year may be outside the API range.
                for(int candidate=year;candidate<=year+1;candidate++) {
                    if(candidate<lunar.GetYear(lunar.MinSupportedDateTime) || candidate>lunar.GetYear(lunar.MaxSupportedDateTime))continue;
                    var first=lunar.ToDateTime(candidate,1,1,0,0,0,0);
                    var end=lunar.ToDateTime(candidate,1,15,0,0,0,0);
                    Add(result,date,"spring-festival",first.AddDays(-1),end,date<first?"eve":date==first?"new-year":"festival");
                }
                if(!isLeap) {
                    LunarDay(result,date,month,day,1,15,"lantern");
                    LunarDay(result,date,month,day,8,15,"mid-autumn");
                    LunarDay(result,date,month,day,5,5,"dragon-boat");
                    LunarDay(result,date,month,day,7,7,"qixi");
                    LunarDay(result,date,month,day,9,9,"double-ninth");
                    LunarDay(result,date,month,day,12,8,"laba");
                }
            }
            SolarDates terms;if(solar.TryGetValue(date.Year,out terms)) {
                Add(result,date,"qingming",terms.Qingming,terms.Qingming.AddDays(1),"day");
                Add(result,date,"winter-solstice",terms.WinterSolstice,terms.WinterSolstice.AddDays(1),"day");
            }
            Fixed(result,date,"valentines-day",2,14,1,"day");
            Fixed(result,date,"national-day",10,1,7,"day");
            Fixed(result,date,"christmas",12,24,2,date.Day==24?"eve":"day");
            Fixed(result,date,"halloween",10,31,1,"day");
            Fixed(result,date,"new-year",1,1,1,"day");
            Fixed(result,date,"labour",5,1,1,"day");
            Fixed(result,date,"children",6,1,1,"day");
            return result.AsReadOnly();
        }
        static void LunarDay(List<HolidayOccurrence> output,DateTime date,int month,int day,int expectedMonth,int expectedDay,string id)
        {if(month==expectedMonth && day==expectedDay)Add(output,date,id,date,date.AddDays(1),"day");}
        static void Fixed(List<HolidayOccurrence> output,DateTime date,string id,int month,int day,int length,string stage)
        {var start=new DateTime(date.Year,month,day);Add(output,date,id,start,start.AddDays(length),stage);}
        static void Add(List<HolidayOccurrence> output,DateTime date,string id,DateTime start,DateTime end,string stage)
        {
            if(date>=start && date<end)output.Add(new HolidayOccurrence{Id=id,StartDate=start,EndExclusive=end,InstanceKey=id+":"+start.ToString("yyyyMMdd",CultureInfo.InvariantCulture),Stage=stage});
        }
    }
}
