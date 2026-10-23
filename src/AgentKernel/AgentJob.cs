using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public enum AgentJobState { Starting,Running,Stopping,CleanupFailed,Succeeded,Failed,Stopped,TimedOut,StartFailed,Interrupted }
    public enum AgentJobStopReason { User,Timeout,ApplicationExit,OwnerDeleted,LogStorageFailed }

    [DataContract] public sealed class AgentJobOwner
    {
        [DataMember] public string ProjectId {get;private set;}
        [DataMember] public string ConversationId {get;private set;}
        [DataMember] public string AgentId {get;private set;}
        public AgentJobOwner(string projectId,string conversationId,string agentId)
        {
            ProjectId=Identity(projectId,"projectId");ConversationId=Identity(conversationId,"conversationId");AgentId=Identity(agentId,"agentId");
        }
        static string Identity(string value,string name){if(String.IsNullOrWhiteSpace(value)||value.Length>128)throw new ArgumentException(name);return value;}
        public bool Matches(AgentJobOwner other){return other!=null&&ProjectId==other.ProjectId&&ConversationId==other.ConversationId&&AgentId==other.AgentId;}
    }

    [DataContract] public sealed class AgentJobSpec
    {
        static readonly UTF8Encoding utf8=new UTF8Encoding(false,true);
        [DataMember] public string Command {get;private set;}
        [DataMember] public string Title {get;private set;}
        [DataMember] public string WorkingDirectory {get;private set;}
        [DataMember] public int TimeoutSeconds {get;private set;}
        public AgentJobSpec(string command,string title,string workingDirectory,int timeoutSeconds)
        {
            if(String.IsNullOrWhiteSpace(command)||command.IndexOf('\0')>=0||utf8.GetByteCount(command)>AgentJobPolicy.MaximumCommandBytes)throw new ArgumentException("command");
            if(String.IsNullOrWhiteSpace(workingDirectory)||!Path.IsPathRooted(workingDirectory))throw new ArgumentException("working_directory");
            if(title==null){title=command.Trim().Split(new[]{'\r','\n'},2)[0];var bytes=utf8.GetBytes(title);if(bytes.Length>AgentJobPolicy.MaximumTitleBytes){int count=AgentJobPolicy.MaximumTitleBytes;while((bytes[count]&0xC0)==0x80)count--;title=utf8.GetString(bytes,0,count);}}
            if(String.IsNullOrWhiteSpace(title)||title.IndexOf('\0')>=0||utf8.GetByteCount(title)>AgentJobPolicy.MaximumTitleBytes)throw new ArgumentException("title");
            Command=command;Title=title;WorkingDirectory=Path.GetFullPath(workingDirectory);TimeoutSeconds=AgentJobPolicy.ResolveTimeout(timeoutSeconds,AgentJobPolicy.MaximumTimeoutSeconds);
        }
    }

    public sealed class AgentJobOutputChunk
    {
        readonly byte[] bytes;
        public string Stream {get;private set;}
        public DateTime TimestampUtc {get;private set;}
        public byte[] Bytes {get{return (byte[])bytes.Clone();}}
        public int Count {get{return bytes.Length;}}
        public AgentJobOutputChunk(string stream,byte[] data,int count,DateTime timestampUtc)
        {
            if(stream!="stdout"&&stream!="stderr")throw new ArgumentException("stream");
            if(data==null||count<0||count>data.Length)throw new ArgumentOutOfRangeException("count");
            bytes=new byte[count];Buffer.BlockCopy(data,0,bytes,0,count);Stream=stream;TimestampUtc=timestampUtc.ToUniversalTime();
        }
    }

    // Mutable state stays internal; every public snapshot owns a separate record.
    [DataContract] internal sealed class AgentJobRecord
    {
        [DataMember] public int Version=1;
        [DataMember] public string Id,InvocationKey,PayloadHash;
        [DataMember] public string SourceTaskId,SourceCallId,SourceReplyId;
        [DataMember] public AgentJobOwner Owner;
        [DataMember] public AgentJobSpec Spec;
        [DataMember] public AgentJobState State;
        [DataMember] public DateTime CreatedUtc;
        [DataMember] public DateTime? StartedUtc,FinishedUtc;
        [DataMember] public DateTime? RootExitObservedUtc,TreeExitObservedUtc;
        [DataMember] public bool RootExited,CleanupConfirmed,MayHaveExecuted;
        [DataMember] public int? RootExitCode;
        [DataMember] public AgentJobStopReason? StopReason;
        [DataMember] public string LogWarning;
        [DataMember] public Dictionary<string,long?> TimingsMs=new Dictionary<string,long?>(StringComparer.Ordinal);
        internal AgentJobRecord Copy(){var copy=(AgentJobRecord)MemberwiseClone();copy.TimingsMs=new Dictionary<string,long?>(TimingsMs??new Dictionary<string,long?>(),StringComparer.Ordinal);return copy;}
    }
    public sealed class AgentJobSnapshot
    {
        readonly AgentJobRecord record;
        internal AgentJobSnapshot(AgentJobRecord record){if(record==null)throw new ArgumentNullException("record");this.record=record.Copy();}
        internal AgentJobRecord ToRecord(){return record.Copy();}
        public string Id {get{return record.Id;}}
        public AgentJobOwner Owner {get{return record.Owner;}}
        public string SourceTaskId {get{return record.SourceTaskId;}}
        public string SourceCallId {get{return record.SourceCallId;}}
        public string SourceReplyId {get{return record.SourceReplyId;}}
        public AgentJobSpec Spec {get{return record.Spec;}}
        public AgentJobState State {get{return record.State;}}
        public DateTime CreatedUtc {get{return record.CreatedUtc;}}
        public DateTime? StartedUtc {get{return record.StartedUtc;}}
        public DateTime? FinishedUtc {get{return record.FinishedUtc;}}
        public DateTime? RootExitObservedUtc {get{return record.RootExitObservedUtc;}}
        public DateTime? TreeExitObservedUtc {get{return record.TreeExitObservedUtc;}}
        public bool RootExited {get{return record.RootExited;}}
        public int? RootExitCode {get{return record.RootExitCode;}}
        public AgentJobStopReason? StopReason {get{return record.StopReason;}}
        public bool CleanupConfirmed {get{return record.CleanupConfirmed;}}
        public bool MayHaveExecuted {get{return record.MayHaveExecuted;}}
        public string LogWarning {get{return record.LogWarning;}}
        public IDictionary<string,long?> TimingsMs {get{return new ReadOnlyDictionary<string,long?>(new Dictionary<string,long?>(record.TimingsMs,StringComparer.Ordinal));}}
    }
    public sealed class AgentJobLogRecord
    {
        public long Sequence {get;private set;}
        public string Stream {get;private set;}
        public DateTime TimestampUtc {get;private set;}
        public string Text {get;private set;}
        public int FragmentOffset {get;private set;}
        public AgentJobLogRecord(long sequence,string stream,DateTime timestampUtc,string text,int fragmentOffset)
        {Sequence=sequence;Stream=stream;TimestampUtc=timestampUtc;Text=text;FragmentOffset=fragmentOffset;}
    }
    public sealed class AgentJobLogGap
    {
        public string Reason {get;private set;}
        public long DroppedBytes {get;private set;}
        public string ResumeCursor {get;private set;}
        public AgentJobLogGap(string reason,long droppedBytes,string resumeCursor){Reason=reason;DroppedBytes=droppedBytes;ResumeCursor=resumeCursor;}
    }
    public sealed class AgentJobPage
    {
        public AgentJobSnapshot Snapshot {get;private set;}
        public IList<AgentJobLogRecord> Records {get;private set;}
        public string NextCursor {get;private set;}
        public bool HasMore {get;private set;}
        public AgentJobLogGap Gap {get;private set;}
        public AgentJobPage(AgentJobSnapshot snapshot,IEnumerable<AgentJobLogRecord> records,string nextCursor,bool hasMore,AgentJobLogGap gap)
        {Snapshot=snapshot;Records=new List<AgentJobLogRecord>(records).AsReadOnly();NextCursor=nextCursor;HasMore=hasMore;Gap=gap;}
    }
    public sealed class AgentJobProcessExit
    {
        public bool RootExited {get;private set;}
        public int? RootExitCode {get;private set;}
        public bool TreeExited {get;private set;}
        public bool StreamsDrained {get;private set;}
        public int ActiveProcessCount {get;private set;}
        public AgentJobProcessExit(bool rootExited,int? rootExitCode,bool treeExited,bool streamsDrained,int activeProcessCount)
        {RootExited=rootExited;RootExitCode=rootExitCode;TreeExited=treeExited;StreamsDrained=streamsDrained;ActiveProcessCount=activeProcessCount;}
    }
    public sealed class AgentJobCleanupResult
    {
        public bool CleanupConfirmed {get;private set;}
        public string Error {get;private set;}
        public AgentJobCleanupResult(bool cleanupConfirmed,string error){CleanupConfirmed=cleanupConfirmed;Error=error;}
    }
    public sealed class AgentJobExitReport
    {
        public bool CleanupConfirmed {get;private set;}
        public IList<AgentJobSnapshot> Jobs {get;private set;}
        public long ElapsedMilliseconds {get;private set;}
        public AgentJobExitReport(bool cleanupConfirmed,IEnumerable<AgentJobSnapshot> jobs,long elapsedMilliseconds)
        {CleanupConfirmed=cleanupConfirmed;Jobs=new List<AgentJobSnapshot>(jobs).AsReadOnly();ElapsedMilliseconds=elapsedMilliseconds;}
    }
    public interface IAgentJobClock {DateTime UtcNow {get;}long ElapsedMilliseconds {get;}}
    public interface IAgentJobProcess : IDisposable
    {
        AgentJobProcessExit Status {get;}
        Task<AgentJobProcessExit> Completion {get;}
        void Resume();
        Task<AgentJobCleanupResult> StopAsync(CancellationToken token);
    }
    public interface IAgentJobProcessFactory
    {
        Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token);
    }
    // Optional adapter timing: only sandbox setup is excluded from the launch budget.
    public interface IAgentJobPreparationMetrics {long SandboxPreparationMilliseconds {get;}}
    public interface IAgentJobCompletionMetrics {long? EofDrainMilliseconds {get;}}
    public sealed class AgentJobPolicySnapshot
    {
        public string Root {get;private set;}
        public string DirectoryIdentity {get;private set;}
        public int PermissionVersion {get;private set;}
        public int TimeoutCeiling {get;private set;}
        public bool TerminalAllowed {get;private set;}
        public bool ReadOnly {get;private set;}
        public bool Sandbox {get;private set;}
        public AgentApprovalMode ApprovalMode {get;private set;}
        public IList<string> ReadDirectories {get;private set;}
        public AgentJobPolicySnapshot(string root,string directoryIdentity,int permissionVersion,int timeoutCeiling,bool terminalAllowed,bool readOnly,bool sandbox,AgentApprovalMode approvalMode,IEnumerable<string> readDirectories)
        {
            Root=root;DirectoryIdentity=directoryIdentity;PermissionVersion=permissionVersion;TimeoutCeiling=timeoutCeiling;TerminalAllowed=terminalAllowed;ReadOnly=readOnly;Sandbox=sandbox;ApprovalMode=approvalMode;ReadDirectories=new List<string>(readDirectories??new string[0]).AsReadOnly();
        }
    }
    public sealed class AgentJobStartContext
    {
        public AgentJobOwner Owner {get;private set;}
        public AgentToolInvocation Invocation {get;private set;}
        public IAgentAuthorizationService Authorization {get;private set;}
        public Func<AgentJobPolicySnapshot> RecheckPolicy {get;private set;}
        public Func<IDisposable> AcquireDirectory {get;private set;}
        public Action BeforeEffect {get;private set;}
        internal string ReplyId;
        public AgentJobStartContext(AgentJobOwner owner,AgentToolInvocation invocation,IAgentAuthorizationService authorization,Func<AgentJobPolicySnapshot> recheckPolicy,Func<IDisposable> acquireDirectory,Action beforeEffect)
        {
            if(owner==null||invocation==null||authorization==null||recheckPolicy==null||acquireDirectory==null)throw new ArgumentNullException("context");
            Owner=owner;Invocation=invocation;Authorization=authorization;RecheckPolicy=recheckPolicy;AcquireDirectory=acquireDirectory;BeforeEffect=beforeEffect;
        }
    }
}
