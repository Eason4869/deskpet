using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public enum ImageGenerationSource { Unknown, Workspace, Conversation, SettingsTest }
    public enum ImageGenerationTaskState { Queued, Generating, Downloading, Saving, Succeeded, Failed, Canceled, Interrupted }
    public enum ImageTaskDeliveryState { NotRequired, Pending, Delivered, Failed, Detached }
    public enum ImageGenerationPhase { Generating, Downloading, Saving }

    [DataContract]
    public sealed class ImageTaskOrigin
    {
        [DataMember] public ImageGenerationSource Source {get;set;}
        [DataMember] public string ConversationId {get;set;}
        [DataMember] public string KernelSessionId {get;set;}
        [DataMember] public string PersonaId {get;set;}
        [DataMember] public string AgentTaskId {get;set;}
        [DataMember] public string ToolCallId {get;set;}
        [DataMember] public string MessageId {get;set;}
        [DataMember] public string GroupId {get;set;}
        [DataMember] public int Sequence {get;set;}
        public ImageTaskOrigin Copy(){return (ImageTaskOrigin)MemberwiseClone();}
    }

    // The only durable task schema. It deliberately has no provider connection,
    // request object, image bytes, reference paths or endpoint URL.
    [DataContract]
    public class ImageGenerationTaskRecord
    {
        [DataMember] public string Id {get;set;}
        [DataMember] public string RequestId {get;set;}
        [DataMember] public string RetryOfTaskId {get;set;}
        [DataMember] public bool Persistent {get;set;}
        [DataMember] public ImageTaskOrigin Origin {get;set;}
        [DataMember] public ImageGenerationTaskState State {get;set;}
        [DataMember] public DateTime QueuedUtc {get;set;}
        [DataMember] public DateTime? StartedUtc {get;set;}
        [DataMember] public DateTime? CompletedUtc {get;set;}
        [DataMember] public DateTime? UpdatedUtc {get;set;}
        [DataMember] public string ProviderId {get;set;}
        [DataMember] public string ProviderName {get;set;}
        [DataMember] public string Protocol {get;set;}
        [DataMember] public string Model {get;set;}
        [DataMember] public string Prompt {get;set;}
        [DataMember] public string Size {get;set;}
        [DataMember] public string Quality {get;set;}
        [DataMember] public List<string> ReferenceImageIds {get;set;}
        [DataMember] public string ResultImageId {get;set;}
        [DataMember] public string Error {get;set;}
        [DataMember] public ImageTaskDeliveryState DeliveryState {get;set;}
        [DataMember] public string DeliveryError {get;set;}
        public ImageGenerationTaskRecord(){Persistent=true;}
        [OnDeserializing] void BeforeDeserialize(StreamingContext context){Persistent=true;}
        public ImageGenerationSource Source {get{return Origin==null?ImageGenerationSource.Unknown:Origin.Source;}set{if(Origin==null)Origin=new ImageTaskOrigin();Origin.Source=value;}}
        public bool IsTerminal {get{return State==ImageGenerationTaskState.Succeeded || State==ImageGenerationTaskState.Failed || State==ImageGenerationTaskState.Canceled || State==ImageGenerationTaskState.Interrupted;}}
        public ImageGenerationTaskRecord Copy()
        {
            return new ImageGenerationTaskRecord{Id=Id,RequestId=RequestId,RetryOfTaskId=RetryOfTaskId,Persistent=Persistent,Origin=Origin==null?null:Origin.Copy(),State=State,QueuedUtc=QueuedUtc,StartedUtc=StartedUtc,CompletedUtc=CompletedUtc,UpdatedUtc=UpdatedUtc,
                ProviderId=ProviderId,ProviderName=ProviderName,Protocol=Protocol,Model=Model,Prompt=Prompt,Size=Size,Quality=Quality,ReferenceImageIds=ReferenceImageIds==null?null:new List<string>(ReferenceImageIds),ResultImageId=ResultImageId,Error=Error,DeliveryState=DeliveryState,DeliveryError=DeliveryError};
        }
    }

    // A consumer can edit its snapshot without changing the manager's record.
    public sealed class ImageGenerationTaskSnapshot : ImageGenerationTaskRecord
    {
        public ImageGenerationTaskSnapshot(ImageGenerationTaskRecord record)
        {
            if(record==null)throw new ArgumentNullException("record");
            Id=record.Id;RequestId=record.RequestId;RetryOfTaskId=record.RetryOfTaskId;Persistent=record.Persistent;Origin=record.Origin==null?null:record.Origin.Copy();State=record.State;QueuedUtc=record.QueuedUtc;StartedUtc=record.StartedUtc;CompletedUtc=record.CompletedUtc;UpdatedUtc=record.UpdatedUtc;
            ProviderId=record.ProviderId;ProviderName=record.ProviderName;Protocol=record.Protocol;Model=record.Model;Prompt=record.Prompt;Size=record.Size;Quality=record.Quality;
            ReferenceImageIds=record.ReferenceImageIds==null?null:new List<string>(record.ReferenceImageIds);ResultImageId=record.ResultImageId;Error=record.Error;DeliveryState=record.DeliveryState;DeliveryError=record.DeliveryError;
        }
    }

    // This in-memory object is never serialized by the task store. Each access
    // returns a copy so the submitted connection and bytes cannot change later.
    public sealed class ImageGenerationTaskSubmission
    {
        readonly ImageGenerationRequest request;
        readonly ImageTaskOrigin origin;
        public string RequestId {get;private set;}
        public ImageGenerationRequest Request {get{return request.Copy();}}
        public ImageTaskOrigin Origin {get{return origin.Copy();}}
        public ImageGenerationTaskSubmission(string requestId,ImageGenerationRequest request,ImageTaskOrigin origin)
        {
            if(String.IsNullOrWhiteSpace(requestId))throw new ArgumentException("请指定提交 ID。","requestId");
            if(request==null)throw new ArgumentNullException("request");
            RequestId=requestId;this.request=request.Copy();this.origin=(origin??new ImageTaskOrigin()).Copy();
        }
    }
    public sealed class ImageGenerationTaskHandle
    {
        public string Id {get;private set;}
        public Task<GeneratedImage> Completion {get;private set;}
        public ImageGenerationTaskHandle(string id,Task<GeneratedImage> completion)
        {if(completion==null)throw new ArgumentNullException("completion");Id=id;Completion=completion;}
    }
}
