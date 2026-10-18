using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
namespace Dafeiyu
{
    public sealed class AgentAuditEvent
    {
        public string CallId,TaskId,Tool,Action,Resource,Result,Reason,PayloadHash,SessionId,ProjectId;public long DurationMs;public DateTime CreatedUtc;
    }
    public sealed class AgentAuditLog
    {
        readonly object gate=new object();readonly string path;readonly List<AgentAuditEvent> events=new List<AgentAuditEvent>();
        public AgentAuditLog(string directory){path=Path.Combine(Path.GetFullPath(directory),"audit.json");}
        public IList<AgentAuditEvent> Events{get{lock(gate)return events.ToArray();}}
        public void Record(AgentAuditEvent item){if(item==null)return;lock(gate){events.Add(item);Directory.CreateDirectory(Path.GetDirectoryName(path));using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read))new DataContractJsonSerializer(typeof(List<AgentAuditEvent>)).WriteObject(stream,events);}}
    }
}
