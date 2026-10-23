using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Dafeiyu.Tests
{
    internal static class AgentJobTestSupport
    {
        internal static void DirectoryLink(string path,string target)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)
            {
                var method=typeof(Directory).GetMethod("CreateSymbolicLink",new[]{typeof(string),typeof(string)});
                if(method==null)throw new PlatformNotSupportedException("Directory link fixture unavailable");
                method.Invoke(null,new object[]{path,target});return;
            }
            Directory.CreateDirectory(path);byte[] substitute=Encoding.Unicode.GetBytes("\\??\\"+target),printable=Encoding.Unicode.GetBytes(target);
            byte[] data=new byte[16+substitute.Length+2+printable.Length+2];
            Array.Copy(BitConverter.GetBytes(0xA0000003u),0,data,0,4);Array.Copy(BitConverter.GetBytes((ushort)(data.Length-8)),0,data,4,2);
            Array.Copy(BitConverter.GetBytes((ushort)substitute.Length),0,data,10,2);Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length+2)),0,data,12,2);Array.Copy(BitConverter.GetBytes((ushort)printable.Length),0,data,14,2);
            Array.Copy(substitute,0,data,16,substitute.Length);Array.Copy(printable,0,data,18+substitute.Length,printable.Length);
            using(var handle=OpenDirectory(path,0x40000000,0,IntPtr.Zero,3,0x02200000,IntPtr.Zero))
            {
                uint returned;if(handle.IsInvalid||!SetReparsePoint(handle,0x000900A4,data,data.Length,IntPtr.Zero,0,out returned,IntPtr.Zero))throw new IOException("Directory link fixture failed: "+Marshal.GetLastWin32Error());
            }
        }
        [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle OpenDirectory(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",EntryPoint="DeviceIoControl",SetLastError=true)]static extern bool SetReparsePoint(SafeFileHandle handle,uint code,byte[] input,int inputSize,IntPtr output,int outputSize,out uint returned,IntPtr overlapped);
        internal sealed class Clock : IAgentJobClock
        {
            public DateTime UtcNow {get;private set;}
            public long ElapsedMilliseconds {get;private set;}
            internal Clock(){UtcNow=new DateTime(2026,10,23,0,0,0,DateTimeKind.Utc);}
            internal void Advance(TimeSpan elapsed){UtcNow+=elapsed;ElapsedMilliseconds+=(long)elapsed.TotalMilliseconds;}
            internal void AdjustUtc(TimeSpan change){UtcNow+=change;}
        }
        internal static AgentJobSnapshot Snapshot(string directory,string id,AgentJobState state,IAgentJobClock clock)
        {
            bool terminal=state==AgentJobState.Succeeded||state==AgentJobState.Failed||state==AgentJobState.Stopped||state==AgentJobState.TimedOut||state==AgentJobState.StartFailed||state==AgentJobState.Interrupted;
            return new AgentJobSnapshot(new AgentJobRecord{Id=id,Owner=new AgentJobOwner("project","conversation","primary"),Spec=new AgentJobSpec("echo fixture",null,directory,1800),State=state,CreatedUtc=clock.UtcNow,StartedUtc=clock.UtcNow,FinishedUtc=terminal?(DateTime?)clock.UtcNow:null,RootExited=terminal,RootExitCode=terminal?(int?)0:null,CleanupConfirmed=terminal,InvocationKey=Hash("call:"+id),PayloadHash=Hash("payload:"+id)});
        }
        static string Hash(string value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        // This bridge lets RED tests execute against the real pre-feature assembly.
        internal sealed class Store : IDisposable
        {
            readonly AgentJobStore instance;
            internal Store(string dataDirectory,IAgentJobClock clock,AgentJobStorageIO io=null){instance=io==null?new AgentJobStore(dataDirectory,clock):new AgentJobStore(dataDirectory,clock,io);}
            internal string DirectoryPath {get{return instance.DirectoryPath;}}
            internal AgentJobLogStore OpenLog(string id){return instance.OpenLog(id);}
            internal void Save(AgentJobSnapshot snapshot){instance.SaveAsync(snapshot,CancellationToken.None).GetAwaiter().GetResult();}
            internal IList<AgentJobSnapshot> Load(){return instance.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();}
            public void Dispose(){instance.Dispose();}
        }
        internal sealed class Logs : IDisposable
        {
            readonly AgentJobLogStore instance;
            internal Logs(Store store,string id){instance=store.OpenLog(id);}
            internal int PendingBytes {get{return instance.PendingBytes;}}
            internal long DiskBytes {get{return instance.DiskBytes;}}
            internal long DroppedBytes {get{return instance.DroppedBytes;}}
            internal Exception Failure {get{return instance.Failure;}}
            internal event Action<Exception> Faulted {add{instance.Faulted+=value;}remove{instance.Faulted-=value;}}
            internal void Append(string stream,byte[] bytes,DateTime utc){instance.Append(new AgentJobOutputChunk(stream,bytes,bytes.Length,utc));}
            internal void Flush(){instance.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();}
            internal AgentJobPage Read(string cursor,int bytes){return instance.ReadAsync(cursor,bytes,CancellationToken.None).GetAwaiter().GetResult();}
            public void Dispose(){instance.Dispose();}
        }
        internal sealed class ControlledIO : AgentJobStorageIO,IDisposable
        {
            internal bool FailCommit,HoldWrites,HoldMetadataCommit;
            internal string FailAppendFor;
            internal long AppendedBytes;
            internal int IndexCommits;
            internal Action AfterMetadataCommit;
            internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim IndexSaved=new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim CommitEntered=new ManualResetEventSlim(false),CommitRelease=new ManualResetEventSlim(false);
            internal readonly Dictionary<string,long> ReportedLengths=new Dictionary<string,long>(StringComparer.Ordinal);
            internal override void Commit(string temporary,string destination){if(FailCommit)throw new IOException("Controlled commit failure");if(HoldMetadataCommit&&destination.EndsWith(".json",StringComparison.Ordinal)){CommitEntered.Set();CommitRelease.Wait();}base.Commit(temporary,destination);if(destination.EndsWith(".json",StringComparison.Ordinal)&&AfterMetadataCommit!=null)AfterMetadataCommit();if(destination.EndsWith(".log-index",StringComparison.Ordinal)){Interlocked.Increment(ref IndexCommits);IndexSaved.Set();}}
            internal override void Append(Stream stream,byte[] bytes)
            {
                if(HoldWrites){Entered.Set();Release.Wait();}
                var file=stream as FileStream;if(file!=null&&FailAppendFor!=null&&Path.GetFileName(file.Name).StartsWith(FailAppendFor+".",StringComparison.Ordinal))throw new IOException("Controlled log failure");
                base.Append(stream,bytes);Interlocked.Add(ref AppendedBytes,bytes.Length);
            }
            internal override long Length(string path){long value;return ReportedLengths.TryGetValue(Path.GetFileName(path),out value)?value:base.Length(path);}
            public void Dispose(){Release.Set();CommitRelease.Set();Entered.Dispose();Release.Dispose();IndexSaved.Dispose();CommitEntered.Dispose();CommitRelease.Dispose();}
        }
        internal sealed class TemporaryDirectory : IDisposable
        {
            readonly string parent;
            internal string Path { get; private set; }
            internal TemporaryDirectory()
            {
                parent=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"agent-job-fixtures"));
                Path=System.IO.Path.Combine(parent,"task 中文 "+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }
            public void Dispose()
            {
                string full=System.IO.Path.GetFullPath(Path);
                if(!full.StartsWith(parent+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Fixture escaped its test root");
                if(Directory.Exists(full))Directory.Delete(full,true);
            }
        }
        internal sealed class OfflineApi : IConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){throw new InvalidOperationException("Unexpected model call in job fixture");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wave,CancellationToken token){throw new InvalidOperationException("Unexpected transcription call in job fixture");}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token){throw new InvalidOperationException("Unexpected speech call in job fixture");}
            public void Dispose(){}
        }
        internal sealed class SilentSpeech : IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}
            public void SetVolume(int value){} public void Stop(){} public void Dispose(){}
        }
    }
}
