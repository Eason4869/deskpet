using System;
namespace Dafeiyu
{
    public interface IExecutionIsolationProvider {bool Available {get;}string Mode {get;}string Description {get;}}
    public static class AgentExecutionIsolation
    {
        sealed class Provider:IExecutionIsolationProvider
        {
            public bool Available {get;private set;}public string Mode {get;private set;}public string Description {get;private set;}
            internal Provider(bool available,string mode,string description){Available=available;Mode=mode;Description=description;}
        }
        public static IExecutionIsolationProvider Resolve(AgentTerminalPolicy policy)
        {
            if(policy==AgentTerminalPolicy.ConfirmEach)return new Provider(Environment.OSVersion.Platform==PlatformID.Win32NT,"current-account-confirmed","current-account-confirmed; filesystem and network are not isolated; Job Object controls process lifetime");
            return new Provider(false,policy==AgentTerminalPolicy.Sandboxed?"sandbox-unavailable":"disabled",policy==AgentTerminalPolicy.Sandboxed?"OS isolation has not been validated; execution is disabled without fallback":"Terminal execution is disabled");
        }
    }
}
