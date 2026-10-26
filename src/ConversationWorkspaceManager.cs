using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class ConversationWorkspaceManager : IDisposable
    {
        public Dafeiyu.Maintenance.DataMaintenanceGate Maintenance {get;private set;}
        readonly object gate=new object();readonly List<ConversationWorkspace> workspaces=new List<ConversationWorkspace>();
        readonly Dictionary<string,Task> closing=new Dictionary<string,Task>(StringComparer.Ordinal);
        readonly string dataRoot,personaId;readonly Func<IConversationApi> apiFactory;readonly Func<IReplySpeech> speechFactory;readonly Func<IAudioRecorder> recorderFactory;readonly Func<PetSettings> settings;readonly Func<bool> saveHistory;bool disposed;
        public ConversationWorkspaceManager(string dataRoot,string personaId,Func<IConversationApi> apiFactory,Func<IReplySpeech> speechFactory,Func<IAudioRecorder> recorderFactory,Func<PetSettings> settings,Func<bool> saveHistory,Dafeiyu.Maintenance.DataMaintenanceGate maintenance=null)
        {
            Maintenance=maintenance??new Dafeiyu.Maintenance.DataMaintenanceGate();this.dataRoot=dataRoot;this.personaId=personaId;this.apiFactory=apiFactory;this.speechFactory=speechFactory;this.recorderFactory=recorderFactory;this.settings=settings;this.saveHistory=saveHistory;
        }
        ComposerDraftStore composerDrafts;
        internal void BindDraft(ConversationWorkspace workspace,string role)
        {
            if(composerDrafts==null)composerDrafts=new ComposerDraftStore(dataRoot,Maintenance);
            workspace.DraftOwner=role;
            workspace.DraftProjectChanged=delegate{if(workspace.IsDraft)BindDraft(workspace,workspace.DraftOwner);};
            workspace.Session.BindDraft(composerDrafts,ComposerDraftStore.Scope(role,workspace.Definition.Mode,workspace.ProjectId,workspace.Definition.SectionId),saveHistory);
        }
        public int OpenCount {get{lock(gate)return workspaces.Count(w=>!closing.ContainsKey(w.Id));}}
        public event Action StateChanged;
        AgentJobApplicationService jobServices;
        public AgentJobApplicationService JobServices
        {
            get{lock(gate)return jobServices;}
            set{lock(gate){if(workspaces.Count>0&&!Object.ReferenceEquals(value,jobServices))throw new InvalidOperationException("workspaces_open");jobServices=value;}}
        }
        public RelationshipRuntime Relationships {get;set;}
        public Func<string> RelationshipCompanion {get;set;}
        public IAgentApprovalProvider ApprovalProvider {get;set;}
        public IAgentQuestionProvider QuestionProvider {get;set;}
        public GeneratedImageStore ImageStore {get;set;}
        public ImageGenerationClient ImageClient {get;set;}
        public ImageGenerationTaskManager ImageTasks {get;set;}
        public ImageAssetUsageRegistry ImageUsage {get;set;}
        public McpConnectionManager McpConnections {get;set;}
        void NotifyState(){var changed=StateChanged;if(!disposed && changed!=null)changed();}
        public IList<ConversationWorkspace> OpenWorkspaces {get{lock(gate)return workspaces.Where(w=>!closing.ContainsKey(w.Id)).ToArray();}}
        internal IList<ConversationWorkspace> AllOwnedWorkspaces {get{lock(gate)return workspaces.ToArray();}}
        public bool Busy {get{lock(gate)return closing.Count>0||workspaces.Any(w=>w.Session.IsBusy || w.Queue.IsRunning);}}
        public ConversationWorkspace CreateWorkspace(ProjectDefinition project,ConversationDefinition conversation,Func<PetSettings> settingsOverride=null)
        {using(Maintenance.EnterActivity()){
            if(project==null||conversation==null)throw new ArgumentNullException("conversation");
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("ConversationWorkspaceManager");
                if(closing.ContainsKey(conversation.Id))throw new InvalidOperationException(TextResources.Get("conversationWorkspaceManager.createWorkspace.text"));
                var existing=workspaces.SingleOrDefault(w=>w.Id==conversation.Id);
                if(existing!=null)return existing;
                var workspace=ConversationWorkspace.Create(project,conversation,dataRoot,personaId,apiFactory,speechFactory,recorderFactory,settings,saveHistory,settingsOverride,Maintenance);
                workspace.Session.ConfigureRelationships(Relationships,RelationshipCompanion);
                workspace.Session.ApprovalProvider=ApprovalProvider;
                workspace.Session.QuestionProvider=QuestionProvider;
                workspace.Session.ImageStore=ImageStore;
                workspace.Session.ImageClient=ImageClient;
                workspace.Session.ImageTasks=ImageTasks;
                workspace.Session.ImageUsage=ImageUsage;
                workspace.Session.McpConnections=McpConnections;
                workspace.ConfigureJobServices(jobServices);
                workspace.Session.StateChanged+=NotifyState;
                workspace.Queue.Changed+=NotifyState;
                workspaces.Add(workspace);
                return workspace;
            }
        }}
        public async Task<ConversationWorkspace> CreateWorkspaceAsync(ProjectDefinition project,ConversationDefinition conversation,Func<PetSettings> settingsOverride=null)
        {
            while(true)
            {
                Task pending;
                lock(gate)
                {
                    if(disposed)throw new ObjectDisposedException("ConversationWorkspaceManager");
                    if(!closing.TryGetValue(conversation.Id,out pending))return CreateWorkspace(project,conversation,settingsOverride);
                }
                await pending.ConfigureAwait(false);
            }
        }
        internal async Task<ConversationSession> GetExistingImageOwnerAsync(ProjectCatalog catalog,ImageTaskOrigin origin)
        {return (await GetCatalogWorkspaceAsync(catalog,origin.ConversationId,origin.PersonaId??"").ConfigureAwait(false)).Session;}
        internal Task<ConversationWorkspace> OpenCatalogWorkspaceAsync(ProjectCatalog catalog,string id)
        {
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("ConversationWorkspaceManager");
                var draft=workspaces.SingleOrDefault(w=>w.Id==id&&w.IsDraft);
                // UI expansion may share an unsent owner that deliberately has no catalog row.
                // Image repair continues through the strict original-catalog path below.
                if(draft!=null&&!closing.ContainsKey(id))return Task.FromResult(draft);
            }
            return GetCatalogWorkspaceAsync(catalog,id,null);
        }
        async Task<ConversationWorkspace> GetCatalogWorkspaceAsync(ProjectCatalog catalog,string id,string requiredPersona)
        {using(Maintenance.EnterActivity()){
            while(true)
            {
                Task pending;
                lock(gate)
                {
                    if(disposed)throw new ObjectDisposedException("ConversationWorkspaceManager");
                    // Re-resolve membership and project after every awaited close handoff.
                    var definition=ProjectCatalogStore.AllConversations(catalog).SingleOrDefault(c=>c.Id==id);
                    if(definition==null)throw new InvalidOperationException(TextResources.Get("conversationWorkspaceManager.getCatalogWorkspaceAsync.text"));
                    if(!closing.TryGetValue(id,out pending))
                    {
                        var existing=workspaces.SingleOrDefault(w=>w.Id==id);
                        if(existing!=null)return existing;
                        var project=catalog.Projects.FirstOrDefault(p=>p.Conversations.Any(c=>c.Id==id));
                        if(project==null)project=catalog.QuickConversations.Any(c=>c.Id==id)?ProjectCatalogStore.QuickProject(catalog,settings()):ProjectCatalogStore.UnassignedProject(catalog,settings());
                        if(requiredPersona!=null)
                        {
                            string directory=ConversationWorkspace.ResolveDataDirectory(dataRoot,id);
                            if(!new HistoryStore(Path.Combine(directory,"history")).HasSavedHistory(requiredPersona))throw new InvalidOperationException(TextResources.Get("conversationWorkspaceManager.getCatalogWorkspaceAsync.text.alternativeA"));
                        }
                        return CreateWorkspace(project,definition);
                    }
                }
                await pending.ConfigureAwait(false);
            }
        }}
        public Task CloseWorkspaceAsync(string id)
        {
            lock(gate)
            {
                Task pending;
                if(closing.TryGetValue(id,out pending))return pending;
                var workspace=workspaces.SingleOrDefault(w=>w.Id==id);
                if(workspace==null)return Task.FromResult(0);
                var admission=Maintenance.EnterActivity();
                var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                // Publish ownership before invoking any synchronous pre-await close work.
                closing.Add(id,completion.Task);
                Task.Run(async delegate
                {
                    Exception error=null;
                    try
                    {
                        workspace.Session.StateChanged-=NotifyState;
                        workspace.Queue.Changed-=NotifyState;
                        await workspace.CloseAsync().ConfigureAwait(false);
                    }
                    catch(Exception failure){error=failure;}
                    finally
                    {
                        lock(gate)
                        {
                            if(error==null)workspaces.Remove(workspace);
                            else if(!workspace.IsDisposed){workspace.Session.StateChanged+=NotifyState;workspace.Queue.Changed+=NotifyState;}
                            if(error==null||!workspace.IsDisposed)closing.Remove(id);
                        }
                        admission.Dispose();if(error==null)completion.TrySetResult(true);else completion.TrySetException(error);
                        NotifyState();
                    }
                });
                return completion.Task;
            }
        }
        internal Task WaitForClosingAsync(){lock(gate)return Task.WhenAll(closing.Values.ToArray());}
        public async Task SaveAllAsync()
        {
            while(true)
            {
                Task[] pending;ConversationSession[] sessions;ConversationQueue[] queues;
                lock(gate){pending=closing.Values.ToArray();var active=workspaces.Where(w=>!closing.ContainsKey(w.Id)).ToArray();sessions=active.Select(w=>w.Session).ToArray();queues=active.Select(w=>w.Queue).ToArray();}
                await Task.WhenAll(pending.Concat(sessions.Select(session=>session.WaitForIdleAsync()))).ConfigureAwait(false);
                await Task.WhenAll(sessions.Select(session=>session.FlushHistoryAsync()).Concat(queues.Select(queue=>queue.FlushAsync()))).ConfigureAwait(false);
                if(composerDrafts!=null)composerDrafts.Flush();
                if(!Busy)break;
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;ConversationWorkspace[] snapshot;lock(gate){snapshot=workspaces.Where(w=>!closing.ContainsKey(w.Id)).ToArray();foreach(var workspace in snapshot)workspaces.Remove(workspace);}
            foreach(var workspace in snapshot){workspace.Session.StateChanged-=NotifyState;workspace.Queue.Changed-=NotifyState;workspace.Dispose();}
            if(composerDrafts!=null)composerDrafts.Dispose();
        }
    }
}
