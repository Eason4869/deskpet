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
        public static string SourceLabel(ImageGenerationSource source){switch(source){case ImageGenerationSource.Workspace:return "图像页";case ImageGenerationSource.Conversation:return "对话";case ImageGenerationSource.SettingsTest:return "设置测试";default:return "未知来源";}}
        public static bool Matches(ImageGenerationTaskRecord task,int filter){return filter==0||filter==1&&task.State==ImageGenerationTaskState.Queued||filter==2&&!task.IsTerminal&&task.State!=ImageGenerationTaskState.Queued||filter==3&&task.State==ImageGenerationTaskState.Succeeded||filter==4&&task.IsTerminal&&task.State!=ImageGenerationTaskState.Succeeded;}
        public static string Metadata(ImageGenerationTaskRecord task){return SourceLabel(task.Source)+" · "+task.ProviderName+" · "+task.Model;}
        public static string Elapsed(ImageGenerationTaskRecord task){return "耗时 "+ImageGenerationMotion.ElapsedSeconds(task,DateTime.UtcNow)+" 秒";}
        public static string ResultLabel(ImageGenerationTaskRecord task){return task.DeliveryState==ImageTaskDeliveryState.Failed?"图片已保存，会话同步失败。":task.DeliveryState==ImageTaskDeliveryState.Detached?"独立重试结果，可在图库查看。":!String.IsNullOrEmpty(task.Error)?task.Error:task.State==ImageGenerationTaskState.Succeeded?"结果已保存，可查看原图。":"";}
        // Both repair and normal opening use the manager's one registered logical owner.
        public static async Task RepairExistingAsync(ImageGenerationTaskManager tasks,GeneratedImageStore assets,ImageAssetUsageRegistry usage,IEnumerable<ConversationSession> sessions,ProjectCatalog catalog,string dataRoot,string id,bool persist,CancellationToken token,ConversationWorkspaceManager workspaceOwners=null)
        {
            var task=tasks.Get(id);
            if(task==null||task.Source!=ImageGenerationSource.Conversation||task.State!=ImageGenerationTaskState.Succeeded||task.DeliveryState==ImageTaskDeliveryState.Detached)
                throw new InvalidOperationException("没有可同步的原会话结果。");
            var session=sessions.FirstOrDefault(s=>String.Equals(s.ImageConversationId,task.Origin.ConversationId,StringComparison.Ordinal));
            if(session==null)
            {
                if(catalog==null||workspaceOwners==null)throw new InvalidOperationException("原会话已不可用，请在图库查看图片。");
                if(!persist)throw new InvalidOperationException("请先开启聊天记录保存，再同步未打开的原会话。");
                session=workspaceOwners.GetExistingImageOwner(catalog,task.Origin);
            }
            token.ThrowIfCancellationRequested();
            await session.RepairImageDeliveryAsync(id,token).ConfigureAwait(false);
        }
    }
}
