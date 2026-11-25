using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class HistoryTests
    {
        static int passed,failed;
        public static int Main()
        {
            Run("display retains earlier turns while model context uses last ten",delegate {
                var log=new ConversationLog();for(int i=0;i<12;i++)log.Commit("u"+i,"a"+i);
                Check(log.Messages.Count==24,"earlier display turns were lost");
                var request=log.ForRequest("persona","new");Check(request.Count==26 && request[1].Content=="u0","request retains stored complete pairs for budget selection");
            });
            Run("pending turns restore as interrupted and never enter model context",delegate {
                var log=new ConversationLog();log.Commit("ok","answer");ChatMessage user;var partial=log.BeginTurn("cancel","text",out user);log.Update(partial.Id,"partial","pending");
                var restored=new ConversationLog();restored.Replace(log.Messages);
                Check(restored.Messages.Last().Status=="interrupted" && restored.ForRequest("","new").Count==3,"interrupted reply leaked into context");
            });
            Run("retention removes whole old turns after two hundred",delegate {
                var log=new ConversationLog();for(int i=0;i<201;i++)log.Commit("u"+i,"a"+i);
                Check(log.Messages.Count==400 && log.Messages[0].Content=="u1" && log.Messages.Last().Content=="a200","turn trimming split or retained old turn");
            });
            Run("encrypted restart preserves stable ids and isolates pets",delegate {
                string directory=Temp();var store=new HistoryStore(directory);var log=new ConversationLog();log.Commit("sensitive-history-秘密","answer");
                store.SaveAsync("e-bao",log.Messages).GetAwaiter().GetResult();var files=Directory.GetFiles(directory);
                Check(files.Length==1 && !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains("sensitive-history"),"history was written in plaintext");
                var fresh=new HistoryStore(directory);Check(fresh.Load("e-bao")[0].Id==log.Messages[0].Id && fresh.Load("marfu").Count==0,"restart or pet isolation failed");
            });
            Run("Agent turn replay restores only events newer than its history checkpoint",AgentTurnReplay);
            Run("a finished Agent turn is recovered and compacted only after the history commit",AgentTurnCompaction);
            Run("a reader retries a replaced history snapshot after Agent journal compaction",AgentTurnConcurrentCommit);
            Run("superseded Agent turns retire after the replacement history is durable",AgentTurnReplacement);
            Run("clearing history removes encrypted Agent turn events",AgentTurnClear);
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)
                Run("a live work session records visible progress and honours history opt out",()=>AgentTurnSessionAsync().GetAwaiter().GetResult());
            Run("serialized writes and clear leave no stale history",delegate {
                string directory=Temp();var store=new HistoryStore(directory);var log=new ConversationLog();log.Commit("first","one");var first=store.SaveAsync("pet",log.Messages);log.Commit("second","two");var second=store.SaveAsync("pet",log.Messages);store.FlushAsync().GetAwaiter().GetResult();
                Check(store.Load("pet").Count==4,"last queued write lost");store.ClearAsync("pet").GetAwaiter().GetResult();Check(store.Load("pet").Count==0 && Directory.GetFiles(directory).Length==0,"clear left stale encrypted history");
            });
            Run("save and damaged-read failures surface safe warnings",delegate {
                string directory=Temp();string obstacle=Path.Combine(directory,"file");File.WriteAllText(obstacle,"x");var log=new ConversationLog();log.Commit("secret","answer");var failedStore=new HistoryStore(obstacle);failedStore.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();Check(!String.IsNullOrEmpty(failedStore.Warning) && !failedStore.Warning.Contains(obstacle),"save warning missing or leaks path");
                var store=new HistoryStore(directory);store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();string path=Directory.GetFiles(directory).Single(p=>p.EndsWith(".history"));File.WriteAllText(path,"broken");Check(store.Load("pet").Count==0 && !String.IsNullOrEmpty(store.Warning) && File.ReadAllText(path)=="broken","damaged file mishandled");
            });
            Run("eight mebibyte limit drops entire oversized turn",delegate {
                var log=new ConversationLog();log.Commit("old",new string('界',2800000));log.Commit("new","answer");Check(log.Messages.Count==2 && log.Messages[0].Content=="new","oversized turn survived or trimming split it");
            });
            Run("cancel retains partial display but excludes incomplete pair from context",delegate { CancelRetainsAsync().GetAwaiter().GetResult(); });
            Run("request passes complete history to the shared context budget",delegate {
                var log=new ConversationLog();for(int i=0;i<3;i++)log.Commit("u"+i,new string('a',45000));var request=log.ForRequest("","new");Check(request.Count==7 && request[0].Content=="u0","complete history was discarded before budget selection");
            });
            Run("stream updates same id and cancelled partial survives restart",delegate { StreamingCancelAsync().GetAwaiter().GetResult(); });
            Run("failed stream retains partial and safe error status",delegate { StreamingErrorAsync().GetAwaiter().GetResult(); });
            Run("disabled saving preserves existing file but clear deletes it",delegate { DisabledSaveAsync().GetAwaiter().GetResult(); });
            Run("dispose flushes interrupted turn and rejects late stream publishing",delegate { LateDisposeAsync().GetAwaiter().GetResult(); });
            Run("DND enabled while waiting suppresses eventual speech",delegate { QuietAsync().GetAwaiter().GetResult(); });
            Run("ASR and Live preserve source, stable event ids and pet isolation",delegate { SourcesAsync().GetAwaiter().GetResult(); });
            Run("unreadable encrypted history remains protected until explicit clear",delegate {
                string directory=Temp();var store=new HistoryStore(directory);var log=new ConversationLog();log.Commit("one","answer");store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();string path=Directory.GetFiles(directory).Single();File.WriteAllText(path,"unreadable-original");store.Load("pet");log.Commit("new","two");store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();Check(File.ReadAllText(path)=="unreadable-original" && !String.IsNullOrEmpty(store.Warning),"save overwrote unreadable original");store.ClearAsync("pet").GetAwaiter().GetResult();store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();Check(store.Load("pet").Count==4,"explicit clear did not permit future saving");
            });
            Run("stream checkpoint throttles partial saves and final flush keeps last delta",delegate { CheckpointAsync().GetAwaiter().GetResult(); });
            Run("running snapshot cannot mutate later persona history",delegate { PersonaSnapshotAsync().GetAwaiter().GetResult(); });
            Run("turn ids prevent replaying mutated request snapshots",delegate {
                var log=new ConversationLog();log.Commit("original","answer");var request=log.ForRequest("persona","next");request.Clear();Check(log.Messages.Count==2 && log.Messages[0].Content=="original","request mutation changed history");
            });
            Run("Live interrupted and stopped captions persist without successful context",delegate { IncompleteLiveAsync().GetAwaiter().GetResult(); });
            Run("clearing a never-created history directory succeeds without warning",delegate {
                string parent=Temp();string directory=Path.Combine(parent,"never-created-history");var store=new HistoryStore(directory);store.ClearAsync("e-bao").GetAwaiter().GetResult();store.FlushAsync().GetAwaiter().GetResult();Check(String.IsNullOrEmpty(store.Warning) && store.Load("e-bao").Count==0 && !Directory.Exists(directory),"empty clear created storage or produced a failure warning");
            });
            Console.WriteLine("History tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static string Temp(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"history-test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        static ChatMessage BeginAgentReply(ConversationLog log,string stream)
        {
            ChatMessage user;
            var reply=log.BeginTurn("agent request","text",out user);
            log.MarkAgentTurn(reply.Id,stream,0);
            return reply;
        }
        static void AgentTurnReplay()
        {
            string path=Temp(),stream=Guid.NewGuid().ToString("N");
            var store=new HistoryStore(path);
            var log=new ConversationLog();
            var reply=BeginAgentReply(log,stream);
            var recording=store.CreateAgentTurnRecording("pet");
            recording.BeginSegment(stream,"task");
            recording.Text("already saved / ");
            log.Update(reply.Id,"already saved / ","pending");
            log.MarkAgentTurn(reply.Id,stream,recording.Revision);
            store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();
            recording.Text("durable tail");
            recording.Tool(new AgentToolEvent{Id="done",Name="read_file",Summary="safe result",Status="complete"});
            recording.Tool(new AgentToolEvent{Id="running",Name="read_file",Summary="safe progress",Status="running"});
            recording.Close();
            store.FlushAsync().GetAwaiter().GetResult();

            var fresh=new HistoryStore(path);
            var loaded=fresh.Load("pet").Last();
            Check(loaded.Id==reply.Id && loaded.Status=="interrupted" && loaded.Content=="already saved / durable tail","replay lost or duplicated the public output");
            Check(loaded.ToolEvents.Count==2 && loaded.ToolEvents[0].Status=="complete" && loaded.ToolEvents[0].Summary=="safe result" && loaded.ToolEvents[1].Status=="error","replay did not retain the result and close the unfinished tool");
            Check(fresh.Load("pet").Last().Content==loaded.Content && fresh.Load("other").Count==0,"repeat replay changed the projection or crossed persona boundaries");
            var restored=new ConversationLog();restored.Replace(fresh.Load("pet"));
            Check(restored.ForRequest("","next").Count==1,"interrupted replay entered the model context");
        }
        static void AgentTurnCompaction()
        {
            string path=Temp(),stream=Guid.NewGuid().ToString("N");
            var store=new HistoryStore(path);
            var log=new ConversationLog();BeginAgentReply(log,stream);
            store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();
            var recording=store.CreateAgentTurnRecording("pet");
            recording.BeginSegment(stream,"task");
            recording.Text("draft text");
            recording.Tool(new AgentToolEvent{Id="call",Name="read_file",Status="running",Summary="tool progress"});
            recording.FinishSegment("final answer","complete",new[]{new AgentToolEvent{Id="call",Name="read_file",Status="error",Summary="actual terminal reason"}});
            recording.Close();
            store.FlushAsync().GetAwaiter().GetResult();

            string eventPath=Directory.GetFiles(path,"turn.events",SearchOption.AllDirectories).Single();
            var reopened=new HistoryStore(path);
            var recovered=reopened.Load("pet");
            Check(recovered.Last().Status=="complete" && recovered.Last().Content=="final answer" && File.Exists(eventPath),"replay lost terminal state or retired its evidence before saving");
            Check(recovered.Last().ToolEvents.Single().Summary=="actual terminal reason","journal-only replay replaced the actual terminal tool result");
            reopened.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();
            Check(File.Exists(eventPath),"an older checkpoint discarded newer events");
            reopened.SaveAsync("pet",recovered).GetAwaiter().GetResult();
            Check(!File.Exists(eventPath) && reopened.Load("pet").Last().Content=="final answer","committed final history did not take over the projection");
        }
        static void AgentTurnConcurrentCommit()
        {
            var store=new HistoryStore(Temp());var log=new ConversationLog();string stream=Guid.NewGuid().ToString("N");
            var reply=BeginAgentReply(log,stream);
            store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();
            var recording=store.CreateAgentTurnRecording("pet");recording.BeginSegment(stream,"task");recording.Text("public draft");
            recording.FinishSegment("final reply","complete");recording.Close();store.FlushAsync().GetAwaiter().GetResult();
            var reader=new HistoryStore(store.DirectoryPath);
            reader.BeforeAgentTurnReplay=()=>{
                reader.BeforeAgentTurnReplay=null;
                log.MarkAgentTurn(reply.Id,stream,recording.Revision);
                log.Update(reply.Id,"final reply","complete");
                store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();
            };
            var restored=reader.Load("pet").Last();
            Check(restored.Status=="complete"&&restored.Content=="final reply","the reader combined an old checkpoint with a newer empty journal");
        }
        static void AgentTurnReplacement()
        {
            var store=new HistoryStore(Temp());var old=new ConversationLog();string stream=Guid.NewGuid().ToString("N");BeginAgentReply(old,stream);
            store.SaveAsync("pet",old.Messages).GetAwaiter().GetResult();
            var recording=store.CreateAgentTurnRecording("pet");recording.BeginSegment(stream,"task");
            recording.Tool(new AgentToolEvent{Id="moved",Name="read_file",Status="running",Summary="pending action"});
            recording.FinishSegment("","complete");recording.Close();store.FlushAsync().GetAwaiter().GetResult();
            string events=Directory.GetFiles(store.DirectoryPath,"turn.events",SearchOption.AllDirectories).Single();
            var replacement=new ConversationLog();replacement.Commit("new input","new response");
            store.SaveAsync("pet",replacement.Messages).GetAwaiter().GetResult();
            Check(!File.Exists(events)&&store.Load("pet").Last().Content=="new response","a superseded terminal stream pinned the new checkpoint");
        }
        static void AgentTurnClear()
        {
            string path=Temp(),stream=Guid.NewGuid().ToString("N");
            var store=new HistoryStore(path);
            var log=new ConversationLog();BeginAgentReply(log,stream);
            store.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();
            var recording=store.CreateAgentTurnRecording("pet");
            recording.BeginSegment(stream,"task");
            recording.Text("private fragment");
            recording.Close();
            store.FlushAsync().GetAwaiter().GetResult();
            string events=Directory.GetFiles(path,"turn.events",SearchOption.AllDirectories).Single();
            Check(!File.ReadAllText(events).Contains("private fragment"),"the Agent reply was stored in plaintext");
            File.AppendAllText(events,"bad frame\n");
            var reopened=new HistoryStore(path);
            Check(reopened.Load("pet").Last().Status=="interrupted" && !String.IsNullOrEmpty(reopened.Warning) && File.Exists(events),"corrupt events hid readable history or were discarded during replay");
            reopened.ClearAsync("pet").GetAwaiter().GetResult();
            Check(!File.Exists(events) && reopened.Load("pet").Count==0 && String.IsNullOrEmpty(reopened.Warning),"explicit clear retained Agent reply data");
        }
        static async Task AgentTurnSessionAsync()
        {
            var store=new HistoryStore(Temp());
            var settings=Configured();settings.AgentEnabled=true;settings.AgentWorkspace=Temp();
            var api=new WorkStreamApi();
            using(var session=new ConversationSession(api,new TestSpeech(),()=>null,store,()=>settings.SaveChatHistory))
            {
                session.SelectPersona("pet");
                var work=session.SendTextAsync("agent fixture",settings,"pet");
                try
                {
                    Check(await Task.WhenAny(api.Started.Task,work,Task.Delay(10000))==api.Started.Task,"work request did not reach Agent streaming");
                    api.Delta("first fragment");
                    api.Tool(new AgentToolEvent{Id="call",Name="read_file",Status="complete",Summary="public tool result"});
                    await store.FlushAsync();
                    var live=session.History.Messages.Last();
                    var reopened=new HistoryStore(store.DirectoryPath).Load("pet").Last();
                    Check(live.Status=="pending"&&live.Id==reopened.Id&&reopened.Content=="first fragment"&&reopened.ToolEvents.Single().Summary=="public tool result","live progress was lost when reading from a fresh history store");

                    settings.SaveChatHistory=false;
                    api.Delta(" after opt out");
                    await store.FlushAsync();
                    reopened=new HistoryStore(store.DirectoryPath).Load("pet").Last();
                    Check(reopened.Content=="first fragment"&&!reopened.Content.Contains("opt out"),"history opt out still wrote Agent output");
                    api.Finish.TrySetResult("first fragment after opt out");
                    await work;
                    Check(session.History.Messages.Last().Content=="first fragment after opt out","history opt out changed the live response");
                }
                finally{api.Finish.TrySetResult("fixture finished");try{work.GetAwaiter().GetResult();}catch(OperationCanceledException){}}
            }
        }
        static PetSettings Configured(){return new PetSettings {AgentEnabled=false,ReadReplies=false,Text=new ConnectionSettings {BaseUrl="http://127.0.0.1:9001/v1",Model="chat"}};}
        static async Task CancelRetainsAsync()
        {
            using(var session=new ConversationSession(new TestApi {Delay=1000},new TestSpeech(),delegate{return null;})) {
                session.SelectPersona("e-bao");var task=session.SendTextAsync("cancel",Configured(),"E包");await Task.Delay(20);await session.StopAsync();try{await task;}catch(OperationCanceledException){}
                Check(session.History.Messages.Count==2 && session.History.Messages.Last().Status=="cancelled","cancelled round was not retained");Check(session.History.ForRequest("","next").Count==1,"cancelled round entered model context");
            }
        }
        static async Task StreamingCancelAsync()
        {
            string directory=Temp();var store=new HistoryStore(directory);var api=new StreamingApi();using(var session=new ConversationSession(api,new TestSpeech(),delegate{return null;},store)) {
                session.SelectPersona("e-bao");string userId=null,replyId=null;int additions=0,updates=0;session.MessageAdded+=m=>{additions++;if(m.Role=="user")userId=m.Id;else replyId=m.Id;};session.MessageUpdated+=m=>{Check(m.Id==replyId,"stream changed message id");updates++;};
                var task=session.SendTextAsync("stream",Configured(),"E包");await api.Started.Task;var initial=session.History.Messages.Last();api.Delta("你好");api.Delta("世界");
                Check(additions==2 && updates==2 && initial.Content=="" && session.History.Messages.Last().Content=="你好世界","stream snapshot or visible delta failed");await session.FlushHistoryAsync();Check(store.Load("e-bao").Last().Content=="你好世界","partial flush lost latest text");
                await session.StopAsync();try{await task;}catch(OperationCanceledException){}
                var loaded=new HistoryStore(directory).Load("e-bao");Check(loaded[0].Id==userId && loaded[1].Id==replyId && loaded[1].Status=="cancelled" && loaded[1].Content=="你好世界","cancelled restart lost partial or ids");
            }
        }
        static async Task StreamingErrorAsync()
        {
            var api=new StreamingApi();using(var session=new ConversationSession(api,new TestSpeech(),delegate{return null;})) {
                session.SelectPersona("e-bao");var task=session.SendTextAsync("fail",Configured(),"E包");await api.Started.Task;api.Delta("部分回复");api.Finish.TrySetException(new ApiException("服务中断"));bool failed=false;try{await task;}catch(ApiException){failed=true;}
                Check(failed && !session.IsBusy && session.History.Messages.Last().Status=="error" && session.History.Messages.Last().Content=="部分回复" && session.History.ForRequest("","next").Count==1,"stream failure lost or reused partial");
            }
        }
        static async Task DisabledSaveAsync()
        {
            string directory=Temp();var store=new HistoryStore(directory);var log=new ConversationLog();log.Commit("saved","answer");await store.SaveAsync("e-bao",log.Messages);string file=Directory.GetFiles(directory).Single();byte[] before=File.ReadAllBytes(file);
            using(var session=new ConversationSession(new TestApi(),new TestSpeech(),delegate{return null;},store,delegate{return false;})) {
                int resets=0;session.HistoryChanged+=delegate{resets++;};session.SelectPersona("e-bao");await session.SendTextAsync("unsaved",Configured(),"E包");await session.FlushHistoryAsync();Check(File.ReadAllBytes(file).SequenceEqual(before) && session.History.Messages.Count==4,"disabled save changed disk or lost in-memory history");Check(session.ExportHistory().Contains("unsaved"),"export lost unsaved history");session.Clear();await session.FlushHistoryAsync();Check(!File.Exists(file) && session.History.Messages.Count==0 && resets==2,"disabled clear did not delete disk or notify windows");
            }
        }
        static async Task LateDisposeAsync()
        {
            var store=new HistoryStore(Temp());var api=new StreamingApi {IgnoreCancel=true};var session=new ConversationSession(api,new TestSpeech(),delegate{return null;},store);session.SelectPersona("e-bao");var task=session.SendTextAsync("late",Configured(),"E包");await api.Started.Task;api.Delta("已收到");int events=0;session.MessageAdded+=delegate{events++;};session.MessageUpdated+=delegate{events++;};session.Dispose();api.Delta("迟到");api.Finish.TrySetResult("迟到回答");try{await task;}catch(OperationCanceledException){}await session.FlushHistoryAsync();
            Check(events==0 && session.History.Messages.Last().Content=="已收到" && session.History.Messages.Last().Status=="interrupted" && store.Load("e-bao").Last().Content=="已收到","disposed session published or committed late response");
        }
        static async Task QuietAsync()
        {
            var api=new StreamingApi();var speech=new TestSpeech();var cfg=Configured();cfg.ReadReplies=true;using(var session=new ConversationSession(api,speech,delegate{return null;})) {
                var task=session.SendTextAsync("quiet",cfg,"E包");await api.Started.Task;cfg.QuietMode=true;api.Finish.TrySetResult("完成");await task;Check(speech.Calls==0 && session.History.Messages.Last().Status=="complete","DND failed to suppress automatic speech");
            }
        }
        static async Task SourcesAsync()
        {
            var store=new HistoryStore(Temp());using(var session=new ConversationSession(new TestApi(),new TestSpeech(),delegate{return null;},store)) {
                session.SelectPersona("e-bao");var cfg=Configured();cfg.Transcription=new ConnectionSettings {BaseUrl="http://127.0.0.1:9001/v1",Model="asr"};await session.SendVoiceAsync(WaveData.EncodePcm(new byte[16000]),cfg,"E包");Check(session.History.Messages.All(m=>m.Source=="asr"),"ASR source lost");string liveReplyId=null;session.MessageAdded+=m=>{if(m.Role=="assistant")liveReplyId=m.Id;};session.AddLiveTurn("e-bao","语音","现场回答");Check(session.History.Messages.Last().Id==liveReplyId && session.History.Messages.Last().Source=="live","Live emitted unrelated message ids");session.AddLiveTurn("marfu","other","different");session.SelectPersona("marfu");Check(session.History.Messages.Count==2 && session.History.Messages.Last().Content=="different","Live crossed persona histories");await session.FlushHistoryAsync();
            }
        }
        static async Task CheckpointAsync()
        {
            var store=new HistoryStore(Temp());var api=new StreamingApi();using(var session=new ConversationSession(api,new TestSpeech(),delegate{return null;},store)) {
                session.SelectPersona("pet");var task=session.SendTextAsync("checkpoint",Configured(),"伙伴");await api.Started.Task;api.Delta("first");await store.FlushAsync();Check(store.Load("pet").Last().Content=="","partial checkpoint saved too often");await Task.Delay(1100);api.Delta("second");await store.FlushAsync();Check(store.Load("pet").Last().Content=="firstsecond","elapsed checkpoint did not preserve partial");api.Delta("third");await store.FlushAsync();Check(store.Load("pet").Last().Content=="firstsecond","burst deltas bypassed one-second throttle");api.Finish.TrySetResult("firstsecondthird");await task;await session.FlushHistoryAsync();Check(store.Load("pet").Last().Content=="firstsecondthird" && store.Load("pet").Last().Status=="complete","final flush omitted latest delta");
            }
        }
        static async Task PersonaSnapshotAsync()
        {
            var api=new StreamingApi();var cfg=Configured();cfg.SystemPrompt="original persona";using(var session=new ConversationSession(api,new TestSpeech(),delegate{return null;})) {
                session.SelectPersona("e-bao");var task=session.SendTextAsync("text",cfg,"E包");await api.Started.Task;cfg.SystemPrompt="changed persona";cfg.SkinId="marfu";bool blocked=false;try{session.SelectPersona("marfu");}catch(InvalidOperationException){blocked=true;}api.Finish.TrySetResult("old answer");await task;Check(blocked && api.RequestMessages[0].Content.StartsWith("original persona\n") && !api.RequestMessages[0].Content.Contains("changed persona"),"active request lost snapshot or allowed persona switch");session.SelectPersona("marfu");Check(session.History.Messages.Count==0,"answer mutated later persona");session.SelectPersona("e-bao");Check(session.History.Messages.Last().Content=="old answer","answer lost original persona");
            }
        }
        static async Task IncompleteLiveAsync()
        {
            var store=new HistoryStore(Temp());using(var session=new ConversationSession(new TestApi(),new TestSpeech(),()=>null,store)) {
                session.SelectPersona("e-bao");session.AddLiveTurn("e-bao","完整输入","完整回复");session.AddLiveTurn("e-bao","打断输入","半截回复","interrupted");session.AddLiveTurn("e-bao","停止输入","部分字幕","cancelled");session.AddLiveTurn("e-bao","只有输入","","cancelled");await session.FlushHistoryAsync();
                Check(session.History.ForRequest("","next").Count==3 && session.History.Messages.Count==8,"incomplete Live turns entered successful context or disappeared");
                var restored=new ConversationLog();restored.Replace(store.Load("e-bao"));Check(restored.Messages[3].Status=="interrupted" && restored.Messages[5].Status=="cancelled" && restored.Messages[7].Status=="cancelled" && restored.ForRequest("","next").Count==3,"restart promoted incomplete Live history into successful context");
            }
        }
        class TestApi:IConversationApi
        {
            public int Delay;public bool IgnoreCancel;
            public async Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> m,CancellationToken token){if(Delay>0)await Task.Delay(Delay,IgnoreCancel?CancellationToken.None:token);return "回答";}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] wav,CancellationToken token){return Task.FromResult("识别");}
            public Task<byte[]> SpeechAsync(ConnectionSettings c,string voice,string text,CancellationToken token){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class StreamingApi:TestApi,IStreamingConversationApi
        {
            public readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>();public readonly TaskCompletionSource<string> Finish=new TaskCompletionSource<string>();public Action<string> Delta;public IList<ChatMessage> RequestMessages;
            public async Task<string> ChatStreamAsync(ConnectionSettings cfg,IList<ChatMessage> messages,Action<string> delta,CancellationToken token)
            { RequestMessages=messages;Delta=delta;Started.TrySetResult(true);if(!IgnoreCancel){var cancelled=Task.Delay(Timeout.Infinite,token);await Task.WhenAny(Finish.Task,cancelled);token.ThrowIfCancellationRequested();}return await Finish.Task; }
        }
        sealed class WorkStreamApi:TestApi,IAgentConversationApi
        {
            public readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<string> Finish=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Action<string> Delta;
            public Action<AgentToolEvent> Tool;
            public Task<string> ChatAgentAsync(ConnectionSettings cfg,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken token)
            {
                Delta=delta;Tool=tool;
                Started.TrySetResult(true);
                return Finish.Task;
            }
        }
        sealed class TestSpeech:IReplySpeech
        {
            public int Calls;public Task SpeakAsync(string text,PetSettings cfg,CancellationToken token){Calls++;return Task.FromResult(0);}public void SetVolume(int value){}public void Stop(){}public void Dispose(){}
        }
        static void Run(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    }
}
