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
    private bool accepted,disposed;
    private string? modelProvider,modelId;
    internal string Id=>definition.Id;
    internal string EventId {get;}
    internal string Draft {get;set;}="";
    internal List<ChatAttachment> Attachments {get;}=new();
    internal string CompanionId=>definition.PetId;
    internal ConversationSession Session {get;}
    internal ConversationQueue Queue {get;}
    internal bool Accepted {get{lock(gate)return accepted;}}
    internal ProactiveConversationBinding(DesktopController owner,DesktopRuntime runtime,ProactiveEvent item,ImageDescriptionOverride fallback)
    {
        this.owner=owner;EventId=item.Id;companionName=owner.Skin.DisplayName;
        definition=new ConversationDefinition{Id=item.ConversationId??Guid.NewGuid().ToString("N"),Name=TextResources.Get("projectCatalog.createConversation.nameStringIsNullOrWhiteSpace"),Mode="chat",PetId=item.CompanionId,SectionId=ProjectCatalogStore.DefaultChatSectionId,ImageDescriptionOverride=fallback.Copy(),CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
        owner.Observation.Events.BindConversation(item.Id,item.CompanionId,Id);
        var api=new ApiClient();var root=Path.Combine(owner.DataDirectory,"conversations",Id);
        Session=new ConversationSession(api,runtime.CreateSpeech(api),runtime.CreateRecorder,new HistoryStore(Path.Combine(root,"history")),()=>Accepted&&owner.Settings.SaveChatHistory,Id,owner.DataMaintenance);
        Session.ProactiveEvents=owner.Observation.Events;
        Session.ImageStore=owner.ImageStore;Session.ImageClient=owner.ImageClient;Session.ImageTasks=owner.ImageTasks;Session.ImageUsage=owner.ImageUsage;
        Session.SelectPersona(item.CompanionId);Session.AttachProactive(item);
        string relationship=owner.RelationshipCompanionId;Session.ConfigureRelationships(owner.Relationships,()=>relationship);
        Session.BeforeUserTurnAcceptedAsync=PromoteAsync;
        Queue=new ConversationQueue(Session,EffectiveSettings,()=>companionName,Path.Combine(root,"queued-messages.json"));
        Session.MessageAdded+=Changed;Session.MessageUpdated+=Changed;Session.StateChanged+=owner.NotifyWorkChanged;Queue.Changed+=owner.NotifyWorkChanged;
    }
    private void Changed(ChatMessage item)=>owner.NotifyWorkChanged();
    private Task PromoteAsync()=>Task.Run(()=>
    {
        lock(gate)
        {
            if(accepted)return;
            ObjectDisposedException.ThrowIf(disposed,this);
            if(!owner.WorkBindings.AdmitOrdinary(definition))throw new IOException(TextResources.Get("desktopObservation.saveFailed"));
            accepted=true;
        }
    });
    internal PetSettings EffectiveSettings()
    {
        var settings=owner.Settings.Copy();settings.AgentEnabled=false;
        lock(gate){settings.ImageDescriptionOverride=definition.ImageDescriptionOverride?.Copy();if(modelProvider!=null&&modelId!=null)settings.SelectTextModel(modelProvider,modelId);}
        return settings;
    }
    internal void SelectModel(string provider,string model){lock(gate){modelProvider=provider;modelId=model;}owner.NotifyWorkChanged();}
    internal ImageDescriptionOverride Fallback {get{lock(gate)return definition.ImageDescriptionOverride.Copy();}}
    internal async Task SendAsync(string text,IList<ChatAttachment> files,Action admitted)
    {
        if(Interlocked.CompareExchange(ref sending,1,0)!=0)return;
        try
        {
            Session.BeforeUserTurnAcceptedAsync=async()=>{await PromoteAsync().ConfigureAwait(false);admitted();};
            await Session.SendTextAsync(text,EffectiveSettings(),companionName,attachments:files).ConfigureAwait(false);
        }
        finally{Session.BeforeUserTurnAcceptedAsync=PromoteAsync;Volatile.Write(ref sending,0);}
    }
    internal async Task DrainAsync(){await Queue.PauseAsync();await Session.StopAsync();await Session.WaitForIdleAsync();await Session.FlushHistoryAsync();}
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;}
        Session.MessageAdded-=Changed;Session.MessageUpdated-=Changed;Session.StateChanged-=owner.NotifyWorkChanged;Queue.Changed-=owner.NotifyWorkChanged;
        Queue.Dispose();Session.Dispose();
    }
}
