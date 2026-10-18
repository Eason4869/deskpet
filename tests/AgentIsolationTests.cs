using System;
namespace Dafeiyu.Tests
{
    public static class AgentIsolationTests
    {
        public static int Main(){try{var none=AgentExecutionIsolation.Resolve(AgentTerminalPolicy.Disabled);if(none.Available)throw new Exception("disabled terminal became available");var sandbox=AgentExecutionIsolation.Resolve(AgentTerminalPolicy.Sandboxed);if(sandbox.Available||sandbox.Mode=="current-account-confirmed")throw new Exception("unavailable sandbox silently fell back");var current=AgentExecutionIsolation.Resolve(AgentTerminalPolicy.ConfirmEach);if(!current.Description.Contains("not isolated"))throw new Exception("current account mislabelled as isolated");Console.WriteLine("Agent isolation tests: 3 passed (OS sandbox intentionally unavailable per user decision)");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
