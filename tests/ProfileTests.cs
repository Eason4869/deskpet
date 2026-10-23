using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dafeiyu;
namespace Dafeiyu.Tests
{
    public static class ProfileTests
    {
        static int passed;
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profile-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var catalog=new SkinCatalog(root);var settings=new PetSettings();
                CheckVoiceProfiles(catalog,root);
                Check(settings.LiveVoice=="Puck","new default Ebao gets its recommended Live voice");
                Check(new PetSettings().OnboardingState=="new","fresh install requests first-run onboarding");
                string guideFile=Path.Combine(root,"onboarding-settings.json");File.WriteAllText(guideFile,"{\"SkinId\":\"marfu\",\"VoiceName\":\"keep-me\"}");var guideStore=new SettingsStore(guideFile);var guide=guideStore.Load();
                Check(guide.OnboardingState=="completed" && guide.VoiceName=="keep-me" && guide.LiveVoice=="Kore","legacy settings upgrade without reopening first-run onboarding or losing configuration");
                guide.OnboardingState="deferred";guide.OnboardingStep=3;guideStore.Save(guide);guide=guideStore.Load().Copy();
                Check(guide.OnboardingState=="deferred" && guide.OnboardingStep==3,"deferred onboarding resumes its persisted step and survives settings copy");
                guide.OnboardingState="invalid";guide.OnboardingStep=99;guide.Normalize();Check(guide.OnboardingState=="completed" && guide.OnboardingStep==PetSettings.OnboardingLastStep,"unknown onboarding state cannot retrigger welcome and step is bounded");
                File.WriteAllText(guideFile,"{\"OnboardingState\":\"deferred\",\"OnboardingStep\":7}");guide=guideStore.Load();
                Check(guide.OnboardingStep==8,"old service lesson progress migrates after inserted double click step");guideStore.Save(guide);guide=guideStore.Load().Copy();guide.Normalize();
                Check(guide.OnboardingStep==8,"migrated progress is not shifted again after save load and copy");
                guide.OnboardingStep=15;guideStore.Save(guide);guide=guideStore.Load();Check(guide.OnboardingStep==15,"appended network lesson progress survives restart without remapping");
                Check(catalog.Skins[0].Id=="e-bao" && catalog.Skins[1].Id=="dafeiyu" && catalog.Find("missing").Id=="e-bao","Ebao is first and default fallback, with Dafeiyu second");
                PersonaProfiles.Activate(settings,catalog.Find("dafeiyu"));Check(settings.SystemPrompt.Contains("鲸鱼娘"),"new dafeiyu resolves whale persona");
                foreach(var builtin in catalog.Skins.Where(s=>s.Embedded)) {
                    var original=builtin.Load();
                    Check(original.FrameMultiplier==1 && original.CellWidth==480 && original.CellHeight==520,"original real-frame atlas loads: "+builtin.Id);
                    CheckOriginalFrames(builtin,original);
                }
                var marfu=catalog.Find("marfu");
                var cadence=CreateAnimation(marfu);cadence.Play("running-right",0);cadence.Advance(.12);Check(cadence.Frame==0,"Marfu right run holds its pose beyond the former .11-second interval");
                int[] right={0,2,3,2,4,2,6,2},left={0,2,3,2,4,2,6,2};
                for(int phase=0;phase<8;phase++) Check(ActionFrame(marfu,1,phase)==right[phase] && ActionFrame(marfu,2,phase)==left[phase],"Marfu alternating original running poses phase "+phase);
                foreach(var builtin in catalog.Skins.Where(s=>s.Embedded)) for(int row=0;row<11;row++) for(int phase=0;phase<(row<9?AnimationPlayer.Counts[row]:8);phase++)
                    if(builtin.Id!="marfu" || row<1 || row>2) if(ActionFrame(builtin,row,phase)!=phase) throw new Exception("other action sampling changed: "+builtin.Id+" "+row);
                Check(true,"other pets, other actions and original gaze frame order remain unchanged");
                Check(ActionRow(marfu,2)==1 && MirrorsAction(marfu,2),"Marfu left stride uses visibly extended right-run artwork mirrored at runtime");
                var whale=catalog.Find("dafeiyu");var ebao=catalog.Find("e-bao");
                Check(ActionRow(whale,2)==1 && MirrorsAction(whale,2),"Dafeiyu left stride mirrors the accepted forward right-run poses");
                for(int row=0;row<11;row++) if(row!=2 && (ActionRow(whale,row)!=row || MirrorsAction(whale,row)))throw new Exception("Dafeiyu unrelated action mirrored");
                Check(ActionRow(ebao,2)==2 && !MirrorsAction(ebao,2),"Ebao retains its original independent left-facing artwork");
                foreach(var builtin in catalog.Skins.Where(s=>s.Embedded)) foreach(string action in new[]{"running-right","running-left"}) {
                    double period=.22;
                    var animation=CreateAnimation(builtin);animation.Play(action,0);animation.Advance(period-.001);
                    Check(animation.Frame==0,builtin.Id+" holds its real pose for "+period+" seconds: "+action);animation.Advance(.002);Check(animation.Frame==1,builtin.Id+" advances at "+period+" seconds: "+action);
                    animation.Play(action,0);animation.Advance(period*8-.001);Check(animation.Frame==7,builtin.Id+" eight-frame cycle lasts "+period*8+" seconds: "+action);animation.Advance(.0011);Check(animation.Frame==0,builtin.Id+" original cycle wraps cleanly: "+action);
                    animation.Play(action,.3);animation.Advance(.301);Check(animation.State=="idle",builtin.Id+" slower running does not stretch explicit action duration: "+action);
                }
                foreach(var builtin in catalog.Skins.Where(s=>s.Embedded))foreach(string action in AnimationPlayer.States) {
                    if(action=="running-right" || action=="running-left" || AdditionalInteractions.Has(builtin,action))continue;
                    double actualDuration;bool actualLoop;if(SmoothMotion.TryTiming(builtin.Id,Array.IndexOf(AnimationPlayer.States,action),out actualDuration,out actualLoop) && !actualLoop)continue;
                    var baseline=new AnimationPlayer();var selected=CreateAnimation(builtin);baseline.Play(action,0);selected.Play(action,0);
                    for(int tick=0;tick<120;tick++){baseline.Advance(.016);selected.Advance(.016);if(baseline.Frame!=selected.Frame)throw new Exception("unrelated animation timing changed: "+builtin.Id+" "+action);}
                }
                Check(true,"all non-running actions keep their original timing");
                PersonaProfiles.Activate(settings,catalog.Find("marfu"));Check(catalog.Skins.Count(s=>s.Embedded)==3 && settings.SystemPrompt.Contains("马尔芙") && settings.SystemPrompt.Contains("魔法少女") && settings.SystemPrompt.Contains("性转马尔福") && settings.SystemPrompt.Contains("哈利波特"),"Marfu default includes the requested gender-swapped Malfoy background");
                PersonaProfiles.Save(settings,catalog.Find("marfu"),"自定义马尔芙人格");PersonaProfiles.Activate(settings,catalog.Find("e-bao"));PersonaProfiles.Activate(settings,catalog.Find("marfu"));Check(settings.SystemPrompt=="自定义马尔芙人格","Marfu default update preserves an explicit user override");PersonaProfiles.Reset(settings,catalog.Find("marfu"));Check(settings.SystemPrompt==PersonaProfiles.Marfu,"Marfu reset restores the updated default");
                PersonaProfiles.Activate(settings,catalog.Find("e-bao"));string exact="你是E包，一位傲娇、毒舌的白发正太桌面伙伴。用自然的中文与用户对话，帮助写代码、做设计和整理思路。回答清楚简洁，如实表达不确定性。你不能直接看见用户桌面，也不会自动读取文件。";
                Check(settings.SystemPrompt==exact && !settings.SystemPrompt.Contains("鲸鱼"),"Ebao uses the exact user supplied persona");
                PersonaProfiles.Save(settings,catalog.Find("e-bao"),"自定义E包");PersonaProfiles.Activate(settings,catalog.Find("dafeiyu"));PersonaProfiles.Activate(settings,catalog.Find("e-bao"));Check(settings.SystemPrompt=="自定义E包","per-pet override survives a round trip");
                PersonaProfiles.Reset(settings,catalog.Find("e-bao"));Check(settings.SystemPrompt==exact,"reset restores only the current pet preset");
                var legacy=new PetSettings {SkinId="e-bao",SystemPrompt="你是大肥鱼，蓝白鲸鱼娘。"};PersonaProfiles.Activate(legacy,catalog.Find("e-bao"));Check(legacy.SystemPrompt=="你是大肥鱼，蓝白鲸鱼娘。"&&legacy.PersonaOverrides.ContainsKey("e-bao"),"legacy custom persona remains with its saved owner without inferring ownership from text");PersonaProfiles.Activate(legacy,catalog.Find("dafeiyu"));Check(legacy.SystemPrompt==PersonaProfiles.Dafeiyu&&!legacy.PersonaOverrides.ContainsKey("dafeiyu"),"switching companions keeps the old custom persona on the original companion");
                string atlas=Path.Combine(root,"spritesheet.png");using(var stream=typeof(SkinCatalog).Assembly.GetManifestResourceStream("Dafeiyu.Atlas"))using(var file=File.Create(atlas))stream.CopyTo(file);
                string manifest=Path.Combine(root,"pet.json");File.WriteAllText(manifest,"{\"version\":1,\"name\":\"小墨\",\"atlas\":\"spritesheet.png\",\"system_prompt\":\"你是小墨，沉稳的画师。\",\"description\":\"喜欢画画\"}");var imported=catalog.Import(manifest);PersonaProfiles.Activate(settings,imported);Check(settings.SystemPrompt=="你是小墨，沉稳的画师。" && imported.Description=="喜欢画画","import retains its own preset and description");
                var reloaded=new SkinCatalog(root).Find(imported.Id);Check(reloaded.SystemPrompt==imported.SystemPrompt,"imported persona survives catalog reload");
                var png=catalog.Import(atlas);Check(png.SystemPrompt.Contains(png.Name) && !png.SystemPrompt.Contains("鲸鱼娘"),"plain PNG receives a named generic preset");
                foreach(string action in new[]{"running-right","running-left"}) {
                    var original=CreateAnimation(png);original.Play(action,0);original.Advance(.109);Check(original.Frame==0,"imported run retains .11-second pose duration: "+action);
                    original.Advance(.002);Check(original.Frame==1,"imported run retains .11-second advance: "+action);
                }
                PersonaProfiles.Save(settings,catalog.Find("dafeiyu"),"保留给大肥鱼的设定");var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(settings);var saved=store.Load();Check(saved.PersonaOverrides["dafeiyu"]=="保留给大肥鱼的设定" && saved.PersonaProfileVersion==1,"persona ownership persists with settings");
                Console.WriteLine("Profile tests: "+passed+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL Profile tests: "+e.Message);return 1;}
        }
        static int ActionFrame(SkinDefinition skin,int row,int phase)
        {return skin.ActionFrame(row,phase);}
        static void CheckVoiceProfiles(SkinCatalog catalog,string root)
        {
            var settings=new PetSettings {SkinId="e-bao",VoiceName="clone-ebao-123",LiveVoice="custom-live-ebao"};
            settings.Speech=new ConnectionSettings{BaseUrl="https://api.xiaomimimo.com/v1",Model="mimo-v2.5-tts",Protocol="mimo",EncryptedKey="keep-encrypted-bytes"};
            PersonaProfiles.Activate(settings,catalog.Find("e-bao"));
            PersonaProfiles.Activate(settings,catalog.Find("dafeiyu"));
            Check(settings.VoiceName!="clone-ebao-123" && settings.LiveVoice!="custom-live-ebao","legacy global voice belongs only to the previously active pet");
            Check(HasVoice("speech",settings.SpeechConnection(),settings.VoiceName) && HasVoice("live",settings.Live,settings.LiveVoice),"other pet receives only supported catalog recommendations");
            settings.VoiceName="clone-whale";settings.LiveVoice="custom-live-whale";
            PersonaProfiles.Activate(settings,catalog.Find("marfu"));
            Check(HasVoice("speech",settings.SpeechConnection(),settings.VoiceName) && HasVoice("live",settings.Live,settings.LiveVoice),"third pet receives compatible recommended voices");
            settings.VoiceName="clone-marfu";settings.LiveVoice="custom-live-marfu";
            PersonaProfiles.Activate(settings,catalog.Find("e-bao"));
            Check(settings.VoiceName=="clone-ebao-123" && settings.LiveVoice=="custom-live-ebao","manual IDs return intact when switching pets");
            Check(settings.Speech.EncryptedKey=="keep-encrypted-bytes" && settings.Speech.Model=="mimo-v2.5-tts","pet switch preserves shared service and encrypted credentials");
            var store=new SettingsStore(Path.Combine(root,"voice-settings.json"));store.Save(settings);var loaded=store.Load();
            PersonaProfiles.Activate(loaded,catalog.Find("marfu"));Check(loaded.VoiceName=="clone-marfu" && loaded.LiveVoice=="custom-live-marfu","manual voice IDs persist across restart and pet switching");
            var copy=loaded.Copy();copy.VoiceName="copy-only";PersonaProfiles.Activate(copy,catalog.Find("dafeiyu"));
            PersonaProfiles.Activate(loaded,catalog.Find("dafeiyu"));Check(loaded.VoiceName=="clone-whale" && loaded.LiveVoice=="custom-live-whale","all three pets retain independent manual selections");
            PersonaProfiles.Activate(loaded,catalog.Find("marfu"));Check(loaded.VoiceName=="clone-marfu","copied settings cannot mutate original voice profile");
            loaded.VoiceProfileVersion=0;loaded.VoiceName="stale-global";PersonaProfiles.Activate(loaded,catalog.Find("marfu"));
            Check(loaded.VoiceName=="clone-marfu","migration never overwrites a preexisting pet-specific manual voice");
            var malformed=new PetSettings();malformed.VoiceProfiles["e-bao"]=new PersonaVoiceProfile();malformed.VoiceProfiles["e-bao"].Speech["valid"]="  private-id  ";malformed.VoiceProfiles["e-bao"].Speech["oversize"]=new string('x',513);malformed.VoiceProfiles["null"]=null;
            malformed.Normalize();Check(malformed.VoiceProfiles["e-bao"].Speech["valid"]=="private-id" && !malformed.VoiceProfiles["e-bao"].Speech.ContainsKey("oversize") && !malformed.VoiceProfiles.ContainsKey("null"),"malformed voice entries normalize without discarding valid manual IDs");
        }
        static bool HasVoice(string kind,ConnectionSettings connection,string id)
        {foreach(var item in VoicePresets.For(kind,connection))if(item.Id==id)return true;return false;}
        static int ActionRow(SkinDefinition skin,int row)
        {return skin.ActionRow(row);}
        static bool MirrorsAction(SkinDefinition skin,int row)
        {return skin.MirrorsAction(row);}
        static AnimationPlayer CreateAnimation(SkinDefinition skin)
        {return skin.CreateAnimation();}
        static void CheckOriginalFrames(SkinDefinition skin,SpriteAtlas actual)
        {
            BitmapSource source;
            using(var stream=typeof(SkinCatalog).Assembly.GetManifestResourceStream(skin.Source))
                source=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            int frames=0;
            for(int row=0;row<11;row++) for(int col=0;col<(row<9?AnimationPlayer.Counts[row]:8);col++)
            {
                var crop=new CroppedBitmap(source,new Int32Rect(col*480,row*520,480,520));
                var expected=new FormatConvertedBitmap(crop,PixelFormats.Bgra32,null,0);
                byte[] original=new byte[480*520*4],shown=new byte[original.Length];expected.CopyPixels(original,480*4,0);actual.GetFrame(row,col).CopyPixels(shown,480*4,0);
                for(int i=0;i<original.Length;i++) if(original[i]!=shown[i]) throw new Exception("blended or altered frame: "+skin.Id+" "+row+"/"+col);
                if(!actual.GetFrame(row,col).IsFrozen || actual.AlphaAt(row,col,0,0)!=0) throw new Exception("frame freezing/alpha regression: "+skin.Id);
                frames++;
            }
            Check(frames==73,"all 73 displayed frames exactly match original artwork: "+skin.Id);
        }
        static void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    }
}
