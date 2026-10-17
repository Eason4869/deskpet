using System;
using System.Collections.Generic;
using System.Linq;
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
        public IList<ConversationWorkspace> OpenWorkspaces {get{lock(gate)return workspaces.ToArray();}}
        public bool Busy {get{lock(gate)return workspaces.Any(w=>w.Session.IsBusy || w.Queue.IsRunning);}}
        public ConversationWorkspace CreateWorkspace(ProjectDefinition project,ConversationDefinition conversation,Func<PetSettings> settingsOverride=null)
        {
            if(disposed)throw new ObjectDisposedException("ConversationWorkspaceManager");
            var workspace=ConversationWorkspace.Create(project,conversation,dataRoot,personaId,apiFactory,speechFactory,recorderFactory,settings,saveHistory,settingsOverride);lock(gate)workspaces.Add(workspace);return workspace;
        }
        public async Task CloseWorkspaceAsync(string id)
        {
            ConversationWorkspace workspace;lock(gate){workspace=workspaces.SingleOrDefault(w=>w.Id==id);if(workspace!=null)workspaces.Remove(workspace);}
            if(workspace!=null)await workspace.CloseAsync().ConfigureAwait(false);
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
            foreach(var workspace in snapshot)workspace.Dispose();
        }
    }
}
