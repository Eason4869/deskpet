using Dafeiyu;
using Dafeiyu.Maintenance;
using DeskPet.Desktop;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;

internal static class ProactiveConversationChecks
{
    internal static int RunHost()
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        var capture=new Capture();
        using var host=new DesktopObservationHost(new DataMaintenanceGate(),capture);
        var settings=new PetSettings();
        using(var result=host.GenerateAsync(settings,"pet","Pet",null,null,DateTime.Now,true,true).GetAwaiter().GetResult())
            Check(result.Reason==ObservationReason.Disabled&&capture.Calls==0,"disabled host does no image work");
        long stamp=host.Epoch;host.Invalidate();Check(!host.IsCurrent(stamp),"invalidation immediately rejects stale work");
        var item=new ProactiveEvent("shown","pet","Original",new byte[]{137,80,78,71,13,10,26,10},DateTime.UtcNow,"target","model");
        host.Events.Register(item);host.Invalidate();Check(host.Events.TryGet(item.Id).Png==null,"privacy invalidation clears retained images");
        return checks;
    }
    internal static int RunAdmission(string directory)
    {
        SecretProtection.Configure(new TestProtector());
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        var api=new Api();var settings=new PetSettings{AgentEnabled=false,ReadReplies=false,ShowPredictedMessages=false};settings.Text.BaseUrl="https://example.com/v1";settings.Text.Model="fixture";settings.Text.ApiKey="fixture";
        using var session=new ConversationSession(api,new TestSpeech(),()=>new TestRecorder(),new HistoryStore(Path.Combine(directory,"history")));
        session.SelectPersona("pet");session.AttachProactive(new ProactiveEvent("initial","pet","Original immutable",null,DateTime.UtcNow,"",""));
        session.BeforeUserTurnAcceptedAsync=()=>throw new IOException("fixture admission failed");
        bool refused=false;try{session.SendTextAsync("retained reply",settings,"Pet").GetAwaiter().GetResult();}catch(IOException){refused=true;}
        Check(refused&&api.Calls==0&&session.History.Messages.Count==1,"failed admission performs no model call or user/history mutation");
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        session.BeforeUserTurnAcceptedAsync=async()=>{entered.SetResult();await release.Task.ConfigureAwait(false);};
        var pending=session.SendTextAsync("admitted reply",settings,"Pet");entered.Task.GetAwaiter().GetResult();
        var stopping=session.StopAsync();release.SetResult();try{pending.GetAwaiter().GetResult();}catch(OperationCanceledException){}
        Wait(stopping);
        Check(api.Calls==0&&session.History.Messages.Any(m=>m.Role=="user"&&m.Content=="admitted reply"),"cancellation during durable admission retains the accepted user row without a model call");
        Check(session.History.Messages.First().Content=="Original immutable","admission does not change original assistant");
        var catalogPath=Path.Combine(directory,"projects.json");var store=new ProjectCatalogStore(catalogPath);
        var initial=new ProjectCatalog();initial.UnassignedConversations.Add(new ConversationDefinition{Id="other",Mode="chat",PetId="other-pet",Name="Concurrent",SectionId=ProjectCatalogStore.DefaultChatSectionId});store.Save(initial);
        store.BeforeSave=next=>next.UnassignedConversations=new ProjectCatalogStore(catalogPath).Load().UnassignedConversations;
        var definition=new ConversationDefinition{Id="admitted",Name="New",Mode="chat",PetId="pet",SectionId=ProjectCatalogStore.DefaultChatSectionId};
        store.SaveOrdinaryConversation(definition);
        var changed=new ProjectCatalogStore(catalogPath).Load();changed.UnassignedConversations.Single(c=>c.Id=="admitted").Name="Renamed concurrently";new ProjectCatalogStore(catalogPath).Save(changed);
        var duplicate=store.SaveOrdinaryConversation(definition);
        Check(duplicate.UnassignedConversations.Count==2&&duplicate.UnassignedConversations.Single(c=>c.Id=="admitted").Name=="Renamed concurrently","idempotent admission preserves unrelated rows and concurrent rename");
        bool collision=false;try{store.SaveOrdinaryConversation(new ConversationDefinition{Id="other",Mode="chat",PetId="pet"});}catch(InvalidOperationException){collision=true;}
        Check(collision&&new ProjectCatalogStore(catalogPath).Load().UnassignedConversations.Count==2,"admission rejects another companion identity without rewriting rows");
        return checks;
    }
    private sealed class Api:IConversationApi
    {
        internal int Calls;
        public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Calls++;return Task.FromResult("reply");}
        public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wav,CancellationToken cancel)=>throw new NotSupportedException();
        public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel)=>throw new NotSupportedException();
        public void Dispose(){}
    }
    internal static int RunReply(string directory)
    {
        Console.WriteLine("ProactiveReply starting owner");
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new Runtime(directory));
        Console.WriteLine("ProactiveReply owner ready");
        var old=new ChatWindow(owner,true);old.Show();var draft=old.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="ChatDraft");draft.Text="old draft";old.AddAttachments(new[]{ChatAttachment.FromText("old.txt","old attachment")});
        owner.Settings.AgentEnabled=true;
        owner.RegisterShownProactive(new ProactiveEvent("reply-id",owner.Skin.Id,"Original exact sentence",null,DateTime.UtcNow,"",""),owner.Settings.Copy());
        var first=owner.ReplyToProactive("reply-id");var repeated=owner.ReplyToProactive("reply-id");
        Console.WriteLine("ProactiveReply views ready");
        Check(first!=null&&ReferenceEquals(first,repeated),"double reply reuses the exact view");
        Check(first!.BoundSession!=owner.Session&&ReferenceEquals(first.BoundSession.ProactiveEvents,owner.Observation.Events),"dedicated session shares the app event store");
        Check(!first.EffectiveSettings().AgentEnabled&&owner.Settings.AgentEnabled,"ordinary reply never changes global Agent preference");
        Check(first.BoundSession.History.Messages.Single().Content=="Original exact sentence","reply contains exact original assistant without fake user");
        var full=owner.ExpandProactive(first.ProactiveBinding!);Check(ReferenceEquals(full.BoundSession,first.BoundSession)&&ReferenceEquals(full.BoundQueue,first.BoundQueue),"expansion retains same session and queue");
        Check(draft.Text=="old draft"&&old.GetLogicalDescendants().OfType<Border>().Any(b=>b.Name=="AttachmentCard"),"opening reply preserves old draft and attachments");
        first.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="ChatDraft").Text="unanswered draft";
        first.Close();full.Close();Dispatcher.UIThread.RunJobs();
        Console.WriteLine("ProactiveReply unanswered views closed");
        Check(!File.Exists(Path.Combine(directory,"projects.json"))&&!Directory.Exists(Path.Combine(directory,"conversations")),"unanswered close leaves no catalog or conversation storage");
        Check(owner.Observation.Events.TryGet("reply-id")!=null,"closing a reply never disposes the shared store");
        old.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);return checks;
    }
    internal static void Wait(Task task){var deadline=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(deadline.ElapsedMilliseconds>15000)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
    private sealed class Runtime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();
        public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override IPcmMicrophone CreateMicrophone()=>throw new NotSupportedException();public override ILiveAudioOutput CreateLiveOutput()=>throw new NotSupportedException();
    }
    private sealed class Capture:IDesktopObservationCapture
    {
        internal int Calls;
        public DesktopCaptureStatus GetStatus()=>DesktopCaptureStatus.UnsupportedPlatform;
        public bool IsCurrent(DesktopObservationFrame frame)=>false;
        public Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel){Calls++;return Task.FromResult<DesktopObservationFrame>(null!);}
    }
}
