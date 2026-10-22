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
            new ImageServicePreset{Id="openai",Name="OpenAI Images",BaseUrl="https://api.openai.com/v1",Model="gpt-image-2.5-sunburst",Protocol="openai",SupportsEdits=true,Notes="OpenAI Images；生成使用 JSON，参考图编辑使用 multipart。模型名称可自定义。",DocsUrl="https://developers.openai.com/api/reference/resources/images/methods/generate"},
            new ImageServicePreset{Id="gemini",Name="Google Gemini",BaseUrl="https://generativelanguage.googleapis.com/v1beta",Model="gemini-3.1-flash-image",Protocol="gemini",SupportsEdits=true,Notes="Gemini 原生 generateContent；尺寸可选宽高比或分辨率档位；质量请用服务默认。",DocsUrl="https://ai.google.dev/gemini-api/docs/generate-content/image-generation"},
            new ImageServicePreset{Id="doubao",Name="豆包 · Seedream",BaseUrl="https://ark.cn-beijing.volces.com/api/v3",Model="doubao-seedream-4-5-251128",Protocol="doubao",SupportsEdits=true,Notes="火山方舟 Seedream；参考图通过 JSON image 发送。4.5 支持 2K、4K，尺寸限制随模型变化。",DocsUrl="https://docs.volcengine.com/docs/ark/image-generation-api?lang=zh"},
            new ImageServicePreset{Id="minimax",Name="MiniMax",BaseUrl="https://api.minimax.cn/v1",Model="image-01",Protocol="minimax",SupportsEdits=true,Notes="MiniMax 原生 image_generation；参考图仅用作人物主体参考，不提供通用图片编辑保证；提示词最多 1500 字。",DocsUrl="https://platform.minimax.cn/docs/api-reference/image-generation-i2i"},
            new ImageServicePreset{Id="qwen",Name="通义千问 · Qwen Image",BaseUrl="https://dashscope.aliyuncs.com/compatible-mode/v1",Model="qwen-image-3.0",Protocol="qwen",SupportsEdits=true,Notes="百炼同步 Images 兼容接口；参考图通过同一生成接口的 JSON image 扩展发送。密钥和模型须属于同一区域；可换为工作空间专属域名。",DocsUrl="https://www.alibabacloud.com/help/en/model-studio/qwen-image-generation-and-editing-api-reference"},
            new ImageServicePreset{Id="grok",Name="xAI Grok Imagine",BaseUrl="https://api.x.ai/v1",Model="grok-imagine-image-2.0",Protocol="grok",SupportsEdits=true,Notes="xAI 原生 Images；参考图编辑为 JSON；尺寸表示宽高比，质量支持 low、medium、auto。",DocsUrl="https://docs.x.ai/developers/model-capabilities/images/generation"},
            new ImageServicePreset{Id="custom",Name="自定义兼容服务",BaseUrl="",Model="",Protocol="openai",SupportsEdits=false,Notes="填写服务地址和模型，按供应商文档选择协议；服务支持编辑时再启用参考图。",DocsUrl=""}
        };}}
        public static string[] Sizes(string protocol){return ImageGenerationProtocols.SizeOptions(protocol).Where(s=>!String.IsNullOrEmpty(s)).ToArray();}
        public static string[] Qualities(string protocol){return ImageGenerationProtocols.QualityOptions(protocol).Where(s=>!String.IsNullOrEmpty(s)).ToArray();}
    }
}
