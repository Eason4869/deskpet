using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class ConversationWorkspaceManager : IDisposable
    {
        readonly object gate=new object();readonly List<ConversationWorkspace> workspaces=new List<ConversationWorkspace>();
        readonly string dataRoot,personaId;readonly Func<IConversationApi> apiFactory;readonly Func<IReplySpeech> speechFactory;readonly Func<IAudioRecorder> recorderFactory;readonly Func<PetSettings> settings;readonly Func<bool> saveHistory;bool disposed;
        public ConversationWorkspaceManager(string dataRoot,string personaId,Func<IConversationApi> apiFactory,Func<IReplySpeech> speechFactory,Func<IAudioRecorder> recorderFactory,Func<PetSettings> settings,Func<bool> saveHistory)
        {
            this.dataRoot=dataRoot;this.personaId=personaId;this.apiFactory=apiFactory;this.speechFactory=speechFactory;this.recorderFactory=recorderFactory;this.settings=settings;this.saveHistory=saveHistory;
        }
        public int OpenCount {get{lock(gate)return workspaces.Count;}}
        public event Action StateChanged;
        public IAgentApprovalProvider ApprovalProvider {get;set;}
        public IAgentQuestionProvider QuestionProvider {get;set;}
        public GeneratedImageStore ImageStore {get;set;}
        public ImageGenerationClient ImageClient {get;set;}
        public ImageGenerationTaskManager ImageTasks {get;set;}
        public ImageAssetUsageRegistry ImageUsage {get;set;}
        public McpConnectionManager McpConnections {get;set;}
        void NotifyState(){var changed=StateChanged;if(!disposed && changed!=null)changed();}
        public IList<ConversationWorkspace> OpenWorkspaces {get{lock(gate)return workspaces.ToArray();}}
        public bool Busy {get{lock(gate)return workspaces.Any(w=>w.Session.IsBusy || w.Queue.IsRunning);}}
        public ConversationWorkspace CreateWorkspace(ProjectDefinition project,ConversationDefinition conversation,Func<PetSettings> settingsOverride=null)
        {
            if(project==null||conversation==null)throw new ArgumentNullException("conversation");
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("ConversationWorkspaceManager");
                var existing=workspaces.SingleOrDefault(w=>w.Id==conversation.Id);
                if(existing!=null)return existing;
                var workspace=ConversationWorkspace.Create(project,conversation,dataRoot,personaId,apiFactory,speechFactory,recorderFactory,settings,saveHistory,settingsOverride);
                workspace.Session.ApprovalProvider=ApprovalProvider;
                workspace.Session.QuestionProvider=QuestionProvider;
                workspace.Session.ImageStore=ImageStore;
                workspace.Session.ImageClient=ImageClient;
                workspace.Session.ImageTasks=ImageTasks;
                workspace.Session.ImageUsage=ImageUsage;
                workspace.Session.McpConnections=McpConnections;
                workspace.Session.StateChanged+=NotifyState;
                workspace.Queue.Changed+=NotifyState;
                workspaces.Add(workspace);
                return workspace;
            }
        }
        internal ConversationSession GetExistingImageOwner(ProjectCatalog catalog,ImageTaskOrigin origin)
        {
            // Resolve catalog membership before registering an owner; never invent a chat.
            var definition=ProjectCatalogStore.AllConversations(catalog).SingleOrDefault(c=>c.Id==origin.ConversationId);
            if(definition==null)throw new InvalidOperationException("原会话已不可用，请在图库查看图片。");
            var project=catalog.Projects.FirstOrDefault(p=>p.Conversations.Any(c=>c.Id==definition.Id));
            if(project==null)project=catalog.QuickConversations.Any(c=>c.Id==definition.Id)?ProjectCatalogStore.QuickProject(catalog,settings()):ProjectCatalogStore.UnassignedProject(catalog,settings());
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("ConversationWorkspaceManager");
                var existing=workspaces.SingleOrDefault(w=>w.Id==definition.Id);
                if(existing!=null)return existing.Session;
                string directory=ConversationWorkspace.ResolveDataDirectory(dataRoot,definition.Id);
                var history=new HistoryStore(Path.Combine(directory,"history"));
                if(!history.HasSavedHistory(origin.PersonaId??""))throw new InvalidOperationException("原会话记录不存在，请在图库查看图片。");
                return CreateWorkspace(project,definition).Session;
            }
        }
        public async Task CloseWorkspaceAsync(string id)
        {
            ConversationWorkspace workspace;lock(gate){workspace=workspaces.SingleOrDefault(w=>w.Id==id);if(workspace!=null)workspaces.Remove(workspace);}
            if(workspace!=null){workspace.Session.StateChanged-=NotifyState;workspace.Queue.Changed-=NotifyState;await workspace.CloseAsync().ConfigureAwait(false);NotifyState();}
        }
        public async Task SaveAllAsync()
        {
            while(true)
            {
                ConversationWorkspace[] snapshot;lock(gate)snapshot=workspaces.ToArray();
                await Task.WhenAll(snapshot.Select(w=>w.Session.WaitForIdleAsync())).ConfigureAwait(false);
                await Task.WhenAll(snapshot.SelectMany(w=>new Task[]{w.Session.FlushHistoryAsync(),w.Queue.FlushAsync()})).ConfigureAwait(false);
                if(!Busy)break;
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;ConversationWorkspace[] snapshot;lock(gate){snapshot=workspaces.ToArray();workspaces.Clear();}
            foreach(var workspace in snapshot){workspace.Session.StateChanged-=NotifyState;workspace.Queue.Changed-=NotifyState;workspace.Dispose();}
        }
    }
}
