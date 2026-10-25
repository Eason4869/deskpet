using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        int relationshipContentEpoch;
        readonly HashSet<string> dismissedRelationshipEvents=new HashSet<string>();
        readonly Queue<string> dismissedRelationshipOrder=new Queue<string>();
        readonly HashSet<string> relationshipWelcomes=new HashSet<string>();
        bool RelationshipNoticeDismissed(RelationshipFeedbackNotice notice)
        {return notice.EventIds.Count>0&&notice.EventIds.All(dismissedRelationshipEvents.Contains);}
        void OpenRelationshipNotice(RelationshipFeedbackNotice notice)
        {
            if(relationshipNotice!=null)relationshipNotice.Close();
            relationshipNotice=new RelationshipFeedbackWindow(this,notice);var shown=relationshipNotice;
            shown.Closed+=delegate{foreach(var id in shown.EventIds)if(dismissedRelationshipEvents.Add(id))dismissedRelationshipOrder.Enqueue(id);while(dismissedRelationshipOrder.Count>256)dismissedRelationshipEvents.Remove(dismissedRelationshipOrder.Dequeue());if(ReferenceEquals(relationshipNotice,shown))relationshipNotice=null;};shown.Show();
        }
        bool RelationshipInteractionAllowed(string id)
        {return RelationshipFeedbackAllowed(id)&&!DataMaintenance.IsMaintenanceActive&&!DialogueBusy&&!DictationActive&&!LiveActive&&!HasActiveAgentJobs&&!HasQueuedMessages&&!GuideActive&&!Paused&&!dragging&&!walking&&!HolidayPlaying&&!holidayTransition.Active&&!IsUpdating;}
        async void RecordRelationshipInteraction(bool started)
        {
            string id=RelationshipCompanionId,eventId;Task completion;
            if(!Relationships.TryEnqueueUserInteraction(id,true,started,out completion,out eventId))return;
            int epoch=++relationshipContentEpoch,revision=interactionRevision,suppression=Relationships.FeedbackSuppressionVersion;var skin=Skin;var pack=RelationshipPack;string locale=TextResources.CurrentLanguage;
            bool admitted=RelationshipInteractionAllowed(id);
            try{
                await completion;
                if(!admitted||epoch!=relationshipContentEpoch||revision!=interactionRevision||!ReferenceEquals(skin,Skin)||!RelationshipInteractionAllowed(id)||suppression!=Relationships.FeedbackSuppressionVersion||Relationships.PendingCount!=0||Relationships.RequiresRestart||Relationships.Warning.Length!=0)return;
                var content=await new RelationshipExperience(Relationships).ReadAsync(id,pack,locale);
                if(epoch!=relationshipContentEpoch||revision!=interactionRevision||!ReferenceEquals(skin,Skin)||!RelationshipInteractionAllowed(id)||suppression!=Relationships.FeedbackSuppressionVersion||dismissedRelationshipEvents.Contains(eventId)||!content.CanRespondToEvent(eventId)||Relationships.PendingCount!=0||Relationships.RequiresRestart||Relationships.Warning.Length!=0)return;
                ShowRelationshipInteraction(id,content.Interaction,eventId);
            }catch(ObjectDisposedException){}
        }
        void ShowRelationshipInteraction(string id,string text,string eventId)
        {
            if(!RelationshipInteractionAllowed(id)||dismissedRelationshipEvents.Contains(eventId))return;
            if(relationshipNotice==null||!relationshipNotice.IsVisible||relationshipNotice.CompanionId!=id)OpenRelationshipNotice(new RelationshipFeedbackNotice{CompanionId=id,Milestones=new List<RelationshipMilestone>()});
            if(relationshipNotice!=null)relationshipNotice.SetInteraction(text,eventId);
        }
        bool RelationshipNoticeVisible {get{return relationshipNotice!=null&&relationshipNotice.IsVisible;}}
        string RelationshipWelcome(RelationshipExperienceContent content)
        {return content!=null&&content.HasSavedState&&!content.ReadFailed&&!relationshipWelcomes.Contains(content.CompanionId+"|"+content.StageId)?content.Welcome:null;}
        void RememberRelationshipWelcome(RelationshipExperienceContent content,string line)
        {if(content!=null&&content.HasSavedState&&line==RelationshipWelcome(content))relationshipWelcomes.Add(content.CompanionId+"|"+content.StageId);}
    }
}
