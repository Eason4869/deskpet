using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Dafeiyu.Tests
{
    public static class PortableTests
    {
        static int contractChecks;
        public static int Main(string[] args)
        {
            if(args.Length==4&&args[0]=="--system-language-fixture")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return SystemLanguageTests.RunFixture(args[1],args[2],args[3]);}
            if(args.Length==1&&args[0]=="--speech-language-only")return SpeechLanguagePolicyTests.RunAll();
            if(args.Length==1&&args[0]=="--system-language-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return SystemLanguageTests.RunAll();}
            if(args.Length==2&&args[0]=="--reply-language-fixture")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ReplyLanguagePolicyTests.RunFixture(args[1]);}
            if(args.Length==1&&args[0]=="--reply-language-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ReplyLanguagePolicyTests.RunAll();}
            if(args.Length==3&&args[0]=="--language-startup-fixture")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return LanguagePackTests.RunStartupFixture(args[1],args[2]);}
            if(args.Length>0&&args[0]=="--agent-job-fixture")return AgentJobProcessTests.RunFixture(args);
            if(args.Length==1&&args[0]=="--agent-jobs-resources-only")return AgentJobProcessTests.RunResourceChecks();
            if(args.Length==1 && args[0]=="--holiday-assets-only")return HolidayAssetTests.Main();
            if(args.Length==1 && args[0]=="--official-legacy-only")return OfficialCompanionLegacyTests.Main();
            if(args.Length==1 && args[0]=="--official-identity-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return OfficialCompanionIdentityTests.Main();}
            if(args.Length==1 && args[0]=="--holiday-source-only")return HolidaySourceBindingTests.Main();
            if(args.Length==1 && args[0]=="--official-companion-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return OfficialCompanionTests.Main();}
            if(args.Length==1 && args[0]=="--image-task-center-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageTaskCenterLifecycleTests.Main();}
            if(args.Length==1 && args[0]=="--image-management-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageGalleryManagementTests.Main();}
            if(args.Length==1 && args[0]=="--image-backend-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageGenerationBackendTests.Main();}
            if(args.Length==1 && args[0]=="--image-layout-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageGalleryLayoutTests.Main();}
            if(args.Length==1 && args[0]=="--image-gallery-export-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageGalleryExportTests.Main();}
            if(args.Length==1 && args[0]=="--image-protocols-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageProviderProtocolTests.Main();}
            if(args.Length==1 && args[0]=="--image-task-store-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageGenerationTaskStoreTests.Main();}
            if(args.Length==1 && args[0]=="--history-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return HistoryTests.Main();}
            if(args.Length==1 && args[0]=="--image-integration-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageGenerationIntegrationTests.Main();}
            if(args.Length==1 && args[0]=="--image-group-only")return GeneratedImageGroupTests.Main();
            if(args.Length==1 && args[0]=="--image-motion-only")return ImageGenerationMotionTests.Main();
            if(args.Length==1 && args[0]=="--image-tasks-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return ImageGenerationTaskManagerTests.Main();}
            if(args.Length==1 && args[0]=="--language-packs-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return LanguagePackTests.RunAll();}
            if(args.Length==1 && args[0]=="--localization-only")return LocalizationTests.Main();
            if(args.Length==1 && args[0]=="--agent-job-tools-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return AgentJobToolTests.Main() | AgentJobIntegrationTests.Main();}
            if(args.Length==1 && args[0]=="--agent-job-session-only")using(var protector=new TestProtector()){SecretProtection.Configure(protector);return AgentJobSessionTests.Main();}
            if(args.Length==1 && args[0]=="--agent-jobs-only")return RunAgentJobLogic();
            if(args.Length==1 && args[0]=="--agent-job-integration-only")return RunAgentJobIntegration();
            if(args.Length==1 && args[0]=="--agent-job-contract-only")return AgentJobContractTests.Main();
            if(args.Length==1 && args[0]=="--agent-job-store-only")return AgentJobStoreTests.Main();
            if(args.Length==1 && args[0]=="--agent-jobs-native-only")return RunAgentJobNative();
            if(args.Length==1 && args[0]=="--agent-job-manager-only")return AgentJobManagerTests.Main();
            if(args.Length==1 && args[0]=="--agent-job-log-only")return AgentJobLogTests.RunAll();
            string report=null;
            if(args.Length!=0) {
                if(args.Length!=2 || args[0]!="--report-json"){Console.Error.WriteLine("Usage: DeskPet.Tests [--report-json <path> | --agent-jobs-only | --agent-job-integration-only | --agent-jobs-native-only | --agent-job-tools-only | --agent-job-session-only | --agent-job-contract-only | --agent-job-store-only | --agent-job-manager-only | --agent-job-log-only | --agent-jobs-resources-only | --official-companion-only | --official-identity-only | --official-legacy-only | --holiday-source-only | --image-task-center-only | --image-management-only | --image-gallery-export-only | --image-group-only | --image-motion-only | --image-integration-only | --image-tasks-only | --image-task-store-only | --history-only | --image-backend-only | --image-layout-only | --image-protocols-only]");return 2;}
                report=Path.GetFullPath(args[1]);
            }
            var original=Console.Out;using(var captured=new StringWriter())
            {
                int result;
                using(var output=new TeeWriter(original,captured))
                {
                    Console.SetOut(output);
                    try{result=Run();}finally{Console.SetOut(original);}
                }
                if(report!=null) {
                    var lines=captured.ToString().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
                    var summaries=lines.Select(line=>System.Text.RegularExpressions.Regex.Match(line,"^(?:.* tests|[A-Za-z][A-Za-z0-9]*Tests|AI greetings): ([0-9]+) passed",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Where(match=>match.Success).ToArray();
                    Directory.CreateDirectory(Path.GetDirectoryName(report));
                    File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new{
                        schemaVersion=1,success=result==0,exitCode=result,
                        passedChecks=contractChecks+summaries.Sum(match=>Int32.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)),
                        failures=lines.Where(line=>line.StartsWith("FAIL ",StringComparison.Ordinal)).ToArray(),
                        framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                        operatingSystem=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
                    },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                }
                return result;
            }
        }
        static int RunAgentJobLogic()
        {
            using(var protector=new TestProtector())
            {
                SecretProtection.Configure(protector);
                return AgentJobContractTests.Main() | AgentJobStoreTests.Main() | AgentJobLogTests.RunAll() | AgentJobManagerTests.Main() | AgentJobToolTests.Main();
            }
        }
        static int RunAgentJobIntegration()
        {
            // The fake process suite still validates real Windows directory identities.
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.Error.WriteLine("Windows workspace background job integration is unsupported on this platform");return 2;}
            using(var protector=new TestProtector())
            {
                SecretProtection.Configure(protector);
                return AgentJobIntegrationTests.Main();
            }
        }
        static int RunAgentJobNative()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.Error.WriteLine("Windows native background jobs are unsupported on this platform");return 2;}
            using(var protector=new TestProtector())
            {
                SecretProtection.Configure(protector);
                return AgentJobProcessTests.RunAll() | AgentJobSandboxTests.Main() | AgentJobSessionTests.Main();
            }
        }
        static int Run()
        {
            contractChecks=0;
            try
            {
                // No production fallback may silently persist plaintext before platform bootstrap.
                bool refused=false;
                try { new ConnectionSettings().ApiKey="must-not-persist"; }
                catch(InvalidOperationException) { refused=true; }
                Check(refused,"portable secrets fail closed without a configured platform protector");
                using(var protector=new TestProtector())
                {
                    SecretProtection.Configure(protector);
                    Secrets(); JsonShapes(); Rules(); Personas(); CorruptQueue(); PortableBoundary();
                    int result=0;
                    foreach(var test in new Func<int>[] { SpeechLanguagePolicyTests.RunAll, SystemLanguageTests.RunAll, LanguagePackTests.RunAll, ReplyLanguagePolicyTests.RunAll, AgentJobToolTests.Main, AgentJobManagerTests.Main, AgentJobLogTests.RunAll, AgentJobStoreTests.Main, AgentJobContractTests.Main, LocalizationTests.Main, OfficialCompanionLegacyTests.Main, OfficialCompanionIdentityTests.Main, HolidaySourceBindingTests.Main, OfficialCompanionTests.Main, HolidayStateTests.Main, HolidayCalendarTests.Main, HolidayRuntimeTests.Main, HolidayHoverTests.Main, HolidayAssetTests.Main, HolidayGreetingTests.Main, McpConnectionTests.PortableRegression,ApiTests.Main, StreamingTests.Main, ProxyTests.Main,
                        ConversationTests.Main, HistoryTests.Main, ConversationWorkspaceManagerTests.Main, ImageTaskCenterLifecycleTests.Main, ImageGenerationTaskStoreTests.Main, ImageGenerationIntegrationTests.Main, ImageGenerationTaskManagerTests.Main,
                        ImageGenerationMotionTests.Main, GeneratedImageGroupTests.Main, ImageGalleryManagementTests.Main, ImageGalleryExportTests.Main,
                        QueueTests.Main, DictationTests.Main, ImageGenerationBackendTests.Main, ImageGalleryLayoutTests.Main, ImageProviderProtocolTests.Main,
                        AgentContextPolicyTests.Main,AgentRunRecoveryTests.Main,AgentTaskMemoryTests.Main,AgentIsolationTests.Main,AgentApprovalTests.Main,ProviderTests.Main, LiveTests.Main, GreetingTests.Main, AiGreetingTests.Main, RecorderFailureTests.Main, OnboardingUpgradeTests.Main })
                    { SecretProtection.Configure(protector); result |= test(); }
                    // Windows identity checks belong in the Windows default regression;
                    // macOS uses AgentJobToolTests to verify the unsupported-platform gate.
                    SecretProtection.Configure(protector);
                    if(Environment.OSVersion.Platform==PlatformID.Win32NT)result |= AgentJobIntegrationTests.Main();
                    return result;
                }
            }
            catch(Exception e) { Console.WriteLine("FAIL portable contracts: "+e);return 1; }
        }
        static void Secrets()
        {
            string folder=Path.Combine(AppContext.BaseDirectory,"portable-secrets",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,"settings.json");
            var settings=new PetSettings();settings.Text.ApiKey="private-portable-key";
            var store=new SettingsStore(path);store.Save(settings);
            Check(!File.ReadAllText(path).Contains("private-portable-key"),"saved settings do not contain the API key");
            Check(store.Load().Text.ApiKey=="private-portable-key","platform-protected API key survives restart");
            var invalid=settings.Copy();invalid.Text.EncryptedKey=Convert.ToBase64String(new byte[48]);
            foreach(var provider in invalid.TextProviders)provider.Connection.EncryptedKey=invalid.Text.EncryptedKey;
            store.Save(invalid);Check(store.Load().Text.ApiKey=="" && store.Warning.Length>0,"foreign or damaged encrypted keys warn without plaintext fallback");
            File.WriteAllText(path,"{broken");var recovered=store.Load();
            Check(recovered!=null && store.Warning.Length>0 && Directory.GetFiles(folder,"*.invalid-*").Length==1,"damaged configuration is preserved before defaults are restored");
        }
        static void JsonShapes()
        {
            var json=new JavaScriptSerializer();
            var value=(Dictionary<string,object>)json.DeserializeObject("{\"a\":[{\"delta\":\"你好🌙\"},null,true,42],\"rate\":0.25}");
            var array=(object[])value["a"];
            Check(((Dictionary<string,object>)array[0])["delta"].Equals("你好🌙") && array[1]==null && array[2].Equals(true) && Convert.ToInt32(array[3])==42,"portable JSON retains nested dictionary, object array, Unicode and number shapes");
            bool rejected=false;try{new JavaScriptSerializer{MaxJsonLength=2}.DeserializeObject("[123]");}catch(ArgumentException){rejected=true;}
            Check(rejected,"JSON length guard runs before parsing");
        }
        static void Rules()
        {
            var motion=new DropMotion();motion.Start(100,208,900);double prior=motion.Position;
            for(int i=0;i<10;i++){motion.Advance(.03);Check(motion.Position>=prior,"drop never jumps upward");prior=motion.Position;}
            Check(!motion.Active && Math.Abs(motion.Position-124)<.001,"paired drop settles at its physical target");
            motion.Start(850,208,900);Check(!motion.Active && motion.Position==850,"screen bottom does not force an upward drop");
            int rolls=0;var reaction=new DropReaction(()=>{rolls++;return .1;});
            Check(!reaction.TryStumble(false,true,true,0) && rolls==0,"clicks do not roll a drag reaction");
            Check(reaction.TryStumble(true,true,true,0) && !reaction.TryStumble(true,true,true,1) && rolls==1,"completed drag rolls once and cooldown survives");
            var sleep=new IdleSleepSchedule(0,()=>0);
            Check(!sleep.TrySleep(500,500,false,true) && sleep.TrySleep(500,500,true,true),"busy state blocks automatic sleep without consuming eligibility");
            sleep.Touch(500);Check(!sleep.TrySleep(501,1000,true,true),"recent interaction prevents sleep");
            var animation=new AnimationPlayer();animation.Play("jumping",1);animation.Advance(.9);Check(animation.Frame==4,"timed jump holds landing pose");
            animation.Advance(.2);Check(animation.State=="idle","timed animation returns to idle");
        }
        static void Personas()
        {
            var settings=new PetSettings();PersonaProfiles.Activate(settings,"e-bao",PersonaProfiles.Ebao);
            PersonaProfiles.Save(settings,"e-bao",PersonaProfiles.Ebao,"only e-bao");
            PersonaProfiles.Activate(settings,"marfu",PersonaProfiles.Marfu);
            Check(settings.SystemPrompt==PersonaProfiles.Marfu,"switching companion loads its own persona");
            PersonaProfiles.Activate(settings,"e-bao",PersonaProfiles.Ebao);
            Check(settings.SystemPrompt=="only e-bao","companion persona persists independently of UI skin objects");
        }
        static void CorruptQueue()
        {
            string folder=Path.Combine(AppContext.BaseDirectory,"portable-queue",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,"queue.json");
            const string damaged="{\"Version\":1,\"Payload\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}";
            File.WriteAllText(path,damaged);
            using(var session=new ConversationSession(new ApiClient(),new UnusedSpeech(),()=>null))
            {
                // These operations need no transport; no network or speech can run.
                using(var queue=new ConversationQueue(session,()=>new PetSettings{Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}},()=>"fixture",path))
                {
                    Check(queue.Notice.Length>0 && queue.HasPending,"damaged queue reports unreadable pending data");
                    bool blocked=false;try{queue.Enqueue("replacement");}catch(InvalidOperationException){blocked=true;}
                    Check(blocked && File.ReadAllText(path)==damaged,"damaged encrypted queue cannot be overwritten by a new request");
                }
            }
        }
        static void PortableBoundary()
        {
            var references=typeof(PetSettings).Assembly.GetReferencedAssemblies().Select(a=>a.Name).ToArray();
            Check(!references.Any(name=>name=="PresentationFramework" || name=="PresentationCore" || name=="WindowsBase" || name=="System.Windows.Forms" || name=="System.Speech" || name=="System.Security.Cryptography.ProtectedData"),"portable business assembly has no Windows UI, speech or DPAPI references");
        }
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);contractChecks++;Console.WriteLine("PASS "+message);}
        sealed class TeeWriter:TextWriter
        {
            readonly TextWriter original,captured;
            public TeeWriter(TextWriter original,TextWriter captured){this.original=original;this.captured=captured;}
            public override Encoding Encoding{get{return original.Encoding;}}
            public override void Write(char value){original.Write(value);captured.Write(value);}
            public override void Write(string value){original.Write(value);captured.Write(value);}
            public override void WriteLine(string value){original.WriteLine(value);captured.WriteLine(value);}
        }
        sealed class UnusedSpeech : IReplySpeech
        {
            public System.Threading.Tasks.Task SpeakAsync(string text,PetSettings settings,System.Threading.CancellationToken token){throw new InvalidOperationException("The corrupted queue must not send or speak.");}
            public void SetVolume(int value){}public void Stop(){}public void Dispose(){}
        }
        // Authenticated encryption is test-only; production must register Keychain-backed protection.
        sealed class TestProtector : ISecretProtector,IDisposable
        {
            readonly byte[] key=RandomNumberGenerator.GetBytes(32);
            public byte[] Protect(byte[] plain){byte[] nonce=RandomNumberGenerator.GetBytes(12),cipher=new byte[plain.Length],tag=new byte[16];using(var aes=new AesGcm(key,16))aes.Encrypt(nonce,plain,cipher,tag);return nonce.Concat(tag).Concat(cipher).ToArray();}
            public byte[] Unprotect(byte[] data){if(data.Length<28)throw new CryptographicException();byte[] plain=new byte[data.Length-28];using(var aes=new AesGcm(key,16))aes.Decrypt(data.AsSpan(0,12),data.AsSpan(28),data.AsSpan(12,16),plain);return plain;}
            public void Dispose(){CryptographicOperations.ZeroMemory(key);}
        }
    }
}
