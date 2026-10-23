using System;
using System.Collections.Generic;
namespace Dafeiyu
{
    public sealed class ServicePreset
    {
        public string Name,Url,Model,Protocol,Note;
        public ServicePreset(string name,string url,string model,string protocol="openai",string note=""){Name=name;Url=url;Model=model;Protocol=protocol;Note=note;}
        public override string ToString(){return Name;}
    }
    public static class ServicePresets
    {
        public static IList<ServicePreset> For(string kind)
        {
            var items=new List<ServicePreset>{new ServicePreset(TextResources.Get("servicePresets.for.listServicePresetServicePreset"),"","","auto",TextResources.Get("servicePresets.for.listServicePresetServicePreset.alternativeA"))};
            if(kind=="live") {items.Add(new ServicePreset("Gemini Live","wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent","gemini-3.8-live","gemini-live",TextResources.Get("servicePresets.for.items")));return items;}
            if(kind=="asr" || kind=="speech")
            {
                items.Add(new ServicePreset("MiMo","https://api.xiaomimimo.com/v1",kind=="asr" ? "mimo-v2.5-asr" : "mimo-v2.5-tts","mimo",TextResources.Get("servicePresets.for.items.alternativeA")));
                items.Add(new ServicePreset(TextResources.Get("servicePresets.for.items.alternativeB"),"https://token-plan-cn.xiaomimimo.com/v1",kind=="asr" ? "mimo-v2.5-asr" : "mimo-v2.5-tts","mimo",TextResources.Get("servicePresets.for.items.alternativeC")));
                items.Add(new ServicePreset("OpenAI","https://api.openai.com/v1",kind=="asr" ? "whisper-1" : "tts-1","openai",kind=="asr" ? TextResources.Get("servicePresets.for.items.alternativeD") : TextResources.Get("servicePresets.for.items.alternativeE")));return items;
            }
            items.Add(new ServicePreset("ChatGPT / OpenAI","https://api.openai.com/v1","gpt-4.1-mini"));
            items.Add(new ServicePreset("Gemini","https://generativelanguage.googleapis.com/v1beta/openai","gemini-3.8-flash"));
            items.Add(new ServicePreset("DeepSeek","https://api.deepseek.com","deepseek-flash"));
            items.Add(new ServicePreset("MiMo","https://api.xiaomimimo.com/v1","mimo-v2.6-flash"));
            items.Add(new ServicePreset(TextResources.Get("servicePresets.for.items.alternativeB"),"https://token-plan-cn.xiaomimimo.com/v1","mimo-v2.6-flash"));
            items.Add(new ServicePreset("火山方舟","https://ark.cn-beijing.volces.com/api/v3","","openai",TextResources.Get("servicePresets.for.items.alternativeF")));
            items.Add(new ServicePreset("Kimi","https://api.moonshot.cn/v1","kimi-k3"));
            items.Add(new ServicePreset("MiniMax","https://api.minimax.io/v1","MiniMax-M2.5"));
            items.Add(new ServicePreset("智谱","https://open.bigmodel.cn/api/paas/v4","","openai",TextResources.Get("servicePresets.for.items.alternativeG")));
            items.Add(new ServicePreset(TextResources.Get("servicePresets.for.items.alternativeH"),"https://dashscope.aliyuncs.com/compatible-mode/v1","qwen-plus"));
            items.Add(new ServicePreset("Claude / Anthropic","https://api.anthropic.com/v1","claude-sonnet-4-6","anthropic"));
            items.Add(new ServicePreset("DeepSeek · Anthropic","https://api.deepseek.com/anthropic/v1","deepseek-flash","anthropic"));return items;
        }
        public static bool SameOrigin(string before,string after)
        {Uri a,b;return Uri.TryCreate(before,UriKind.Absolute,out a) && Uri.TryCreate(after,UriKind.Absolute,out b) && a.Scheme==b.Scheme && String.Equals(a.Host,b.Host,StringComparison.OrdinalIgnoreCase) && a.Port==b.Port;}
    }
}
