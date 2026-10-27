using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;

namespace Dafeiyu
{
    public enum HolidayKeepsakeObservationStatus { Completed, ClockRollback, MaintenanceBusy, StorageUnavailable, StoreIncompatible, Closed }
    public enum HolidayKeepsakeStatus { Saved, AlreadyOwned, UnsupportedCompanion, ResourceUnavailable }
    public enum HolidayKeepsakeAvailability { Available, NotEnabled, UnsupportedCompanion, ResourceUnavailable }
    public enum HolidayKeepsakeReadStatus { Available, NotFound, ResourceUnavailable, StorageUnavailable, StoreIncompatible, Closed }
    public enum HolidayEncounterKind { VisibleBuddy, CompanionHome }
    public sealed class HolidayKeepsakeEncounter
    {
        public string CompanionId {get;private set;}
        public string VerifiedCharacterId {get;private set;}
        public string CompanionName {get;private set;}
        public RelationshipTime Time {get;private set;}
        public HolidayEncounterKind Kind {get;private set;}
        // Calling this API is an assertion by the host that a real encounter occurred.
        // Visibility, lock-screen, DND and wearing preferences are not storage inputs.
        public HolidayKeepsakeEncounter(string companionId,string verifiedCharacterId,string name,RelationshipTime time)
            :this(companionId,verifiedCharacterId,name,HolidayEncounterKind.VisibleBuddy,time){}
        public HolidayKeepsakeEncounter(string companionId,string verifiedCharacterId,string name,HolidayEncounterKind kind,RelationshipTime time)
        {
            if(time==null||String.IsNullOrWhiteSpace(companionId)||companionId.Length>256||name==null||name.Length>256
                ||name.IndexOf('\0')>=0||(kind!=HolidayEncounterKind.VisibleBuddy&&kind!=HolidayEncounterKind.CompanionHome))throw new ArgumentException("Invalid captured encounter.");
            CompanionId=companionId;VerifiedCharacterId=verifiedCharacterId;CompanionName=name;Time=time;Kind=kind;
        }
    }
    [DataContract] public sealed class HolidayKeepsakeRecord
    {
        [DataMember(IsRequired=true)] public string CompanionId {get;internal set;}
        [DataMember(IsRequired=true)] public string CharacterId {get;internal set;}
        [DataMember(IsRequired=true)] public string CompanionName {get;internal set;}
        [DataMember(IsRequired=true)] public string HolidayId {get;internal set;}
        [DataMember(IsRequired=true)] public string CalendarId {get;internal set;}
        [DataMember(IsRequired=true)] public string InstanceKey {get;internal set;}
        [DataMember(IsRequired=true)] public int OccurrenceYear {get;internal set;}
        [DataMember(IsRequired=true)] public string OccurrenceStartLocalDate {get;internal set;}
        [DataMember(IsRequired=true)] public string OccurrenceEndExclusiveLocalDate {get;internal set;}
        [DataMember(IsRequired=true)] public long EarnedUtcTicks {get;internal set;}
        [DataMember(IsRequired=true)] public int OffsetMinutes {get;internal set;}
        [DataMember(IsRequired=true)] public string EarnedLocalDate {get;internal set;}
        [DataMember(IsRequired=true)] public string OutfitId {get;internal set;}
        [DataMember(IsRequired=true)] public string OutfitVersion {get;internal set;}
        [DataMember(IsRequired=true)] public string SceneId {get;internal set;}
        [DataMember(IsRequired=true)] public string SceneVersion {get;internal set;}
        [DataMember(IsRequired=true)] public int PixelWidth {get;internal set;}
        [DataMember(IsRequired=true)] public int PixelHeight {get;internal set;}
        [DataMember(IsRequired=true)] public int ByteLength {get;internal set;}
        [DataMember(IsRequired=true)] public string SceneSha256 {get;internal set;}
        public DateTime EarnedUtc {get{return new DateTime(EarnedUtcTicks,DateTimeKind.Utc);}}
    }
    [DataContract] internal sealed class HolidayKeepsakeData
    {
        [DataMember(IsRequired=true)] public int Schema=1;
        [DataMember(IsRequired=true)] public string DateHighWater="";
        [DataMember(IsRequired=true)] public List<HolidayKeepsakeRecord> Records=new List<HolidayKeepsakeRecord>();
    }
    public sealed class HolidayKeepsakeResult
    {
        public string HolidayId {get;private set;} public string InstanceKey {get;private set;}
        public HolidayKeepsakeStatus Status {get;private set;} public HolidayKeepsakeRecord Record {get;private set;}
        internal HolidayKeepsakeResult(string holiday,string instance,HolidayKeepsakeStatus status,HolidayKeepsakeRecord record=null)
        {HolidayId=holiday;InstanceKey=instance;Status=status;Record=record;}
    }
    public sealed class HolidayKeepsakeObservationResult
    {
        public HolidayKeepsakeObservationStatus Status {get;private set;}
        public ReadOnlyCollection<HolidayKeepsakeResult> Results {get;private set;}
        internal HolidayKeepsakeObservationResult(HolidayKeepsakeObservationStatus status,IList<HolidayKeepsakeResult> results=null)
        {Status=status;Results=new List<HolidayKeepsakeResult>(results??new HolidayKeepsakeResult[0]).AsReadOnly();}
    }
    public sealed class HolidayKeepsakeAlbum
    {
        public HolidayKeepsakeReadStatus Status {get;private set;}
        public ReadOnlyCollection<HolidayKeepsakeRecord> Records {get;private set;}
        internal HolidayKeepsakeAlbum(HolidayKeepsakeReadStatus status,IEnumerable<HolidayKeepsakeRecord> records=null)
        {Status=status;Records=new List<HolidayKeepsakeRecord>(records??new HolidayKeepsakeRecord[0]).AsReadOnly();}
    }
    public sealed class HolidayKeepsakeSceneResult
    {
        readonly byte[] bytes;
        public HolidayKeepsakeReadStatus Status {get;private set;}
        public HolidayKeepsakeRecord Record {get;private set;}
        internal HolidayKeepsakeSceneResult(HolidayKeepsakeReadStatus status,HolidayKeepsakeRecord record=null,byte[] png=null)
        {Status=status;Record=record;bytes=png;}
        public byte[] CopyPngBytes(){return bytes==null?null:(byte[])bytes.Clone();}
    }
    public sealed class HolidayKeepsakePreviewResult
    {
        readonly byte[] bytes;
        public HolidayKeepsakeAvailability Availability {get;private set;}
        public string HolidayId {get;private set;} public string InstanceKey {get;private set;}
        public RelationshipStageArtAsset Descriptor {get;private set;}
        internal HolidayKeepsakePreviewResult(HolidayKeepsakeAvailability status,string holiday,string instance=null,RelationshipStageArtAsset descriptor=null,byte[] png=null)
        {Availability=status;HolidayId=holiday;InstanceKey=instance;Descriptor=descriptor;bytes=png;}
        public byte[] CopyPngBytes(){return bytes==null?null:(byte[])bytes.Clone();}
    }
    public interface IHolidayKeepsakeSceneValidator {void Validate(byte[] png,int expectedWidth,int expectedHeight);}
}
