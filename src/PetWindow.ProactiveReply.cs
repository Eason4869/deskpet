using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        sealed class ReplyBinding
        {
            internal string EventId,CompanionId,CompanionName,RelationshipId,WorkspaceId;
            internal ImageDescriptionOverride Fallback;
            internal ConversationWorkspace Workspace;
            internal QuickChatWindow Quick;
            internal ChatWindow Full;
            internal bool Accepted;
        }
        readonly Dictionary<string,ReplyBinding> proactiveReplies=new Dictionary<string,ReplyBinding>(StringComparer.Ordinal);
        string lastProactiveEventId;
        readonly Dictionary<string,string> lastProactiveByCompanion=new Dictionary<string,string>(StringComparer.Ordinal);
        void RememberProactiveEvent(string id,PetSettings settings)
        {
            foreach(string expired in proactiveReplies.Keys.Where(k=>!ProactiveEventExists(k,null) && proactiveReplies[k].Workspace==null).ToArray())proactiveReplies.Remove(expired);
            var model=settings.ImageDescriptionModel;
            proactiveReplies[id]=new ReplyBinding{EventId=id,CompanionId=Skin.Id,CompanionName=Skin.Name,RelationshipId=RelationshipCompanionId,WorkspaceId=Guid.NewGuid().ToString("N"),Fallback=model==null?new ImageDescriptionOverride{Mode="disabled"}:new ImageDescriptionOverride{Mode="selected",Model=model.Copy()}};
            lastProactiveEventId=id;lastProactiveByCompanion[Skin.Id]=id;
        }
        public async void OpenProactiveReply(string eventId)
        {
            if(closing || IsUpdating || DataMaintenance.IsMaintenanceActive || ReturnToGuide())return;
            ReplyBinding binding=null;var item=observationEvents.TryGet(eventId);if(item==null)return;
            try {
            if(item.CompanionId!=Skin.Id || !proactiveReplies.TryGetValue(eventId,out binding))return;
            RecordPetInteraction();
            if(binding.Full!=null && binding.Full.IsVisible){binding.Full.SwitchWorkspace(binding.Workspace);binding.Full.Activate();return;}
            if(binding.Quick!=null){if(!binding.Quick.IsVisible)binding.Quick.Show();binding.Quick.Activate();binding.Quick.FocusInput();return;}
            if(binding.Workspace==null)
            {
                await Workspaces.WaitForClosingAsync();if(closing || DataMaintenance.IsMaintenanceActive || Skin.Id!=binding.CompanionId || !ProactiveEventExists(eventId,binding.CompanionId))return;
                ConversationDefinition definition;
                if(binding.Accepted){definition=ProjectCatalogStore.AllConversations(Projects).FirstOrDefault(c=>c.Id==binding.WorkspaceId && c.PetId==binding.CompanionId);if(definition==null)return;}
                else definition=new ConversationDefinition{Id=binding.WorkspaceId,Name=TextResources.Get("projectCatalog.createConversation.nameStringIsNullOrWhiteSpace"),Mode="chat",Origin="proactive",PetId=binding.CompanionId,SectionId=ProjectCatalogStore.DefaultChatSectionId,ImageDescriptionOverride=binding.Fallback.Copy(),CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
                var project=Projects.Projects.FirstOrDefault(p=>p.Conversations.Any(c=>c.Id==definition.Id))??(Projects.QuickConversations.Any(c=>c.Id==definition.Id)?ProjectCatalogStore.QuickProject(Projects,Settings):ProjectCatalogStore.UnassignedProject(Projects,Settings));
                var workspace=Workspaces.CreateWorkspace(project,definition,transient:!binding.Accepted);binding.Workspace=workspace;
                workspace.Session.ConfigureRelationships(Relationships,()=>binding.RelationshipId);workspace.Session.ProactiveEvents=observationEvents;observationEvents.BindConversation(eventId,binding.CompanionId,workspace.Id);workspace.Session.AttachProactive(item);
                workspace.Session.BeforeUserTurnAcceptedAsync=()=>PromoteProactiveAsync(binding);
                WatchAcceptedProactiveTitle(binding,project,definition);
            }
            var quickWindow=new QuickChatWindow(this,binding.Workspace,()=>ExpandProactiveReply(binding));binding.Quick=quickWindow;
            quickWindow.Closed+=delegate{if(ReferenceEquals(binding.Quick,quickWindow))binding.Quick=null;CloseProactiveIfUnowned(binding);};
            quickWindow.Show();quickWindow.Activate();quickWindow.FocusInput();
            } catch(Exception error) {if(!(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException))throw;Notice=TextResources.Get("desktopObservation.saveFailed");if(binding!=null)CloseProactiveIfUnowned(binding);} finally { ClearEventCopy(item); }
        }
        void WatchAcceptedProactiveTitle(ReplyBinding binding,ProjectDefinition project,ConversationDefinition definition)
        {
            var session=binding.Workspace.Session;Action changed=null;
            changed=delegate{
                if(!binding.Accepted || session.IsBusy || !session.History.Messages.Any(m=>m.Role=="assistant" && m.Source!="proactive" && m.Status=="complete"))return;
                session.StateChanged-=changed;Dispatcher.BeginInvoke(new Action(delegate{if(closing || binding.Workspace==null || binding.Workspace.Session!=session)return;WatchConversationTitle(project,definition,binding.Workspace);QueueConversationTitle(project,definition,session);}));
            };
            session.StateChanged+=changed;
        }
        async Task PromoteProactiveAsync(ReplyBinding binding)
        {
            await Dispatcher.InvokeAsync(new Action(delegate{
                if(binding.Accepted)return;
                if(closing || binding.Workspace==null)throw new InvalidOperationException(TextResources.Get("desktopObservation.saveFailed"));
                ProjectCatalog latest=null;
                if(!TryChangeWorkBindings(new[]{ProjectCatalogStore.UnassignedProjectId},binding.WorkspaceId,()=>{latest=ProjectStore.SaveOrdinaryConversation(binding.Workspace.Definition);} ))throw new InvalidOperationException(TextResources.Get("desktopObservation.saveFailed"));
                binding.Workspace.AdmitPersistence();binding.Accepted=true;
                try{RestoreProjectCatalog(latest);NotifyProjectsChanged();}catch(Exception){Notice=TextResources.Get("desktopObservation.saveFailed");}
            }));
        }
        internal bool IsProactiveTransient(ConversationSession session)
        {return proactiveReplies.Values.Any(b=>b.Workspace!=null && ReferenceEquals(b.Workspace.Session,session) && !b.Accepted);}
        internal async Task SendProactiveAsync(ConversationSession session,string text,IList<ChatAttachment> attachments,Action admitted)
        {
            var binding=proactiveReplies.Values.Single(b=>b.Workspace!=null && ReferenceEquals(b.Workspace.Session,session));
            session.BeforeUserTurnAcceptedAsync=async()=>{await PromoteProactiveAsync(binding).ConfigureAwait(false);admitted();};
            try{await session.SendTextAsync(text,binding.Workspace.EffectiveSettings(),binding.CompanionName,attachments:attachments);}
            finally{session.BeforeUserTurnAcceptedAsync=()=>PromoteProactiveAsync(binding);}
        }
        void ExpandProactiveReply(ReplyBinding binding)
        {
            if(binding.Full==null){var window=new ChatWindow(this,binding.Workspace);binding.Full=window;if(binding.Quick!=null){window.ReplacePendingAttachments(binding.Quick.SnapshotPendingAttachments());binding.Quick.ReplacePendingAttachments(new ChatAttachment[0]);}window.Closing+=delegate{if(binding.Quick!=null)binding.Quick.ReplacePendingAttachments(window.SnapshotPendingAttachments(binding.Workspace.Session));};window.Closed+=delegate{if(ReferenceEquals(binding.Full,window))binding.Full=null;if(binding.Quick!=null){var oldQuick=binding.Quick;binding.Quick=null;oldQuick.Close();}CloseProactiveIfUnowned(binding);};}
            if(binding.Quick!=null)binding.Quick.Hide();if(!binding.Full.IsVisible)binding.Full.Show();binding.Full.Activate();
        }
        async void CloseProactiveIfUnowned(ReplyBinding binding)
        {
            if(closing || binding.Quick!=null || binding.Full!=null || binding.Workspace==null)return;
            var workspace=binding.Workspace;binding.Workspace=null;try{await Workspaces.CloseWorkspaceAsync(workspace.Id);}catch(Exception){binding.Workspace=workspace;}
        }
        string LastProactiveEvent(){string id;return lastProactiveByCompanion.TryGetValue(Skin.Id,out id)?id:null;}
        static void ClearEventCopy(ProactiveEvent item){if(item!=null && item.Png!=null)Array.Clear(item.Png,0,item.Png.Length);}
        bool ProactiveEventExists(string id,string companion){var item=observationEvents.TryGet(id);try{return item!=null && (companion==null || item.CompanionId==companion);}finally{ClearEventCopy(item);}}
        bool HasLastProactiveEvent(){return ProactiveEventExists(LastProactiveEvent(),Skin.Id);}
        bool IsProactiveWorkspace(ConversationWorkspace workspace){return proactiveReplies.Values.Any(b=>ReferenceEquals(b.Workspace,workspace));}
        bool IsProactiveWindow(ChatWindow window){return proactiveReplies.Values.Any(b=>ReferenceEquals(b.Full,window));}
        bool ProactiveWindowsVisible(){return proactiveReplies.Values.Any(b=>WindowInUse(b.Quick)||WindowInUse(b.Full));}
    }
}
