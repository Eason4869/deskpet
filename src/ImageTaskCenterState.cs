using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class ImageTaskCompletionTracker
    {
        readonly object gate=new object();readonly Dictionary<string,bool> terminal=new Dictionary<string,bool>();
        public ImageTaskCompletionTracker(IEnumerable<ImageGenerationTaskSnapshot> existing){foreach(var task in existing)terminal[task.Id]=task.IsTerminal;}
        public bool Observe(ImageGenerationTaskSnapshot task){lock(gate){bool prior;bool known=terminal.TryGetValue(task.Id,out prior);terminal[task.Id]=task.IsTerminal;return known&&!prior&&task.IsTerminal;}}
    }
    // Shared display rules; native pages own only rows, subscriptions and clocks.
    public static class ImageTaskCenterState
    {
        public static async Task StopForExitAsync(ImageGenerationTaskManager tasks)
        {
            if(tasks==null)return;
            await DrainForExitAsync(()=>Task.Run(()=>tasks.StopAsync(CancellationToken.None))).ConfigureAwait(false);
        }
        public static Task<bool> DrainForExitAsync(Func<Task> drain)
        {Task completion;return BeginExitDrain(drain,out completion);}
        public static Task<bool> BeginExitDrain(Func<Task> drain,out Task completion)
        {
            // The deadline starts before any stage is invoked and covers every later
            // asynchronous stage. A timed-out drain still owns its live resources.
            var deadline=Task.Delay(TimeSpan.FromSeconds(5));completion=Task.Run(drain);
            return WaitForExitAsync(completion,deadline);
        }
        static async Task<bool> WaitForExitAsync(Task pending,Task deadline)
        {
            if(await Task.WhenAny(pending,deadline)==pending){await pending;return true;}
            ObserveDrain(pending);return false;
        }
        static async void ObserveDrain(Task pending){try{await pending.ConfigureAwait(false);}catch(Exception){}}
        public static string SourceLabel(ImageGenerationSource source){switch(source){case ImageGenerationSource.Workspace:return TextResources.Get("imageTaskCenterState.sourceLabel.text");case ImageGenerationSource.Conversation:return TextResources.Get("imageTaskCenterState.sourceLabel.text.alternativeA");case ImageGenerationSource.SettingsTest:return TextResources.Get("imageTaskCenterState.sourceLabel.text.alternativeB");default:return TextResources.Get("imageTaskCenterState.sourceLabel.text.alternativeC");}}
        public static bool Matches(ImageGenerationTaskRecord task,int filter){return filter==0||filter==1&&task.State==ImageGenerationTaskState.Queued||filter==2&&!task.IsTerminal&&task.State!=ImageGenerationTaskState.Queued||filter==3&&task.State==ImageGenerationTaskState.Succeeded||filter==4&&task.IsTerminal&&task.State!=ImageGenerationTaskState.Succeeded;}
        public static string Metadata(ImageGenerationTaskRecord task){return SourceLabel(task.Source)+" · "+task.ProviderName+" · "+task.Model;}
        public static string Elapsed(ImageGenerationTaskRecord task){return TextResources.Format("imageTaskCenterState.elapsed.template",ImageGenerationMotion.ElapsedSeconds(task,DateTime.UtcNow));}
        public static bool HasResult(ImageGenerationTaskRecord task,GeneratedImageStore assets){return task.State==ImageGenerationTaskState.Succeeded&&!String.IsNullOrEmpty(task.ResultImageId)&&assets.Get(task.ResultImageId)!=null;}
        public static bool CanRepairDelivery(ImageGenerationTaskRecord task){return task.Source==ImageGenerationSource.Conversation&&task.State==ImageGenerationTaskState.Succeeded&&(task.DeliveryState==ImageTaskDeliveryState.Failed||task.DeliveryState==ImageTaskDeliveryState.AwaitingSync);}
        public static string ResultLabel(ImageGenerationTaskRecord task,bool resultAvailable=true){if(task.State==ImageGenerationTaskState.Succeeded&&!resultAvailable)return String.IsNullOrEmpty(task.ResultImageId)?TextResources.Get("imageTaskCenterState.resultLabel.text"):TextResources.Get("imageTaskCenterState.resultLabel.text.alternativeA");return task.DeliveryState==ImageTaskDeliveryState.AwaitingSync?TextResources.Get("imageTaskCenterState.resultLabel.text.alternativeB"):task.DeliveryState==ImageTaskDeliveryState.Failed?TextResources.Get("imageTaskCenterState.resultLabel.text.alternativeC"):task.DeliveryState==ImageTaskDeliveryState.Detached?TextResources.Get("imageTaskCenterState.resultLabel.text.alternativeD"):!String.IsNullOrEmpty(task.Error)?task.Error:task.State==ImageGenerationTaskState.Succeeded?TextResources.Get("imageTaskCenterState.resultLabel.text.alternativeE"):"";}
        // Both repair and normal opening use the manager's one registered logical owner.
        public static async Task RepairExistingAsync(ImageGenerationTaskManager tasks,GeneratedImageStore assets,ImageAssetUsageRegistry usage,IEnumerable<ConversationSession> sessions,ProjectCatalog catalog,string dataRoot,string id,bool persist,CancellationToken token,ConversationWorkspaceManager workspaceOwners=null)
        {
            var task=tasks.Get(id);
            if(task==null||task.Source!=ImageGenerationSource.Conversation||task.State!=ImageGenerationTaskState.Succeeded||(task.DeliveryState==ImageTaskDeliveryState.Detached||task.DeliveryState==ImageTaskDeliveryState.Pending))
                throw new InvalidOperationException(TextResources.Get("imageTaskCenterState.repairExistingAsync.text"));
            var session=sessions.FirstOrDefault(s=>String.Equals(s.ImageConversationId,task.Origin.ConversationId,StringComparison.Ordinal));
            if(workspaceOwners!=null&&catalog!=null&&(ProjectCatalogStore.AllConversations(catalog).Any(c=>c.Id==task.Origin.ConversationId)||workspaceOwners.AllOwnedWorkspaces.Any(w=>w.Id==task.Origin.ConversationId)))
            {
                if(session==null&&!persist)throw new InvalidOperationException(TextResources.Get("imageTaskCenterState.repairExistingAsync.text.alternativeA"));
                session=await workspaceOwners.GetExistingImageOwnerAsync(catalog,task.Origin).ConfigureAwait(false);
            }
            else if(session==null)
            {
                if(catalog==null||workspaceOwners==null)throw new InvalidOperationException(TextResources.Get("imageTaskCenterState.repairExistingAsync.text.alternativeB"));
                if(!persist)throw new InvalidOperationException(TextResources.Get("imageTaskCenterState.repairExistingAsync.text.alternativeA"));
                session=await workspaceOwners.GetExistingImageOwnerAsync(catalog,task.Origin).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            await session.RepairImageDeliveryAsync(id,token).ConfigureAwait(false);
        }
    }
}
