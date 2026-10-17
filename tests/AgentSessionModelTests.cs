using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class AgentSessionModelTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                var session=new AgentSessionModel("s1","p1","c1");
                var input=new AgentSessionInput{IdempotencyId="input-1",Kind="prompt",Text="first"};
                var first=session.Enqueue(input);var duplicate=session.Enqueue(new AgentSessionInput{IdempotencyId="input-1",Kind="prompt",Text="ignored"});
                Check(Object.ReferenceEquals(first,duplicate) && session.Projection.PendingCount==1,"same-session input idempotency prevents duplicate admission");
                var second=session.Enqueue(new AgentSessionInput{IdempotencyId="input-2",Kind="steer",Text="second",Steer=true});
                Check(session.Projection.PendingCount==2,"different inputs remain pending until delivered");
                var delivered=session.DeliverNext();
                Check(delivered.IdempotencyId=="input-1" && session.Projection.PendingCount==1 && session.Projection.DeliveredCount==1,"inbox delivery preserves order and separates pending from delivered");
                session.StartStep();session.CompleteStep("step-1");
                Check(session.Projection.Step==1 && session.Projection.RecoveryState=="idle","step completion advances durable session state");
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"session-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var store=new AgentSessionStore(root);store.Save(session);var loaded=store.Load("p1","c1");
                Check(loaded!=null && loaded.SessionId=="s1" && loaded.Projection.DeliveredCount==1,"session store preserves admitted and delivered state");
                Console.WriteLine("Agent session model tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
