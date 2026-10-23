using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    // A lease-protected view of the shared work catalog. Opening work never
    // imports ordinary history; the WPF ownership migration is a separate step.
    public sealed class AgentWorkCatalog
    {
        readonly object gate=new object();
        readonly AgentJobApplicationService jobs;
        readonly AgentJobCatalogGuard mutations;
        readonly ProjectCatalogStore store;
        readonly string path;
        ProjectCatalog catalog;
        string baseline;
        public AgentWorkCatalog(string dataDirectory,AgentJobApplicationService jobs)
        {
            if(jobs==null)throw new ArgumentNullException("jobs");
            this.jobs=jobs;mutations=new AgentJobCatalogGuard(jobs);
            path=Path.Combine(dataDirectory,"projects.json");store=new ProjectCatalogStore(path);
            catalog=store.Load();baseline=ProjectCatalogStore.WorkBindingSignature(catalog);
            store.BeforeSave=next=>
            {
                var current=new ProjectCatalogStore(path).Load();
                if(ProjectCatalogStore.WorkBindingSignature(current)!=baseline)throw new IOException(TextResources.Get("agent.jobs.ui.bindingChanged"));
                jobs.EnsureWritable();
                // Another host may still create ordinary chats without owning
                // the work lease. Preserve that unrelated metadata atomically.
                next.UnassignedConversations=current.UnassignedConversations;
                next.QuickConversations=current.QuickConversations;
                next.SidebarConversationOrder=current.SidebarConversationOrder;
                next.SidebarSectionLabels=current.SidebarSectionLabels;
                next.Sections=current.Sections;
                next.RecentIncludeProjects=current.RecentIncludeProjects;
            };
            store.AfterSave=next=>baseline=ProjectCatalogStore.WorkBindingSignature(next);
        }
        public ProjectCatalog Snapshot {get{lock(gate)return catalog.Copy();}}
        public async Task<ProjectDefinition> CreateProjectAsync(string name,string directory,CancellationToken token)
        {
            ProjectDefinition created=null;
            await mutations.RunAsync(new[]{Guid.NewGuid().ToString("N")},null,false,()=>
            {
                lock(gate){var next=catalog.Copy();created=ProjectCatalogStore.CreateProject(next,name,directory);store.Save(next);catalog=next;created=created.Copy();}
                return Task.FromResult(0);
            },token).ConfigureAwait(false);
            return created;
        }
        public async Task<ConversationDefinition> CreateConversationAsync(string projectId,string name,CancellationToken token)
        {
            ConversationDefinition created=null;
            await mutations.RunAsync(new[]{projectId},null,false,()=>
            {
                lock(gate){var next=catalog.Copy();created=ProjectCatalogStore.CreateConversation(next,projectId,name);created.Mode="work";store.Save(next);catalog=next;created=created.Copy();}
                return Task.FromResult(0);
            },token).ConfigureAwait(false);
            return created;
        }
        public async Task<ConversationWorkspace> OpenAsync(ConversationWorkspaceManager workspaces,string projectId,string conversationId,CancellationToken token,Func<PetSettings> settingsOverride=null)
        {
            await jobs.EnsureWritableAsync(token).ConfigureAwait(false);
            ProjectDefinition project;ConversationDefinition conversation;
            lock(gate)
            {
                // Re-read under the store's exclusive writer check before a
                // workspace becomes a writer; stale catalog snapshots fail closed.
                using(var readLease=new FileStream(path+".write.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    if(ProjectCatalogStore.WorkBindingSignature(new ProjectCatalogStore(path).Load())!=baseline)throw new IOException(TextResources.Get("agent.jobs.ui.bindingChanged"));
                }
                project=catalog.Projects.SingleOrDefault(value=>value.Id==projectId);
                conversation=project==null?null:project.Conversations.SingleOrDefault(value=>value.Id==conversationId&&value.Mode=="work");
                if(conversation==null)throw new InvalidOperationException(TextResources.Get("agent.work.notAvailable"));
                project=project.Copy();conversation=project.Conversations.Single(value=>value.Id==conversationId);
            }
            token.ThrowIfCancellationRequested();
            return await workspaces.CreateWorkspaceAsync(project,conversation,settingsOverride).ConfigureAwait(false);
        }
        public bool TryChangeProject(string projectId,Action<ProjectDefinition> change)
        {
            return mutations.TryRun(new[]{projectId},null,()=>
            {
                lock(gate){var next=catalog.Copy();var project=next.Projects.Single(value=>value.Id==projectId);change(project);store.Save(next);catalog=next;}
            });
        }
        public Task ChangeConversationAsync(string projectId,string conversationId,string name,bool archived,CancellationToken token)
        {
            return mutations.RunAsync(new[]{projectId},conversationId,false,()=>
            {
                lock(gate)
                {
                    var next=catalog.Copy();var project=next.Projects.Single(value=>value.Id==projectId);var conversation=project.Conversations.Single(value=>value.Id==conversationId);
                    name=(name??"").Trim();if(name.Length<1||name.Length>80)throw new ArgumentException(TextResources.Get("projectCatalog.createConversation.text.alternativeA"));
                    conversation.Name=name;conversation.NameEdited=true;conversation.Archived=archived;store.Save(next);catalog=next;
                }
                return Task.FromResult(0);
            },token);
        }
    }
}
