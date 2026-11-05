using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        NotificationSoundPlayer notificationSounds;
        bool notificationAudioReady;
        TrayNotificationWindow trayNotification;
        readonly ConditionalWeakTable<ConversationSession,object> notificationSessions=new ConditionalWeakTable<ConversationSession,object>();

        internal void EnableNotificationAudio(){notificationAudioReady=true;}
        internal void StopNotificationSounds(){if(notificationSounds!=null)notificationSounds.Stop();}
        void RefreshNotificationSounds(){if(Settings.QuietMode||!Settings.NotificationSoundsEnabled||Settings.Volume<=0||ProactiveGreetingSpeechActive||LiveActive||DictationActive||ConversationSessions.Any(s=>s.Stage==ConversationStage.Speaking))StopNotificationSounds();}
        internal void PlayNotificationCue(NotificationCue cue,string eventId)
        {
            if(closing||!notificationAudioReady||Dispatcher.HasShutdownStarted)return;
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>PlayNotificationCue(cue,eventId)));return;}
            if(notificationSounds==null)notificationSounds=new NotificationSoundPlayer(()=>Settings,()=>new PcmOutput(),()=>ProactiveGreetingSpeechActive||LiveActive||DictationActive||ConversationSessions.Any(s=>s.Stage==ConversationStage.Speaking));
            notificationSounds.TryPlay(cue,eventId);
        }
        internal static void PlayApplicationCue(NotificationCue cue,string eventId)
        {
            if(Application.Current==null)return;var pet=Application.Current.Windows.OfType<PetWindow>().FirstOrDefault();if(pet!=null)pet.PlayNotificationCue(cue,eventId);
        }
        internal void ShowTrayNotification(Func<string> title,Func<string> message,Func<string> actionLabel,Action open)
        {
            if(closing||Settings.QuietMode)return;if(trayNotification!=null)trayNotification.Close();
            var notice=new TrayNotificationWindow(title,message,actionLabel,open);trayNotification=notice;notice.Closed+=delegate{if(ReferenceEquals(trayNotification,notice))trayNotification=null;};notice.ShowNearTray(new WindowInteropHelper(this).Handle);
        }
        internal void ObserveNotificationSession(ConversationSession session)
        {
            object existing;if(session==null||notificationSessions.TryGetValue(session,out existing))return;notificationSessions.Add(session,new object());
            string running=null;var gate=new object();
            session.AgentTaskChanged+=delegate{
                var state=session.CurrentAgentTask;if(state==null)return;NotificationCue? cue=null;
                lock(gate){if(state.Status=="running")running=state.Id;else if(running==state.Id){running=null;if(state.Status=="complete")cue=NotificationCue.Complete;else if(state.Status=="error"||state.Status=="needs_reconciliation")cue=NotificationCue.Failure;}}
                if(cue.HasValue)PlayNotificationCue(cue.Value,"task:"+state.Id);
            };
        }
        AgentQuestionController CreateNotificationQuestions()
        {
            var controller=new AgentQuestionController();controller.Changed+=delegate{
                if(closing||Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(delegate{if(closing)return;foreach(var request in controller.Requests)PlayNotificationCue(NotificationCue.Review,"question:"+request.SessionId+":"+request.TaskId+":"+request.CallId);}));
            };return controller;
        }
        OfficialCompanionPreparation TrackPreparationNotifications(OfficialCompanionPreparation service)
        {
            var gate=new object();string running=null;
            service.Changed+=delegate{
                NotificationCue? cue=null;string id=null;lock(gate){if(service.State==OfficialCompanionPreparationState.Running){if(running==null)running=Guid.NewGuid().ToString("N");}else if(running!=null){id=running;running=null;if(service.State==OfficialCompanionPreparationState.Completed)cue=NotificationCue.Complete;else if(service.State==OfficialCompanionPreparationState.Failed)cue=NotificationCue.Failure;}}
                if(cue.HasValue)PlayNotificationCue(cue.Value,"preparation:"+id);
            };return service;
        }
        void DisposeNotificationServices(){notificationAudioReady=false;if(trayNotification!=null)trayNotification.Close();if(notificationSounds!=null)notificationSounds.Dispose();}
    }
}
