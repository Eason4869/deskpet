using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu.Tests
{
    // Explicit paid acceptance only: bounded per-mode requests, no retries.
    public static class ImageWorkflowLiveTests
    {
        static Application app; static PetWindow pet; static string evidence; static BudgetHandler handler;
        static string metadataName="http-metadata.jsonl";
        static int checks;
        static void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("PASS "+label);}
        static IEnumerable<T> All<T>(DependencyObject p) where T:DependencyObject {if(p is T)yield return (T)p;for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++)foreach(var c in All<T>(VisualTreeHelper.GetChild(p,i)))yield return c;}
        static void Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static async Task Until(Func<bool> ready,string label){var clock=System.Diagnostics.Stopwatch.StartNew();while(!ready()&&clock.ElapsedMilliseconds<5000)await Task.Delay(10);Check(ready(),label);}
        static void Capture(Window window,string name){window.UpdateLayout();var frame=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);frame.Render(window);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));using(var file=File.Create(Path.Combine(evidence,name)))encoder.Save(file);}
        static string ExportFolder(){string folder=Path.Combine(evidence,"export");Directory.CreateDirectory(folder);return folder;}
        sealed class BudgetHandler:DelegatingHandler
        {
            readonly int maximum;
            internal int Requests;
            internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>(),Release=new TaskCompletionSource<bool>();
            internal BudgetHandler(int limit):base(new HttpClientHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){maximum=limit;}
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                Entered.TrySetResult(true);await Release.Task;token.ThrowIfCancellationRequested();
                if(Interlocked.Increment(ref Requests)>maximum)throw new InvalidOperationException("Live request budget exhausted.");
                Trace(new{kind="request",method=request.Method.Method,path=request.RequestUri.AbsolutePath,authorizationPresent=request.Headers.Authorization!=null,protocol="openai",model="gpt-image-2.5-sunburst"});
                try { var response=await base.SendAsync(request,token);Trace(new{kind="response",status=(int)response.StatusCode,contentType=response.Content==null||response.Content.Headers.ContentType==null?null:response.Content.Headers.ContentType.MediaType,contentLength=response.Content==null?null:response.Content.Headers.ContentLength});return response; }
                catch(Exception error){Trace(new{kind="transport",exceptionType=error.GetType().Name,innerExceptionType=error.InnerException==null?null:error.InnerException.GetType().Name});throw;}
            }
            static void Trace(object value){File.AppendAllText(Path.Combine(evidence,metadataName),new JavaScriptSerializer().Serialize(value)+Environment.NewLine);}
        }
        sealed class ScriptedChat:IConversationApi,IAgentConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){return Task.FromResult("scripted acceptance");}
            public async Task<string> ChatAgentAsync(ConnectionSettings settings,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tools,bool stream,CancellationToken token)
            {
                foreach(var item in new[]{new[]{"dev3-tool-one","A blue ceramic teacup beside a small white flower on a pale studio background, simple composition, no text."},new[]{"dev3-tool-two","A warm yellow ceramic teacup beside a small green leaf on a pale studio background, simple composition, no text."}})
                {
                    var result=await workspace.ExecuteAsync("generate_image",new JavaScriptSerializer().Serialize(new{prompt=item[1]}),item[0],token);
                    if(result.StartsWith("错误："))throw new InvalidOperationException("Actual built-in image tool did not complete.");
                }
                return "两张图片已生成。";
            }
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] wave,CancellationToken token){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string voice,string text,CancellationToken token){throw new NotSupportedException();}
            public void Dispose(){}
        }
        sealed class SilentSpeech:IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}
            public void SetVolume(int volume){} public void Stop(){} public void Dispose(){}
        }
        static async Task Run(string servicePath,bool single)
        {
            evidence=Path.Combine(Directory.GetCurrentDirectory(),"work","dev3-live-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
            var secret=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(servicePath));
            string endpoint=(string)secret["BaseUrl"];const string model="gpt-image-2.5-sunburst";
            Check(((object[])secret["Models"]).Cast<string>().Contains(model),"configured GPT image model exists");
            using(var socket=new System.Net.Sockets.TcpClient()) {var uri=new Uri(endpoint);var connect=socket.ConnectAsync(uri.Host,uri.Port);if(await Task.WhenAny(connect,Task.Delay(5000))!=connect)throw new InvalidOperationException("Service TCP preflight timed out; no image HTTP sent.");await connect;}
            var settings=new PetSettings{SaveChatHistory=true,AgentEnabled=true,AgentApprovalMode=AgentApprovalMode.Full,AgentWorkspace=Path.Combine(evidence,"workspace"),ReadReplies=false,ShowPredictedMessages=false,AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,OnboardingState="completed",ThemeMode="light"};
            Directory.CreateDirectory(settings.AgentWorkspace);foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            settings.Text.BaseUrl="https://scripted.invalid/v1";settings.Text.ApiKey="scripted-chat-fixture";settings.Text.Model="scripted-no-chat-http";settings.Text.ToolSupport=CapabilitySupport.Supported;
            var provider=new ImageGenerationProvider{Name="dev3 live GPT acceptance",Connection=new ConnectionSettings{BaseUrl=endpoint,EncryptedKey=(string)secret["EncryptedKey"],Model=model,UseProxy=false}};
            settings.ImageProviders=new List<ImageGenerationProvider>{provider};settings.ActiveImageProviderId=provider.Id;
            var data=Path.Combine(evidence,"data");pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
            handler=new BudgetHandler(single?1:3);var client=new ImageGenerationClient(pet.ImageStore,handler,null);var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),pet.ImageStore,client,pet.ImageUsage);
            await manager.RestoreAsync(CancellationToken.None);typeof(PetWindow).GetField("imageClient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,client);typeof(PetWindow).GetField("imageTasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);
            var window=new ChatWindow(pet);window.Show();window.ShowImagesPage();await Task.Delay(100);var page=All<ImageWorkspacePage>(window).Single();
            All<TextBox>(page).Single(t=>t.Name=="ImagePrompt").Text="A blue ceramic teacup on a white studio background, soft light, simple composition, no text.";
            var manual=page.GenerateAsync();await handler.Entered.Task;
            if(single)
            {
                handler.Release.TrySetResult(true);await manual;
                Check(handler.Requests==1&&manager.List().Single().State==ImageGenerationTaskState.Succeeded,"single diagnostic manual request committed one asset");
                var saved=pet.ImageStore.List().Single();Click(All<Button>(page).Single(b=>b.Name=="ImageGalleryTab"));
                for(int i=0;i<500&&!All<ImageGalleryTile>(page).Any();i++)await Task.Delay(10);
                All<ImageGalleryTile>(page).Single().OpenDetails();await Task.Delay(100);
                Check(All<Border>(window).Any(b=>b.Name=="ChatImagePreviewOverlay"),"live original opens actual native preview");
                var exported=await new ImageGalleryExport(pet.ImageStore,pet.ImageUsage).ExportAsync(new[]{saved.Id},ExportFolder(),null,CancellationToken.None);
                Check(exported.Completed==1&&File.ReadAllBytes(exported.Items.Single().Path).SequenceEqual(pet.ImageStore.Read(saved.Id)),"single live original export preserves bytes");
                Click(All<Button>(window).Single(b=>b.Name=="ChatPreviewClose"));pet.ImageStore.MoveToTrash(new[]{saved.Id});Check(pet.ImageStore.List().Count==0&&pet.ImageStore.Read(saved.Id).Length==saved.Bytes,"single live trash retains readable original");
                pet.ImageStore.Restore(new[]{saved.Id});Check(pet.ImageStore.List().Single().Id==saved.Id,"single live restore preserves asset ID");
                File.WriteAllText(Path.Combine(evidence,"report.json"),new JavaScriptSerializer().Serialize(new{success=true,mode="single-manual",checks=checks,imageHttpRequests=handler.Requests,assetId=saved.Id,bytes=saved.Bytes}));
                await manager.StopAsync(CancellationToken.None);window.Close();pet.Close();await pet.ImageServicesShutdown;pet=null;return;
            }
            using(var session=new ConversationSession(new ScriptedChat(),new SilentSpeech(),()=>null,new HistoryStore(Path.Combine(evidence,"chat-history")),()=>true))
            {
                session.SelectPersona("dev3-fixture");session.ConfigureKernelSession(new AgentSessionModel("dev3-live-session","dev3-project","dev3-conversation"),null);session.ImageStore=pet.ImageStore;session.ImageClient=client;session.ImageTasks=manager;session.ImageUsage=pet.ImageUsage;
                var chat=session.SendTextAsync("Generate two cup variations in this reply.",settings,"pet");
                for(int i=0;i<500&&!manager.List().Any(j=>j.Source==ImageGenerationSource.Conversation);i++)await Task.Delay(10);
                Check(manager.List().Any(j=>j.Source==ImageGenerationSource.Conversation&&j.State==ImageGenerationTaskState.Queued),"actual page and built-in tool share held queue");
                var canceled=await manager.EnqueueAsync(new ImageGenerationTaskSubmission("dev3-cancel-no-http",new ImageGenerationRequest{Provider=provider,Prompt="Must never be sent",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Workspace}),CancellationToken.None);
                Check(await manager.CancelAsync(canceled.Id)&&handler.Requests==0,"queued fourth scenario cancels before any HTTP forwarding");
                handler.Release.TrySetResult(true);await Task.WhenAll(manual,chat);await session.FlushHistoryAsync();
                var reply=session.History.Messages.Single(m=>m.GeneratedImages!=null&&m.GeneratedImages.Count==2);
                Check(reply.ImageGenerationTaskIds.Count==2&&reply.GeneratedImages.Count==2,"two actual built-in tools attach to one original reply");
                Check(handler.Requests==3&&manager.Get(canceled.Id).State==ImageGenerationTaskState.Canceled,"exactly three real generation POSTs; canceled job sent zero");
                var view=new ChatMessageView(reply,new StackPanel(),pet,window);var host=new Window{Content=view,Width=760,Height=650};host.Show();await Task.Delay(100);var group=All<GeneratedImageGroupCard>(view).Single();
                foreach(var image in reply.GeneratedImages){Click(All<Button>(group).Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string)b.Tag==image.ImageJobId));await Task.Delay(50);group.UpdateLayout();Check((string)All<GeneratedImageCard>(group).Single().Tag==image.Id,"live multi-image selection targets saved original");}host.Close();
                var images=pet.ImageStore.List();Check(images.Count==3,"manual plus two tool assets in gallery");
                var exported=await new ImageGalleryExport(pet.ImageStore,pet.ImageUsage).ExportAsync(images.Select(i=>i.Id),ExportFolder(),null,CancellationToken.None);
                Check(exported.Completed==3&&exported.Items.All(i=>File.ReadAllBytes(i.Path).SequenceEqual(pet.ImageStore.Read(i.Id))),"live original exports preserve bytes");
                var ids=images.Select(i=>i.Id).ToArray();pet.ImageStore.MoveToTrash(ids);Check(pet.ImageStore.List().Count==0&&reply.GeneratedImages.All(i=>pet.ImageStore.Read(i.Id).Length==i.Bytes),"trash keeps original chat assets readable");pet.ImageStore.Restore(ids);Check(pet.ImageStore.List().Count==3,"restore reuses the same three live assets");
                await manager.StopAsync(CancellationToken.None);
                using(var restored=new GeneratedImageStore(data))using(var restoredClient=new ImageGenerationClient(restored,handler,null))
                {
                    var recovered=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),restored,restoredClient,new ImageAssetUsageRegistry());await recovered.RestoreAsync(CancellationToken.None);Check(recovered.List().Count(j=>j.State==ImageGenerationTaskState.Succeeded)==3&&handler.Requests==3,"restart restores committed jobs without another HTTP");await recovered.StopAsync(CancellationToken.None);
                }
                File.WriteAllText(Path.Combine(evidence,"report.json"),new JavaScriptSerializer().Serialize(new{success=true,checks=checks,imageHttpRequests=handler.Requests,chatProtocol="scripted actual built-in tool invocations; no chat HTTP",model=model,assets=images.Select(i=>new{i.Id,i.Width,i.Height,i.Bytes}).ToArray()}));
            }
            window.Close();pet.Close();await pet.ImageServicesShutdown;pet=null;
        }
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length==1&&args[0]=="--compile-only")return 0;
            if(args.Length==2&&args[0]=="--reopen-only")
            {
                try{Reopen(args[1]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine("Reopen failed: "+error.GetType().Name);return 1;}
            }
            bool conversation=args.Length==3&&args[0]=="--conversation-only";bool conversationReuse=args.Length==2&&args[0]=="--conversation-reuse-only";
            if(!conversation&&!conversationReuse&&(args.Length!=2||(args[0]!="--run"&&args[0]!="--single-manual"&&args[0]!="--reuse-only"))){Console.Error.WriteLine("Explicit opt-in: --run / --single-manual <DPAPI service.json>; budget three/one. --conversation-only <service.json> <existing fixture> budget two. --reuse-only / --reopen-only <fixture> zero HTTP.");return 2;}
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int code=1;
            app.Dispatcher.BeginInvoke(new Action(async delegate{try{if(conversationReuse)await Reuse(args[1],true);else if(conversation)await Conversation(args[1],args[2]);else if(args[0]=="--reuse-only")await Reuse(args[1],false);else await Run(args[1],args[0]=="--single-manual");Console.WriteLine("Live image workflow: "+checks+" passed");code=0;}catch(Exception error){Console.Error.WriteLine("Live acceptance failed: "+error.GetType().Name+"; no automatic retry.");if(evidence!=null)File.WriteAllText(Path.Combine(evidence,conversationReuse?"conversation-reuse-failure.json":conversation?"conversation-failure.json":args[0]=="--reuse-only"?"reuse-failure.json":"failure.json"),new JavaScriptSerializer().Serialize(new{success=false,errorType=error.GetType().Name,checks=checks,imageHttpRequests=handler==null?0:handler.Requests}));}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}}));app.Run();return code;
        }
        sealed class DenyHttp:HttpMessageHandler
        {
            internal int Attempts;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Attempts++;throw new InvalidOperationException("Restart must never send HTTP.");}
        }
        static async Task Reopen(string fixture)
        {
            var data=Path.Combine(Path.GetFullPath(fixture),"data");
            using(var assets=new GeneratedImageStore(data))using(var deny=new DenyHttp())using(var client=new ImageGenerationClient(assets,deny,deny))
            {
                var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),assets,client,new ImageAssetUsageRegistry());await manager.RestoreAsync(CancellationToken.None);
                var images=assets.List();Check(images.Count>0&&images.All(image=>assets.Read(image.Id).Length==image.Bytes)&&manager.List().Count(j=>j.State==ImageGenerationTaskState.Succeeded)==images.Count,"fresh process restores the same committed originals and tasks");
                string historyPath=Path.Combine(fixture,"conversation-history");if(Directory.Exists(historyPath)){var messages=new HistoryStore(historyPath).Load("dev3-fixture");Check(messages.Count(m=>m.GeneratedImages!=null&&m.GeneratedImages.Count==2&&m.ImageGenerationTaskIds.Count==2)==1,"fresh process restores the original two-image reply group");}
                Check(deny.Attempts==0,"fresh process restart sent zero HTTP");await manager.StopAsync(CancellationToken.None);
                File.WriteAllText(Path.Combine(fixture,images.Count==1?"reopen-report.json":"conversation-reopen-report.json"),new JavaScriptSerializer().Serialize(new{success=true,imageHttpRequests=0,assetIds=images.Select(i=>i.Id).ToArray(),checks=checks}));
            }
        }
        static async Task Reuse(string fixture,bool conversation)
        {
            evidence=Path.GetFullPath(fixture);string data=Path.Combine(evidence,"data");
            var settings=new PetSettings{SaveChatHistory=true,ReadReplies=false,ShowPredictedMessages=false,AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,OnboardingState="completed",ThemeMode="light"};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);var deny=new DenyHttp();var client=new ImageGenerationClient(pet.ImageStore,deny,deny);var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),pet.ImageStore,client,pet.ImageUsage);await manager.RestoreAsync(CancellationToken.None);
            typeof(PetWindow).GetField("imageClient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,client);typeof(PetWindow).GetField("imageTasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);
            var savedImages=pet.ImageStore.List();var saved=savedImages.First();var window=new ChatWindow(pet);window.Show();window.ShowImagesPage();await Task.Delay(100);var page=All<ImageWorkspacePage>(window).Single();Click(All<Button>(page).Single(b=>b.Name=="ImageGalleryTab"));
            for(int i=0;i<500&&!All<ImageGalleryTile>(page).Any();i++)await Task.Delay(10);All<ImageGalleryTile>(page).First().OpenDetails();await Task.Delay(100);Check(All<Border>(window).Any(b=>b.Name=="ChatImagePreviewOverlay"),"saved live original reopens actual native gallery preview");
            Click(All<Button>(window).Single(b=>b.Name=="ChatPreviewClose"));
            if(conversation)
            {
                var reply=new HistoryStore(Path.Combine(evidence,"conversation-history")).Load("dev3-fixture").Single(m=>m.GeneratedImages!=null&&m.GeneratedImages.Count==2);Check(reply.ImageGenerationTaskIds.Count==2,"same original real reply restores both fixed slots");
                var groupHost=new Window{Width=760,Height=650};var view=new ChatMessageView(reply,new StackPanel(),pet,window);groupHost.Content=view;groupHost.Show();await Task.Delay(100);var group=All<GeneratedImageGroupCard>(view).Single();
                int slot=0;foreach(var image in reply.GeneratedImages)
                {
                    slot++;Click(All<Button>(group).Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string)b.Tag==image.ImageJobId));
                    await Until(delegate{group.UpdateLayout();var cards=All<GeneratedImageCard>(group).ToArray();return cards.Length==1&&(string)cards[0].Tag==image.Id;},"restored real multi-image selection targets correct saved original");
                    Click(All<Button>(group).Single(b=>b.Name=="PreviewGeneratedImage"));await Until(()=>All<Image>(window).Any(i=>i.Name=="ChatPreviewImage"&&i.Source!=null),"each selected real reply asset opens decoded native preview");Click(All<Button>(window).Single(b=>b.Name=="ChatPreviewClose"));
                    foreach(int width in new[]{760,390}){groupHost.Width=width;await Until(delegate{groupHost.UpdateLayout();return All<ScrollViewer>(group).Any(scroll=>scroll.Content is StackPanel&&((StackPanel)scroll.Content).Orientation==(width>=480?Orientation.Vertical:Orientation.Horizontal));},"real reply thumbnail rail adapts at "+width);await groupHost.Dispatcher.InvokeAsync(new Action(()=>groupHost.UpdateLayout()),System.Windows.Threading.DispatcherPriority.Render);Capture(groupHost,"conversation-"+(width>=480?"wide":"narrow")+"-slot-"+slot+".png");}groupHost.Width=760;
                }
                groupHost.UpdateLayout();var capture=new System.Windows.Media.Imaging.RenderTargetBitmap((int)groupHost.ActualWidth,(int)groupHost.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(groupHost);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(capture));using(var output=File.Create(Path.Combine(evidence,"conversation-group.png")))encoder.Save(output);groupHost.Close();
            }
            var exported=await new ImageGalleryExport(pet.ImageStore,pet.ImageUsage).ExportAsync(savedImages.Select(i=>i.Id),ExportFolder(),null,CancellationToken.None);Check(exported.Completed==savedImages.Count&&exported.Items.All(i=>File.ReadAllBytes(i.Path).SequenceEqual(pet.ImageStore.Read(i.Id))),"saved live originals export exact bytes to existing chosen folder");
            var ids=savedImages.Select(i=>i.Id).ToArray();pet.ImageStore.MoveToTrash(ids);Check(pet.ImageStore.List().Count==0&&savedImages.All(i=>pet.ImageStore.Read(i.Id).Length==i.Bytes),"saved live trash remains readable");pet.ImageStore.Restore(ids);Check(pet.ImageStore.List().Count==savedImages.Count&&savedImages.All(i=>pet.ImageStore.Get(i.Id)!=null),"saved live restore keeps same asset IDs");Check(deny.Attempts==0,"reuse sends zero HTTP");
            await manager.StopAsync(CancellationToken.None);window.Close();pet.Close();await pet.ImageServicesShutdown;pet=null;
            File.WriteAllText(Path.Combine(evidence,conversation?"conversation-reuse-report.json":"reuse-report.json"),new JavaScriptSerializer().Serialize(new{success=true,checks=checks,imageHttpRequests=0,assets=savedImages.Select(i=>new{i.Id,i.Width,i.Height,i.Bytes}).ToArray()}));
        }
        static async Task Conversation(string servicePath,string fixture)
        {
            evidence=Path.GetFullPath(fixture);metadataName="conversation-http-metadata.jsonl";
            var secret=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(servicePath));string data=Path.Combine(evidence,"data");
            var settings=new PetSettings{SaveChatHistory=true,AgentEnabled=true,AgentApprovalMode=AgentApprovalMode.Full,AgentWorkspace=Path.Combine(evidence,"workspace"),ReadReplies=false,ShowPredictedMessages=false,AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,OnboardingState="completed",ThemeMode="light"};Directory.CreateDirectory(settings.AgentWorkspace);foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            settings.Text.BaseUrl="https://scripted.invalid/v1";settings.Text.ApiKey="scripted-chat-fixture";settings.Text.Model="scripted-no-chat-http";settings.Text.ToolSupport=CapabilitySupport.Supported;
            var provider=new ImageGenerationProvider{Name="dev3 live GPT acceptance",Connection=new ConnectionSettings{BaseUrl=(string)secret["BaseUrl"],EncryptedKey=(string)secret["EncryptedKey"],Model="gpt-image-2.5-sunburst",UseProxy=false}};settings.ImageProviders=new List<ImageGenerationProvider>{provider};settings.ActiveImageProviderId=provider.Id;
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);Check(pet.ImageStore.List().Count==1,"conversation reuses existing single manual asset store");handler=new BudgetHandler(2);handler.Release.TrySetResult(true);var client=new ImageGenerationClient(pet.ImageStore,handler,null);var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),pet.ImageStore,client,pet.ImageUsage);await manager.RestoreAsync(CancellationToken.None);
            typeof(PetWindow).GetField("imageClient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,client);typeof(PetWindow).GetField("imageTasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);
            var window=new ChatWindow(pet);window.Show();
            using(var session=new ConversationSession(new ScriptedChat(),new SilentSpeech(),()=>null,new HistoryStore(Path.Combine(evidence,"conversation-history")),()=>true))
            {
                session.SelectPersona("dev3-fixture");session.ConfigureKernelSession(new AgentSessionModel("dev3-live-session","dev3-project","dev3-conversation"),null);session.ImageStore=pet.ImageStore;session.ImageClient=client;session.ImageTasks=manager;session.ImageUsage=pet.ImageUsage;
                await session.SendTextAsync("Generate two cup variations in this reply.",settings,"pet");await session.FlushHistoryAsync();var reply=session.History.Messages.Single(m=>m.GeneratedImages!=null&&m.GeneratedImages.Count==2);Check(handler.Requests==2&&reply.ImageGenerationTaskIds.Count==2,"two new real built-in calls belong to one original scripted-chat reply");
                var groupHost=new Window{Width=760,Height=650};var view=new ChatMessageView(reply,new StackPanel(),pet,window);groupHost.Content=view;groupHost.Show();await Task.Delay(100);var group=All<GeneratedImageGroupCard>(view).Single();
                foreach(var image in reply.GeneratedImages){Click(All<Button>(group).Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string)b.Tag==image.ImageJobId));await Task.Delay(50);group.UpdateLayout();Check((string)All<GeneratedImageCard>(group).Single().Tag==image.Id,"real multi-image selection targets correct saved asset");Click(All<Button>(group).Single(b=>b.Name=="PreviewGeneratedImage"));await Task.Delay(100);Check(All<Image>(window).Any(i=>i.Name=="ChatPreviewImage"&&i.Source!=null),"selected real conversation image opens decoded native preview");Click(All<Button>(window).Single(b=>b.Name=="ChatPreviewClose"));}groupHost.Close();
                var images=pet.ImageStore.List();Check(images.Count==3,"one manual and two conversation originals share gallery");var exported=await new ImageGalleryExport(pet.ImageStore,pet.ImageUsage).ExportAsync(images.Select(i=>i.Id),ExportFolder(),null,CancellationToken.None);Check(exported.Completed==3&&exported.Items.All(i=>File.ReadAllBytes(i.Path).SequenceEqual(pet.ImageStore.Read(i.Id))),"all three real assets export original bytes without overwriting earlier export");
                var ids=images.Select(i=>i.Id).ToArray();pet.ImageStore.MoveToTrash(ids);Check(pet.ImageStore.List().Count==0&&reply.GeneratedImages.All(i=>pet.ImageStore.Read(i.Id).Length==i.Bytes),"real reply images remain readable in trash");pet.ImageStore.Restore(ids);Check(pet.ImageStore.List().Count==3&&handler.Requests==2,"restoring all real assets sends no additional generation");
                File.WriteAllText(Path.Combine(evidence,"conversation-report.json"),new JavaScriptSerializer().Serialize(new{success=true,checks=checks,imageHttpRequests=handler.Requests,chatHttpRequests=0,replyId=reply.Id,taskIds=reply.ImageGenerationTaskIds,assets=images.Select(i=>new{i.Id,i.Width,i.Height,i.Bytes}).ToArray()}));
            }
            await manager.StopAsync(CancellationToken.None);window.Close();pet.Close();await pet.ImageServicesShutdown;pet=null;
        }
    }
}
