using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class AgentJobStorageWarning
    {
        public string JobId {get;private set;}
        public string Code {get;private set;}
        internal AgentJobStorageWarning(string jobId,string code){JobId=jobId;Code=code;}
    }
    public sealed class AgentJobStore : IDisposable
    {
        readonly object ioGate=new object();
        readonly IAgentJobClock clock;
        readonly AgentJobStorageIO io;
        readonly FileStream writerLease;
        readonly List<AgentJobStorageWarning> warnings=new List<AgentJobStorageWarning>();
        readonly Dictionary<string,AgentJobRecord> records=new Dictionary<string,AgentJobRecord>(StringComparer.Ordinal);
        readonly Dictionary<string,AgentJobLogStore> logs=new Dictionary<string,AgentJobLogStore>(StringComparer.Ordinal);
        static readonly Regex jobId=new Regex("\\A[a-f0-9]{32}\\z",RegexOptions.CultureInvariant);
        static readonly Regex hash=new Regex("\\A[a-f0-9]{64}\\z",RegexOptions.CultureInvariant);
        volatile bool disposed;
        bool loading;
        internal event Action<string> Retired;
        public string DirectoryPath {get;private set;}
        public IList<AgentJobStorageWarning> Warnings {get{lock(ioGate)return warnings.ToArray();}}
        public AgentJobStore(string dataDirectory,IAgentJobClock clock):this(dataDirectory,clock,new AgentJobStorageIO()){}
        internal AgentJobStore(string dataDirectory,IAgentJobClock clock,AgentJobStorageIO io)
        {
            if(clock==null||io==null)throw new ArgumentNullException("clock");
            if(String.IsNullOrWhiteSpace(dataDirectory)||!Path.IsPathRooted(dataDirectory))throw new ArgumentException("dataDirectory");
            this.clock=clock;this.io=io;AgentInstructions.RejectLinks(dataDirectory);
            DirectoryPath=Path.GetFullPath(Path.Combine(dataDirectory,".deskpet-agent-kernel","jobs"));
            AgentInstructions.RejectLinks(DirectoryPath);Directory.CreateDirectory(DirectoryPath);AgentInstructions.RejectLinks(DirectoryPath);
            string leasePath=Path.Combine(DirectoryPath,"writer.lock");AgentInstructions.RejectLinks(leasePath);
            writerLease=new FileStream(leasePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        void EnsureOwned(){if(disposed)throw new ObjectDisposedException("AgentJobStore");AgentInstructions.RejectLinks(DirectoryPath);}
        internal static void ValidateId(string id){if(id==null||!jobId.IsMatch(id))throw new ArgumentException("id");}
        internal string MetadataPath(string id){ValidateId(id);return Path.Combine(DirectoryPath,id+".json");}
        static void Validate(AgentJobRecord record,string expectedId)
        {
            if(record==null||record.Version!=1||record.Id!=expectedId||!Enum.IsDefined(typeof(AgentJobState),record.State)||record.Owner==null||record.Spec==null)throw new SerializationException("job_metadata_invalid");
            ValidateId(record.Id);
            if(new[]{record.SourceTaskId,record.SourceCallId,record.SourceReplyId}.Any(value=>value!=null&&value.Length>256))throw new SerializationException("job_source_invalid");
            new AgentJobOwner(record.Owner.ProjectId,record.Owner.ConversationId,record.Owner.AgentId);
            new AgentJobSpec(record.Spec.Command,record.Spec.Title,record.Spec.WorkingDirectory,record.Spec.TimeoutSeconds);
            if(record.InvocationKey==null||record.PayloadHash==null||!hash.IsMatch(record.InvocationKey)||!hash.IsMatch(record.PayloadHash)||record.CreatedUtc==default(DateTime)||record.TimingsMs==null||record.TimingsMs.Count>16)throw new SerializationException("job_metadata_invalid");
            if(IsTerminal(record.State)&&!record.FinishedUtc.HasValue)throw new SerializationException("job_terminal_time_missing");
            if(record.RootExitCode.HasValue&&!record.RootExited)throw new SerializationException("job_exit_code_without_exit");
        }
        internal static bool IsTerminal(AgentJobState state){return state==AgentJobState.Succeeded||state==AgentJobState.Failed||state==AgentJobState.Stopped||state==AgentJobState.TimedOut||state==AgentJobState.StartFailed||state==AgentJobState.Interrupted;}
        byte[] Serialize(AgentJobRecord record)
        {
            Validate(record,record.Id);
            using(var memory=new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(AgentJobRecord)).WriteObject(memory,record);
                if(memory.Length>AgentJobPolicy.MaximumMetadataBytes)throw new IOException("job_metadata_too_large");
                return memory.ToArray();
            }
        }
        void SaveCore(AgentJobRecord record)
        {
            EnsureOwned();string path=MetadataPath(record.Id);AgentInstructions.RejectLinks(path);byte[] bytes=Serialize(record);
            AgentJobRecord previous;if(!IsTerminal(record.State)&&(!records.TryGetValue(record.Id,out previous)||IsTerminal(previous.State)))ReserveLogCapacity(record.Id);
            io.WriteAtomic(path,bytes);records[record.Id]=record.Copy();if(!loading)PruneCore();
        }
        void Warn(string id,string code)
        {
            if(warnings.Any(warning=>warning.JobId==id&&warning.Code==code))return;
            if(warnings.Count>=AgentJobPolicy.MaximumHistory)warnings.RemoveAt(0);warnings.Add(new AgentJobStorageWarning(id,code));
        }
        long LogUsage(string proposedActiveId)
        {
            var active=new HashSet<string>(records.Values.Where(record=>!IsTerminal(record.State)).Select(record=>record.Id),StringComparer.Ordinal);active.Add(proposedActiveId);
            var paths=new HashSet<string>(Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
            foreach(string id in records.Keys.Concat(active))foreach(string suffix in new[]{".0.log",".1.log",".log-index"})paths.Add(Path.Combine(DirectoryPath,id+suffix));
            var existing=Directory.EnumerateFiles(DirectoryPath).Where(path=>path.EndsWith(".log",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".log-index",StringComparison.OrdinalIgnoreCase)).Take(4097).ToArray();
            if(existing.Length>4096)throw new IOException("job_log_count_exceeded");foreach(string path in existing)paths.Add(path);
            var actual=new Dictionary<string,long>(StringComparer.Ordinal);long total=0;
            foreach(string path in paths)
            {
                string name=Path.GetFileName(path);if(name.Length<32)throw new IOException("job_log_path_invalid");string id=name.Substring(0,32);ValidateId(id);
                string suffix=name.Substring(32);if(suffix!=".0.log"&&suffix!=".1.log"&&suffix!=".log-index")throw new IOException("job_log_path_invalid");
                AgentInstructions.RejectLinks(path);long bytes=io.Length(path);if(bytes<0)throw new IOException("job_log_usage_invalid");bytes=Math.Min(bytes,AgentJobPolicy.MaximumApplicationLogBytes+1);total+=bytes;
                long previous;actual.TryGetValue(id,out previous);actual[id]=previous+bytes;
            }
            foreach(string id in active){long bytes;actual.TryGetValue(id,out bytes);total+=Math.Max(0,AgentJobPolicy.MaximumJobLogBytes-bytes);}
            return total;
        }
        void ReserveLogCapacity(string id)
        {
            if(LogUsage(id)<=AgentJobPolicy.MaximumApplicationLogBytes)return;
            foreach(var candidate in records.Values.Where(record=>IsTerminal(record.State)).OrderBy(record=>record.FinishedUtc).ThenBy(record=>record.Id,StringComparer.Ordinal).ToArray())
            {TryRetire(candidate.Id);if(LogUsage(id)<=AgentJobPolicy.MaximumApplicationLogBytes)return;}
            throw new IOException("job_log_quota_exceeded");
        }
        bool TryRetire(string id)
        {
            try
            {
                AgentJobLogStore log;if(logs.TryGetValue(id,out log)){log.Dispose();logs.Remove(id);}
                foreach(string suffix in new[]{".0.log",".1.log",".log-index",".json"})
                {
                    string path=Path.Combine(DirectoryPath,id+suffix);AgentInstructions.RejectLinks(path);if(File.Exists(path))File.Delete(path);
                }
                records.Remove(id);var retired=Retired;if(retired!=null)retired(id);return true;
            }
            catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException))throw;Warn(id,"retention_cleanup_failed");return false;}
        }
        void PruneCore()
        {
            var terminal=records.Values.Where(record=>IsTerminal(record.State)).OrderByDescending(record=>record.FinishedUtc).ThenBy(record=>record.Id,StringComparer.Ordinal).ToArray();
            var remove=new HashSet<string>(terminal.Skip(AgentJobPolicy.MaximumHistory).Select(record=>record.Id),StringComparer.Ordinal);
            foreach(var record in terminal)if(record.FinishedUtc.Value<clock.UtcNow.AddDays(-AgentJobPolicy.HistoryDays))remove.Add(record.Id);
            foreach(string id in remove)TryRetire(id);
        }
        public Task SaveAsync(AgentJobSnapshot snapshot,CancellationToken token)
        {
            if(snapshot==null)throw new ArgumentNullException("snapshot");var record=snapshot.ToRecord();
            return Task.Run(delegate{token.ThrowIfCancellationRequested();lock(ioGate){token.ThrowIfCancellationRequested();SaveCore(record);}},token);
        }
        AgentJobRecord ReadCore(string path)
        {
            AgentInstructions.RejectLinks(path);
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length>AgentJobPolicy.MaximumMetadataBytes)throw new SerializationException("job_metadata_too_large");
                using(var memory=new MemoryStream())
                {
                    var buffer=new byte[4096];int count;
                    while((count=stream.Read(buffer,0,buffer.Length))!=0){if(memory.Length+count>AgentJobPolicy.MaximumMetadataBytes)throw new SerializationException("job_metadata_too_large");memory.Write(buffer,0,count);}
                    memory.Position=0;return (AgentJobRecord)new DataContractJsonSerializer(typeof(AgentJobRecord)).ReadObject(memory);
                }
            }
        }
        // Startup restoration only: live state belongs to the manager, never a disk refresh.
        public Task<IList<AgentJobSnapshot>> LoadAsync(CancellationToken token)
        {
            return Task.Run<IList<AgentJobSnapshot>>(delegate
            {
                lock(ioGate)
                {
                    EnsureOwned();var files=Directory.EnumerateFiles(DirectoryPath,"*.json").Take(4097).ToArray();
                    if(files.Length>4096)throw new IOException("job_metadata_count_exceeded");
                    records.Clear();loading=true;
                    try{foreach(var path in files)
                    {
                        token.ThrowIfCancellationRequested();string id=Path.GetFileNameWithoutExtension(path);ValidateId(id);AgentJobRecord record;
                        try{record=ReadCore(path);Validate(record,id);}
                        catch(Exception error)
                        {
                            if(!(error is SerializationException||error is ArgumentException||error is System.Xml.XmlException))throw;
                            AgentInstructions.RejectLinks(path);File.Move(path,Path.Combine(DirectoryPath,id+"-"+Guid.NewGuid().ToString("N")+".corrupt"));Warn(id,"corrupt_record");continue;
                        }
                        if(!IsTerminal(record.State))
                        {
                            record.State=AgentJobState.Interrupted;record.FinishedUtc=clock.UtcNow;record.MayHaveExecuted=true;record.CleanupConfirmed=false;SaveCore(record);
                        }
                        records[id]=record.Copy();
                    }}finally{loading=false;}
                    PruneCore();return records.Values.Select(record=>new AgentJobSnapshot(record)).ToList().AsReadOnly();
                }
            },token);
        }
        public AgentJobLogStore OpenLog(string id)
        {
            lock(ioGate)
            {
                EnsureOwned();ValidateId(id);AgentJobRecord record;if(!records.TryGetValue(id,out record))throw new KeyNotFoundException("job_not_found");
                AgentJobLogStore log;if(!logs.TryGetValue(id,out log)){log=new AgentJobLogStore(DirectoryPath,new AgentJobSnapshot(record),clock,EnsureOwned,io);logs.Add(id,log);}return log;
            }
        }
        public void Dispose(){lock(ioGate){if(disposed)return;foreach(var log in logs.Values)log.Dispose();disposed=true;writerLease.Dispose();}}
    }
}
