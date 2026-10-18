using System;
using System.Runtime.Serialization;
namespace Dafeiyu
{
    [DataContract] public enum CapabilitySupport { [EnumMember] Unknown, [EnumMember] Supported, [EnumMember] Unsupported }
    public sealed class AgentCapabilitySnapshot
    {
        public CapabilitySupport Tools {get;private set;}public CapabilitySupport Images {get;private set;}
        public bool SupportsTools {get;private set;}public bool SupportsImages {get;private set;}
        public int ContextWindowTokens {get;private set;}public string ContextSource {get;private set;}public string ToolSource {get;private set;}
        internal AgentCapabilitySnapshot(CapabilitySupport tools,CapabilitySupport images,bool useTools,bool useImages,int capacity,string contextSource,string toolSource)
        {Tools=tools;Images=images;SupportsTools=useTools;SupportsImages=useImages;ContextWindowTokens=capacity;ContextSource=contextSource;ToolSource=toolSource;}
        internal AgentModelCapabilities ToLegacy(){return new AgentModelCapabilities{SupportsTools=SupportsTools,SupportsImages=SupportsImages,SupportsStreaming=true,ContextWindow=ContextWindowTokens};}
    }
    public static class AgentCapabilityResolver
    {
        public static AgentCapabilitySnapshot Resolve(PetSettings settings,ProjectDefinition project)
        {
            var connection=settings.Text??new ConnectionSettings();var legacy=new AgentProviderRegistry().Resolve(ProviderIdentity.Brand(connection.BaseUrl)+" "+connection.Protocol,connection.Model);
            CapabilitySupport tools=Valid(connection.ToolSupport),images=Valid(connection.ImageSupport);string toolSource=tools==CapabilitySupport.Unknown?"legacy-unverified":"user";if(tools==CapabilitySupport.Unknown&&Valid(connection.MetadataToolSupport)!=CapabilitySupport.Unknown){tools=Valid(connection.MetadataToolSupport);toolSource="server-metadata";}if(images==CapabilitySupport.Unknown)images=Valid(connection.MetadataImageSupport);
            return new AgentCapabilitySnapshot(tools,images,tools==CapabilitySupport.Supported || tools==CapabilitySupport.Unknown&&legacy.SupportsTools,images==CapabilitySupport.Supported || images==CapabilitySupport.Unknown&&legacy.SupportsImages,ModelContextCapacity.Budget(connection,project==null?settings.AgentContextTokenLimit:project.ContextTokenLimit),connection.ContextWindowTokens>0?"configured":"fallback",toolSource);
        }
        internal static CapabilitySupport Valid(CapabilitySupport value){return Enum.IsDefined(typeof(CapabilitySupport),value)?value:CapabilitySupport.Unknown;}
    }
    [DataContract] public sealed class ModelCapabilitySetting
    {
        [DataMember] public CapabilitySupport Tools,Images;
        internal static ModelCapabilitySetting Read(System.Collections.Generic.Dictionary<string,object> item)
        {
            object nested;var caps=item.TryGetValue("capabilities",out nested)?nested as System.Collections.Generic.Dictionary<string,object>:null;
            return new ModelCapabilitySetting{Tools=Flag(item,caps,"supports_tools","tools","function_calling"),Images=Flag(item,caps,"supports_images","images","vision")};
        }
        static CapabilitySupport Flag(System.Collections.Generic.Dictionary<string,object> item,System.Collections.Generic.Dictionary<string,object> caps,params string[] names)
        {foreach(var map in new[]{item,caps})if(map!=null)foreach(var name in names){object value;if(map.TryGetValue(name,out value)&&value is bool)return (bool)value?CapabilitySupport.Supported:CapabilitySupport.Unsupported;}return CapabilitySupport.Unknown;}
        public ModelCapabilitySetting Copy(){return (ModelCapabilitySetting)MemberwiseClone();}
    }
}
