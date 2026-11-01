using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class DesktopObservationServiceTests
    {
        static int checks;
        static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("DesktopObservationServiceTests: "+checks+" assertions passed.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){await Gates();await Routes();await Output();await Races();await Deadlines();await Accounting();await AdditionalBoundaries();await BlockingCancellation();}
        sealed class Fixture
        {
            public readonly Clock Clock=new Clock();public readonly Capture Capture=new Capture();public readonly Api Api=new Api();public readonly DesktopObservationPolicy Policy=new DesktopObservationPolicy();
            public DesktopObservationService Service;
            public Fixture(){Service=new DesktopObservationService(Policy,Capture,()=>Api,Clock,bytes=>"small-normalized-scene");Capture.Clock=Clock;}
            public Task<ProactiveDecision> Generate(DesktopObservationContext c=null,CancellationToken cancel=default(CancellationToken)){return Service.GenerateAsync(c??DesktopObservationPolicyTests.Context(),cancel);}
        }
        static async Task Gates()
        {
            foreach(var c in new[]{DesktopObservationPolicyTests.Context(due:false),DesktopObservationPolicyTests.Context(eligible:false),DesktopObservationPolicyTests.Context(current:()=>false)}){var f=new Fixture();using(var d=await f.Generate(c))Check(d.Kind==ProactiveDecisionKind.Skip && f.Capture.Calls==0 && f.Api.Calls==0,"ineligible/undue/stale has zero capture and requests");}
            foreach(string gate in new[]{"off","consent","route","endpoint","configuration"}){var f=new Fixture();var s=DesktopObservationPolicyTests.Settings();if(gate=="off")s.DesktopObservationEnabled=false;if(gate=="consent")s.DesktopObservationConsentVersion=0;if(gate=="route")s.Text.ImageSupport=CapabilitySupport.Unknown;if(gate=="endpoint")s.Text.BaseUrl="http://127.0.0.1:2/v1";if(gate=="configuration")s.Text.Model="";using(var d=await f.Generate(DesktopObservationPolicyTests.Context(s)))Check(d.Kind==ProactiveDecisionKind.Skip && f.Capture.Calls==0 && f.Api.Calls==0,"preflight "+gate+" denies all image work");}
            foreach(DesktopCaptureStatus status in new[]{DesktopCaptureStatus.NoEligibleForeground,DesktopCaptureStatus.PermissionRequired,DesktopCaptureStatus.UnsupportedPlatform,DesktopCaptureStatus.NonInteractiveDesktop}){var f=new Fixture();f.Capture.Status=status;using(var d=await f.Generate())Check(d.Kind==ProactiveDecisionKind.Skip && f.Capture.Calls==0,"platform/privacy preflight never captures or opens permission UI");}
            {var f=new Fixture();f.Capture.Empty=true;using(var d=await f.Generate())Check(d.Kind==ProactiveDecisionKind.Skip && f.Api.Calls==0,"null capture is skip, no ordinary fallback");}
            {var f=new Fixture();var s=DesktopObservationPolicyTests.Settings();s.DesktopObservationExcludedApps.Add("private-app");using(var d=await f.Generate(DesktopObservationPolicyTests.Context(s)))Check(d.Kind==ProactiveDecisionKind.Skip && f.Api.Calls==0,"excluded app never reaches model even if adapter returns frame");Check(f.Capture.Options.IsExcluded("private-app"),"stable exclusion identities passed to capture adapter");}
        }
        static PetSettings Described()
        {
            var s=DesktopObservationPolicyTests.Settings();s.Text.ImageSupport=CapabilitySupport.Unsupported;
            var provider=new TextProviderSettings{Id="fallback",Name="fallback",Connection=new ConnectionSettings{BaseUrl="http://127.0.0.1:3/v1",Model="vision"},Models=new List<string>{"vision"}};
            provider.ModelCapabilities["vision"]=new ModelCapabilitySetting{Images=CapabilitySupport.Supported};provider.Normalize();s.TextProviders=new List<TextProviderSettings>{provider};s.ImageDescriptionModel=new ModelReference{ProviderId="fallback",ModelId="vision"};s.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(s);return s;
        }
        static async Task Routes()
        {
            var f=new Fixture();using(var d=await f.Generate()){
                Check(d.Kind==ProactiveDecisionKind.Speak && d.Message=="记得休息一下。" && d.CandidateEvent!=null,"valid JSON returns speak and candidate event");
                Check(f.Api.Calls==1 && f.Api.Connections[0].Model=="actual-chat" && f.Api.Messages[0][1].Attachments.Count==1,"direct route uses actual global model in exactly one call");
                Check(d.CandidateEvent.CompanionId=="dafeiyu" && d.CandidateEvent.Description==null && f.Capture.Last.IsDisposed,"candidate has companion identity; direct has no caption; frame disposed");
                string prompt=String.Join("\n",f.Api.Messages[0].Select(m=>m.Content));Check(!prompt.Contains("private-app") && !prompt.Contains("secret-window-title") && !prompt.Contains("127.0.0.1") && prompt.Contains("untrusted"),"prompt excludes native identity and settings; image instructions untrusted");
            }
            f=new Fixture();f.Api.Replies.Enqueue("[\"a safe scene\"]");using(var d=await f.Generate(DesktopObservationPolicyTests.Context(Described()))){Check(d.Kind==ProactiveDecisionKind.Speak && f.Api.Calls==2,"explicit described route uses at most caption and greeting calls");Check(f.Api.Connections[0].Model=="vision" && f.Api.Connections[1].Model=="actual-chat","caption and text use existing model references");Check(d.CandidateEvent.Description=="a safe scene" && f.Api.Messages[1].All(m=>m.Attachments==null || m.Attachments.Count==0),"bounded caption reused on event and text route sends no image");}
            f=new Fixture();f.Api.Replies.Enqueue("[\""+new string('x',241)+"\"]");using(var d=await f.Generate(DesktopObservationPolicyTests.Context(Described())))Check(d.Kind==ProactiveDecisionKind.OrdinaryFallback && f.Api.Calls==1,"oversized caption stops before greeting; local fallback only");
        }
        static async Task Output()
        {
            var f=new Fixture();f.Api.Reply="{\"speak\":false,\"message\":\"\"}";using(var d=await f.Generate())Check(d.Kind==ProactiveDecisionKind.Skip && d.CandidateEvent==null && f.Api.Calls==1,"valid skip never generates ordinary greeting");
            foreach(string reply in new[]{"not json","```json\n{}\n```","{\"speak\":\"true\",\"message\":\"hello\"}","{\"speak\":true,\"message\":\""+new string('长',81)+"\"}","{\"speak\":true,\"message\":\"hi\",\"tool\":\"shell\"}","{\"speak\":true,\"message\":\"one\\ntwo\"}","{\"speak\":false,\"message\":\"wrong\"}"}){f=new Fixture();f.Api.Reply=reply;using(var d=await f.Generate())Check(d.Kind==ProactiveDecisionKind.OrdinaryFallback && d.CandidateEvent==null && f.Api.Calls==1 && d.Message.Length<=80,"invalid strict JSON yields only local ordinary fallback");}
            f=new Fixture();var c=new DesktopObservationContext(DesktopObservationPolicyTests.Settings(),"dafeiyu","大肥鱼",null,new[]{"记得休息一下！"},DateTime.Now,true,true,()=>true);using(var d=await f.Generate(c))Check(d.Kind==ProactiveDecisionKind.Skip && d.CandidateEvent==null,"punctuation-only duplicate speech skipped");
            f=new Fixture();f.Api.Failure=new ApiException("private server stack /secret/key");using(var d=await f.Generate())Check(d.Kind==ProactiveDecisionKind.OrdinaryFallback && !d.Message.Contains("secret") && d.Reason==ObservationReason.ModelFailed,"error body never leaks into UI decision");
            f=new Fixture();using(var d=await f.Generate()){}f.Clock.Advance(TimeSpan.FromMinutes(10));using(var d=await f.Generate())Check(d.Reason==ObservationReason.DuplicateScene && f.Api.Calls==1,"same normalized scene skips model for thirty minutes");
        }
        static async Task Races()
        {
            foreach(string state in new[]{"foreground","companion","relationship","model","endpoint","capability","language","enabled","chat"}){
                var f=new Fixture();var epoch=0;f.Api.Pending=new TaskCompletionSource<string>();var c=DesktopObservationPolicyTests.Context(current:()=>Volatile.Read(ref epoch)==0);var pending=f.Generate(c);await Until(()=>f.Api.Calls==1);Interlocked.Increment(ref epoch);f.Api.Pending.SetResult(f.Api.Reply);using(var d=await pending)Check(d.Kind==ProactiveDecisionKind.Skip && d.CandidateEvent==null,"changed "+state+" epoch drops result and event");
            }
            {var f=new Fixture();f.Capture.Current=false;using(var d=await f.Generate())Check(d.Reason==ObservationReason.Stale && f.Api.Calls==0,"foreground changed after capture prevents model call");}
            {var f=new Fixture();f.Capture.AfterCapture=()=>f.Capture.Current=false;using(var d=await f.Generate())Check(d.Kind==ProactiveDecisionKind.Skip && f.Capture.Last.IsDisposed,"late foreground frame disposed");}
            {var f=new Fixture();f.Api.Pending=new TaskCompletionSource<string>();var s=DesktopObservationPolicyTests.Settings();var c=DesktopObservationPolicyTests.Context(s);var pending=f.Generate(c);await Until(()=>f.Api.Calls==1);s.Text.Model="changed";s.SystemPrompt="changed";f.Api.Pending.SetResult(f.Api.Reply);using(var d=await pending)Check(f.Api.Connections[0].Model=="actual-chat" && d.Kind==ProactiveDecisionKind.Speak,"detached request snapshots cannot be redirected by mutation; host epoch separately enforces invalidation");}
        }
        static async Task Deadlines()
        {
            {var f=new Fixture();f.Capture.Pending=new TaskCompletionSource<DesktopObservationFrame>();var pending=f.Generate();await Until(()=>f.Capture.Calls==1 && f.Clock.DelayCount>=2);f.Clock.Advance(TimeSpan.FromSeconds(2));using(var d=await pending)Check(d.Kind==ProactiveDecisionKind.Skip && d.Reason==ObservationReason.CaptureTimeout,"uncooperative capture bounded at two seconds");using(var d=await f.Generate())Check(d.Reason==ObservationReason.Busy,"timed out capture retains actual slot");var late=f.Capture.Frame();f.Capture.Pending.SetResult(late);await Until(()=>!f.Service.IsBusy);Check(late.IsDisposed,"late frame disposed after actual capture returns");}
            {var f=new Fixture();f.Api.Pending=new TaskCompletionSource<string>();var pending=f.Generate();await Until(()=>f.Api.Calls==1 && f.Clock.DelayCount>=2);f.Clock.Advance(TimeSpan.FromSeconds(8));using(var d=await pending){await Until(()=>f.Api.Token.IsCancellationRequested);Check(d.Kind==ProactiveDecisionKind.OrdinaryFallback,"model bounded at eight seconds and cancelled");}using(var d=await f.Generate())Check(d.Reason==ObservationReason.Busy && f.Api.Calls==1,"uncooperative model retains slot after caller timeout");Check(f.Capture.Last.IsDisposed,"original frame cleared on caller timeout even while transport remains pending");Check(!f.Api.Disposed && !f.Service.WaitForIdleAsync().IsCompleted,"transport and maintenance activity retained while real call runs");var idle=f.Service.WaitForIdleAsync();f.Api.Pending.SetResult(f.Api.Reply);await Until(()=>!f.Service.IsBusy);await idle;Check(f.Api.Disposed && f.Service.WaitForIdleAsync().IsCompleted,"late model completion drains activity and disposes transport");}
            {var f=new Fixture();f.Api.Pending=new TaskCompletionSource<string>();using(var cancel=new CancellationTokenSource()){var pending=f.Generate(cancel:cancel.Token);await Until(()=>f.Api.Calls==1);cancel.Cancel();using(var d=await pending)Check(d.Kind==ProactiveDecisionKind.Skip && d.Reason==ObservationReason.Cancelled,"caller cancellation produces no fallback");using(var d=await f.Generate())Check(d.Reason==ObservationReason.Busy,"cancelled noncooperative call retains slot");f.Api.Pending.SetResult(f.Api.Reply);await Until(()=>!f.Service.IsBusy);}}
            {var f=new Fixture();f.Capture.BlockPreflight=new TaskCompletionSource<bool>();var pending=f.Generate();await Until(()=>f.Capture.StatusCalls>0);f.Clock.Advance(TimeSpan.FromSeconds(10));using(var d=await pending)Check(d.Kind==ProactiveDecisionKind.Skip && f.Capture.Calls==0,"ten second total includes blocked preflight");f.Capture.BlockPreflight.SetResult(true);await Until(()=>!f.Service.IsBusy);Check(f.Capture.Calls==0,"late preflight cannot start capture");}
        }
        static async Task Accounting()
        {
            string root=Path.GetFullPath(Path.Combine("work","observation-usage-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            try{var ledger=new UsageStore(root);UsageTracking.Initialize(ledger);var f=new Fixture();f.Api.Track=true;using(var d=await f.Generate()){}Check(ledger.ReadAll().Count==1 && ledger.ReadAll()[0].Feature=="desktop-observation","direct usage counted exactly once");
                f=new Fixture();f.Api.Track=true;f.Api.Replies.Enqueue("[\"scene\"]");using(var d=await f.Generate(DesktopObservationPolicyTests.Context(Described()))){}var records=ledger.ReadAll();Check(records.Count==3 && records.Count(r=>r.Feature=="image-description")==1 && records.Count(r=>r.Feature=="greeting")==1,"described usage has one record per actual call with separate categories");
                f=new Fixture();using(var d=await f.Generate()){}f.Clock.Advance(TimeSpan.FromMinutes(1));using(var d=await f.Generate(DesktopObservationPolicyTests.Context(manual:true)))Check(d.IsManualTest && f.Policy.ManualTestCount==1 && f.Api.Calls==2,"manual test labelled and bypasses automatic dedupe/quota without resetting them");using(var d=await f.Generate())Check(d.Reason==ObservationReason.MinimumInterval,"manual test leaves automatic quota intact");
            }finally{UsageTracking.Initialize(null);Directory.Delete(root,true);}
        }
        static async Task AdditionalBoundaries()
        {
            {var f=new Fixture();Check(f.Service.WaitForIdleAsync().IsCompleted,"unused service is already drained");}
            {var f=new Fixture();var settings=Described();settings.Text.BaseUrl="http://127.0.0.1:8/v1";using(var d=await f.Generate(DesktopObservationPolicyTests.Context(settings)))Check(d.Reason==ObservationReason.ConsentRequired && f.Capture.Calls==0,"changed text destination also invalidates described-route consent");}
            {var f=new Fixture();using(var d=await f.Generate(DesktopObservationPolicyTests.Context(due:false,manual:true)))Check(d.IsManualTest && d.Kind==ProactiveDecisionKind.Speak,"manual test bypasses schedule without changing it");}
            {var f=new Fixture();var settings=Described();settings.Text.ImageSupport=CapabilitySupport.Supported;settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(settings);using(var d=await f.Generate(DesktopObservationPolicyTests.Context(settings)))Check(d.Kind==ProactiveDecisionKind.Speak && f.Api.Calls==1,"supported global model remains direct even with fallback configured");}
            foreach(string json in new[]{"{speak:true,message:'hello'}","{\"speak\":true,\"message\":\"hello\",}","{\"speak\":false,\"speak\":true,\"message\":\"hello\"}"}){var f=new Fixture();f.Api.Reply=json;using(var d=await f.Generate())Check(d.Kind==ProactiveDecisionKind.OrdinaryFallback,"JSON rejects JavaScript extensions, trailing commas and duplicate fields");}
            {var f=new Fixture();var epoch=0;f.Api.BeforeFailure=()=>Interlocked.Increment(ref epoch);f.Api.Failure=new ApiException("private error");using(var d=await f.Generate(DesktopObservationPolicyTests.Context(current:()=>Volatile.Read(ref epoch)==0)))Check(d.Kind==ProactiveDecisionKind.Skip && d.Reason==ObservationReason.Stale,"stale error response does not produce fallback");}
            {var f=new Fixture();f.Api.Failure=new ApiException("failure");using(var d=await f.Generate()){}var admission=f.Policy.TryBegin(DesktopObservationPolicyTests.Context(),f.Clock.UtcNow);Check(admission.AutomaticCallsLastHour==1 && admission.ConsecutiveFailures==1 && admission.Reason==ObservationReason.Cooldown,"model failure consumes one automatic call and cooldown");}
            {var f=new Fixture();f.Capture.BlockCurrent=new TaskCompletionSource<bool>();var pending=f.Generate();await Until(()=>f.Capture.CurrentCalls==1);f.Clock.Advance(TimeSpan.FromSeconds(10));using(var d=await pending)Check(d.Kind==ProactiveDecisionKind.Skip && f.Api.Calls==0 && f.Capture.Last.IsDisposed,"direct total deadline includes blocked currentness and clears image");f.Capture.BlockCurrent.SetResult(true);await Until(()=>!f.Service.IsBusy);Check(f.Capture.Last.IsDisposed && f.Api.Calls==0,"late currentness cannot start model");}
            {var f=new Fixture();f.Api.FirstPending=new TaskCompletionSource<string>();f.Api.Pending=new TaskCompletionSource<string>();var pending=f.Generate(DesktopObservationPolicyTests.Context(Described()));await Until(()=>f.Api.Calls==1);f.Clock.Advance(TimeSpan.FromSeconds(7));f.Api.FirstPending.SetResult("[\"scene\"]");await Until(()=>f.Api.Calls==2 && f.Clock.DelayCount>=2);f.Clock.Advance(TimeSpan.FromSeconds(8));using(var d=await pending)Check(d.Kind==ProactiveDecisionKind.OrdinaryFallback && d.Reason==ObservationReason.ModelTimeout && f.Api.Calls==2,"second model independently bounded to eight seconds");f.Api.Pending.SetResult(f.Api.Reply);await Until(()=>!f.Service.IsBusy);}
            {var f=new Fixture();f.Capture.BlockCurrent=new TaskCompletionSource<bool>();var pending=f.Generate(DesktopObservationPolicyTests.Context(Described()));await Until(()=>f.Capture.CurrentCalls==1);f.Clock.Advance(TimeSpan.FromSeconds(17));Check(!pending.IsCompleted,"described pipeline retains full eighteen second budget");f.Clock.Advance(TimeSpan.FromSeconds(1));using(var d=await pending)Check(d.Kind==ProactiveDecisionKind.Skip,"described total deadline is eighteen seconds including preflight");f.Capture.BlockCurrent.SetResult(true);await Until(()=>!f.Service.IsBusy);}
            {
                TextResources.ConfigureStartupLanguage("zh_CN");var chinese=TextResources.CaptureLanguage();var english=TextResources.PrepareLanguage("en_US");
                var f=new Fixture();var cfg=DesktopObservationPolicyTests.Settings();cfg.SystemPrompt="custom frozen persona";var user=new ChatMessage("user","permitted memory","turn","complete","chat");var reply=new ChatMessage("assistant","completed answer","turn","complete","chat");
                var c=new DesktopObservationContext(cfg,"dafeiyu","大肥鱼",new[]{user,reply},null,DateTime.Now,true,true,()=>true);TextResources.ApplyLanguage(english);cfg.SystemPrompt="changed persona";
                try{using(var d=await f.Generate(c)){string prompt=f.Api.Messages[0][0].Content;Check(prompt.Contains("custom frozen persona") && !prompt.Contains("changed persona") && prompt.Contains(chinese.Get("languages.replyInstruction")),"request retains persona and language snapshots");Check(f.Api.Messages[0][1].Content.Contains("permitted memory") && f.Api.Messages[0][1].Content.Contains("completed answer"),"permitted completed memory included as bounded quoted context");}}finally{TextResources.ApplyLanguage(chinese);}
            }
            {var f=new Fixture();ProactiveEvent candidate;using(var d=await f.Generate()){candidate=d.CandidateEvent;using(var store=new ProactiveEventStore(()=>f.Clock.UtcNow)){Check(store.TryGet(candidate.Id)==null,"service creates candidate only; no event registration");store.Register(candidate);d.Dispose();Check(candidate.Png==null && store.TryGet(candidate.Id).Png!=null,"candidate disposal clears transient image while displayed store copy survives");}}}
        }
        static async Task Until(Func<bool> condition){var limit=Stopwatch.StartNew();while(!condition()){if(limit.ElapsedMilliseconds>5000)throw new Exception("fixture did not reach awaited boundary");await Task.Delay(1);}}
        sealed class Clock:IClock
        {
            sealed class Wait {public DateTime At;public TaskCompletionSource<bool> Task=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);}
            readonly object gate=new object();readonly List<Wait> waits=new List<Wait>();DateTime now=new DateTime(2026,11,2,0,0,0,DateTimeKind.Utc);
            public DateTime UtcNow{get{lock(gate)return now;}}
            public int DelayCount{get{lock(gate)return waits.Count(w=>!w.Task.Task.IsCompleted);}}
            public Task Delay(TimeSpan span,CancellationToken cancel){lock(gate){var w=new Wait{At=now+span};waits.Add(w);cancel.Register(()=>w.Task.TrySetCanceled());return w.Task.Task;}}
            public void Advance(TimeSpan span){Wait[] due;lock(gate){now+=span;due=waits.Where(w=>w.At<=now).ToArray();}foreach(var w in due)w.Task.TrySetResult(true);}
        }
        sealed class Capture:IDesktopObservationCapture
        {
            public Clock Clock;public int Calls,StatusCalls,CurrentCalls;public bool Empty,Current=true;public DesktopCaptureStatus Status=DesktopCaptureStatus.Ready;public DesktopCaptureOptions Options;public DesktopObservationFrame Last;public TaskCompletionSource<DesktopObservationFrame> Pending;public TaskCompletionSource<bool> BlockPreflight,BlockCurrent;public Action AfterCapture;
            public DesktopCaptureStatus GetStatus(){Interlocked.Increment(ref StatusCalls);if(BlockPreflight!=null)BlockPreflight.Task.GetAwaiter().GetResult();return Status;}
            public DesktopObservationFrame Frame(){return new DesktopObservationFrame(new byte[]{137,80,78,71,13,10,26,10},new DateTimeOffset(Clock.UtcNow),new DesktopObservationIdentity(1,2,3,4,"private-app","secret-window-title","class",0,0,10,10));}
            public Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel){Options=options;Interlocked.Increment(ref Calls);Last=Empty?null:Frame();if(AfterCapture!=null)AfterCapture();return Pending==null?Task.FromResult(Last):Pending.Task;}
            public bool IsCurrent(DesktopObservationFrame frame){Interlocked.Increment(ref CurrentCalls);if(BlockCurrent!=null)BlockCurrent.Task.GetAwaiter().GetResult();return Current;}
        }
        sealed class Api:IConversationApi
        {
            public int Calls;public string Reply="{\"speak\":true,\"message\":\"记得休息一下。\"}";public Queue<string> Replies=new Queue<string>();public Exception Failure;public Action BeforeFailure;public TaskCompletionSource<string> Pending,FirstPending;public CancellationToken Token;public bool Disposed,Track;public List<ConnectionSettings> Connections=new List<ConnectionSettings>();public List<IList<ChatMessage>> Messages=new List<IList<ChatMessage>>();
            public TaskCompletionSource<bool> BlockCancellation;public readonly TaskCompletionSource<bool> CancellationEntered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);public int Completed;
            public async Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Token=cancel;Connections.Add(settings.Copy());Messages.Add(messages.Select(m=>m.Copy()).ToList());if(BlockCancellation!=null)cancel.Register(()=>{CancellationEntered.TrySetResult(true);BlockCancellation.Task.GetAwaiter().GetResult();});int call=Interlocked.Increment(ref Calls);using(var request=Track?UsageTracking.BeginRequest(settings,cancel):null){if(Failure!=null){if(BeforeFailure!=null)BeforeFailure();throw Failure;}var result=call==1&&FirstPending!=null?await FirstPending.Task:Pending!=null?await Pending.Task:Replies.Count>0?Replies.Dequeue():Reply;if(request!=null)request.Complete();Interlocked.Exchange(ref Completed,1);return result;}}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken c){throw new Exception("no audio");}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){throw new Exception("no speech");}public void Dispose(){Disposed=true;}
        }
        static async Task BlockingCancellation()
        {
            foreach(bool caller in new[]{true,false})
            {
                var f=new Fixture();f.Api.Pending=new TaskCompletionSource<string>();f.Api.BlockCancellation=new TaskCompletionSource<bool>();
                using(var cancel=new CancellationTokenSource())
                {
                    var pending=f.Generate(cancel:cancel.Token);await Until(()=>f.Api.Calls==1 && f.Clock.DelayCount>=2);
                    Task hostCancel=Task.FromResult(true);
                    if(caller)hostCancel=Task.Run(()=>cancel.Cancel());else f.Clock.Advance(TimeSpan.FromSeconds(8));
                    await Until(()=>f.Api.CancellationEntered.Task.IsCompleted);await Until(()=>pending.IsCompleted);
                    using(var d=await pending)Check(d.Kind==(caller?ProactiveDecisionKind.Skip:ProactiveDecisionKind.OrdinaryFallback),"blocking cancellation callback cannot delay caller cancellation/model deadline");
                    await Until(()=>hostCancel.IsCompleted);Check(hostCancel.IsCompleted,"external cancellation never synchronously executes transport callback");
                    Check(f.Capture.Last.IsDisposed && f.Service.IsBusy && !f.Service.WaitForIdleAsync().IsCompleted,"callback keeps actual slot/activity but never original screenshot");
                    using(var d=await f.Generate())Check(d.Reason==ObservationReason.Busy && f.Api.Calls==1,"blocked callback cannot admit replacement call");
                    f.Api.Pending.SetResult(f.Api.Reply);await Until(()=>Volatile.Read(ref f.Api.Completed)==1);Check(f.Service.IsBusy && !f.Api.Disposed,"completed model still waits for blocked cancellation callback cleanup");
                    f.Api.BlockCancellation.SetResult(true);await f.Service.WaitForIdleAsync();await hostCancel;Check(!f.Service.IsBusy && f.Api.Disposed,"callback release drains operation and disposes transport");
                }
            }
        }
    }
}
