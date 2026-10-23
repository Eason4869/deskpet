using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class ReplyLanguagePolicyTests
    {
        static readonly string[] Codes={"zh_CN","zh_TW","en_US","ja_JP","ko_KR"};
        static int checks;
        static void Check(bool yes,string reason){if(!yes)throw new Exception(reason);checks++;}
        static TextCatalog Catalog(string code){return (TextCatalog)typeof(TextResources).GetMethod("CatalogFor",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{code});}
        public static int Main(string[] args){return args.Length==2&&args[0]=="--reply-language-fixture"?RunFixture(args[1]):args.Length==0?RunAll():2;}
        public static int RunAll()
        {
            try
            {
                foreach(string code in Codes)
                {
                    string assembly=typeof(ReplyLanguagePolicyTests).Assembly.Location;
                    var start=new ProcessStartInfo{UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
                    if(assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)){using(var current=Process.GetCurrentProcess())start.FileName=current.MainModule.FileName;start.Arguments="\""+assembly+"\" ";}else start.FileName=assembly;
                    start.Arguments+="--reply-language-fixture "+code;
                    using(var process=Process.Start(start))
                    {
                        try{if(!process.WaitForExit(20000))throw new Exception("Reply language fixture timed out: "+code);string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();if(process.ExitCode!=0)throw new Exception(code+": "+output+error);Console.WriteLine(output.Trim());}
                        finally{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}
                    }
                }
                return 0;
            }
            catch(Exception error){Console.WriteLine("FAIL ReplyLanguagePolicy: "+error);return 1;}
        }
        public static int RunFixture(string code)
        {
            try
            {
                Console.OutputEncoding=new UTF8Encoding(false);if(!Codes.Contains(code))return 2;var earlyProfile=OfficialCompanionProfiles.Find("e-bao");string earlyPrompt=earlyProfile.SystemPrompt;TextResources.ConfigureStartupLanguage(code);checks=0;
                var selected=Catalog(code);string instruction=selected.Get("languages.replyInstruction");
                Check(earlyProfile.SystemPrompt==selected.Get("personaProfiles.defaults.ebao")&&earlyProfile.Description==selected.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile"),"official metadata initialized before language selection does not freeze Chinese prompts or descriptions");
                Check(ReplyLanguagePolicy.Apply("custom persona").EndsWith(instruction+"\n[/DeskPet reply language]",StringComparison.Ordinal),"request requires the active UI language");
                string once=ReplyLanguagePolicy.Apply("custom persona");Check(ReplyLanguagePolicy.Apply(once)==once,"language policy is idempotent");
                string custom="My edited persona. 鲸鱼 and 大肥鱼 are examples, not ownership metadata.";
                Check(PersonaProfiles.LocalizeDefaultPrompt(custom)==custom,"custom persona is not rewritten based on Chinese substrings");
                foreach(string source in Codes)foreach(string id in new[]{"ebao","dafeiyu","marfu","xiaobei","gugugaga","agent"})
                {
                    string key="personaProfiles.defaults."+id;
                    Check(PersonaProfiles.LocalizeDefaultPrompt(Catalog(source).Get(key))==selected.Get(key),"exact legacy/localized default maps to active language: "+source+" "+id);
                }
                string originalName="Custom Name";foreach(string source in Codes)
                {
                    string generic=Catalog(source).Get("personaProfiles.defaults.generic").Replace("{pet_name}",originalName);
                    Check(PersonaProfiles.LocalizeDefaultPrompt(generic)==selected.Get("personaProfiles.defaults.generic").Replace("{pet_name}",originalName),"generic template changes language without changing custom companion name");
                }
                var settings=new PetSettings{PersonaProfileVersion=1,SkinId="e-bao",SystemPrompt=custom,ReadReplies=false,AgentEnabled=false};
                settings.UiLanguage=code=="ko_KR"?"en_US":"ko_KR";
                settings.PersonaOverrides["e-bao"]=custom;PersonaProfiles.Activate(settings,"e-bao",Catalog("zh_CN").Get("personaProfiles.defaults.ebao"));
                Check(settings.SystemPrompt==custom&&settings.PersonaOverrides["e-bao"]==custom,"activation preserves a custom override exactly");
                Check(ReplyLanguagePolicy.Apply(settings.SystemPrompt).Contains(instruction),"pending saved selection cannot affect current outgoing policy");
                PersonaProfiles.Reset(settings,"e-bao",Catalog("zh_CN").Get("personaProfiles.defaults.ebao"));
                Check(settings.SystemPrompt==selected.Get("personaProfiles.defaults.ebao")&&!settings.PersonaOverrides.ContainsKey("e-bao"),"reset uses localized default and clears only the selected override");
                var old=new PetSettings{PersonaProfileVersion=0,SkinId="e-bao",SystemPrompt=Catalog("zh_CN").Get("personaProfiles.defaults.ebao")};
                PersonaProfiles.Activate(old,"e-bao",Catalog("zh_CN").Get("personaProfiles.defaults.ebao"));
                Check(old.SystemPrompt==selected.Get("personaProfiles.defaults.ebao")&&!old.PersonaOverrides.ContainsKey("e-bao"),"legacy unchanged default is not preserved as a custom override");
                var edited=new PetSettings{PersonaProfileVersion=0,SkinId="e-bao",SystemPrompt=custom};PersonaProfiles.Activate(edited,"e-bao",Catalog("zh_CN").Get("personaProfiles.defaults.ebao"));
                Check(edited.SystemPrompt==custom&&edited.PersonaOverrides["e-bao"]==custom&&!edited.PersonaOverrides.ContainsKey("dafeiyu"),"legacy custom persona stays with its saved owner");
                VerifyOutgoingRequests(code,instruction,custom).GetAwaiter().GetResult();
                Console.WriteLine("PASS ReplyLanguagePolicy "+code+": "+checks+" checks");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static async Task VerifyOutgoingRequests(string code,string instruction,string custom)
        {
            foreach(bool agent in new[]{false,true})
            {
                var api=new CaptureApi();var settings=new PetSettings{AgentEnabled=agent,ReadReplies=false,SystemPrompt=custom,AgentSystemPrompt=custom,ShowPredictedMessages=false};settings.Text.BaseUrl="https://example.com/v1";settings.Text.Model="fixture-model";settings.Text.ApiKey="fixture";
                using(var session=new ConversationSession(api,new SilentSpeech(),()=>null))
                {session.SelectPersona("e-bao");await session.SendTextAsync("Test request",settings,"Ebao");Check(api.Messages[0].Role=="system"&&api.Messages[0].Content.Contains(custom)&&api.Messages[0].Content.Contains(instruction),"actual plain/Agent request retains persona and includes language requirement: "+agent);Check(settings.SystemPrompt==custom&&settings.AgentSystemPrompt==custom,"outgoing policy does not mutate saved prompts");}
            }
            var greetingApi=new CaptureApi();var greetingSettings=new PetSettings{AiGreetingEnabled=true,SystemPrompt=custom};greetingSettings.Text.BaseUrl="https://example.com/v1";greetingSettings.Text.Model="fixture-model";greetingSettings.Text.ApiKey="fixture";
            var greetings=new GreetingService(()=>greetingApi,TimeSpan.FromSeconds(2));await greetings.GenerateAsync(greetingSettings,"e-bao","Ebao",new List<ChatMessage>(),new DateTime(2026,10,24,12,0,0),0,new List<string>(),CancellationToken.None);
            Check(greetingApi.Messages!=null&&greetingApi.Messages[0].Content.Contains(custom)&&greetingApi.Messages[0].Content.Contains(instruction),"actual greeting request retains custom persona and language requirement");
            var socket=new CaptureLiveSocket();var liveSettings=new PetSettings();liveSettings.Live.BaseUrl="wss://example.com/live";liveSettings.Live.Model="fixture-live-model";liveSettings.Live.ApiKey="fixture";
            using(var live=new GeminiLiveSession(()=>socket,()=>new SilentMicrophone(),()=>new SilentOutput()))
            {
                await live.StartAsync(liveSettings,custom);
                var body=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(socket.Setup);var setup=(Dictionary<string,object>)body["setup"];var system=(Dictionary<string,object>)setup["systemInstruction"];var part=(Dictionary<string,object>)((object[])system["parts"])[0];string text=(string)part["text"];
                Check(text.Contains(custom)&&text.Contains(instruction),"actual Live setup retains custom persona and requires active reply language");await live.StopAsync();
            }
            await VerifyNativeAgentRequests(instruction,custom);
        }
        static async Task VerifyNativeAgentRequests(string instruction,string custom)
        {
            foreach(bool anthropic in new[]{false,true})foreach(bool withWorkspace in new[]{false,true})
            {
                if(withWorkspace&&Environment.OSVersion.Platform!=PlatformID.Win32NT)continue;
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reply-language-fixtures",Guid.NewGuid().ToString("N"));
                AgentWorkspace workspace=null;
                try
                {
                    if(withWorkspace){Directory.CreateDirectory(root);workspace=new AgentWorkspace(root);workspace.BeginTask(new AgentTaskState{Workspace=root,Goal="Read-only language fixture"});}
                    using(var server=new NativeProtocolFixture(anthropic))using(var api=new ApiClient(TimeSpan.FromSeconds(5)))
                    {
                        var field=typeof(ApiClient).GetField("http",BindingFlags.Instance|BindingFlags.NonPublic);((HttpClient)field.GetValue(api)).Dispose();field.SetValue(api,new HttpClient(server));
                        var connection=new ConnectionSettings{BaseUrl="https://fixture.invalid/v1",Model="fixture-model",ApiKey="fixture",Protocol=anthropic?"anthropic":"openai",UseProxy=false};
                        var messages=new[]{new ChatMessage("system",ReplyLanguagePolicy.Apply(custom)),new ChatMessage("user","A fixture request")};
                        string reply=await api.ChatAgentAsync(connection,messages,workspace,null,null,null,false,CancellationToken.None);
                        Check(reply=="Fixture reply","actual native Agent reply parses: "+anthropic+" "+withWorkspace);
                        var body=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(server.Body);string system;
                        var wire=((object[])body["messages"]).Cast<Dictionary<string,object>>().ToArray();
                        if(anthropic){system=(string)body["system"];Check(wire.All(m=>(string)m["role"]!="system"),"Anthropic keeps system instructions in its native field");}
                        else system=String.Join("\n",wire.Where(m=>(string)m["role"]=="system").Select(m=>(string)m["content"]));
                        Check(system.Contains(custom)&&system.Contains(instruction),"native assembled system preserves persona and active language");
                        Check(system.Split(new[]{"[DeskPet reply language]"},StringSplitOptions.None).Length==2,"native assembled system has exactly one language policy block");
                        Check(system.EndsWith("[/DeskPet reply language]",StringComparison.Ordinal),"native language policy follows all assembled system instructions");
                        Check((string)body["model"]=="fixture-model"&&!(bool)body["stream"],"native machine protocol fields remain unchanged");
                    }
                }
                finally
                {
                    if(workspace!=null)workspace.Dispose();
                    string parent=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reply-language-fixtures"))+Path.DirectorySeparatorChar;
                    if(Directory.Exists(root)){if(!Path.GetFullPath(root).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected fixture cleanup path");AgentInstructions.RejectLinks(root);Directory.Delete(root,true);}
                }
            }
        }
        // Exercise the real native serializer/parser without opening a network connection.
        sealed class NativeProtocolFixture:HttpMessageHandler
        {
            readonly bool anthropic;public string Body;
            public NativeProtocolFixture(bool anthropic){this.anthropic=anthropic;}
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                Body=await request.Content.ReadAsStringAsync();
                string json=anthropic?"{\"content\":[{\"type\":\"text\",\"text\":\"Fixture reply\"}],\"stop_reason\":\"end_turn\"}":"{\"choices\":[{\"message\":{\"content\":\"Fixture reply\"},\"finish_reason\":\"stop\"}]}";
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
            }
        }
        sealed class CaptureApi:IConversationApi
        {
            public IList<ChatMessage> Messages;
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Messages=messages;return Task.FromResult("Fixture reply");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wav,CancellationToken cancel){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}
            public void Dispose(){}
        }
        sealed class SilentSpeech:IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}
            public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}
        }
        sealed class CaptureLiveSocket:ILiveSocket
        {
            public string Setup;int received;
            public Task ConnectAsync(ConnectionSettings settings,CancellationToken token){return Task.FromResult(0);}
            public Task SendTextAsync(string text,CancellationToken token){if(Setup==null)Setup=text;return Task.FromResult(0);}
            public async Task<string> ReceiveTextAsync(CancellationToken token){if(Interlocked.Increment(ref received)==1)return "{\"setupComplete\":{}}";await Task.Delay(Timeout.Infinite,token);return null;}
            public void Abort(){}public void Dispose(){}
        }
        sealed class SilentMicrophone:IPcmMicrophone
        {public event Action<byte[]> PcmAvailable;public event Action<string> Failed;public void Start(){}public void Dispose(){}}
        sealed class SilentOutput:ILiveAudioOutput
        {public void Enqueue(byte[] bytes,int rate){}public void Interrupt(){}public void SetVolume(int volume){}public void Dispose(){}}
    }
}
