using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu.Tests
{
    public static class DataManagementTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        public static int Main()
        {
            try{Run().GetAwaiter().GetResult();Console.WriteLine("DataManagementTests: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        static async Task Run()
        {
            var method=typeof(SettingsStore).GetMethod("RestoreDefaultPreferencesAsync");Check(method!=null,"preference transaction API exists");
            string root=Path.GetFullPath(Path.Combine("work","data-management",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            var store=new SettingsStore(Path.Combine(root,"settings.json"));var settings=new PetSettings{ThemeMode="dark",Scale=2,QuietMode=true,OnboardingState="completed",AgentWorkspace="owned-workspace",UiLanguage="ja_JP",AutoUpdate=false};
            settings.Text.ApiKey="owned-text-key";settings.Speech.ApiKey="owned-speech-key";settings.PersonaOverrides["fixture"]="saved persona";settings.VoiceProfiles["fixture"]=new PersonaVoiceProfile{Speech=new Dictionary<string,string>{{"fixture","voice-original"}}};
            var provider=new TextProviderSettings{Id="provider",Name="Saved name",Connection=new ConnectionSettings{BaseUrl="https://fixture.invalid",Model="saved-model"}};provider.Connection.ApiKey="owned-provider-key";settings.TextProviders.Add(provider);settings.ActiveTextProviderId="provider";
            settings.AudioProviders["speech"]=new ServiceProviderSettings{Providers=new System.Collections.Generic.List<TextProviderSettings>{provider.Copy()},ActiveProviderId="provider"};
            settings.AgentSandboxReadDirectories="owned-read-directory";settings.AgentGlobalInstructions="retained instructions";settings.AgentGoal="retained project goal";settings.X=22;settings.Y=33;settings.LiveVoice="retained voice";settings.UpdateMirrorAddress="https://updates.fixture.invalid";settings.UpdateChannel=UpdateChannel.Preview;
            string textCipher=settings.Text.EncryptedKey,speechCipher=settings.Speech.EncryptedKey,providerCipher=provider.Connection.EncryptedKey;
            var whitelist=new HashSet<string>(("ThemeMode Scale Volume Topmost FollowCursor Wander StumbleEnabled AutoSleepEnabled GreetingEnabled AiGreetingEnabled GreetingMinMinutes GreetingMaxMinutes QuietMode HideOnFullscreen ClickThrough LockPosition SaveChatHistory ShowPredictedMessages PlainTextEditor ShowContextUsage SendOnEnter FollowUpBehavior ImageGenerationConcurrency ImageGenerationMotionEnabled ImageGalleryLayout Hotkeys LocalShortcuts").Split(' '));
            var retained=typeof(PetSettings).GetProperties().Where(p=>Attribute.IsDefined(p,typeof(DataMemberAttribute))&&!whitelist.Contains(p.Name)).ToDictionary(p=>p,p=>Serialized(p.PropertyType,p.GetValue(settings,null)));
            var normalizedCopySource=SettingsStore.Snapshot(settings);normalizedCopySource.Normalize();var copied=normalizedCopySource.Copy();var copyDifferences=typeof(PetSettings).GetProperties().Where(p=>Attribute.IsDefined(p,typeof(DataMemberAttribute))).Where(p=>Serialized(p.PropertyType,p.GetValue(copied,null))!=Serialized(p.PropertyType,p.GetValue(normalizedCopySource,null))).Select(p=>p.Name).ToArray();
            Check(copyDifferences.Length==0,"detached settings copy retains every serialized field: "+String.Join(",",copyDifferences));
            var text=settings.Text;var providers=settings.TextProviders;var personas=settings.PersonaOverrides;var gate=new DataMaintenanceGate();
            string relation=Path.Combine(root,"relationship-fixture.bin");File.WriteAllBytes(relation,new byte[]{1,8,5,9});
            var relationships=new RelationshipService(new RelationshipStore(root));Check(relationships.UpdateSettings(false,false,false).Success,"actual relationship settings fixture saved");
            var protectedFiles=new[]{"projects/owned/project-facts.bin","projects/owned/project.json","history/retained.history","preserved-attachments/retained.png"};
            foreach(string relative in protectedFiles){string path=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,new byte[]{3,5,8,13});}
            var protectedBytes=protectedFiles.Select(p=>Path.Combine(root,p)).Concat(Directory.GetFiles(Path.Combine(root,"relationships"))).ToDictionary(p=>p,p=>File.ReadAllBytes(p));
            using(gate.EnterActivity()){bool busy=false;try{await Invoke(method,store,settings,gate);}catch(InvalidOperationException){busy=true;}Check(busy&&settings.ThemeMode=="dark"&&!File.Exists(Path.Combine(root,"settings.json")),"busy refuses reset before write");}
            await Invoke(method,store,settings,gate);
            Check(settings.ThemeMode==new PetSettings().ThemeMode&&settings.Scale==1.25&&!settings.QuietMode,"whitelisted preferences reset");
            Check(ReferenceEquals(text,settings.Text)&&ReferenceEquals(providers,settings.TextProviders)&&ReferenceEquals(personas,settings.PersonaOverrides),"retained runtime references unchanged");
            Check(settings.Text.EncryptedKey==textCipher&&settings.TextProviders[0].Connection.EncryptedKey==providerCipher&&settings.Speech.EncryptedKey==speechCipher&&settings.Text.ApiKey=="owned-text-key","real encrypted credentials unchanged exactly and decryptable");
            string json=File.ReadAllText(Path.Combine(root,"settings.json"));Check(json.Contains("saved persona")&&json.Contains("Saved name")&&json.Contains("owned-workspace"),"retained provider persona workspace durably stored");
            PetSettings persisted;using(var stream=File.OpenRead(Path.Combine(root,"settings.json")))persisted=(PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream);
            foreach(var pair in retained)Check(Serialized(pair.Key.PropertyType,pair.Key.GetValue(persisted,null))==pair.Value&&Serialized(pair.Key.PropertyType,pair.Key.GetValue(settings,null))==pair.Value,"nonwhitelisted field retained in memory and durable snapshot: "+pair.Key.Name);
            foreach(var pair in protectedBytes)Check(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value),"preference reset preserves protected file bytes: "+Path.GetFileName(pair.Key));
            Check(settings.UiLanguage=="ja_JP"&&!settings.AutoUpdate&&settings.OnboardingState=="completed"&&File.ReadAllBytes(relation).SequenceEqual(new byte[]{1,8,5,9}),"language update onboarding and relationship bytes retained");
            settings.ThemeMode="dark";settings.Hotkeys["quick"]="";var keys=settings.Hotkeys;var blocked=Path.Combine(root,"blocked.json");Directory.CreateDirectory(blocked);bool failed=false;
            try{await Invoke(method,new SettingsStore(blocked),settings,gate);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
            Check(failed&&settings.ThemeMode=="dark"&&ReferenceEquals(keys,settings.Hotkeys)&&settings.Hotkeys["quick"]=="","failed atomic save leaves all in-memory preferences unchanged");
            using(await gate.BeginAsync())Check(gate.IsMaintenanceActive,"failure releases maintenance admission");
            for(int index=0;index<24;index++){
                settings.ThemeMode="dark";var reset=Invoke(method,store,settings,gate);var normalSave=Task.Run(()=>{try{store.Save(settings);}catch(IOException){}});await Task.WhenAll(reset,normalSave);
                using(var stream=File.OpenRead(Path.Combine(root,"settings.json")))persisted=(PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream);
                Check(persisted.ThemeMode=="system"&&settings.ThemeMode=="system","concurrent ordinary save cannot collide with reset or restore stale preferences "+index);
            }
        }
        static string Serialized(Type type,object value){using(var stream=new MemoryStream()){new DataContractJsonSerializer(type).WriteObject(stream,value);return Convert.ToBase64String(stream.ToArray());}}
        static Task Invoke(MethodInfo method,SettingsStore store,PetSettings settings,DataMaintenanceGate gate){try{return (Task)method.Invoke(store,new object[]{settings,gate});}catch(TargetInvocationException e){throw e.InnerException;}}
    }
}
