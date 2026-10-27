using System.Net;
using System.Net.Http;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class ImageTaskCenterUiChecks
{
    static object Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    internal static int RunFinalReview(string data,string scenario)
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        Task Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>6)throw new Exception("FAIL native condition: "+scenario);Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();return Task.CompletedTask;}
        void Complete(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        T Result<T>(Task<T> task){Complete(task);return task.Result;}
        Task Pause(){var until=DateTime.UtcNow.AddMilliseconds(150);while(DateTime.UtcNow<until){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}return Task.CompletedTask;}
        IEnumerable<T> All<T>(StyledElement control)where T:StyledElement=>control.GetLogicalDescendants().OfType<T>().Distinct();
        Button Find(StyledElement control,string name)=>All<Button>(control).Single(b=>b.Name==name);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Border Row(StyledElement control,string id)=>All<Border>(control).Single(b=>b.Name=="ImageTaskRow"&&(string?)b.Tag==id);
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(data));

            var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
            var session=owner.Session;var assets=owner.ImageStore;var usage=owner.ImageUsage;
            var http=new Handler{Delay=false};var client=new ImageGenerationClient(assets,http,null);
            var records=new List<ImageGenerationTaskRecord>();
            var reply=session.History.BeginTurn("restored original","text",out var originalUser);
            foreach(var state in new[]{ImageGenerationTaskState.Generating,ImageGenerationTaskState.Saving,ImageGenerationTaskState.Succeeded})
            {
                var id=Guid.NewGuid().ToString("N");var image=assets.Save(bytes,new GeneratedImage{ImageJobId=id,Source=ImageGenerationSource.Conversation,Prompt="restored "+state},true);
                var record=new ImageGenerationTaskRecord{Id=id,RequestId=id,State=state,DeliveryState=ImageTaskDeliveryState.Pending,Origin=new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=session.ImageConversationId,PersonaId=session.PersonaId,MessageId=reply.Id},Prompt="restored "+state,QueuedUtc=DateTime.UtcNow,ReferenceImageIds=new List<string>(),ResultImageId=state==ImageGenerationTaskState.Succeeded?image.Id:null};
                if(state==ImageGenerationTaskState.Succeeded)session.History.AttachGeneratedImage(reply.Id,image);
                records.Add(record);
            }
            var weakId=Guid.NewGuid().ToString("N");var weak=assets.Save(bytes,new GeneratedImage{ImageJobId=weakId,Source=ImageGenerationSource.Workspace,Prompt="weak result"},true);
            records.Add(new ImageGenerationTaskRecord{Id=weakId,RequestId=weakId,State=ImageGenerationTaskState.Succeeded,ResultImageId=weak.Id,Source=ImageGenerationSource.Workspace,Prompt="weak result",QueuedUtc=DateTime.UtcNow,ReferenceImageIds=new List<string>()});
            new ImageGenerationTaskStore(data).Save(records);var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),assets,client,usage);Complete(manager.RestoreAsync(CancellationToken.None));typeof(DesktopController).GetField("<ImageTasks>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,manager);
            session.ImageTasks=manager;session.ImageStore=assets;session.ImageUsage=usage;
            var management=new ImageAssetManagementService(assets,usage,new ImageAssetReferenceResolver(data,assets,manager));
            ImagePurgeResult PurgeAfterThumbnailReads(string id)
            {
                var wait=System.Diagnostics.Stopwatch.StartNew();
                while(true)
                {
                    var result=Result(management.PurgeAsync(new[]{id},CancellationToken.None));
                    if(result.DeletedIds.Contains(id))return result;
                    // A visible row can precede its asynchronous thumbnail decode.
                    // Retry only that temporary usage refusal, never a reference or storage error.
                    if(!result.Blocked.TryGetValue(id,out var reason) || reason!=TextResources.Get("imageAssetReferenceResolver.purgeCore.resultBlockedId") || wait.Elapsed.TotalSeconds>6)
                        throw new Exception("Unexpected purge refusal after "+wait.ElapsedMilliseconds+"ms: "+string.Join(",",result.Blocked.Values));
                    Complete(Pause());
                }
            }
            var page=new ImageWorkspacePage(owner,new Window());var host=new Window{Content=page,Width=900,Height=740};host.Show();page.SelectedIndex=2;Dispatcher.UIThread.RunJobs();
            Complete(Pause());Complete(Until(()=>All<Border>(page).Count(b=>b.Name=="ImageTaskRow")==4));
            if(scenario=="recovery")
            {
                Check(new ImageGenerationTaskStore(data).Load().Take(3).All(t=>t.DeliveryState==ImageTaskDeliveryState.AwaitingSync),"awaiting sync survives encrypted task persistence");
                foreach(var record in records.Take(3))
                {
                    var row=Row(page,record.Id);Check(manager.Get(record.Id).State==ImageGenerationTaskState.Succeeded,"committed asset restores successful "+record.State);
                    Check(All<TextBlock>(row).Any(t=>t.Text=="图片已保存，等待同步到原会话。"),"recovered pending delivery truthfully awaits sync: "+record.State);
                    Check(All<Button>(row).Any(b=>b.Name=="RepairImageDelivery"),"native recovered pending delivery offers saved-asset repair");
                    Click(Find(row,"RepairImageDelivery"));Complete(Until(()=>manager.Get(record.Id).DeliveryState==ImageTaskDeliveryState.Delivered));
                    Complete(session.RepairImageDeliveryAsync(record.Id,CancellationToken.None));
                }
                Check(http.Calls==0&&session.History.Messages.Single(m=>m.Id==reply.Id).GeneratedImages.Count==3,"recovery and repeat repair attach each original once with zero executor calls");
                var provider=new ImageGenerationProvider{Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="fixture"}};
                var live=Result(manager.EnqueueAsync(new ImageGenerationTaskSubmission("live-pending",new ImageGenerationRequest{Provider=provider,Prompt="live",Persistent=true},records[0].Origin),CancellationToken.None));Complete(live.Completion);Complete(Until(()=>All<Border>(page).Any(b=>b.Name=="ImageTaskRow"&&(string?)b.Tag==live.Id)));
                Complete(Until(()=>All<TextBlock>(Row(page,live.Id)).Any(t=>t.Text=="结果已保存，可查看原图。")));
                Check(manager.Get(live.Id).DeliveryState==ImageTaskDeliveryState.Pending&&!All<Button>(Row(page,live.Id)).Any(b=>b.Name=="RepairImageDelivery"),"live pending delivery is not exposed to a competing repair");
                Complete(manager.SetDeliveryStateAsync(live.Id,ImageTaskDeliveryState.Detached));Complete(Until(()=>All<TextBlock>(Row(page,live.Id)).Any(t=>t.Text=="独立重试结果，可在图库查看。")));
                Check(!All<Button>(Row(page,live.Id)).Any(b=>b.Name=="RepairImageDelivery"),"detached result never repairs old tool reply");
                Check(ImageTaskCenterState.ResultLabel(new ImageGenerationTaskRecord{State=ImageGenerationTaskState.Generating,DeliveryState=ImageTaskDeliveryState.Pending},false)!="图片已删除"&&ImageTaskCenterState.ResultLabel(new ImageGenerationTaskRecord{State=ImageGenerationTaskState.Succeeded},false)=="图片结果暂不可用。","no result yet is distinct from a deleted saved result");
            }
            if(scenario=="purge")
            {
                var row=Row(page,weakId);Check(All<Button>(row).Any(b=>b.Name=="ViewImageTaskResult"),"saved weak result initially viewable");
                var subscriptions=typeof(GeneratedImageStore).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!;Func<int> count=()=>(subscriptions.GetValue(assets) as Delegate)?.GetInvocationList().Length??0;int baseline=count();
                assets.MoveToTrash(new[]{weak.Id});var purged=Result(management.PurgeAsync(new[]{weak.Id},CancellationToken.None));Check(purged.DeletedIds.Contains(weak.Id),"successful task weak result remains safely purgeable");
                Complete(Until(()=>All<TextBlock>(row).Any(t=>t.Text=="图片已删除")));
                Check(ReferenceEquals(row,Row(page,weakId))&&!All<Button>(row).Any(b=>b.Name=="ViewImageTaskResult"),"visible purge updates label and removes stale action in same row");
                host.Hide();Complete(Pause());Check(count()<baseline,"hidden task center releases asset subscription");host.Show();Complete(Pause());Check(count()==baseline,"reopened center rebinds asset subscription once");
                Check(All<TextBlock>(Row(page,weakId)).Any(t=>t.Text=="图片已删除")&&!All<Button>(Row(page,weakId)).Any(b=>b.Name=="ViewImageTaskResult"),"reopened center preserves deleted result state");
                page.Dispose();host.Close();Check(count()<baseline,"disposed center releases asset subscription");
                var restarted=new ImageGenerationTaskManager(new ImageGenerationTaskStore(data),assets,client,usage);Complete(restarted.RestoreAsync(CancellationToken.None));manager=restarted;typeof(DesktopController).GetField("<ImageTasks>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,manager);
                page=new ImageWorkspacePage(owner,new Window());host=new Window{Content=page,Width=900,Height=740};host.Show();Dispatcher.UIThread.RunJobs();
                Check(All<TextBlock>(page).Any(t=>t.Text=="图片文件不可用，可在任务中查看详情。"),"reopened workspace preview tolerates permanently purged weak result");
                page.SelectedIndex=2;Dispatcher.UIThread.RunJobs(); Complete(Pause());
                Check(manager.Get(weakId).State==ImageGenerationTaskState.Succeeded&&All<TextBlock>(Row(page,weakId)).Any(t=>t.Text=="图片已删除")&&!All<Button>(Row(page,weakId)).Any(b=>b.Name=="ViewImageTaskResult"),"restart retains success and truthful deleted weak link");
            }
            if(scenario=="service"||scenario=="model")
            {
                page.SelectedIndex=1;Dispatcher.UIThread.RunJobs();
                var first=assets.Save(bytes,new GeneratedImage{ProviderId="service-a",Model="model-a",Prompt="first filter"},true);var other=assets.Save(bytes,new GeneratedImage{ProviderId="service-b",Model="model-b",Prompt="remaining filter"},true);assets.MoveToTrash(new[]{first.Id,other.Id});
                var trash=(CheckBox)Field(page,"trashOnly");trash.IsChecked=true;
                var filter=(ComboBox)Field(page,scenario=="service"?"providerFilter":"modelFilter");
                Complete(Until(()=>filter.Items.Count>2));filter.SelectedItem=scenario=="service"?filter.Items.OfType<ImageGalleryProviderChoice>().Single(p=>p.Id=="service-a"):(object)"model-a";
                Complete(Until(()=>((IList<GeneratedImage>)Field(page,"loadedGallery")).Count==1&&((IList<GeneratedImage>)Field(page,"loadedGallery"))[0].Id==first.Id));
                ((ImageGallerySelection)Field(page,"gallerySelection")).Toggle(first.Id);
                // Exercise the same in-flight thumbnail lease that can still be active
                // after the asynchronously loaded gallery row becomes visible.
                IDisposable? pendingReader=scenario=="model"?usage.Acquire(first.Id,"fixture-thumbnail"):null;
                var releaseReader=pendingReader==null?null:new System.Threading.Timer(_=>pendingReader.Dispose(),null,1000,Timeout.Infinite);
                try {
                    var purged=PurgeAfterThumbnailReads(first.Id);
                    Console.WriteLine("PURGE_RESULT scenario="+scenario+" deleted="+purged.DeletedIds.Count+" blocked="+string.Join(",",purged.Blocked.Values));
                    Check(purged.DeletedIds.Contains(first.Id),"last selected "+scenario+" asset permanently deleted");
                }finally{releaseReader?.Dispose();pendingReader?.Dispose();}
                Complete(Until(()=>filter.SelectedIndex==0));Complete(Pause());
                var loaded=(IList<GeneratedImage>)Field(page,"loadedGallery");Check(loaded.Count==1&&loaded[0].Id==other.Id&&(int)Field(page,"galleryTotal")==1,"All "+scenario+" control renders the normalized page and count");
                Check(((ImageGallerySelection)Field(page,"gallerySelection")).Count==0,"effective "+scenario+" filter change clears explicit selection");
                var search=(TextBox)Field(page,"search");var gate=Field(assets,"gate");
                Monitor.Enter(gate);try{search.Text="remaining";page.RefreshGallery(); search.Text="no match at latest revision";page.RefreshGallery();}finally{Monitor.Exit(gate);}
                Complete(Until(()=>((IList<GeneratedImage>)Field(page,"loadedGallery")).Count==0));Complete(Pause());Check((int)Field(page,"galleryTotal")==0&&((IList<GeneratedImage>)Field(page,"loadedGallery")).Count==0,"stale queued gallery revision cannot replace latest query");
            }
            page.Dispose();host.Close();Complete(manager.StopAsync(CancellationToken.None));client.Dispose();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);return checks;
    }
    internal static int Run(string directory)
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        void Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>12)throw new TimeoutException("Native task transition timed out");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        void Complete(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        T Result<T>(Task<T> task){Complete(task);return task.Result;}
        IEnumerable<T> All<T>(StyledElement control)where T:StyledElement=>control.GetLogicalDescendants().OfType<T>().Distinct();
        Button Find(StyledElement control,string name)=>All<Button>(control).Single(b=>b.Name==name);
        void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Border Row(StyledElement control,string id)=>All<Border>(control).Single(b=>b.Name=="ImageTaskRow"&&(string?)b.Tag==id);
        var handler=new Handler();using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory),store=>new ImageGenerationClient(store,handler,null));
        var provider=new ImageGenerationProvider{Name="Native fixture service",Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="fixture-image",UseProxy=false}};owner.Settings.ImageProviders=new(){provider};owner.Settings.ActiveImageProviderId=provider.Id;owner.Save();
        var chatHost=new ChatWindow(owner,false);Window host=chatHost;host.Show();chatHost.ShowImages();var workspace=chatHost.Images;host.Width=960;host.Height=760;Dispatcher.UIThread.RunJobs();
        Check(workspace.Items.Count==3,"ThreeEntriesAppearInSameCenter: third task tab exists");workspace.SelectedIndex=2;Dispatcher.UIThread.RunJobs();var center=All<Grid>(workspace).Single(g=>g.Name=="ImageTaskCenterPage");
        var handles=new List<ImageGenerationTaskHandle>();foreach(var source in new[]{ImageGenerationSource.Workspace,ImageGenerationSource.Conversation,ImageGenerationSource.SettingsTest})handles.Add(Result(owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest{Provider=provider,Prompt=source==ImageGenerationSource.Conversation?"把陶杯放在窗边的柔和晨光中":source==ImageGenerationSource.SettingsTest?"测试当前图片服务连接":"为夏日旅行生成一张海边插画",Persistent=true},new ImageTaskOrigin{Source=source}),CancellationToken.None)));
        Until(()=>All<Border>(center).Count(b=>b.Name=="ImageTaskRow")==3);Check(new[]{"图像页","对话","设置测试"}.All(s=>All<TextBlock>(center).Any(t=>t.Text?.Contains(s)==true)),"ThreeEntriesAppearInSameCenter");All<CheckBox>(Row(center,handles[1].Id)).Single().IsChecked=true;
        // Hold queued Changed/UI notifications until after the click returns.
        // The action's own completion must already respect cleared selection.
        var batchCancel=Find(center,"CancelSelectedImageTasks");
        using(Dispatcher.UIThread.DisableProcessing())
        {
            Click(batchCancel);
            Check(!batchCancel.IsEnabled,"batch completion stays disabled before queued task notifications");
        }
        Until(()=>owner.ImageTasks.Get(handles[1].Id).State==ImageGenerationTaskState.Canceled);Check(!owner.ImageTasks.Get(handles[0].Id).IsTerminal&&!owner.ImageTasks.Get(handles[2].Id).IsTerminal,"CancelOnlySelectedJob");Until(()=>All<CheckBox>(center).Count(c=>c.IsChecked==true)==0);Check(!Find(center,"CancelSelectedImageTasks").IsEnabled,"terminal selection agrees with batch count");
        var subscriptions=typeof(ImageGenerationTaskManager).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!;int baseline=((Delegate)subscriptions.GetValue(owner.ImageTasks)!).GetInvocationList().Length;for(int visit=0;visit<3;visit++){workspace.SelectedIndex=0;Dispatcher.UIThread.RunJobs();workspace.SelectedIndex=2;Dispatcher.UIThread.RunJobs();}Check(((Delegate)subscriptions.GetValue(owner.ImageTasks)!).GetInvocationList().Length==baseline,"repeated task views do not duplicate subscriptions");
        var noticeField=typeof(DesktopController).GetField("imageNotification",BindingFlags.Instance|BindingFlags.NonPublic)!;Until(()=>noticeField.GetValue(owner) is Window);var completionNotice=(Window)noticeField.GetValue(owner)!;Check(completionNotice.Name=="ImageTaskCompletionNotification"&&!completionNotice.ShowActivated,"completion notification uses nonactivating native window");
        var clock=center.GetType().GetProperty("ClockRunning",BindingFlags.Instance|BindingFlags.NonPublic)!;Check((bool)clock.GetValue(center)!,"active center elapsed clock");host.WindowState=WindowState.Minimized;Dispatcher.UIThread.RunJobs();Check(!(bool)clock.GetValue(center)!,"minimized center stops elapsed clock");host.WindowState=WindowState.Normal;host.Hide();Dispatcher.UIThread.RunJobs();Check(!(bool)clock.GetValue(center)!,"hidden center stops elapsed clock");host.Show();Dispatcher.UIThread.RunJobs();Check((bool)clock.GetValue(center)!,"shown center resumes elapsed clock");
        var evidence=Path.Combine(Directory.GetCurrentDirectory(),"work","image-ui-evidence");Directory.CreateDirectory(evidence);foreach(var theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=host.CaptureRenderedFrame();frame!.Save(Path.Combine(evidence,"task-center-avalonia-"+theme+".png"),new PngBitmapEncoderOptions());}host.Width=420;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var frame=host.CaptureRenderedFrame())frame!.Save(Path.Combine(evidence,"task-center-avalonia-narrow.png"),new PngBitmapEncoderOptions());Check(All<Border>(center).Where(b=>b.Name=="ImageTaskRow").All(b=>b.Bounds.Width<=center.Bounds.Width),"narrow task rows fit available width");host.Width=960;DesktopTheme.Apply("light");
        foreach(var handle in handles)Complete(owner.ImageTasks.CancelAsync(handle.Id));Until(()=>handles.All(h=>owner.ImageTasks.Get(h.Id).IsTerminal));handler.Delay=false;Until(()=>All<Button>(Row(center,handles[1].Id)).Any(b=>b.Name=="RetryImageTask"));int before=handler.Calls;Click(Find(Row(center,handles[1].Id),"RetryImageTask"));Dispatcher.UIThread.RunJobs();var dialog=(TopLevel.GetTopLevel(workspace) as Window)!.OwnedWindows.Single(w=>w.Name=="ImageTaskRetryDialog");Check(All<TextBox>(dialog).Any(t=>t.Text=="把陶杯放在窗边的柔和晨光中"),"retry prompt preview");Click(Find(dialog,"ConfirmImageTaskRetry"));Until(()=>owner.ImageTasks.List().Count==4&&owner.ImageTasks.List().Last().IsTerminal);var retry=owner.ImageTasks.List().Last();Check(ReferenceEquals(completionNotice,noticeField.GetValue(owner)),"multiple terminal results reuse one completion window");Check(retry.Id!=handles[1].Id&&handler.Calls==before+1&&retry.RetryOfTaskId==handles[1].Id&&retry.DeliveryState==ImageTaskDeliveryState.Detached,"RetryCreatesNewIdAndOneRequest");
        var reply=owner.Session.History.BeginTurn("draw","text",out _);var delivery=Result(owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission("repair-fixture",new ImageGenerationRequest{Provider=provider,Prompt="repair",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=owner.Session.ImageConversationId,PersonaId=owner.Session.PersonaId,MessageId=reply.Id}),CancellationToken.None));Complete(delivery.Completion);Complete(owner.ImageTasks.SetDeliveryStateAsync(delivery.Id,ImageTaskDeliveryState.Failed));Until(()=>All<Button>(Row(center,delivery.Id)).Any(b=>b.Name=="RepairImageDelivery"));before=handler.Calls;Click(Find(Row(center,delivery.Id),"RepairImageDelivery"));Until(()=>owner.ImageTasks.Get(delivery.Id).DeliveryState==ImageTaskDeliveryState.Delivered);Check(handler.Calls==before&&owner.Session.History.Messages.Single(m=>m.Id==reply.Id).GeneratedImages.Any(),"DeliveryRepairDoesNotRegenerate");
        var repairHandles=new List<ImageGenerationTaskHandle>();foreach(var name in new[]{"same-owner-a","same-owner-b"}){var handle=Result(owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(name,new ImageGenerationRequest{Provider=provider,Prompt=name,Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=owner.Session.ImageConversationId,PersonaId=owner.Session.PersonaId,MessageId=reply.Id}),CancellationToken.None));Complete(handle.Completion);Complete(owner.ImageTasks.SetDeliveryStateAsync(handle.Id,ImageTaskDeliveryState.Failed));repairHandles.Add(handle);}before=handler.Calls;Complete(Task.WhenAll(repairHandles.Select(h=>owner.RepairImageDeliveryAsync(h.Id))));owner.Session.AddLiveTurn(owner.Session.PersonaId,"native edit after concurrent repairs","new reply");Complete(owner.Session.FlushHistoryAsync());Check(owner.Session.History.Messages.Single(m=>m.Id==reply.Id).GeneratedImages.Count==3&&owner.Session.History.Messages.Any(m=>m.Content=="native edit after concurrent repairs")&&handler.Calls==before,"native same-owner concurrent repairs preserve both images and new live message without HTTP");
        workspace.SelectedIndex=0;All<TextBox>(workspace).Single(t=>t.Name=="ImagePrompt").Text="first explicit submit";handler.Delay=true;var first=workspace.GenerateAsync();Until(()=>owner.ImageTasks.List().Last().State==ImageGenerationTaskState.Generating);var firstId=owner.ImageTasks.List().Last().Id;Check(Find(workspace,"StartImageGeneration").IsEnabled,"accepted submission unlocks form before HTTP completes");All<TextBox>(workspace).Single(t=>t.Name=="ImagePrompt").Text="second explicit submit";var second=workspace.GenerateAsync();Until(()=>owner.ImageTasks.List().Last().State==ImageGenerationTaskState.Queued);var secondId=owner.ImageTasks.List().Last().Id;workspace.Dispose();host.Close();var reopened=new ImageWorkspacePage(owner,new Window());host=new Window{Content=reopened,Width=900,Height=700};host.Show();reopened.SelectedIndex=2;Dispatcher.UIThread.RunJobs();Check(All<Border>(reopened).Any(b=>b.Name=="ImageTaskRow"&&(string?)b.Tag==firstId)&&!first.IsCompleted,"ReopenViewRebindsExistingJob");Complete(owner.ImageTasks.CancelAsync(secondId));Complete(owner.ImageTasks.CancelAsync(firstId));Complete(Task.WhenAll(first,second));
        var restart=Path.Combine(directory,"restart");new ImageGenerationTaskStore(restart).Save(new[]{new ImageGenerationTaskRecord{Id="old-job",RequestId="old",Origin=new ImageTaskOrigin{Source=ImageGenerationSource.Workspace},State=ImageGenerationTaskState.Generating,QueuedUtc=DateTime.UtcNow,Prompt="before restart",ReferenceImageIds=new()}});before=handler.Calls;var restored=new ImageGenerationTaskManager(new ImageGenerationTaskStore(restart),owner.ImageStore,owner.ImageClient,owner.ImageUsage);Complete(restored.RestoreAsync(CancellationToken.None));Check(restored.Get("old-job").State==ImageGenerationTaskState.Interrupted&&handler.Calls==before,"RestartShowsInterruptedWithoutNetwork");
        var concurrency=typeof(PetSettings).GetProperty("ImageGenerationConcurrency");Check(concurrency!=null&&(int)concurrency.GetValue(new PetSettings())! ==1,"SettingsDefaultsAndRoundTrip default");var settings=new SettingsWindow(owner);settings.SelectSection("behavior");settings.Show();Dispatcher.UIThread.RunJobs();var number=All<NumericUpDown>(settings).Single(n=>n.Name=="ImageGenerationConcurrency");number.Value=3;var motion=All<CheckBox>(settings).Single(c=>c.Name=="ImageGenerationMotionEnabled");motion.IsChecked=false;var saved=new SettingsStore(Path.Combine(owner.DataDirectory,"settings.json")).Load();Check((int)concurrency!.GetValue(saved)! ==3&&!saved.ImageGenerationMotionEnabled&&owner.ImageTasks.Concurrency==3,"SettingsDefaultsAndRoundTrip native UI applies");settings.Close();var notificationId=(string?)typeof(DesktopController).GetField("imageNotificationId",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner);Click(All<Button>(completionNotice).Single(b=>b.Content as string=="查看任务"));Dispatcher.UIThread.RunJobs();var notificationHost=(ChatWindow)typeof(DesktopController).GetField("fullChat",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;Check(notificationHost.CurrentPage=="images"&&All<Border>(notificationHost).Any(b=>b.Name=="ImageTaskRow"&&(string?)b.Tag==notificationId&&b.BorderThickness.Left==2),"completion notification navigates to corresponding native task row");notificationHost.Close();reopened.Dispose();host.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);foreach(var scenario in new[]{"recovery","purge","service","model"})checks+=RunFinalReview(Path.Combine(directory,"final-"+scenario),scenario);return checks;
    }
    static ClassicDesktopStyleApplicationLifetime NativeLifetime()
    {
        var lifetime=new ClassicDesktopStyleApplicationLifetime{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        typeof(ClassicDesktopStyleApplicationLifetime).GetMethod("SubscribeGlobalEvents",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(lifetime,null);
        return lifetime;
    }
    internal static int RunQuit(string directory)
    {
        void Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>8)throw new TimeoutException("Native lifetime exit did not finish");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        var lifetime=NativeLifetime();var handler=new Handler();using var owner=new DesktopController(lifetime,new TestRuntime(directory),store=>new ImageGenerationClient(store,handler,null));var host=new ChatWindow(owner,false);lifetime.MainWindow=host;host.Show();
        var provider=new ImageGenerationProvider{Name="exit fixture",Connection=new ConnectionSettings{BaseUrl="https://unused.invalid/v1",Model="fixture"}};var accepted=owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission("quit-fixture",new ImageGenerationRequest{Provider=provider,Prompt="pending at native quit",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Workspace}),CancellationToken.None);Until(()=>accepted.IsCompleted&&handler.Calls==1);var handle=accepted.Result;bool exited=false,hostClosed=false;host.Closed+=(_,_)=>hostClosed=true;lifetime.Exit+=(_,_)=>exited=true;bool immediate=lifetime.TryShutdown();if(immediate)throw new Exception("FAIL native ShutdownRequested must defer OS quit for graceful aggregate drain");Until(()=>exited);if(!hostClosed)throw new Exception("FAIL graceful native quit did not close real ChatWindow");if(!owner.ImageTasks.Get(handle.Id).IsTerminal)throw new Exception("FAIL native lifetime exited before manager drain");owner.Dispose();Until(()=>owner.ImageServicesShutdown.IsCompleted);Console.WriteLine("PASS native OS quit defers termination, drains manager, and disposes after writers finish");return 2;
    }
    internal static int RunHeldQuit(string directory)
    {
        Action diagnostic=()=>{};
        void Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>10){diagnostic();throw new TimeoutException("Held native exit did not finish");}Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        var lifetime=NativeLifetime();using var owner=new DesktopController(lifetime,new TestRuntime(directory));var host=new ChatWindow(owner,false);lifetime.MainWindow=host;host.Show();
        var api=new HeldConversationApi();var speech=new HeldFinalization();var setupSafety=Task.Run(async()=>{await Task.Delay(15000);api.Release.TrySetResult("cleanup");speech.Release.Set();});typeof(ConversationSession).GetField("api",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner.Session,api);typeof(ConversationSession).GetField("speech",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner.Session,speech);
        owner.Settings.AgentEnabled=false;owner.Settings.ReadReplies=false;owner.Settings.Text.BaseUrl="https://unused.invalid/v1";owner.Settings.Text.Model="fixture";owner.Settings.Text.ApiKey="local-fixture-only";var sending=owner.Session.SendTextAsync("hold active session on native quit",owner.Settings,"pet");Until(()=>api.Entered.Task.IsCompleted||sending.IsCompleted);if(!api.Entered.Task.IsCompleted){speech.Release.Set();sending.GetAwaiter().GetResult();throw new Exception("active Avalonia session did not enter held API");}
        bool exited=false,hostClosed=false;host.Closed+=(_,_)=>hostClosed=true;lifetime.Exit+=(_,_)=>{owner.Dispose();exited=true;};var watchdog=Task.Run(async()=>{await Task.Delay(7500);speech.Release.Set();});var watch=System.Diagnostics.Stopwatch.StartNew();
        diagnostic=()=>{var drain=(Task)typeof(DesktopController).GetField("applicationExitDrain",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;Console.WriteLine("HELD_EXIT sending="+sending.Status+" shutdown="+owner.ImageServicesShutdown.Status+" drain="+drain.Status+" busy="+owner.Session.IsBusy+" idle="+owner.Session.WaitForIdleAsync().IsCompleted+" exited="+exited+" hostClosed="+hostClosed+" apiReleased="+api.Release.Task.IsCompleted+" speechEntered="+speech.Entered.Task.IsCompleted+" speechReleased="+speech.Release.WaitOne(0));};
        try
        {
            if(lifetime.TryShutdown())throw new Exception("FAIL native held session quit was not deferred");Until(()=>exited);
            if(watch.Elapsed.TotalSeconds>=6.5)throw new Exception("FAIL actual native OS quit exceeded budget in synchronous active-session finalization");
            if(!hostClosed)throw new Exception("FAIL held native quit did not close real ChatWindow");
            if(speech.Entered.Task.IsCompleted||owner.ImageServicesShutdown.IsCompleted)throw new Exception("FAIL native OS quit finalized resources before active writer drained");
        }
        finally{api.Release.TrySetResult("finished");speech.Release.Set();diagnostic();}
        Until(()=>sending.IsCompleted&&owner.ImageServicesShutdown.IsCompleted);Console.WriteLine("PASS actual native OS quit defers held active-session finalization beyond deadline, then drains safely");return 3;
    }
    internal static int RunWriterGateQuit(string directory)
    {
        void Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>12)throw new TimeoutException("Writer gate native exit did not finish");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        var lifetime=NativeLifetime();using var owner=new DesktopController(lifetime,new TestRuntime(directory));var host=new ChatWindow(owner,false);lifetime.MainWindow=host;host.Show();Dispatcher.UIThread.RunJobs();
        if(lifetime.Windows.Count!=1)throw new Exception("Actual native ChatWindow is not tracked by lifetime");
        bool hostClosed=false;host.Closed+=(_,_)=>hostClosed=true;
        var original=(ISecretProtector)typeof(SecretProtection).GetField("current",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var held=new HeldAssetCommit(original);Task writer=Task.CompletedTask;bool exited=false;
        try
        {
            var image=owner.ImageStore.Save(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII="),new GeneratedImage{Prompt="held-writer-gate-marker"},false);
            ChatMessage user;var reply=owner.Session.History.BeginTurn("original image","text",out user);owner.Session.History.AttachGeneratedImage(reply.Id,image);
            SecretProtection.Configure(held);writer=Task.Run(()=>owner.Session.AddLiveTurn(owner.Skin.Id,"writer save","reply"));
            if(!held.Entered.Task.Wait(5000))throw new Exception("asset promotion did not enter held commit");
            lifetime.Exit+=(_,_)=>{owner.Dispose();exited=true;};var watchdog=Task.Run(async()=>{await Task.Delay(8000);held.Release.Set();});var watch=System.Diagnostics.Stopwatch.StartNew();
            if(lifetime.TryShutdown())throw new Exception("FAIL writer-gate native quit was not deferred");Until(()=>exited);
            if(watch.Elapsed.TotalSeconds>=6.5)throw new Exception("FAIL actual ChatWindow native quit blocked on held session writer gate after deadline");
            if(!hostClosed)throw new Exception("FAIL native lifetime did not actually close ChatWindow");
            if(writer.IsCompleted||owner.ImageServicesShutdown.IsCompleted)throw new Exception("FAIL native writer resources completed before held commit released");
        }
        finally{held.Release.Set();SecretProtection.Configure(original);}
        Until(()=>writer.IsCompleted&&owner.ImageServicesShutdown.IsCompleted);writer.GetAwaiter().GetResult();Console.WriteLine("PASS actual ChatWindow OS quit stays bounded with session gate held in asset promotion, then storage drains");return 3;
    }
    sealed class HeldAssetCommit:ISecretProtector
    {
        readonly ISecretProtector inner;internal readonly TaskCompletionSource<bool> Entered=new();internal readonly ManualResetEvent Release=new(false);
        internal HeldAssetCommit(ISecretProtector original){inner=original;}
        public byte[] Protect(byte[] plain){var json=System.Text.Encoding.UTF8.GetString(plain);if(json.Contains("\"Images\"")&&json.Contains("held-writer-gate-marker")){Entered.TrySetResult(true);Release.WaitOne();}return inner.Protect(plain);}
        public byte[] Unprotect(byte[] data)=>inner.Unprotect(data);
    }
    sealed class HeldConversationApi:IConversationApi
    {
        internal readonly TaskCompletionSource<bool> Entered=new();internal readonly TaskCompletionSource<string> Release=new();
        public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){Entered.TrySetResult(true);return Release.Task;}
        public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] bytes,CancellationToken token)=>Task.FromResult("");
        public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token)=>Task.FromResult(Array.Empty<byte>());
        public void Dispose(){}
    }
    sealed class HeldFinalization:IReplySpeech
    {
        internal readonly TaskCompletionSource<bool> Entered=new();internal readonly ManualResetEvent Release=new(false);
        public Task SpeakAsync(string text,PetSettings settings,CancellationToken token)=>Task.CompletedTask;
        public void SetVolume(int value){}public void Stop(){}public void Dispose(){Entered.TrySetResult(true);Release.WaitOne();}
    }
    sealed class Handler:HttpMessageHandler
    {
        internal int Calls;internal bool Delay=true;readonly byte[] bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Interlocked.Increment(ref Calls);if(Delay)await Task.Delay(Timeout.Infinite,token);return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"b64_json\":\""+Convert.ToBase64String(bytes)+"\"}]}")};}
    }
}
