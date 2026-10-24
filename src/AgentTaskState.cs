using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    [DataContract]public sealed class AgentGuidance
    {
        [DataMember]public string Id,Text;
        [DataMember]public int Revision;
        [DataMember]public List<ChatAttachment> Attachments;
        public AgentGuidance Copy(){return new AgentGuidance{Id=Id,Text=Text,Revision=Revision,Attachments=ChatAttachment.Snapshot(Attachments)};}
    }
    [DataContract] public sealed class AgentPlanStep
    {
        [DataMember]public string Text,Status;
        public AgentPlanStep Copy(){return (AgentPlanStep)MemberwiseClone();}
    }
    [DataContract]public sealed class AgentTaskState
    {
        [DataMember]public int Version=1;
        [DataMember(EmitDefaultValue=false)]public string RelationshipCompanionId,RelationshipEventId;
        [DataMember]public string Id=Guid.NewGuid().ToString("N"),PersonaId="",Workspace="",Goal="",Status="running",Notice="";
        [DataMember]public List<AgentGuidance> Guidance=new List<AgentGuidance>();
        [DataMember]public int ContextCompactions,EstimatedContextTokens,ModelContextTokens;
        [DataMember]public int ContextInputBudget,ModelRequests;
        [DataMember]public long EstimatedTotalTokens,ChildEstimatedTotalTokens;
        [DataMember]public string ContextModel="";
        [DataMember]public string ContextSummary="",ContextStatus="";
        [DataMember]public DateTime UpdatedUtc=DateTime.UtcNow;
        [DataMember]public List<AgentPlanStep> Plan=new List<AgentPlanStep>();
        [DataMember]public List<AgentChange> Changes=new List<AgentChange>();
        [DataMember]public List<CommandResult> Commands=new List<CommandResult>();
        [DataMember]public List<AgentToolEvent> Tools=new List<AgentToolEvent>();
        [DataMember]public List<AgentChildResult> Children=new List<AgentChildResult>();
        [DataMember]public Dictionary<string,string> Outputs=new Dictionary<string,string>();
        public AgentTaskState Copy(){var c=(AgentTaskState)MemberwiseClone();c.Children=(Children??new List<AgentChildResult>()).Select(child=>child.Copy()).ToList();c.Guidance=(Guidance??new List<AgentGuidance>()).Select(g=>g.Copy()).ToList();c.Plan=(Plan??new List<AgentPlanStep>()).Select(p=>p.Copy()).ToList();c.Changes=(Changes??new List<AgentChange>()).Select(p=>p.Copy()).ToList();c.Commands=(Commands??new List<CommandResult>()).Select(p=>p.Copy()).ToList();c.Tools=(Tools??new List<AgentToolEvent>()).Select(p=>p.Copy()).ToList();c.Outputs=Outputs==null?new Dictionary<string,string>():new Dictionary<string,string>(Outputs);return c;}
        public string CheckpointText()
        {
            var s=new StringBuilder();s.AppendLine(TextResources.Get("agentTaskState.checkpointText.sAppendLine.alternativeA")).AppendLine(Goal);
            foreach(var guide in Guidance??new List<AgentGuidance>())s.AppendLine(TextResources.Format("agentTaskState.checkpointText.sAppendLine",guide.Text));
            if(!String.IsNullOrEmpty(ContextSummary))s.AppendLine(TextResources.Get("agentTaskState.checkpointText.sAppendLine.alternativeB")).AppendLine(ContextSummary);
            foreach(var p in Plan??new List<AgentPlanStep>())s.Append('[').Append(p.Status).Append("] ").AppendLine(p.Text);
            foreach(var c in Changes??new List<AgentChange>())s.Append(TextResources.Get("agentTaskState.checkpointText.sAppend")).Append(c.Path).Append(TextResources.Get("agentTaskState.checkpointText.sAppend.alternativeA")).Append(c.Status).Append(" SHA256 ").AppendLine(c.AfterHash);
            foreach(var c in Commands??new List<CommandResult>())s.Append(TextResources.Get("agentTaskState.checkpointText.sAppend.alternativeB")).Append(c.CommandId).Append(TextResources.Get("agentTaskState.checkpointText.sAppend.alternativeC")).Append(c.ExitCode).Append(TextResources.Get("agentTaskState.checkpointText.sAppend.alternativeD")).AppendLine(c.TimedOut.ToString());
            if(!String.IsNullOrEmpty(Notice))s.AppendLine(Notice);return s.Length>20000?s.ToString(0,20000):s.ToString();
        }
    }
    public sealed partial class AgentWorkspace
    {
        readonly object taskGate=new object();AgentTaskState task;
        public Action<AgentTaskState> TaskChanged {get;set;}
        public AgentTaskState TaskState{get{lock(taskGate)return task==null?null:task.Copy();}}
        public void BeginTask(AgentTaskState state)
        {
            if(state==null)throw new ArgumentNullException("state");kernelTaskId=state.Id;if(kernelSessionModel!=null)kernelSessionModel.Record("task.started",null,state.Id);if(kernelClaims!=null&&!String.IsNullOrEmpty(state.Id))kernelClaims.TryClaim(state.Id,state.Id);if(!String.Equals(Path.GetFullPath(state.Workspace).TrimEnd('\\','/'),Root.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new ArgumentException(TextResources.Get("agentTaskState.beginTask.text"));
            lock(taskGate){task=state.Copy();task.Status="running";task.Notice="";}
            ImportChanges(state.Changes);
            if(desktopSession && !readOnly){runJournal=new AgentRunJournal(RunJournalPath(Root),()=>LockDirectories(RunJournalPath(Root),true));if(runJournal.HasPending(state.Id)){lock(taskGate){task.Status="needs_reconciliation";task.Notice=TextResources.Get("agentTaskState.beginTask.taskNotice");}PublishTask();throw new ApiException(task.Notice);}}
            lock(commandResults){commandResults.Clear();if(state.Commands!=null)commandResults.AddRange(state.Commands.Select(c=>c.Copy()));}
            PublishTask();
        }
        void EnsureTask(){lock(taskGate)if(task==null)task=new AgentTaskState{Workspace=Root};}
        internal void PublishTask()
        {
            EnsureTask();AgentTaskState snapshot;lock(taskGate){task.Changes=Changes.ToList();task.Commands=CommandResults.Reverse().Take(8).Reverse().ToList();task.UpdatedUtc=DateTime.UtcNow;snapshot=task.Copy();}
            var changed=TaskChanged;if(changed!=null)changed(snapshot);
        }
        internal void RecordTool(AgentToolEvent item,string output=null)
        {
            EnsureTask();lock(taskGate){int index=task.Tools.FindIndex(t=>t.Id==item.Id);if(index>=0)task.Tools[index]=item.Copy();else{task.Tools.Add(item.Copy());if(task.Tools.Count>100)task.Tools.RemoveAt(0);}
                if(output!=null){if(task.Outputs.Count>=32 && !task.Outputs.ContainsKey(item.Id))task.Outputs.Remove(task.Outputs.Keys.First());task.Outputs[item.Id]=output.Length>24000?output.Substring(0,24000):output;}}
            PublishTask();
        }
        internal Func<CancellationToken,System.Threading.Tasks.Task<IList<ChatMessage>>> TakeGuidanceAsync {get;set;}
        internal Func<IList<ChatMessage>,bool> GuidanceFits {get;set;}
        internal void RecordGuidance(IEnumerable<AgentGuidance> items){EnsureTask();lock(taskGate){if(task.Guidance==null)task.Guidance=new List<AgentGuidance>();foreach(var item in items)if(!task.Guidance.Any(g=>g.Id==item.Id && g.Revision==item.Revision))task.Guidance.Add(item.Copy());}PublishTask();}
        internal void RecordContextUsage(int tokens,string model,int capacity){EnsureTask();lock(taskGate){task.EstimatedContextTokens=tokens;task.ContextModel=model??"";task.ModelContextTokens=capacity;task.ContextInputBudget=AgentContextPolicy.Resolve(ContextTokenLimit,ContextSchemaOverheadTokens).InputBudget;}PublishTask();}
        internal void RecordModelUsage(long tokens){EnsureTask();lock(taskGate){task.ModelRequests++;task.EstimatedTotalTokens+=Math.Max(0,tokens);}PublishTask();}
        internal void ContextProgress(string status){EnsureTask();lock(taskGate)task.ContextStatus=status;PublishTask();if(kernelSessionModel!=null)kernelSessionModel.Record("context.progress",null,status);}
        internal void RecordCompaction(int tokens,string summary){EnsureTask();lock(taskGate){task.ContextCompactions++;task.EstimatedContextTokens=tokens;task.ContextSummary=summary??"";task.ContextStatus="ready";}PublishTask();if(kernelSessionModel!=null)kernelSessionModel.Record("context.compacted",null,"summary");}
        public void FinishTask(string status,string notice)
        {
            EnsureTask();string claimId;lock(taskGate)claimId=task==null?"":task.Id;lock(taskGate){task.Status=status;task.Notice=notice??"";foreach(var item in task.Tools.Where(t=>t.Status=="running")){item.Status="error";item.Summary+=TextResources.Get("agentTaskState.finishTask.itemSummary");}}if(kernelSessionModel!=null&&!String.IsNullOrEmpty(claimId))kernelSessionModel.Record(status=="complete"?"task.completed":"task.failed",null,claimId);if(kernelClaims!=null&&!String.IsNullOrEmpty(claimId)){if(status=="complete")kernelClaims.Complete(claimId,claimId);else kernelClaims.Fail(claimId,claimId);}PublishTask();
        }
        string UpdatePlan(Dictionary<string,object> args)
        {
            object value;if(!args.TryGetValue("steps",out value) || !(value is object[]))throw new ArgumentException(TextResources.Get("agentTaskState.updatePlan.text"));var items=(object[])value;if(items.Length<1 || items.Length>20)throw new ArgumentException(TextResources.Get("agentTaskState.updatePlan.text.alternativeA"));var plan=new List<AgentPlanStep>();
            foreach(object item in items){var data=item as Dictionary<string,object>;if(data==null)throw new ArgumentException(TextResources.Get("agentTaskState.updatePlan.text.alternativeB"));string text=GetString(data,"text",null),status=GetString(data,"status",null);if(String.IsNullOrWhiteSpace(text) || text.Length>250 || (status!="pending" && status!="in_progress" && status!="complete"))throw new ArgumentException(TextResources.Get("agentTaskState.updatePlan.text.alternativeC"));plan.Add(new AgentPlanStep{Text=text,Status=status});}
            if(plan.Count(p=>p.Status=="in_progress")>1)throw new ArgumentException(TextResources.Get("agentTaskState.updatePlan.text.alternativeD"));EnsureTask();lock(taskGate)task.Plan=plan;PublishTask();return TextResources.Get("agentTaskState.updatePlan.text.alternativeE");
        }
        internal string SavedOutput(string id,int offset,int limit)
        {
            EnsureTask();string value;lock(taskGate)if(!task.Outputs.TryGetValue(id,out value))throw new ArgumentException(TextResources.Get("agentTaskState.savedOutput.text"));offset=Math.Min(offset,value.Length);int count=Math.Min(limit,value.Length-offset);
            return new JavaScriptSerializer().Serialize(new{output_id=id,Output=value.Substring(offset,count),next_offset=offset+count<value.Length?(int?)(offset+count):null});
        }
        void ImportChanges(IEnumerable<AgentChange> imported)
        {
            if(imported==null)return;foreach(var source in imported){
                if(source==null || source.Status=="restored")continue;string path=Resolve(source.Path);string expected=Path.Combine(RestrictToRoot?Root:Path.GetDirectoryName(path),BackupFolder);
                if(source.BackupPath!=null && (!String.Equals(Path.GetDirectoryName(Path.GetFullPath(source.BackupPath)),expected,StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(source.BackupPath),"^[a-f0-9]{32}\\.bak$")))throw new ArgumentException(TextResources.Get("agentTaskState.importChanges.text"));
                if(source.AfterHash==null || !System.Text.RegularExpressions.Regex.IsMatch(source.AfterHash,"^[a-f0-9]{64}$"))throw new ArgumentException(TextResources.Get("agentTaskState.importChanges.text.alternativeA"));
                lock(changes)if(!changes.Any(c=>String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase)))changes.Add(source.Copy());observed[path]=source.AfterHash;
            }
        }
    }
}
