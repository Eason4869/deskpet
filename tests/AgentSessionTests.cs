using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class AgentSessionTests
    {
        static int checks;
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent session tests: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
        static async Task Run()
        {
            var settings=new PetSettings{AgentEnabled=true,ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9876",Model="test"}};
            var api=new Model();var voice=new Speech();using(var session=new ConversationSession(api,voice,()=>null))
            {
                settings.AgentGoal="整理本项目的使用文档";settings.AgentPlanMode=true;
                var task=session.SendTextAsync("写一份文档",settings,"E包");Check(api.ReadOnly && api.System.Contains(settings.AgentGoal),"persistent goal and planning mode reach the actual model request");api.Think("先检查工作目录。");api.Answer("我会先读取文件。");api.Tool("running");
                var pending=session.History.Messages.Last();settings.AgentEnabled=false;await Task.Delay(1050);
                Check(pending.ProcessingStartedAt.HasValue && pending.WorkMode && pending.ProcessingLabel(DateTime.UtcNow).StartsWith("处理时间 1秒"),"timer advances without stream tokens and retains request mode after switching settings");settings.AgentEnabled=true;
                Check(session.History.Messages.Last().Content=="我会先读取文件。" && !session.History.Messages.Last().Reasoning.Contains("先读取文件"),"tool start retains visible commentary without moving it into reasoning");
                Check(session.History.Messages.Last().ToolEvents.Single().Status=="running","running tool reaches the same assistant message");
                api.Tool("complete");api.Answer("# 已完成\n\n**文档**已写入。");api.Finish();await task;
                var reply=session.History.Messages.Last();Check(reply.Status=="complete" && reply.Content.Contains("# 已完成") && reply.Content.StartsWith("我会先读取文件。"),"completion preserves commentary followed by final Markdown");
                Check(reply.ProcessingMilliseconds>=1000 && reply.ProcessingLabel(DateTime.UtcNow)==reply.ProcessingLabel(DateTime.UtcNow.AddHours(1)),"completed response freezes its full processing duration");
                Check(reply.ToolEvents.Single().Status=="complete" && reply.Reasoning.Contains("工作目录") && !reply.Content.Contains("先检查工作目录") && !reply.Reasoning.Contains("先读取文件"),"actual reasoning stays separate from visible commentary");
                await session.SpeakReplyAsync(reply.Id,settings);Check(voice.Text.Contains("已完成") && voice.Text.Contains("先读取文件") && !voice.Text.Contains("**") && !voice.Text.Contains("工作目录"),"manual speech reads the visible body without reasoning");
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"agent-session-history",Guid.NewGuid().ToString("N"));var store=new HistoryStore(path);await store.SaveAsync("pet",session.History.Messages);var saved=store.Load("pet");Check(saved.Last().ToolEvents.Single().Status=="complete","encrypted history retains completed tool records");
                Check(saved.Last().WorkMode && saved.Last().ProcessingMilliseconds==reply.ProcessingMilliseconds && saved.Last().ProcessingStartedAt.HasValue && Math.Abs((saved.Last().ProcessingStartedAt.Value-reply.ProcessingStartedAt.Value).TotalMilliseconds)<1,"encrypted history retains request mode start time and final duration at JSON millisecond precision");
                Check(saved.Last().Content==reply.Content && !saved.Last().Reasoning.Contains("先读取文件"),"history reopen preserves public narration in the body");
                task=session.SendTextAsync("继续",settings,"E包");api.Answer("现在检查第二个文件。");api.Tool("running");api.Tool("complete");api.Answer("检查结果已确认。");api.Tool("running");await session.StopAsync();try{await task;}catch(OperationCanceledException){}
                Check(session.History.Messages.Last().Status=="cancelled" && session.History.Messages.Last().ToolEvents.Single().Status=="error","cancel marks running tool interrupted and keeps history");
                var cancelled=session.History.Messages.Last();Check(cancelled.ProcessingLabel(DateTime.UtcNow)==cancelled.ProcessingLabel(DateTime.UtcNow.AddMinutes(5)),"cancelled timer remains frozen");
                Check(cancelled.Content.Contains("第二个文件") && cancelled.Content.Contains("检查结果已确认") && cancelled.Reasoning.Length==0,"cancellation retains public narration across repeated tool starts");
                task=session.SendTextAsync("继续原任务",settings,"E包");api.Answer("我会先执行流程B。");api.Tool("running");
                session.RegisterGuidance(session.PersonaId,"retain-public-text","顺便告诉我进度",null);api.Answer("好的，进度如下。");api.Tool("complete");api.Tool("running");api.Tool("complete");api.Answer("任务A已完成。");api.Finish();await task;
                var guided=session.History.Messages.Where(m=>m.Role=="assistant").ToArray();
                Check(guided.Any(m=>m.Content=="我会先执行流程B。")&&guided.Last().Content=="好的，进度如下。任务A已完成。","guidance preserves both earlier public text and the follow-up explanation through completion");
                Check(guided.All(m=>!m.Reasoning.Contains("流程B")&&!m.Reasoning.Contains("进度如下")),"guidance never moves public narration into reasoning");
                task=session.SendTextAsync("长任务",settings,"E包");api.Answer(new string('甲',30000));api.Tool("running");api.Tool("complete");api.Answer(new string('乙',30000));api.Finish();await task;
                var longReply=session.History.Messages.Last();Check(longReply.Status=="complete"&&longReply.Content.Length==60000&&longReply.Content.StartsWith(new string('甲',30000))&&longReply.Content.EndsWith(new string('乙',30000)),"public text across multiple rounds does not inherit the single-round 50000-character limit");
                await store.SaveAsync("long-task",session.History.Messages);Check(store.Load("long-task").Last().Content==longReply.Content,"long multi-round public text survives encrypted history reopen");
                session.Clear();
                settings.AgentEnabled=false;settings.AgentWorkspace=Path.Combine(Path.GetTempPath(),"not-created-"+Guid.NewGuid().ToString("N"));await session.SendTextAsync("普通聊天",settings,"E包");Check(api.PlainCalls==1,"disabled agent uses ordinary conversation without tools");Check(api.AgentCalls==4 && !Directory.Exists(settings.AgentWorkspace) && api.PlainSystem.Contains("纯对话模式") && !api.PlainSystem.Contains(settings.AgentGoal),"pure chat ignores work permissions goals and invalid workspace without creating or invoking tools");
                Check(!session.History.Messages.Last().WorkMode && session.History.Messages.Last().ProcessingLabel(DateTime.UtcNow).StartsWith("思考时间 "),"pure chat uses thinking duration label");
            }
        }
        static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        sealed class Model:IConversationApi,IAgentConversationApi
        {
            Action<string> delta,reasoning;Action<AgentToolEvent> tools;TaskCompletionSource<string> done;string text="";public int PlainCalls,AgentCalls;public string PlainSystem;public bool ReadOnly;public string System;
            public Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> m,AgentWorkspace w,Action<string> a,Action<string> r,Action<AgentToolEvent> t,bool stream,CancellationToken c){AgentCalls++;ReadOnly=w.ReadOnly;System=m.First().Content;text="";delta=a;reasoning=r;tools=t;done=new TaskCompletionSource<string>();c.Register(()=>done.TrySetCanceled());return done.Task;}
            public void Think(string value){reasoning(value);}public void Answer(string value){text+=value;delta(value);}public void Tool(string status){if(status=="running")text="";tools(new AgentToolEvent{Id="call",Name="read_file",Summary="已读取 note.md",Status=status});}public void Finish(){done.SetResult(text);}
            public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){PlainCalls++;PlainSystem=m.First().Content;return Task.FromResult("普通回复");}public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){throw new NotSupportedException();}public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public string Text="";public Task SpeakAsync(string text,PetSettings s,CancellationToken c){Text=text;return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
    }
}
