using System;
using System.IO;
using System.Linq;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        ProactiveGreetingSpeech proactiveGreetingSpeech;
        ApiClient proactiveSpeechApi;
        internal bool ProactiveGreetingSpeechActive {get{return proactiveGreetingSpeech!=null&&proactiveGreetingSpeech.IsSpeaking;}}
        bool ProactiveSpeechBusy {get{return closing||LiveActive||DictationActive||DialogueBusy||ConversationSessions.Any(s=>s.Stage==ConversationStage.Speaking);}}
        internal void SpeakProactiveGreeting(string text)
        {
            if(proactiveGreetingSpeech==null)proactiveGreetingSpeech=new ProactiveGreetingSpeech(()=>Settings,()=>new SpeechOutput(proactiveSpeechApi??(proactiveSpeechApi=new ApiClient()),Path.Combine(Store.DirectoryPath,"audio-cache"),Dispatcher),()=>ProactiveSpeechBusy);
            StopNotificationSounds();
            var pending=proactiveGreetingSpeech.SpeakAsync(text);
        }
        internal void StopProactiveGreetingSpeech(){if(proactiveGreetingSpeech!=null)proactiveGreetingSpeech.Stop();}
        internal void RefreshProactiveGreetingSpeech()
        {
            if(!Dispatcher.CheckAccess()){if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(RefreshProactiveGreetingSpeech));return;}
            if(proactiveGreetingSpeech!=null)proactiveGreetingSpeech.Refresh();
        }
        internal void DisposeProactiveGreetingSpeech(){if(proactiveGreetingSpeech!=null)proactiveGreetingSpeech.Dispose();if(proactiveSpeechApi!=null)proactiveSpeechApi.Dispose();}
    }
}
