using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ImageGenerationIntegrationTests
    {
        static int checks;
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        [STAThread] public static int Main()
        {
            try
            {
                Check(typeof(ChatMessage).GetProperty("GeneratedImages")!=null,"assistant messages preserve generated image asset references");
                Check(typeof(ConversationSession).GetProperty("ImageStore")!=null,"session owns an injected image asset store");
                Check(typeof(ConversationSession).GetProperty("QuestionProvider")!=null,"session exposes its question provider");
                Check(typeof(ConversationSession).GetProperty("ImageTasks")!=null,"AllEntriesShareOneQueue: session accepts application manager");
                Lifecycle().GetAwaiter().GetResult();
                Console.WriteLine("ImageGenerationIntegrationTests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static async Task<ImageGenerationTaskManager> Manager(string root,GeneratedImageStore assets,ImageGenerationClient client)
        {var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(root),assets,client,new ImageAssetUsageRegistry());await manager.RestoreAsync(CancellationToken.None);return manager;}
        static readonly byte[] picture=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
        static async Task Lifecycle()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","image-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string savedId;
            using(var assets=new GeneratedImageStore(root))
            {
                var image=assets.Save(picture,new GeneratedImage{Prompt="a blue cup",Model="custom-image-model"},true);savedId=image.Id;var log=new ConversationLog();ChatMessage user;var reply=log.BeginTurn("draw a cup","text",out user);log.AttachGeneratedImage(reply.Id,image);log.Update(reply.Id,"图片已生成。","complete");image.Prompt="outside mutation";
                Check(log.Messages.Last().GeneratedImages[0].Prompt=="a blue cup","message owns a snapshot of image metadata");var copy=log.Messages.Last();copy.GeneratedImages[0].Prompt="copy mutation";Check(log.Messages.Last().GeneratedImages[0].Prompt=="a blue cup","snapshot reads cannot mutate stored image reference");
                log.AttachGeneratedImage(reply.Id,assets.Get(savedId));Check(log.Messages.Last().GeneratedImages.Count==1,"same completed asset is not attached twice");var history=new HistoryStore(Path.Combine(root,"history"));await history.SaveAsync("pet",log.Messages);var restored=history.Load("pet");Check(restored.Last().GeneratedImages[0].Id==savedId,"encrypted history restores generated asset ID");
                byte[] plain=SecretProtection.Unprotect(File.ReadAllBytes(Directory.GetFiles(history.DirectoryPath,"*.history").Single()));Check(!Encoding.UTF8.GetString(plain).Contains(Convert.ToBase64String(picture)),"history contains no encoded image bytes");
                string export=Path.Combine(root,"user-export.png");File.WriteAllBytes(export,assets.Read(savedId));var temp=assets.Save(picture,new GeneratedImage{Prompt="temporary"},false);string temporaryPath=assets.PathFor(temp.Id);assets.Dispose();Check(!File.Exists(temporaryPath)&&File.Exists(export),"temporary cleanup preserves user exported original");
            }
            using(var assets=new GeneratedImageStore(root)){Check(assets.Read(savedId).SequenceEqual(picture),"restart resolves saved original bytes");File.Delete(assets.PathFor(savedId));var restored=new HistoryStore(Path.Combine(root,"history")).Load("pet");Check(restored.Count==2&&restored.Last().GeneratedImages[0].Id==savedId,"missing image does not invalidate the whole chat history");}
            await AgentBinding(root);
            await QuestionBinding(root);
            await ServiceRejection(root);
            await QueueOwnershipAndDelivery(root);
            await PreflightFailure(root);
            await RepairOwnership(root,true);
            await RepairOwnership(root,false);
            WorkspaceImageOwners(root);
        }
        sealed class HeldHandler:HttpMessageHandler
        {
            internal HttpStatusCode Status=HttpStatusCode.OK;internal int Calls;
            internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>();internal readonly TaskCompletionSource<bool> Release=new TaskCompletionSource<bool>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){Interlocked.Increment(ref Calls);Entered.TrySetResult(true);using(cancel.Register(()=>Release.TrySetCanceled()))await Release.Task;cancel.ThrowIfCancellationRequested();return new HttpResponseMessage(Status){Content=new StringContent("{\"data\":[{\"b64_json\":\""+Convert.ToBase64String(picture)+"\"}]}")};}
        }
        sealed class ImageApi:IConversationApi,IAgentConversationApi
        {
            internal string Tool="generate_image",Arguments="{\"prompt\":\"a test image\"}",LastResult;internal bool ExpectError,NeedsReconciliation,SkipTool;
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){return Task.FromResult("plain");}
            public async Task<string> ChatAgentAsync(ConnectionSettings settings,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tools,bool stream,CancellationToken cancel){if(SkipTool)return "continued without image";LastResult=await workspace.ExecuteAsync(Tool,Arguments,"integration-call",cancel);NeedsReconciliation=workspace.NeedsReconciliation;if(LastResult.StartsWith("错误：")&&!ExpectError)throw new ApiException(LastResult);return ExpectError?LastResult:"图片已生成。";}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wave,CancellationToken cancel){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}public void Dispose(){}
        }
        sealed class SilentSpeech:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
        static async Task AgentBinding(string root)
        {
            string workspace=Path.Combine(root,"workspace");Directory.CreateDirectory(workspace);var cfg=new PetSettings{AgentEnabled=true,AgentApprovalMode=AgentApprovalMode.Full,AgentWorkspace=workspace,ReadReplies=false,ShowPredictedMessages=false};cfg.Text.BaseUrl="https://unused.invalid/v1";cfg.Text.ApiKey="fixture-key";cfg.Text.Model="test-chat";cfg.Text.ToolSupport=CapabilitySupport.Supported;
            var provider=new ImageGenerationProvider{Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="test-image"}};cfg.ImageProviders=new List<ImageGenerationProvider>{provider};cfg.ActiveImageProviderId=provider.Id;
            bool persist=false;using(var assets=new GeneratedImageStore(Path.Combine(root,"agent")))using(var handler=new HeldHandler())using(var client=new ImageGenerationClient(assets,handler,null))using(var questions=new AgentQuestionController())using(var origin=new ConversationSession(new ImageApi(),new SilentSpeech(),()=>null,new HistoryStore(Path.Combine(root,"agent-history")),()=>persist))using(var other=new ConversationSession(new ImageApi(),new SilentSpeech(),()=>null))
            {
                origin.SelectPersona("original");origin.ConfigureKernelSession(new AgentSessionModel("original-session","project","conversation"),null);origin.ImageStore=assets;origin.ImageClient=client;origin.ImageTasks=await Manager(root,assets,client);origin.QuestionProvider=questions;other.SelectPersona("other");var sending=origin.SendTextAsync("draw",cfg,"pet");var arrived=await Task.WhenAny(handler.Entered.Task,sending,Task.Delay(10000));if(arrived!=handler.Entered.Task){await sending;throw new Exception("Image request never started.");}
                string originalReply=origin.History.Messages.Last().Id;Check(origin.History.Messages.Last().ImageGenerationTaskIds.Count==1,"SteeringKeepsOriginalPendingSlot: ID attached before HTTP");origin.RegisterGuidance("original","guide-during-image","Use softer lighting",null);persist=true;cfg.ImageProviders[0].Connection.Model="changed-while-waiting";handler.Release.TrySetResult(true);await sending;Check(origin.History.Messages.Any(m=>m.Id==originalReply),"steering cannot remove original in-flight image reply");var reply=origin.History.Messages.Single(m=>m.Id==originalReply);Check(reply.GeneratedImages!=null&&reply.GeneratedImages.Count==1,"agent tool result attaches a real image reference");var image=reply.GeneratedImages[0];Check(image.MessageId==reply.Id&&image.SessionId=="original-session"&&image.Model=="test-image","in-flight request retains original message/session and provider snapshot");Check(origin.ImageTasks.Get(image.ImageJobId).Persistent,"EnablingHistoryPromotesTaskAndReferenceBeforeHistoryWrite: task promoted");Check(!assets.Get(image.Id).Temporary,"enabling history promotes in-flight temporary result before persisting reference");Check(other.History.Messages.Count==0,"completion cannot be inserted into another active chat");Check(!reply.Content.Contains(".png")&&!reply.Content.Contains("generated-images"),"assistant result is an image instead of a filesystem filename");
                persist=false;var temporary=assets.Save(picture,new GeneratedImage{Prompt="created before history is enabled"},false);ChatMessage priorUser;var priorReply=origin.History.BeginTurn("temporary draw","image",out priorUser);origin.History.AttachGeneratedImage(priorReply.Id,temporary);origin.History.Update(priorReply.Id,"temporary image","complete");provider.SupportsEdits=true;var reference=assets.SaveReference(picture,new GeneratedImage{Prompt="temporary reference"},false);var temporaryHandle=await origin.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest{Provider=provider,Prompt="referenced",ReferenceImageId=reference.Id,Persistent=false},new ImageTaskOrigin{Source=ImageGenerationSource.Workspace}),CancellationToken.None);await temporaryHandle.Completion;origin.History.AttachImageTask(priorReply.Id,temporaryHandle.Id);origin.History.AttachImageTask(priorReply.Id,"aged-out-task");persist=true;cfg.AgentEnabled=false;await origin.SendTextAsync("now save history",cfg,"pet");await origin.FlushHistoryAsync();Check(!assets.Get(temporary.Id).Temporary,"later ordinary history save promotes older temporary image references");Check(String.IsNullOrEmpty(origin.HistoryWarning),"retained history remains savable after terminal task journal ages out");Check(origin.ImageTasks.Get(temporaryHandle.Id).Persistent&&!assets.Get(reference.Id).Temporary,"EnablingHistoryPromotesTaskAndReferenceBeforeHistoryWrite: ordinary Save promotes task and reference");
            }
        }
        static async Task QueueOwnershipAndDelivery(string root)
        {
            foreach(string mode in new[]{"single","stop","delivery","journal"})
            {
                var path=Path.Combine(root,mode);var cfg=AgentSettings(Path.Combine(path,"workspace"));var provider=new ImageGenerationProvider{Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="image"}};cfg.ImageProviders=new List<ImageGenerationProvider>{provider};cfg.ActiveImageProviderId=provider.Id;
                var api=new ImageApi{ExpectError=mode=="single"};var history=new HistoryStore(Path.Combine(path,"history"));
                using(var assets=new GeneratedImageStore(path))using(var handler=new HeldHandler())using(var client=new ImageGenerationClient(assets,handler,null))using(var session=new ConversationSession(api,new SilentSpeech(),()=>null,history,()=>mode=="delivery"||mode=="journal"))
                {
                    session.ImageStore=assets;session.ImageClient=client;session.ImageTasks=await Manager(path,assets,client);session.SelectPersona("pet");
                    var sending=session.SendTextAsync("draw",cfg,"pet");var reached=await Task.WhenAny(handler.Entered.Task,sending,Task.Delay(5000));if(reached!=handler.Entered.Task){await sending;throw new Exception("HTTP not entered");}
                    var job=session.ImageTasks.List().Single();
                    if(mode=="journal")
                    {
                        string journal=Path.Combine(path,"generated-images","tasks.dat");File.Delete(journal);Directory.CreateDirectory(journal);handler.Release.TrySetResult(true);await sending;
                        var saved=session.ImageTasks.Get(job.Id);Check(saved.State==ImageGenerationTaskState.Succeeded&&api.LastResult.Contains(saved.ResultImageId)&&handler.Calls==1,"delivery journal failure still returns saved receipt without regeneration");Directory.Delete(journal);
                    }
                    else if(mode=="delivery")
                    {
                        Check(history.Load("pet").Last().ImageGenerationTaskIds.Contains(job.Id),"pending original ID durably persisted before HTTP");
                        string historyFile=Directory.GetFiles(history.DirectoryPath,"*.history").Single();File.Delete(historyFile);Directory.CreateDirectory(historyFile);handler.Release.TrySetResult(true);await sending;
                        var failed=session.ImageTasks.Get(job.Id);Check(failed.State==ImageGenerationTaskState.Succeeded&&failed.DeliveryState==ImageTaskDeliveryState.Failed,"asset success separate from delivery failure");
                        Check(api.LastResult.Contains(failed.ResultImageId)&&!api.NeedsReconciliation,"delivery failure returns known saved asset receipt");
                        Directory.Delete(historyFile);await session.RepairImageDeliveryAsync(job.Id,CancellationToken.None);Check(handler.Calls==1&&session.ImageTasks.Get(job.Id).DeliveryState==ImageTaskDeliveryState.Delivered,"DeliveryFailureRetryNeverCallsImageService");
                        session.Clear();await session.RepairImageDeliveryAsync(job.Id,CancellationToken.None);Check(session.ImageTasks.Get(job.Id).DeliveryState==ImageTaskDeliveryState.Detached&&session.History.Messages.Count==0,"repair missing original message detaches without new turn");
                    }
                    else if(mode=="single")
                    {
                        await session.ImageTasks.CancelAsync(job.Id);await sending;Check(api.LastResult.Contains("取消")&&!api.NeedsReconciliation,"CancelSingleImageReturnsReadableToolResult");
                    }
                    else
                    {
                        var standalone=await session.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest{Provider=provider,Prompt="workspace",Persistent=false},new ImageTaskOrigin{Source=ImageGenerationSource.Workspace}),CancellationToken.None);
                        var settings=await session.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest{Provider=provider,Prompt="settings",Persistent=false},new ImageTaskOrigin{Source=ImageGenerationSource.SettingsTest}),CancellationToken.None);
                        Check(session.ImageTasks.List().Count==3&&handler.Calls==1,"AllEntriesShareOneQueue");
                        await session.StopAsync();try{await sending;}catch(OperationCanceledException){}
                        Check(session.ImageTasks.Get(standalone.Id).State!=ImageGenerationTaskState.Canceled&&session.ImageTasks.Get(settings.Id).State!=ImageGenerationTaskState.Canceled,"StopAgentCancelsItsJobsOnly");
                        Check(session.ImageTasks.Get(settings.Id)!=null,"ViewCloseKeepsTaskAccessible: manager retains independent entry");
                        await session.ImageTasks.CancelProviderAsync(provider.Id);try{await standalone.Completion;}catch{}try{await settings.Completion;}catch{}
                        Check(session.ImageTasks.List().All(t=>t.IsTerminal),"provider deletion explicitly cancels unfinished jobs");
                    }
                    await session.ImageTasks.StopAsync(CancellationToken.None);
                }
            }
        }
        static void WorkspaceImageOwners(string root)
        {
            var project=new ProjectDefinition{Id="image-project",Name="image project",Workspace=root};var definition=new ConversationDefinition{Id="stable-image-workspace",Name="image workspace"};var other=new ConversationDefinition{Id="other-image-workspace",Name="other workspace"};var cfg=AgentSettings(Path.Combine(root,"identity-workspace"));
            using(var first=ConversationWorkspace.Create(project,definition,root,"pet",()=>new ImageApi(),()=>new SilentSpeech(),()=>null,()=>cfg,()=>true))
            using(var restored=ConversationWorkspace.Create(project,definition,root,"pet",()=>new ImageApi(),()=>new SilentSpeech(),()=>null,()=>cfg,()=>true))
            using(var foreign=ConversationWorkspace.Create(project,other,root,"pet",()=>new ImageApi(),()=>new SilentSpeech(),()=>null,()=>cfg,()=>true))
            {Check(first.Session.ImageConversationId==definition.Id&&first.Session.ImageConversationId==restored.Session.ImageConversationId&&first.Session.ImageConversationId!=foreign.Session.ImageConversationId,"workspace creation binds stable distinct logical image owners");}
        }
        static async Task RepairOwnership(string root,bool restart)
        {
            string path=Path.Combine(root,restart?"repair-restart":"repair-foreign"),historyPath=Path.Combine(path,"history");var cfg=AgentSettings(Path.Combine(path,"workspace"));var provider=new ImageGenerationProvider{Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="image"}};cfg.ImageProviders=new List<ImageGenerationProvider>{provider};cfg.ActiveImageProviderId=provider.Id;
            string jobId,replyId,oldKernel;int initialCalls;
            using(var assets=new GeneratedImageStore(path))using(var handler=new HeldHandler())using(var client=new ImageGenerationClient(assets,handler,null))using(var session=new ConversationSession(new ImageApi(),new SilentSpeech(),()=>null,new HistoryStore(historyPath),()=>true,restart?null:"foreign-workspace"))
            {
                session.ImageStore=assets;session.ImageClient=client;session.ImageTasks=await Manager(path,assets,client);session.SelectPersona("pet");
                if(!restart)session.ConfigureKernelSession(new AgentSessionModel("workspace-kernel","project","foreign-workspace"),null);
                var sending=session.SendTextAsync("draw",cfg,"pet");var reached=await Task.WhenAny(handler.Entered.Task,sending,Task.Delay(5000));if(reached!=handler.Entered.Task){await sending;throw new Exception("HTTP not entered");}
                var job=session.ImageTasks.List().Single();jobId=job.Id;replyId=job.Origin.MessageId;oldKernel=session.KernelSession.ConversationId;
                await session.FlushHistoryAsync();string historyFile=Directory.GetFiles(historyPath,"*.history").Single();byte[] originalHistory=File.ReadAllBytes(historyFile);File.Move(historyFile,historyFile+".backup");Directory.CreateDirectory(historyFile);handler.Release.TrySetResult(true);await sending;await session.FlushHistoryAsync();
                Check(session.ImageTasks.Get(jobId).DeliveryState==ImageTaskDeliveryState.Failed,"repair ownership fixture has failed delivery");
                Directory.Delete(historyFile);File.Move(historyFile+".backup",historyFile);initialCalls=handler.Calls;
                if(!restart)
                {
                    string journal=Path.Combine(path,"generated-images","tasks.dat");byte[] before=File.ReadAllBytes(journal);string originalMessages=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(session.History.Messages);
                    using(var foreign=new ConversationSession(new ImageApi(),new SilentSpeech(),()=>null))
                    {
                        foreign.ImageTasks=session.ImageTasks;foreign.ImageStore=assets;foreign.ImageClient=client;bool rejected=false;try{await foreign.RepairImageDeliveryAsync(jobId,CancellationToken.None);}catch(InvalidOperationException){rejected=true;}
                        Check(rejected,"FreshUnboundForeignRepairRejected");Check(foreign.History.Messages.Count==0&&foreign.KernelSession==null,"foreign repair does not load or create history/kernel");
                    }
                    Check(before.SequenceEqual(File.ReadAllBytes(journal))&&session.ImageTasks.Get(jobId).DeliveryState==ImageTaskDeliveryState.Failed,"foreign repair preserves task journal and original delivery state");
                    Check(originalHistory.SequenceEqual(File.ReadAllBytes(historyFile))&&originalMessages==new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(session.History.Messages),"foreign repair preserves original disk and memory history");return;
                }
            }
            using(var assets=new GeneratedImageStore(path))using(var handler=new HeldHandler())using(var client=new ImageGenerationClient(assets,handler,null))using(var restored=new ConversationSession(new ImageApi{SkipTool=true},new SilentSpeech(),()=>null,new HistoryStore(historyPath),()=>true))
            {
                restored.ImageStore=assets;restored.ImageClient=client;restored.ImageTasks=await Manager(path,assets,client);restored.SelectPersona("pet");Check(restored.History.Messages.Any(m=>m.Id==replyId),"restart loads original persisted pending message");await restored.SendTextAsync("a new Agent turn",cfg,"pet");Check(restored.KernelSession!=null&&restored.KernelSession.ConversationId!=oldKernel,"restart creates new transient Agent kernel");
                Check(restored.ImageTasks.Get(jobId).Origin.ConversationId==restored.ImageConversationId,"standalone logical owner survives kernel replacement");await restored.RepairImageDeliveryAsync(jobId,CancellationToken.None);var job=restored.ImageTasks.Get(jobId);var original=restored.History.Messages.Single(m=>m.Id==replyId);
                Check(job.DeliveryState==ImageTaskDeliveryState.Delivered&&original.GeneratedImages.Count==1&&original.GeneratedImages[0].Id==job.ResultImageId,"StandaloneRestartAfterAgentTurnRepairsOriginalMessage");
                Check(initialCalls==1&&handler.Calls==0,"restart repair issues no additional image HTTP");
            }
        }
        static async Task PreflightFailure(string root)
        {
            var path=Path.Combine(root,"preflight");var cfg=AgentSettings(Path.Combine(path,"workspace"));var provider=new ImageGenerationProvider{Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="image"}};cfg.ImageProviders=new List<ImageGenerationProvider>{provider};cfg.ActiveImageProviderId=provider.Id;
            string blocked=Path.Combine(path,"blocked-history");File.WriteAllText(blocked,"not a directory");
            using(var assets=new GeneratedImageStore(path))using(var handler=new HeldHandler())using(var client=new ImageGenerationClient(assets,handler,null))using(var session=new ConversationSession(new ImageApi(),new SilentSpeech(),()=>null,new HistoryStore(blocked),()=>true))
            {
                session.ImageStore=assets;session.ImageClient=client;session.ImageTasks=await Manager(path,assets,client);try{await session.SendTextAsync("draw",cfg,"pet");}catch(ApiException){}
                Check(handler.Calls==0&&session.ImageTasks.List().Single().State==ImageGenerationTaskState.Failed,"pending history failure prevents every HTTP call");
            }
        }
        static PetSettings AgentSettings(string path)
        {
            Directory.CreateDirectory(path);var cfg=new PetSettings{AgentEnabled=true,AgentApprovalMode=AgentApprovalMode.Full,AgentWorkspace=path,ReadReplies=false,ShowPredictedMessages=false};cfg.Text.BaseUrl="https://unused.invalid/v1";cfg.Text.Model="test-chat";cfg.Text.ApiKey="fixture-key";cfg.Text.ToolSupport=CapabilitySupport.Supported;return cfg;
        }
        static async Task QuestionBinding(string root)
        {
            var cfg=AgentSettings(Path.Combine(root,"question-workspace"));var api=new ImageApi{Tool="ask_user",Arguments="{\"question\":\"Choose platform\",\"options\":[\"Windows\",\"Mac\"]}"};
            using(var questions=new AgentQuestionController())using(var session=new ConversationSession(api,new SilentSpeech(),()=>null))
            {
                session.QuestionProvider=questions;session.SelectPersona("pet");var asked=new TaskCompletionSource<bool>();questions.Changed+=delegate{if(questions.Requests.Count>0)asked.TrySetResult(true);};var sending=session.SendTextAsync("ask before proceeding",cfg,"pet");var reached=await Task.WhenAny(asked.Task,sending,Task.Delay(5000));if(reached!=asked.Task){await sending;throw new Exception("Question tool did not wait.");}var question=questions.Requests.Single();Check(session.KernelSession!=null&&question.SessionId==session.KernelSession.SessionId,"legacy entry binds actual ask_user tool to stable session identity");Check(questions.Submit(question.SessionId,question.TaskId,question.CallId,new AgentQuestionAnswer{OptionIndex=1}),"actual tool question accepts answer on original call");await sending;Check(api.LastResult.Contains("Mac")&&questions.Requests.Count==0,"actual ask_user tool resumes and clears pending request");
            }
        }
        static async Task ServiceRejection(string root)
        {
            var cfg=AgentSettings(Path.Combine(root,"rejection-workspace"));var provider=new ImageGenerationProvider{Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="test-image"}};cfg.ImageProviders=new List<ImageGenerationProvider>{provider};cfg.ActiveImageProviderId=provider.Id;var api=new ImageApi{ExpectError=true};
            using(var assets=new GeneratedImageStore(Path.Combine(root,"rejections")))using(var handler=new HeldHandler{Status=HttpStatusCode.Forbidden})using(var client=new ImageGenerationClient(assets,handler,null))using(var session=new ConversationSession(api,new SilentSpeech(),()=>null))
            {
                session.ImageClient=client;session.ImageTasks=await Manager(Path.Combine(root,Guid.NewGuid().ToString("N")),assets,client);session.ImageStore=assets;handler.Release.TrySetResult(true);await session.SendTextAsync("draw",cfg,"pet");Check(api.LastResult.Contains("403")&&api.LastResult.Contains("鉴权")&&!api.NeedsReconciliation,"explicit service rejection remains readable and completes failed journal state");Check(assets.List().Count==0,"rejected generation creates no fake gallery asset");
            }
            provider.TimeoutSeconds=1;var timeoutApi=new ImageApi();using(var assets=new GeneratedImageStore(Path.Combine(root,"timeouts")))using(var handler=new HeldHandler())using(var client=new ImageGenerationClient(assets,handler,null))using(var session=new ConversationSession(timeoutApi,new SilentSpeech(),()=>null))
            {
                session.ImageClient=client;session.ImageTasks=await Manager(Path.Combine(root,Guid.NewGuid().ToString("N")),assets,client);session.ImageStore=assets;bool readable=false;try{await session.SendTextAsync("draw",cfg,"pet");}catch(ApiException error){readable=error.Message.Contains("超时")&&error.Message.Contains("不会自动重试");}Check(readable,"ambiguous image timeout preserves readable reason and no automatic retry");
            }
        }
    }
}
