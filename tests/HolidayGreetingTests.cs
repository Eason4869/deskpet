using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class HolidayGreetingTests
    {
        static int passed;
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Holiday greeting tests: "+passed+" passed");return 0;}catch(Exception error){Console.WriteLine("FAIL holiday greeting: "+error);return 1;}}
        static async Task Run()
        {
            foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}) {
                var lines=HolidayGreetingPhrases.ForCharacter(id,"halloween","day");
                Check(lines.Count==6 && lines.Distinct().Count()==6,"six unique holiday phrases: "+id);
                Check(lines.All(x=>x.Length>0 && x.Length<=80 && x.IndexOfAny(new[]{'\r','\n','`'})<0),"bounded natural phrases: "+id);
            }
            Check(HolidayGreetingPhrases.ForCharacter("custom","halloween","day").Count==0,"custom character cannot borrow built-in voice");
            Check(HolidayGreetingPhrases.ForCharacter("e-bao","qingming","day").Count==0,"unreviewed holiday has no invented blessing");
            var holiday=new HolidayGreetingContext(new HolidayContext{PersonaId="old-personal-id",CharacterId="e-bao",LocalTime=new DateTime(2026,10,31)},new HolidayOccurrence{Id="halloween",InstanceKey="halloween:20261031",Stage="day"},null,false,2,true){OutfitId="halloween-2026",OutfitName="万圣夜短披风"};
            var pool=HolidayGreetingPhrases.ForCharacter("e-bao","halloween","day");
            string local=GreetingService.Fallback("old-personal-id","E包",holiday.Date,0,null,holiday);
            Check(pool.Contains(local),"legacy persona uses trusted holiday pool");
            string next=GreetingService.Fallback("old-personal-id","E包",holiday.Date,0,new[]{local},holiday);
            Check(pool.Contains(next) && next!=local,"holiday fallback avoids recent text");
            Check(!pool.Contains(GreetingService.Fallback("e-bao","E包",holiday.Date.AddDays(1),0,null,holiday)),"outdated holiday context cannot claim holiday");
            var settings=new PetSettings{AiGreetingEnabled=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};
            var fake=new FakeApi();int created=0;var service=new GreetingService(()=>{created++;return fake;},TimeSpan.FromMilliseconds(100));
            string result=await service.GenerateAsync(settings,"old-personal-id","E包",null,holiday.Date,0,null,CancellationToken.None,holiday);
            Check(created==0 && pool.Contains(result),"AI off never creates transport and uses holiday fallback");
            settings.AiGreetingEnabled=true;fake.Failure=new InvalidOperationException("fixture failure");
            result=await service.GenerateAsync(settings,"old-personal-id","E包",null,holiday.Date,0,null,CancellationToken.None,holiday);
            Check(pool.Contains(result),"AI failure falls back to holiday phrase");
            Check(fake.Messages[1].Content.Contains("2026-10-31") && fake.Messages[1].Content.Contains("万圣节") && fake.Messages[1].Content.Contains("尚未拥有"),"AI receives actual date, event and temporary outfit status");
            holiday.UseHolidayPool=false;
            Check(!pool.Contains(GreetingService.Fallback("e-bao","E包",holiday.Date,0,null,holiday)),"ordinary half of later greeting remains ordinary");
            Check(GreetingService.Fallback("e-bao","E包",holiday.Date,0,null)==GreetingService.Fallback("e-bao","E包",holiday.Date,0,null,null),"legacy fallback overload unchanged");
        }
        sealed class FakeApi:IConversationApi
        {
            public Exception Failure;public IList<ChatMessage> Messages;
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Messages=messages;if(Failure!=null)throw Failure;return Task.FromResult("今晚的糖果给你留了一颗。");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wave,CancellationToken cancel){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}
            public void Dispose(){}
        }
    }
}
