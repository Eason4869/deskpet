using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        public GeneratedImageStore ImageStore {get;set;}
        public ImageGenerationClient ImageClient {get;set;}
        public IAgentQuestionProvider QuestionProvider {get;set;}

        void ConfigureInteractiveTools(Operation op)
        {
            op.Workspace.QuestionProvider=QuestionProvider;
            var provider=op.Snapshot.SelectedImageProvider();
            if(provider==null||String.IsNullOrWhiteSpace(provider.Connection.BaseUrl)||String.IsNullOrWhiteSpace(provider.Connection.Model)||ImageClient==null||ImageStore==null)return;
            op.Workspace.ImageGenerator=async (prompt,callId,cancel)=>
            {
                string originReplyId=op.ReplyId;op.History.RetainImageReply(originReplyId,true);
                try
                {
                var request=new ImageGenerationRequest{Provider=provider.Copy(),Prompt=prompt,Size=provider.Size,Quality=provider.Quality,
                    SessionId=kernelSession==null?op.PersonaId:kernelSession.SessionId,TaskId=op.Workspace.TaskState==null?"":op.Workspace.TaskState.Id,
                    MessageId=originReplyId,Persistent=saveEnabled()};
                GeneratedImage image;
                try{image=await ImageClient.GenerateAsync(request,cancel).ConfigureAwait(false);}
                catch(InvalidOperationException error){return "错误："+error.Message+" 不会自动重试；请处理服务配置后再发起新请求。";}
                catch(System.IO.InvalidDataException){return "错误：图片服务返回的图片或响应格式无效，未标记生成完成；不会自动重试。";}
                catch(TimeoutException error){throw new ApiException(error.Message+" 服务可能仍在生成，请核对后再发起新请求。不会自动重试。");}
                catch(System.IO.IOException error){throw new ApiException(error.Message+" 图片服务可能已接收请求，请核对后再操作。不会自动重试。");}
                cancel.ThrowIfCancellationRequested();ChatMessage updated;
                if(saveEnabled()&&image.Temporary)image=ImageStore.Promote(image.Id);
                lock(gate){if(disposed||active!=op||op.Ended!=0)throw new OperationCanceledException(cancel);updated=op.History.AttachGeneratedImage(originReplyId,image);}
                if(updated==null)throw new ApiException("图片已进入图库，但原消息已删除。");
                if(store!=null&&saveEnabled())
                {
                    await store.SaveAsync(op.PersonaId,op.History.Messages).ConfigureAwait(false);
                    if(!String.IsNullOrEmpty(store.Warning))throw new ApiException("图片已保存到图库，但聊天记录保存失败；请检查磁盘空间与权限。");
                }
                lock(gate)if(!disposed&&op.PersonaId==personaId&&MessageUpdated!=null)MessageUpdated(updated);
                return new JavaScriptSerializer().Serialize(new{asset_id=image.Id,status="complete",message="图片已在原会话中展示，并加入图库。",model=image.Model,width=image.Width,height=image.Height});
                }
                finally{op.History.RetainImageReply(originReplyId,false);if(op.ReplyId!=originReplyId)op.History.RemoveEmptyReply(originReplyId);}
            };
        }
    }
}
