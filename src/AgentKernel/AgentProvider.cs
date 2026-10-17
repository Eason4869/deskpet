using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class AgentModelCapabilities
    {
        public string Provider="",Model="";public long ContextWindow=32768;public bool SupportsTools=true,SupportsStreaming=true,SupportsReasoning,SupportsImages,SupportsAudio;public bool CanUseTool(string tool){return SupportsTools && !String.IsNullOrWhiteSpace(tool);}
    }
    public sealed class AgentProviderRegistry
    {
        public AgentModelCapabilities Resolve(string provider,string model)
        {
            provider=(provider??"").ToLowerInvariant();model=model??"";string descriptor=provider+" "+model.ToLowerInvariant();
            var caps=new AgentModelCapabilities{Provider=provider,Model=model};
            if(descriptor.Contains("mimo")){caps.SupportsTools=true;caps.SupportsStreaming=true;caps.SupportsReasoning=true;caps.SupportsAudio=true;caps.ContextWindow=131072;}
            else if(descriptor.Contains("anthropic")||descriptor.Contains("claude")){caps.SupportsTools=true;caps.SupportsStreaming=true;caps.SupportsReasoning=true;caps.SupportsImages=true;caps.ContextWindow=200000;}
            else if(descriptor.Contains("openai")||descriptor.Contains("gpt")){caps.SupportsTools=true;caps.SupportsStreaming=true;caps.SupportsReasoning=true;caps.SupportsImages=true;caps.ContextWindow=128000;}
            else if(descriptor.Contains("gemini")){caps.SupportsTools=true;caps.SupportsStreaming=true;caps.SupportsImages=true;caps.SupportsAudio=true;caps.ContextWindow=1000000;}
            else{caps.SupportsTools=false;caps.SupportsStreaming=false;caps.ContextWindow=8192;}
            return caps;
        }

        public string Describe(string provider,string model){return new JavaScriptSerializer{MaxJsonLength=64*1024}.Serialize(Resolve(provider,model));}
    }
}
