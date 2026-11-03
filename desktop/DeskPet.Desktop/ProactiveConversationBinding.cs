using Dafeiyu;

namespace DeskPet.Desktop;

// Event-owned ordinary conversation. Construction and unanswered drafts are memory-only.
internal sealed class ProactiveConversationBinding : IDisposable
{
    private readonly DesktopController owner;
    private readonly ConversationDefinition definition;
    private readonly string companionName;
    private readonly object gate=new();
    private int sending;
    private Task? promotion;
    private bool accepted,disposed;
    private string? modelProvider,modelId;
    internal string Id=>definition.Id;
    internal string EventId {get;}
    private string draft="";
    private long composerRevision;
    private readonly List<ChatAttachment> attachments=new();
    internal sealed record ComposerSnapshot(long Revision,string Text,IList<ChatAttachment> Attachments);
    internal event Action? ComposerChanged;
    internal string Draft {get{lock(gate)return draft;}set{lock(gate){if(disposed||draft==value)return;draft=value;composerRevision++;}ComposerChanged?.Invoke();}}
    internal IList<ChatAttachment> Attachments=>attachments.AsReadOnly();
    internal void AddAttachments(IEnumerable<ChatAttachment> added)
    {
        lock(gate){ObjectDisposedException.ThrowIf(disposed,this);var next=ChatAttachment.Snapshot(attachments.Concat(added));attachments.Clear();attachments.AddRange(next);composerRevision++;}
        ComposerChanged?.Invoke();
    }
    internal void RemoveAttachment(ChatAttachment value)
    {lock(gate){if(disposed||!attachments.Remove(value))return;composerRevision++;}ComposerChanged?.Invoke();}
    internal ComposerSnapshot CaptureComposer()
    {lock(gate)return new ComposerSnapshot(composerRevision,draft,ChatAttachment.Snapshot(attachments));}
    internal bool ClearSubmittedComposer(ComposerSnapshot submitted)
    {
        lock(gate){if(disposed||composerRevision!=submitted.Revision)return false;draft="";attachments.Clear();composerRevision++;}
        ComposerChanged?.Invoke();return true;
    }
    internal string CompanionId=>definition.PetId;
    internal PetSkin CompanionSkin {get;}
    internal ConversationSession Session {get;}
    internal ConversationQueue Queue {get;}
    internal bool Accepted {get{lock(gate)return accepted;}}
    internal ProactiveConversationBinding(DesktopController owner,DesktopRuntime runtime,ProactiveEvent item,ImageDescriptionOverride fallback,IConversationApi? transport=null,IReplySpeech? speech=null)
        :this(owner,runtime,new ConversationDefinition{Id=item.ConversationId??Guid.NewGuid().ToString("N"),Name=TextResources.Get("projectCatalog.createConversation.nameStringIsNullOrWhiteSpace"),Mode="chat",Origin="proactive",PetId=item.CompanionId,SectionId=ProjectCatalogStore.DefaultChatSectionId,ImageDescriptionOverride=fallback.Copy(),CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks},item,transport,speech,false){}
    internal ProactiveConversationBinding(DesktopController owner,DesktopRuntime runtime,ConversationDefinition saved)
        :this(owner,runtime,saved.Copy(),null,null,null,true){}
    private ProactiveConversationBinding(DesktopController owner,DesktopRuntime runtime,ConversationDefinition definition,ProactiveEvent? item,IConversationApi? transport,IReplySpeech? speech,bool accepted)
    {
        if(!Guid.TryParseExact(definition.Id,"N",out _))throw new ArgumentException(TextResources.Get("conversationWorkspace.resolveDataDirectory.text"));
        this.owner=owner;this.definition=definition;this.accepted=accepted;EventId=item?.Id??"";CompanionSkin=owner.Skin;companionName=owner.Skin.DisplayName;
        if(item!=null)owner.Observation.Events.BindConversation(item.Id,item.CompanionId,Id);
        var api=transport??new ApiClient();var root=Path.Combine(owner.DataDirectory,"conversations",Id);
        Session=new ConversationSession(api,speech??runtime.CreateSpeech((ApiClient)api),runtime.CreateRecorder,new HistoryStore(Path.Combine(root,"history")),()=>Accepted&&owner.Settings.SaveChatHistory,Id,owner.DataMaintenance);
        Session.ProactiveEvents=owner.Observation.Events;
        Session.ImageStore=owner.ImageStore;Session.ImageClient=owner.ImageClient;Session.ImageTasks=owner.ImageTasks;Session.ImageUsage=owner.ImageUsage;
        Session.SelectPersona(definition.PetId);if(item!=null)Session.AttachProactive(item);
        if(accepted&&owner.Settings.SaveChatHistory)Draft=owner.ComposerDrafts.Get("desktop:proactive:"+Id);
        string relationship=owner.RelationshipCompanionId;Session.ConfigureRelationships(owner.Relationships,()=>relationship);
        Session.BeforeUserTurnAcceptedAsync=PromoteAsync;
        Queue=new ConversationQueue(Session,EffectiveSettings,()=>companionName,Path.Combine(root,"queued-messages.json"),()=>Accepted);
        Session.MessageAdded+=Changed;Session.MessageUpdated+=Changed;Session.StateChanged+=owner.NotifyWorkChanged;Queue.Changed+=owner.NotifyWorkChanged;
    }
    private void Changed(ChatMessage item)=>owner.NotifyWorkChanged();
    private Task PromoteAsync()
    {
        lock(gate)
        {
            if(accepted)return Task.CompletedTask;
            ObjectDisposedException.ThrowIf(disposed,this);
            if(promotion!=null&&!promotion.IsFaulted&&!promotion.IsCanceled)return promotion;
            var submitted=definition.Copy();
            return promotion=Task.Run(()=>
            {
                if(!owner.WorkBindings.AdmitOrdinary(submitted))throw new IOException(TextResources.Get("desktopObservation.saveFailed"));
                lock(gate)accepted=true;
            });
        }
    }
    internal PetSettings EffectiveSettings()
    {
        var settings=owner.Settings.Copy();
        PersonaProfiles.Activate(settings,CompanionId,CompanionSkin.Prompt,CompanionSkin.Voices,CompanionSkin.OfficialCharacterId??CompanionSkin.BuiltinCharacterId);
        settings.AgentEnabled=false;
        lock(gate){settings.ImageDescriptionOverride=definition.ImageDescriptionOverride?.Copy();if(modelProvider!=null&&modelId!=null)settings.SelectTextModel(modelProvider,modelId);}
        return settings;
    }
    internal void SelectModel(string provider,string model){lock(gate){modelProvider=provider;modelId=model;}owner.NotifyWorkChanged();}
    internal ImageDescriptionOverride? Fallback {get{lock(gate)return definition.ImageDescriptionOverride?.Copy();}}
    internal void SetFallback(ImageDescriptionOverride? value)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(promotion!=null&&!promotion.IsCompleted)throw new InvalidOperationException(TextResources.Get("agent.jobs.ui.bindingBusy"));
            if(accepted&&!owner.WorkBindings.SaveProactiveImageDescription(Id,value))throw new InvalidOperationException(TextResources.Get("agent.jobs.ui.bindingBusy"));
            definition.ImageDescriptionOverride=value==null||value.Mode=="inherit"?null:value.Copy();
        }
        owner.NotifyWorkChanged();
    }
    internal void ClearFallbackForMaintenance(){lock(gate)definition.ImageDescriptionOverride=null;}
    internal async Task SendAsync(string text,IList<ChatAttachment> files,Action admitted)
    {
        if(Interlocked.CompareExchange(ref sending,1,0)!=0)return;
        try
        {
            lock(gate)if(!accepted&&!string.IsNullOrWhiteSpace(text)){var title=text.Trim().Replace('\r',' ').Replace('\n',' ');int length=Math.Min(60,title.Length);if(length>0&&char.IsHighSurrogate(title[length-1]))length--;definition.Name=title.Substring(0,length);}
            Session.BeforeUserTurnAcceptedAsync=async()=>{await PromoteAsync().ConfigureAwait(false);admitted();};
            await Session.SendTextAsync(text,EffectiveSettings(),companionName,attachments:files).ConfigureAwait(false);
        }
        finally{Session.BeforeUserTurnAcceptedAsync=PromoteAsync;Volatile.Write(ref sending,0);}
    }
    internal async Task DrainAsync(){await Queue.PauseAsync();await Session.StopAsync();await Session.WaitForIdleAsync();await Session.FlushHistoryAsync();}
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;draft="";attachments.Clear();}
        Session.MessageAdded-=Changed;Session.MessageUpdated-=Changed;Session.StateChanged-=owner.NotifyWorkChanged;Queue.Changed-=owner.NotifyWorkChanged;
        Queue.Dispose();Session.Dispose();
    }
}
