using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Dafeiyu.Tests
{
    public static class HolidayCalendarTests
    {
        static int passed;
        static void Check(bool value,string label){if(!value)throw new Exception(label);passed++;}
        static DateTime Date(string value){return DateTime.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture);}
        static HolidayOccurrence Find(HolidayCalendar calendar,string date,string id){return calendar.Resolve(Date(date)).FirstOrDefault(x=>x.Id==id);}
        [DataContract] public sealed class Fixture {[DataMember(Name="cases")]public List<Example> Cases;}
        [DataContract] public sealed class Example
        {
            [DataMember(Name="date")]public string Date;
            [DataMember(Name="holiday")]public string Holiday;
            [DataMember(Name="expected")]public bool Expected;
        }
        public static int Main()
        {
            try {
                passed=0;var calendar=new HolidayCalendar();
                Check(Find(calendar,"2026-10-31","halloween")!=null,"Halloween is recognized");
                Check(Find(calendar,"2026-10-30","halloween")==null && Find(calendar,"2026-11-01","halloween")==null,"Halloween lasts one day");
                foreach(var pair in new[]{new[]{"new-year","2026-01-01"},new[]{"labour","2026-05-01"},new[]{"children","2026-06-01"}})
                    Check(Find(calendar,pair[1],pair[0])!=null,"fixed date "+pair[0]);
                for(int day=1;day<=7;day++)Check(Find(calendar,"2026-10-"+day.ToString("00"),"national-day")!=null,"National Day includes "+day);
                Check(Find(calendar,"2026-09-30","national-day")==null && Find(calendar,"2026-10-08","national-day")==null,"National Day boundaries");
                var christmas=Find(calendar,"2026-12-24","christmas");
                Check(christmas!=null && christmas.StartDate==Date("2026-12-24") && christmas.EndExclusive==Date("2026-12-26"),"Christmas two-day interval");
                Check(christmas.InstanceKey==Find(calendar,"2026-12-25","christmas").InstanceKey,"Christmas shares one instance");
                Check(Find(calendar,"2026-12-23","christmas")==null && Find(calendar,"2026-12-26","christmas")==null,"Christmas boundaries");
                Check(christmas.Stage=="eve" && Find(calendar,"2026-12-25","christmas").Stage=="day","Christmas greeting stages");
                var spring=Find(calendar,"2026-02-16","spring-festival");
                Check(spring!=null && spring.Stage=="eve" && spring.EndExclusive==Date("2026-03-03"),"29-day New Year's Eve through day fourteen");
                Check(Find(calendar,"2024-02-09","spring-festival").Stage=="eve","30-day New Year's Eve");
                Check(Find(calendar,"2026-02-17","spring-festival").Stage=="new-year" && Find(calendar,"2026-02-18","spring-festival").Stage=="festival","Spring Festival greeting stages");
                Check(calendar.Resolve(Date("2020-10-01")).Select(x=>x.Id).SequenceEqual(new[]{"mid-autumn","national-day"}),"overlapping holidays are ordered");
                Check(calendar.Resolve(new DateTime(1800,1,1)).Any(x=>x.Id=="new-year"),"Gregorian holiday outside lunar range");
                Check(calendar.Resolve(DateTime.MinValue).Count==1 && calendar.Resolve(DateTime.MaxValue).Count==0,"extreme dates remain safe");
                Check(!calendar.Resolve(new DateTime(2036,4,4)).Any(x=>x.Id=="qingming"),"unavailable solar terms are not guessed");
                Check(calendar.Resolve(new DateTime(2026,10,31,23,59,59)).Any(x=>x.Id=="halloween"),"time of day is normalized locally");
                Check(calendar.Resolve(new DateTime(1901,2,19))!=null && calendar.Resolve(new DateTime(2101,1,28))!=null,"lunar boundary years do not throw");
                using(var stream=typeof(HolidayCalendarTests).Assembly.GetManifestResourceStream("Dafeiyu.Tests.HolidayGolden")) {
                    Check(stream!=null,"independent HKO fixtures are embedded");
                    var fixture=(Fixture)new DataContractJsonSerializer(typeof(Fixture)).ReadObject(stream);
                    Check(fixture.Cases.Count>300,"multi-year independent fixtures exist");
                    foreach(var sample in fixture.Cases)Check((Find(calendar,sample.Date,sample.Holiday)!=null)==sample.Expected,"HKO "+sample.Date+" "+sample.Holiday+" expected "+sample.Expected);
                }
                Console.WriteLine("Holiday calendar tests: "+passed+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL holiday calendar: "+error);return 1;}
        }
    }
}
