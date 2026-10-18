using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class ChangeSummaryPersistenceTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        public static int Main()
        {
            try{Run().GetAwaiter().GetResult();Console.WriteLine("Change summary persistence tests: "+checks+" passed");return 0;}
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        static async Task Run()
        {
            string root=Path.Combine(Path.GetTempPath(),"deskpet-summary-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var state=new AgentTaskState{Id="fixture-task",Workspace=root,Status="complete",Goal="PRIVATE_INTENT",Notice="PRIVATE_NOTICE",Changes=new List<AgentChange>{new AgentChange{Path=Path.Combine(root,"first.txt"),BackupPath=Path.Combine(root,"backup.bak"),Status="modified",AddedLines=2,RemovedLines=1}}};state.Outputs["read"]="PRIVATE_OUTPUT";
                var summary=AgentReplyChangeSummary.FromTask(state);state.Changes[0].AddedLines=99;
                Check(summary.Changes[0].AddedLines==2,"task changes are captured as an independent snapshot");
                var message=new ChatMessage("assistant","done"){AgentTaskId=state.Id,ChangeSummary=summary};var copy=message.Copy();copy.ChangeSummary.Changes[0].AddedLines=100;copy.ChangeSummary.Changes.Clear();
                Check(message.ChangeSummary.Changes.Count==1 && message.ChangeSummary.Changes[0].AddedLines==2,"message copy deep copies the summary and every file change");
                var converted=summary.ToTask();converted.Changes[0].RemovedLines=101;
                Check(converted.Id=="fixture-task" && converted.Workspace==root && converted.Status=="complete" && summary.Changes[0].RemovedLines==1,"summary reconstructs independent minimal task state");
                string serialized=Json(summary);Check(!serialized.Contains("PRIVATE_") && !serialized.Contains("Goal") && !serialized.Contains("Outputs") && !serialized.Contains("Tools"),"summary excludes intent notices and tool outputs");
                var legacy=(ChatMessage)new DataContractJsonSerializer(typeof(ChatMessage)).ReadObject(new MemoryStream(Encoding.UTF8.GetBytes("{\"Role\":\"assistant\",\"Content\":\"legacy\",\"Status\":\"complete\"}")));
                var legacyLog=new ConversationLog();legacyLog.Replace(new[]{legacy});Check(legacyLog.Messages.Single().ChangeSummary==null && legacyLog.Messages.Single().Content=="legacy","old messages without summaries remain readable");
                var log=new ConversationLog();log.Commit("question","answer");var reply=log.Messages.Last();log.LinkAgentTask(reply.Id,state.Id);log.UpdateAgentTaskSummary(summary.ToTask());
                log.LinkAgentTask(reply.Id,state.Id);Check(log.Messages.Last().ChangeSummary!=null && log.Messages.Last().ChangeSummary.Changes.Single().AddedLines==2,"rebinding the same task preserves its existing summary");
                log.LinkAgentTask(reply.Id,"replacement-task");Check(log.Messages.Last().AgentTaskId=="replacement-task" && log.Messages.Last().ChangeSummary==null,"binding a new task clears the previous summary before any task update");
                log.LinkAgentTask(reply.Id,state.Id);log.UpdateAgentTaskSummary(summary.ToTask());
                var request=log.ForRequest("system","next");Check(request.All(m=>m.ChangeSummary==null) && !Json(request).Contains("backup.bak"),"model request excludes persisted summary and backup paths");
                log.Commit("second question","second answer");var regeneration=log.ForRegeneration(log.Messages.Last().Id,"system");Check(regeneration.Any(m=>m.Content=="answer") && regeneration.All(m=>m.ChangeSummary==null),"regeneration retains earlier messages without their persisted summary state");
                var isolatedStore=new HistoryStore(Path.Combine(root,"roundtrip"));await isolatedStore.SaveAsync("fixture",log.Messages);var restored=isolatedStore.Load("fixture").Single(m=>m.Id==reply.Id);
                Check(restored.ChangeSummary.TaskId=="fixture-task" && restored.ChangeSummary.Changes.Single().AddedLines==2 && restored.ChangeSummary.Changes.Single().RemovedLines==1,"encrypted history reload retains task identity and file line counts");
                Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(isolatedStore.DirectoryPath,"*.history").Single())).Contains("first.txt"),"persisted file summary is encrypted on disk");
                await SessionLifecycle(root);
            }
            finally{Directory.Delete(root,true);}
        }
        static async Task SessionLifecycle(string root)
        {
            string workspace=Path.Combine(root,"project");Directory.CreateDirectory(workspace);File.WriteAllText(Path.Combine(workspace,"first.txt"),"before\n");File.WriteAllText(Path.Combine(workspace,"second.txt"),"base\n");
            var settings=new PetSettings{AgentEnabled=true,AgentFullAccess=true,ReadReplies=false,AgentWorkspace=workspace,SaveChatHistory=true,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1",Model="fixture",ToolSupport=CapabilitySupport.Supported}};
            var api=new Model();var store=new HistoryStore(Path.Combine(root,"session-history"));
            using(var session=new ConversationSession(api,new Speech(),()=>null,store))
            {
                try
                {
                session.SelectPersona("fixture");var first=session.SendTextAsync("first task",settings,"fixture");await WaitForStart(api,first);
                var running=session.History.Messages.Last();string firstId=running.Id,firstTask=running.AgentTaskId;
                Check(running.ChangeSummary!=null && running.ChangeSummary.Status=="running" && running.ChangeSummary.Changes.Single().AddedLines==2 && running.ChangeSummary.Changes.Single().RemovedLines==1,"running file edits update the matching assistant summary in memory");
                await session.FlushHistoryAsync();Check(store.Load("fixture").Last().ChangeSummary.Changes.Single().AddedLines==2,"existing history checkpoints persist in-progress summary counts");
                api.Finish.TrySetResult("first done");await first;await session.FlushHistoryAsync();
                Check(store.Load("fixture").Last().ChangeSummary.Status=="complete","task completion saves final summary after the final reply checkpoint");
                api.Reset();var second=session.SendTextAsync("second task",settings,"fixture");await WaitForStart(api,second);
                Check(session.History.Messages.Single(m=>m.Id==firstId).ChangeSummary.TaskId==firstTask && session.History.Messages.Single(m=>m.Id==firstId).ChangeSummary.Changes.Single().Path.EndsWith("first.txt"),"starting another task preserves the previous reply summary");
                Check(api.Request.All(m=>m.ChangeSummary==null) && !Json(api.Request).Contains(".bak"),"next actual agent request does not receive summary backup paths");
                api.Finish.TrySetResult("second done");await second;
                string secondTask=session.CurrentAgentTask.Id;session.History.Commit("follow up","same task reply");session.History.LinkAgentTask(session.History.Messages.Last().Id,secondTask);
                string restore=await session.RestoreAgentFileAsync(Path.Combine(workspace,"second.txt"),settings);await session.FlushHistoryAsync();
                Check(!restore.StartsWith("错误：") && File.ReadAllText(Path.Combine(workspace,"second.txt"))=="base\n","restore executes the real file tool without a new assistant reply");
                var matching=session.History.Messages.Where(m=>m.Role=="assistant" && m.AgentTaskId==secondTask).ToList();
                Check(matching.Count==2 && matching.All(m=>m.ChangeSummary.Status=="complete" && m.ChangeSummary.Changes.Single().Status=="restored"),"restore updates every assistant snapshot linked to its task even when ReplyId is null");
                var persisted=store.Load("fixture");Check(persisted.Where(m=>m.AgentTaskId==secondTask).All(m=>m.ChangeSummary.Changes.Single().Status=="restored"),"restore completion persists updated summaries without reply streaming");
                Check(persisted.Single(m=>m.Id==firstId).ChangeSummary.Changes.Single().Status=="modified" && persisted.Single(m=>m.Id==firstId).ChangeSummary.Changes.Single().AddedLines==2,"restoring a newer task leaves earlier summary counts unchanged");
                Check(!session.ExportHistory().Contains(".bak") && !session.ExportHistory().Contains("ChangeSummary"),"text history export contains message text without summary metadata");
                }
                finally{session.StopAsync().GetAwaiter().GetResult();session.FlushHistoryAsync().GetAwaiter().GetResult();}
            }
        }
        static async Task WaitForStart(Model api,Task operation)
        {var completed=await Task.WhenAny(api.Started.Task,operation,Task.Delay(10000));if(completed==operation)await operation;if(!api.Started.Task.IsCompleted)throw new Exception("fake agent did not start within ten seconds");}
        static string Json<T>(T value){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);return Encoding.UTF8.GetString(stream.ToArray());}}
        sealed class Model:IConversationApi,IAgentConversationApi
        {
            public TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<string> Finish=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);public IList<ChatMessage> Request;int calls;
            public void Reset(){Started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);Finish=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);}
            public async Task<string> ChatAgentAsync(ConnectionSettings settings,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tools,bool stream,CancellationToken cancel)
            {
                Request=messages;calls++;string result=await workspace.ExecuteAsync("write_file",new JavaScriptSerializer().Serialize(new{path=calls==1?"first.txt":"second.txt",content=calls==1?"after\nsecond\n":"different\n"}),cancel);if(result.StartsWith("错误："))throw new Exception(result);workspace.RecordTool(new AgentToolEvent{Id="write-"+calls,Name="write_file",Status="complete"},result);Started.TrySetResult(true);
                using(cancel.Register(()=>Finish.TrySetCanceled()))return await Finish.Task;
            }
            public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){throw new NotSupportedException();}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken c){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){throw new NotSupportedException();}
            public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string text,PetSettings s,CancellationToken c){return Task.FromResult(true);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
    }
}
