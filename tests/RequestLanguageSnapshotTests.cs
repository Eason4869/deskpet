using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class RequestLanguageSnapshotTests
    {
        static int checks;
        static void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
        public static int Main()
        {try{Run().GetAwaiter().GetResult();Console.WriteLine("RequestLanguageSnapshotTests: "+checks+" passed");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static async Task Run()
        {
            TextResources.ConfigureStartupLanguage("zh_CN");var chinese=TextResources.CaptureLanguage();var english=TextResources.PrepareLanguage("en_US");
            using(LanguageRequestScope.Enter(chinese)){
                await Task.Yield();TextResources.ApplyLanguage(english);
                Check(TextResources.Get("common.cancel")=="Cancel","UI reads use active catalog during old request");
                Check(ReplyLanguagePolicy.Apply("custom persona").Contains(chinese.Get("languages.replyInstruction")),"same task retains instruction across language switch");
                Check(await Task.Run(()=>ReplyLanguagePolicy.Apply("child").Contains(chinese.Get("languages.replyInstruction"))),"child execution inherits task language");
                using(LanguageRequestScope.Enter(english))Check(ReplyLanguagePolicy.Apply("nested").Contains(english.Get("languages.replyInstruction")),"nested scope can select independent language");
                Check(LanguageRequestScope.Capture().Code=="zh_CN","nested scope restores parent");
            }
            Check(ReplyLanguagePolicy.Apply("new task").Contains(english.Get("languages.replyInstruction")),"new task uses current language after old scope ends");
            Check(PersonaProfiles.LocalizeDefaultPrompt("user edited persona",chinese)=="user edited persona","snapshot localization preserves custom persona");
            TextResources.ApplyLanguage(chinese);var api=new CaptureApi();var cfg=new PetSettings{AgentEnabled=false,ReadReplies=false,ShowPredictedMessages=false,SystemPrompt=chinese.Get("personaProfiles.defaults.ebao")};
            cfg.Text.BaseUrl=cfg.Transcription.BaseUrl="https://example.com/v1";cfg.Text.Model="fixture-model";cfg.Transcription.Model="fixture-asr";cfg.Text.ApiKey=cfg.Transcription.ApiKey="fixture";
            using(var session=new ConversationSession(api,new SilentSpeech(),()=>null)){
                session.SelectPersona("e-bao");var inFlight=session.SendVoiceAsync(WaveData.EncodePcm(new byte[400]),cfg,"Ebao");
                Check(!inFlight.IsCompleted,"recognition is genuinely in flight before switch");
                TextResources.ApplyLanguage(english);api.Transcript.SetResult("recognized fixture");await inFlight;
                Check(api.Requests.Any(m=>m[0].Content.Contains(chinese.Get("languages.replyInstruction"))&&m[0].Content.Contains(chinese.Get("personaProfiles.defaults.ebao"))),"actual reply after delayed recognition uses original task language and default persona");
                int prior=api.Requests.Count;await session.SendTextAsync("next task",cfg,"Ebao");
                Check(api.Requests.Skip(prior).Any(m=>m[0].Content.Contains(english.Get("languages.replyInstruction"))&&m[0].Content.Contains(english.Get("personaProfiles.defaults.ebao"))),"actual subsequent reply uses new language without mutating persona settings");
                Check(cfg.SystemPrompt==chinese.Get("personaProfiles.defaults.ebao"),"persisted default source is unchanged");
            }
            TextResources.ApplyLanguage(chinese);
            var sockets=new List<CaptureSocket>();
            using(var live=new GeminiLiveSession(()=>{var socket=new CaptureSocket();sockets.Add(socket);return socket;},()=>new SilentMicrophone(),()=>new SilentOutput())){
                var liveSettings=new PetSettings();liveSettings.Live.BaseUrl="wss://example.com/live";liveSettings.Live.Model="fixture-live-model";liveSettings.Live.ApiKey="fixture";
                await live.StartAsync(liveSettings,"custom live persona");var original=sockets[0];
                TextResources.ApplyLanguage(english);
                Check(live.IsActive&&sockets.Count==1&&original.Prompt.Contains(chinese.Get("languages.replyInstruction")),"changing UI does not reconnect or rewrite an established Live setup");
                Check(live.Status==english.Get("geminiLiveSession.run.setStatus.alternativeC"),"active Live status immediately follows UI language");
                await live.StopAsync();await live.StartAsync(liveSettings,"custom live persona");
                Check(sockets.Count==2&&sockets[1].Prompt.Contains(english.Get("languages.replyInstruction")),"next Live connection uses new language");
                await live.StopAsync();
            }
            TextResources.ApplyLanguage(chinese);
        }
        sealed class CaptureApi:IConversationApi
        {
            internal readonly TaskCompletionSource<string> Transcript=new TaskCompletionSource<string>();
            readonly List<ChatMessage[]> requests=new List<ChatMessage[]>();
            internal List<ChatMessage[]> Requests {get{lock(requests)return new List<ChatMessage[]>(requests);}}
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){lock(requests)requests.Add(messages.ToArray());return Task.FromResult("Fixture reply");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wav,CancellationToken cancel){return Transcript.Task;}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}
            public void Dispose(){}
        }
        sealed class CaptureSocket:ILiveSocket
        {
            internal string Setup;int received;
            internal string Prompt {get{var root=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(Setup) as Dictionary<string,object>;var setup=(Dictionary<string,object>)root["setup"];var instruction=(Dictionary<string,object>)setup["systemInstruction"];return (string)((Dictionary<string,object>)((object[])instruction["parts"])[0])["text"];}}
            public Task ConnectAsync(ConnectionSettings settings,CancellationToken token){return Task.FromResult(0);}
            public Task SendTextAsync(string text,CancellationToken token){if(Setup==null)Setup=text;return Task.FromResult(0);}
            public async Task<string> ReceiveTextAsync(CancellationToken token){if(Interlocked.Increment(ref received)==1)return "{\"setupComplete\":{}}";await Task.Delay(Timeout.Infinite,token);return null;}
            public void Abort(){}public void Dispose(){}
        }
        sealed class SilentMicrophone:IPcmMicrophone
        {public event Action<byte[]> PcmAvailable {add{}remove{}}public event Action<string> Failed {add{}remove{}}public void Start(){}public void Dispose(){}}
        sealed class SilentOutput:ILiveAudioOutput
        {public void Enqueue(byte[] bytes,int rate){}public void Interrupt(){}public void SetVolume(int volume){}public void Dispose(){}}
        sealed class SilentSpeech:IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}
            public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}
        }
    }
}
