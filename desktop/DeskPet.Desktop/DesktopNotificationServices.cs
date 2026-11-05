using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    NotificationSoundPlayer? notificationSounds;
    TrayNotificationWindow? trayNotification;
    readonly ConditionalWeakTable<ConversationSession,object> notificationSessions=new();
    internal void StopNotificationSounds()=>notificationSounds?.Stop();
    internal bool NotificationAudioBusy=>ProactiveGreetingSpeechActive||Live.IsActive||Dictation.IsBusy||new[]{Session}.Concat(WorkBindings.Manager.OpenWorkspaces.Select(w=>w.Session)).Concat(ProactiveBindingsSnapshot().Select(b=>b.Session)).Any(s=>s.Stage==ConversationStage.Speaking);
    void RefreshNotificationSounds(){if(Settings.QuietMode||!Settings.NotificationSoundsEnabled||Settings.Volume<=0||NotificationAudioBusy)StopNotificationSounds();}
    internal void PlayNotificationCue(NotificationCue cue,string eventId)
    {
        if(exiting||!runtime.ShowTray)return;
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>PlayNotificationCue(cue,eventId));return;}
        notificationSounds??=new NotificationSoundPlayer(()=>Settings,runtime.CreateLiveOutput,()=>NotificationAudioBusy);
        notificationSounds.TryPlay(cue,eventId);
    }
    TrayNotificationWindow? ShowTrayNotification(Func<string> title,Func<string> message,Func<string> actionLabel,Action open,string? name=null)
    {
        if(exiting||Settings.QuietMode)return null;trayNotification?.Close();var notice=new TrayNotificationWindow(title,message,actionLabel,open);if(name!=null)notice.Name=name;trayNotification=notice;notice.Closed+=(_,_)=>{if(ReferenceEquals(trayNotification,notice))trayNotification=null;};notice.ShowNearTray(fullChat??(Window?)Pet);return notice;
    }
    internal void ObserveNotificationSession(ConversationSession session)
    {
        if(notificationSessions.TryGetValue(session,out _))return;notificationSessions.Add(session,new object());string? running=null;var gate=new object();
        session.AgentTaskChanged+=()=>{var state=session.CurrentAgentTask;if(state==null)return;NotificationCue? cue=null;lock(gate){if(state.Status=="running")running=state.Id;else if(running==state.Id){running=null;if(state.Status=="complete")cue=NotificationCue.Complete;else if(state.Status is "error" or "needs_reconciliation")cue=NotificationCue.Failure;}}if(cue.HasValue)PlayNotificationCue(cue.Value,"task:"+state.Id);};
    }
    void NotificationQuestionsChanged()
    {
        if(exiting)return;Dispatcher.UIThread.Post(()=>{if(exiting)return;foreach(var request in Questions.Requests)PlayNotificationCue(NotificationCue.Review,"question:"+request.SessionId+":"+request.TaskId+":"+request.CallId);});
    }
    OfficialCompanionPreparation TrackPreparationNotifications(OfficialCompanionPreparation service)
    {
        var gate=new object();string? running=null;service.Changed+=()=>{NotificationCue? cue=null;string? id=null;lock(gate){if(service.State==OfficialCompanionPreparationState.Running){running??=Guid.NewGuid().ToString("N");}else if(running!=null){id=running;running=null;if(service.State==OfficialCompanionPreparationState.Completed)cue=NotificationCue.Complete;else if(service.State==OfficialCompanionPreparationState.Failed)cue=NotificationCue.Failure;}}if(cue.HasValue)PlayNotificationCue(cue.Value,"preparation:"+id);};return service;
    }
    void DisposeNotificationServices(){Questions.Changed-=NotificationQuestionsChanged;trayNotification?.Close();notificationSounds?.Dispose();}
}
