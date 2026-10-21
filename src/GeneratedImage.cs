using System;
using System.Runtime.Serialization;

namespace Dafeiyu
{
    [DataContract]
    public sealed class GeneratedImage
    {
        [DataMember] public string Id {get;set;}
        [DataMember] public string Prompt {get;set;}
        [DataMember] public string ProviderId {get;set;}
        [DataMember] public string Model {get;set;}
        [DataMember] public string Size {get;set;}
        [DataMember] public string Quality {get;set;}
        [DataMember] public DateTime CreatedUtc {get;set;}
        [DataMember] public string SessionId {get;set;}
        [DataMember] public string TaskId {get;set;}
        [DataMember] public string MessageId {get;set;}
        [DataMember] public string MediaType {get;set;}
        [DataMember] public int Width {get;set;}
        [DataMember] public int Height {get;set;}
        [DataMember] public long Bytes {get;set;}
        [DataMember] public string ReferenceImageId {get;set;}
        [DataMember] public bool Temporary {get;set;}
        [DataMember] public bool IsReference {get;set;}
        public GeneratedImage Copy(){return (GeneratedImage)MemberwiseClone();}
    }
    public sealed class ImageGenerationRequest
    {
        public ImageGenerationProvider Provider {get;set;}
        public string Prompt {get;set;}
        public string Size {get;set;}
        public string Quality {get;set;}
        public byte[] ReferenceImageBytes {get;set;}
        public string ReferenceMediaType {get;set;}
        public string ReferenceImageId {get;set;}
        public string SessionId {get;set;}
        public string TaskId {get;set;}
        public string MessageId {get;set;}
        public bool Persistent {get;set;}
        public ImageGenerationRequest(){Persistent=true;}
    }
}
