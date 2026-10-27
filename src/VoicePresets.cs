using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    public sealed class VoicePreset
    {
        public string Id { get; private set; }
        public string Description { get; private set; }
        public VoicePreset(string id, string description) { Id = id; Description = description; }
        public override string ToString() { return Id + " · " + Description; }
    }
    public static class VoicePresets
    {
        // IDs and neutral labels are sourced in docs/voice-presets.md; custom input stays available.
        public static IList<VoicePreset> For(string kind, ConnectionSettings settings)
        {
            var items = new List<VoicePreset>(); if(settings == null) return items;
            Uri uri; if(!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out uri)) return items;
            string model = (settings.Model ?? "").Trim().ToLowerInvariant();
            if(model.StartsWith("models/")) model = model.Substring(7);
            string protocol = (settings.Protocol ?? "auto").Trim().ToLowerInvariant();
            bool generic = protocol == "auto" || protocol == "" || protocol == "openai";
            string host = uri.Host.TrimEnd('.').ToLowerInvariant();
            bool mimoHost = host == "xiaomimimo.com" || host.EndsWith(".xiaomimimo.com");
            bool openaiHost = host == "api.openai.com";
            bool geminiHost = host == "generativelanguage.googleapis.com";
            if(kind == "speech")
            {
                if(model == "mimo-v2.5-tts" && (generic || protocol == "mimo") && !openaiHost && !geminiHost)
                {
                    items.Add(new VoicePreset("mimo_default", TextResources.Get("voicePresets.for.items")));
                    foreach(string id in new[] {"冰糖", "茉莉"}) items.Add(new VoicePreset(id, TextResources.Get("voicePresets.for.items.alternativeA")));
                    foreach(string id in new[] {"苏打", "白桦"}) items.Add(new VoicePreset(id, TextResources.Get("voicePresets.for.items.alternativeB")));
                    foreach(string id in new[] {"Mia", "Chloe"}) items.Add(new VoicePreset(id, TextResources.Get("voicePresets.for.items.alternativeC")));
                    foreach(string id in new[] {"Milo", "Dean"}) items.Add(new VoicePreset(id, TextResources.Get("voicePresets.for.items.alternativeD")));
                }
                else if(generic && !mimoHost && !geminiHost && (model == "tts-1" || model == "tts-1-hd" || model == "gpt-4o-mini-tts"))
                {
                    string[] voices = model == "gpt-4o-mini-tts" ? new[] {"alloy","ash","ballad","coral","echo","fable","nova","onyx","sage","shimmer","verse","marin","cedar"} : new[] {"alloy","ash","coral","echo","fable","onyx","nova","sage","shimmer"};
                    foreach(string id in voices) items.Add(new VoicePreset(id, TextResources.Get("voicePresets.for.items.alternativeE")));
                }
            }
            else if(kind == "live" && (protocol == "gemini-live" || protocol == "auto" || protocol == "") && !mimoHost && !openaiHost && NativeLiveModel(model))
            {
                string[,] voices = {
                    {"Kore",TextResources.Get("voicePresets.gemini.kore.description")}, {"Zephyr",TextResources.Get("voicePresets.gemini.zephyr.description")}, {"Puck",TextResources.Get("voicePresets.gemini.puck.description")}, {"Charon",TextResources.Get("voicePresets.gemini.charon.description")}, {"Fenrir",TextResources.Get("voicePresets.gemini.fenrir.description")},
                    {"Leda",TextResources.Get("voicePresets.gemini.leda.description")}, {"Orus",TextResources.Get("voicePresets.gemini.orus.description")}, {"Aoede",TextResources.Get("voicePresets.gemini.aoede.description")}, {"Callirrhoe",TextResources.Get("voicePresets.gemini.callirrhoe.description")}, {"Autonoe",TextResources.Get("voicePresets.gemini.autonoe.description")},
                    {"Enceladus",TextResources.Get("voicePresets.gemini.enceladus.description")}, {"Iapetus",TextResources.Get("voicePresets.gemini.iapetus.description")}, {"Umbriel",TextResources.Get("voicePresets.gemini.umbriel.description")}, {"Algieba",TextResources.Get("voicePresets.gemini.algieba.description")}, {"Despina",TextResources.Get("voicePresets.gemini.despina.description")},
                    {"Erinome",TextResources.Get("voicePresets.gemini.erinome.description")}, {"Algenib",TextResources.Get("voicePresets.gemini.algenib.description")}, {"Rasalgethi",TextResources.Get("voicePresets.gemini.rasalgethi.description")}, {"Laomedeia",TextResources.Get("voicePresets.gemini.laomedeia.description")}, {"Achernar",TextResources.Get("voicePresets.gemini.achernar.description")},
                    {"Alnilam",TextResources.Get("voicePresets.gemini.alnilam.description")}, {"Schedar",TextResources.Get("voicePresets.gemini.schedar.description")}, {"Gacrux",TextResources.Get("voicePresets.gemini.gacrux.description")}, {"Pulcherrima",TextResources.Get("voicePresets.gemini.pulcherrima.description")}, {"Achird",TextResources.Get("voicePresets.gemini.achird.description")},
                    {"Zubenelgenubi",TextResources.Get("voicePresets.gemini.zubenelgenubi.description")}, {"Vindemiatrix",TextResources.Get("voicePresets.gemini.vindemiatrix.description")}, {"Sadachbia",TextResources.Get("voicePresets.gemini.sadachbia.description")}, {"Sadaltager",TextResources.Get("voicePresets.gemini.sadaltager.description")}, {"Sulafat",TextResources.Get("voicePresets.gemini.sulafat.description")}
                };
                for(int i = 0; i < voices.GetLength(0); i++) items.Add(new VoicePreset(voices[i,0], voices[i,1]));
            }
            return items;
        }
        private static bool NativeLiveModel(string model)
        {
            return model == "gemini-3.8-live" || model == "gemini-3.8-live-extended-thinking" || model == "gemini-3.1-flash-live-preview" ||
                model == "gemini-2.5-flash-native-audio" || model.StartsWith("gemini-2.5-flash-native-audio-preview-") || model == "gemini-live-2.5-flash-native-audio";
        }
    }
}
