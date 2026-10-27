using System;
namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        Func<string> relationshipCompanion;
        public RelationshipRuntime Relationships {get;private set;}
        public string RelationshipWarning {get{return Relationships==null?"":Relationships.Warning;}}
        public void ConfigureRelationships(RelationshipRuntime runtime,Func<string> committedCompanion)
        {
            if(Relationships!=null)Relationships.Changed-=OnWarningChanged;
            Relationships=runtime;relationshipCompanion=committedCompanion;
            if(Relationships!=null)Relationships.Changed+=OnWarningChanged;
        }
        public string CaptureRelationshipCompanion(){return relationshipCompanion==null?null:relationshipCompanion();}
    }
}
