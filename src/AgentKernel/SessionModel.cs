using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
namespace Dafeiyu
{
    [DataContract] public sealed class AgentSessionInput
    {
        [DataMember] public string IdempotencyId="",Kind="prompt",Text="";
        [DataMember] public bool Resume,Steer;
        [DataMember] public List<string> AttachmentIds=new List<string>();
        public AgentSessionInput Copy(){var copy=(AgentSessionInput)MemberwiseClone();copy.AttachmentIds=new List<string>(AttachmentIds??new List<string>());return copy;}
    }
    [DataContract] public sealed class AgentSessionEvent
    {
        [DataMember] public long Sequence;[DataMember] public string Type="",InputId="",StepId="";
        [DataMember] public DateTime CreatedUtc;
    }
    public sealed class AgentSessionProjection
    {
        public string SessionId,RecoveryState;public int PendingCount,DeliveredCount;public long Step;
    }
    [DataContract] public sealed class AgentSessionModel
    {
        [DataMember] public string SessionId="",ProjectId="",ConversationId="";
        [DataMember] public List<AgentSessionInput> Pending=new List<AgentSessionInput>(),Delivered=new List<AgentSessionInput>();
        [DataMember] public List<AgentSessionEvent> Events=new List<AgentSessionEvent>();
        [DataMember] public long Sequence,Step;[DataMember] public string RecoveryState="idle";
        public AgentSessionModel(){}
        public AgentSessionModel(string sessionId,string projectId,string conversationId){SessionId=sessionId;ProjectId=projectId;ConversationId=conversationId;}
        public AgentSessionProjection Projection {get{return new AgentSessionProjection{SessionId=SessionId,RecoveryState=RecoveryState,PendingCount=Pending.Count,DeliveredCount=Delivered.Count,Step=Step};}}
        public AgentSessionInput Enqueue(AgentSessionInput input)
        {
            if(input==null || String.IsNullOrWhiteSpace(input.IdempotencyId) || input.IdempotencyId.Length>200)throw new ArgumentException("会话输入必须具有稳定幂等 ID。");
            var existing=Pending.Concat(Delivered).FirstOrDefault(i=>i.IdempotencyId==input.IdempotencyId);if(existing!=null)return existing;
            var copy=input.Copy();Pending.Add(copy);Append("input.enqueued",copy.IdempotencyId,"");return copy;
        }
        public AgentSessionInput DeliverNext()
        {
            if(Pending.Count==0)return null;var input=Pending[0];Pending.RemoveAt(0);Delivered.Add(input);Append("input.delivered",input.IdempotencyId,"");return input;
        }
        public void StartStep(){Step++;RecoveryState="running";Append("step.started","",Step.ToString());}
        public void CompleteStep(string stepId){RecoveryState="idle";Append("step.completed","",stepId??"");}
        public void FailStep(string stepId){RecoveryState="failed";Append("step.failed","",stepId??"");}
        public void Record(string type,string inputId,string stepId){Append(type,inputId,stepId);}
        void Append(string type,string inputId,string stepId){Events.Add(new AgentSessionEvent{Sequence=++Sequence,Type=type,InputId=inputId,StepId=stepId,CreatedUtc=DateTime.UtcNow});}
    }
}
