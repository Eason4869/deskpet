using System;
using Dafeiyu;

// Real ledger events for owned UI fixtures, never hand-edited relationship scores.
internal static class RelationshipHomeFixtures
{
    internal static void Seed156(RelationshipService service,string id)
    {
        var now=RelationshipTime.Current();
        for(int day=0;day<15;day++){
            var time=new RelationshipTime(now.UtcNow.AddDays(day-20),now.LocalOffset);
            Record(service,id,RelationshipEventType.Visit,"seed-visit-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"seed-work-a-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"seed-work-b-"+day,time);
        }
        Record(service,id,RelationshipEventType.Visit,"seed-current-visit",now);
        Record(service,id,RelationshipEventType.SuccessfulWorkTask,"seed-current-work",now);
    }
    internal static void SeedMaximum(RelationshipService service,string id)
    {
        var now=RelationshipTime.Current();
        for(int day=0;day<100;day++){
            var time=new RelationshipTime(now.UtcNow.AddDays(day-99),now.LocalOffset);
            Record(service,id,RelationshipEventType.Visit,"max-visit-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"max-work-a-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"max-work-b-"+day,time);
        }
    }
    internal static void ReachDailyLimit(RelationshipService service,string id)
    {
        // Keep the owned cap fixture at local noon so simulated cooldowns cannot cross midnight.
        var clock=RelationshipTime.Current();var now=new RelationshipTime(clock.UtcNow,TimeSpan.FromHours(12-clock.UtcNow.Hour));int index=0;
        var types=new[]{RelationshipEventType.Visit,RelationshipEventType.SuccessfulWorkTask,RelationshipEventType.SuccessfulWorkTask,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction};
        foreach(var type in types){index++;Record(service,id,type,"cap-fixture-"+index,new RelationshipTime(now.UtcNow.AddSeconds(index*25),now.LocalOffset));}
    }
    static void Record(RelationshipService service,string id,RelationshipEventType type,string token,RelationshipTime time)
    {var result=service.Record(id,type,token,time);if(!result.Success)throw new Exception("UI fixture ledger: "+result.Error);}
}
