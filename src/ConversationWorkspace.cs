using System;
using System.IO;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class ConversationWorkspace : IDisposable
    {
        Func<bool> saveHistory;bool disposed;
        public string Id {get;private set;}
        public string ProjectId {get;private set;}
        public string Name {get;private set;}public string ProjectWorkspace {get;private set;}
        public string DataDirectory {get;private set;}
        public ConversationSession Session {get;private set;}public AgentSessionModel KernelSession {get;private set;}
        public ConversationQueue Queue {get;private set;}
        ConversationWorkspace(){}
        public static ConversationWorkspace Create(ProjectDefinition project,ConversationDefinition conversation,string dataRoot,string personaId,Func<IConversationApi> apiFactory,Func<IReplySpeech> speechFactory,Func<IAudioRecorder> recorderFactory,Func<PetSettings> settings,Func<bool> saveHistory,Func<PetSettings> settingsOverride=null)
        {
            if(project==null || conversation==null)throw new ArgumentNullException("project");
            if(String.IsNullOrWhiteSpace(project.Id) || String.IsNullOrWhiteSpace(conversation.Id))throw new ArgumentException("项目和会话必须具有稳定 ID。");
            if(apiFactory==null || speechFactory==null || recorderFactory==null || settings==null)throw new ArgumentNullException("factories");
            var workspace=new ConversationWorkspace{Id=conversation.Id,ProjectId=project.Id,Name=conversation.Name,ProjectWorkspace=project.Workspace,saveHistory=saveHistory??delegate{return true;}};
            workspace.DataDirectory=Path.GetFullPath(Path.Combine(dataRoot,"projects",project.Id,"chats",conversation.Id));
            Directory.CreateDirectory(workspace.DataDirectory);
            workspace.Session=new ConversationSession(apiFactory(),speechFactory(),recorderFactory,new HistoryStore(Path.Combine(workspace.DataDirectory,"history")),workspace.saveHistory);
            workspace.Session.SelectPersona(personaId??"");
            workspace.KernelSession=new AgentSessionModel(conversation.Id,project.Id,conversation.Id);var kernelStore=new AgentSessionStore(Path.Combine(workspace.DataDirectory,"kernel-sessions"));workspace.Session.ConfigureKernelSession(workspace.KernelSession,kernelStore);
            workspace.Queue=new ConversationQueue(workspace.Session,settingsOverride??settings,delegate{return conversation.Name;},Path.Combine(workspace.DataDirectory,"queued-messages.json"));
            return workspace;
        }
        // Rebinds an open workspace to another project after a drag-and-drop move.
        public void FollowProject(ProjectDefinition project)
        {
            if(project==null)return;ProjectId=project.Id;ProjectWorkspace=project.Workspace;
        }
        public async Task CloseAsync()
        {
            if(disposed)return;
            try{await Queue.PauseAsync().ConfigureAwait(false);await Session.FlushHistoryAsync().ConfigureAwait(false);}finally{Dispose();}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(Queue!=null){Queue.Dispose();Queue=null;}
            if(Session!=null){Session.Dispose();Session=null;}
        }
    }
}
