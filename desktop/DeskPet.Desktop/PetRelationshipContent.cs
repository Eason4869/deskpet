using Avalonia.Threading;
using Dafeiyu;
namespace DeskPet.Desktop;
internal sealed partial class DesktopController
{
    readonly HashSet<string> dismissedRelationshipEvents=new();readonly Queue<string> dismissedRelationshipOrder=new();
    internal bool RelationshipNoticeVisible=>relationshipNotice?.IsVisible==true;
    internal bool RelationshipEventDismissed(string id)=>dismissedRelationshipEvents.Contains(id);
    void OpenRelationshipNotice(RelationshipFeedbackNotice notice)
    {
        relationshipNotice?.Close();var shown=new RelationshipFeedbackWindow(this,notice);relationshipNotice=shown;
        shown.Closed+=(_,_)=>{foreach(var id in shown.EventIds)if(dismissedRelationshipEvents.Add(id))dismissedRelationshipOrder.Enqueue(id);while(dismissedRelationshipOrder.Count>256)dismissedRelationshipEvents.Remove(dismissedRelationshipOrder.Dequeue());if(ReferenceEquals(relationshipNotice,shown))relationshipNotice=null;};shown.Show();
    }
    void ShowRelationshipFeedback(RelationshipFeedbackNotice notice)
    {
        if(exiting||notice.CompanionId!=RelationshipCompanionId||Settings.QuietMode||!Pet.RelationshipFeedbackAllowed||notice.SuppressionVersion.HasValue&&notice.SuppressionVersion.Value!=Relationships.FeedbackSuppressionVersion||notice.EventIds.Count>0&&notice.EventIds.All(dismissedRelationshipEvents.Contains))return;
        if(relationshipNotice?.IsVisible==true&&relationshipNotice.CompanionId==notice.CompanionId)relationshipNotice.Merge(notice);else OpenRelationshipNotice(notice);
        if(notice.Milestones.Count>0)Pet.CelebrateRelationship(RelationshipContent.Action(notice.Milestones.Last().StageId,RelationshipPack)??"waving");
    }
    internal void ShowRelationshipInteraction(string id,string text,string eventId)
    {
        if(exiting||id!=RelationshipCompanionId||RelationshipEventDismissed(eventId))return;
        if(relationshipNotice?.IsVisible!=true||relationshipNotice.CompanionId!=id)OpenRelationshipNotice(new RelationshipFeedbackNotice{CompanionId=id,Milestones=new List<RelationshipMilestone>()});
        relationshipNotice!.SetInteraction(text,eventId);
    }
}
internal sealed partial class PetWindow
{
    int relationshipContentEpoch,relationshipActionRevision;
    readonly HashSet<string> relationshipWelcomes=new();
    bool RelationshipInteractionAllowed(string id)=>!owner.IsExiting&&id==owner.RelationshipCompanionId&&RelationshipFeedbackAllowed&&!owner.Settings.QuietMode&&!owner.DataMaintenance.IsMaintenanceActive&&!owner.HolidayBusinessBusy&&!owner.WorkBindings.Manager.Busy&&owner.WorkBindings.Jobs.Manager?.HasActiveJobs!=true&&!owner.GuideLocked&&!Paused&&!dragging&&!drop.Active&&!holidayPlayback.Active&&!holidayTransition.Active;
    async void RecordRelationshipInteraction(bool userOrigin,bool started)
    {
        string id=owner.RelationshipCompanionId;
        if(!owner.Relationships.TryEnqueueUserInteraction(id,userOrigin,started,out var completion,out var eventId))return;
        int epoch=++relationshipContentEpoch,revision=relationshipActionRevision,suppression=owner.Relationships.FeedbackSuppressionVersion;var skin=owner.Skin;var pack=owner.RelationshipPack;string locale=TextResources.CurrentLanguage;bool admitted=RelationshipInteractionAllowed(id);
        try{
            await completion;
            if(!admitted||epoch!=relationshipContentEpoch||revision!=relationshipActionRevision||!ReferenceEquals(skin,owner.Skin)||!RelationshipInteractionAllowed(id)||suppression!=owner.Relationships.FeedbackSuppressionVersion||owner.Relationships.PendingCount!=0||owner.Relationships.RequiresRestart||owner.Relationships.Warning.Length!=0)return;
            var content=await new RelationshipExperience(owner.Relationships).ReadAsync(id,pack,locale);
            if(epoch!=relationshipContentEpoch||revision!=relationshipActionRevision||!ReferenceEquals(skin,owner.Skin)||!RelationshipInteractionAllowed(id)||suppression!=owner.Relationships.FeedbackSuppressionVersion||!content.CanRespondToEvent(eventId)||owner.Relationships.PendingCount!=0||owner.Relationships.RequiresRestart||owner.Relationships.Warning.Length!=0)return;
            owner.ShowRelationshipInteraction(id,content.Interaction,eventId);
        }catch(ObjectDisposedException){}
    }
    string? RelationshipWelcome(RelationshipExperienceContent? content)=>content?.HasSavedState==true&&!content.ReadFailed&&!relationshipWelcomes.Contains(content.CompanionId+"|"+content.StageId)?content.Welcome:null;
    void RememberRelationshipWelcome(RelationshipExperienceContent? content,string text){if(content?.HasSavedState==true&&text==RelationshipWelcome(content))relationshipWelcomes.Add(content.CompanionId+"|"+content.StageId);}
}
