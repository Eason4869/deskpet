using System;
using System.Security.Cryptography;
using System.Text;
namespace Dafeiyu
{
    [System.Runtime.Serialization.DataContract]
    public sealed class VoiceRecommendations
    {
        [System.Runtime.Serialization.DataMember(Name="speech")] public string[] Speech {get;set;}
        [System.Runtime.Serialization.DataMember(Name="live")] public string[] Live {get;set;}
        [System.Runtime.Serialization.DataMember(Name="description")] public string Description {get;set;}
        [System.Runtime.Serialization.DataMember(Name="sample_text")] public string SampleText {get;set;}
    }
    public static class PersonaProfiles
    {
        public const string Dafeiyu="你是大肥鱼，一位温和、俏皮的蓝白鲸鱼娘桌面伙伴。用自然的中文与用户对话，帮助写代码、做设计和整理思路。回答清楚简洁，如实表达不确定性。你不能直接看见用户桌面，也不会自动读取文件。";
        public const string Ebao="你是E包，一位傲娇、毒舌的白发正太桌面伙伴。用自然的中文与用户对话，帮助写代码、做设计和整理思路。回答清楚简洁，如实表达不确定性。你不能直接看见用户桌面，也不会自动读取文件。";
        public const string Marfu="你是马尔芙，一位金发绿眼的魔法少女桌面伙伴，角色背景是《哈利波特》中德拉科·马尔福的女性版本（性转马尔福）。你聪明、自信、俏皮，带一点马尔福式的骄傲和嘴硬，也愿意耐心陪伴用户。用自然的中文与用户聊天，帮助写代码、做设计和整理思路。可以偶尔用简短的魔法比喻，但不要让角色扮演妨碍清楚实用的回答。不挖苦用户，不编造能力，遇到不确定的信息如实说明。你不能直接看见用户桌面，也不会自动读取文件。";
        public static string Generic(string name) { return "你是"+name+"，一位温和、俏皮的桌面伙伴。用自然的中文与用户对话，帮助写代码、做设计和整理思路。回答清楚简洁，如实表达不确定性。你不能直接看见用户桌面，也不会自动读取文件。"; }
#if !PORTABLE
        public static void Activate(PetSettings settings,SkinDefinition skin) { Activate(settings,skin.Id,skin.SystemPrompt,skin.Manifest==null?null:skin.Manifest.VoicePresets); }
        public static void Save(PetSettings settings,SkinDefinition skin,string prompt) { Save(settings,skin.Id,skin.SystemPrompt,prompt,skin.Manifest==null?null:skin.Manifest.VoicePresets); }
        public static void Reset(PetSettings settings,SkinDefinition skin) { Reset(settings,skin.Id,skin.SystemPrompt,skin.Manifest==null?null:skin.Manifest.VoicePresets); }
#endif
        public static void Activate(PetSettings settings,string skinId,string defaultPrompt,VoiceRecommendations voices=null)
        {
            settings.Normalize();
            SaveVoiceProfile(settings,settings.VoiceProfileVersion==0);
            settings.VoiceProfileVersion=1;
            if(settings.PersonaProfileVersion==0)
            {
                string old=settings.SystemPrompt ?? "";
                if(!String.IsNullOrWhiteSpace(old) && old!=Generic("{pet_name}"))
                {
                    string owner=old.Contains("鲸鱼") || old.Contains("大肥鱼") ? "dafeiyu" : settings.SkinId;
                    if(!settings.PersonaOverrides.ContainsKey(owner)) settings.PersonaOverrides[owner]=old;
                }
                settings.PersonaProfileVersion=1;
            }
            string prompt; settings.SystemPrompt=settings.PersonaOverrides.TryGetValue(skinId,out prompt) ? prompt : defaultPrompt;
            settings.SkinId=skinId;
            settings.ActiveVoiceRecommendations=voices;
            settings.VoiceName=VoiceFor(settings,skinId,"speech",settings.SpeechConnection());
            settings.LiveVoice=VoiceFor(settings,skinId,"live",settings.Live);
        }
        public static void Save(PetSettings settings,string skinId,string defaultPrompt,string prompt,VoiceRecommendations voices=null)
        {
            prompt=(prompt ?? "").Trim(); if(prompt.Length==0 || prompt.Length>8000) throw new InvalidOperationException(TextResources.Get("personaProfiles.save.text"));
            Activate(settings,skinId,defaultPrompt,voices); settings.PersonaOverrides[skinId]=prompt; settings.SystemPrompt=prompt;
        }
        public static void Reset(PetSettings settings,string skinId,string defaultPrompt,VoiceRecommendations voices=null) { Activate(settings,skinId,defaultPrompt,voices); settings.PersonaOverrides.Remove(skinId); settings.SystemPrompt=defaultPrompt; }
        public static void SaveVoices(PetSettings settings)
        {
            settings.Normalize();SaveVoiceProfile(settings,false);settings.VoiceProfileVersion=1;
        }
        static void SaveVoiceProfile(PetSettings settings,bool migrating)
        {
            PersonaVoiceProfile profile;
            if(!settings.VoiceProfiles.TryGetValue(settings.SkinId,out profile)) {
                if(settings.VoiceProfiles.Count>=32) {string remove=null;foreach(string key in settings.VoiceProfiles.Keys){remove=key;break;}if(remove!=null)settings.VoiceProfiles.Remove(remove);}
                settings.VoiceProfiles[settings.SkinId]=profile=new PersonaVoiceProfile();
            }
            string speechKey=VoiceContext("speech",settings.SpeechConnection()),liveKey=VoiceContext("live",settings.Live);
            if(!migrating || !profile.Speech.ContainsKey(speechKey))StoreVoice(profile.Speech,speechKey,settings.VoiceName.Length==0 ? RecommendedVoice(settings.SkinId,"speech",settings.SpeechConnection(),settings.ActiveVoiceRecommendations) : settings.VoiceName);
            if(!migrating || !profile.Live.ContainsKey(liveKey))StoreVoice(profile.Live,liveKey,settings.LiveVoice);
        }
        static void StoreVoice(System.Collections.Generic.Dictionary<string,string> choices,string key,string value)
        {
            if(!choices.ContainsKey(key) && choices.Count>=8) {string remove=null;foreach(string item in choices.Keys){remove=item;break;}if(remove!=null)choices.Remove(remove);}
            if(value.Length<=512)choices[key]=value;
        }
        public static void SaveVoiceChoice(PetSettings settings,string kind,string context,string voice)
        {
            PersonaVoiceProfile profile;if(!settings.VoiceProfiles.TryGetValue(settings.SkinId,out profile))settings.VoiceProfiles[settings.SkinId]=profile=new PersonaVoiceProfile();
            StoreVoice(kind=="live"?profile.Live:profile.Speech,context,voice.Trim());
        }
        public static string VoiceFor(PetSettings settings,string skinId,string kind,ConnectionSettings connection)
        {
            PersonaVoiceProfile profile;string value;
            if(settings.VoiceProfiles!=null && settings.VoiceProfiles.TryGetValue(skinId,out profile) && profile!=null) {
                var choices=kind=="live"?profile.Live:profile.Speech;
                if(choices!=null && choices.TryGetValue(VoiceContext(kind,connection),out value))return value;
            }
            return RecommendedVoice(skinId,kind,connection,settings.SkinId==skinId?settings.ActiveVoiceRecommendations:null);
        }
        public static string VoiceContext(string kind,ConnectionSettings connection)
        {
            connection=connection??new ConnectionSettings();Uri uri;string address=(connection.BaseUrl??"").Trim();
            if(Uri.TryCreate(address,UriKind.Absolute,out uri))address=uri.Scheme.ToLowerInvariant()+"://"+uri.Authority.ToLowerInvariant()+uri.AbsolutePath.TrimEnd('/');
            string protocol=(connection.Protocol??"auto").Trim().ToLowerInvariant();
            // Auto and OpenAI use the same model-inferred catalog; UI normalizes auto to OpenAI.
            if(protocol=="auto" || protocol=="openai" || protocol.Length==0)protocol="auto";
            string model=(connection.Model??"").Trim().ToLowerInvariant();if(model.StartsWith("models/"))model=model.Substring(7);
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(kind+"\n"+address+"\n"+model+"\n"+protocol))).Replace("-","");
        }
        public static string RecommendedVoice(string skinId,string kind,ConnectionSettings connection,VoiceRecommendations voices=null)
        {
            var choices=VoicePresets.For(kind,connection);
            var candidates=voices==null?null:kind=="live"?voices.Live:voices.Speech;
            if(candidates!=null)foreach(string id in candidates)foreach(var item in choices)if(item.Id==id)return id;
            string[] preferred=kind=="live" ? (skinId=="e-bao"?new[]{"Puck"}:skinId=="marfu"?new[]{"Kore"}:new[]{"Aoede"}) :
                skinId=="e-bao"?new[]{"苏打","ash"}:skinId=="marfu"?new[]{"茉莉","coral"}:new[]{"冰糖","nova"};
            foreach(string id in preferred)foreach(var item in choices)if(item.Id==id)return id;
            return choices.Count>0?choices[0].Id:"";
        }
    }
}
