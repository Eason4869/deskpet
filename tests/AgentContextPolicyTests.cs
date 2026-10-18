using System;
namespace Dafeiyu.Tests
{
    public static class AgentContextPolicyTests
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        public static int Main(){try{var history=new ConversationLog();for(int i=0;i<25;i++)history.Commit("user-"+i,"reply-"+i);var request=history.ForRequest("system","current");Check(request.Count==52 && request[1].Content=="user-0","request retains complete stored history beyond ten turns before budget selection");Console.WriteLine("Agent context policy tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
