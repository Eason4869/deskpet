using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class RelationshipExperienceContent
    {
        public string CompanionId {get;private set;}
        public string StageId {get;private set;}
        public string Welcome {get;private set;}
        public string Interaction {get;private set;}
        public string AiContext {get;private set;}
        public bool HasSavedState {get;private set;}
        public bool ReadFailed {get;private set;}
        readonly RelationshipSnapshot snapshot;
        internal RelationshipExperienceContent(string id,RelationshipSnapshot saved,RelationshipPackContent pack,string locale,bool failed)
        {
            CompanionId=id;snapshot=saved;ReadFailed=failed;HasSavedState=saved!=null&&saved.State!=null&&!failed;
            StageId=HasSavedState?saved.StageId:null;Welcome=HasSavedState?RelationshipContent.Welcome(StageId,pack,locale):"";
            Interaction=HasSavedState?RelationshipContent.Interaction(StageId,pack,locale):"";
            AiContext=HasSavedState&&saved.Settings.AiContextEnabled?RelationshipContent.StageContext(StageId,locale):"";
        }
        internal bool CanRespondToEvent(string eventId)
        {return HasSavedState&&snapshot.Settings.FeedbackEnabled&&(!snapshot.Settings.GrowthEnabled||snapshot.State.Receipts.Any(r=>r.EventId==eventId));}
    }
    // Presentation reads only committed state. Never records, claims or schedules growth.
    public sealed class RelationshipExperience
    {
        readonly RelationshipRuntime runtime;
        public RelationshipExperience(RelationshipRuntime runtime){if(runtime==null)throw new ArgumentNullException("runtime");this.runtime=runtime;}
        public Task<RelationshipExperienceContent> ReadAsync(string companionId,RelationshipPackContent pack,string locale=null)
        {
            string language=locale??TextResources.CurrentLanguage;
            return Task.Run(delegate{
                try{return new RelationshipExperienceContent(companionId,runtime.Service.GetSnapshot(companionId),pack,language,false);}
                catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is SerializationException||error is ArgumentException||error is System.Xml.XmlException))throw;return new RelationshipExperienceContent(companionId,null,null,language,true);}
            });
        }
    }
}
