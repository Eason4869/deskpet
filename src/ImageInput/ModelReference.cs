using System;
using System.Runtime.Serialization;
namespace Dafeiyu
{
    // Stable IDs only. Connections and encrypted credentials stay in TextProviders.
    [DataContract] public sealed class ModelReference
    {
        [DataMember] public string ProviderId="";
        [DataMember] public string ModelId="";
        public ModelReference Copy(){return new ModelReference{ProviderId=ProviderId,ModelId=ModelId};}
        public void Normalize(){ProviderId=(ProviderId??"").Trim();ModelId=(ModelId??"").Trim();if(ProviderId.Length>128)ProviderId="";if(ModelId.Length>256)ModelId="";}
    }
    [DataContract] public sealed class ImageDescriptionOverride
    {
        [DataMember] public string Mode="inherit";
        [DataMember] public ModelReference Model;
        public ImageDescriptionOverride Copy(){return new ImageDescriptionOverride{Mode=Mode,Model=Model==null?null:Model.Copy()};}
        public void Normalize(){Mode=(Mode??"").Trim().ToLowerInvariant();if(Mode!="selected"&&Mode!="disabled")Mode="inherit";if(Mode!="selected")Model=null;else if(Model!=null)Model.Normalize();}
    }
}
