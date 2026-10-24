using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        ImageAssetUsageRegistry imageUsage;
        Task imageRepairTail=Task.FromResult(0);
        public ImageAssetUsageRegistry ImageUsage {get{return imageUsage;}set{lock(gate){if(imageUsage!=null&&!Object.ReferenceEquals(imageUsage,value))throw new InvalidOperationException(TextResources.Get("conversationImageGeneration.imageUsage.text"));imageUsage=value;if(store!=null)store.ImageUsage=value;foreach(var log in histories.Values)log.ConfigureImageUsage(value);}}}
        public GeneratedImageStore ImageStore {get;set;}
        public ImageGenerationClient ImageClient {get;set;}
        public ImageGenerationTaskManager ImageTasks {get;set;}
        // Logical history ownership is independent of the transient Agent kernel.
        public string ImageConversationId {get;private set;}
        static string CreateImageConversationId(HistoryStore history)
        {
            if(history==null)return "ephemeral:"+Guid.NewGuid().ToString("N");
            string path=System.IO.Path.GetFullPath(history.DirectoryPath).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)path=path.ToUpperInvariant();
            using(var hash=System.Security.Cryptography.SHA256.Create())return "history:"+BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path))).Replace("-","").ToLowerInvariant();
        }
        public IAgentQuestionProvider QuestionProvider {get;set;}

        void ConfigureInteractiveTools(Operation op)
        {
            op.Workspace.QuestionProvider=QuestionProvider;
            var provider=op.Snapshot.SelectedImageProvider();
            if(provider==null||String.IsNullOrWhiteSpace(provider.Connection.BaseUrl)||String.IsNullOrWhiteSpace(provider.Connection.Model)||ImageTasks==null||ImageStore==null)return;
            op.Workspace.ImageGenerator=async (prompt,callId,cancel)=>
            {
                string originReplyId=op.ReplyId;op.History.RetainImageReply(originReplyId,true);
                try
                {
                var request=new ImageGenerationRequest{Provider=provider.Copy(),Prompt=prompt,Size=provider.Size,Quality=provider.Quality,
                    SessionId=kernelSession==null?op.PersonaId:kernelSession.SessionId,TaskId=op.Workspace.TaskState==null?"":op.Workspace.TaskState.Id,
                    MessageId=originReplyId,Persistent=saveEnabled()};
                GeneratedImage image;string jobId=null;
                var submission=new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),request,new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=ImageConversationId,KernelSessionId=request.SessionId,PersonaId=op.PersonaId,AgentTaskId=request.TaskId,ToolCallId=callId,MessageId=originReplyId,GroupId=originReplyId,Sequence=(op.History.Messages.First(m=>m.Id==originReplyId).ImageGenerationTaskIds??new System.Collections.Generic.List<string>()).Count});
                submission.BeforeStart=async id=>{jobId=id;cancel.ThrowIfCancellationRequested();lock(gate){if(disposed||active!=op||op.Ended!=0)throw new OperationCanceledException(cancel);if(op.History.AttachImageTask(originReplyId,id)==null)throw new InvalidOperationException(TextResources.Get("conversationImageGeneration.configureInteractiveTools.idCancelThrowIfCancellationRequested"));}await PersistImageHistoryAsync(op.PersonaId,op.History,cancel).ConfigureAwait(false);};
                try{var handle=await ImageTasks.EnqueueAsync(submission,cancel).ConfigureAwait(false);jobId=handle.Id;image=await handle.Completion.ConfigureAwait(false);}
                catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;return "错误：这张图片的生成已取消，可以继续对话；不会自动重试。";}
                catch(InvalidOperationException error){return "错误："+error.Message+" 不会自动重试；请处理服务配置后再发起新请求。";}
                catch(System.IO.InvalidDataException){return "错误：图片服务返回的图片或响应格式无效，未标记生成完成；不会自动重试。";}
                catch(TimeoutException error){throw new ApiException(error.Message+" 服务可能仍在生成，请核对后再发起新请求。不会自动重试。");}
                catch(System.IO.IOException error){throw new ApiException(error.Message+" 图片服务可能已接收请求，请核对后再操作。不会自动重试。");}
                if(cancel.IsCancellationRequested||disposed||active!=op||op.Ended!=0){await MarkImageDeliveryAsync(jobId,ImageTaskDeliveryState.Detached).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();return "图片已进入图库，原轮次已结束。";}
                try{await DeliverImageAsync(jobId,op.History,op.PersonaId,cancel,op).ConfigureAwait(false);}
                catch(OperationCanceledException){throw;}
                catch(Exception){MarkImageDeliveryAsync(jobId,ImageTaskDeliveryState.Failed).GetAwaiter().GetResult();return new JavaScriptSerializer().Serialize(new{asset_id=image.Id,status="complete",delivery_status="failed",message=TextResources.Get("conversationImageGeneration.configureInteractiveTools.javaScriptSerializer")});}
                return new JavaScriptSerializer().Serialize(new{asset_id=image.Id,status="complete",delivery_status=ImageTasks.Get(jobId).DeliveryState.ToString(),message=TextResources.Get("conversationImageGeneration.configureInteractiveTools.javaScriptSerializer.alternativeA"),model=image.Model,width=image.Width,height=image.Height});
                }
                finally{op.History.RetainImageReply(originReplyId,false);if(op.ReplyId!=originReplyId)op.History.RemoveEmptyReply(originReplyId);}
            };
        }
        // Delivery journaling must never turn a committed image into an ambiguous generation failure.
        async Task MarkImageDeliveryAsync(string id,ImageTaskDeliveryState state)
        {
            try{await ImageTasks.SetDeliveryStateAsync(id,state).ConfigureAwait(false);}
            catch(System.IO.IOException){imageHistoryWarning=TextResources.Get("conversationImageGeneration.markImageDeliveryAsync.imageHistoryWarning");}
            catch(UnauthorizedAccessException){imageHistoryWarning=TextResources.Get("conversationImageGeneration.markImageDeliveryAsync.imageHistoryWarning.alternativeA");}
        }
        async Task PersistImageHistoryAsync(string persona,ConversationLog history,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();if(store==null||!saveEnabled())return;
            await PromoteImageTasksAsync(history,token).ConfigureAwait(false);
            Task saved;
            // Ordinary Save also snapshots/enqueues under this gate. A later live edit
            // therefore cannot enqueue an older snapshot after a repaired attachment.
            lock(gate){history.PersistGeneratedImages(ImageStore);saved=store.SaveAsync(persona,history.Messages);}
            await saved.ConfigureAwait(false);
            if(!String.IsNullOrEmpty(store.Warning))throw new System.IO.IOException(TextResources.Get("conversationImageGeneration.persistImageHistoryAsync.text"));history.ReconcileImageUsage();
        }
        async Task PromoteImageTasksAsync(ConversationLog history,CancellationToken token)
        {
            if(ImageTasks==null)return;
            foreach(var message in history.Messages)if(message.ImageGenerationTaskIds!=null)foreach(var id in message.ImageGenerationTaskIds)
            {
                var job=ImageTasks.Get(id);if(job==null)continue; // Old terminal task logs may have aged out; asset references remain authoritative.
                var linked=(job.ReferenceImageIds??new System.Collections.Generic.List<string>()).Concat(String.IsNullOrEmpty(job.ResultImageId)?new string[0]:new[]{job.ResultImageId});
                if(!job.Persistent||linked.Any(assetId=>ImageStore==null||ImageStore.Get(assetId)==null||ImageStore.Get(assetId).Temporary))await ImageTasks.PromoteAsync(id,token).ConfigureAwait(false);
            }
        }
        async Task DeliverImageAsync(string id,ConversationLog history,string persona,CancellationToken token,Operation owner=null)
        {
            var job=ImageTasks.Get(id);var image=job==null?null:ImageStore.Get(job.ResultImageId);
            if(image==null)throw new InvalidOperationException(TextResources.Get("conversationImageGeneration.deliverImageAsync.text"));
            token.ThrowIfCancellationRequested();if(saveEnabled()){await ImageTasks.PromoteAsync(id,token).ConfigureAwait(false);image=ImageStore.Get(image.Id);}
            ChatMessage updated;lock(gate){updated=disposed||token.IsCancellationRequested||(owner!=null&&(active!=owner||owner.Ended!=0))?null:history.AttachGeneratedImage(job.Origin.MessageId,image);}
            if(updated==null){await MarkImageDeliveryAsync(id,ImageTaskDeliveryState.Detached).ConfigureAwait(false);return;}
            await PersistImageHistoryAsync(persona,history,token).ConfigureAwait(false);
            await MarkImageDeliveryAsync(id,ImageTaskDeliveryState.Delivered).ConfigureAwait(false);
            lock(gate)if(!disposed&&persona==personaId&&MessageUpdated!=null)MessageUpdated(updated);
        }
        public Task RepairImageDeliveryAsync(string taskId,CancellationToken token)
        {
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("ConversationSession");
                var admission=Maintenance.EnterActivity();
                var repair=imageRepairTail.ContinueWith(async _=>{try{await RepairImageDeliveryCoreAsync(taskId,token).ConfigureAwait(false);}finally{admission.Dispose();}},TaskScheduler.Default).Unwrap();
                imageRepairTail=repair.ContinueWith(completed=>{var observed=completed.Exception;},TaskScheduler.Default);
                return repair;
            }
        }
        async Task RepairImageDeliveryCoreAsync(string taskId,CancellationToken token)
        {
            if(ImageTasks==null)throw new InvalidOperationException(TextResources.Get("conversationImageGeneration.repairImageDeliveryCoreAsync.text"));var job=ImageTasks.Get(taskId);
            if(job==null||job.State!=ImageGenerationTaskState.Succeeded||job.Origin==null||job.Source!=ImageGenerationSource.Conversation)throw new InvalidOperationException(TextResources.Get("conversationImageGeneration.repairImageDeliveryCoreAsync.text.alternativeA"));
            if(!String.Equals(job.Origin.ConversationId,ImageConversationId,StringComparison.Ordinal))throw new InvalidOperationException(TextResources.Get("conversationImageGeneration.repairImageDeliveryCoreAsync.text.alternativeB"));
            try
            {
                var history=GetHistory(job.Origin.PersonaId??"");
                if(store!=null&&store.IsUnreadable(job.Origin.PersonaId??""))throw new System.IO.IOException(TextResources.Get("conversationImageGeneration.repairImageDeliveryCoreAsync.text.alternativeC"));
                await DeliverImageAsync(taskId,history,job.Origin.PersonaId??"",token).ConfigureAwait(false);
            }
            catch(OperationCanceledException){throw;}
            catch{MarkImageDeliveryAsync(taskId,ImageTaskDeliveryState.Failed).GetAwaiter().GetResult();throw;}
        }
    }
}
