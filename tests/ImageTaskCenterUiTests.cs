using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
    public static class ImageTaskCenterUiTests
    {
        static Application app;static PetWindow pet;static int checks;static string evidence;
        static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        static IEnumerable<T> All<T>(DependencyObject p)where T:DependencyObject{if(p is T)yield return(T)p;for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++)foreach(var t in All<T>(VisualTreeHelper.GetChild(p,i)))yield return t;}
        static Button Find(DependencyObject p,string name){return All<Button>(p).Single(b=>b.Name==name);}
        static void Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static async Task Until(Func<bool> ready){for(int i=0;i<400&&!ready();i++)await Task.Delay(10);Check(ready(),"native task transition completed");}
        sealed class Handler:HttpMessageHandler
        {
            internal int Calls;internal bool Delay=true;internal byte[] Bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Interlocked.Increment(ref Calls);if(Delay)await Task.Delay(Timeout.Infinite,token);return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"b64_json\":\""+Convert.ToBase64String(Bytes)+"\"}]}")};}
        }
        static Border Row(DependencyObject center,string id){return All<Border>(center).Single(b=>b.Name=="ImageTaskRow"&&(string)b.Tag==id);}
        static void Capture(Window host,string name){host.UpdateLayout();var bitmap=new RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(host);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(evidence,name)))encoder.Save(output);}
        static async Task Run()
        {
            evidence=Path.Combine(Directory.GetCurrentDirectory(),"work","image-ui-evidence");Directory.CreateDirectory(evidence);string data=Path.Combine(evidence,"task8-data-"+Guid.NewGuid().ToString("N"));var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,OnboardingState="completed",ThemeMode="light"};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);var provider=new ImageGenerationProvider{Name="Local fixture service",Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="fixture-image"}};settings.ImageProviders=new List<ImageGenerationProvider>{provider};settings.ActiveImageProviderId=provider.Id;var http=new Handler();var client=new ImageGenerationClient(pet.ImageStore,http,null);var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),pet.ImageStore,client,pet.ImageUsage);await manager.RestoreAsync(CancellationToken.None);typeof(PetWindow).GetField("imageTasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);typeof(PetWindow).GetField("imageClient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,client);
            var chatHost=new ChatWindow(pet);Window host=chatHost;host.Show();chatHost.ShowImagesPage();await Task.Delay(80);var page=All<ImageWorkspacePage>(host).Single();host.Width=960;host.Height=760;
            Check(All<Button>(page).Any(b=>b.Name=="ImageTasksTab"),"ThreeEntriesAppearInSameCenter: task tab exists");Click(Find(page,"ImageTasksTab"));await Task.Delay(30);var center=All<Grid>(page).Single(g=>g.Name=="ImageTaskCenterPage");
            var handles=new List<ImageGenerationTaskHandle>();foreach(var source in new[]{ImageGenerationSource.Workspace,ImageGenerationSource.Conversation,ImageGenerationSource.SettingsTest})handles.Add(await manager.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest{Provider=provider,Prompt=source==ImageGenerationSource.Conversation?"把陶杯放在窗边的柔和晨光中":source==ImageGenerationSource.SettingsTest?"测试当前图片服务连接":"为夏日旅行生成一张海边插画",Persistent=true},new ImageTaskOrigin{Source=source}),CancellationToken.None));await Until(()=>All<Border>(center).Count(b=>b.Name=="ImageTaskRow")==3);Check(new[]{"图像页","对话","设置测试"}.All(s=>All<TextBlock>(center).Any(t=>t.Text.Contains(s))),"ThreeEntriesAppearInSameCenter");
            var select=All<CheckBox>(Row(center,handles[1].Id)).Single();select.IsChecked=true;select.IsChecked=false;Check(All<TextBlock>(center).Any(t=>t.Text.EndsWith("已选择 0 项"))&&!Find(center,"CancelSelectedImageTasks").IsEnabled,"manual clear selection updates header and cancel eligibility");select.IsChecked=true;Click(Find(center,"CancelSelectedImageTasks"));await Until(()=>manager.Get(handles[1].Id).State==ImageGenerationTaskState.Canceled);Check(!manager.Get(handles[0].Id).IsTerminal&&!manager.Get(handles[2].Id).IsTerminal,"CancelOnlySelectedJob");await Task.Delay(50);Check(All<CheckBox>(center).Count(c=>c.IsChecked==true)==0&&!Find(center,"CancelSelectedImageTasks").IsEnabled,"terminal selection clears consistently with batch count");
            center.GetType().GetMethod("SelectTask",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(center,new object[]{handles[0].Id});Check(Row(center,handles[0].Id).BorderThickness.Left==2,"selecting existing task highlights row without waiting for new snapshot");
            var subscriptions=typeof(ImageGenerationTaskManager).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic);int baseline=((Delegate)subscriptions.GetValue(manager)).GetInvocationList().Length;for(int visit=0;visit<3;visit++){Click(Find(page,"ImageGenerateTab"));await Task.Delay(20);Click(Find(page,"ImageTasksTab"));await Task.Delay(20);}Check(((Delegate)subscriptions.GetValue(manager)).GetInvocationList().Length==baseline,"repeated task views do not duplicate manager subscriptions");
            var clockProperty=center.GetType().GetProperty("ClockRunning",BindingFlags.Instance|BindingFlags.NonPublic);Check((bool)clockProperty.GetValue(center,null),"task center active elapsed clock runs");var retained=Row(center,handles[0].Id);await Task.Delay(1100);Check(ReferenceEquals(retained,Row(center,handles[0].Id)),"elapsed tick preserves native rows");host.WindowState=WindowState.Minimized;await Task.Delay(30);Check(!(bool)clockProperty.GetValue(center,null),"minimized task center stops elapsed clock");host.WindowState=WindowState.Normal;host.Hide();await Task.Delay(30);Check(!(bool)clockProperty.GetValue(center,null),"hidden task center stops elapsed clock");host.Show();await Task.Delay(30);Check((bool)clockProperty.GetValue(center,null),"shown task center resumes elapsed clock");
            foreach(var theme in new[]{"light","dark"}){ThemeManager.Apply(theme);await Task.Delay(60);Capture(host,"task-center-wpf-"+theme+".png");}host.Width=460;await Task.Delay(60);Capture(host,"task-center-wpf-narrow.png");Check(All<Border>(center).Where(b=>b.Name=="ImageTaskRow").All(b=>b.ActualWidth<=center.ActualWidth),"narrow task rows fit available width");host.Width=960;ThemeManager.Apply("light");
            await manager.CancelAsync(handles[0].Id);await manager.CancelAsync(handles[2].Id);await Until(()=>handles.All(h=>manager.Get(h.Id).IsTerminal));http.Delay=false;int before=http.Calls;
            var retryAutomation=app.Dispatcher.BeginInvoke(new Action(async delegate{await Task.Delay(30);var dialog=app.Windows.OfType<Window>().Single(w=>w.Name=="ImageTaskRetryDialog");Check(All<TextBox>(dialog).Any(t=>t.Text=="把陶杯放在窗边的柔和晨光中"),"retry preview freezes prompt");Click(Find(dialog,"ConfirmImageTaskRetry"));}));Click(Find(Row(center,handles[1].Id),"RetryImageTask"));await Until(()=>manager.List().Count==4&&manager.List().Last().IsTerminal);var retry=manager.List().Last();Check(retry.Id!=handles[1].Id&&retry.RetryOfTaskId==handles[1].Id&&http.Calls==before+1&&retry.DeliveryState==ImageTaskDeliveryState.Detached,"RetryCreatesNewIdAndOneRequest");
            pet.Conversation.ImageTasks=manager;ChatMessage user;var reply=pet.Conversation.History.BeginTurn("draw fixture","text",out user);var delivery=await manager.EnqueueAsync(new ImageGenerationTaskSubmission("repair-fixture",new ImageGenerationRequest{Provider=provider,Prompt="Repair original",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=pet.Conversation.ImageConversationId,PersonaId=pet.Conversation.PersonaId,MessageId=reply.Id}),CancellationToken.None);await delivery.Completion;await manager.SetDeliveryStateAsync(delivery.Id,ImageTaskDeliveryState.Failed);await Until(()=>All<Button>(Row(center,delivery.Id)).Any(b=>b.Name=="RepairImageDelivery"));before=http.Calls;Click(Find(Row(center,delivery.Id),"RepairImageDelivery"));await Until(()=>manager.Get(delivery.Id).DeliveryState==ImageTaskDeliveryState.Delivered);Check(http.Calls==before&&pet.Conversation.History.Messages.Single(m=>m.Id==reply.Id).GeneratedImages.Any(),"DeliveryRepairDoesNotRegenerate");
            var definition=new ConversationDefinition{Id="nativeclosedowner",Name="Existing repair owner",PetId=pet.Skin.Id,Mode="chat"};pet.Projects.UnassignedConversations.Add(definition);var ownerProject=ProjectCatalogStore.UnassignedProject(pet.Projects,settings);var original=new ConversationLog();ChatMessage originalUser;var originalReply=original.BeginTurn("closed native owner","text",out originalUser);original.Update(originalReply.Id,"original reply","complete");string ownerHistory=Path.Combine(data,"conversations",definition.Id,"history");await new HistoryStore(ownerHistory).SaveAsync(pet.Skin.Id,original.Messages);
            var repairHandles=new List<ImageGenerationTaskHandle>();foreach(var name in new[]{"closed-a","closed-b"}){var handle=await manager.EnqueueAsync(new ImageGenerationTaskSubmission(name,new ImageGenerationRequest{Provider=provider,Prompt=name,Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=definition.Id,PersonaId=pet.Skin.Id,MessageId=originalReply.Id}),CancellationToken.None);await handle.Completion;await manager.SetDeliveryStateAsync(handle.Id,ImageTaskDeliveryState.Failed);repairHandles.Add(handle);}before=http.Calls;int windowsBefore=app.Windows.Count;
            await Task.WhenAll(repairHandles.Select(h=>pet.RepairImageDeliveryAsync(h.Id)));var canonical=pet.Workspaces.CreateWorkspace(ownerProject,definition);canonical.Session.AddLiveTurn(pet.Skin.Id,"native new message after repair","new reply");await canonical.Session.FlushHistoryAsync();var repairedHistory=new HistoryStore(ownerHistory).Load(pet.Skin.Id);
            Check(repairedHistory.Single(m=>m.Id==originalReply.Id).GeneratedImages.Count==2&&repairedHistory.Any(m=>m.Content=="native new message after repair"),"native closed-owner repair and opening share both image attachments and live edits");Check(http.Calls==before&&app.Windows.Count==windowsBefore&&pet.Workspaces.OpenWorkspaces.Count(w=>w.Id==definition.Id)==1,"native repair registers one existing owner without HTTP or new window");
            Click(Find(page,"ImageGenerateTab"));await Task.Delay(40);All<TextBox>(page).Single(t=>t.Name=="ImagePrompt").Text="first explicit submit";http.Delay=true;var first=page.GenerateAsync();await Until(()=>manager.List().Last().State==ImageGenerationTaskState.Generating);string firstId=manager.List().Last().Id;Check(Find(page,"StartImageGeneration").IsEnabled,"accepted submit releases form while HTTP continues");All<TextBox>(page).Single(t=>t.Name=="ImagePrompt").Text="second explicit submit";var second=page.GenerateAsync();await Until(()=>manager.List().Last().State==ImageGenerationTaskState.Queued);string secondId=manager.List().Last().Id;page.Dispose();host.Close();var reopened=new ImageWorkspacePage(pet);host=new Window{Content=reopened,Width=900,Height=700};host.Show();Click(Find(reopened,"ImageTasksTab"));await Task.Delay(50);Check(All<Border>(reopened).Any(b=>b.Name=="ImageTaskRow"&&(string)b.Tag==firstId)&&!first.IsCompleted,"ReopenViewRebindsExistingJob");await manager.CancelAsync(secondId);await manager.CancelAsync(firstId);await Task.WhenAll(first,second);
            var notificationId=typeof(PetWindow).GetField("imageNotificationId",BindingFlags.Instance|BindingFlags.NonPublic);await Until(()=>notificationId.GetValue(pet)!=null);string notified=(string)notificationId.GetValue(pet);typeof(PetWindow).GetMethod("OpenImageTaskNotification",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{null,EventArgs.Empty});await Task.Delay(90);Check(app.Windows.OfType<ChatWindow>().Any(w=>w.CurrentPage=="images"&&All<Border>(w).Any(b=>b.Name=="ImageTaskRow"&&(string)b.Tag==notified&&b.BorderThickness.Left==2)),"native tray notification opens corresponding task row");
            var restartRoot=Path.Combine(data,"restart");var record=new ImageGenerationTaskRecord{Id="interrupted",RequestId="old",Origin=new ImageTaskOrigin{Source=ImageGenerationSource.Workspace},State=ImageGenerationTaskState.Generating,QueuedUtc=DateTime.UtcNow,Prompt="Before restart",ProviderName="Fixture",ReferenceImageIds=new List<string>()};new ImageGenerationTaskStore(restartRoot).Save(new[]{record});before=http.Calls;var restored=new ImageGenerationTaskManager(new ImageGenerationTaskStore(restartRoot),pet.ImageStore,client,pet.ImageUsage);await restored.RestoreAsync(CancellationToken.None);Check(restored.Get("interrupted").State==ImageGenerationTaskState.Interrupted&&http.Calls==before,"RestartShowsInterruptedWithoutNetwork");
            typeof(PetWindow).GetField("imageTasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,restored);var restoredPage=new ImageWorkspacePage(pet);var restoredHost=new Window{Content=restoredPage,Width=650,Height=600};restoredHost.Show();Click(Find(restoredPage,"ImageTasksTab"));await Task.Delay(40);Check(All<TextBlock>(restoredPage).Any(t=>t.Text=="生成已中断")&&http.Calls==before,"restart binds interrupted native row without network");restoredPage.Dispose();restoredHost.Close();typeof(PetWindow).GetField("imageTasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);var originalManager=pet.ImageTasks;
            var concurrency=typeof(PetSettings).GetProperty("ImageGenerationConcurrency");Check(concurrency!=null&&(int)concurrency.GetValue(new PetSettings(),null)==1,"SettingsDefaultsAndRoundTrip default concurrency");var general=new QuietPanel(pet);var generalHost=new Window{Content=general};generalHost.Show();await Task.Delay(30);var choices=All<ComboBox>(general).Single(b=>b.Name=="ImageGenerationConcurrency");choices.SelectedItem=3;var motion=All<CheckBox>(general).Single(b=>b.Name=="ImageGenerationMotionEnabled");motion.IsChecked=false;var saved=new SettingsStore(Path.Combine(data,"settings.json")).Load();Check((int)concurrency.GetValue(saved,null)==3&&!saved.ImageGenerationMotionEnabled&&manager.Concurrency==3,"SettingsDefaultsAndRoundTrip native controls apply immediately");generalHost.Close();reopened.Dispose();host.Close();await manager.StopAsync(CancellationToken.None);await NavigationHandoffCheck();Console.WriteLine("Task center WPF: "+checks+" passed");pet.Close();
        }
        static async Task NavigationHandoffCheck()
        {
            var project=ProjectCatalogStore.UnassignedProject(pet.Projects,pet.Settings);var a=pet.Projects.UnassignedConversations.Single(c=>c.Id=="nativeclosedowner");var workspace=pet.Workspaces.CreateWorkspace(project,a);var finalizer=new HeldFinalization();typeof(ConversationSession).GetField("speech",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(workspace.Session,finalizer);
            var closing=pet.Workspaces.CloseWorkspaceAsync(a.Id);await Until(()=>finalizer.Entered.Task.IsCompleted);var safety=Task.Run(async()=>{await Task.Delay(6000);finalizer.Release.Set();});
            var b=new ConversationDefinition{Id="navigationb",Name="Navigation B",Mode="chat",PetId=pet.Skin.Id};pet.Projects.UnassignedConversations.Add(b);
            try
            {
                pet.OpenProjectConversation(project,a);pet.OpenProjectConversation(project,b);
                await Until(()=>app.Windows.OfType<ChatWindow>().Any(w=>w.ConversationId==b.Id));
                Check(!closing.IsCompleted&&app.Windows.OfType<ChatWindow>().Any(w=>w.ConversationId==b.Id),"native navigation B remains responsive while A close finalization is held");
            }
            finally{finalizer.Release.Set();}
            await closing;await Task.Delay(100);
            Check(app.Windows.OfType<ChatWindow>().Any(w=>w.ConversationId==b.Id)&&!app.Windows.OfType<ChatWindow>().Any(w=>w.ConversationId==a.Id),"stale awaited A navigation cannot override later B selection");
        }
        sealed class HeldRepairHistory:ISecretProtector
        {
            readonly ISecretProtector inner;internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>();internal readonly ManualResetEvent Release=new ManualResetEvent(false);int armed=1;
            internal HeldRepairHistory(ISecretProtector original){inner=original;}
            public byte[] Protect(byte[] plain){var json=System.Text.Encoding.UTF8.GetString(plain);if(json.Contains("\"Messages\"")&&json.Contains("repair-admission-marker")&&Interlocked.CompareExchange(ref armed,0,1)==1){Entered.TrySetResult(true);Release.WaitOne();}return inner.Protect(plain);}
            public byte[] Unprotect(byte[] data){return inner.Unprotect(data);}
        }
        static async Task RepairAdmissionExitCheck(bool deletedOwner=false)
        {
            var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,ReadReplies=false,OnboardingState="completed",AgentEnabled=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            string data=Path.Combine(Directory.GetCurrentDirectory(),"work","task8-repair-admission-"+Guid.NewGuid().ToString("N"));pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
            var handler=new Handler{Delay=false};var client=new ImageGenerationClient(pet.ImageStore,handler,null);var tasks=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),pet.ImageStore,client,pet.ImageUsage);await tasks.RestoreAsync(CancellationToken.None);typeof(PetWindow).GetField("imageTasks",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,tasks);typeof(PetWindow).GetField("imageClient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,client);
            var definition=new ConversationDefinition{Id="admissionowner",Name="Existing admission owner",Mode="chat",PetId=pet.Skin.Id};pet.Projects.UnassignedConversations.Add(definition);var project=ProjectCatalogStore.UnassignedProject(pet.Projects,settings);var log=new ConversationLog();ChatMessage user;var reply=log.BeginTurn("repair-admission-marker","text",out user);log.Update(reply.Id,"original","complete");string historyPath=Path.Combine(data,"conversations",definition.Id,"history");await new HistoryStore(historyPath).SaveAsync(pet.Skin.Id,log.Messages);
            var provider=new ImageGenerationProvider{Name="fixture",Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="fixture"}};var handle=await tasks.EnqueueAsync(new ImageGenerationTaskSubmission("admission-result",new ImageGenerationRequest{Provider=provider,Prompt="fixture",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=definition.Id,PersonaId=pet.Skin.Id,MessageId=reply.Id}),CancellationToken.None);await handle.Completion;await tasks.SetDeliveryStateAsync(handle.Id,ImageTaskDeliveryState.Failed);int before=handler.Calls;
            var owner=pet.Workspaces.CreateWorkspace(project,definition);var originalSession=owner.Session;var speech=new HeldFinalization();typeof(ConversationSession).GetField("speech",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner.Session,speech);var closingOwner=pet.Workspaces.CloseWorkspaceAsync(owner.Id);await Until(()=>speech.Entered.Task.IsCompleted);
            var original=(ISecretProtector)typeof(SecretProtection).GetField("current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var held=new HeldRepairHistory(original);SecretProtection.Configure(held);Task repair=null;var closed=new TaskCompletionSource<bool>();pet.Closed+=delegate{closed.TrySetResult(true);};
            var safety=Task.Run(async()=>{await Task.Delay(15000);speech.Release.Set();held.Release.Set();});
            try
            {
                repair=pet.RepairImageDeliveryAsync(handle.Id);
                Check(!repair.IsCompleted&&!closingOwner.IsCompleted,"actual repair entry accepts operation waiting for held owner close");
                var pending=typeof(PetWindow).GetField("pendingImageRepairs",BindingFlags.Instance|BindingFlags.NonPublic);
                Check(pending!=null&&((ICollection<Task>)pending.GetValue(pet)).Contains(repair),"application owns complete accepted repair before awaiting owner handoff");
                if(deletedOwner)pet.Projects.UnassignedConversations.Remove(definition);
                var watch=System.Diagnostics.Stopwatch.StartNew();pet.Close();bool denied=false;try{await pet.RepairImageDeliveryAsync(handle.Id);}catch(OperationCanceledException){denied=true;}
                Check(denied,"true exit freezes new repair admission");speech.Release.Set();
                if(!deletedOwner)await Until(()=>held.Entered.Task.IsCompleted);
                await closed.Task;
                var actualDrain=(Task)typeof(PetWindow).GetField("applicationExitDrain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                if(deletedOwner)Check(watch.Elapsed.TotalSeconds<6.5,"failed accepted repair does not prevent actual application close");
                else Check(watch.Elapsed.TotalSeconds<6.5&&!repair.IsCompleted&&!actualDrain.IsCompleted&&!pet.ImageServicesShutdown.IsCompleted,"actual close honors deadline while accepted handoff repair and image resources remain tracked");
            }
            finally{speech.Release.Set();held.Release.Set();SecretProtection.Configure(original);}
            bool failed=false;try{await repair;}catch(InvalidOperationException){failed=true;}await closingOwner;await pet.ImageServicesShutdown;
            if(deletedOwner)
            {
                var pending=(ICollection<Task>)typeof(PetWindow).GetField("pendingImageRepairs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                Check(failed&&pending.Count==0&&tasks.Get(handle.Id).DeliveryState==ImageTaskDeliveryState.Failed&&handler.Calls==before,"exit observes failed accepted handoff repair without new owner, HTTP or leaked operation");return;
            }
            var retainedOwners=(ConversationSession[])typeof(PetWindow).GetField("imageExitOwners",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
            Check(retainedOwners.Any(session=>session.ImageConversationId==definition.Id&&!ReferenceEquals(session,originalSession)),"exit refreshes ownership snapshot after accepted repair creates replacement session");
            var messages=new HistoryStore(historyPath).Load(pet.Skin.Id);
            Check(tasks.Get(handle.Id).DeliveryState==ImageTaskDeliveryState.Delivered&&messages.Single(m=>m.Id==reply.Id).GeneratedImages.Count==1&&handler.Calls==before,"accepted handoff repair completes before resource release without HTTP or lost attachment");
        }
        sealed class HeldConversationApi:IConversationApi
        {
            internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>();
            internal readonly TaskCompletionSource<string> Release=new TaskCompletionSource<string>();
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){Entered.TrySetResult(true);return Release.Task;}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] bytes,CancellationToken token){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class HeldFinalization:IReplySpeech
        {
            internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>();
            internal readonly ManualResetEvent Release=new ManualResetEvent(false);
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}
            public void SetVolume(int value){}public void Stop(){}
            public void Dispose(){Entered.TrySetResult(true);Release.WaitOne();}
        }
        static async Task ExitFinalizationCheck()
        {
            var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,ReadReplies=false,OnboardingState="completed",AgentEnabled=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            settings.Text.BaseUrl="https://unused.invalid/v1";settings.Text.Model="fixture";settings.Text.ApiKey="local-fixture-only";
            string data=Path.Combine(Directory.GetCurrentDirectory(),"work","task8-exit-held-"+Guid.NewGuid().ToString("N"));
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
            var api=new HeldConversationApi();var speech=new HeldFinalization();var session=new ConversationSession(api,speech,()=>null,new HistoryStore(Path.Combine(data,"history")),()=>true,"held-owner");session.SelectPersona("pet");typeof(PetWindow).GetField("conversation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,session);
            var sending=session.SendTextAsync("hold active session during close",settings,"pet");if(await Task.WhenAny(api.Entered.Task,sending,Task.Delay(5000))!=api.Entered.Task){speech.Release.Set();await sending;throw new Exception("active WPF session did not enter held API");}
            var closed=new TaskCompletionSource<bool>();pet.Closed+=delegate{closed.TrySetResult(true);};
            var watchdog=Task.Run(async delegate{await Task.Delay(7500);speech.Release.Set();});var watch=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                pet.Close();await closed.Task;
                Check(watch.Elapsed.TotalSeconds<6.5,"actual WPF close stays within budget while active session finalization is held");
                Check(!speech.Entered.Task.IsCompleted&&!pet.ImageServicesShutdown.IsCompleted,"WPF final close defers active session and storage finalization");
            }
            finally{api.Release.TrySetResult("finished");speech.Release.Set();}
            try{await sending;}catch(OperationCanceledException){}await pet.ImageServicesShutdown;
            Check(speech.Entered.Task.IsCompleted,"deferred WPF session finalizes after its writer drains");
        }
        [STAThread] public static int Main(string[] args){app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int code=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{if(args.Contains("--repair-admission-only"))await RepairAdmissionExitCheck();else{await Run();await ExitFinalizationCheck();await RepairAdmissionExitCheck();await RepairAdmissionExitCheck(true);}Console.WriteLine("Task center WPF including held finalization and repair admission: "+checks+" passed");code=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}}));app.Run();return code;}
    }
}
