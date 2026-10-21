using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        public GeneratedImageStore ImageStore {get;set;}
        public ImageGenerationClient ImageClient {get;set;}
        public ImageGenerationTaskManager ImageTasks {get;set;}
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
                var submission=new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),request,new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId=kernelSession==null?op.PersonaId:kernelSession.ConversationId,KernelSessionId=request.SessionId,PersonaId=op.PersonaId,AgentTaskId=request.TaskId,ToolCallId=callId,MessageId=originReplyId,GroupId=originReplyId,Sequence=(op.History.Messages.First(m=>m.Id==originReplyId).ImageGenerationTaskIds??new System.Collections.Generic.List<string>()).Count});
                submission.BeforeStart=async id=>{jobId=id;cancel.ThrowIfCancellationRequested();lock(gate){if(disposed||active!=op||op.Ended!=0)throw new OperationCanceledException(cancel);if(op.History.AttachImageTask(originReplyId,id)==null)throw new InvalidOperationException("原消息已删除。");}await PersistImageHistoryAsync(op.PersonaId,op.History,cancel).ConfigureAwait(false);};
                try{var handle=await ImageTasks.EnqueueAsync(submission,cancel).ConfigureAwait(false);jobId=handle.Id;image=await handle.Completion.ConfigureAwait(false);}
                catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;return "错误：这张图片的生成已取消，可以继续对话；不会自动重试。";}
                catch(InvalidOperationException error){return "错误："+error.Message+" 不会自动重试；请处理服务配置后再发起新请求。";}
                catch(System.IO.InvalidDataException){return "错误：图片服务返回的图片或响应格式无效，未标记生成完成；不会自动重试。";}
                catch(TimeoutException error){throw new ApiException(error.Message+" 服务可能仍在生成，请核对后再发起新请求。不会自动重试。");}
                catch(System.IO.IOException error){throw new ApiException(error.Message+" 图片服务可能已接收请求，请核对后再操作。不会自动重试。");}
                if(cancel.IsCancellationRequested||disposed||active!=op||op.Ended!=0){await MarkImageDeliveryAsync(jobId,ImageTaskDeliveryState.Detached).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();return "图片已进入图库，原轮次已结束。";}
                try{await DeliverImageAsync(jobId,op.History,op.PersonaId,cancel,op).ConfigureAwait(false);}
                catch(OperationCanceledException){throw;}
                catch(Exception){MarkImageDeliveryAsync(jobId,ImageTaskDeliveryState.Failed).GetAwaiter().GetResult();return new JavaScriptSerializer().Serialize(new{asset_id=image.Id,status="complete",delivery_status="failed",message="图片已保存到图库，但原消息同步失败；可重试同步，无需再次生成。"});}
                return new JavaScriptSerializer().Serialize(new{asset_id=image.Id,status="complete",delivery_status=ImageTasks.Get(jobId).DeliveryState.ToString(),message="图片已保存；可在原会话或图库查看。",model=image.Model,width=image.Width,height=image.Height});
                }
                finally{op.History.RetainImageReply(originReplyId,false);if(op.ReplyId!=originReplyId)op.History.RemoveEmptyReply(originReplyId);}
            };
        }
        // Delivery journaling must never turn a committed image into an ambiguous generation failure.
        async Task MarkImageDeliveryAsync(string id,ImageTaskDeliveryState state)
        {
            try{await ImageTasks.SetDeliveryStateAsync(id,state).ConfigureAwait(false);}
            catch(System.IO.IOException){imageHistoryWarning="图片已保存，但同步状态暂未写入，请检查磁盘后重试同步。";}
            catch(UnauthorizedAccessException){imageHistoryWarning="图片已保存，但同步状态暂未写入，请检查权限后重试同步。";}
        }
        async Task PersistImageHistoryAsync(string persona,ConversationLog history,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();if(store==null||!saveEnabled())return;
            await PromoteImageTasksAsync(history,token).ConfigureAwait(false);history.PersistGeneratedImages(ImageStore);
            await store.SaveAsync(persona,history.Messages).ConfigureAwait(false);
            if(!String.IsNullOrEmpty(store.Warning))throw new System.IO.IOException("聊天记录保存失败。");
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
            if(image==null)throw new InvalidOperationException("已保存的图片不可用。");
            token.ThrowIfCancellationRequested();if(saveEnabled()){await ImageTasks.PromoteAsync(id,token).ConfigureAwait(false);image=ImageStore.Get(image.Id);}
            ChatMessage updated;lock(gate){updated=disposed||token.IsCancellationRequested||(owner!=null&&(active!=owner||owner.Ended!=0))?null:history.AttachGeneratedImage(job.Origin.MessageId,image);}
            if(updated==null){await MarkImageDeliveryAsync(id,ImageTaskDeliveryState.Detached).ConfigureAwait(false);return;}
            await PersistImageHistoryAsync(persona,history,token).ConfigureAwait(false);
            await MarkImageDeliveryAsync(id,ImageTaskDeliveryState.Delivered).ConfigureAwait(false);
            lock(gate)if(!disposed&&persona==personaId&&MessageUpdated!=null)MessageUpdated(updated);
        }
        public async Task RepairImageDeliveryAsync(string taskId,CancellationToken token)
        {
            if(ImageTasks==null)throw new InvalidOperationException("图片任务服务不可用。");var job=ImageTasks.Get(taskId);
            if(job==null||job.State!=ImageGenerationTaskState.Succeeded||job.Origin==null||job.Source!=ImageGenerationSource.Conversation)throw new InvalidOperationException("没有可同步的图片结果。");
            if(kernelSession!=null&&job.Origin.ConversationId!=kernelSession.ConversationId)throw new InvalidOperationException("请在原会话中重试同步。");
            try{await DeliverImageAsync(taskId,GetHistory(job.Origin.PersonaId??""),job.Origin.PersonaId??"",token).ConfigureAwait(false);}
            catch(OperationCanceledException){throw;}
            catch{MarkImageDeliveryAsync(taskId,ImageTaskDeliveryState.Failed).GetAwaiter().GetResult();throw;}
        }
    }
}
