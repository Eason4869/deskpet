using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu.Tests
{
    public static class DataMaintenanceAdmissionTests
    {
        static int passed,failed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Run(string name,Func<Task> test){try{var pending=test();if(!pending.Wait(TimeSpan.FromSeconds(20)))throw new TimeoutException(name);pending.GetAwaiter().GetResult();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error);}}
        static async Task Rejected(Func<Task> action){try{await action();}catch(InvalidOperationException){return;}throw new Exception("maintenance admitted conflicting work");}
        static async Task<IDisposable> AfterTerminalTails(DataMaintenanceGate gate)
        {
            var deadline=DateTime.UtcNow.AddSeconds(5);
            while(true){try{return await gate.BeginAsync();}catch(InvalidOperationException){if(DateTime.UtcNow>=deadline)throw;}await Task.Delay(1);}
        }
        static string Root(){string path=Path.GetFullPath(Path.Combine("work","data-admission-fixtures",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(path);return path;}
        static PetSettings Config(){return new PetSettings{AgentEnabled=false,ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9001/v1",Model="test"}};}
        static ConversationSession Session(DataMaintenanceGate gate,HistoryStore store=null,Api api=null){return new ConversationSession(api??new Api(),new Speech(),()=>null,store,maintenance:gate);}
        public static int Main()
        {
            Run("exclusive maintenance rejects admitted activity and releases after failure",async()=>{
                var gate=new DataMaintenanceGate();using(gate.EnterActivity())await Rejected(async()=>{using(await gate.BeginAsync()){} });
                using(await gate.BeginAsync()){await Rejected(()=>{using(gate.EnterActivity()){}return Task.FromResult(0);});await Rejected(async()=>{using(await gate.BeginAsync()){} });}
                using(gate.EnterActivity()){}
            });
            Run("maintenance closes admission before draining and propagates writer failure",async()=>{
                var gate=new DataMaintenanceGate();var entered=new TaskCompletionSource<bool>();var tail=new TaskCompletionSource<bool>();
                using(gate.Register(()=>{entered.TrySetResult(true);return tail.Task;})){
                    var pending=gate.BeginAsync();await entered.Task;Check(!pending.IsCompleted,"returned before drain");await Rejected(()=>{using(gate.EnterActivity()){}return Task.FromResult(0);});
                    tail.SetException(new IOException("held writer failure"));bool failedDrain=false;try{using(await pending){}}catch(IOException){failedDrain=true;}Check(failedDrain&&!gate.IsMaintenanceActive,"failed drain leaked exclusion");
                }
                using(await gate.BeginAsync()){}
            });
            Run("actual session send and recording admission share maintenance exclusion",async()=>{
                var gate=new DataMaintenanceGate();var api=new Api{Hold=true};using(var session=Session(gate,null,api)){
                    var sending=session.SendTextAsync("hello",Config(),"pet");await api.Started.Task;await Rejected(async()=>{using(await gate.BeginAsync()){} });api.Finish.SetResult("answer");await sending;
                    using(await gate.BeginAsync()){
                        await Rejected(()=>session.SendTextAsync("blocked",Config(),"pet"));
                        await Rejected(()=>{session.SelectPersona("other");return Task.FromResult(0);});
                        await Rejected(()=>{session.AddLiveTurn("","blocked","answer");return Task.FromResult(0);});
                    }
                    Check(session.History.Messages.Count==2,"blocked work changed history");
                }
            });
            Run("disposed session retains its real queued history writer until maintenance drains",async()=>{
                var gate=new DataMaintenanceGate();var store=new HistoryStore(Path.Combine(Root(),"history"));var held=new TaskCompletionSource<bool>();
                typeof(HistoryStore).GetField("worker",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(store,held.Task);
                var session=Session(gate,store);var log=new ConversationLog();log.Commit("held original","answer");var save=store.SaveAsync("pet",log.Messages);session.Dispose();
                Check(gate.GetParticipants<ConversationSession>().Contains(session),"disposed writer owner unregistered early");
                var maintenance=gate.BeginAsync();Check(gate.IsMaintenanceActive,"reservation was not synchronous");Check(!maintenance.IsCompleted,"disposed writer dropped from drain");held.SetResult(true);
                using(await maintenance){await save;Check(store.Load("pet").Count==2,"real queued write not complete before handoff");}
            });
            Run("workspace creation is rejected before directory or session publication",async()=>{
                var gate=new DataMaintenanceGate();string root=Root();using(var manager=new ConversationWorkspaceManager(root,"pet",()=>new Api(),()=>new Speech(),()=>null,Config,()=>true,gate)){
                    var project=new ProjectDefinition{Id="project",Workspace=root};var conversation=new ConversationDefinition{Id="chat",Name="test",Mode="chat"};
                    using(await gate.BeginAsync()){await Rejected(()=>{manager.CreateWorkspace(project,conversation);return Task.FromResult(0);});Check(manager.OpenCount==0&&!Directory.Exists(Path.Combine(root,"conversations","chat")),"creation had side effects");}
                    var workspace=manager.CreateWorkspace(project,conversation);Check(Object.ReferenceEquals(workspace.Session.Maintenance,gate),"workspace gate differs");await manager.CloseWorkspaceAsync(workspace.Id);
                }
            });
            Run("queue admission and paused pending messages block maintenance",async()=>{
                var gate=new DataMaintenanceGate();var api=new Api{Hold=true};using(var session=Session(gate,null,api))using(var queue=new ConversationQueue(session,Config,()=>"pet",Path.Combine(Root(),"queue.json"))){
                    queue.Enqueue("first");await api.Started.Task;queue.Hold();queue.Enqueue("pending");api.Finish.SetResult("answer");await queue.FlushAsync();
                    Check(queue.HasPending,"fixture pending message lost");await Rejected(async()=>{using(await gate.BeginAsync()){} });queue.Remove(queue.Items.Single().Id);
                    using(await gate.BeginAsync()){await Rejected(()=>{queue.Enqueue("blocked");return Task.FromResult(0);});await Rejected(()=>{queue.Resume();return Task.FromResult(0);});}
                }
            });
            Run("dictation cancellation retains admission until a noncooperative ASR returns",async()=>{
                var gate=new DataMaintenanceGate();var api=new Api{HoldAsr=true};var cfg=Config();cfg.Transcription=new ConnectionSettings{BaseUrl="http://127.0.0.1/v1",Model="asr"};var owner=new object();
                using(var dictation=new DictationSession(api,()=>new Recorder(),()=>cfg,gate)){
                    using(await gate.BeginAsync())await Rejected(()=>{dictation.Start(owner);return Task.FromResult(0);});
                    dictation.Start(owner);await Rejected(async()=>{using(await gate.BeginAsync()){} });var recognizing=dictation.FinishAsync(owner);await api.Started.Task;
                    await dictation.CancelAsync(owner);await Rejected(async()=>{using(await gate.BeginAsync()){} });api.Finish.SetResult("late words");try{await recognizing;}catch(OperationCanceledException){}
                    using(await gate.BeginAsync()){}
                }
            });
            Run("Live holds admission through actual socket cleanup and rejects new calls",async()=>{
                var gate=new DataMaintenanceGate();var socket=new Socket();var cfg=Config();cfg.Live.ApiKey="fixture";
                using(var live=new GeminiLiveSession(()=>socket,()=>new Microphone(),()=>new Output(),gate)){
                    using(await gate.BeginAsync())await Rejected(()=>live.StartAsync(cfg,"persona"));
                    await live.StartAsync(cfg,"persona");await Rejected(async()=>{using(await gate.BeginAsync()){} });await live.StopAsync();using(await gate.BeginAsync()){}
                }
            });
            Run("background job pending approval and running process both reject maintenance",async()=>{
                var gate=new DataMaintenanceGate();var clock=new Clock();string root=Root();var authorization=new Authorization();var factory=new JobFactory();
                using(var store=new AgentJobStore(root,clock)){
                    var manager=new AgentJobManager(store,factory,clock,gate);await manager.RecoverAsync(CancellationToken.None);
                    var owner=new AgentJobOwner("project","conversation","primary");
                    var invocation=new AgentToolInvocation("start_job",new System.Collections.Generic.Dictionary<string,object>{{"command","echo fixture"},{"title","fixture"},{"working_directory",root},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId="project",AgentId="primary",TaskId="task",CallId="call"});
                    var context=new AgentJobStartContext(owner,invocation,authorization,()=>new AgentJobPolicySnapshot(root,"directory",1,1800,true,false,false,AgentApprovalMode.Ask,null),()=>new NoopLease(),()=>{});var spec=new AgentJobSpec("echo fixture","fixture",root,1800);
                    using(await gate.BeginAsync())await Rejected(()=>manager.StartAsync(context,spec,CancellationToken.None));
                    var start=manager.StartAsync(context,spec,CancellationToken.None);await authorization.Started.Task;await Rejected(async()=>{using(await gate.BeginAsync()){} });authorization.Allow.SetResult(true);var job=await start;
                    await Rejected(async()=>{using(await gate.BeginAsync()){} });await manager.StopAsync(owner,job.Id,AgentJobStopReason.User,CancellationToken.None);using(await AfterTerminalTails(gate)){}
                }
            });
            Run("simultaneous actual send and maintenance have exactly one admitted owner",async()=>{
                for(int attempt=0;attempt<25;attempt++){
                    var gate=new DataMaintenanceGate();var api=new Api{Hold=true};using(var session=Session(gate,null,api))using(var start=new ManualResetEventSlim(false)){
                        IDisposable maintenance=null;Task send=null;bool sent=false,maintained=false;
                        var first=Task.Run(async()=>{start.Wait();try{maintenance=await gate.BeginAsync();maintained=true;}catch(InvalidOperationException){}});
                        var second=Task.Run(async()=>{start.Wait();send=session.SendTextAsync("race",Config(),"pet");await Task.WhenAny(send,api.Started.Task);if(api.Started.Task.IsCompleted)sent=true;else try{await send;}catch(InvalidOperationException){}});
                        start.Set();await Task.WhenAll(first,second);Check(sent!=maintained,"both operations admitted or both rejected");
                        if(maintenance!=null)maintenance.Dispose();api.Finish.TrySetResult("answer");if(sent)await send;
                    }
                }
            });
            Run("workspace close owns admission through a held real history flush",async()=>{
                var gate=new DataMaintenanceGate();string root=Root();using(var manager=new ConversationWorkspaceManager(root,"pet",()=>new Api(),()=>new Speech(),()=>null,Config,()=>true,gate)){
                    var workspace=manager.CreateWorkspace(new ProjectDefinition{Id="project",Workspace=root},new ConversationDefinition{Id="closing",Mode="chat"});
                    var store=(HistoryStore)typeof(ConversationSession).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace.Session);var held=new TaskCompletionSource<bool>();typeof(HistoryStore).GetField("worker",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(store,held.Task);
                    workspace.Session.AddLiveTurn("pet","retained text","answer");var closing=manager.CloseWorkspaceAsync(workspace.Id);
                    await Rejected(async()=>{using(await gate.BeginAsync()){} });Check(!closing.IsCompleted,"close did not wait for real writer");held.SetResult(true);await closing;
                    using(await gate.BeginAsync())Check(store.Load("pet").Count==2,"close writer missing at maintenance boundary");
                }
            });
            Console.WriteLine("Data maintenance admission tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
        sealed class Api:IConversationApi
        {
            internal bool Hold,HoldAsr;internal readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>();internal readonly TaskCompletionSource<string> Finish=new TaskCompletionSource<string>();
            public Task<string> ChatAsync(ConnectionSettings c,System.Collections.Generic.IList<ChatMessage> m,CancellationToken token){Started.TrySetResult(true);return Hold?Finish.Task:Task.FromResult("answer");}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] bytes,CancellationToken token){Started.TrySetResult(true);return HoldAsr?Finish.Task:Task.FromResult("voice");}
            public Task<byte[]> SpeechAsync(ConnectionSettings c,string voice,string text,CancellationToken token){return Task.FromResult(new byte[0]);}public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int v){}public void Stop(){}public void Dispose(){}}
        sealed class Recorder:IAudioRecorder{public bool IsRecording{get{return true;}}public void Start(){}public Task<byte[]> FinishAsync(){return Task.FromResult(WaveData.EncodePcm(new byte[16000]));}public void Dispose(){}}
        sealed class Microphone:IPcmMicrophone{public event Action<byte[]> PcmAvailable{add{}remove{}}public event Action<string> Failed{add{}remove{}}public void Start(){}public void Dispose(){}}
        sealed class Output:ILiveAudioOutput{public void Enqueue(byte[] bytes,int rate){}public void Interrupt(){}public void SetVolume(int v){}public void Dispose(){}}
        sealed class Socket:ILiveSocket
        {
            bool setup;public Task ConnectAsync(ConnectionSettings c,CancellationToken token){return Task.FromResult(0);}public Task SendTextAsync(string text,CancellationToken token){return Task.FromResult(0);}
            public async Task<string> ReceiveTextAsync(CancellationToken token){if(!setup){setup=true;return "{\"setupComplete\":{}}";}await Task.Delay(Timeout.Infinite,token);return null;}public void Abort(){}public void Dispose(){}
        }
        sealed class Clock:IAgentJobClock{public DateTime UtcNow{get{return DateTime.UtcNow;}}public long ElapsedMilliseconds{get{return Environment.TickCount&Int32.MaxValue;}}}
        sealed class NoopLease:IDisposable{public void Dispose(){}}
        sealed class Authorization:IAgentAuthorizationService
        {
            internal readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(),Allow=new TaskCompletionSource<bool>();
            public async Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken token){Started.TrySetResult(true);await Allow.Task;return new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow,Reason="fixture"};}
        }
        sealed class JobFactory:IAgentJobProcessFactory{public Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token){return Task.FromResult<IAgentJobProcess>(new JobProcess());}}
        sealed class JobProcess:IAgentJobProcess
        {
            readonly TaskCompletionSource<AgentJobProcessExit> done=new TaskCompletionSource<AgentJobProcessExit>();public Task<AgentJobProcessExit> Completion{get{return done.Task;}}public AgentJobProcessExit Status{get;private set;}
            internal JobProcess(){Status=new AgentJobProcessExit(false,null,false,false,1);}public void Resume(){}public Task<AgentJobCleanupResult> StopAsync(CancellationToken token){Status=new AgentJobProcessExit(true,0,true,true,0);done.TrySetResult(Status);return Task.FromResult(new AgentJobCleanupResult(true,null));}public void Dispose(){}
        }
    }
}
