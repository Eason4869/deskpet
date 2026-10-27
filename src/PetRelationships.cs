using System;
using System.Linq;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        RelationshipFeedback relationshipFeedback;
        RelationshipFeedbackWindow relationshipNotice;
        bool relationshipFullscreen;
        void ObserveRelationshipFullscreen(bool fullscreen)
        {if(relationshipFullscreen!=fullscreen){relationshipFullscreen=fullscreen;Relationships.InvalidateFeedback();}}
        internal bool RelationshipFeedbackAllowed(string id)
        {bool fullscreen=DesktopInterop.ForegroundIsFullScreen();ObserveRelationshipFullscreen(fullscreen);return !closing&&id==RelationshipCompanionId&&!Settings.QuietMode&&Relationships.FeedbackEnabled&&IsPetVisible&&!sessionLocked&&!fullscreen;}
        void InitializeRelationshipFeedback()
        {
            relationshipFeedback=new RelationshipFeedback(Relationships,RelationshipFeedbackAllowed,ShowRelationshipFeedback,null,
                action=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(action);});
        }
        void ShowRelationshipFeedback(RelationshipFeedbackNotice notice)
        {
            if(!RelationshipFeedbackAllowed(notice.CompanionId)||notice.SuppressionVersion.HasValue&&notice.SuppressionVersion.Value!=Relationships.FeedbackSuppressionVersion||RelationshipNoticeDismissed(notice))return;
            if(relationshipNotice!=null&&relationshipNotice.IsVisible&&relationshipNotice.CompanionId==notice.CompanionId)relationshipNotice.Merge(notice);
            else OpenRelationshipNotice(notice);
            // Busy work/chat may show the passive card. Only an idle pet may animate.
            if(notice.Milestones.Count>0)CelebrateRelationship(notice.Milestones.Last().StageId);
        }
        internal void CelebrateRelationship(string stageId)
        {
            if(!RelationshipFeedbackAllowed(RelationshipCompanionId)||DialogueBusy||DictationActive||LiveActive||HasActiveAgentJobs||HasQueuedMessages||GuideActive||Paused||dragging||walking||interaction.Active||HolidayPlaying||holidayTransition.Active||IsUpdating||Animation.State!="idle")return;
            string action=RelationshipContent.Action(stageId,RelationshipPack)??"waving";
            if(AnimationPlayer.States.Contains(action))PlayAction(action,1.2);else PlayInteraction(action);
        }
        void DisposeRelationshipFeedback()
        {if(relationshipFeedback!=null)relationshipFeedback.Dispose();if(relationshipNotice!=null)relationshipNotice.Close();relationshipNotice=null;}
    }
}
