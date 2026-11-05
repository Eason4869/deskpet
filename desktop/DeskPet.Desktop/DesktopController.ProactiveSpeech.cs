using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    private ProactiveGreetingSpeech? proactiveGreetingSpeech;
    internal bool ProactiveGreetingSpeechActive=>proactiveGreetingSpeech?.IsSpeaking==true;
    private bool ProactiveSpeechBusy=>exiting||Live.IsActive||Dictation.IsBusy||Session.IsBusy||ProactiveBusy||WorkBindings.Manager.Busy||
        new[]{Session}.Concat(WorkBindings.Manager.OpenWorkspaces.Select(w=>w.Session)).Concat(ProactiveBindingsSnapshot().Select(b=>b.Session)).Any(s=>s.Stage==ConversationStage.Speaking);
    internal void SpeakProactiveGreeting(string text)
    {
        proactiveGreetingSpeech??=new ProactiveGreetingSpeech(()=>Settings,()=>runtime.CreateSpeech(Api),()=>ProactiveSpeechBusy);
        StopNotificationSounds();
        _=proactiveGreetingSpeech.SpeakAsync(text);
    }
    internal void StopProactiveGreetingSpeech()=>proactiveGreetingSpeech?.Stop();
    internal void RefreshProactiveGreetingSpeech()
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(RefreshProactiveGreetingSpeech);return;}
        proactiveGreetingSpeech?.Refresh();
    }
    internal void DisposeProactiveGreetingSpeech()=>proactiveGreetingSpeech?.Dispose();
}
