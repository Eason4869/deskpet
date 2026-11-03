using Dafeiyu;
using Dafeiyu.Maintenance;
using DeskPet.Desktop;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using SkiaSharp;

internal static class ProactiveConversationChecks
{
    internal static int RunHost()
    {
        SecretProtection.Configure(new TestProtector());
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
        host.ObserveInputIdle(TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(100));stamp=host.Epoch;
        host.ObserveInputIdle(TimeSpan.FromSeconds(31),TimeSpan.FromSeconds(101));Check(host.IsCurrent(stamp),"elapsed idle alone does not invalidate");
        host.ObserveInputIdle(TimeSpan.Zero,TimeSpan.FromSeconds(102));Check(!host.IsCurrent(stamp),"same-window aggregate input resets invalidate immediately");
        var manualNow=DateTime.UtcNow;Check(host.TryBeginManualTest(manualNow)&&!host.TryBeginManualTest(manualNow.AddSeconds(9))&&host.TryBeginManualTest(manualNow.AddSeconds(10)),"manual upload debounce belongs to app lifetime");
        var guarded=new Capture{Status=DesktopCaptureStatus.Ready};var api=new Api{Pending=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)};var maintenance=new DataMaintenanceGate();
        using var live=new DesktopObservationHost(maintenance,guarded,()=>api);
        var ready=new PetSettings{DesktopObservationEnabled=true,GreetingEnabled=true,AiGreetingEnabled=true};ready.Text.BaseUrl="https://example.com/v1";ready.Text.Model="fixture";ready.Text.ApiKey="fixture";ready.Text.ImageSupport=CapabilitySupport.Supported;
        using(var denied=live.GenerateAsync(ready,"pet","Pet",null,null,DateTime.Now,true,true).GetAwaiter().GetResult())Check(denied.Reason==ObservationReason.ConsentRequired&&guarded.Calls==0,"unconfirmed destination never captures");
        ready.Text.ImageSupport=CapabilitySupport.Unknown;
        using(var denied=live.GenerateAsync(ready,"pet","Pet",null,null,DateTime.Now,true,true).GetAwaiter().GetResult())Check(denied.Reason==ObservationReason.NoRoute&&guarded.Calls==0,"unknown image capability never captures");
        ready.Text.ImageSupport=CapabilitySupport.Supported;ready.DesktopObservationConsentVersion=DesktopObservationConsent.Version;ready.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(ready);
        using(var preview=live.PreviewAsync(ready,live.Epoch,CancellationToken.None).GetAwaiter().GetResult())Check(preview!=null&&api.Calls==0,"local preview captures only in memory without any API request");
        var running=live.GenerateAsync(ready,"pet","Pet",null,null,DateTime.Now,true,true);Until(()=>api.Calls==1);live.Invalidate();Wait(running);
        using(var stale=running.Result)Check(stale.Kind==ProactiveDecisionKind.Skip&&guarded.Last!.IsDisposed,"late invalidated model work has no utterance and cleared frame");
        var draining=live.DrainAsync();Check(!draining.IsCompleted&&!live.Service.WaitForIdleAsync().IsCompleted,"drain retains actual model ownership after caller cancellation");
        bool maintenanceRefused=false;try{maintenance.BeginAsync().GetAwaiter().GetResult();}catch(InvalidOperationException){maintenanceRefused=true;}
        Check(maintenanceRefused,"maintenance admission waits for real owned work");api.Pending.SetResult("{\"speak\":true,\"message\":\"late\"}");Wait(draining);
        using(var lease=maintenance.BeginAsync().GetAwaiter().GetResult())Check(maintenance.IsMaintenanceActive,"completed host drain releases its real maintenance activities");
        return checks;
    }
    internal static int RunAdmission(string directory)
    {
        SecretProtection.Configure(new TestProtector());
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        var api=new Api();var settings=new PetSettings{AgentEnabled=false,ReadReplies=false,ShowPredictedMessages=false};settings.Text.BaseUrl="https://example.com/v1";settings.Text.Model="fixture";settings.Text.ApiKey="fixture";
        using var session=new ConversationSession(api,new TestSpeech(),()=>new TestRecorder(),new HistoryStore(Path.Combine(directory,"history")));
        session.SelectPersona("pet");session.AttachProactive(new ProactiveEvent("initial","pet","Original immutable",null,DateTime.UtcNow,"",""));
        int admissions=0;session.BeforeUserTurnAcceptedAsync=()=>{admissions++;return Task.CompletedTask;};
        using(var image=new SKBitmap(8,8))
        {
            image.Erase(SKColors.Blue);using var png=image.Encode(SKEncodedImageFormat.Png,100);bool imageRefused=false;
            try{session.SendTextAsync("unavailable image",settings,"Pet",attachments:new[]{ChatAttachment.CreateImage("manual.png",png.ToArray())}).GetAwaiter().GetResult();}catch(ApiException){imageRefused=true;}
            Check(imageRefused&&admissions==0&&api.Calls==0&&session.History.Messages.Count==1,"failed image preprocessing never reaches durable user admission");
        }
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
        internal TaskCompletionSource<string>? Pending;
        internal readonly List<IList<ChatMessage>> Requests=new();
        internal readonly List<ConnectionSettings> Connections=new();
        public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Connections.Add(settings.Copy());Requests.Add(messages.Select(m=>m.Copy()).ToArray());Interlocked.Increment(ref Calls);return Pending?.Task??Task.FromResult("reply");}
        public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wav,CancellationToken cancel)=>throw new NotSupportedException();
        public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel)=>throw new NotSupportedException();
        public void Dispose(){}
    }
    internal static int RunReply(string directory)
    {
        Console.WriteLine("ProactiveReply starting owner");
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        var protector=new TestProtector();using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new Runtime(directory,protector));
        Console.WriteLine("ProactiveReply owner ready");
        var old=new ChatWindow(owner,true);old.Show();var draft=old.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="ChatDraft");draft.Text="old draft";old.AddAttachments(new[]{ChatAttachment.FromText("old.txt","old attachment")});
        owner.Settings.Text.BaseUrl=owner.Settings.Transcription.BaseUrl="https://example.com/v1";owner.Settings.Text.Model="fixture";owner.Settings.Transcription.Model="fixture-asr";owner.Settings.Text.ApiKey=owner.Settings.Transcription.ApiKey="fixture";
        owner.Session.StartRecording(owner.Settings);
        owner.Settings.AgentEnabled=true;
        owner.Settings.ImageDescriptionModel=new ModelReference{ProviderId="shown-provider",ModelId="shown-model"};
        owner.RegisterShownProactive(new ProactiveEvent("reply-id",owner.Skin.Id,"Original exact sentence",null,DateTime.UtcNow,"",""),owner.Settings.Copy());owner.Settings.ImageDescriptionModel=null;
        var first=owner.ReplyToProactive("reply-id");var repeated=owner.ReplyToProactive("reply-id");
        Console.WriteLine("ProactiveReply views ready");
        Check(first!=null&&ReferenceEquals(first,repeated),"double reply reuses the exact view");
        Check(first!.BoundSession!=owner.Session&&ReferenceEquals(first.BoundSession.ProactiveEvents,owner.Observation.Events),"dedicated session shares the app event store");
        Check(!first.EffectiveSettings().AgentEnabled&&owner.Settings.AgentEnabled,"ordinary reply never changes global Agent preference");
        Check(owner.Session.IsBusy&&owner.Session.Stage==ConversationStage.Recording,"opening a dedicated reply preserves the old running session");Wait(owner.Session.StopAsync());
        Check(first.BoundSession.History.Messages.Single().Content=="Original exact sentence","reply contains exact original assistant without fake user");
        Check(first.ProactiveBinding!.Fallback?.Model?.ProviderId=="shown-provider","reply inherits fallback reference at shown time");
        var menuButton=first.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="ImageDescriptionMenu");menuButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var menu=menuButton.ContextMenu!;((MenuItem)menu.Items[1]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));menu.Close();
        Check(first.ProactiveBinding.Fallback?.Mode=="disabled"&&owner.WorkBindings.RootSettings(owner.Session.PersonaId).ImageDescriptionOverride==null&&!File.Exists(Path.Combine(directory,"projects.json")),"pre-admission menu changes only event owner in memory");
        var full=owner.ExpandProactive(first.ProactiveBinding!);Check(ReferenceEquals(full.BoundSession,first.BoundSession)&&ReferenceEquals(full.BoundQueue,first.BoundQueue),"expansion retains same session and queue");
        var firstDraft=first.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="ChatDraft");var fullDraft=full.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="ChatDraft");fullDraft.Text="shared event draft";Check(firstDraft.Text=="shared event draft","expanded views preserve one event-owned draft");
        Check(draft.Text=="old draft"&&old.GetLogicalDescendants().OfType<Border>().Any(b=>b.Name=="AttachmentCard"),"opening reply preserves old draft and attachments");
        first.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="ChatDraft").Text="unanswered draft";
        first.Close();full.Close();Dispatcher.UIThread.RunJobs();
        Console.WriteLine("ProactiveReply unanswered views closed");
        Check(!File.Exists(Path.Combine(directory,"projects.json"))&&!Directory.Exists(Path.Combine(directory,"conversations"))&&!Directory.Exists(Path.Combine(directory,"composer-drafts")),"unanswered close leaves no catalog, history, queue or composer draft storage");
        Check(owner.Observation.Events.TryGet("reply-id")!=null,"closing a reply never disposes the shared store");
        var acceptedEvent=new ProactiveEvent("accepted-id",owner.Skin.Id,"Accepted original",null,DateTime.UtcNow,"","");owner.RegisterShownProactive(acceptedEvent,owner.Settings.Copy());var api=new Api();
        owner.Settings.Text.BaseUrl="https://example.com/v1";owner.Settings.Text.Model="fixture";owner.Settings.Text.ApiKey="fixture";owner.Settings.ReadReplies=false;owner.Settings.ShowPredictedMessages=false;
        string acceptedId;
        using(var admitted=new ProactiveConversationBinding(owner,new Runtime(directory),acceptedEvent,new ImageDescriptionOverride{Mode="disabled"},api,new TestSpeech()))
        {
            acceptedId=admitted.Id;
            admitted.Draft="Real accepted user";bool failedCallback=false;bool saveRefused=false;
            using(var blocked=new FileStream(Path.Combine(directory,"projects.json.write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                try{Wait(admitted.SendAsync(admitted.Draft,Array.Empty<ChatAttachment>(),()=>failedCallback=true));}catch(IOException){saveRefused=true;}
            }
            Check(saveRefused&&!failedCallback&&!admitted.Accepted&&api.Calls==0&&admitted.Draft=="Real accepted user"&&admitted.Session.History.Messages.Count==1,"catalog save failure retains original and reply draft without model admission");
            bool callback=false;Wait(admitted.SendAsync("Real accepted user",Array.Empty<ChatAttachment>(),()=>callback=true));Wait(admitted.Session.FlushHistoryAsync());
            Check(callback&&admitted.Accepted&&api.Calls==1,"first real user admission promotes exactly one ordinary conversation");
            var catalog=new ProjectCatalogStore(Path.Combine(directory,"projects.json")).Load();Check(catalog.UnassignedConversations.Single(c=>c.Id==admitted.Id).Mode=="chat"&&admitted.Session.History.Messages.Count==3,"durable acceptance preserves original assistant without fake user");
            Check(api.Requests.Single().Any(m=>m.Role=="assistant"&&m.Content=="Accepted original"),"first actual model request receives exact original sentence");
            admitted.SetFallback(null);Check(new ProjectCatalogStore(Path.Combine(directory,"projects.json")).Load().UnassignedConversations.Single(c=>c.Id==admitted.Id).ImageDescriptionOverride==null,"accepted picker persists only ordinary conversation owner");
        }
        owner.Save();old.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);
        using(var restarted=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new Runtime(directory,protector)))
        {
            bool globalMode=restarted.Settings.AgentEnabled;
            var historyEntry=restarted.Pet.ContextMenu!.Items.OfType<MenuItem>().Single(item=>item.Header?.ToString()==TextResources.Get("desktopObservation.history"));historyEntry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            var historyWindow=(Window)typeof(DesktopController).GetField("proactiveHistory",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(restarted)!;Dispatcher.UIThread.RunJobs();
            var historyRow=historyWindow.GetLogicalDescendants().OfType<Button>().Single(button=>(button.Content as TextBlock)?.Text=="Real accepted user");historyRow.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(!historyWindow.IsVisible,"existing pet menu opens the scoped history list and restores its selected row");
            var restored=restarted.ReopenProactiveConversation(acceptedId);var again=restarted.ReopenProactiveConversation(acceptedId);
            Check(restored!=null&&ReferenceEquals(restored,again)&&restored.ProactiveBinding!.Id==acceptedId,"restart reopens the same durable ordinary owner idempotently");
            Check(restored!.BoundSession.History.Messages.Any(m=>m.Role=="assistant"&&m.Content=="Accepted original")&&restored.BoundSession.History.Messages.Any(m=>m.Role=="user"&&m.Content=="Real accepted user"),"restart restores original assistant and accepted user history");
            Check(!restored.EffectiveSettings().AgentEnabled&&restarted.Settings.AgentEnabled==globalMode,"restoration remains ordinary without changing global Agent preference");
            var wrongPet=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="Other pet",Mode="chat",Origin="proactive",PetId="other",SectionId=ProjectCatalogStore.DefaultChatSectionId};restarted.WorkBindings.AdmitOrdinary(wrongPet);
            Check(restarted.ReopenProactiveConversation(wrongPet.Id)==null&&restarted.WorkBindings.ProactiveConversations(restarted.Skin.Id).All(row=>row.PetId==restarted.Skin.Id),"history entry excludes another companion");
            var malformed=new ConversationDefinition{Id="../outside",Name="Malformed",Mode="chat",Origin="proactive",PetId=restarted.Skin.Id,SectionId=ProjectCatalogStore.DefaultChatSectionId};restarted.WorkBindings.AdmitOrdinary(malformed);
            Check(restarted.ReopenProactiveConversation(malformed.Id)==null,"malformed local catalog identity cannot escape conversation storage");
            restored.Close();restarted.Pet.Close();restarted.Dispose();Wait(restarted.ImageServicesShutdown);
        }
        checks+=RunBoundCompanion(Path.Combine(directory,"bound-companion"));
        checks+=RunPendingComposer(Path.Combine(directory,"pending-composer"));
        return checks;
    }
    private static int RunPendingComposer(string directory)
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        var protector=new TestProtector();SecretProtection.Configure(protector);Directory.CreateDirectory(directory);
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{OnboardingState="completed",AutoUpdate=false,FollowCursor=false,Wander=false,GreetingEnabled=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new Runtime(directory,protector));
        owner.Settings.Text.BaseUrl="https://example.com/v1";owner.Settings.Text.Model="fixture";owner.Settings.Text.ApiKey="fixture";owner.Settings.ReadReplies=false;owner.Settings.ShowPredictedMessages=false;
        var catalog=(AgentWorkCatalog)typeof(DesktopWorkspaces).GetProperty("Catalog",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner.WorkBindings)!;
        var store=(ProjectCatalogStore)typeof(AgentWorkCatalog).GetField("store",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(catalog)!;
        try
        {
            foreach(bool edit in new[]{false,true})
            {
                var item=new ProactiveEvent(Guid.NewGuid().ToString("N"),owner.Skin.Id,"Original pending admission",null,DateTime.UtcNow,"","");owner.RegisterShownProactive(item,owner.Settings.Copy());var api=new Api();
                using var binding=new ProactiveConversationBinding(owner,new Runtime(directory,protector),item,new ImageDescriptionOverride{Mode="disabled"},api,new TestSpeech());
                var quick=new ChatWindow(owner,true,binding);quick.Show();var peer=owner.ExpandProactive(binding);
                var input=quick.GetLogicalDescendants().OfType<TextBox>().Single(box=>box.Name=="ChatDraft");input.Text="Unchanged submitted text";quick.AddAttachments(new[]{ChatAttachment.FromText("submitted.txt","submitted content")});
                using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var prior=store.BeforeSave;
                store.BeforeSave=next=>{prior?.Invoke(next);entered.Set();if(!release.Wait(10000))throw new TimeoutException("pending catalog admission release");};
                try
                {
                    typeof(ChatWindow).GetMethod("Send",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(quick,null);Until(()=>entered.IsSet);
                    if(edit)peer.AddAttachments(new[]{ChatAttachment.FromText("late-unsent.txt","late user content")});
                    release.Set();Until(()=>api.Calls==1&&!binding.Session.IsBusy);Dispatcher.UIThread.RunJobs();
                    var sent=api.Requests.Single().SelectMany(message=>message.Attachments??new List<ChatAttachment>()).Select(file=>file.Name).ToArray();
                    Check(sent.Contains("submitted.txt")&&!sent.Contains("late-unsent.txt"),"request snapshot excludes attachment added after submission");
                    if(edit)Check(binding.Attachments.Any(file=>file.Name=="late-unsent.txt")&&input.Text=="Unchanged submitted text"&&peer.GetLogicalDescendants().OfType<TextBlock>().Any(text=>text.Text=="late-unsent.txt"),"peer attachment added during admission remains in the shared composer");
                    else Check(binding.Attachments.Count==0&&input.Text==""&&peer.GetLogicalDescendants().OfType<TextBox>().Single(box=>box.Name=="ChatDraft").Text=="","unchanged submitted composer clears across both views after admission");
                }
                finally{release.Set();store.BeforeSave=prior;quick.Close();peer.Close();Wait(binding.DrainAsync());}
            }
        }
        finally{owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}
        return checks;
    }
    private static int RunBoundCompanion(string directory)
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        var protector=new TestProtector();SecretProtection.Configure(protector);Directory.CreateDirectory(directory);
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{OnboardingState="completed",AutoUpdate=false,FollowCursor=false,Wander=false,GreetingEnabled=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new Runtime(directory,protector));
        var skinA=owner.Skin;
        owner.Settings.Text.BaseUrl=owner.Settings.Speech.BaseUrl="https://example.com/v1";owner.Settings.Text.Model="fixture";owner.Settings.Speech.Model="fixture-speech";owner.Settings.Text.ApiKey=owner.Settings.Speech.ApiKey="fixture";
        owner.Settings.ReadReplies=true;owner.Settings.Volume=50;owner.Settings.ShowPredictedMessages=false;
        PersonaProfiles.Save(owner.Settings,skinA.Id,skinA.Prompt,"Only companion A custom prompt {pet_name}",skinA.Voices,skinA.OfficialCharacterId??skinA.BuiltinCharacterId);
        owner.Settings.VoiceName="voice-A";owner.Settings.LiveVoice="live-A";PersonaProfiles.SaveVoices(owner.Settings);
        var skinB=new PetSkin{Id="fixture-companion-b",PickerKey="custom-pack:fixture-companion-b",Name="Companion B",Prompt="Only companion B default prompt",Atlas=skinA.Atlas,Gaze=skinA.Gaze};
        foreach(var clip in skinA.Clips)skinB.Clips[clip.Key]=clip.Value;
        foreach(var clip in skinA.Alternates)skinB.Alternates[clip.Key]=clip.Value;
        owner.Skins.Add(skinB);
        var bProfile=owner.Settings.Copy();PersonaProfiles.Save(bProfile,skinB.Id,skinB.Prompt,"Only companion B custom prompt");bProfile.VoiceName="voice-B";bProfile.LiveVoice="live-B";PersonaProfiles.SaveVoices(bProfile);
        owner.Settings.PersonaOverrides[skinB.Id]=bProfile.PersonaOverrides[skinB.Id];owner.Settings.VoiceProfiles[skinB.Id]=bProfile.VoiceProfiles[skinB.Id].Copy();
        var item=new ProactiveEvent("companion-identity",skinA.Id,"A initiated this exact sentence",null,DateTime.UtcNow,"","");owner.RegisterShownProactive(item,owner.Settings.Copy());
        var api=new Api();var speech=new InspectSpeech();using var binding=new ProactiveConversationBinding(owner,new Runtime(directory,protector),item,new ImageDescriptionOverride{Mode="disabled"},api,speech);
        var quick=new ChatWindow(owner,true,binding);quick.Show();var full=owner.ExpandProactive(binding);
        string relationshipA=binding.Session.CaptureRelationshipCompanion();
        try
        {
            Wait(owner.SelectSkinAsync(skinB));owner.Settings.TextProviders.Single(provider=>provider.Id==owner.Settings.ActiveTextProviderId).Models.Add("latest-service-model");owner.Settings.SelectTextModel(owner.Settings.ActiveTextProviderId,"latest-service-model");
            string globalB=SettingsStore.SnapshotIdentity(owner.Settings);
            quick.GetLogicalDescendants().OfType<TextBox>().Single(box=>box.Name=="ChatDraft").Text="Real reply to A";
            typeof(ChatWindow).GetMethod("Send",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(quick,null);Until(()=>api.Calls==1&&!binding.Session.IsBusy);Wait(binding.Session.FlushHistoryAsync());
            Check(api.Requests.Single()[0].Content.Contains("Only companion A custom prompt")&&!api.Requests.Single()[0].Content.Contains("Only companion B"),"retained A reply uses A custom prompt after global companion switches to B");
            var effective=binding.EffectiveSettings();Check(effective.SkinId==skinA.Id&&effective.VoiceName=="voice-A"&&effective.LiveVoice=="live-A"&&!effective.AgentEnabled,"bound identity and voice profile remain A in ordinary effective settings");
            Check(speech.Requests.Single().SkinId==skinA.Id&&speech.Requests.Single().VoiceName=="voice-A","actual reply speech receives A identity and voice");
            Check(api.Connections.Single().Model=="latest-service-model"&&SettingsStore.SnapshotIdentity(owner.Settings)==globalB&&owner.Skin==skinB&&owner.Settings.VoiceName=="voice-B","bound persona keeps current model preferences and leaves global B settings unchanged");
            Check(quick.BoundSession.PersonaId==skinA.Id&&ReferenceEquals(quick.BoundSession,full.BoundSession)&&binding.Session.History.Messages.Last().RelationshipCompanionId==relationshipA&&relationshipA!=owner.RelationshipCompanionId,"retained quick/full history and relationship owner remain A");
        }
        finally{quick.Close();full.Close();Wait(binding.DrainAsync());owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}
        return checks;
    }
    private sealed class InspectSpeech:IReplySpeech
    {
        internal readonly List<PetSettings> Requests=new();
        public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){Requests.Add(settings.Copy());return Task.CompletedTask;}
        public void Stop(){}public void SetVolume(int volume){}public void Dispose(){}
    }
    internal static void Wait(Task task){var deadline=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(deadline.ElapsedMilliseconds>15000)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
    private static void Until(Func<bool> ready){var deadline=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(deadline.ElapsedMilliseconds>10000)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}}
    internal static int RunSettings(string directory)
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new Runtime(directory));
        owner.Settings.Text.BaseUrl="https://example.com/a/very/long/service/address/v1";owner.Settings.Text.Model=new string('M',130);owner.Settings.Text.ImageSupport=CapabilitySupport.Supported;
        var window=new SettingsWindow(owner);window.Show();window.SelectSection("text");Dispatcher.UIThread.RunJobs();
        var providerDraft=window.GetLogicalDescendants().OfType<TextBox>().First();providerDraft.Text="unsaved provider draft";
        window.SelectSection("behavior");Dispatcher.UIThread.RunJobs();var panel=window.GetLogicalDescendants().OfType<DesktopObservationSettingsPanel>().Single();
        Check(!panel.GetLogicalDescendants().OfType<TextBlock>().Any(text=>(text.Text??"").Contains("private")),"service labels never expose URL credentials");
        Check(DesktopObservationUiText.SafeAddress("https://name:private@example.com/v1?key=private#private")=="https://example.com/v1","safe destination formatting removes credentials");
        var toggle=panel.GetLogicalDescendants().OfType<CheckBox>().Single(box=>box.Name=="DesktopObservationEnabled");
        Check(!panel.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="ObservationPause").IsEnabled&&!panel.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="ObservationUploadTest").IsEnabled,"off/dependency state disables pause and upload actions");
        toggle.IsChecked=true;Dispatcher.UIThread.RunJobs();
        var consent=window.OwnedWindows.Single(dialog=>dialog.Title==TextResources.Get("desktopObservation.consentTitle"));
        Check(!consent.GetLogicalDescendants().OfType<TextBlock>().Any(text=>(text.Text??"").Contains("{0}")),"actual consent dialog contains no unformatted recipient placeholder");
        Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Directory.CreateDirectory("work/task7b-renders");using(var image=consent.CaptureRenderedFrame()){Check(image!=null,"formatted consent dialog actually renders");image!.Save(Path.GetFullPath("work/task7b-renders/consent-fix1-light-500.png"),new PngBitmapEncoderOptions());}
        Check(consent.GetLogicalDescendants().OfType<TextBlock>().Any(text=>(text.Text??"").Contains("https://example.com")),"explicit consent shows its actual receiving service");
        var cancel=consent.GetLogicalDescendants().OfType<Button>().Single(button=>(button.Content as TextBlock)?.Text==TextResources.Get("desktopObservation.consentCancel"));cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
        Check(toggle.IsChecked==false&&!owner.Settings.DesktopObservationEnabled,"cancelling consent leaves observation visibly disabled");
        TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));DesktopTheme.Apply("dark");
        Check(ReferenceEquals(toggle,panel.GetLogicalDescendants().OfType<CheckBox>().Single()),"language/theme retains the controls and selection");
        window.SelectSection("text");Check(providerDraft.Text=="unsaved provider draft","language/theme switch preserves provider draft");window.SelectSection("behavior");
        string output=Path.GetFullPath("work/task7b-renders");Directory.CreateDirectory(output);
        foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{570,790})
        {
            DesktopTheme.Apply(theme);window.Width=width;window.Height=980;panel.BringIntoView();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame=window.CaptureRenderedFrame();Check(frame!=null,"settings actually render "+theme+width);frame!.Save(Path.Combine(output,$"observation-{theme}-{width}.png"),new PngBitmapEncoderOptions());
        }
        foreach(string theme in new[]{"light","dark"})
        {
            DesktopTheme.Apply(theme);var closeup=new Window{Width=410,Height=720,Background=DesktopTheme.Surface};using var group=new DesktopObservationSettingsPanel(owner,closeup);closeup.Content=new Border{Padding=new Thickness(16),Child=group};closeup.Show();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame=closeup.CaptureRenderedFrame();Check(frame!=null,"complete narrow observation group renders "+theme);frame!.Save(Path.Combine(output,$"observation-group-{theme}-410.png"),new PngBitmapEncoderOptions());closeup.Close();
        }
        var store=(SettingsStore)typeof(DesktopController).GetField("store",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
        store.BeforeExactSave=()=>throw new IOException("fixture disk failure");var failed=owner.SaveObservationSettingsAsync(s=>s.DesktopObservationEnabled=true);Wait(failed);
        Check(!failed.Result&&!owner.Settings.DesktopObservationEnabled,"failed settings transaction does not publish enablement");store.BeforeExactSave=null;
        owner.Settings.DesktopObservationEnabled=true;owner.RefreshObservationStatus();store.BeforeExactSave=()=>throw new IOException("fixture disable save failed");toggle.IsChecked=false;Wait(owner.ObservationSettingsIdle);
        Check(toggle.IsChecked==false&&owner.Settings.DesktopObservationEnabled&&owner.ObservationPaused,"failed disable retains off draft and runtime pause despite durable enabled setting");store.BeforeExactSave=null;
        using var started=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int writes=0;
        store.BeforeExactSave=()=>{if(Interlocked.Increment(ref writes)==1){started.Set();if(!release.Wait(10000))throw new TimeoutException("settings fixture release");}};
        var concurrent=owner.SaveObservationSettingsAsync(s=>s.DesktopObservationExcludedApps.Add("com.example.fixture"));Until(()=>started.IsSet);owner.Settings.Text.Model="newer-model";owner.Save();release.Set();Wait(concurrent);store.BeforeExactSave=null;
        Check(concurrent.Result&&new SettingsStore(Path.Combine(directory,"settings.json")).Load().Text.Model=="newer-model"&&owner.Settings.DesktopObservationExcludedApps.Contains("com.example.fixture"),"explicit observation save replays over concurrent provider updates");
        var confirmation=panel.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="ObservationConfirm");confirmation.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Wait(owner.ObservationSettingsIdle);
        Check(!owner.Settings.DesktopObservationEnabled,"off save retry persists the pending privacy choice");owner.Settings.DesktopObservationEnabled=true;owner.RefreshObservationStatus();
        confirmation.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
        var testConsent=window.OwnedWindows.Single(dialog=>dialog.Title==TextResources.Get("desktopObservation.consentTitle"));
        var accept=testConsent.GetLogicalDescendants().OfType<Button>().Single(button=>(button.Content as TextBlock)?.Text==TextResources.Get("desktopObservation.consentAccept"));accept.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Until(()=>DesktopObservationConsent.IsConfirmed(owner.GlobalObservationSettings()));
        Check(owner.ObservationPaused&&DesktopObservationConsent.IsConfirmed(new SettingsStore(Path.Combine(directory,"settings.json")).Load()),"explicit test confirms the current destination durably while retaining privacy pause");
        // Exercise the actual explicit model-refresh UI entry point against a local HTTP catalog.
        var portProbe=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);portProbe.Start();int port=((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
        string endpoint="http://127.0.0.1:"+port+"/";
        using(var catalog=new System.Net.HttpListener())
        {
            catalog.Prefixes.Add(endpoint);catalog.Start();
            var reply=Task.Run(async()=>{var response=await catalog.GetContextAsync();byte[] body=System.Text.Encoding.UTF8.GetBytes("{\"data\":[{\"id\":\"fixture\"}]}");response.Response.ContentType="application/json";response.Response.ContentLength64=body.Length;await response.Response.OutputStream.WriteAsync(body);response.Response.Close();});
            var connection=new ConnectionSettings{BaseUrl=endpoint+"v1",Model="fixture",ImageSupport=CapabilitySupport.Supported};
            var provider=new TextProviderSettings{Id="refresh",Name="Refresh",Connection=connection.Copy(),Models=new List<string>{"fixture"}};provider.ModelCapabilities["fixture"]=new ModelCapabilitySetting{Images=CapabilitySupport.Supported};
            owner.Settings.ApplyProviders("text",new List<TextProviderSettings>{provider},provider.Id,connection.Copy());owner.Settings.DesktopObservationEnabled=owner.Settings.GreetingEnabled=owner.Settings.AiGreetingEnabled=true;
            owner.Settings.DesktopObservationConsentVersion=DesktopObservationConsent.Version;owner.Settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(owner.Settings);
            var rejection=new Api{Pending=new TaskCompletionSource<string>()};rejection.Pending.SetException(new ApiException("private rejected body","image-unsupported"));
            var service=new DesktopObservationService(owner.Observation.Policy,new Capture{Status=DesktopCaptureStatus.Ready},()=>rejection);
            var context=new DesktopObservationContext(owner.GlobalObservationSettings(),owner.Skin.Id,owner.Skin.Name,null,null,DateTime.Now,true,true,()=>true);
            using(var result=service.GenerateAsync(context,CancellationToken.None).GetAwaiter().GetResult())Check(result.Reason==ObservationReason.NoRoute,"Avalonia receiver rejection is latched");
            owner.Settings.Topmost=!owner.Settings.Topmost;owner.Save();
            Check(owner.Observation.Policy.TryBegin(context,DateTime.UtcNow.AddHours(2)).Reason==ObservationReason.NoRoute,"unrelated Avalonia settings save preserves rejected receiver");
            using var editor=new ProviderEditor(owner,"text");owner.ObservationReason=ObservationReason.NoRoute;long epoch=owner.Observation.Epoch;
            var refresh=(Task)typeof(ProviderEditor).GetMethod("FetchAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(editor,null)!;Wait(refresh);Wait(reply);
            Check(owner.Observation.Policy.TryBegin(context,DateTime.UtcNow.AddHours(2)).Allowed && owner.Observation.Epoch>epoch && owner.ObservationReason==ObservationReason.Allowed,"actual explicit Avalonia HTTP model refresh clears matching latch and invalidates old attempts");
        }
        window.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);DesktopTheme.Apply("light");return checks;
    }
    private sealed class Runtime(string directory,ISecretProtector? protector=null):DesktopRuntime
    {
        private readonly ISecretProtector shared=protector??new TestProtector();
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(shared);
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();
        public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override IPcmMicrophone CreateMicrophone()=>throw new NotSupportedException();public override ILiveAudioOutput CreateLiveOutput()=>throw new NotSupportedException();
    }
    private sealed class Capture:IDesktopObservationCapture
    {
        internal int Calls;
        internal DesktopCaptureStatus Status=DesktopCaptureStatus.UnsupportedPlatform;
        internal DesktopObservationFrame? Last;
        public DesktopCaptureStatus GetStatus(DesktopCaptureOptions? options=null)=>Status;
        public bool IsCurrent(DesktopObservationFrame frame)=>true;
        public Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel)
        {
            Calls++;using var bitmap=new SKBitmap(8,8);bitmap.Erase(SKColors.Blue);using var encoded=bitmap.Encode(SKEncodedImageFormat.Png,100);
            Last=new DesktopObservationFrame(encoded.ToArray(),DateTimeOffset.UtcNow,new DesktopObservationIdentity(1,2,3,4,"example.app","private title","",0,0,8,8));return Task.FromResult(Last);
        }
    }
}
