using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Dafeiyu
{
    [DataContract] public sealed class HolidayActivityState
    {
        [DataMember] public string InstanceKey {get;set;}
        [DataMember] public DateTime StartDate {get;set;}
        [DataMember] public bool ManualOverride {get;set;}
        [DataMember] public bool TransitionShown {get;set;}
        [DataMember] public bool GreetingShown {get;set;}
    }
    [DataContract] public sealed class HolidayCharacterState
    {
        [DataMember] public bool Enabled {get;set;}
        [DataMember] public string PreferredOutfitId {get;set;}
        [DataMember] public List<string> UnlockedOutfits {get;set;}
        [DataMember] public List<HolidayActivityState> Activities {get;set;}
        public HolidayCharacterState(){Enabled=true;UnlockedOutfits=new List<string>();Activities=new List<HolidayActivityState>();}
        [OnDeserializing] void BeforeDeserialize(StreamingContext context){Enabled=true;}
    }
    [DataContract] public sealed class HolidayState
    {
        [DataMember] public int SchemaVersion {get;set;}
        [DataMember] public bool WardrobeDiscovered {get;set;}
        [DataMember] public Dictionary<string,HolidayCharacterState> Characters {get;set;}
        public HolidayState(){SchemaVersion=2;Characters=new Dictionary<string,HolidayCharacterState>(StringComparer.Ordinal);}
        public HolidayCharacterState ForCharacter(string id)
        {id=SourceKey(id);HolidayCharacterState result;if(!Characters.TryGetValue(id,out result)){result=new HolidayCharacterState();Characters.Add(id,result);}return result;}
        public void Unlock(string characterId,string outfitId)
        {RequireId(outfitId,false);var role=ForCharacter(characterId);if(!role.UnlockedOutfits.Contains(outfitId))role.UnlockedOutfits.Add(outfitId);WardrobeDiscovered=true;}
        internal static string SourceKey(string value)
        {
            if(value==null)throw new InvalidDataException("Invalid holiday state source.");
            if(!value.Contains(":")){RequireId(value,false);return "official:"+value;}
            string prefix=value.StartsWith("official:",StringComparison.Ordinal)?"official:":value.StartsWith("custom-pack:",StringComparison.Ordinal)?"custom-pack:":null;
            if(prefix==null)throw new InvalidDataException("Invalid holiday state source.");
            RequireId(value.Substring(prefix.Length),false);return value;
        }
        internal static void RequireId(string value,bool instance)
        {
            if(String.IsNullOrEmpty(value) || value.Length>128 || value=="." || value==".." || value.Any(c=>!(c>='a' && c<='z' || c>='0' && c<='9' || c=='-' || c=='_' || c=='.' || instance && c==':')))
                throw new InvalidDataException("Invalid holiday identifier.");
        }
        internal void ValidateAndPrune()
        {
            if(SchemaVersion>2)throw new NotSupportedException("Holiday state was written by a newer application.");
            if(SchemaVersion!=1&&SchemaVersion!=2)throw new InvalidDataException("Invalid holiday state version.");
            if(Characters==null)Characters=new Dictionary<string,HolidayCharacterState>(StringComparer.Ordinal);
            if(SchemaVersion==1) {
                var migrated=new Dictionary<string,HolidayCharacterState>(StringComparer.Ordinal);
                foreach(var pair in Characters)migrated.Add(SourceKey(pair.Key),pair.Value);
                Characters=migrated;SchemaVersion=2;
            }
            if(Characters.Count>64)throw new InvalidDataException("Too many holiday characters.");
            int activities=0;
            foreach(var pair in Characters) {
                if(SourceKey(pair.Key)!=pair.Key)throw new InvalidDataException("Noncanonical holiday source.");var role=pair.Value;if(role==null)throw new InvalidDataException("Missing holiday character state.");
                if(role.UnlockedOutfits==null)role.UnlockedOutfits=new List<string>();
                if(role.Activities==null)role.Activities=new List<HolidayActivityState>();
                if(role.UnlockedOutfits.Count>128)throw new InvalidDataException("Too many unlocked outfits.");
                foreach(var id in role.UnlockedOutfits)RequireId(id,false);
                role.UnlockedOutfits=role.UnlockedOutfits.Distinct(StringComparer.Ordinal).ToList();
                if(!String.IsNullOrEmpty(role.PreferredOutfitId) && !role.UnlockedOutfits.Contains(role.PreferredOutfitId))throw new InvalidDataException("Preferred outfit is not owned.");
                if(role.UnlockedOutfits.Count>0)WardrobeDiscovered=true;
                activities+=role.Activities.Count;if(activities>16384)throw new InvalidDataException("Too many holiday activity records.");
                var keys=new HashSet<string>(StringComparer.Ordinal);
                foreach(var activity in role.Activities) {
                    if(activity==null)throw new InvalidDataException("Missing holiday activity record.");
                    RequireId(activity.InstanceKey,true);
                    if(!keys.Add(activity.InstanceKey))throw new InvalidDataException("Duplicate holiday activity record.");
                    activity.StartDate=activity.StartDate.Date;
                }
            }
            foreach(var entry in Characters.Values.SelectMany(role=>role.Activities.Select(activity=>new{Role=role,Activity=activity})).OrderByDescending(x=>x.Activity.StartDate).Skip(4096).ToList())
                entry.Role.Activities.Remove(entry.Activity);
        }
    }
    public sealed class HolidayStateStore
    {
        const int MaximumBytes=1024*1024;
        readonly string path;
        readonly object gate=new object();
        bool writeBlocked;
        [DataContract] sealed class StateHeader {[DataMember]public int SchemaVersion {get;set;}}
        public string Warning {get;private set;}
        public HolidayStateStore(string filePath){path=Path.GetFullPath(filePath);}
        public HolidayState Load()
        {
            lock(gate) {
                Warning=null;writeBlocked=false;
                if(File.Exists(path)) {
                    try{return ReadWithRetry(path);}
                    catch(NotSupportedException error){writeBlocked=true;Warning=error.Message;return new HolidayState();}
                    catch(Exception error) {
                        if(!Recoverable(error))throw;Warning="Holiday state could not be read: "+error.Message;
                        if(!Corrupt(error)){writeBlocked=true;return new HolidayState();}
                        // Rename rather than copy: a damaged/oversized file is preserved without unbounded IO.
                        try{File.Move(path,path+".invalid-"+Guid.NewGuid().ToString("N"));}
                        catch(Exception preserveError){if(!Recoverable(preserveError))throw;writeBlocked=true;Warning+=" Original file could not be preserved: "+preserveError.Message;}
                    }
                }
                if(File.Exists(path+".bak")) {
                    try{var backup=ReadWithRetry(path+".bak");Warning=(Warning??"Holiday state is missing.")+" Recovered backup.";return backup;}
                    catch(NotSupportedException error){writeBlocked=true;Warning=error.Message;}
                    catch(Exception error){if(!Recoverable(error))throw;if(!Corrupt(error))writeBlocked=true;Warning=(Warning??"")+" Backup could not be read: "+error.Message;}
                }
                return new HolidayState();
            }
        }
        static bool Recoverable(Exception error)
        {return error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is SerializationException || error is System.Xml.XmlException || error is ArgumentException;}
        static bool Corrupt(Exception error)
        {return error is InvalidDataException || error is SerializationException || error is System.Xml.XmlException;}
        static HolidayState ReadWithRetry(string file)
        {
            for(int attempt=0;;attempt++) {
                try{return Read(file);}
                catch(IOException){if(attempt==2)throw;System.Threading.Thread.Sleep(20*(attempt+1));}
            }
        }
        static HolidayState Read(string file)
        {
            using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if(input.Length>MaximumBytes)throw new InvalidDataException("Holiday state exceeds 1 MiB.");
                var header=(StateHeader)new DataContractJsonSerializer(typeof(StateHeader)).ReadObject(input);
                if(header!=null && header.SchemaVersion>2)throw new NotSupportedException("Holiday state was written by a newer application.");
                input.Position=0;
                var state=(HolidayState)new DataContractJsonSerializer(typeof(HolidayState)).ReadObject(input);
                if(state==null)throw new InvalidDataException("Holiday state is empty.");state.ValidateAndPrune();return state;
            }
        }
        public void Save(HolidayState state)
        {
            if(state==null)throw new ArgumentNullException("state");
            lock(gate) {
                if(writeBlocked)throw new InvalidOperationException("Holiday state cannot be overwritten. "+Warning);
                state.ValidateAndPrune();byte[] bytes;
                using(var output=new MemoryStream()) {
                    new DataContractJsonSerializer(typeof(HolidayState)).WriteObject(output,state);
                    if(output.Length>MaximumBytes)throw new InvalidDataException("Holiday state exceeds 1 MiB.");bytes=output.ToArray();
                }
                if(File.Exists(path)&&!File.Exists(path+".schema-1.bak")) {
                    using(var input=File.OpenRead(path)) {
                        var header=(StateHeader)new DataContractJsonSerializer(typeof(StateHeader)).ReadObject(input);
                        if(header!=null&&header.SchemaVersion==1)File.Copy(path,path+".schema-1.bak",false);
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
                try {
                    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}
                    if(File.Exists(path))File.Replace(temporary,path,path+".bak",true);else File.Move(temporary,path);
                    Warning=null;
                }finally {if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
    }
}
