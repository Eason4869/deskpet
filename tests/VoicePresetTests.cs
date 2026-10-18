using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class VoicePresetTests
    {
        private static int passed, failed;
        [STAThread] public static int Main()
        {
            Run("voice recommendations never cross model or provider catalogs", delegate {
                foreach(string pet in new[]{"e-bao","dafeiyu","marfu"})foreach(var connection in new[]{
                    new ConnectionSettings{BaseUrl="https://api.xiaomimimo.com/v1",Model="mimo-v2.5-tts",Protocol="mimo"},
                    new ConnectionSettings{BaseUrl="https://api.openai.com/v1",Model="tts-1",Protocol="openai"},
                    new ConnectionSettings{BaseUrl="https://api.openai.com/v1",Model="gpt-4o-mini-tts",Protocol="openai"}}) {
                    string recommended=PersonaProfiles.RecommendedVoice(pet,"speech",connection);
                    Assert(Array.IndexOf(Ids(VoicePresets.For("speech",connection)).Split(','),recommended)>=0,"recommendation unavailable on selected provider/model");
                }
                Equal("",PersonaProfiles.RecommendedVoice("e-bao","speech",new ConnectionSettings{BaseUrl="https://api.openai.com/v1",Model="custom-tts"}));
            });
            Run("manual IDs are retained independently for each service model", delegate {
                var settings=new PetSettings();settings.Normalize();
                settings.Speech=new ConnectionSettings{BaseUrl="https://api.openai.com/v1",Model="gpt-4o-mini-tts",Protocol="openai"};
                settings.VoiceName="private-mini-voice";PersonaProfiles.SaveVoices(settings);
                settings.Speech.Model="tts-1";
                Equal("ash",PersonaProfiles.VoiceFor(settings,"e-bao","speech",settings.Speech));
                settings.VoiceName="private-legacy-voice";PersonaProfiles.SaveVoices(settings);
                settings.Speech.Model="gpt-4o-mini-tts";
                Equal("private-mini-voice",PersonaProfiles.VoiceFor(settings,"e-bao","speech",settings.Speech));
                settings.Speech.BaseUrl="https://another-service.example/v1";
                Equal("ash",PersonaProfiles.VoiceFor(settings,"e-bao","speech",settings.Speech));
            });
            Run("MiMo model on direct and Token Plan hosts exposes only actual built-in voice IDs", delegate {
                foreach(string url in new[] { "https://api.xiaomimimo.com/v1", "https://token-plan-cn.xiaomimimo.com/v1" })
                    Equal("mimo_default,冰糖,茉莉,苏打,白桦,Mia,Chloe,Milo,Dean", Ids(For("speech", url, "mimo-v2.5-tts", "mimo")));
            });
            Run("MiMo clone and design models cannot suggest unsupported preset IDs", delegate {
                foreach(string model in new[] { "mimo-v2.5-tts-voiceclone", "mimo-v2.5-tts-voicedesign", "mimo-v2.5-asr" })
                    Equal("", Ids(For("speech", "https://api.xiaomimimo.com/v1", model, "mimo")));
            });
            Run("OpenAI legacy TTS models use their documented smaller nine-voice set", delegate {
                foreach(string model in new[] { "tts-1", "tts-1-hd" })
                    Equal("alloy,ash,coral,echo,fable,onyx,nova,sage,shimmer", Ids(For("speech", "https://api.openai.com/v1", model, "openai")));
            });
            Run("OpenAI mini TTS adds voices unavailable on tts-1", delegate {
                Equal("alloy,ash,ballad,coral,echo,fable,nova,onyx,sage,shimmer,verse,marin,cedar", Ids(For("speech", "https://api.openai.com/v1", "gpt-4o-mini-tts", "auto")));
            });
            Run("Gemini native Live exposes documented thirty IDs with Kore supported default", delegate {
                IList<VoicePreset> values = For("live", "wss://generativelanguage.googleapis.com/ws/test", "gemini-3.8-live", "gemini-live");
                Equal(30, values.Count); Equal("Kore", values[0].Id);
                Assert(Ids(values).Contains("Puck") && Ids(values).Contains("Vindemiatrix"), "native voice list incomplete");
                Assert(values[0].Description.Contains("坚定"), "missing official style");
            });
            Run("official host and model conflicts cannot mix manufacturers", delegate {
                Equal("", Ids(For("speech", "https://api.openai.com/v1", "mimo-v2.5-tts", "auto")));
                Equal("", Ids(For("speech", "https://api.xiaomimimo.com/v1", "tts-1", "auto")));
                Equal("", Ids(For("speech", "https://generativelanguage.googleapis.com/v1", "tts-1", "openai")));
                Equal("", Ids(For("live", "wss://api.openai.com/live", "gemini-3.8-live", "gemini-live")));
            });
            Run("explicit incompatible protocols cannot silently select a different vendor", delegate {
                Equal("", Ids(For("speech", "https://proxy.example/v1", "tts-1", "mimo")));
                Equal("", Ids(For("speech", "https://api.openai.com/v1", "tts-1", "anthropic")));
                Equal("", Ids(For("live", "wss://generativelanguage.googleapis.com/ws", "gemini-3.8-live", "openai")));
            });
            Run("custom proxy can infer actual voice family from supported model and protocol", delegate {
                Equal(9, For("speech", "https://proxy.example/v1", "mimo-v2.5-tts", "auto").Count);
                Equal(9, For("speech", "http://127.0.0.1:8080/v1", "tts-1", "openai").Count);
                Equal(30, For("live", "wss://proxy.example/live", "models/gemini-3.8-live", "gemini-live").Count);
            });
            Run("unknown models and unsupported service kinds allow manual entry", delegate {
                Equal(0, For("speech", "https://api.openai.com/v1", "custom-tts", "openai").Count);
                Equal(0, For("live", "wss://generativelanguage.googleapis.com/ws", "gemini-unknown-live", "gemini-live").Count);
                Equal(0, For("asr", "https://api.openai.com/v1", "tts-1", "openai").Count);
                Equal(0, For("speech", "https://proxy.example/v1", "", "auto").Count);
            });
            Run("MiMo descriptions use documented gender and mark unauditioned style", delegate {
                IList<VoicePreset> values = For("speech", "https://api.xiaomimimo.com/v1", "mimo-v2.5-tts", "mimo");
                Assert(values[3].Description.Contains("男声") && values[3].Description.Contains("试听"), "unsupported MiMo style implied");
                Assert(values[3].ToString().Contains("苏打") && values[3].ToString().Contains("男声"), "label loses ID or description");
            });
            Run("changing one returned list cannot contaminate later vendor lists", delegate {
                IList<VoicePreset> first = For("speech", "https://api.openai.com/v1", "tts-1", "openai"); first.Clear();
                Equal(9, For("speech", "https://api.openai.com/v1", "tts-1", "openai").Count);
            });
            Run("real editable voice controls preserve each pet and provider on save and reopen",RealVoiceControls);
            Console.WriteLine("Voice preset tests: " + passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
        private static void RealVoiceControls()
        {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"voice-profile-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try
            {
                var cfg=new PetSettings {GreetingEnabled=false,Wander=false,AutoUpdate=false,HideOnFullscreen=false,SaveChatHistory=false};
                foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();pet.ShowSettings();Pump();
                var settings=SettingsPageFor(pet);
                foreach(string id in new[]{"e-bao","dafeiyu","marfu"})
                {
                    settings.SelectPage(0);Pump();((ComboBox)settings.FindControl("SkinPicker")).SelectedItem=pet.Catalog.Find(id);Pump();Equal(id,pet.Skin.Id);
                    settings.SelectAiSection(2);Pump();((CheckBox)settings.FindControl("UseCloudVoice")).IsChecked=true;
                    ((ComboBox)settings.FindControl("SpeechPreset")).SelectedIndex=3;Pump();
                    var speech=(ComboBox)settings.FindControl("SpeechVoice");Assert(speech.Items.Cast<VoicePreset>().Any(v=>v.Id=="alloy"),"OpenAI catalog missing");
                    speech.SelectedItem=speech.Items.Cast<VoicePreset>().First(v=>v.Id=="alloy");Pump();Equal("alloy",Editable(speech).Text);
                    Editable(speech).Text="voice-"+id+"-openai";Pump();Equal("voice-"+id+"-openai",speech.Text);Assert(settings.SaveAiConfiguration(),"OpenAI save failed");
                    settings.SelectAiSection(3);Pump();((ComboBox)settings.FindControl("LiveServicePreset")).SelectedIndex=1;Pump();
                    var live=(ComboBox)settings.FindControl("LiveVoice");Assert(live.Items.Cast<VoicePreset>().Any(v=>v.Id=="Kore"),"Live catalog missing");
                    Editable(live).Text="voice-"+id+"-live";Pump();Assert(settings.SaveAiConfiguration(),"Live save failed");
                    settings.SelectAiSection(2);Pump();((ComboBox)settings.FindControl("SpeechPreset")).SelectedIndex=1;Pump();
                    speech=(ComboBox)settings.FindControl("SpeechVoice");Assert(speech.Items.Cast<VoicePreset>().Any(v=>v.Id=="苏打"),"MiMo catalog missing");
                    Editable(speech).Text="voice-"+id+"-mimo";Pump();Assert(settings.SaveAiConfiguration(),"MiMo save failed");
                    speech.BringIntoView();Pump();Capture(settings,Path.Combine(folder,id+"-mimo.png"));
                    ((ComboBox)settings.FindControl("SpeechPreset")).SelectedIndex=3;Pump();Equal("voice-"+id+"-openai",Editable(speech).Text);Assert(settings.SaveAiConfiguration(),"restored OpenAI save failed");
                    Equal("voice-"+id+"-openai",pet.Store.Load().VoiceName);Equal("voice-"+id+"-live",pet.Store.Load().LiveVoice);
                }
                foreach(string id in new[]{"e-bao","dafeiyu","marfu"})
                {
                    settings.SelectPage(0);Pump();((ComboBox)settings.FindControl("SkinPicker")).SelectedItem=pet.Catalog.Find(id);Pump();Equal(id,pet.Skin.Id);
                    settings.SelectAiSection(2);Pump();var voice=(ComboBox)settings.FindControl("SpeechVoice");Equal("voice-"+id+"-openai",Editable(voice).Text);
                    ((ComboBox)settings.FindControl("SpeechPreset")).SelectedIndex=1;Pump();Equal("voice-"+id+"-mimo",Editable(voice).Text);Assert(settings.SaveAiConfiguration(),"restored MiMo save failed");
                    ((ComboBox)settings.FindControl("SpeechPreset")).SelectedIndex=3;Pump();Equal("voice-"+id+"-openai",Editable(voice).Text);Assert(settings.SaveAiConfiguration(),"restored provider save failed");
                    settings.SelectAiSection(3);Pump();Equal("voice-"+id+"-live",Editable((ComboBox)settings.FindControl("LiveVoice")).Text);
                }
                settings.HostWindow.Close();pet.ShowSettings();Pump();settings=SettingsPageFor(pet);settings.SelectAiSection(2);Pump();Equal("voice-marfu-openai",Editable((ComboBox)settings.FindControl("SpeechVoice")).Text);
                settings.SelectAiSection(3);Pump();var restored=(ComboBox)settings.FindControl("LiveVoice");Equal("voice-marfu-live",Editable(restored).Text);restored.BringIntoView();Pump();Capture(settings,Path.Combine(folder,"marfu-live-reopened.png"));
                Console.WriteLine("Voice UI previews: "+folder);
            }
            finally{if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
        private static TextBox Editable(ComboBox box){var field=box.Template.FindName("PART_EditableTextBox",box) as TextBox;Assert(box.IsEditable && field!=null && field.Visibility==Visibility.Visible,"visible editable voice part missing");return field;}
        private static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(120)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        private static SettingsPage SettingsPageFor(PetWindow pet){return (SettingsPage)typeof(PetWindow).GetField("preferences",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);}
        private static void Capture(FrameworkElement window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);using(var stream=File.Create(path)){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(stream);}}
        private static IList<VoicePreset> For(string kind, string url, string model, string protocol)
        {
            return VoicePresets.For(kind, new ConnectionSettings {BaseUrl=url,Model=model,Protocol=protocol});
        }
        private static string Ids(IList<VoicePreset> values) { var ids = new List<string>(); foreach(VoicePreset item in values) ids.Add(item.Id); return String.Join(",", ids); }
        private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch(Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
        private static void Equal<T>(T expected, T actual) { Assert(Object.Equals(expected, actual), "Expected " + expected + ", got " + actual); }
        private static void Assert(bool value, string reason) { if(!value) throw new Exception(reason); }
    }
}
