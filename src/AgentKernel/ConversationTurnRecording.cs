using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dafeiyu
{
    internal sealed class AgentTurnRecording
    {
        const int FragmentCharacters=16000;
        readonly AgentTurnJournal journal;
        readonly Action<Action> enqueue;
        readonly Action failed;
        readonly object gate=new object();
        readonly List<AgentTurnEvent> pending=new List<AgentTurnEvent>();
        IDisposable lease;
        bool scheduled,closing,writeFailed;
        string taskId;

        internal string StreamId { get; private set; }
        internal long Revision { get; private set; }

        internal AgentTurnRecording(AgentTurnJournal journal,Action<Action> schedule,Action failed)
        {
            this.journal=journal;
            enqueue=schedule;
            this.failed=failed;
        }

        internal void BeginSegment(string streamId,string task)
        {
            StreamId=streamId;
            taskId=task;
            Revision=0;
            Queue(new[]{Record("turn.started"),Record("step.started")});
        }

        internal void Text(string text)
        {
            if(!String.IsNullOrEmpty(text))Queue(Fragments("reply.delta",text));
        }

        internal static bool IsUnfinished(AgentToolEvent tool)
        {
            return tool.Status=="running"||tool.Status=="pending"||tool.Status=="waiting_approval";
        }

        internal void Tool(AgentToolEvent item)
        {
            if(item==null||String.IsNullOrEmpty(item.Id))return;
            var record=Record(IsUnfinished(item)?"tool.started":"tool.finished");
            record.CallId=item.Id;
            record.Outcome=item.Status;
            record.Tool=item.Copy();
            Queue(new[]{record});
        }

        internal void FinishSegment(string content,string outcome,IEnumerable<AgentToolEvent> tools=null,bool transfer=false)
        {
            // Only a terminal event makes the replacement authoritative during replay.
            // A torn batch leaves the previous public fragments intact.
            var records=Fragments("reply.final.part",content??"").ToList();
            if(tools!=null)foreach(var tool in tools.Where(item=>!String.IsNullOrEmpty(item.Id)))
            {
                var record=Record("tool.finished");
                record.CallId=tool.Id;
                record.Outcome=transfer?"transferred":tool.Status;
                record.Tool=transfer?null:tool.Copy();
                records.Add(record);
            }
            var step=Record("step.finished");
            step.Outcome=outcome;
            records.Add(step);
            var turn=Record("turn.finished");
            turn.Outcome=outcome;
            if(content=="")turn.Text="";
            records.Add(turn);
            Queue(records);
        }

        IEnumerable<AgentTurnEvent> Fragments(string kind,string text)
        {
            for(int offset=0;offset<text.Length;)
            {
                int length=Math.Min(FragmentCharacters,text.Length-offset);
                if(offset+length<text.Length&&Char.IsHighSurrogate(text[offset+length-1]))length--;
                var record=Record(kind);
                record.Text=text.Substring(offset,length);
                yield return record;
                offset+=length;
            }
        }

        AgentTurnEvent Record(string kind)
        {
            return new AgentTurnEvent{Kind=kind,TurnId=StreamId,TaskId=taskId,StepId=StreamId,Revision=++Revision};
        }

        void Queue(IEnumerable<AgentTurnEvent> events)
        {
            lock(gate)
            {
                if(closing||writeFailed)return;
                pending.AddRange(events);
                Schedule();
            }
        }

        internal void Close()
        {
            lock(gate)
            {
                if(closing)return;
                closing=true;
                Schedule();
            }
        }

        void Schedule()
        {
            if(scheduled)return;
            scheduled=true;
            enqueue(FlushBatch);
        }

        void FlushBatch()
        {
            AgentTurnEvent[] batch;
            bool close;
            lock(gate)
            {
                scheduled=false;
                batch=pending.ToArray();
                pending.Clear();
                close=closing;
                if(writeFailed)return;
            }
            try
            {
                if(batch.Length!=0)
                {
                    if(lease==null)lease=journal.AcquireTurn();
                    journal.AppendBatch(batch);
                }
            }
            catch(Exception error)
            {
                if(!HistoryStore.IsAgentTurnStorageError(error))throw;
                lock(gate)writeFailed=true;
                close=true;
                failed();
            }
            finally
            {
                if(close&&lease!=null){lease.Dispose();lease=null;}
            }
        }
    }

    public sealed partial class ConversationSession
    {
        void BeginAgentTurnSegment(Operation operation,string taskId)
        {
            if(store==null||operation.TurnRecordingDisabled||!operation.Snapshot.SaveChatHistory||!operation.Live.SaveChatHistory||!saveEnabled())return;
            if(operation.TurnRecording==null)operation.TurnRecording=store.CreateAgentTurnRecording(operation.PersonaId);
            string stream=Guid.NewGuid().ToString("N");
            operation.History.MarkAgentTurn(operation.ReplyId,stream,0);
            // The initial checkpoint binds the display reply before any stream events can be written.
            Save(operation.PersonaId,operation.History);
            var recording=operation.TurnRecording;
            recording.BeginSegment(stream,taskId);
            foreach(var tool in operation.ToolEvents)recording.Tool(tool);
            operation.History.MarkAgentTurn(operation.ReplyId,stream,recording.Revision);
        }

        AgentTurnRecording ActiveAgentTurnRecording(Operation operation)
        {
            if(operation.TurnRecording!=null&&(!operation.Live.SaveChatHistory||!saveEnabled()))CloseAgentTurnRecording(operation);
            return operation.TurnRecording;
        }

        void FinishAgentTurnSegment(Operation operation,string content,string status,bool close,IEnumerable<AgentToolEvent> tools)
        {
            var recording=ActiveAgentTurnRecording(operation);
            if(recording==null)return;
            recording.FinishSegment(String.Equals(content,operation.Partial.ToString(),StringComparison.Ordinal)?null:content,status,tools,!close);
            operation.History.MarkAgentTurn(operation.ReplyId,recording.StreamId,recording.Revision);
            if(close)CloseAgentTurnRecording(operation);
        }

        static void CloseAgentTurnRecording(Operation operation)
        {
            if(operation.TurnRecording==null)return;
            operation.TurnRecording.Close();
            operation.TurnRecording=null;
            operation.TurnRecordingDisabled=true;
        }
    }

    public sealed partial class HistoryStore
    {
        internal static string AgentTurnsDirectory(string historyDirectory)
        {
            return Path.Combine(historyDirectory,"agent-turns");
        }

        static string AgentTurnDirectory(string historyPath)
        {
            return Path.Combine(AgentTurnsDirectory(Path.GetDirectoryName(historyPath)),Path.GetFileNameWithoutExtension(historyPath));
        }

        static string AgentTurnPath(string historyPath)
        {
            return Path.Combine(AgentTurnDirectory(historyPath),AgentTurnJournal.FileName);
        }

        internal AgentTurnRecording CreateAgentTurnRecording(string persona)
        {
            string path=FilePath(persona);
            return new AgentTurnRecording(new AgentTurnJournal(AgentTurnDirectory(path)),action=>Enqueue(action),
                ()=>SetWarning(AgentTurnPath(path),TextResources.Get("historyStore.saveAsync.setWarning.alternativeA")));
        }

        internal static bool IsAgentTurnStorageError(Exception error)
        {
            return IsStorageError(error)||error is InvalidOperationException||error is FormatException;
        }

        static bool CanReplayAgentReply(ChatMessage item)
        {
            return item!=null&&item.Role=="assistant"&&!String.IsNullOrEmpty(item.AgentTurnStreamId)
                &&(item.Status=="pending"||item.Status=="interrupted");
        }

        void ReplayAgentTurns(string historyPath,IList<ChatMessage> messages)
        {
            string path=AgentTurnPath(historyPath);
            try
            {
                var journal=new AgentTurnJournal(AgentTurnDirectory(historyPath));
                journal.RecoverInterrupted();
                var events=journal.Read().ToLookup(item=>item.TurnId,StringComparer.Ordinal);
                foreach(var reply in messages.Where(CanReplayAgentReply))ReplayAgentReply(reply,events[reply.AgentTurnStreamId]);
                SetWarning(path,"");
            }
            catch(Exception error)
            {
                if(!IsAgentTurnStorageError(error))throw;
                SetWarning(path,TextResources.Get("historyStore.load.setWarning"));
            }
        }

        static void ReplayAgentReply(ChatMessage reply,IEnumerable<AgentTurnEvent> events)
        {
            var publicText=new StringBuilder(reply.Content??"");
            var finalText=new StringBuilder();
            var tools=reply.ToolEvents==null?new List<AgentToolEvent>():reply.ToolEvents.Where(tool=>tool!=null).Select(tool=>tool.Copy()).ToList();
            long checkpoint=reply.AgentTurnRevision;
            string status=reply.Status;
            foreach(var item in events)
            {
                if(item.Kind=="reply.final.part")finalText.Append(item.Text);
                bool recovery=item.Revision==0&&item.Outcome=="interrupted";
                if(!recovery&&item.Revision<=checkpoint)continue;
                reply.AgentTurnRevision=Math.Max(reply.AgentTurnRevision,item.Revision);
                switch(item.Kind)
                {
                    case "reply.delta":publicText.Append(item.Text);break;
                    case "tool.started":
                    case "tool.finished":
                        int index=tools.FindIndex(tool=>tool.Id==item.CallId);
                        if(item.Tool!=null)
                        {
                            if(index<0)tools.Add(item.Tool.Copy());else tools[index]=item.Tool.Copy();
                        }
                        else if(item.Outcome=="transferred"&&index>=0)tools.RemoveAt(index);
                        else if(recovery&&index>=0&&AgentTurnRecording.IsUnfinished(tools[index]))InterruptTool(tools[index]);
                        break;
                    case "turn.finished":
                        status=item.Outcome;
                        if(!recovery&&(item.Text!=null||finalText.Length>0)){publicText.Clear();publicText.Append(item.Text??finalText.ToString());}
                        break;
                }
            }
            if(status!="pending")foreach(var tool in tools.Where(AgentTurnRecording.IsUnfinished))InterruptTool(tool);
            reply.Update(publicText.ToString(),status);
            reply.SetTools(tools);
        }

        static void InterruptTool(AgentToolEvent tool)
        {
            tool.Status="error";
            tool.Summary+=TextResources.Get("petBusiness.normalize.itemSummary");
        }

        void CompactAgentTurns(string historyPath,IList<ChatMessage> saved)
        {
            string path=AgentTurnPath(historyPath);
            try
            {
                var journal=new AgentTurnJournal(AgentTurnDirectory(historyPath));
                var replies=saved.ToLookup(reply=>reply.AgentTurnStreamId,StringComparer.Ordinal);
                bool compacted=journal.TryClear(events=>events.GroupBy(item=>item.TurnId,StringComparer.Ordinal).All(turn=>{
                    // A successful save can retire finished streams omitted by regeneration, guidance or retention.
                    if(!replies.Contains(turn.Key))return turn.Any(item=>item.Kind=="turn.finished");
                    return replies[turn.Key].Any(reply=>reply.Status!="pending"&&reply.AgentTurnRevision>=turn.Max(item=>item.Revision));
                }));
                if(compacted)SetWarning(path,"");
            }
            catch(Exception error)
            {
                if(!IsAgentTurnStorageError(error))throw;
                SetWarning(path,TextResources.Get("historyStore.saveAsync.setWarning.alternativeA"));
            }
        }

        void DiscardAgentTurns(string historyPath)
        {
            new AgentTurnJournal(AgentTurnDirectory(historyPath)).Discard();
            SetWarning(AgentTurnPath(historyPath),"");
        }
    }
}
