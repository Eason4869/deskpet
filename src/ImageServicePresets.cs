using System;
using System.Collections.Generic;
using System.Linq;

namespace Dafeiyu
{
    public sealed class ImageServicePreset
    {
        public string Id {get;set;}
        public string Name {get;set;}
        public string BaseUrl {get;set;}
        public string Model {get;set;}
        public string Protocol {get;set;}
        public bool SupportsEdits {get;set;}
        public string Notes {get;set;}
        public string DocsUrl {get;set;}
        public ImageGenerationProvider CreateProvider(){return new ImageGenerationProvider{Name=Name,Protocol=Protocol,SupportsEdits=SupportsEdits,Connection=new ConnectionSettings{BaseUrl=BaseUrl,Model=Model}};}
        public override string ToString(){return Name;}
    }
    public static class ImageServicePresets
    {
        public static string[] Protocols {get{return new[]{"openai","gemini","doubao","minimax","qwen","grok"};}}
        // Return fresh instances so editing a preset-backed configuration cannot mutate defaults.
        public static IList<ImageServicePreset> All {get{return new[]{
            new ImageServicePreset{Id="openai",Name="OpenAI Images",BaseUrl="https://api.openai.com/v1",Model="gpt-image-2.5-sunburst",Protocol="openai",SupportsEdits=true,Notes=TextResources.Get("imageServicePresets.all.notes"),DocsUrl="https://developers.openai.com/api/reference/resources/images/methods/generate"},
            new ImageServicePreset{Id="gemini",Name="Google Gemini",BaseUrl="https://generativelanguage.googleapis.com/v1beta",Model="gemini-3.1-flash-image",Protocol="gemini",SupportsEdits=true,Notes=TextResources.Get("imageServicePresets.all.notes.alternativeA"),DocsUrl="https://ai.google.dev/gemini-api/docs/generate-content/image-generation"},
            new ImageServicePreset{Id="doubao",Name="豆包 · Seedream",BaseUrl="https://ark.cn-beijing.volces.com/api/v3",Model="doubao-seedream-4-5-251128",Protocol="doubao",SupportsEdits=true,Notes=TextResources.Get("imageServicePresets.all.notes.alternativeB"),DocsUrl="https://docs.volcengine.com/docs/ark/image-generation-api?lang=zh"},
            new ImageServicePreset{Id="minimax",Name="MiniMax",BaseUrl="https://api.minimax.cn/v1",Model="image-01",Protocol="minimax",SupportsEdits=true,Notes=TextResources.Get("imageServicePresets.all.notes.alternativeC"),DocsUrl="https://platform.minimax.cn/docs/api-reference/image-generation-i2i"},
            new ImageServicePreset{Id="qwen",Name="通义千问 · Qwen Image",BaseUrl="https://dashscope.aliyuncs.com/compatible-mode/v1",Model="qwen-image-3.0",Protocol="qwen",SupportsEdits=true,Notes=TextResources.Get("imageServicePresets.all.notes.alternativeD"),DocsUrl="https://www.alibabacloud.com/help/en/model-studio/qwen-image-generation-and-editing-api-reference"},
            new ImageServicePreset{Id="grok",Name="xAI Grok Imagine",BaseUrl="https://api.x.ai/v1",Model="grok-imagine-image-2.0",Protocol="grok",SupportsEdits=true,Notes=TextResources.Get("imageServicePresets.all.notes.alternativeE"),DocsUrl="https://docs.x.ai/developers/model-capabilities/images/generation"},
            new ImageServicePreset{Id="custom",Name=TextResources.Get("imageServicePresets.all.name"),BaseUrl="",Model="",Protocol="openai",SupportsEdits=false,Notes=TextResources.Get("imageServicePresets.all.notes.alternativeF"),DocsUrl=""}
        };}}
        public static string[] Sizes(string protocol){return ImageGenerationProtocols.SizeOptions(protocol).Where(s=>!String.IsNullOrEmpty(s)).ToArray();}
        public static string[] Qualities(string protocol){return ImageGenerationProtocols.QualityOptions(protocol).Where(s=>!String.IsNullOrEmpty(s)).ToArray();}
    }
}
