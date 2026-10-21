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
        // An unopened conversation is repaired from existing storage only. No workspace,
        // kernel, API client, catalog entry or new chat is constructed for this operation.
        public static async Task RepairExistingAsync(ImageGenerationTaskManager tasks,GeneratedImageStore assets,ImageAssetUsageRegistry usage,IEnumerable<ConversationSession> sessions,ProjectCatalog catalog,string dataRoot,string id,bool persist,CancellationToken token)
        {
            var task=tasks.Get(id);if(task==null||task.Source!=ImageGenerationSource.Conversation||task.State!=ImageGenerationTaskState.Succeeded||task.DeliveryState==ImageTaskDeliveryState.Detached)throw new InvalidOperationException("没有可同步的原会话结果。");
            var origin=task.Origin;var session=sessions.FirstOrDefault(s=>String.Equals(s.ImageConversationId,origin.ConversationId,StringComparison.Ordinal));
            if(session!=null){await session.RepairImageDeliveryAsync(id,token).ConfigureAwait(false);return;}
            if(catalog==null||!ProjectCatalogStore.AllConversations(catalog).Any(c=>c.Id==origin.ConversationId))throw new InvalidOperationException("原会话已不可用，请在图库查看图片。");
            string historyPath=ExistingHistory(dataRoot,origin.ConversationId);if(historyPath==null)throw new InvalidOperationException("原会话记录不存在，请在图库查看图片。");
            var historyStore=new HistoryStore(historyPath){ImageUsage=usage};var messages=historyStore.Load(origin.PersonaId??"");if(!String.IsNullOrEmpty(historyStore.Warning))throw new IOException("原会话记录无法读取，已保留原文件。");
            if(!messages.Any(m=>m.Id==origin.MessageId)){await tasks.SetDeliveryStateAsync(id,ImageTaskDeliveryState.Detached).ConfigureAwait(false);return;}
            if(!persist)throw new InvalidOperationException("请先开启聊天记录保存，再同步未打开的原会话。");
            token.ThrowIfCancellationRequested();await tasks.PromoteAsync(id,token).ConfigureAwait(false);var image=assets.Get(task.ResultImageId);if(image==null)throw new IOException("已保存的图片不可用。");
            var history=new ConversationLog();history.ConfigureImageUsage(usage);
            try{history.Replace(messages);history.AttachGeneratedImage(origin.MessageId,image);await historyStore.SaveAsync(origin.PersonaId??"",history.Messages).ConfigureAwait(false);if(!String.IsNullOrEmpty(historyStore.Warning))throw new IOException("原会话同步未保存，请检查本机存储。");await tasks.SetDeliveryStateAsync(id,ImageTaskDeliveryState.Delivered).ConfigureAwait(false);}
            finally{history.ReleaseImageUsage();}
        }
        static string ExistingHistory(string root,string id)
        {
            if(String.IsNullOrWhiteSpace(id)||id.IndexOfAny(Path.GetInvalidFileNameChars())>=0||id=="."||id=="..")return null;
            var modern=Path.Combine(root,"conversations",id,"history");if(Directory.Exists(modern))return modern;var projects=Path.Combine(root,"projects");if(!Directory.Exists(projects))return null;
            var found=Directory.EnumerateDirectories(projects).Select(p=>Path.Combine(p,"chats",id,"history")).Where(Directory.Exists).ToArray();if(found.Length>1)throw new IOException("多个原会话目录具有相同 ID，请先核对。");return found.FirstOrDefault();
        }
    }
}
