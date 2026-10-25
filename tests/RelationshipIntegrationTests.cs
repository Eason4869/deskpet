using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class RelationshipIntegrationTests
    {
        const string A="legacy-skin:skin-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", B="legacy-skin:skin-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        static int failed,passed;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        static PetSettings Settings(){return new PetSettings{AgentEnabled=false,ReadReplies=false,SystemPrompt="Keep my chosen persona.",Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9001/v1",Model="fake"},Transcription=new ConnectionSettings{BaseUrl="http://127.0.0.1:9001/v1",Model="fake"}};}
        static void Run(string name,Func<Task> test){try{test().GetAwaiter().GetResult();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
        public static int Main()
        {
            Run("bounded real pending writes and failed retirement recovery",delegate{RelationshipAtomicWriteChecks.Pending();RelationshipAtomicWriteChecks.PostLedgerRetirement();return Task.FromResult(0);});
            Run("old completed history and task startup never reconstruct relationship rewards",async delegate{
                using(var f=new RelationshipFixture()){
                    var history=new HistoryStore(Path.Combine(f.Root,"history"));var old=new ConversationLog();old.Commit("old completed user","old completed reply");await history.SaveAsync("pet",old.Messages);
                    var cfg=Settings();cfg.AgentWorkspace=f.Root;cfg.AgentEnabled=true;
                    await new AgentTaskStore(Path.Combine(history.DirectoryPath,"agent-tasks")).SaveAsync(new AgentTaskState{Workspace=f.Root,PersonaId="pet",Status="complete",Goal="work completed before relationship data"});
                    Check(!Directory.Exists(Path.Combine(f.Root,"relationships")),"old-data fixture unexpectedly includes relationship storage");
                    for(int startup=0;startup<2;startup++){
                        var gate=new Dafeiyu.Maintenance.DataMaintenanceGate();using(var runtime=new RelationshipRuntime(f.Root,maintenance:gate))using(var session=new ConversationSession(new Api(),new Speech(),()=>null,new HistoryStore(history.DirectoryPath),maintenance:gate)){
                            session.ConfigureRelationships(runtime,()=>A);session.SelectPersona("pet");session.LoadAgentTask(cfg);await session.FlushHistoryAsync();await runtime.FlushAsync();
                            Check(session.History.Messages.Count==2&&session.CurrentAgentTask!=null&&session.CurrentAgentTask.Status=="complete","actual old completed history and task were not loaded");
                            Check(runtime.Service.GetSnapshot(A).State==null&&runtime.PendingCount==0,"startup or restart reconstructed old completion points");
                            if(startup==1){await runtime.EnqueueVisit(A);var state=runtime.Service.GetSnapshot(A).State;Check(state.Score==2&&state.Receipts.Count==1&&state.Receipts[0].EventType==RelationshipEventType.Visit,"only the new actual encounter earns its own receipt");}
                        }
                    }
                }
            });
            Run("text, ASR and retry use durable user ownership and one receipt",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root))using(var s=new ConversationSession(new Api{Delay=40},new Speech(),()=>null)){
                    string selected=A;s.ConfigureRelationships(runtime,()=>selected);Check(runtime.Service.GetSnapshot(A).State==null,"background visit");
                    var started=s.SendTextAsync("hello",Settings(),"pet");selected=B;await started;var reply=s.History.Messages.Last();await s.RegenerateAsync(reply.Id,Settings(),"pet");
                    Check(runtime.Service.GetSnapshot(A).State.Score==2 && runtime.Service.GetSnapshot(A).State.Receipts.Count==1,"regeneration changed owner or duplicated");
                    Check(runtime.Service.GetSnapshot(B).State==null,"switched owner awarded");await s.SendVoiceAsync(WaveData.EncodePcm(new byte[16000]),Settings(),"pet");Check(runtime.Service.GetSnapshot(B).State.Score==2,"ASR final missing");
                }
            });
            Run("failed and cancelled turn can succeed once, preserving owner",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    var api=new Api{Fail=true};using(var s=new ConversationSession(api,new Speech(),()=>null)){
                        string selected=A;s.ConfigureRelationships(runtime,()=>selected);try{await s.SendTextAsync("fail",Settings(),"pet");}catch(ApiException){}Check(runtime.Service.GetSnapshot(A).State==null,"failed award");
                        selected=B;api.Fail=false;await s.RegenerateAsync(s.History.Messages.Last().Id,Settings(),"pet");await s.RegenerateAsync(s.History.Messages.Last().Id,Settings(),"pet");var retried=StateAfterExplicitRetry(runtime,A);Check(retried!=null && retried.Score==2,"failed retry ownership; pending="+runtime.PendingCount+" warning="+runtime.Warning+" outbox="+File.ReadAllText(Path.Combine(f.Root,"relationships","pending.json")));
                        api.Delay=1000;var pending=s.SendTextAsync("cancel",Settings(),"pet");await Task.Delay(15);await s.StopAsync();try{await pending;}catch(OperationCanceledException){}Check(runtime.Service.GetSnapshot(B).State==null,"cancel award");api.Delay=0;await s.RegenerateAsync(s.History.Messages.Last().Id,Settings(),"pet");var cancelled=StateAfterExplicitRetry(runtime,B);Check(cancelled!=null && cancelled.Score==2,"cancel retry missing; pending="+runtime.PendingCount+" warning="+runtime.Warning+" outbox="+File.ReadAllText(Path.Combine(f.Root,"relationships","pending.json")));
                    }
                }
            });
            Run("failed state I/O preserves chat success and ordered pending events",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    Directory.CreateDirectory(Path.Combine(f.Root,"relationships","state.json"));
                    using(var s=new ConversationSession(new Api(),new Speech(),()=>null)){
                        s.ConfigureRelationships(runtime,()=>A);await s.SendTextAsync("complete despite I/O",Settings(),"pet");Check(s.History.Messages.Last().Status=="complete" && runtime.PendingCount==1 && runtime.Warning.Length>0,"chat failure or missing retry");
                        runtime.Record(A,RelationshipEventType.CompletedConversation,"later",RelationshipTime.Current().AddSeconds(1));Check(runtime.PendingCount==2,"later event escaped queue");
                        Directory.Delete(Path.Combine(f.Root,"relationships","state.json"));runtime.RetryPending();var state=runtime.Service.GetSnapshot(A).State;Check(runtime.PendingCount==0 && state.Score==4 && state.Receipts.Count==2 && state.Receipts[0].Utc<state.Receipts[1].Utc,"retry ordering or loss");runtime.RetryPending();Check(runtime.Service.GetSnapshot(A).State.Score==4,"retry duplicate");
                    }
                }
            });
            Run("real Live provenance, work exclusivity, and root task copy/store",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root))using(var s=new ConversationSession(new Api(),new Speech(),()=>null)){
                    s.ConfigureRelationships(runtime,()=>A);s.AddLiveTurn("","fixture","reply");s.AddLiveTurn("","user","reply","interrupted","live-1",A,true);Check(runtime.Service.GetSnapshot(A).State==null,"fixture/interruption awarded");
                    s.AddLiveTurn("","user","reply","complete","live-2",A,true);s.AddLiveTurn("","user","reply","complete","live-2",A,true);s.AddLiveTurn("","user2","reply2","complete","live-3",A,true);await runtime.FlushAsync();Check(runtime.Service.GetSnapshot(A).State.Score==4,"live identity");
                    var task=new AgentTaskState{Workspace=f.Root,PersonaId="work",RelationshipCompanionId=B,RelationshipEventId="root-1"};var store=new AgentTaskStore(Path.Combine(f.Root,"tasks"));await store.SaveAsync(task.Copy());var loaded=store.Load("work",f.Root);Check(loaded.RelationshipCompanionId==B && loaded.RelationshipEventId=="root-1","task metadata lost");
                    runtime.Record(B,RelationshipEventType.SuccessfulWorkTask,loaded.RelationshipEventId,RelationshipTime.Current());runtime.Record(B,RelationshipEventType.CompletedConversation,loaded.RelationshipEventId,RelationshipTime.Current());Check(runtime.Service.GetSnapshot(B).State.Score==4 && runtime.Service.GetSnapshot(B).State.Receipts.Count==1,"work chat double credit");
                }
            });
            Run("explicit interaction and Visit require successful user presentation",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    runtime.RecordUserInteraction(A,false,false);runtime.RecordUserInteraction(A,true,false);runtime.RecordVisit(A,false);Check(runtime.Service.GetSnapshot(A).State==null,"background/action rejection rewarded");runtime.RecordVisit(A,true);runtime.RecordUserInteraction(A,true,true);Check(runtime.Service.GetSnapshot(A).State.Score==3,"explicit starts missing");await Task.FromResult(0);
                }
            });
            Run("optional pack content validates and language fallback is safe",async delegate{
                var pack=new RelationshipPackContent{FallbackLocale="en_US",Stages=new Dictionary<string,RelationshipStageContent>{{"familiar",new RelationshipStageContent{Welcome=new Dictionary<string,string>{{"en_US","Welcome back."},{"ja_JP","おかえり。"}},Interaction=new Dictionary<string,string>{{"en_US","Hello!"}},Action="waving"}}}};
                Check(RelationshipContent.ParseJson("\ufeff{\"stages\":{}}").Stages.Count==0,"BOM content rejected");RelationshipContent.Validate(null,new[]{"waving"});RelationshipContent.Validate(pack,new[]{"waving"});Check(RelationshipContent.Welcome("familiar",pack,"ja_JP")=="おかえり。" && RelationshipContent.Welcome("familiar",pack,"fr_FR")=="Welcome back.","locale fallback");pack.Stages["familiar"].Action="../../run.exe";bool denied=false;try{RelationshipContent.Validate(pack,new[]{"waving"});}catch(InvalidDataException){denied=true;}Check(denied,"unsafe action accepted");await Task.FromResult(0);
            });
            Run("stage context keeps custom persona and respects opt-out",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    var api=new Api();using(var s=new ConversationSession(api,new Speech(),()=>null)){s.ConfigureRelationships(runtime,()=>A);await s.SendTextAsync("hello",Settings(),"pet");Check(api.Messages[0].Content.StartsWith("Keep my chosen persona.") && api.Messages[0].Content.Contains(RelationshipContent.StageContext("first_meeting")),"stage/persona missing");runtime.Service.UpdateSettings(true,true,false);await s.SendTextAsync("again",Settings(),"pet");Check(!api.Messages[0].Content.Contains(RelationshipContent.StageContext("first_meeting")),"opt-out ignored");}
                }
            });
            Run("queued submission survives restart and a partner switch",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    string selected=A;var api=new Api{Fail=true};string queuePath=Path.Combine(f.Root,"queue.json");
                    using(var session=new ConversationSession(api,new Speech(),()=>null)){
                        session.ConfigureRelationships(runtime,()=>selected);using(var queue=new ConversationQueue(session,Settings,()=>"pet",queuePath)){queue.Enqueue("persist me");await queue.FlushAsync();Check(queue.Items.Count==1 && queue.Items[0].RelationshipCompanionId==A,"submission owner missing");}
                    }
                    selected=B;using(var session=new ConversationSession(new Api(),new Speech(),()=>null)){
                        session.ConfigureRelationships(runtime,()=>selected);using(var queue=new ConversationQueue(session,Settings,()=>"pet",queuePath)){Check(queue.IsPaused,"restart must wait for Resume");queue.Resume();await queue.FlushAsync();Check(runtime.Service.GetSnapshot(A).State.Score==2 && runtime.Service.GetSnapshot(B).State==null,"queued submission moved to new partner");}
                    }
                }
            });
            Run("pending outbox replays original timestamps after application restart",async delegate{
                using(var f=new RelationshipFixture()){
                    Directory.CreateDirectory(Path.Combine(f.Root,"relationships","state.json"));var time=RelationshipFixture.Time("2026-10-24T10:00:00Z");
                    using(var runtime=new RelationshipRuntime(f.Root)){runtime.Record(A,RelationshipEventType.CompletedConversation,"failed-A",time);runtime.Record(A,RelationshipEventType.CompletedConversation,"later-B",time.AddSeconds(5));Check(runtime.PendingCount==2,"pending missing");}
                    Directory.Delete(Path.Combine(f.Root,"relationships","state.json"));using(var runtime=new RelationshipRuntime(f.Root)){Check(runtime.PendingCount==2 && runtime.Service.GetSnapshot(A).State==null,"restart invented growth before retry");runtime.RetryPending();var state=runtime.Service.GetSnapshot(A).State;Check(state.Score==4 && state.Receipts[0].Utc==time.UtcNow && state.Receipts[1].Utc==time.AddSeconds(5).UtcNow,"restart timestamp order");}
                    await Task.FromResult(0);
                }
            });
            Run("successful regeneration stays deduplicated across persisted history restart",async delegate{
                using(var f=new RelationshipFixture()){
                    string reply;var history=new HistoryStore(Path.Combine(f.Root,"history"));using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null,history)){
                        session.SelectPersona("pet");session.ConfigureRelationships(runtime,()=>A);await session.SendTextAsync("hello",Settings(),"pet");reply=session.History.Messages.Last().Id;await session.FlushHistoryAsync();
                    }
                    using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null,new HistoryStore(Path.Combine(f.Root,"history")))){
                        session.ConfigureRelationships(runtime,()=>B);session.SelectPersona("pet");await session.RegenerateAsync(reply,Settings(),"pet");Check(runtime.Service.GetSnapshot(A).State.Score==2 && runtime.Service.GetSnapshot(B).State==null,"restart regeneration duplicate/owner");await session.FlushHistoryAsync();
                    }
                }
            });
            Run("work session completes one root task and its resume cannot award chat",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null,new HistoryStore(Path.Combine(f.Root,"history")))){
                    string selected=A;session.ConfigureRelationships(runtime,()=>selected);var cfg=Settings();cfg.AgentEnabled=true;cfg.AgentWorkspace=f.Root;await session.SendTextAsync("work",cfg,"pet");Check(runtime.Service.GetSnapshot(A).State.WorkPoints==4 && runtime.Service.GetSnapshot(A).State.ConversationPoints==0,"work emitted chat reward");
                    if(Environment.OSVersion.Platform==PlatformID.Win32NT){var root=session.CurrentAgentTask;Check(root!=null && root.RelationshipCompanionId==A,"root task binding missing");selected=B;await session.SendTextAsync("resume",cfg,"pet",resumeTask:root);Check(runtime.Service.GetSnapshot(A).State.Score==4 && runtime.Service.GetSnapshot(B).State==null,"root resume changed award");}
                    await session.FlushHistoryAsync();
                }
            });
            Run("manifest extension rejects malformed types and unsafe content",async delegate{
                var content=RelationshipContent.ParseJson("{\"fallback_locale\":\"en_US\",\"stages\":{\"familiar\":{\"welcome\":{\"en_US\":\"Hi\"},\"action\":\"waving\"}}}");RelationshipContent.Validate(content,new[]{"waving"});
                foreach(string json in new[]{"{\"stages\":{\"familiar\":{\"welcome\":{\"en_US\":123}}}}","{\"stages\":[]}","{\"stages\":{\"invented_stage\":{}}}","{\"stages\":{\"familiar\":{\"companion_id\":\"official:e-bao\"}}}"}){
                    bool rejected=false;try{RelationshipContent.Validate(RelationshipContent.ParseJson(json),new[]{"waving"});}catch(InvalidDataException){rejected=true;}Check(rejected,"malformed extension accepted: "+json);
                }await Task.FromResult(0);
            });
            Run("committed notification does not retain the outbox lock",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    bool unlocked=false;runtime.Committed+=(id,result)=>{var read=Task.Run(()=>{runtime.RetryPending();return runtime.PendingCount;});unlocked=read.Wait(500);};runtime.RecordVisit(A);Check(unlocked,"notification retained outbox lock");await Task.FromResult(0);
                }
            });
            Run("Live transport captures distinct logical user turns and ignores interruption/audio chunks",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null)){
                    string selected=A;var socket=new Socket();var mic=new Mic();var output=new Output();session.ConfigureRelationships(runtime,()=>selected);
                    using(var live=new GeminiLiveSession(()=>socket,()=>mic,()=>output)){
                        live.RelationshipCompanion=()=>selected;var turns=new List<RelationshipLiveTurn>();live.LogicalTurnCommitted+=turn=>{session.AddLiveTurn("",turn.User,turn.Reply,turn.Status,turn.LogicalTurnId,turn.CompanionId,true);lock(turns)turns.Add(turn);};
                        var cfg=Settings();cfg.Live.ApiKey="fixture-key";socket.Push("{\"setupComplete\":{}}");await live.StartAsync(cfg,"persona");
                        socket.Push("{\"serverContent\":{\"inputTranscription\":{\"text\":\"first\"}}}");await Wait(()=>live.Status.Length>0 && socket.Received>=2);selected=B;
                        socket.Push("{\"serverContent\":{\"outputTranscription\":{\"text\":\"reply\"},\"turnComplete\":true}}");await Wait(()=>{lock(turns)return turns.Count==1;});
                        socket.Push("{\"serverContent\":{\"inputTranscription\":{\"text\":\"interrupted\"},\"outputTranscription\":{\"text\":\"partial\"},\"interrupted\":true}}");
                        socket.Push("{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AQIDBA==\"}}]}}}");
                        socket.Push("{\"serverContent\":{\"inputTranscription\":{\"text\":\"second\"},\"outputTranscription\":{\"text\":\"reply2\"},\"turnComplete\":true}}");await Wait(()=>{lock(turns)return turns.Count==3;});await live.StopAsync();await runtime.FlushAsync();
                        Check(turns[0].CompanionId==A && turns[2].CompanionId==B && turns[0].LogicalTurnId!=turns[2].LogicalTurnId,"live turn capture/id");Check(runtime.Service.GetSnapshot(A).State.Score==2 && runtime.Service.GetSnapshot(B).State.Score==2,"live incomplete/audio duplicate credit");
                    }
                }
            });
            Run("title and hidden prediction requests do not add growth or stage context",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    var api=new Api();using(var session=new ConversationSession(api,new Speech(),()=>null)){
                        session.ConfigureRelationships(runtime,()=>A);await session.SendTextAsync("hello",Settings(),"pet");var context=RelationshipContent.StageContext("first_meeting");await session.GenerateTitleAsync(Settings(),CancellationToken.None);Check(!api.Messages.Any(m=>m.Content.Contains(context)),"title received stage context");await session.RequestPredictionsAsync(Settings());Check(runtime.Service.GetSnapshot(A).State.Score==2 && runtime.Service.GetSnapshot(A).State.Receipts.Count==1,"hidden requests awarded");
                    }
                }
            });
            Run("interrupted real Live history can retry once with captured attribution",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null)){
                    session.ConfigureRelationships(runtime,()=>B);session.AddLiveTurn("","user","partial","interrupted","live-retry",A,true);var reply=session.History.Messages.Last().Id;await session.RegenerateAsync(reply,Settings(),"pet");await session.RegenerateAsync(reply,Settings(),"pet");Check(runtime.Service.GetSnapshot(A).State!=null && runtime.Service.GetSnapshot(A).State.Score==2 && runtime.Service.GetSnapshot(B).State==null,"Live retry lost logical binding");
                }
            });
            Run("successful regeneration with real blocked I/O retains original owner until explicit retry",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    var api=new Api{Fail=true};string selected=A;using(var session=new ConversationSession(api,new Speech(),()=>null)){
                        session.ConfigureRelationships(runtime,()=>selected);try{await session.SendTextAsync("first fails",Settings(),"pet");}catch(ApiException){}
                        var failedReply=session.History.Messages.Last();var token=failedReply.RelationshipEventId;Check(!String.IsNullOrEmpty(token),"original logical input missing");
                        Directory.CreateDirectory(Path.Combine(f.Root,"relationships","state.json"));selected=B;api.Fail=false;await session.RegenerateAsync(failedReply.Id,Settings(),"pet");await session.RegenerateAsync(failedReply.Id,Settings(),"pet");
                        Check(session.History.Messages.Last().Status=="complete" && runtime.Service.GetSnapshot(A).State==null && runtime.PendingCount==1 && runtime.Warning.Length>0,"pending I/O mistaken for failed chat or committed state");
                        string queued=File.ReadAllText(Path.Combine(f.Root,"relationships","pending.json"));Check(queued.Contains(A)&&queued.Contains(token)&&!queued.Contains(B),"pending retry lost original attribution");
                        Directory.Delete(Path.Combine(f.Root,"relationships","state.json"));runtime.RetryPending();var settled=runtime.Service.GetSnapshot(A).State;Check(runtime.PendingCount==0 && settled.Score==2 && settled.Receipts.Count==1 && settled.Receipts[0].EventId==token && runtime.Service.GetSnapshot(B).State==null,"explicit retry committed incorrect owner/token/score");
                    }
                }
            });
            Run("async host enqueue stays responsive under held lease and shutdown preserves original order",async delegate{
                using(var f=new RelationshipFixture()){
                    var runtime=new RelationshipRuntime(f.Root);Directory.CreateDirectory(Path.Combine(f.Root,"relationships"));var time=RelationshipFixture.Time("2026-10-24T10:00:00Z");Task first,second,shutdown;
                    using(var lease=new FileStream(Path.Combine(f.Root,"relationships",".transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                        var clock=System.Diagnostics.Stopwatch.StartNew();first=runtime.Enqueue(A,RelationshipEventType.CompletedConversation,"async-A",time);Check(clock.ElapsedMilliseconds<250,"host enqueue blocked on disk");
                        await Wait(()=>File.Exists(Path.Combine(f.Root,"relationships","pending.json")));clock.Restart();var warning=runtime.Warning;var count=runtime.PendingCount;Check(clock.ElapsedMilliseconds<250 && !first.IsCompleted,"UI getters blocked behind relationship I/O");
                        second=runtime.Enqueue(A,RelationshipEventType.CompletedConversation,"async-B",time.AddSeconds(2));shutdown=runtime.ShutdownAsync();Check(!shutdown.IsCompleted,"shutdown failed to join accepted writes");
                    }
                    await Task.WhenAll(first,second,shutdown);var state=f.Service.GetSnapshot(A).State;Check(state.Score==4 && state.Receipts.Count==2 && state.Receipts[0].EventId=="async-A" && state.Receipts[0].Utc==time.UtcNow && state.Receipts[1].EventId=="async-B" && state.Receipts[1].Utc==time.AddSeconds(2).UtcNow,"async queue reordered or lost original events");
                    await runtime.Enqueue(A,RelationshipEventType.CompletedConversation,"after-shutdown",time.AddSeconds(3));Check(f.Service.GetSnapshot(A).State.Score==4,"shutdown accepted late event");runtime.Dispose();
                }
            });
            foreach(string ending in new[]{"direct","error","cancelled"})Run("disabled-growth prior success survives history restart after "+ending,async delegate{
                using(var f=new RelationshipFixture()){
                    string reply;var api=new Api();using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(api,new Speech(),()=>null,new HistoryStore(Path.Combine(f.Root,"history")))){
                        session.SelectPersona("pet");session.ConfigureRelationships(runtime,()=>A);Check(runtime.Service.UpdateSettings(false,true,true).Success,"disable failed");await session.SendTextAsync("complete while disabled",Settings(),"pet");reply=session.History.Messages.Last().Id;CheckNoGrowth(runtime,A,"initial disabled success");Check(runtime.Service.UpdateSettings(true,true,true).Success,"enable failed");
                        if(ending=="direct")await session.RegenerateAsync(reply,Settings(),"pet");
                        else if(ending=="error"){api.Fail=true;try{await session.RegenerateAsync(reply,Settings(),"pet");}catch(ApiException){}Check(session.History.Messages.Last().Status=="error","failed regeneration fixture");}
                        else {api.Delay=1000;int calls=api.Calls;var regeneration=session.RegenerateAsync(reply,Settings(),"pet");await Wait(()=>api.Calls>calls);await session.StopAsync();try{await regeneration;}catch(OperationCanceledException){}Check(session.History.Messages.Last().Status=="cancelled","cancelled regeneration fixture");}
                        await session.FlushHistoryAsync();
                    }
                    using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null,new HistoryStore(Path.Combine(f.Root,"history")))){
                        session.SelectPersona("pet");session.ConfigureRelationships(runtime,()=>B);await session.RegenerateAsync(reply,Settings(),"pet");CheckNoGrowth(runtime,A,ending+" previously successful logical input reopened after restart");CheckNoGrowth(runtime,B,"regeneration switched companion");Check(session.History.Messages.Where(m=>m.Id==reply || m.Role=="user").All(m=>m.RelationshipSucceeded),"persisted user/reply success decision lost");await session.FlushHistoryAsync();
                    }
                }
            });
            foreach(string ending in new[]{"direct","error","cancelled"})Run("disabled-growth completed root survives copy restart and resume after "+ending,async delegate{
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.WriteLine("INFO local work task fixture requires Windows");return;}
                using(var f=new RelationshipFixture()){
                    var api=new Api();var cfg=Settings();cfg.AgentEnabled=true;cfg.AgentWorkspace=f.Root;AgentTaskState root;var taskStore=new AgentTaskStore(Path.Combine(f.Root,"saved-root"));
                    using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(api,new Speech(),()=>null,new HistoryStore(Path.Combine(f.Root,"history")))){
                        session.SelectPersona("pet");session.ConfigureRelationships(runtime,()=>A);Check(runtime.Service.UpdateSettings(false,true,true).Success,"disable failed");await session.SendTextAsync("complete work while disabled",cfg,"pet");root=session.CurrentAgentTask.Copy();Check(root.Status=="complete" && root.RelationshipSucceeded,"initial root success decision not complete");await taskStore.SaveAsync(root);root=taskStore.Load("pet",f.Root);Check(runtime.Service.UpdateSettings(true,true,true).Success,"enable failed");
                        if(ending=="direct")await session.SendTextAsync("resume successful root",cfg,"pet",resumeTask:root);
                        else if(ending=="error"){api.Fail=true;try{await session.SendTextAsync("resume then fail",cfg,"pet",resumeTask:root);}catch(ApiException){}Check(session.CurrentAgentTask.Status=="error","failed resume fixture");}
                        else {api.Delay=1000;int calls=api.Calls;var resume=session.SendTextAsync("resume then cancel",cfg,"pet",resumeTask:root);await Wait(()=>api.Calls>calls);await session.StopAsync();try{await resume;}catch(OperationCanceledException){}Check(session.CurrentAgentTask.Status=="cancelled","cancelled resume fixture");}
                        root=session.CurrentAgentTask.Copy();await taskStore.SaveAsync(root);await session.FlushHistoryAsync();
                    }
                    root=taskStore.Load("pet",f.Root);Check(root.RelationshipSucceeded,"root success decision lost through copy/store/reload");using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null)){
                        session.SelectPersona("pet");session.ConfigureRelationships(runtime,()=>B);await session.SendTextAsync("resume persisted prior success",cfg,"pet",resumeTask:root);CheckNoGrowth(runtime,A,ending+" completed root reopened after restart");CheckNoGrowth(runtime,B,"root ownership redirected");
                    }
                }
            });
            Run("first failure or cancellation while disabled remains eligible for its first actual success",async delegate{
                foreach(bool cancel in new[]{false,true})using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    var api=new Api{Fail=!cancel,Delay=cancel?1000:0};using(var session=new ConversationSession(api,new Speech(),()=>null)){
                        session.ConfigureRelationships(runtime,()=>A);Check(runtime.Service.UpdateSettings(false,true,true).Success,"disable failed");var attempt=session.SendTextAsync("first attempt fails",Settings(),"pet");if(cancel){await Wait(()=>api.Calls>0);await session.StopAsync();}try{await attempt;}catch(ApiException){}catch(OperationCanceledException){}
                        CheckNoGrowth(runtime,A,"unsuccessful operation gained score");Check(runtime.Service.UpdateSettings(true,true,true).Success,"enable failed");api.Fail=false;api.Delay=0;await session.RegenerateAsync(session.History.Messages.Last().Id,Settings(),"pet");var state=StateAfterExplicitRetry(runtime,A);Check(state!=null && state.Score==2 && state.Receipts.Count==1,"genuine first success suppressed");
                    }
                }
            });
            Run("disabled real Live completion cannot reopen through repeated notification or regeneration",async delegate{
                using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                    var api=new Api();using(var session=new ConversationSession(api,new Speech(),()=>null)){
                        session.ConfigureRelationships(runtime,()=>A);Check(runtime.Service.UpdateSettings(false,true,true).Success,"disable failed");session.AddLiveTurn("","real user","complete","complete","disabled-live",A,true);await runtime.FlushAsync();CheckNoGrowth(runtime,A,"disabled Live credited");Check(runtime.Service.UpdateSettings(true,true,true).Success,"enable failed");
                        session.AddLiveTurn("","real user","duplicate","complete","disabled-live",A,true);await runtime.FlushAsync();CheckNoGrowth(runtime,A,"repeated Live notification reopened growth");var reply=session.History.Messages.Last().Id;api.Fail=true;try{await session.RegenerateAsync(reply,Settings(),"pet");}catch(ApiException){}api.Fail=false;await session.RegenerateAsync(reply,Settings(),"pet");CheckNoGrowth(runtime,A,"failed Live regeneration reopened prior success");
                    }
                }
            });
            Run("settled history preserves an original failed-save event timestamp across restart and regeneration",async delegate{
                using(var f=new RelationshipFixture()){
                    string reply,token,queued;long ticks;
                    using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null,new HistoryStore(Path.Combine(f.Root,"history")))){
                        session.SelectPersona("pet");session.ConfigureRelationships(runtime,()=>A);Directory.CreateDirectory(Path.Combine(f.Root,"relationships","state.json"));await session.SendTextAsync("success with failed relationship save",Settings(),"pet");reply=session.History.Messages.Last().Id;token=session.History.Messages.Last().RelationshipEventId;Check(session.History.Messages.All(m=>m.RelationshipSucceeded),"successful pending turn decision missing");Check(runtime.PendingCount==1,"original pending event missing");queued=File.ReadAllText(Path.Combine(f.Root,"relationships","pending.json"));var entries=(object[])new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(queued);ticks=Convert.ToInt64(((Dictionary<string,object>)entries[0])["UtcTicks"]);await session.FlushHistoryAsync();
                    }
                    using(var runtime=new RelationshipRuntime(f.Root))using(var session=new ConversationSession(new Api(),new Speech(),()=>null,new HistoryStore(Path.Combine(f.Root,"history")))){
                        session.SelectPersona("pet");session.ConfigureRelationships(runtime,()=>B);await session.RegenerateAsync(reply,Settings(),"pet");await runtime.FlushAsync();Check(runtime.PendingCount==1 && File.ReadAllText(Path.Combine(f.Root,"relationships","pending.json"))==queued,"regeneration replaced original pending timestamp/attribution");Directory.Delete(Path.Combine(f.Root,"relationships","state.json"));await runtime.RetryPendingAsync();var state=runtime.Service.GetSnapshot(A).State;Check(state!=null && state.Score==2 && state.Receipts.Count==1 && state.Receipts[0].EventId==token && state.Receipts[0].Utc.Ticks==ticks,"pending original event failed to settle once");await session.RegenerateAsync(reply,Settings(),"pet");Check(runtime.Service.GetSnapshot(A).State.Score==2 && runtime.Service.GetSnapshot(B).State==null,"settled turn reopened after retry");await session.FlushHistoryAsync();
                    }
                }
            });
            Console.WriteLine("Relationship integration: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
        static void CheckNoGrowth(RelationshipRuntime runtime,string id,string reason){var state=runtime.Service.GetSnapshot(id).State;Check(state==null || state.Score==0 && state.Receipts.Count==0,reason);}
        static CompanionRelationshipState StateAfterExplicitRetry(RelationshipRuntime runtime,string id)
        {
            if(runtime.PendingCount>0){Check(runtime.Warning.Length>0,"pending event without persistence warning");Console.WriteLine("INFO explicit retry before asserting committed state; pending="+runtime.PendingCount+" warning="+runtime.Warning);runtime.RetryPending();}
            Check(runtime.PendingCount==0,"explicit retry could not settle pending progress: "+runtime.Warning);return runtime.Service.GetSnapshot(id).State;
        }
        static async Task Wait(Func<bool> done){for(int i=0;i<200;i++){if(done())return;await Task.Delay(5);}throw new Exception("fixture timeout");}
        sealed class Socket:ILiveSocket{
            readonly System.Collections.Concurrent.BlockingCollection<string> messages=new System.Collections.Concurrent.BlockingCollection<string>();public int Received;
            public void Push(string message){messages.Add(message);}public Task ConnectAsync(ConnectionSettings s,CancellationToken c){return Task.FromResult(0);}public Task SendTextAsync(string text,CancellationToken c){return Task.FromResult(0);}
            public Task<string> ReceiveTextAsync(CancellationToken c){return Task.Run(()=>{var message=messages.Take(c);Interlocked.Increment(ref Received);return message;},c);}public void Abort(){}public void Dispose(){messages.Dispose();}
        }
        sealed class Mic:IPcmMicrophone{public event Action<byte[]> PcmAvailable;public event Action<string> Failed;public void Start(){}public void Dispose(){}}
        sealed class Output:ILiveAudioOutput{public void Enqueue(byte[] bytes,int rate){}public void Interrupt(){}public void SetVolume(int v){}public void Dispose(){}}
        sealed class Api:IConversationApi,IAgentConversationApi{
            public bool Fail;public int Delay,Calls;public IList<ChatMessage> Messages;
            public async Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){Messages=m;Interlocked.Increment(ref Calls);if(Delay>0)await Task.Delay(Delay,c);if(Fail)throw new ApiException("fixture");return "complete reply";}
            public Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> text,Action<string> reasoning,Action<AgentToolEvent> tools,bool stream,CancellationToken cancel){return ChatAsync(s,messages,cancel);}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] bytes,CancellationToken c){return Task.FromResult("recognized");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string voice,string text,CancellationToken c){return Task.FromResult(new byte[0]);}public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string text,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int v){}public void Stop(){}public void Dispose(){}}
    }
}
