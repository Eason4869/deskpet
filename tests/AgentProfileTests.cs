using System;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class AgentProfileTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                var registry=new AgentProfileRegistry();
                Check(registry.Get("build")!=null && registry.Get("plan")!=null && registry.Get("explore")!=null && registry.Get("general")!=null,"built-in agent profiles are available");
                var plan=registry.PermissionRuntime("plan");
                Check(plan.Decide("write","src/a.cs").Effect==AgentPermissionEffect.Deny && plan.Decide("read","src/a.cs").Effect==AgentPermissionEffect.Allow,"plan profile is read-only");
                var build=registry.PermissionRuntime("build");
                Check(build.Decide("write","src/a.cs").Effect==AgentPermissionEffect.Allow && build.Decide("shell","cmd").Effect==AgentPermissionEffect.Allow,"build profile permits local development actions");
                var explore=registry.PermissionRuntime("explore");
                Check(explore.Decide("write","src/a.cs").Effect==AgentPermissionEffect.Deny && explore.Decide("network","https://example.test").Effect==AgentPermissionEffect.Allow,"explore profile reads code and web sources without edits");
                Console.WriteLine("Agent profile tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
