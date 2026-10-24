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
    static void Record(RelationshipService service,string id,RelationshipEventType type,string token,RelationshipTime time)
    {var result=service.Record(id,type,token,time);if(!result.Success)throw new Exception("UI fixture ledger: "+result.Error);}
}
