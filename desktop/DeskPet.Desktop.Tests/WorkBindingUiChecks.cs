using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class WorkBindingUiChecks
{
    internal static int Run(string directory,string? language=null)
    {
        int checks=0;
        void Check(bool value,string description){if(!value)throw new Exception("Work binding: "+description);checks++;}
        void Complete(Task task){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>15000)throw new TimeoutException("Work binding fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.Combine(directory,"history"));
        string ordinary=Path.Combine(directory,"history","ordinary.history");File.WriteAllBytes(ordinary,new byte[]{1,2,3,4,5});byte[] original=SHA256.HashData(File.ReadAllBytes(ordinary));
        var protector=new TestProtector();
        if(language!=null){SecretProtection.Configure(protector);new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false});}
        var runtime=new BindingRuntime(directory,protector);
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),runtime);
        void SeedQueue(string id,string text)
        {var root=Path.Combine(directory,"conversations",id);Directory.CreateDirectory(root);using var memory=new MemoryStream();new DataContractJsonSerializer(typeof(Dictionary<string,List<QueuedMessage>>)).WriteObject(memory,new Dictionary<string,List<QueuedMessage>>{{"work",new List<QueuedMessage>{new QueuedMessage{Id=Guid.NewGuid().ToString("N"),Text=text}}}});using var file=File.Create(Path.Combine(root,"queued-messages.json"));new DataContractJsonSerializer(typeof(QueueFixture)).WriteObject(file,new QueueFixture{Version=1,Payload=Convert.ToBase64String(SecretProtection.Protect(memory.ToArray()))});}
        string savedProject="",savedConversation="";
        var first=new ChatWindow(owner,false);var second=new ChatWindow(owner,false);first.Show();second.Show();
        try
        {
            Check(ReferenceEquals(first.BoundSession,owner.Session)&&ReferenceEquals(first.BoundQueue,owner.Queue),"unbound windows retain ordinary session and queue");
            var create=owner.WorkBindings.CreateProjectAsync("Fixture",directory);Complete(create);var project=create.Result;var conversation=project.Conversations.Single();savedProject=project.Id;savedConversation=conversation.Id;SeedQueue(conversation.Id,"first queued");
            Complete(first.BindWorkAsync(project.Id,conversation.Id));Complete(second.BindWorkAsync(project.Id,conversation.Id));
            Check(first.BoundSession!=owner.Session&&ReferenceEquals(first.BoundSession,second.BoundSession)&&ReferenceEquals(first.BoundQueue,second.BoundQueue),"two windows reuse one persistent session and queue");
            Check(first.QuestionSessionId==conversation.Id&&second.QuestionSessionId==conversation.Id,"questions use the persistent conversation identity");
            Check(first.BoundSession.ImageConversationId==conversation.Id&&ReferenceEquals(first.BoundSession.ImageTasks,owner.ImageTasks)&&ReferenceEquals(first.BoundSession.ImageStore,owner.ImageStore),"bound image tools retain their persistent source while sharing application image services");
            Check(first.EffectiveSettings().AgentWorkspace==Path.GetFullPath(directory).TrimEnd('\\','/'),"bound settings use the selected project directory");
            owner.Settings.TextProviders.Single(provider=>provider.Id==owner.Settings.ActiveTextProviderId).Models.Add("binding-model-fixture");
            owner.WorkBindings.SelectModel(conversation.Id,owner.Settings.ActiveTextProviderId,"binding-model-fixture");
            Check(first.EffectiveSettings().Text.Model=="binding-model-fixture"&&owner.Settings.Text.Model!="binding-model-fixture","bound model selection does not change ordinary chat settings");
            first.BoundSession.AddLiveTurn(first.BoundSession.PersonaId,"bound user","bound reply");Dispatcher.UIThread.RunJobs();
            Check(first.GetLogicalDescendants().OfType<ChatMessageView>().Count()==2&&second.GetLogicalDescendants().OfType<ChatMessageView>().Count()==2,"shared bound history events refresh both owning windows");
            var draft=first.GetLogicalDescendants().OfType<TextBox>().Single(control=>control.Name=="ChatDraft");draft.Text="first work draft";
            Complete(first.BindWorkAsync(null,null));Check(ReferenceEquals(first.BoundSession,owner.Session)&&draft.Text!="first work draft","returning to normal chat restores its own draft");
            Complete(first.BindWorkAsync(project.Id,conversation.Id));Check(draft.Text=="first work draft","work draft survives switching away and reopening");
            Check(first.BoundQueue.IsPaused,"restored work queues remain paused without model or network calls");
            first.AddAttachments(new[]{ChatAttachment.FromText("first.txt","first attachment")});
            var otherCreate=owner.WorkBindings.CreateProjectAsync("Second fixture",directory);Complete(otherCreate);var other=otherCreate.Result;
            SeedQueue(other.Conversations.Single().Id,"second queued");Complete(first.BindWorkAsync(other.Id,other.Conversations.Single().Id));
            Check(first.BoundQueue.Items.Single().Text=="second queued"&&second.BoundQueue.Items.Single().Text=="first queued","queued messages remain in their original work conversation");
            Check(!first.GetLogicalDescendants().OfType<Border>().Any(control=>control.Name=="AttachmentCard"),"attachments do not cross work bindings");
            using var questionsCancellation=new CancellationTokenSource();
            var firstQuestion=owner.Questions.AskAsync(new AgentQuestionRequest{SessionId=first.QuestionSessionId,TaskId="first-question",CallId="first-call",Question="Only first window"},questionsCancellation.Token);
            var secondQuestion=owner.Questions.AskAsync(new AgentQuestionRequest{SessionId=second.QuestionSessionId,TaskId="second-question",CallId="second-call",Question="Only second window"},questionsCancellation.Token);Dispatcher.UIThread.RunJobs();
            bool HasQuestion(ChatWindow host,string text)=>host.GetLogicalDescendants().OfType<TextBlock>().Any(block=>block.Text==text);
            Check(HasQuestion(first,"Only first window")&&!HasQuestion(first,"Only second window")&&HasQuestion(second,"Only second window")&&!HasQuestion(second,"Only first window"),"ask_user cards render only in their source conversation");questionsCancellation.Cancel();
            first.BoundSession.AddLiveTurn(first.BoundSession.PersonaId,"second user","second reply");Dispatcher.UIThread.RunJobs();
            Check(first.BoundSession.ExportHistory().Contains("second reply")&&!first.BoundSession.ExportHistory().Contains("bound reply"),"export resolves the active work history");
            first.BoundSession.Clear();Dispatcher.UIThread.RunJobs();Check(first.BoundSession.History.Messages.Count==0&&second.BoundSession.History.Messages.Count==2,"clear affects only the bound history and refreshes its view");
            var workSettings=owner.WorkBindings.ProjectSettings(other.Id);Check(workSettings.AgentWorkspace==first.EffectiveSettings().AgentWorkspace,"work settings resolve the bound project");
            Check(owner.WorkBindings.TryChangeProject(other.Id,value=>value.Goal="second goal")&&first.EffectiveSettings().AgentGoal=="second goal"&&second.EffectiveSettings().AgentGoal!="second goal"&&owner.Settings.AgentGoal!="second goal","project settings update only workspaces in that project");
            Complete(first.BindWorkAsync(project.Id,conversation.Id));Check(first.GetLogicalDescendants().OfType<Border>().Any(control=>control.Name=="AttachmentCard"),"returning to a work binding restores only its attachment buffer");
            string catalogPath=Path.Combine(directory,"projects.json");var independentStore=new ProjectCatalogStore(catalogPath);var ordinaryCatalog=independentStore.Load();
            ordinaryCatalog.UnassignedConversations.Add(new ConversationDefinition{Id="ordinary-added-by-other-host",Name="ordinary",Mode="chat",PetId=owner.Skin.Id,SectionId=ProjectCatalogStore.DefaultChatSectionId});independentStore.Save(ordinaryCatalog);
            Complete(owner.WorkBindings.CreateConversationAsync(project.Id,"Another persistent work conversation"));
            Check(independentStore.Load().UnassignedConversations.Any(value=>value.Id=="ordinary-added-by-other-host"),"saving work bindings preserves ordinary metadata changed by another host");
            byte[] catalogHash=SHA256.HashData(File.ReadAllBytes(catalogPath));var oldBinding=first.BoundSession;bool missingRefused=false;
            try{Complete(first.BindWorkAsync("missing-project","missing-conversation"));}catch(InvalidOperationException){missingRefused=true;}
            Check(missingRefused&&ReferenceEquals(first.BoundSession,oldBinding)&&catalogHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(catalogPath))),"invalid binding keeps the prior window and exact catalog bytes");
            using(var cancelApproval=new CancellationTokenSource())
            {
                var request=new AgentApprovalRequest{ProjectId=project.Id,SessionId=first.QuestionSessionId,TaskId="approval-task",CallId="approval-call",ToolName="start_job",Resource=directory,Arguments="command=fixture; timeout_seconds=60",IsolationDescription="sandbox fixture",PayloadHash="fixture-hash"};
                var approval=owner.WorkBindings.Manager.ApprovalProvider;var pending=((ICancellableAgentApprovalProvider)approval).ApproveAsync(request,cancelApproval.Token);Dispatcher.UIThread.RunJobs();
                var dialog=first.OwnedWindows.Concat(second.OwnedWindows).Single(window=>window.Name=="WorkApprovalDialog");var details=dialog.GetLogicalDescendants().OfType<TextBox>().Single(value=>value.Name=="WorkApprovalDetails").Text??"";
                Check(details.Contains(project.Id)&&details.Contains(first.QuestionSessionId)&&details.Contains("approval-call")&&details.Contains("timeout_seconds=60")&&details.Contains(directory)&&details.Contains("sandbox fixture")&&details.Contains("fixture-hash"),"approval exposes the immutable source, complete payload, directory, isolation, timeout and hash");
                cancelApproval.Cancel();Complete(pending);Check(!pending.Result,"cancelled approval cannot authorize a different or hidden window");
            }
            string captures=Path.GetFullPath(Path.Combine("work","agent-jobs-ui","avalonia-binding",TextResources.CurrentLanguage));Directory.CreateDirectory(captures);
            foreach(string theme in new[]{"light","dark"})
            {
                DesktopTheme.Apply(theme);var picker=new WorkProjectPicker(owner,first,first.BindWorkAsync);picker.Show(first);picker.Width=390;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Check(picker.Width==390&&picker.GetLogicalDescendants().OfType<Button>().Count(button=>button.Name=="OpenWorkConversation")>=3,"narrow "+theme+" picker retains all persisted work choices");
                using(var capture=picker.CaptureRenderedFrame()){Check(capture!=null,"work picker actually renders "+theme);capture!.Save(Path.Combine(captures,"picker-"+theme+"-390.png"),new PngBitmapEncoderOptions());}picker.Close();
            }
            DesktopTheme.Apply("light");
            runtime.PendingPermission=new TaskCompletionSource<bool>();int microphones=runtime.RecorderCreations;
            var dictate=(Task)typeof(ChatWindow).GetMethod("DictateAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(first,null)!;
            Complete(first.BindWorkAsync(other.Id,other.Conversations.Single().Id));runtime.PendingPermission.SetResult(true);Complete(dictate);
            Check(runtime.RecorderCreations==microphones,"a microphone permission reply from the old binding cannot start dictation in the new one");
            Complete(first.BindWorkAsync(project.Id,conversation.Id));
            var session=first.BoundSession;first.Close();Check(ReferenceEquals(second.BoundSession,session),"closing one view retains the shared workspace");
            var reopened=new ChatWindow(owner,false);reopened.Show();Complete(reopened.BindWorkAsync(project.Id,conversation.Id));Check(ReferenceEquals(reopened.BoundSession,session),"reopening a view reuses stable workspace ownership");reopened.Close();
            using var denied=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new BindingRuntime(directory,protector));
            var deniedWindow=new ChatWindow(denied,false);deniedWindow.Show();bool refused=false;try{Complete(deniedWindow.BindWorkAsync(project.Id,conversation.Id));}catch(IOException){refused=true;}
            Check(refused&&ReferenceEquals(deniedWindow.BoundSession,denied.Session),"second host cannot bind a work writer and retains ordinary chat");deniedWindow.Close();denied.Pet.Close();denied.Dispose();Complete(denied.ImageServicesShutdown);
            Check(SHA256.HashData(File.ReadAllBytes(ordinary)).SequenceEqual(original),"work binding never moves or changes ordinary encrypted history");
            Check(first.BoundSession==session,"closed view does not replace its captured session identity");
        }
        finally{first.Close();second.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
        using(var restarted=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new BindingRuntime(directory,protector)))
        {
            var window=new ChatWindow(restarted,false);window.Show();Complete(window.BindWorkAsync(savedProject,savedConversation));
            Check(window.QuestionSessionId==savedConversation&&window.BoundSession.KernelSession.ProjectId==savedProject,"application restart restores the exact persisted project and conversation owner");
            Check(window.BoundQueue.IsPaused&&window.BoundQueue.Items.Single().Text=="first queued","application restart preserves the original encrypted work queue without sending it");
            window.Close();restarted.Pet.Close();restarted.Dispose();Complete(restarted.ImageServicesShutdown);
        }
        return checks;
    }
    [DataContract]private sealed class QueueFixture{[DataMember]public int Version;[DataMember]public string Payload="";}
    private sealed class BindingRuntime(string directory,ISecretProtector protector):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(protector);
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();
        public int RecorderCreations {get;private set;}public TaskCompletionSource<bool>? PendingPermission {get;set;}
        public override IAudioRecorder CreateRecorder(){RecorderCreations++;return new TestRecorder();}
        public override Task<bool> RequestMicrophoneAccessAsync()=>PendingPermission?.Task??Task.FromResult(false);
        public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("No fixture audio devices");
        public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("No fixture audio devices");
    }
}
