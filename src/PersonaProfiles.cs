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
        public static string Dafeiyu { get { return TextResources.Get("personaProfiles.defaults.dafeiyu"); } }
        public static string Ebao { get { return TextResources.Get("personaProfiles.defaults.ebao"); } }
        public static string Marfu { get { return TextResources.Get("personaProfiles.defaults.marfu"); } }
        public static string Generic(string name) { return TextResources.Get("personaProfiles.defaults.generic").Replace("{pet_name}",name??""); }
#if !PORTABLE
        public static void Activate(PetSettings settings,SkinDefinition skin) { Activate(settings,skin.Id,skin.SystemPrompt,skin.Manifest==null?null:skin.Manifest.VoicePresets,skin.BuiltinCharacterId); }
        public static void Save(PetSettings settings,SkinDefinition skin,string prompt) { Save(settings,skin.Id,skin.SystemPrompt,prompt,skin.Manifest==null?null:skin.Manifest.VoicePresets,skin.BuiltinCharacterId); }
        public static void Reset(PetSettings settings,SkinDefinition skin) { Reset(settings,skin.Id,skin.SystemPrompt,skin.Manifest==null?null:skin.Manifest.VoicePresets,skin.BuiltinCharacterId); }
#endif
        static readonly string[] DefaultKeys={"personaProfiles.defaults.ebao","personaProfiles.defaults.dafeiyu","personaProfiles.defaults.marfu","personaProfiles.defaults.xiaobei","personaProfiles.defaults.gugugaga","personaProfiles.defaults.agent","personaProfiles.defaults.generic"};
        // Only exact known defaults/templates can be localized. A user edit is never inferred from keywords.
        static bool RecognizeDefault(string prompt,out string key,out string name)
        {
            key=null;name=null;if(prompt==null)return false;
            foreach(var choice in UiLanguages.Choices)foreach(string candidate in DefaultKeys)
            {
                string template=TextResources.CatalogFor(choice.Code).Get(candidate);
                if(prompt==template){key=candidate;return true;}
                if(candidate!="personaProfiles.defaults.generic")continue;
                const string marker="{pet_name}";int split=template.IndexOf(marker,StringComparison.Ordinal);if(split<0)continue;
                string prefix=template.Substring(0,split),suffix=template.Substring(split+marker.Length);
                if(prompt.Length>=prefix.Length+suffix.Length&&prompt.StartsWith(prefix,StringComparison.Ordinal)&&prompt.EndsWith(suffix,StringComparison.Ordinal))
                {key=candidate;name=prompt.Substring(prefix.Length,prompt.Length-prefix.Length-suffix.Length);return true;}
            }
            return false;
        }
        public static string LocalizeDefaultPrompt(string prompt)
        {
            string key,name;if(!RecognizeDefault(prompt,out key,out name))return prompt;
            string result=CurrentDefault(key);return name==null?result:result.Replace("{pet_name}",name);
        }
        static string CurrentDefault(string key)
        {
            switch(key)
            {
                case "personaProfiles.defaults.ebao":return TextResources.Get("personaProfiles.defaults.ebao");
                case "personaProfiles.defaults.dafeiyu":return TextResources.Get("personaProfiles.defaults.dafeiyu");
                case "personaProfiles.defaults.marfu":return TextResources.Get("personaProfiles.defaults.marfu");
                case "personaProfiles.defaults.xiaobei":return TextResources.Get("personaProfiles.defaults.xiaobei");
                case "personaProfiles.defaults.gugugaga":return TextResources.Get("personaProfiles.defaults.gugugaga");
                case "personaProfiles.defaults.agent":return TextResources.Get("personaProfiles.defaults.agent");
                case "personaProfiles.defaults.generic":return TextResources.Get("personaProfiles.defaults.generic");
                default:throw new ArgumentException("Unknown default persona key.","key");
            }
        }
        static string DefaultPrompt(string prompt,string officialId)
        {
            string key;
            switch(officialId){case "e-bao":key="personaProfiles.defaults.ebao";break;case "dafeiyu":key="personaProfiles.defaults.dafeiyu";break;case "marfu":key="personaProfiles.defaults.marfu";break;case "xiaobei":key="personaProfiles.defaults.xiaobei";break;case "gugugaga":key="personaProfiles.defaults.gugugaga";break;default:return LocalizeDefaultPrompt(prompt);}
            return CurrentDefault(key);
        }
        public static void Activate(PetSettings settings,string skinId,string defaultPrompt,VoiceRecommendations voices=null,string officialId=null)
        {
            settings.Normalize();
            SaveVoiceProfile(settings,settings.VoiceProfileVersion==0);
            settings.VoiceProfileVersion=1;
            if(settings.PersonaProfileVersion==0)
            {
                string old=settings.SystemPrompt ?? "";
                string key,name;
                if(!String.IsNullOrWhiteSpace(old) && !RecognizeDefault(old,out key,out name))
                {
                    string owner=settings.SkinId;
                    if(!settings.PersonaOverrides.ContainsKey(owner)) settings.PersonaOverrides[owner]=old;
                }
                settings.PersonaProfileVersion=1;
            }
            string prompt; settings.SystemPrompt=settings.PersonaOverrides.TryGetValue(skinId,out prompt) ? LocalizeDefaultPrompt(prompt) : DefaultPrompt(defaultPrompt,officialId);
            settings.SkinId=skinId;
            settings.ActiveVoiceRecommendations=voices;
            settings.VoiceName=VoiceFor(settings,skinId,"speech",settings.SpeechConnection());
            settings.LiveVoice=VoiceFor(settings,skinId,"live",settings.Live);
        }
        public static void Save(PetSettings settings,string skinId,string defaultPrompt,string prompt,VoiceRecommendations voices=null,string officialId=null)
        {
            prompt=(prompt ?? "").Trim(); if(prompt.Length==0 || prompt.Length>8000) throw new InvalidOperationException(TextResources.Get("personaProfiles.save.text"));
            Activate(settings,skinId,defaultPrompt,voices,officialId); settings.PersonaOverrides[skinId]=prompt; settings.SystemPrompt=prompt;
        }
        public static void Reset(PetSettings settings,string skinId,string defaultPrompt,VoiceRecommendations voices=null,string officialId=null) { Activate(settings,skinId,defaultPrompt,voices,officialId); settings.PersonaOverrides.Remove(skinId); settings.SystemPrompt=DefaultPrompt(defaultPrompt,officialId); }
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
