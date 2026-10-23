using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class AgentJobLogStore : IDisposable
    {
        const int IndexMaximum=8192,FrameMaximum=65536,PageRecordMaximum=256;
        const long SegmentMaximum=(AgentJobPolicy.MaximumJobLogBytes-2*IndexMaximum)/2;
        static readonly UTF8Encoding utf8=new UTF8Encoding(false,true);
        readonly object appendGate=new object(),diskGate=new object();
        readonly Queue<Pending> queue=new Queue<Pending>();
        readonly Dictionary<string,Decoder> decoders=new Dictionary<string,Decoder>{{"stdout",Encoding.UTF8.GetDecoder()},{"stderr",Encoding.UTF8.GetDecoder()}};
        readonly List<Location> locations=new List<Location>();
        readonly SemaphoreSlim signal=new SemaphoreSlim(0,1);
        readonly AgentJobSnapshot snapshot;
        readonly IAgentJobClock clock;
        readonly Action ensureOwned;
        readonly AgentJobStorageIO io;
        readonly string directory,id,generation,indexPath;
        readonly byte[] cursorKey=new byte[32];
        readonly Task writer;
        TaskCompletionSource<bool> drained=CompletedDrain();
        FileStream segment;
        long nextSequence=1,droppedBytes,lastRotatedSequence,lastIndexMilliseconds;
        int activeSegment,pendingBytes;
        bool stopping,disposed,partialTail;
        Exception failure;
        public event Action<Exception> Faulted;
        [DataContract] sealed class Frame
        {
            [DataMember] public int Version=1;
            [DataMember] public string JobId,Stream,Text;
            [DataMember] public long Sequence;
            [DataMember] public DateTime TimestampUtc;
        }
        [DataContract] sealed class Index
        {
            [DataMember] public int Version=1;
            [DataMember] public string JobId,Generation,Key;
            [DataMember] public long DroppedBytes,NextSequence,LastRotatedSequence;
        }
        sealed class Location {internal long Sequence,Offset;internal int Segment,Length,BodyBytes;}
        sealed class Pending {internal Frame Frame;internal byte[] Bytes;internal int BodyBytes;}
        sealed class Position {internal long Sequence,Loss;internal int Offset;}
        internal AgentJobLogStore(string directory,AgentJobSnapshot snapshot,IAgentJobClock clock,Action ensureOwned,AgentJobStorageIO io)
        {
            this.directory=directory;this.snapshot=snapshot;this.clock=clock;this.ensureOwned=ensureOwned;this.io=io;id=snapshot.Id;generation=Guid.NewGuid().ToString("N");indexPath=Path.Combine(directory,id+".log-index");
            using(var random=RandomNumberGenerator.Create())random.GetBytes(cursorKey);
            try
            {
                ensureOwned();AgentInstructions.RejectLinks(indexPath);
                if(File.Exists(indexPath))
                {
                    try{var previous=ReadObject<Index>(ReadBounded(indexPath,IndexMaximum));if(previous.Version!=1||previous.JobId!=id||previous.DroppedBytes<0||previous.LastRotatedSequence<0)throw new SerializationException();droppedBytes=previous.DroppedBytes;lastRotatedSequence=previous.LastRotatedSequence;}
                    catch(Exception error){if(!(error is SerializationException||error is ArgumentException||error is System.Xml.XmlException))throw;partialTail=true;}
                }
                Scan(0);Scan(1);locations.Sort((a,b)=>a.Sequence.CompareTo(b.Sequence));
                if(locations.Count>0){nextSequence=checked(locations[locations.Count-1].Sequence+1);activeSegment=locations[locations.Count-1].Segment;}
                WriteIndex();writer=Task.Run((Func<Task>)WriteLoopAsync);
            }
            catch{if(segment!=null)segment.Dispose();signal.Dispose();throw;}
        }
        static TaskCompletionSource<bool> NewDrain(){return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);}
        static TaskCompletionSource<bool> CompletedDrain(){var result=NewDrain();result.SetResult(true);return result;}
        public int PendingBytes {get{lock(appendGate)return pendingBytes;}}
        public long DroppedBytes {get{return Interlocked.Read(ref droppedBytes);}}
        public Exception Failure {get{lock(appendGate)return failure;}}
        public long DiskBytes {get{lock(diskGate)return io.Length(SegmentPath(0))+io.Length(SegmentPath(1))+io.Length(indexPath);}}
        string SegmentPath(int number){return Path.Combine(directory,id+"."+number.ToString(CultureInfo.InvariantCulture)+".log");}
        void EnsureReadable(){ensureOwned();if(disposed)throw new ObjectDisposedException("AgentJobLogStore");}
        static byte[] Serialize<T>(T value){using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(memory,value);return memory.ToArray();}}
        static T ReadObject<T>(byte[] bytes){using(var memory=new MemoryStream(bytes))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}
        static byte[] ReadBounded(string path,int maximum)
        {
            AgentInstructions.RejectLinks(path);using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length>maximum)throw new SerializationException("job_log_record_too_large");var bytes=new byte[(int)stream.Length];int offset=0,count;
                while(offset<bytes.Length&&(count=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=count;
                if(offset!=bytes.Length)throw new IOException("job_log_record_incomplete");return bytes;
            }
        }
        static int BodyBytes(Frame frame,string id)
        {
            if(frame==null||frame.Version!=1||frame.JobId!=id||frame.Sequence<1||frame.Sequence==Int64.MaxValue||(frame.Stream!="stdout"&&frame.Stream!="stderr")||frame.Text==null)throw new SerializationException("job_log_record_invalid");
            int count=utf8.GetByteCount(frame.Text);if(count>AgentJobPolicy.MaximumLogRecordBytes)throw new SerializationException("job_log_record_invalid");return count;
        }
        void Scan(int number)
        {
            string path=SegmentPath(number);AgentInstructions.RejectLinks(path);if(!File.Exists(path))return;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read))
            {
                if(stream.Length>SegmentMaximum)throw new IOException("job_log_segment_too_large");
                long start=0,last=0;var line=new MemoryStream();bool invalid=false;
                try
                {
                    int value;while((value=stream.ReadByte())!=-1)
                    {
                        if(value!=10){if(line.Length>=FrameMaximum){invalid=true;break;}line.WriteByte((byte)value);continue;}
                        try
                        {
                            var frame=ReadObject<Frame>(line.ToArray());int body=BodyBytes(frame,id);if(frame.Sequence<=last)throw new SerializationException();
                            locations.Add(new Location{Sequence=frame.Sequence,Segment=number,Offset=start,Length=checked((int)(stream.Position-start)),BodyBytes=body});last=frame.Sequence;
                        }
                        catch(Exception error){if(!(error is SerializationException||error is ArgumentException||error is System.Xml.XmlException))throw;invalid=true;break;}
                        start=stream.Position;line.SetLength(0);
                    }
                    if(invalid||line.Length>0){stream.SetLength(start);stream.Flush(true);partialTail=true;}
                }
                finally{line.Dispose();}
            }
        }
        void WriteIndex()
        {
            ensureOwned();var bytes=Serialize(new Index{JobId=id,Generation=generation,Key=Convert.ToBase64String(cursorKey),DroppedBytes=DroppedBytes,LastRotatedSequence=lastRotatedSequence,NextSequence=Interlocked.Read(ref nextSequence)});
            if(bytes.Length>IndexMaximum)throw new IOException("job_log_index_too_large");io.WriteAtomic(indexPath,bytes);lastIndexMilliseconds=clock.ElapsedMilliseconds;
        }
        public void Append(AgentJobOutputChunk chunk)
        {
            if(chunk==null)throw new ArgumentNullException("chunk");var input=chunk.Bytes;
            lock(appendGate)
            {
                EnsureReadable();if(stopping)throw new InvalidOperationException("job_log_closed");if(failure!=null)throw new IOException("log_storage_failed",failure);
                var characters=new char[input.Length+2];int count=decoders[chunk.Stream].GetChars(input,0,input.Length,characters,0,false);
                if(count==0)return;var body=utf8.GetBytes(characters,0,count);int offset=0;
                while(offset<body.Length)
                {
                    int take=Math.Min(AgentJobPolicy.MaximumLogRecordBytes,body.Length-offset);while(offset+take<body.Length&&(body[offset+take]&0xC0)==0x80)take--;
                    var frame=new Frame{JobId=id,Sequence=nextSequence++,Stream=chunk.Stream,TimestampUtc=chunk.TimestampUtc,Text=utf8.GetString(body,offset,take)};
                    byte[] json=Serialize(frame),bytes=new byte[json.Length+1];Buffer.BlockCopy(json,0,bytes,0,json.Length);bytes[bytes.Length-1]=10;
                    if(bytes.Length>FrameMaximum)throw new IOException("job_log_record_too_large");
                    while(pendingBytes+bytes.Length>AgentJobPolicy.MaximumPendingLogBytes&&queue.Count>0){var removed=queue.Dequeue();pendingBytes-=removed.Bytes.Length;Interlocked.Add(ref droppedBytes,removed.BodyBytes);}
                    if(pendingBytes==0)drained=NewDrain();
                    queue.Enqueue(new Pending{Frame=frame,Bytes=bytes,BodyBytes=take});pendingBytes+=bytes.Length;if(signal.CurrentCount==0)signal.Release();offset+=take;
                }
            }
        }
        async Task WriteLoopAsync()
        {
            while(true)
            {
                Pending item=null;
                lock(appendGate){if(queue.Count>0)item=queue.Dequeue();else if(stopping)return;}
                if(item==null){await signal.WaitAsync().ConfigureAwait(false);continue;}
                try{lock(diskGate)WriteFrame(item);}
                catch(Exception error)
                {
                    lock(appendGate){failure=error;queue.Clear();pendingBytes=0;drained.TrySetException(new IOException("log_storage_failed",error));}
                    var changed=Faulted;if(changed!=null)try{changed(error);}catch(Exception notification){System.Diagnostics.Debug.WriteLine(notification);}return;
                }
                lock(appendGate){pendingBytes-=item.Bytes.Length;if(pendingBytes==0)drained.TrySetResult(true);}
            }
        }
        void WriteFrame(Pending item)
        {
            EnsureReadable();string path=SegmentPath(activeSegment);AgentInstructions.RejectLinks(path);
            if(segment==null)segment=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);
            if(segment.Length+item.Bytes.Length>SegmentMaximum)
            {
                segment.Dispose();segment=null;activeSegment=1-activeSegment;path=SegmentPath(activeSegment);AgentInstructions.RejectLinks(path);
                var removed=locations.Where(location=>location.Segment==activeSegment).ToArray();
                if(removed.Length>0){lastRotatedSequence=Math.Max(lastRotatedSequence,removed.Max(location=>location.Sequence));Interlocked.Add(ref droppedBytes,removed.Sum(location=>(long)location.BodyBytes));locations.RemoveAll(location=>location.Segment==activeSegment);}
                segment=new FileStream(path,FileMode.Create,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);WriteIndex();
            }
            long offset=segment.Length;segment.Position=offset;io.Append(segment,item.Bytes);segment.Flush();
            locations.Add(new Location{Sequence=item.Frame.Sequence,Segment=activeSegment,Offset=offset,Length=item.Bytes.Length,BodyBytes=item.BodyBytes});
            if(clock.ElapsedMilliseconds-lastIndexMilliseconds>=1000)WriteIndex();
        }
        public async Task FlushAsync(CancellationToken token)
        {
            Task<bool> wait;lock(appendGate){EnsureReadable();if(failure!=null)throw new IOException("log_storage_failed",failure);wait=drained.Task;}
            await AgentAuthorizationService.AwaitCancellation(wait,token).ConfigureAwait(false);
            await Task.Run(delegate{token.ThrowIfCancellationRequested();lock(diskGate){EnsureReadable();if(segment!=null)segment.Flush(true);WriteIndex();}},token).ConfigureAwait(false);
        }
        static string Base64(byte[] value){return Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');}
        static byte[] Unbase64(string value){value=value.Replace('-','+').Replace('_','/');return Convert.FromBase64String(value+new string('=',(4-value.Length%4)%4));}
        string Cursor(long sequence,int offset)
        {
            byte[] payload=Encoding.ASCII.GetBytes(String.Join("|",new[]{id,generation,sequence.ToString(CultureInfo.InvariantCulture),offset.ToString(CultureInfo.InvariantCulture),DroppedBytes.ToString(CultureInfo.InvariantCulture)}));
            using(var hmac=new HMACSHA256(cursorKey))return Base64(payload)+"."+Base64(hmac.ComputeHash(payload));
        }
        Position ParseCursor(string cursor)
        {
            if(String.IsNullOrEmpty(cursor))return new Position{Sequence=1};
            try
            {
                if(cursor.Length>512)throw new FormatException();var pieces=cursor.Split('.');if(pieces.Length!=2)throw new FormatException();byte[] payload=Unbase64(pieces[0]),signature=Unbase64(pieces[1]),expected;
                using(var hmac=new HMACSHA256(cursorKey))expected=hmac.ComputeHash(payload);int different=signature.Length^expected.Length;for(int i=0;i<expected.Length;i++)different|=expected[i]^(i<signature.Length?signature[i]:0);if(different!=0)throw new FormatException();
                var fields=Encoding.ASCII.GetString(payload).Split('|');long sequence,loss;int offset;
                if(fields.Length!=5||fields[0]!=id||fields[1]!=generation||!Int64.TryParse(fields[2],NumberStyles.None,CultureInfo.InvariantCulture,out sequence)||sequence<1||!Int32.TryParse(fields[3],NumberStyles.None,CultureInfo.InvariantCulture,out offset)||offset<0||!Int64.TryParse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture,out loss)||loss<0)throw new FormatException();
                return new Position{Sequence=sequence,Offset=offset,Loss=loss};
            }
            catch(FormatException){throw new ArgumentException("invalid_cursor");}
        }
        Frame ReadFrame(Location location,Dictionary<int,FileStream> readers)
        {
            FileStream stream;if(!readers.TryGetValue(location.Segment,out stream)){string path=SegmentPath(location.Segment);AgentInstructions.RejectLinks(path);stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);readers.Add(location.Segment,stream);}
            stream.Position=location.Offset;var bytes=new byte[location.Length-1];int offset=0,count;while(offset<bytes.Length&&(count=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=count;
            if(offset!=bytes.Length)throw new IOException("job_log_record_incomplete");var frame=ReadObject<Frame>(bytes);BodyBytes(frame,id);return frame;
        }
        public Task<AgentJobPage> ReadAsync(string cursor,int limitBytes,CancellationToken token)
        {
            if(limitBytes<AgentJobPolicy.MinimumPageBytes||limitBytes>AgentJobPolicy.MaximumPageBytes)throw new ArgumentOutOfRangeException("limit_bytes");
            return Task.Run(delegate
            {
                lock(diskGate)
                {
                    EnsureReadable();var position=ParseCursor(cursor);var output=new List<AgentJobLogRecord>();var readers=new Dictionary<int,FileStream>();string gap=String.IsNullOrEmpty(cursor)&&partialTail?"incomplete_tail":null;int remaining=limitBytes;
                    int at=locations.FindIndex(location=>location.Sequence>=position.Sequence);
                    try
                    {
                        while(at>=0&&at<locations.Count&&remaining>0&&output.Count<PageRecordMaximum)
                        {
                            token.ThrowIfCancellationRequested();var location=locations[at];
                            if(location.Sequence>position.Sequence){if(gap==null)gap=position.Sequence<=lastRotatedSequence?"rotated":"buffer_overflow";position.Sequence=location.Sequence;position.Offset=0;}
                            var frame=ReadFrame(location,readers);byte[] body=utf8.GetBytes(frame.Text);if(position.Offset>body.Length||(position.Offset<body.Length&&(body[position.Offset]&0xC0)==0x80))throw new ArgumentException("invalid_cursor");
                            if(position.Offset==body.Length){position.Sequence++;position.Offset=0;at++;continue;}
                            int take=Math.Min(remaining,body.Length-position.Offset);while(position.Offset+take<body.Length&&(body[position.Offset+take]&0xC0)==0x80)take--;if(take==0)break;
                            output.Add(new AgentJobLogRecord(frame.Sequence,frame.Stream,frame.TimestampUtc,utf8.GetString(body,position.Offset,take),position.Offset));position.Offset+=take;remaining-=take;
                            if(position.Offset==body.Length){position.Sequence++;position.Offset=0;at++;}
                        }
                        bool more=at>=0&&at<locations.Count;
                        return new AgentJobPage(snapshot,output,Cursor(position.Sequence,position.Offset),more,gap==null?null:new AgentJobLogGap(gap,Math.Max(0,DroppedBytes-position.Loss),Cursor(position.Sequence,position.Offset)));
                    }
                    finally{foreach(var reader in readers.Values)reader.Dispose();}
                }
            },token);
        }
        public void Dispose()
        {
            lock(appendGate){if(disposed)return;stopping=true;if(signal.CurrentCount==0)signal.Release();}
            var budget=Stopwatch.StartNew();
            using(var deadline=new CancellationTokenSource(2000))
            {
                try{FlushAsync(deadline.Token).GetAwaiter().GetResult();}
                catch(OperationCanceledException){throw new IOException("job_log_cleanup_unconfirmed");}
                catch(IOException){}
            }
            if(!writer.Wait(Math.Max(0,2000-(int)budget.ElapsedMilliseconds)))throw new IOException("job_log_cleanup_unconfirmed");
            lock(diskGate){if(segment!=null){segment.Dispose();segment=null;}lock(appendGate)disposed=true;signal.Dispose();}
        }
    }
}
