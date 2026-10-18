using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class QueueTests
    {
        static int passed;
        public static int Main(){try{Run().GetAwaiter().GetResult();SafeGuide().GetAwaiter().GetResult();GuidanceReliability().GetAwaiter().GetResult();GuidanceSaveFailure().GetAwaiter().GetResult();Console.WriteLine("Queue tests: "+passed+" passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
        static async Task Run()
        {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"queue-test-"+Guid.NewGuid().ToString("N"),"queue.json");
            // Hold completion until the assertions observe actual API entry.
            var api=new Api{Release=new TaskCompletionSource<bool>()};var cfg=new PetSettings{ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};
            using(var session=new ConversationSession(api,new Speech(),()=>null))
            using(var queue=new ConversationQueue(session,()=>cfg,()=>"E包",path))
            {
                session.SelectPersona("e-bao");queue.Enqueue("one");await Until(()=>api.Calls.Count==1);queue.Enqueue("two");queue.Enqueue("three");
                Check(queue.Items.Count==2,"busy sends enter visible queue");api.Release.SetResult(true);await Until(()=>api.Calls.Count==3 && !queue.IsRunning);
                Check(String.Join(",",api.Calls)=="one,two,three" && api.Maximum==1,"queue sends serially in order");
                api.WaitForCancellation="stop-me";queue.Enqueue("stop-me");await Until(()=>api.Calls.Last()=="stop-me");
                queue.Enqueue("keep-me");await queue.PauseAsync();api.WaitForCancellation=null;
                Check(queue.IsPaused && queue.Items.Single().Text=="keep-me","stop pauses and preserves remaining messages");
                string id=queue.Items.Single().Id;queue.Edit(id,"edited");Check(queue.Items.Single().Text=="edited","queued text is editable");queue.Resume();await Until(()=>!queue.IsRunning && queue.Items.Count==0);
                Check(api.Calls.Last()=="edited","resume sends edited text");
                api.WaitForCancellation="stop without waiting items";queue.Enqueue("stop without waiting items");await Until(()=>api.Calls.Last()=="stop without waiting items");
                await queue.PauseAsync();api.WaitForCancellation=null;queue.Enqueue("explicit new send");await Until(()=>!queue.IsRunning && queue.Items.Count==0);
                Check(api.Calls.Last()=="explicit new send","new send after stopping an empty queue starts normally");
                // Keep the original request in flight even if a busy CI scheduler
                // resumes this test after the fixture's normal 120ms response time.
                api.WaitForCancellation="original question";queue.Enqueue("original question");await Until(()=>api.Calls.Last()=="original question");
                queue.Enqueue("after");await Task.Delay(300);await queue.InterruptAsync("make it shorter");api.WaitForCancellation=null;await Until(()=>!queue.IsRunning && queue.Items.Count==0);
                Check(String.Join(",",api.Calls.Skip(api.Calls.Count-3))=="original question,make it shorter,after","steering takes priority then resumes previous queue");
                var request=api.Requests[api.Requests.Count-2];Check(request.Any(m=>m.Content=="original question") && request.Any(m=>m.Content.Contains("partial") && m.Content.Contains("未完成")),"steering includes original question and marked partial reply");
                api.WaitForCancellation="cancel race";api.CancelRelease=new TaskCompletionSource<bool>();queue.Enqueue("cancel race");await Until(()=>api.Calls.Last()=="cancel race");queue.Enqueue("keep after race");
                var steering=queue.InterruptAsync("priority race");var stopping=queue.PauseAsync();api.CancelRelease.SetResult(true);await Task.WhenAll(steering,stopping);
                Check(queue.IsPaused && !queue.IsRunning && String.Join(",",queue.Items.Select(m=>m.Text))=="priority race,keep after race","explicit stop during steering keeps priority and remaining entries paused");
                api.WaitForCancellation=null;api.CancelRelease=null;foreach(var item in queue.Items)queue.Remove(item.Id);queue.Resume();
                api.Fail=true;queue.Enqueue("failure");queue.Enqueue("still waiting");await Until(()=>queue.IsPaused && !queue.IsRunning);
                Check(queue.Items.Count==2 && queue.Items[0].Text=="failure","failure keeps failed text and pauses following messages");
                queue.Remove(queue.Items[0].Id);Check(queue.Items.Single().Text=="still waiting","queued entry can be removed");
                Check(!File.ReadAllText(path).Contains("still waiting"),"disk queue protects pending text with current-user encryption");
                session.SelectPersona("other");Check(queue.Items.Count==0,"queues are isolated by companion");session.SelectPersona("e-bao");Check(queue.Items.Count==1,"companion queue survives switching");
            }
            await Reordering(cfg);

            var modeCfg=new PetSettings{AgentEnabled=true,ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};
            var modeApi=new Api();using(var session=new ConversationSession(modeApi,new Speech(),()=>null))
            using(var queue=new ConversationQueue(session,()=>modeCfg,()=>"E包",path+".modes"))
            {
                queue.Enqueue("work request");await Until(()=>modeApi.Calls.Count==1 && !queue.IsRunning);
                queue.Enqueue("quick request",null,true);await Until(()=>modeApi.Calls.Count==2 && !queue.IsRunning);
                var replies=session.History.Messages.Where(m=>m.Role=="assistant").ToList();
                Check(replies.Count==2 && replies[0].WorkMode && !replies[1].WorkMode,"conversation-only queue entry sends as pure chat while work mode stays global");
                Check(modeCfg.AgentEnabled,"conversation-only sends never change the shared work-mode setting");
            }
            string attachmentPath=Path.Combine(Path.GetDirectoryName(path),"附件.txt");File.WriteAllText(attachmentPath,"queue attachment");var attachment=ChatAttachment.Read(attachmentPath);
            var attachmentApi=new Api{WaitForCancellation="hold"};string attachmentQueuePath=path+".attachments";
            using(var session=new ConversationSession(attachmentApi,new Speech(),()=>null))
            using(var queue=new ConversationQueue(session,()=>cfg,()=>"E包",attachmentQueuePath))
            {
                queue.Enqueue("hold");await Until(()=>attachmentApi.Calls.Count==1);queue.Enqueue("",new[]{attachment});await queue.PauseAsync();File.Delete(attachmentPath);
                Check(queue.Items.Single().Attachments.Single().Text=="queue attachment","attachment-only queue keeps a snapshot after source deletion");
            }
            var restoredApi=new Api();using(var session=new ConversationSession(restoredApi,new Speech(),()=>null))
            using(var queue=new ConversationQueue(session,()=>cfg,()=>"E包",attachmentQueuePath))
            {
                Check(queue.IsPaused && queue.Items.Single().Attachments.Single().Name=="附件.txt","encrypted pending attachments restore paused");queue.Resume();await Until(()=>!queue.IsRunning && queue.Items.Count==0);
                Check(restoredApi.Requests.Last().Last().Attachments.Single().Text=="queue attachment","restored attachment reaches the actual model request");
                Check(session.History.Messages.First().Attachments.Single().Text=="queue attachment","sent attachments remain in user history");
            }
            using(var session=new ConversationSession(new Api(),new Speech(),()=>null))
            using(var queue=new ConversationQueue(session,()=>cfg,()=>"E包",path))
            {session.SelectPersona("e-bao");Check(queue.IsPaused && queue.Items.Single().Text=="still waiting" && !session.IsBusy,"restart restores queue paused without automatic network request");}
        }
        static async Task Reordering(PetSettings cfg)
        {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"queue-reorder-"+Guid.NewGuid().ToString("N"),"queue.json");
            var api=new Api{Release=new TaskCompletionSource<bool>()};
            using(var session=new ConversationSession(api,new Speech(),()=>null))
            using(var queue=new ConversationQueue(session,()=>cfg,()=>"E包",path))
            {
                session.SelectPersona("e-bao");queue.Enqueue("active");await Until(()=>api.Calls.Count==1);
                queue.Enqueue("A");queue.Enqueue("B");queue.Enqueue("C");var items=queue.Items;string a=items[0].Id,b=items[1].Id,c=items[2].Id;
                Check(queue.MoveBefore(c,a) && String.Join(",",queue.Items.Select(m=>m.Text))=="C,A,B","stable-ID move places pending row before its target");
                Check(queue.MoveBefore(a,null) && String.Join(",",queue.Items.Select(m=>m.Text))=="C,B,A","drop at end appends without duplicating entries");
                Check(!queue.IsPaused && api.Calls.Single()=="active","reordering leaves in-flight request and pause state unchanged");
                queue.Edit(c,"C edited");queue.Hold();api.Release.SetResult(true);await Until(()=>!queue.IsRunning);
                using(var restoredSession=new ConversationSession(new Api(),new Speech(),()=>null))
                using(var restored=new ConversationQueue(restoredSession,()=>cfg,()=>"E包",path))
                {restoredSession.SelectPersona("e-bao");Check(restored.IsPaused && String.Join(",",restored.Items.Select(m=>m.Text))=="C edited,B,A","reordered encrypted queue survives reopening with edits and IDs");}
                session.SelectPersona("other");Check(!queue.MoveBefore(c,null),"a stale drag cannot move a row into another companion queue");session.SelectPersona("e-bao");
                api.Release=new TaskCompletionSource<bool>();queue.Resume();await Until(()=>api.Calls.Count==2);
                Check(!queue.MoveBefore(c,b),"an entry that started sending cannot be reinserted by a stale drop");
                Check(!queue.MoveBefore(b,c) && queue.Items[0].Id==b,"a target that started sending safely rejects stale drop");
                queue.Remove(a);Check(!queue.MoveBefore(a,b) && !queue.MoveBefore(b,a),"deleted source and deleted destination are both safe no-ops");
                api.Release.SetResult(true);await Until(()=>!queue.IsRunning && queue.Items.Count==0);
                Check(String.Join(",",api.Calls)=="active,C edited,B" && api.Maximum==1,"actual subsequent requests follow reordered pending sequence serially");
            }
        }
        static async Task SafeGuide()
        {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"safe-guide-"+Guid.NewGuid().ToString("N"),"queue.json");var api=new Api{Release=new TaskCompletionSource<bool>()};var cfg=new PetSettings{ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};
            using(var session=new ConversationSession(api,new Speech(),()=>null))using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",path)){
                queue.Enqueue("active");await Until(()=>api.Calls.Count==1);queue.Enqueue("later");await queue.SteerAsync("guide safely");
                Check(session.IsBusy && session.History.Messages.Last(m=>m.Role=="assistant").Status=="pending" && api.Calls.Count==1,"safe guidance never cancels the active response");
                Check(session.History.Messages.Count(m=>m.Role=="user" && m.Content=="guide safely")==1,"accepted guidance is visible immediately without a duplicate user turn");
                Check(queue.Items[0].IsGuidance && queue.Items[0].Text=="guide safely","guidance is durable and prioritized without interrupting work");
                api.Release.SetResult(true);await Until(()=>!queue.IsRunning && !session.IsBusy);Check(String.Join(",",api.Calls)=="active,guide safely,later" && session.History.Messages.All(m=>m.Status!="cancelled"),"plain conversation guidance waits for completion and preserves queue order");
            }
            var interruptApi=new Api{WaitForCancellation="active with guidance"};
            using(var session=new ConversationSession(interruptApi,new Speech(),()=>null))using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",path+".interrupt")){
                queue.Enqueue("active with guidance");await Until(()=>interruptApi.Calls.Count==1);await queue.SteerAsync("accepted guide");await queue.InterruptAsync("explicit interrupt");await Until(()=>!queue.IsRunning && !session.IsBusy);
                var request=interruptApi.Requests.Single(r=>r.Last().Content=="explicit interrupt");Check(request.Any(m=>m.Content=="active with guidance")&&request.Any(m=>m.Content.Contains("未完成")),"an immediate guidance receipt does not hide the active partial reply from explicit interrupt");
            }
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static async Task GuidanceReliability()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"guidance-reliability-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var protection=new GuidanceProtection();SecretProtection.Configure(protection);
            var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=root,ReadReplies=false};cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="fixture";
            var model=new GuidanceApi();bool editRetained=false,attachmentRecovered=false;
            using(var session=new ConversationSession(model,new Speech(),()=>null,new HistoryStore(Path.Combine(root,"history"))))
            using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",Path.Combine(root,"queue.json"))){
                var work=session.SendTextAsync("original task",cfg,"fixture");await model.Started.Task;queue.Hold();
                await queue.SteerAsync("race-old",attachments:new[]{ChatAttachment.FromText("brief.txt","UNIQUE-GUIDANCE-ATTACHMENT")});string id=queue.Items.Single().Id;
                // .NET may run the awaited boundary inline; release it on another thread
                // so this test can reach the storage latch and unblock the protector.
                protection.Block=true;var boundary=Task.Run(()=>model.Boundary.SetResult(true));
                if(!protection.Entered.WaitOne(5000))throw new Exception("checkpoint did not reach the controlled storage boundary");
                try{queue.Edit(id,"race-new");}finally{protection.Release.Set();}
                await boundary;await model.Applied.Task;editRetained=queue.Items.Any(m=>m.Id==id && m.Text=="race-new");
                await session.StopAsync();try{await work;}catch(OperationCanceledException){}
                // Leave the revised pending guide for a later task; this resume must
                // recover the already accepted attachment from its task checkpoint.
                queue.Dispose();model.Resume=true;await session.ResumeAgentTaskAsync(cfg,"fixture");
                attachmentRecovered=model.Last.SelectMany(m=>m.Attachments??new List<ChatAttachment>()).Any(a=>a.Text=="UNIQUE-GUIDANCE-ATTACHMENT");
            }
            Check(editRetained,"an edit made while guidance is saving survives acknowledgement of the older revision");
            Check(attachmentRecovered,"an accepted guidance attachment survives stop and explicit task continuation");
            var restarted=new GuidanceApi{Resume=true};using(var session=new ConversationSession(restarted,new Speech(),()=>null,new HistoryStore(Path.Combine(root,"history")))){
                session.LoadAgentTask(cfg);
                var reloaded=session.CurrentAgentTask;
                Check(reloaded!=null && reloaded.Guidance!=null && reloaded.Guidance.Count==1,"the accepted guidance survives restart");
                Check(reloaded.Guidance[0].Attachments!=null && reloaded.Guidance[0].Attachments.Count==1 && reloaded.Guidance[0].Attachments[0].Text=="UNIQUE-GUIDANCE-ATTACHMENT","guidance attachments reload from the on-disk task checkpoint");
                await session.ResumeAgentTaskAsync(cfg,"fixture");Check(restarted.Last.SelectMany(m=>m.Attachments??new List<ChatAttachment>()).Any(a=>a.Text=="UNIQUE-GUIDANCE-ATTACHMENT"),"restart and explicit resume supply persisted guidance attachment to the model");
            }
            var changedProject=new GuidanceApi{Resume=true};using(var session=new ConversationSession(changedProject,new Speech(),()=>null))using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",Path.Combine(root,"queue.json"))){
                cfg.AgentWorkspace=Path.Combine(root,"other-project");Directory.CreateDirectory(cfg.AgentWorkspace);queue.Resume();await Until(()=>!queue.IsRunning);
                Check(changedProject.Last==null && queue.IsPaused && queue.Items.Single().Text=="race-new","guidance bound to another project stays queued without a model request");
            }
        }
        static async Task GuidanceSaveFailure()
        {
            foreach(bool lockTask in new[]{true,false}){
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"guide-save-failure-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var history=new HistoryStore(Path.Combine(root,"history"));var model=new SideEffectApi{Output=Path.Combine(root,"effects.txt")};var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=root,ReadReplies=false};cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="fixture";
                using(var session=new ConversationSession(model,new Speech(),()=>null,history))using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",Path.Combine(root,"queue.json"))){
                    queue.Enqueue("original task");await model.Started.Task;await session.FlushHistoryAsync();string file=Directory.GetFiles(lockTask?Path.Combine(root,"history","agent-tasks"):Path.Combine(root,"history"),lockTask?"*.task":"*.history").Single();
                    using(var held=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){
                        await queue.SteerAsync("append one effect");model.Boundary.SetResult(true);await Until(()=>!session.IsBusy && !queue.IsRunning);
                        Check(model.Calls==1 && File.ReadAllLines(model.Output).Length==1 && queue.Items.Count==1 && queue.IsPaused,"save failure pauses queued accepted guidance without replaying its side effect: "+lockTask);
                        Check((lockTask?session.AgentTaskWarning:session.HistoryWarning).Length>0,"real file-sharing failure remains visible until persistence succeeds: "+lockTask);
                    }
                    await history.SaveAsync("",session.History.Messages);await session.RetryAgentTaskSaveAsync();queue.Resume();await Until(()=>!queue.IsRunning);
                    Check(queue.Items.Count==0 && model.Calls==1 && File.ReadAllLines(model.Output).Length==1,"successful persistence confirms completed guidance without another model request: "+lockTask);
                }
            }
        }
        sealed class SideEffectApi:GuidanceApi
        {
            public int Calls;public string Output;
            public override async Task<string> ChatAgentAsync(ConnectionSettings c,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string>d,Action<string>r,Action<AgentToolEvent>t,bool stream,CancellationToken cancel){Calls++;if(Calls==1){Started.TrySetResult(true);await Boundary.Task;var guides=await workspace.TakeGuidanceAsync(cancel);if(guides.Count==0)throw new Exception("fixture guide was not admitted");}File.AppendAllText(Output,"effect\n");return "done";}
        }
        sealed class GuidanceProtection:ISecretProtector
        {
            public bool Block;public readonly ManualResetEvent Entered=new ManualResetEvent(false),Release=new ManualResetEvent(false);
            public byte[] Protect(byte[] value){string text=System.Text.Encoding.UTF8.GetString(value);if(Block && text.Contains("\"Guidance\"") && text.Contains("race-old")){Entered.Set();if(!Release.WaitOne(10000))throw new TimeoutException("controlled checkpoint latch was not released");}return value;}
            public byte[] Unprotect(byte[] value){return value;}
        }
        class GuidanceApi:IConversationApi,IAgentConversationApi
        {
            public readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(),Boundary=new TaskCompletionSource<bool>(),Applied=new TaskCompletionSource<bool>();public bool Resume;public IList<ChatMessage> Last;
            public virtual async Task<string> ChatAgentAsync(ConnectionSettings c,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string>d,Action<string>r,Action<AgentToolEvent>t,bool stream,CancellationToken cancel){Last=messages;if(Resume)return "done";Started.TrySetResult(true);await Boundary.Task;await workspace.TakeGuidanceAsync(cancel);Applied.TrySetResult(true);await Task.Delay(Timeout.Infinite,cancel);return "done";}
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage>m,CancellationToken t){throw new NotSupportedException();}public Task<string> TranscribeAsync(ConnectionSettings c,byte[]b,CancellationToken t){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string s,CancellationToken t){throw new NotSupportedException();}public void Dispose(){}
        }
        static async Task Until(Func<bool> condition){for(int i=0;i<300;i++){if(condition())return;await Task.Delay(10);}throw new Exception("Timed out");}
        sealed class Api:IConversationApi,IStreamingConversationApi
        {
            public readonly List<string> Calls=new List<string>();public readonly List<IList<ChatMessage>> Requests=new List<IList<ChatMessage>>();public int Maximum;int active;public bool Fail;public string WaitForCancellation;public TaskCompletionSource<bool> CancelRelease,Release;
            public async Task<string> ChatStreamAsync(ConnectionSettings c,IList<ChatMessage> m,Action<string> delta,CancellationToken token)
            {int count=Interlocked.Increment(ref active);Maximum=Math.Max(Maximum,count);Calls.Add(m.Last().Content);Requests.Add(m);try{delta("partial");if(Release!=null)await Release.Task;bool cancelled=false;try{await Task.Delay(m.Last().Content==WaitForCancellation?Timeout.Infinite:120,token);}catch(OperationCanceledException){cancelled=true;}if(cancelled){if(CancelRelease!=null)await CancelRelease.Task;token.ThrowIfCancellationRequested();}if(Fail)throw new ApiException("fixture failure");return "partial done";}finally{Interlocked.Decrement(ref active);}}
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> m,CancellationToken t){return ChatStreamAsync(c,m,_=>{},t);}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] b,CancellationToken t){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string s,CancellationToken t){throw new NotSupportedException();}public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string s,PetSettings p,CancellationToken t){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int v){}public void Dispose(){}}
    }
}
