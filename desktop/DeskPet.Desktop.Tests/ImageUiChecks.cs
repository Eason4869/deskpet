using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class ImageUiChecks
{
    internal static int Run(string directory)
    {
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new Exception("FAIL: image UI " + reason); checks++; }
        void Until(Func<bool> done, Func<string>? diagnostic = null) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!done()) { if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Image UI task timed out" + (diagnostic == null ? "" : ": " + diagnostic())); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } Dispatcher.UIThread.RunJobs(); }
        void Complete(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        var handler = new ImageHandler();
        using var owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(directory), store => new ImageGenerationClient(store, handler, null));
        Check(ReferenceEquals(owner.Session.ImageTasks,owner.ImageTasks),"controller and session share application queue");
        Check(owner.Session.ImageConversationId.StartsWith("history:")&&!owner.Session.ImageConversationId.Contains(owner.DataDirectory),"standalone Avalonia image owner binds private stable history identity");
        var window = new ChatWindow(owner, false); window.Show(); Dispatcher.UIThread.RunJobs();
        var workspace = window.Images;
        Check(window.CurrentPage == "chat", "starts on chat route"); window.ShowImages();
        Check(window.CurrentPage == "images" && workspace.ProviderChoices.Items.Count == 0 && !workspace.ReferenceButton.IsEnabled, "empty configuration routes to image page and disables editing");
        window.NavigateHistory(-1); Check(window.CurrentPage == "chat", "back restores chat"); window.NavigateHistory(1); Check(window.CurrentPage == "images", "forward restores image workspace instance");
        var provider = new ImageGenerationProvider { Name = "Fixture image service", Connection = new ConnectionSettings { BaseUrl = "https://images.example/v1", Model = "ui-image-model", ApiKey = "fixture-secret", UseProxy = false }, SupportsEdits = false };
        var editProvider = provider.Copy(); editProvider.Id = Guid.NewGuid().ToString("N"); editProvider.Name = "Fixture editor"; editProvider.SupportsEdits = true;
        owner.Settings.ImageProviders = new() { provider, editProvider }; owner.Settings.ActiveImageProviderId = provider.Id; owner.Save(); Dispatcher.UIThread.RunJobs();
        Check(workspace.ProviderChoices.Items.Count == 2 && !workspace.ReferenceButton.IsEnabled, "configured providers refresh without opening a new page");
        workspace.ProviderChoices.SelectedIndex = 1; Check(workspace.ReferenceButton.IsEnabled && owner.Settings.ActiveImageProviderId == editProvider.Id, "provider selection persists and enables supported reference upload");
        using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        byte[] png; using (var output = new MemoryStream()) { bitmap.Save(output); png = output.ToArray(); } handler.Png = png;
        workspace.SetReference(png, "fixture.png");
        workspace.ProviderChoices.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var prompt = workspace.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(t => t.Name == "ImagePrompt"); prompt.Text = "portable test image";
        try { Complete(workspace.GenerateAsync()); throw new Exception("FAIL: unsupported edit accepted"); } catch (InvalidOperationException) { checks++; }
        Check(handler.Requests == 0, "unsupported reference edit is rejected before any request");
        workspace.ProviderChoices.SelectedIndex = 1;
        handler.Gate=new TaskCompletionSource<bool>();var generation = workspace.GenerateAsync();Until(()=>workspace.GetLogicalDescendants().OfType<ImageGenerationPendingCard>().Any(c=>c.ClockRunning));var pendingCard=workspace.GetLogicalDescendants().OfType<ImageGenerationPendingCard>().First();checks+=ImageMotionUiChecks.Run(owner,window,pendingCard);handler.Gate.SetResult(true);handler.Gate=null; Check(workspace.IsGenerating, "generation exposes running state");
        window.NavigateHistory(-1); Check(window.CurrentPage == "chat" && workspace.IsGenerating, "leaving image page retains running request");
        Complete(generation); Complete(handler.LastResponse);
        Check(handler.Requests == 1 && handler.LastPath.EndsWith("/images/edits") && owner.ImageStore.List().Count == 1, "reference generation performs exactly one edit and commits gallery asset");
        var image = owner.ImageStore.List()[0];
        var revisionField=typeof(ImageWorkspacePage).GetField("galleryRevision",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var loadedField=typeof(ImageWorkspacePage).GetField("loadedGallery",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var statusField=typeof(ImageWorkspacePage).GetField("galleryCount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        IList<GeneratedImage> GalleryItems() => (IList<GeneratedImage>)loadedField.GetValue(workspace)!;
        string GalleryDiagnostic() => "search="+workspace.Search.Text+" cards="+workspace.GalleryCount+" revision="+revisionField.GetValue(workspace)+" status="+((TextBlock)statusField.GetValue(workspace)!).Text+" loaded="+String.Join(",",GalleryItems().Select(i=>i.Id+":"+i.Model))+" expected="+image.Id+":"+image.Model;
        void AwaitGallery(int count) => Until(()=>workspace.GalleryCount==count&&GalleryItems().Count==count&&(count==0||GalleryItems()[0].Id==image.Id),GalleryDiagnostic);
        window.ShowImages();Dispatcher.UIThread.RunJobs();Check(workspace.GetLogicalDescendants().OfType<ImageGenerationPendingCard>().Any(c=>c.GetLogicalDescendants().OfType<GeneratedImageCard>().Any()&&!c.ClockRunning),"SuccessReplacesPlaceholderInPlace and stops frame clock"); workspace.SelectedIndex = 1; workspace.RefreshGallery();
        AwaitGallery(1); Check(workspace.GalleryCount == 1, "workspace generation appears in gallery"); workspace.Search.Text = "not-present"; AwaitGallery(0); Check(workspace.GalleryCount == 0, "gallery filtering excludes mismatched prompt/model");
        var queryGate=typeof(GeneratedImageStore).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner.ImageStore)!;
        Monitor.Enter(queryGate);
        try{workspace.Search.Text="ui-image-model";Dispatcher.UIThread.RunJobs();Check(workspace.GalleryCount==0,"gallery query stays asynchronous while the store is busy");Console.WriteLine("Blocked gallery query: "+GalleryDiagnostic());}
        finally{Monitor.Exit(queryGate);}
        AwaitGallery(1);Check(workspace.GalleryCount==1,"gallery model search finds asset");Console.WriteLine("Completed gallery query: "+GalleryDiagnostic());
        workspace.Prepare(image); Check(workspace.SelectedIndex == 0 && prompt.Text == image.Prompt && !workspace.IsGenerating, "regenerate prepares explicit new request without automatic call");
        handler.Delay = true; prompt.Text = "cancel this generation"; var cancelled = workspace.GenerateAsync(); Check(workspace.IsGenerating, "second generation runs");
        workspace.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "CancelImageGeneration").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Complete(cancelled);
        Check(owner.ImageStore.List().Count == 1 && !workspace.IsGenerating, "cancel creates no fake gallery asset");
        var imageLog = new ConversationLog(); var reply = imageLog.BeginTurn("draw", "text", out _); imageLog.Update(reply.Id, "", "complete"); reply = imageLog.AttachGeneratedImage(reply.Id, image); var replyView = new ChatMessageView(reply, owner, window);
        var card = replyView.GetLogicalDescendants().OfType<GeneratedImageCard>().Single(); Check(card != null, "assistant message renders generated image card even without text");
        Complete(card.CopyAsync()); using (var transfer = window.Clipboard!.TryGetDataAsync().GetAwaiter().GetResult())
        {
            Check(transfer!.Contains(Avalonia.Input.DataFormat.Bitmap), "copy writes bitmap clipboard format");
            var read = transfer.TryGetBitmapAsync(); Complete(read); using var copied = new MemoryStream(); read.Result!.Save(copied); Check(copied.Length > 0, "copied image remains usable after copy operation completes");
        }
        card.ShowPreview(); Dispatcher.UIThread.RunJobs();
        var preview = owner.Pet.OwnedWindows.FirstOrDefault(w => w.Name == "GeneratedImagePreviewWindow") ?? window.OwnedWindows.FirstOrDefault(w => w.Name == "GeneratedImagePreviewWindow");
        Check(preview != null, "card opens image preview window"); preview?.Close(); replyView.DisposeImages();
        window.Width = 430; window.Height = 650; workspace.SelectedIndex = 1; workspace.Search.Text = ""; AwaitGallery(1);
        var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(artifacts);
        foreach (var theme in new[] { "light", "dark" })
        {
            DesktopTheme.Apply(theme); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = window.CaptureRenderedFrame();
            Check(frame != null && frame.PixelSize.Width == 430 && workspace.GalleryCount == 1, "gallery renders in narrow " + theme + " window"); frame!.Save(Path.Combine(artifacts, "image-gallery-" + theme + ".png"), new PngBitmapEncoderOptions());
        }
        DesktopTheme.Apply(owner.Settings.ThemeMode); window.Width = 880;
        var grouped=new ChatMessage("assistant",""){ImageGenerationTaskIds=new(){"slot-a","slot-b","slot-c"}};typeof(ChatMessage).GetMethod("SetGeneratedImages",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(grouped,new object[]{new List<GeneratedImage>()});
        foreach(var id in grouped.ImageGenerationTaskIds){var copy=image.Copy();copy.Id=Guid.NewGuid().ToString("N");copy.ImageJobId=id;grouped.GeneratedImages.Add(owner.ImageStore.Save(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"assets","pet-preview-hd.png")),copy,true));}
        var groupView=new ChatMessageView(grouped,owner,window);var groupHost=new Window{Width=760,Height=700,Content=groupView};groupHost.Show();Dispatcher.UIThread.RunJobs();var group=groupView.GetLogicalDescendants().OfType<GeneratedImageGroupCard>().Single();
        group.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string?)b.Tag=="slot-b").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));typeof(ChatMessage).GetProperty("Content")!.SetValue(grouped,"streamed text");groupView.Update(grouped);grouped.ImageGenerationTaskIds.Add("pending-slot");groupView.Update(grouped);Dispatcher.UIThread.RunJobs();Check(ReferenceEquals(group,groupView.GetLogicalDescendants().OfType<GeneratedImageGroupCard>().Single())&&group.SelectedId=="slot-b","stable group and selected ID survive text streaming and appended tasks");
        var selectedCard=group.GetLogicalDescendants().OfType<GeneratedImageCard>().Single();var metadata=(GeneratedImage)typeof(GeneratedImageCard).GetField("metadata",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(selectedCard)!;Check(metadata.Id==grouped.GeneratedImages[1].Id,"group actions use selected asset");grouped.GeneratedImages.Clear();groupView.Update(grouped);Check(group.GetLogicalDescendants().OfType<GeneratedImageCard>().Any(),"retention-dropped snapshots resolve by job ID");
        handler.Delay=true;ImageGenerationTaskHandle? runningGroup=null,canceledGroup=null;Complete(Task.Run(async()=>{runningGroup=await owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission("group-running",new ImageGenerationRequest{Provider=owner.Settings.SelectedImageProvider(),Prompt="running fixture",Persistent=true},new ImageTaskOrigin()),CancellationToken.None);canceledGroup=await owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission("group-canceled",new ImageGenerationRequest{Provider=owner.Settings.SelectedImageProvider(),Prompt="queued fixture",Persistent=true},new ImageTaskOrigin()),CancellationToken.None);await owner.ImageTasks.CancelAsync(canceledGroup.Id);}));grouped.ImageGenerationTaskIds.Add(runningGroup!.Id);grouped.ImageGenerationTaskIds.Add(canceledGroup!.Id);groupView.Update(grouped);Dispatcher.UIThread.RunJobs();group.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string?)b.Tag==runningGroup.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Check(group.GetLogicalDescendants().OfType<ImageGenerationPendingCard>().Any(),"mixed running slot uses native pending lifecycle");group.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string?)b.Tag==canceledGroup.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Check(group.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="已取消"),"mixed canceled slot shows terminal state");group.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string?)b.Tag=="slot-b").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var stableMain=group.GetLogicalDescendants().OfType<GeneratedImageCard>().Single();stableMain.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string=="详情").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var stableDetails=window.OwnedWindows.Single(w=>w.Title=="图片详情");var stableInfo=stableDetails.GetLogicalDescendants().OfType<SelectableTextBlock>().Single();stableInfo.SelectionStart=0;stableInfo.SelectionEnd=3;groupHost.Activate();var stableFocus=stableMain.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImagePreview");Check(stableFocus.Focus(),"selected group action can receive keyboard focus");var stableSlots=group.GetLogicalDescendants().OfType<Button>().Where(b=>b.Name=="GeneratedImageGroupSlot").ToArray();var stableThumbs=stableSlots.Select(b=>(b.Content as Image)?.Source).ToArray();Complete(owner.ImageTasks.CancelAsync(runningGroup.Id));handler.Delay=false;Until(()=>owner.ImageTasks.Get(runningGroup.Id).IsTerminal);groupView.Update(grouped);Dispatcher.UIThread.RunJobs();Check(ReferenceEquals(stableMain,group.GetLogicalDescendants().OfType<GeneratedImageCard>().Single())&&stableDetails.IsVisible&&stableInfo.SelectionEnd==3,"unrelated task transition preserves selected control and open details state");Check(stableFocus.IsFocused,"unrelated task transition preserves selected action focus");Check(stableSlots.All(b=>group.GetLogicalDescendants().OfType<Button>().Contains(b))&&stableSlots.Select((b,i)=>b.Content is not Image image||ReferenceEquals(image.Source,stableThumbs[i])).All(v=>v),"unrelated task transition reuses existing slots and decoded thumbnails");stableDetails.Close();
        var groupArtifacts=Path.Combine(Directory.GetCurrentDirectory(),"work","task5-avalonia-evidence");Directory.CreateDirectory(groupArtifacts);
        foreach(var theme in new[]{"light","dark"})foreach(var width in new[]{760,390}){DesktopTheme.Apply(theme);groupHost.Background=DesktopTheme.Surface;groupHost.Width=width;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=groupHost.CaptureRenderedFrame();frame!.Save(Path.Combine(groupArtifacts,"group-"+theme+"-"+width+".png"),new PngBitmapEncoderOptions());Check(group.GetLogicalDescendants().OfType<StackPanel>().Any(p=>p.Children.OfType<Button>().Any(b=>b.Name=="GeneratedImageGroupSlot")&&p.Orientation==(width>=480?Avalonia.Layout.Orientation.Vertical:Avalonia.Layout.Orientation.Horizontal)),"group responsive rail "+theme+width);}
        group.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string?)b.Tag=="pending-slot").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(group.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="图片记录不可用"),"unavailable slot switches beside success");for(int slot=grouped.ImageGenerationTaskIds.Count;slot<20;slot++)grouped.ImageGenerationTaskIds.Add("extra-"+slot);groupView.Update(grouped);Dispatcher.UIThread.RunJobs();Check(group.GetLogicalDescendants().OfType<Button>().Count(b=>b.Name=="GeneratedImageGroupSlot")==20,"twenty fixed slots remain switchable");var removedSlot=group.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string?)b.Tag=="slot-c");grouped.ImageGenerationTaskIds.Remove("slot-c");groupView.Update(grouped);Dispatcher.UIThread.RunJobs();Check(removedSlot.Content==null&&!group.GetLogicalDescendants().OfType<Button>().Contains(removedSlot),"removed slot releases cached thumbnail visual");group.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GeneratedImageGroupSlot"&&(string?)b.Tag=="slot-b").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var detachedMain=group.GetLogicalDescendants().OfType<GeneratedImageCard>().Single();groupHost.Content=null;Dispatcher.UIThread.RunJobs();Check(!group.GetLogicalDescendants().OfType<Button>().Any(b=>b.Name=="GeneratedImageGroupSlot")&&typeof(GeneratedImageCard).GetField("thumbnail",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(detachedMain)==null,"detached group releases thumbnail cache and disposes main bitmap");groupHost.Content=groupView;Dispatcher.UIThread.RunJobs();Check(group.SelectedId=="slot-b"&&group.GetLogicalDescendants().OfType<Button>().Count(b=>b.Name=="GeneratedImageGroupSlot")==19&&group.GetLogicalDescendants().OfType<GeneratedImageCard>().Any(),"reattached group restores slots and selected image after cache release");groupHost.Close();groupView.DisposeImages();DesktopTheme.Apply("light");

        File.Delete(owner.ImageStore.PathFor(image.Id)); var missing = new GeneratedImageCard(owner, window, image); Check(missing.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("缺失或损坏") == true), "missing history asset uses recoverable placeholder"); missing.Dispose();
        var settingsWindow = new SettingsWindow(owner); settingsWindow.SelectSection("image"); settingsWindow.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var editor = settingsWindow.GetLogicalDescendants().OfType<ImageProviderEditor>().Distinct().Single();
        T Field<T>(string name) where T : Control => editor.GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
        Check(Field<TextBox>("ImageProviderKey").PasswordChar == '●', "model configuration image tab masks credentials");
        editor.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "AddImageProvider").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(owner.Settings.ImageProviders.Count == 2 && Field<NumericUpDown>("ImageProviderTimeout").Value == 300 && Field<ComboBox>("ImageProviderSize").SelectedIndex == 0, "new service draft preserves independent timeout and default parameter choices");
        Field<TextBox>("ImageProviderName").Text = "third image service"; Field<TextBox>("ImageProviderAddress").Text = "https://images.example/v1"; Field<TextBox>("ImageProviderModel").Text = "ui-model-third"; Field<TextBox>("ImageProviderKey").Text = "private-third";
        var textModel = owner.Settings.Text.Model;
        editor.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "SaveImageProvider").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(owner.Settings.ImageProviders.Count == 3 && owner.Settings.SelectedImageProvider()!.Connection.ApiKey == "private-third" && owner.Settings.Text.Model == textModel && !File.ReadAllText(Path.Combine(owner.DataDirectory, "settings.json")).Contains("private-third"), "image provider saves encrypted credentials without changing text model");
        handler.Delay = false; var test = editor.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Content as string == "测试生成（会调用服务并计费）"); test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Until(() => test.IsEnabled);
        Check(owner.ImageStore.List().Count == 5 && handler.LastPath.EndsWith("/images/generations"), "settings test generation previews a real fixture result and shares gallery"); settingsWindow.Close();
        checks += GalleryLayoutUiChecks.Run(owner, window);
        checks += ImagePresetUiChecks.Run(owner, window, () => handler.Requests);
        checks += ImageEditorConsistencyUiChecks.Run(owner);
        // Disposed surfaces detach from a shared job; they are never its cancellation owner.
        handler.Delay=false;var detachedHost=new Window();var detachedPage=new ImageWorkspacePage(owner,detachedHost);detachedHost.Content=detachedPage;detachedHost.Show();Dispatcher.UIThread.RunJobs();
        detachedPage.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(t=>t.Name=="ImagePrompt").Text="closed view fixture";
        int beforeCloseRequests=handler.Requests;var detachedGeneration=detachedPage.GenerateAsync();Until(()=>handler.Requests>beforeCloseRequests);var detachedId=owner.ImageTasks.List().Last().Id;detachedPage.Dispose();detachedHost.Close();Complete(detachedGeneration);
        Check(owner.ImageTasks.Get(detachedId).State==ImageGenerationTaskState.Succeeded,"ViewCloseKeepsTaskAccessible: disposed workspace completes in shared manager");
        var deleteHost=new Window();using var deleteEditor=new ImageProviderEditor(owner,deleteHost);deleteHost.Content=deleteEditor;deleteHost.Show();Dispatcher.UIThread.RunJobs();handler.Delay=true;int beforeDeleteRequests=handler.Requests;
        var deleteTest=deleteEditor.GetLogicalDescendants().OfType<Button>().Distinct().Single(b=>b.Content as string=="测试生成（会调用服务并计费）");deleteTest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>handler.Requests>beforeDeleteRequests);var deletingId=owner.ImageTasks.List().Last().Id;
        deleteEditor.GetLogicalDescendants().OfType<Button>().Distinct().Single(b=>b.Name=="DeleteImageProvider").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var deleteDialog=deleteHost.OwnedWindows.Single();
        Check(deleteDialog.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text!=null&&t.Text.Contains("1 个未完成")),"provider deletion displays running shared job count");
        deleteDialog.GetLogicalDescendants().OfType<Button>().Distinct().Single(b=>b.Content as string=="取消任务并删除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>owner.ImageTasks.Get(deletingId).IsTerminal);
        Check(owner.ImageTasks.Get(deletingId).State==ImageGenerationTaskState.Canceled,"provider deletion explicitly cancels shared test job");deleteHost.Close();handler.Delay=false;
        using var pendingCancel = new CancellationTokenSource();
        var question = owner.Questions.AskAsync(new AgentQuestionRequest { SessionId = owner.Session.PersonaId, TaskId = "portable-question", CallId = "pick", Question = "选择输出格式", Options = new[] { "PNG", "JPEG" } }, pendingCancel.Token); Dispatcher.UIThread.RunJobs();
        var option = window.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == "PNG");
        // The image page can remain selected while a question arrives; returning to chat reveals it.
        window.NavigateHistory(-1); Dispatcher.UIThread.RunJobs(); option ??= window.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == "PNG");
        Check(option != null, "ask_user choices appear in original chat"); option!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Complete(question); Check(question.Result.OptionIndex == 0 && question.Result.Text == "PNG", "answer submits original session/task/call identity");
        var sibling = new ChatWindow(owner, true); sibling.Show(); Dispatcher.UIThread.RunJobs();
        var unanswered = owner.Questions.AskAsync(new AgentQuestionRequest { SessionId = window.QuestionSessionId, TaskId = "portable-close-question", CallId = "waiting", Question = "关闭窗口测试", Options = new[] { "继续", "结束" } }, CancellationToken.None); Dispatcher.UIThread.RunJobs();
        window.Close(); Dispatcher.UIThread.RunJobs();
        Check(!unanswered.IsCompleted && owner.Questions.Requests.Count == 1, "closing one chat preserves pending question while visible sibling can answer");
        sibling.Close(); Until(() => unanswered.IsCompleted); Check(unanswered.IsCanceled && owner.Questions.Requests.Count == 0, "closing last chat cancels pending question without leaving task waiting");
        owner.Pet.Close(); Dispatcher.UIThread.RunJobs(); owner.Dispose(); Complete(owner.ImageServicesShutdown); return checks;
    }
    private sealed class ImageHandler : HttpMessageHandler
    {
        internal TaskCompletionSource<bool>? Gate;internal byte[] Png = Array.Empty<byte>(); internal int Requests; internal bool Delay; internal string LastPath = ""; internal Task LastResponse = Task.CompletedTask;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if(Gate!=null)await Gate.Task.WaitAsync(cancellationToken);Interlocked.Increment(ref Requests); LastPath = request.RequestUri!.AbsolutePath;
            if (Delay) await Task.Delay(Timeout.Infinite, cancellationToken); else await Task.Delay(80, cancellationToken);
            var json = JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(Png) } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
