using System.Diagnostics;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class DesktopWorkspaces
{
    private readonly DesktopController owner;
    private readonly string directory;
    private AgentWorkCatalog? catalog;
    private readonly object settingsGate=new();
    private readonly Dictionary<string,(string Provider,string Model)> models=new();
    private readonly HashSet<ConversationWorkspace> observed=new();
    internal AgentJobApplicationService Jobs { get; }
    internal ConversationWorkspaceManager Manager { get; }
    private AgentWorkCatalog Catalog => catalog??=new AgentWorkCatalog(directory,Jobs);
    internal ProjectCatalog Snapshot => Catalog.Snapshot;
    internal DesktopWorkspaces(DesktopController owner,DesktopRuntime runtime)
    {
        this.owner=owner;directory=owner.DataDirectory;
        Jobs=new AgentJobApplicationService(directory,clock=>OperatingSystem.IsWindows()?new WindowsAgentJobProcessFactory(clock):throw new PlatformNotSupportedException(),new Clock());
        _=Jobs.InitializeAsync();
        Manager=new ConversationWorkspaceManager(directory,owner.Skin.Id,()=>new ApiClient(),()=>runtime.CreateSpeech(new ApiClient()),runtime.CreateRecorder,()=>owner.Settings,()=>owner.Settings.SaveChatHistory)
        {JobServices=Jobs,QuestionProvider=owner.Questions,ApprovalProvider=new DesktopWorkApproval(owner),ImageStore=owner.ImageStore,ImageClient=owner.ImageClient,ImageTasks=owner.ImageTasks,ImageUsage=owner.ImageUsage};
        Manager.StateChanged+=owner.NotifyWorkChanged;
    }
    internal Task<ProjectDefinition> CreateProjectAsync(string name,string root) => Catalog.CreateProjectAsync(name,root,CancellationToken.None);
    internal Task<ConversationDefinition> CreateConversationAsync(string projectId,string name) => Catalog.CreateConversationAsync(projectId,name,CancellationToken.None);
    internal async Task<ConversationWorkspace> OpenAsync(string projectId,string conversationId,CancellationToken token)
    {
        var workspace=await Catalog.OpenAsync(Manager,projectId,conversationId,token,()=>SessionSettings(conversationId));
        workspace.LocalAgentSupported=OperatingSystem.IsWindows();
        lock(settingsGate)if(observed.Add(workspace)){workspace.Session.MessageAdded+=MessageChanged;workspace.Session.MessageUpdated+=MessageChanged;workspace.Session.HistoryChanged+=owner.NotifyWorkChanged;}
        return workspace;
    }
    private void MessageChanged(ChatMessage message)=>owner.NotifyWorkChanged();
    private PetSettings SessionSettings(string id)
    {
        var settings=owner.Settings.Copy();lock(settingsGate)if(models.TryGetValue(id,out var model))settings.SelectTextModel(model.Provider,model.Model);return settings;
    }
    internal void SelectModel(string id,string provider,string model)
    {lock(settingsGate)models[id]=(provider,model);owner.NotifyWorkChanged();}
    internal Task RenameArchiveAsync(string project,string conversation,string name,bool archived) => Catalog.ChangeConversationAsync(project,conversation,name,archived,CancellationToken.None);
    internal PetSettings ProjectSettings(string id)
    {
        var workspace=Manager.OpenWorkspaces.FirstOrDefault(value=>value.ProjectId==id);if(workspace!=null)return workspace.EffectiveSettings();
        var project=Snapshot.Projects.Single(value=>value.Id==id);var settings=owner.Settings.Copy();settings.AgentWorkspace=project.Workspace;settings.AgentFullAccess=project.FullAccess;settings.AgentApprovalMode=project.ApprovalMode;settings.AgentSandboxEnabled=project.SandboxEnabled;settings.AgentPlanMode=project.PlanMode;settings.AgentGoal=project.Goal;settings.AgentBackupFiles=project.BackupFiles;settings.AgentContextTokenLimit=project.ContextTokenLimit;return settings;
    }
    internal bool TryChangeProject(string id,Action<ProjectDefinition> change)
    {
        if(Manager.OpenWorkspaces.Any(workspace=>workspace.ProjectId==id&&(workspace.Session.IsBusy||workspace.Queue.IsRunning)))return false;
        if(!Catalog.TryChangeProject(id,change))return false;
        var project=Catalog.Snapshot.Projects.Single(value=>value.Id==id);
        foreach(var workspace in Manager.OpenWorkspaces.Where(value=>value.ProjectId==id))workspace.FollowProject(project);
        owner.NotifyWorkChanged();return true;
    }
    internal async Task StopForExitAsync()
    {
        var report=await Jobs.StopAllForExitAsync(CancellationToken.None);
        if(!report.CleanupConfirmed)throw new IOException(TextResources.Get("agentJobs.exitBlocked"));
        foreach(var workspace in Manager.OpenWorkspaces){workspace.Session.MessageAdded-=MessageChanged;workspace.Session.MessageUpdated-=MessageChanged;workspace.Session.HistoryChanged-=owner.NotifyWorkChanged;await Manager.CloseWorkspaceAsync(workspace.Id);}
        Manager.StateChanged-=owner.NotifyWorkChanged;Manager.Dispose();Jobs.Dispose();
    }
    private sealed class Clock:IAgentJobClock
    {private readonly Stopwatch elapsed=Stopwatch.StartNew();public DateTime UtcNow=>DateTime.UtcNow;public long ElapsedMilliseconds=>elapsed.ElapsedMilliseconds;}
}
