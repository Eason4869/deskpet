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
        public static string Dafeiyu { get { return TextResources.Get("personaProfiles.defaultsUpdated.dafeiyu"); } }
        public static string Ebao { get { return TextResources.Get("personaProfiles.defaultsUpdated.ebao"); } }
        public static string Marfu { get { return TextResources.Get("personaProfiles.defaultsUpdated.marfu"); } }
        public static string Generic(string name) { return TextResources.Get("personaProfiles.defaultsUpdated.generic").Replace("{pet_name}",name??""); }
#if !PORTABLE
        public static void Activate(PetSettings settings,SkinDefinition skin) { Activate(settings,skin.Id,skin.SystemPrompt,skin.Manifest==null?null:skin.Manifest.VoicePresets,skin.BuiltinCharacterId); }
        public static void Save(PetSettings settings,SkinDefinition skin,string prompt) { Save(settings,skin.Id,skin.SystemPrompt,prompt,skin.Manifest==null?null:skin.Manifest.VoicePresets,skin.BuiltinCharacterId); }
        public static void Reset(PetSettings settings,SkinDefinition skin) { Reset(settings,skin.Id,skin.SystemPrompt,skin.Manifest==null?null:skin.Manifest.VoicePresets,skin.BuiltinCharacterId); }
#endif
        static readonly string[] DefaultKeys={"personaProfiles.defaultsUpdated.ebao","personaProfiles.defaultsUpdated.dafeiyu","personaProfiles.defaultsUpdated.marfu","personaProfiles.defaultsUpdated.xiaobei","personaProfiles.defaultsUpdated.gugugaga","personaProfiles.defaultsUpdated.agent","personaProfiles.defaultsUpdated.generic"};
        // Only exact known defaults/templates can be localized. A user edit is never inferred from keywords.
        static bool RecognizeDefault(string prompt,out string key,out string name)
        {
            key=null;name=null;if(prompt==null)return false;
            foreach(var choice in UiLanguages.Choices){
                var catalog=TextResources.CatalogFor(choice.Code);string access=TextResources.Get("personaProfiles.screenAccess",catalog);
                foreach(string candidate in DefaultKeys){
                    string current=catalog.Get(candidate);
                    // Exact old templates remain recognizable in saved settings and older skin packs.
                    // Never infer a user-edited prompt from a matching sentence alone.
                    string legacy=current.EndsWith("\n\n"+access,StringComparison.Ordinal)
                        ?current.Substring(0,current.Length-access.Length-2)
                        :current.Replace(access,LegacyScreenAccess(candidate,catalog));
                    foreach(string template in new[]{current,legacy,OriginalDefault(candidate,catalog)}){
                        if(prompt==template){key=candidate;return true;}
                        if(candidate!="personaProfiles.defaultsUpdated.generic")continue;
                        const string marker="{pet_name}";int split=template.IndexOf(marker,StringComparison.Ordinal);if(split<0)continue;
                        string prefix=template.Substring(0,split),suffix=template.Substring(split+marker.Length);
                        if(prompt.Length>=prefix.Length+suffix.Length&&prompt.StartsWith(prefix,StringComparison.Ordinal)&&prompt.EndsWith(suffix,StringComparison.Ordinal))
                        {key=candidate;name=prompt.Substring(prefix.Length,prompt.Length-prefix.Length-suffix.Length);return true;}
                    }
                }
            }
            return false;
        }
        static string LegacyScreenAccess(string key,TextCatalog catalog)
        {
            switch(key){
                case "personaProfiles.defaultsUpdated.ebao":return TextResources.Get("personaProfiles.legacyScreenAccess.ebao",catalog);
                case "personaProfiles.defaultsUpdated.dafeiyu":return TextResources.Get("personaProfiles.legacyScreenAccess.dafeiyu",catalog);
                case "personaProfiles.defaultsUpdated.marfu":return TextResources.Get("personaProfiles.legacyScreenAccess.marfu",catalog);
                case "personaProfiles.defaultsUpdated.generic":return TextResources.Get("personaProfiles.legacyScreenAccess.generic",catalog);
                default:throw new ArgumentException("Unknown legacy screen access key.","key");
            }
        }
        static string OriginalDefault(string key,TextCatalog catalog)
        {
            switch(key){
                case "personaProfiles.defaultsUpdated.ebao":return TextResources.Get("personaProfiles.defaults.ebao",catalog);
                case "personaProfiles.defaultsUpdated.dafeiyu":return TextResources.Get("personaProfiles.defaults.dafeiyu",catalog);
                case "personaProfiles.defaultsUpdated.marfu":return TextResources.Get("personaProfiles.defaults.marfu",catalog);
                case "personaProfiles.defaultsUpdated.xiaobei":return TextResources.Get("personaProfiles.defaults.xiaobei",catalog);
                case "personaProfiles.defaultsUpdated.gugugaga":return TextResources.Get("personaProfiles.defaults.gugugaga",catalog);
                case "personaProfiles.defaultsUpdated.agent":return TextResources.Get("personaProfiles.defaults.agent",catalog);
                case "personaProfiles.defaultsUpdated.generic":return TextResources.Get("personaProfiles.defaults.generic",catalog);
                default:throw new ArgumentException("Unknown original default persona key.","key");
            }
        }
        public static string LocalizeDefaultPrompt(string prompt)
        {
            string key,name;if(!RecognizeDefault(prompt,out key,out name))return prompt;
            string result=CurrentDefault(key);return name==null?result:result.Replace("{pet_name}",name);
        }
        public static string LocalizeDefaultPrompt(string prompt,LanguageSelection language)
        {
            if(language==null)throw new ArgumentNullException("language");
            string key,name;if(!RecognizeDefault(prompt,out key,out name))return prompt;
            string result=language.Get(key);return name==null?result:result.Replace("{pet_name}",name);
        }
        static string CurrentDefault(string key)
        {
            switch(key)
            {
                case "personaProfiles.defaultsUpdated.ebao":return TextResources.Get("personaProfiles.defaultsUpdated.ebao");
                case "personaProfiles.defaultsUpdated.dafeiyu":return TextResources.Get("personaProfiles.defaultsUpdated.dafeiyu");
                case "personaProfiles.defaultsUpdated.marfu":return TextResources.Get("personaProfiles.defaultsUpdated.marfu");
                case "personaProfiles.defaultsUpdated.xiaobei":return TextResources.Get("personaProfiles.defaultsUpdated.xiaobei");
                case "personaProfiles.defaultsUpdated.gugugaga":return TextResources.Get("personaProfiles.defaultsUpdated.gugugaga");
                case "personaProfiles.defaultsUpdated.agent":return TextResources.Get("personaProfiles.defaultsUpdated.agent");
                case "personaProfiles.defaultsUpdated.generic":return TextResources.Get("personaProfiles.defaultsUpdated.generic");
                default:throw new ArgumentException("Unknown default persona key.","key");
            }
        }
        static string DefaultPrompt(string prompt,string officialId)
        {
            string key;
            switch(officialId){case "e-bao":key="personaProfiles.defaultsUpdated.ebao";break;case "dafeiyu":key="personaProfiles.defaultsUpdated.dafeiyu";break;case "marfu":key="personaProfiles.defaultsUpdated.marfu";break;case "xiaobei":key="personaProfiles.defaultsUpdated.xiaobei";break;case "gugugaga":key="personaProfiles.defaultsUpdated.gugugaga";break;default:return LocalizeDefaultPrompt(prompt);}
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
