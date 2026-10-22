using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ImageTaskCenterLifecycleTests
    {
        static int checks;static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Task center lifecycle: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
        static async Task Run()
        {
            await ConcurrentRepairsAndOpening();
            await ExistingHistoryConflict();
            await CloseFailureRetainsOwner();
            var trackerType=typeof(ImageGenerationTaskManager).Assembly.GetType("Dafeiyu.ImageTaskCompletionTracker");Check(trackerType!=null,"completion notification tracker available");
            var old=new ImageGenerationTaskRecord{Id="restored",State=ImageGenerationTaskState.Succeeded};var tracker=Activator.CreateInstance(trackerType,new object[]{new[]{new ImageGenerationTaskSnapshot(old)}});var observe=trackerType.GetMethod("Observe");
            Func<ImageGenerationTaskRecord,bool> publish=record=>(bool)observe.Invoke(tracker,new object[]{new ImageGenerationTaskSnapshot(record)});
            Check(!publish(old),"restored terminal does not notify");var fresh=new ImageGenerationTaskRecord{Id="fresh",State=ImageGenerationTaskState.Queued};Check(!publish(fresh),"queued task does not notify");fresh.State=ImageGenerationTaskState.Succeeded;Check(publish(fresh),"new completion notifies once");fresh.DeliveryState=ImageTaskDeliveryState.Failed;Check(!publish(fresh),"delivery-only update does not duplicate completion notice");fresh.State=ImageGenerationTaskState.Canceled;Check(!publish(fresh),"terminal-to-terminal does not notify");
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","task8-lifecycle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"settings.json");File.WriteAllText(path,"{}");var settings=new SettingsStore(path).Load();Check(settings.ImageGenerationConcurrency==1&&settings.ImageGenerationMotionEnabled,"legacy settings defaults");settings.ImageGenerationConcurrency=99;settings.Normalize();Check(settings.ImageGenerationConcurrency==4,"concurrency clamps maximum");settings.ImageGenerationConcurrency=-1;settings.Normalize();Check(settings.ImageGenerationConcurrency==1,"concurrency clamps minimum");settings.ImageGenerationConcurrency=3;settings.ImageGenerationMotionEnabled=false;new SettingsStore(path).Save(settings);Check(new SettingsStore(path).Load().Copy().ImageGenerationConcurrency==3&&!new SettingsStore(path).Load().Copy().ImageGenerationMotionEnabled,"settings copy roundtrip");
            var usage=new ImageAssetUsageRegistry();using(var store=new GeneratedImageStore(root,usage)){
                var executor=new Executor(store);var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(root),store,executor,usage);await manager.RestoreAsync(CancellationToken.None);var provider=new ImageGenerationProvider{Name="fixture",Connection=new ConnectionSettings{BaseUrl="https://unused.invalid",Model="fixture"}};
                var catalog=new ProjectCatalog();var conversation=new ConversationDefinition{Id="closed-owner",Name="existing"};catalog.UnassignedConversations.Add(conversation);string historyRoot=Path.Combine(root,"conversations",conversation.Id,"history");var log=new ConversationLog();ChatMessage user;var reply=log.BeginTurn("draw","text",out user);await new HistoryStore(historyRoot).SaveAsync("persona",log.Messages);
                using(var sequentialOwners=new ConversationWorkspaceManager(root,"persona",()=>new NoNetworkApi(),()=>new NoopSpeech(),()=>null,()=>settings,()=>true){ImageTasks=manager,ImageStore=store,ImageUsage=usage})
                {
                var h=await manager.EnqueueAsync(new ImageGenerationTaskSubmission("repair",new ImageGenerationRequest{Provider=provider,Prompt="closed existing history",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=conversation.Id,PersonaId="persona",MessageId=reply.Id}),CancellationToken.None);await h.Completion;await manager.SetDeliveryStateAsync(h.Id,ImageTaskDeliveryState.Failed);int before=executor.Calls;await RepairWithOwner(manager,store,usage,sequentialOwners,catalog,root,h.Id);Check(manager.Get(h.Id).DeliveryState==ImageTaskDeliveryState.Delivered&&new HistoryStore(historyRoot).Load("persona").Single(m=>m.Id==reply.Id).GeneratedImages.Count==1&&executor.Calls==before,"closed existing owner repairs without new request or catalog entry");
                await manager.SetDeliveryStateAsync(h.Id,ImageTaskDeliveryState.Failed);var unknown=h.Id;catalog.UnassignedConversations.Clear();bool denied=false;try{await RepairWithOwner(manager,store,usage,sequentialOwners,catalog,root,unknown);}catch(InvalidOperationException){denied=true;}Check(denied&&manager.Get(unknown).DeliveryState==ImageTaskDeliveryState.Failed,"missing owner never detaches another task");
                }
                var shutdown=typeof(ImageTaskCenterState).GetMethod("StopForExitAsync");Check(shutdown!=null,"bounded exit coordinator available");var slow=new SlowExecutor();var slowManager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(Path.Combine(root,"slow")),store,slow,usage);await slowManager.RestoreAsync(CancellationToken.None);var stuck=await slowManager.EnqueueAsync(new ImageGenerationTaskSubmission("slow",new ImageGenerationRequest{Provider=provider,Prompt="ignore cancellation",Persistent=true},new ImageTaskOrigin()),CancellationToken.None);await slow.Entered.Task;var watch=System.Diagnostics.Stopwatch.StartNew();await (Task)shutdown.Invoke(null,new object[]{slowManager});Check(watch.Elapsed.TotalSeconds<6.5,"true exit returns within five second budget for uncooperative request");slow.Release.TrySetCanceled();try{await stuck.Completion;}catch(OperationCanceledException){}
                var aggregate=typeof(ImageTaskCenterState).GetMethod("DrainForExitAsync");Check(aggregate!=null,"aggregate exit budget coordinator available");var later=new TaskCompletionSource<bool>();bool disposed=false;var aggregateWatch=System.Diagnostics.Stopwatch.StartNew();Func<Task> delayedDrain=async delegate{await Task.Delay(1200);await later.Task;disposed=true;};bool graceful=await (Task<bool>)aggregate.Invoke(null,new object[]{delayedDrain});Check(!graceful&&aggregateWatch.Elapsed.TotalSeconds<6.5&&!disposed,"later noncooperative history stage shares initial five second budget and keeps writers alive");later.TrySetResult(true);for(int i=0;i<100&&!disposed;i++)await Task.Delay(5);Check(disposed,"deferred resources drain after timeout when writer finishes");bool normalSaved=false;Func<Task> normal=async delegate{await Task.Delay(20);normalSaved=true;};Check(await (Task<bool>)aggregate.Invoke(null,new object[]{normal})&&normalSaved,"normal graceful exit awaits final save");
                using(var blockingRelease=new ManualResetEvent(false)){bool wasReleased=false;Func<Task> synchronous=delegate{blockingRelease.WaitOne();wasReleased=true;return Task.FromResult(0);};var invocation=Task.Run(async()=>await (Task<bool>)aggregate.Invoke(null,new object[]{synchronous}));var winner=await Task.WhenAny(invocation,Task.Delay(6200));bool bounded=winner==invocation&&!invocation.Result&&!wasReleased;blockingRelease.Set();await invocation;Check(bounded,"synchronous pre-await stage is off owner thread and bounded by original deadline");}
                var begin=typeof(ImageTaskCenterState).GetMethod("BeginExitDrain");Check(begin!=null,"owner receives pending drain before synchronous work starts");using(var held=new ManualResetEvent(false)){Func<Task> owned=delegate{held.WaitOne();return Task.FromResult(0);};var args=new object[]{owned,null};var budget=(Task<bool>)begin.Invoke(null,args);var tracked=(Task)args[1];Check(!tracked.IsCompleted,"owner tracks synchronous writer before first await");held.Set();Check(await budget,"tracked owner drain completes gracefully");await tracked;}
                await manager.StopAsync(CancellationToken.None);
            }
        }
        static async Task ConcurrentRepairsAndOpening()
        {
            var previous=(ISecretProtector)typeof(SecretProtection).GetField("current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var protector=new HeldHistoryCommit(previous);
            SecretProtection.Configure(protector);
            try
            {
                var concurrent=await RepairRace(protector,false);
                var opened=await RepairRace(protector,true);
                var legacy=await RepairRace(protector,true,true);
                var closed=await RepairRace(protector,true,false,true);
                var deleted=await RepairRace(protector,true,false,true,true);
                Console.WriteLine("Held repair diagnostics: concurrent images="+concurrent.Item1+", opened images="+opened.Item1+", opened new message="+opened.Item2);
                Check(concurrent.Item1==2,"simultaneous same-owner repairs preserve both delivered images");
                Check(opened.Item1==2&&opened.Item2,"opening owner during held repair commit preserves both images and new message");
                Check(legacy.Item1==2&&legacy.Item2,"legacy existing owner migrates once and shares repair/open history");
                Check(closed.Item1==2&&closed.Item2,"closing owner hands off both attachments and new user message truthfully");
                Check(deleted.Item1==1&&deleted.Item2,"deleted owner is revalidated after close handoff without new writer or false delivery");
            }
            finally{protector.Release.Set();SecretProtection.Configure(previous);}
        }
        static async Task<Tuple<int,bool>> RepairRace(HeldHistoryCommit protector,bool openWhileHeld,bool legacy=false,bool closeWhileHeld=false,bool deleteWhileHeld=false)
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","task8-repair-race-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var usage=new ImageAssetUsageRegistry();using(var assets=new GeneratedImageStore(root,usage))
            {
                var executor=new Executor(assets);var tasks=new ImageGenerationTaskManager(new ImageGenerationTaskStore(root),assets,executor,usage);await tasks.RestoreAsync(CancellationToken.None);
                var settings=new PetSettings{ReadReplies=false,AgentEnabled=false};var provider=new ImageGenerationProvider{Name="fixture",Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="fixture"}};
                var catalog=new ProjectCatalog();var definition=new ConversationDefinition{Id="repair-owner",Name="existing",PetId="persona",Mode="chat"};catalog.UnassignedConversations.Add(definition);var project=ProjectCatalogStore.UnassignedProject(catalog,settings);
                string historyPath=legacy?Path.Combine(root,"projects","old-project","chats",definition.Id,"history"):Path.Combine(root,"conversations",definition.Id,"history");var log=new ConversationLog();ChatMessage user;var reply=log.BeginTurn("held-repair-marker","text",out user);log.Update(reply.Id,"original reply","complete");await new HistoryStore(historyPath).SaveAsync("persona",log.Messages);
                var handles=new List<ImageGenerationTaskHandle>();foreach(var id in new[]{"a","b"}){var handle=await tasks.EnqueueAsync(new ImageGenerationTaskSubmission(id,new ImageGenerationRequest{Provider=provider,Prompt="fixture "+id,Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=definition.Id,PersonaId="persona",MessageId=reply.Id}),CancellationToken.None);await handle.Completion;await tasks.SetDeliveryStateAsync(handle.Id,ImageTaskDeliveryState.Failed);handles.Add(handle);}
                using(var owners=new ConversationWorkspaceManager(root,"persona",()=>new NoNetworkApi(),()=>new NoopSpeech(),()=>null,()=>settings,()=>true){ImageTasks=tasks,ImageStore=assets,ImageUsage=usage})
                {
                    protector.Reset();
                    var first=Task.Run(()=>RepairWithOwner(tasks,assets,usage,owners,catalog,root,handles[0].Id));
                    if(await Task.WhenAny(protector.Entered.Task,first,Task.Delay(5000))!=protector.Entered.Task){protector.Release.Set();await first;throw new Exception("history commit was not held");}
                    var closing=closeWhileHeld?owners.CloseWorkspaceAsync(definition.Id):Task.FromResult(0);
                    var second=Task.Run(()=>RepairWithOwner(tasks,assets,usage,owners,catalog,root,handles[1].Id));
                    await Task.WhenAny(second,Task.Delay(150));
                    ConversationWorkspace opened=null;
                    try
                    {
                        if(closeWhileHeld)
                        {
                            var method=typeof(ConversationWorkspaceManager).GetMethod("CreateWorkspaceAsync");
                            var reopening=deleteWhileHeld?owners.OpenCatalogWorkspaceAsync(catalog,definition.Id):method==null?Task.FromResult(owners.CreateWorkspace(project,definition)):(Task<ConversationWorkspace>)method.Invoke(owners,new object[]{project,definition,null});
                            await Task.Delay(150);
                            Check(!closing.IsCompleted&&!reopening.IsCompleted&&!second.IsCompleted,"closing canonical owner asynchronously holds reopen and second repair until commit drains");
                            bool busy=false;try{owners.CreateWorkspace(project,definition);}catch(InvalidOperationException){busy=true;}
                            Check(busy&&owners.OpenCount==0&&owners.AllOwnedWorkspaces.Count==1&&ReferenceEquals(closing,owners.CloseWorkspaceAsync(definition.Id)),"closing writer stays owned while sync open reports retryable busy and repeated close coalesces");
                            if(deleteWhileHeld)catalog.UnassignedConversations.Remove(definition);
                            protector.Release.Set();
                            if(deleteWhileHeld)
                            {
                                bool openDenied=false,repairDenied=false;
                                try{await reopening;}catch(InvalidOperationException){openDenied=true;}
                                try{await second;}catch(InvalidOperationException){repairDenied=true;}
                                await Task.WhenAll(first,closing);
                                var saved=new HistoryStore(historyPath).Load("persona");
                                Check(openDenied&&repairDenied&&owners.AllOwnedWorkspaces.Count==0&&executor.Calls==2&&tasks.Get(handles[0].Id).DeliveryState==ImageTaskDeliveryState.Delivered&&tasks.Get(handles[1].Id).DeliveryState==ImageTaskDeliveryState.Failed,"catalog deletion during close rejects waiting open and repair without new owner or HTTP");
                                await tasks.StopAsync(CancellationToken.None);return Tuple.Create(saved.Single(m=>m.Id==reply.Id).GeneratedImages.Count,true);
                            }
                            opened=await reopening;
                        }
                        else if(openWhileHeld)opened=owners.CreateWorkspace(project,definition);
                        if(opened!=null)opened.Session.AddLiveTurn("persona","new message while repair held","new reply");
                    }
                    finally{protector.Release.Set();}
                    await Task.WhenAll(first,second,closing);if(opened!=null)await opened.Session.FlushHistoryAsync();await owners.SaveAllAsync();
                    var finalHistory=Path.Combine(ConversationWorkspace.ResolveDataDirectory(root,definition.Id),"history");var messages=new HistoryStore(finalHistory).Load("persona");int images=messages.Single(m=>m.Id==reply.Id).GeneratedImages.Count;
                    Check(tasks.Get(handles[0].Id).DeliveryState==ImageTaskDeliveryState.Delivered&&tasks.Get(handles[1].Id).DeliveryState==ImageTaskDeliveryState.Delivered,"race fixture records both repaired tasks delivered");
                    Check(executor.Calls==2&&catalog.UnassignedConversations.Count==1,"repair/open race creates no request or catalog chat");
                    Check(owners.OpenCount==1&&ReferenceEquals(owners.CreateWorkspace(project,definition),owners.OpenWorkspaces.Single()),"repair and normal opening resolve one canonical owner");
                    await tasks.StopAsync(CancellationToken.None);return Tuple.Create(images,messages.Any(m=>m.Content=="new message while repair held"));
                }
            }
        }
        static async Task CloseFailureRetainsOwner()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","task8-close-failure-"+Guid.NewGuid().ToString("N"));var settings=new PetSettings();var project=new ProjectDefinition{Id="project",Name="existing"};var definition=new ConversationDefinition{Id="owner",Name="existing",PetId="persona",Mode="chat"};
            using(var owners=new ConversationWorkspaceManager(root,"persona",()=>new NoNetworkApi(),()=>new NoopSpeech(),()=>null,()=>settings,()=>true))
            {
                var workspace=owners.CreateWorkspace(project,definition);workspace.Session.AddLiveTurn("persona","initial","reply");await workspace.Session.FlushHistoryAsync();
                string path=Directory.GetFiles(Path.Combine(workspace.DataDirectory,"history"),"*.history").Single();File.Delete(path);Directory.CreateDirectory(path);
                workspace.Session.AddLiveTurn("persona","retained unsaved user message","reply");await workspace.Session.FlushHistoryAsync();bool rejected=false;
                try{await owners.CloseWorkspaceAsync(definition.Id);}catch(IOException){rejected=true;}
                Check(rejected&&owners.OpenCount==1&&ReferenceEquals(owners.CreateWorkspace(project,definition),workspace)&&workspace.Session.History.Messages.Any(m=>m.Content=="retained unsaved user message"),"failed close retains canonical live owner and unsaved message for retry");
                Directory.Delete(path);await owners.CloseWorkspaceAsync(definition.Id);var reopened=await OpenOwner(owners,project,definition);
                Check(!ReferenceEquals(reopened,workspace)&&reopened.Session.History.Messages.Any(m=>m.Content=="retained unsaved user message"),"corrected storage closes safely then reopens retained history");
            }
        }
        static Task<ConversationWorkspace> OpenOwner(ConversationWorkspaceManager owners,ProjectDefinition project,ConversationDefinition definition)
        {
            var method=typeof(ConversationWorkspaceManager).GetMethod("CreateWorkspaceAsync");return method==null?Task.FromResult(owners.CreateWorkspace(project,definition)):(Task<ConversationWorkspace>)method.Invoke(owners,new object[]{project,definition,null});
        }
        static async Task ExistingHistoryConflict()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","task8-owner-conflict-"+Guid.NewGuid().ToString("N"));var usage=new ImageAssetUsageRegistry();using(var assets=new GeneratedImageStore(root,usage))
            {
                var executor=new Executor(assets);var tasks=new ImageGenerationTaskManager(new ImageGenerationTaskStore(root),assets,executor,usage);await tasks.RestoreAsync(CancellationToken.None);var settings=new PetSettings();var catalog=new ProjectCatalog();var definition=new ConversationDefinition{Id="legacy-owner",Name="existing",PetId="persona"};catalog.UnassignedConversations.Add(definition);string legacy=Path.Combine(root,"projects","old","chats",definition.Id,"history");string modern=Path.Combine(root,"conversations",definition.Id,"history");Directory.CreateDirectory(modern);var log=new ConversationLog();ChatMessage user;var reply=log.BeginTurn("existing legacy history","text",out user);await new HistoryStore(legacy).SaveAsync("persona",log.Messages);var provider=new ImageGenerationProvider{Name="fixture",Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="fixture"}};var handle=await tasks.EnqueueAsync(new ImageGenerationTaskSubmission("conflict",new ImageGenerationRequest{Provider=provider,Prompt="fixture",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=definition.Id,PersonaId="persona",MessageId=reply.Id}),CancellationToken.None);await handle.Completion;await tasks.SetDeliveryStateAsync(handle.Id,ImageTaskDeliveryState.Failed);
                using(var owners=new ConversationWorkspaceManager(root,"persona",()=>new NoNetworkApi(),()=>new NoopSpeech(),()=>null,()=>settings,()=>true){ImageTasks=tasks,ImageStore=assets,ImageUsage=usage})
                {
                    bool denied=false;try{await RepairWithOwner(tasks,assets,usage,owners,catalog,root,handle.Id);}catch(InvalidOperationException){denied=true;}catch(IOException){denied=true;}
                    Check(denied&&owners.OpenCount==0&&tasks.Get(handle.Id).DeliveryState==ImageTaskDeliveryState.Failed&&Directory.GetFiles(legacy,"*.history").Length==1&&Directory.GetFiles(modern,"*.history").Length==0,"empty modern owner cannot hide existing legacy history or fabricate delivered state");
                }
                await tasks.StopAsync(CancellationToken.None);
            }
        }
        static Task RepairWithOwner(ImageGenerationTaskManager tasks,GeneratedImageStore assets,ImageAssetUsageRegistry usage,ConversationWorkspaceManager owners,ProjectCatalog catalog,string root,string id)
        {
            var method=typeof(ImageTaskCenterState).GetMethod("RepairExistingAsync");var args=new List<object>{tasks,assets,usage,new ConversationSession[0],catalog,root,id,true,CancellationToken.None};
            if(method.GetParameters().Length==10)args.Add(owners);
            return (Task)method.Invoke(null,args.ToArray());
        }
        sealed class HeldHistoryCommit:ISecretProtector
        {
            readonly ISecretProtector inner;int armed;
            internal TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>();internal readonly ManualResetEvent Release=new ManualResetEvent(true);
            internal HeldHistoryCommit(ISecretProtector original){inner=original;}
            internal void Reset(){Entered=new TaskCompletionSource<bool>();Release.Reset();Volatile.Write(ref armed,1);}
            public byte[] Protect(byte[] plain){string json=System.Text.Encoding.UTF8.GetString(plain);if(json.Contains("\"Messages\"")&&json.Contains("held-repair-marker")&&Interlocked.CompareExchange(ref armed,0,1)==1){Entered.TrySetResult(true);Release.WaitOne();}return inner.Protect(plain);}
            public byte[] Unprotect(byte[] cipher){return inner.Unprotect(cipher);}
        }
        sealed class NoNetworkApi:IConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){throw new Exception("repair must not call HTTP");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] bytes,CancellationToken token){throw new Exception("unexpected transcription");}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token){throw new Exception("unexpected speech");}
            public void Dispose(){}
        }
        sealed class NoopSpeech:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int value){}public void Stop(){}public void Dispose(){}}
        sealed class SlowExecutor:IImageGenerationExecutor{internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>();internal readonly TaskCompletionSource<GeneratedImage> Release=new TaskCompletionSource<GeneratedImage>();public Task<GeneratedImage> GenerateAsync(ImageGenerationRequest request,IProgress<ImageGenerationPhase> phase,CancellationToken token){Entered.TrySetResult(true);return Release.Task;}}
        sealed class Executor:IImageGenerationExecutor
        {
            readonly GeneratedImageStore store;internal int Calls;internal Executor(GeneratedImageStore images){store=images;}
            public Task<GeneratedImage> GenerateAsync(ImageGenerationRequest request,IProgress<ImageGenerationPhase> phase,CancellationToken token){Calls++;return Task.FromResult(store.Save(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII="),new GeneratedImage{ImageJobId=request.ImageJobId,Prompt=request.Prompt,Source=request.Source},request.Persistent));}
        }
    }
}
