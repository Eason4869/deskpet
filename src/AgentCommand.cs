using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    [DataContract] public sealed class AgentCommand
    {
        [DataMember]public string Id,Executable,Workspace;
        [DataMember]public string[] Arguments;
    }
    [DataContract]public sealed class CommandResult
    {
        [DataMember]public string CommandId,Executable,Workspace,Output;
        [DataMember]public string[] Arguments;
        [DataMember]public int ExitCode;
        [DataMember]public string ExecutionId,CommandText,IsolationDescription;
        [DataMember]public bool Truncated,TimedOut,Cancelled;
        [DataMember]public long ElapsedMilliseconds;
        public CommandResult Copy(){var c=(CommandResult)MemberwiseClone();c.Arguments=Arguments==null?null:(string[])Arguments.Clone();return c;}
    }
    public sealed partial class AgentWorkspace
    {
        readonly List<CommandResult> commandResults=new List<CommandResult>();
        int commandTimeoutSeconds=120;
        public int CommandTimeoutSeconds {get{return commandTimeoutSeconds;}set{commandTimeoutSeconds=value;}}
        public IList<CommandResult> CommandResults{get{lock(commandResults)return commandResults.Select(r=>r.Copy()).ToList().AsReadOnly();}}
        string ReadOutput(string id,int offset,int limit)
        {
            CommandResult result;lock(commandResults)result=commandResults.LastOrDefault(r=>r.CommandId==id);if(result==null)throw new ArgumentException(TextResources.Get("agentCommand.readOutput.text"));
            string output=result.Output??"";offset=Math.Min(offset,output.Length);int count=Math.Min(limit,output.Length-offset);
            return new JavaScriptSerializer().Serialize(new{result.CommandId,result.ExitCode,result.TimedOut,result.Truncated,Output=output.Substring(offset,count),next_offset=offset+count<output.Length?(int?)(offset+count):null});
        }
    }
}
