using System;
using System.Collections.Generic;
namespace Dafeiyu.Tests
{
    public static class DesktopObservationPolicyTests
    {
        static int checks;
        static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
        public static PetSettings Settings()
        {
            var settings=new PetSettings{GreetingEnabled=true,AiGreetingEnabled=true,DesktopObservationEnabled=true,
                Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="actual-chat",ImageSupport=CapabilitySupport.Supported}};
            settings.DesktopObservationConsentVersion=DesktopObservationConsent.Version;
            settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(settings);
            return settings;
        }
        public static DesktopObservationContext Context(PetSettings settings=null,bool due=true,bool eligible=true,bool manual=false,Func<bool> current=null)
        {return new DesktopObservationContext(settings??Settings(),"dafeiyu","大肥鱼",null,null,new DateTime(2026,11,2,12,0,0),due,eligible,current??(()=>true),manual?DesktopObservationMode.ManualTest:DesktopObservationMode.Automatic);}
        public static int Main()
        {
            try{DestinationLabels();ReasonLabels();Gates();Quotas();Scenes();Console.WriteLine("DesktopObservationPolicyTests: "+checks+" assertions passed.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        static void DestinationLabels()
        {
            Check(DesktopObservationUiText.SafeAddress("https://alice:secret@example.com/v1/chat?token=secret#private")=="https://example.com/v1/chat", "address strips secrets");
            Check(DesktopObservationUiText.SafeAddress("not a url")=="", "invalid address is hidden");
            var addresses=DesktopObservationUiText.Destinations(Settings());
            Check(addresses.Length==1 && addresses[0]=="http://127.0.0.1:1/v1/chat/completions", "direct route shows actual endpoint");
            var fallback = new TextProviderSettings { Id="vision", Connection=new ConnectionSettings { BaseUrl="https://vision.example/v1", Model="image" }, Models=new List<string> { "image" } };
            fallback.ModelCapabilities["image"]=new ModelCapabilitySetting { Images=CapabilitySupport.Supported };fallback.Normalize();
            var described=Settings();described.Text.ImageSupport=CapabilitySupport.Unsupported;described.TextProviders=new List<TextProviderSettings>{fallback};described.ImageDescriptionModel=new ModelReference{ProviderId="vision",ModelId="image"};described.ImageDescriptionOverride=null;
            addresses=DesktopObservationUiText.Destinations(described);
            Check(addresses.Length==2 && addresses[0]=="https://vision.example/v1/chat/completions" && addresses[1]=="http://127.0.0.1:1/v1/chat/completions", "described route identifies both destinations");
            string summary=DesktopObservationUiText.ReceivingSummary(described);
            Check(summary.Contains(TextResources.Format("desktopObservation.imageDestination",addresses[0])) && summary.Contains(TextResources.Format("desktopObservation.textDestination",addresses[1])),"described consent labels image and text recipients separately");
            summary=DesktopObservationUiText.ReceivingSummary(Settings());
            Check(summary==TextResources.Format("desktopObservation.imageDestination","http://127.0.0.1:1/v1/chat/completions"),"direct consent only identifies image recipient");
        }
        static void ReasonLabels()
        {
            foreach(ObservationReason reason in Enum.GetValues(typeof(ObservationReason)))
                Check(DesktopObservationUiText.ReasonText(reason)==TextResources.Get("desktopObservation.reason."+Char.ToLowerInvariant(reason.ToString()[0])+reason.ToString().Substring(1)),"every reason has a literal localized key");
            Check(DesktopObservationUiText.ReasonText((ObservationReason)Int32.MaxValue)==TextResources.Get("desktopObservation.reason.ineligible"),"unknown reason uses safe fallback");
        }
        static void Gates()
        {
            var p=new DesktopObservationPolicy();var now=new DateTime(2026,11,2,0,0,0,DateTimeKind.Utc);
            Check(p.TryBegin(Context(due:false),now).Reason==ObservationReason.NotDue,"not due cannot be admitted");
            Check(p.TryBegin(Context(eligible:false),now).Reason==ObservationReason.Ineligible,"privacy or active-chat gate denies");
            Check(p.TryBegin(Context(current:()=>false),now).Reason==ObservationReason.Stale,"old epoch denies");
            foreach(int flag in new[]{0,1,2}){var s=Settings();if(flag==0)s.DesktopObservationEnabled=false;if(flag==1)s.GreetingEnabled=false;if(flag==2)s.AiGreetingEnabled=false;Check(p.TryBegin(Context(s),now).Reason==ObservationReason.Disabled,"all three switches required");}
            var settings=Settings();settings.DesktopObservationConsentVersion=0;
            Check(p.TryBegin(Context(settings),now).Reason==ObservationReason.ConsentRequired,"explicit consent required before capture");
            settings=Settings();settings.Text.BaseUrl="http://127.0.0.1:2/v1";
            Check(p.TryBegin(Context(settings),now).Reason==ObservationReason.ConsentRequired,"changed endpoint requires new confirmation");
            settings=Settings();settings.Text.ImageSupport=CapabilitySupport.Unknown;
            Check(p.TryBegin(Context(settings),now).Reason==ObservationReason.NoRoute,"unknown is not supported");
            settings=Settings();settings.Text.Model="";
            Check(p.TryBegin(Context(settings),now).Reason==ObservationReason.InvalidConfiguration,"invalid connection denies before capture");
            Check(p.TryBegin(Context(),now).Allowed,"valid due request admitted");
            Check(p.TryBegin(Context(),now).AutomaticCallsLastHour==0,"admission and capture alone do not bill automatic quota");
        }
        static void Quotas()
        {
            var p=new DesktopObservationPolicy();var c=Context();var now=new DateTime(2026,11,2,0,0,0,DateTimeKind.Utc);
            p.RecordModelAttempt(c,now);
            Check(!p.TryBegin(c,now.AddMinutes(9.99)).Allowed,"automatic calls have ten minute floor");
            for(int i=1;i<4;i++){Check(p.TryBegin(c,now.AddMinutes(10*i)).Allowed,"spaced call admitted");p.RecordModelAttempt(c,now.AddMinutes(10*i));}
            var denied=p.TryBegin(c,now.AddMinutes(40));Check(denied.Reason==ObservationReason.HourlyLimit && denied.AutomaticCallsLastHour==4 && denied.RetryAtUtc==now.AddHours(1),"rolling hour has four attempts and useful retry time");
            var changed=Settings();changed.Text.Model="other-model";Check(p.TryBegin(Context(changed),now.AddMinutes(40)).Reason==ObservationReason.HourlyLimit,"changing model cannot reset global quota");
            var manual=Context(manual:true);Check(p.TryBegin(manual,now.AddMinutes(31)).Allowed,"manual test bypasses automatic quota");p.RecordModelAttempt(manual,now.AddMinutes(31));Check(p.ManualTestCount==1,"manual counter independent");
            Check(p.TryBegin(c,now.AddHours(1)).Allowed,"old attempt expires at sixty minutes; no backlog");
            p=new DesktopObservationPolicy();foreach(int minutes in new[]{10,30,60,60}){p.RecordFailure(now);var a=p.TryBegin(c,now);Check(a.Reason==ObservationReason.Cooldown && a.RetryAtUtc==now.AddMinutes(minutes),"failure backoff ten/thirty/sixty minutes");now=now.AddMinutes(minutes);}
            p.RecordSuccess();Check(p.TryBegin(c,now).Allowed,"successful response resets failure streak");p.RecordFailure(now);Check(p.TryBegin(c,now).RetryAtUtc==now.AddMinutes(10),"success restores first failure cooldown");
        }
        static void Scenes()
        {
            var p=new DesktopObservationPolicy();var now=DateTime.UtcNow;
            Check(p.TryObserveScene("normalized-frame",now),"new scene admitted");
            Check(!p.TryObserveScene("normalized-frame",now.AddMinutes(29)),"same scene skipped for thirty minutes");
            Check(p.TryObserveScene("normalized-frame",now.AddMinutes(30)),"same scene expires at thirty minutes");
        }
    }
}
