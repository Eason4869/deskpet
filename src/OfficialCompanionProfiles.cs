using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    public sealed class OfficialCompanionProfile
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string DisplayName { get { return OfficialCompanionProfiles.GetDisplayName(Id,Name); } }
        string description,systemPrompt;
        public string Description { get { return OfficialCompanionProfiles.GetDescription(Id,description); } private set { description=value; } }
        public string SystemPrompt { get { return PersonaProfiles.LocalizeDefaultPrompt(systemPrompt); } private set { systemPrompt=value; } }
        string specialAction,specialAction2;
        public string SpecialAction { get { return OfficialCompanionProfiles.GetAction(Id,false,specialAction); } private set { specialAction=value; } }
        public string SpecialAction2 { get { return OfficialCompanionProfiles.GetAction(Id,true,specialAction2); } private set { specialAction2=value; } }
        public string AvatarResourceName { get { return "Dafeiyu.Avatar." + Id; } }
        readonly VoiceRecommendations voices;
        public VoiceRecommendations VoicePresets { get { var result=Copy(voices);if(Id=="gugugaga"){result.Description=TextResources.Get("officialCompanionProfiles.profiles.description");result.SampleText=TextResources.Get("officialCompanionProfiles.profiles.sampleText");}return result; } }
        internal OfficialCompanionProfile(string id, string name, string description, string prompt,
            string special, string special2, VoiceRecommendations voicePresets)
        {
            Id=id; Name=name; Description=description; SystemPrompt=prompt;
            SpecialAction=special; SpecialAction2=special2; voices=Copy(voicePresets);
        }
        static VoiceRecommendations Copy(VoiceRecommendations value)
        {
            return new VoiceRecommendations { Speech=(string[])value.Speech.Clone(), Live=(string[])value.Live.Clone(),
                Description=value.Description, SampleText=value.SampleText };
        }
    }
    // Small, shared catalog metadata. Artwork remains in separately installed
    // packages. These descriptions alone do not confer trusted provenance.
    public static class OfficialCompanionProfiles
    {
        static readonly IList<OfficialCompanionProfile> profiles = new List<OfficialCompanionProfile> {
            new OfficialCompanionProfile("e-bao", "E包", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile"), TextResources.Get("personaProfiles.defaults.ebao"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeA"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeB"),
                new VoiceRecommendations { Speech=new string[] { "苏打", "ash" }, Live=new string[] { "Puck" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("dafeiyu", "大肥鱼", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeC"), TextResources.Get("personaProfiles.defaults.dafeiyu"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeD"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeE"),
                new VoiceRecommendations { Speech=new string[] { "冰糖", "nova" }, Live=new string[] { "Aoede" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("marfu", "马尔芙", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeF"), TextResources.Get("personaProfiles.defaults.marfu"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeG"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeH"),
                new VoiceRecommendations { Speech=new string[] { "茉莉", "coral" }, Live=new string[] { "Kore" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("xiaobei", "小贝", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeI"), TextResources.Get("personaProfiles.defaults.xiaobei"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeJ"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeK"),
                new VoiceRecommendations { Speech=new string[] { "冰糖", "nova" }, Live=new string[] { "Aoede" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("gugugaga", "咕嘎", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeL"), TextResources.Get("personaProfiles.defaults.gugugaga"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeM"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeN"),
                new VoiceRecommendations { Speech=new string[] { "冰糖", "shimmer", "nova" }, Live=new string[] { "Leda", "Zephyr" }, Description=TextResources.Get("officialCompanionProfiles.profiles.description"), SampleText=TextResources.Get("officialCompanionProfiles.profiles.sampleText") }),
        }.AsReadOnly();
        public static IList<OfficialCompanionProfile> All { get { return profiles; } }
        internal static string GetAction(string id,bool second,string original)
        {
            switch(id)
            {
                case "e-bao":return (second?TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeB"):TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeA"));
                case "dafeiyu":return (second?TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeE"):TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeD"));
                case "marfu":return (second?TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeH"):TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeG"));
                case "xiaobei":return (second?TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeK"):TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeJ"));
                case "gugugaga":return (second?TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeN"):TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeM"));
                default:return original;
            }
        }
        internal static string GetDescription(string id,string original)
        {
            switch(id)
            {
                case "e-bao":return TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile");
                case "dafeiyu":return TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeC");
                case "marfu":return TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeF");
                case "xiaobei":return TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeI");
                case "gugugaga":return TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeL");
                default:return original;
            }
        }
        // Display uses trusted official IDs. Stored names, prompts and custom names remain unchanged.
        public static string GetDisplayName(string id,string originalName)
        {
            switch(id)
            {
                case "e-bao":return TextResources.Get("officialCompanionProfiles.names.ebao");
                case "dafeiyu":return TextResources.Get("officialCompanionProfiles.names.dafeiyu");
                case "marfu":return TextResources.Get("officialCompanionProfiles.names.marfu");
                case "xiaobei":return TextResources.Get("officialCompanionProfiles.names.xiaobei");
                case "gugugaga":return TextResources.Get("officialCompanionProfiles.names.gugugaga");
                default:return originalName;
            }
        }
        public static OfficialCompanionProfile Find(string id)
        {
            foreach (var profile in profiles) if (profile.Id == id) return profile;
            return null;
        }
        public static bool IsOfficialId(string id) { return Find(id) != null; }
    }
}
