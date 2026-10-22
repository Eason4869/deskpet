using System;
using System.Runtime.Serialization;

namespace Dafeiyu
{
    [DataContract]
    public sealed class ImageGenerationProvider
    {
        [DataMember] public string Id {get;set;}
        [DataMember] public string Name {get;set;}
        [DataMember] public ConnectionSettings Connection {get;set;}
        [DataMember] public int TimeoutSeconds {get;set;}
        [DataMember] public string Size {get;set;}
        [DataMember] public string Quality {get;set;}
        [DataMember] public bool SupportsEdits {get;set;}
        [DataMember] public string Protocol {get;set;}
        public ImageGenerationProvider(){Defaults();}
        [OnDeserializing] void BeforeDeserialize(StreamingContext context){Defaults();}
        void Defaults(){Id=Guid.NewGuid().ToString("N");Name="图片服务";Connection=new ConnectionSettings();TimeoutSeconds=300;Size="";Quality="";Protocol="openai";}
        public void Normalize()
        {
            if(String.IsNullOrWhiteSpace(Id) || Id.Length>80)Id=Guid.NewGuid().ToString("N");
            Name=String.IsNullOrWhiteSpace(Name)?"图片服务":Name.Trim();if(Name.Length>128)Name=Name.Substring(0,128);
            Connection=Connection??new ConnectionSettings();Connection.Normalize();
            TimeoutSeconds=TimeoutSeconds<=0?300:Math.Min(3600,TimeoutSeconds);
            Size=(Size??"").Trim();Quality=(Quality??"").Trim();
            Protocol=String.IsNullOrWhiteSpace(Protocol)?"openai":Protocol.Trim().ToLowerInvariant();
            if(Size.Length>64)Size="";if(Quality.Length>64)Quality="";
        }
        public ImageGenerationProvider Copy(){return new ImageGenerationProvider{Id=Id,Name=Name,Connection=(Connection??new ConnectionSettings()).Copy(),TimeoutSeconds=TimeoutSeconds,Size=Size,Quality=Quality,SupportsEdits=SupportsEdits,Protocol=Protocol};}
        public override string ToString(){return Name;}
    }
}
