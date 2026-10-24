using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class ConversationWorkspace : IDisposable
    {
        Func<bool> saveHistory;Func<PetSettings> settings;ProjectDefinition project;bool disposed;
        public string Id {get;private set;}
        public string ProjectId {get;private set;}
        public string Name {get;private set;}public string ProjectWorkspace {get;private set;}
        public string DataDirectory {get;private set;}
        public ConversationSession Session {get;private set;}public AgentSessionModel KernelSession {get{return Session.KernelSession;}}
        internal ProjectDefinition Project {get{return project;}}
        internal bool UiObserved;
        internal bool IsDisposed {get{return disposed;}}
        internal bool IsDraft;internal ConversationDefinition Definition;
        AgentJobApplicationService jobServices;
        internal void ConfigureJobServices(AgentJobApplicationService services)
        {
            jobServices=services;
            Session.ConfigureJobServices(GetJobManagerAsync,()=>new AgentJobOwner(ProjectId,Id,"primary"));
        }
        internal Task<AgentJobManager> GetJobManagerAsync(System.Threading.CancellationToken token)
        {
            if(jobServices==null||IsDraft||disposed||Definition.Mode!="work"||ProjectId==ProjectCatalogStore.UnassignedProjectId||ProjectId==ProjectCatalogStore.QuickProjectId)
                return Task.FromResult<AgentJobManager>(null);
            return jobServices.GetManagerAsync(token);
        }
        internal void ConfigureJobs(AgentJobManager manager)
        {
            if(IsDraft||disposed||Definition.Mode!="work"||ProjectId==ProjectCatalogStore.UnassignedProjectId||ProjectId==ProjectCatalogStore.QuickProjectId)throw new InvalidOperationException("job_workspace_required");
            Session.ConfigureJobs(manager,new AgentJobOwner(ProjectId,Id,"primary"));
        }
        bool localAgentSupported=true;
        public bool LocalAgentSupported {get{return localAgentSupported;}set{localAgentSupported=value;}}
        public ConversationQueue Queue {get;private set;}
        ConversationWorkspace(){}
        public static ConversationWorkspace Create(ProjectDefinition project,ConversationDefinition conversation,string dataRoot,string personaId,Func<IConversationApi> apiFactory,Func<IReplySpeech> speechFactory,Func<IAudioRecorder> recorderFactory,Func<PetSettings> settings,Func<bool> saveHistory,Func<PetSettings> settingsOverride=null,Dafeiyu.Maintenance.DataMaintenanceGate maintenance=null)
        {
            maintenance=maintenance??new Dafeiyu.Maintenance.DataMaintenanceGate();using(maintenance.EnterActivity()){
            if(project==null || conversation==null)throw new ArgumentNullException("project");
            if(String.IsNullOrWhiteSpace(project.Id) || String.IsNullOrWhiteSpace(conversation.Id))throw new ArgumentException(TextResources.Get("conversationWorkspace.create.text"));
            if(apiFactory==null || speechFactory==null || recorderFactory==null || settings==null)throw new ArgumentNullException("factories");
            var workspace=new ConversationWorkspace{Id=conversation.Id,ProjectId=project.Id,Name=conversation.Name,ProjectWorkspace=project.Workspace,project=project,Definition=conversation,settings=settingsOverride??settings,saveHistory=saveHistory??delegate{return true;}};
            workspace.DataDirectory=ResolveDataDirectory(dataRoot,conversation.Id);
            Directory.CreateDirectory(workspace.DataDirectory);
            workspace.Session=new ConversationSession(apiFactory(),speechFactory(),recorderFactory,new HistoryStore(Path.Combine(workspace.DataDirectory,"history")),workspace.saveHistory,conversation.Id,maintenance);
            workspace.Session.SelectPersona(conversation.Mode=="work"?"work":String.IsNullOrEmpty(conversation.PetId)?personaId??"":conversation.PetId);
            var kernelStore=new AgentSessionStore(Path.Combine(workspace.DataDirectory,"kernel-sessions"));AgentSessionModel model=null;
            try{if(workspace.saveHistory())model=kernelStore.Load(project.Id,conversation.Id);else kernelStore.Clear();}
            catch(Exception e){if(!AgentSessionStore.StorageError(e))throw;workspace.Session.ReportKernelWarning(TextResources.Get("conversationWorkspace.create.workspaceSessionReportKernelWarning"));}
            if(model==null)model=new AgentSessionModel(conversation.Id,project.Id,conversation.Id);
            if(model.RecoveryState=="running")model.RecoveryState="interrupted";
            workspace.Session.ConfigureKernelSession(model,kernelStore);
            workspace.Queue=new ConversationQueue(workspace.Session,workspace.EffectiveSettings,delegate{return conversation.Name;},Path.Combine(workspace.DataDirectory,"queued-messages.json"));
            return workspace;
        }
        }
        // Rebinds an open workspace to another project after a drag-and-drop move.
        public void FollowProject(ProjectDefinition project)
        {
            if(project==null)return;if(project.Id!=ProjectId && (Session.IsBusy || Queue.IsRunning))throw new InvalidOperationException(TextResources.Get("conversationWorkspace.followProject.text"));this.project=project;ProjectId=project.Id;ProjectWorkspace=project.Workspace;KernelSession.ProjectId=project.Id;
        }
        public PetSettings EffectiveSettings()
        {
            var cfg=settings().Copy();
            if(project.Id!=ProjectCatalogStore.UnassignedProjectId && project.Id!=ProjectCatalogStore.QuickProjectId){
                cfg.AgentWorkspace=project.Workspace;cfg.AgentGoal=project.Goal;cfg.AgentFullAccess=project.FullAccess;cfg.AgentBackupFiles=project.BackupFiles;cfg.AgentPlanMode=project.PlanMode;
                cfg.AgentCommandTimeoutSeconds=project.CommandTimeoutSeconds;cfg.AgentBackgroundJobTimeoutSeconds=project.BackgroundJobTimeoutSeconds;cfg.AgentTerminalPolicy=project.TerminalPolicy;
                cfg.AgentSandboxReadDirectories=project.SandboxReadDirectories;cfg.AgentApprovalMode=project.ApprovalMode;cfg.AgentSandboxEnabled=project.SandboxEnabled;
                cfg.AgentTrustProjectInstructions=project.TrustProjectInstructions;cfg.AgentGlobalInstructions=project.GlobalInstructions;cfg.AgentSkillsDirectory=project.SkillsDirectory;cfg.AgentContextTokenLimit=project.ContextTokenLimit;
            }
            if(!String.IsNullOrEmpty(Definition.Mode))cfg.AgentEnabled=Definition.Mode=="work";
            if(Definition.HasPermissionOverride){cfg.AgentApprovalMode=Definition.ApprovalMode;cfg.AgentSandboxEnabled=Definition.SandboxEnabled;}
            if(!localAgentSupported)cfg.AgentEnabled=false;
            ProjectWorkspace=cfg.AgentWorkspace;return cfg;
        }
        internal static string ResolveDataDirectory(string dataRoot,string conversationId)
        {
            if(String.IsNullOrWhiteSpace(conversationId) || conversationId.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || conversationId=="." || conversationId=="..")throw new ArgumentException(TextResources.Get("conversationWorkspace.resolveDataDirectory.text"));
            string target=Path.GetFullPath(Path.Combine(dataRoot,"conversations",conversationId));bool targetExists=Directory.Exists(target);if(targetExists&&Directory.Exists(Path.Combine(target,"history")))return target;
            string projects=Path.Combine(dataRoot,"projects");if(!Directory.Exists(projects))return target;
            var sources=new System.Collections.Generic.List<string>();
            foreach(string directory in Directory.EnumerateDirectories(projects)){string source=Path.Combine(directory,"chats",conversationId);if(Directory.Exists(source))sources.Add(source);}
            if(targetExists)
            {
                if(!Directory.Exists(Path.Combine(target,"history"))&&sources.Any(source=>Directory.Exists(Path.Combine(source,"history"))))throw new IOException(TextResources.Get("conversationWorkspace.resolveDataDirectory.text.alternativeA"));
                return target;
            }
            if(sources.Count>1)throw new IOException(TextResources.Get("conversationWorkspace.resolveDataDirectory.text.alternativeB"));
            if(sources.Count==1){Directory.CreateDirectory(Path.GetDirectoryName(target));Directory.Move(sources[0],target);}return target;
        }
        public async Task CloseAsync()
        {
            if(disposed)return;
            await Queue.PauseAsync().ConfigureAwait(false);
            await Session.StopAsync().ConfigureAwait(false);
            await Session.SaveHistoryForCloseAsync().ConfigureAwait(false);
            if(saveHistory()&&(!String.IsNullOrEmpty(Session.HistoryWarning)||!String.IsNullOrEmpty(Session.AgentTaskWarning)))throw new IOException(TextResources.Get("conversationWorkspace.closeAsync.text"));
            Dispose();
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(Queue!=null){Queue.Dispose();Queue=null;}
            if(Session!=null){Session.Dispose();Session=null;}
        }
    }
}
