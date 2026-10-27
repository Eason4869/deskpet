namespace Dafeiyu
{
    public sealed class RelationshipLiveTurn
    {
        public string User {get;internal set;}
        public string Reply {get;internal set;}
        public string Status {get;internal set;}
        public string LogicalTurnId {get;internal set;}
        public string CompanionId {get;internal set;}
        public RelationshipTime CompletedTime {get;internal set;}
    }
}
