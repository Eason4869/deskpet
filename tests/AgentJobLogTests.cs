using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
namespace Dafeiyu.Tests
{
    public static class AgentJobLogTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static string Id(){return Guid.NewGuid().ToString("N");}
        static void Utf8SplitPreservesChinese()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));
                    using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        foreach(byte value in Encoding.UTF8.GetBytes("中文🐾\nstdout"))log.Append("stdout",new[]{value},clock.UtcNow);
                        foreach(byte value in Encoding.UTF8.GetBytes("错误流\nstderr"))log.Append("stderr",new[]{value},clock.UtcNow);
                        log.Flush();var page=log.Read("",1024);
                        Check(String.Concat(page.Records.Where(record=>record.Stream=="stdout").Select(record=>record.Text))=="中文🐾\nstdout","byte-fragmented stdout preserves Chinese, emoji and newlines");
                        Check(String.Concat(page.Records.Where(record=>record.Stream=="stderr").Select(record=>record.Text))=="错误流\nstderr","stderr uses a separate incremental UTF-8 decoder");
                        Check(!page.HasMore&&page.Gap==null,"a complete small log page has no invented gap");
                        log.Append("stdout",Encoding.UTF8.GetBytes("later"),clock.UtcNow);log.Flush();
                        var later=log.Read(page.NextCursor,1024);Check(String.Concat(later.Records.Select(record=>record.Text))=="later","a fresh get from the previous cursor observes new output only");
                        log.Append("stdout",new byte[]{0xE4},clock.UtcNow);log.Flush();Check(log.Read(later.NextCursor,1024).Records.Count==0,"a disk flush does not finalize an incomplete UTF-8 codepoint");
                        log.Append("stdout",new byte[]{0xB8,0xAD},clock.UtcNow);log.Flush();Check(log.Read(later.NextCursor,1024).Records.Single().Text=="中","UTF-8 continuation survives an intermediate flush");
                    }
                }
            }
        }
        static void MinPageAdvancesWithinLargeRecord()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        string original=new string('汉',2400);log.Append("stdout",Encoding.UTF8.GetBytes(original),clock.UtcNow);log.Flush();
                        string cursor="";var joined=new StringBuilder();int pages=0,expectedOffset=0;
                        while(true)
                        {
                            var page=log.Read(cursor,1024);int bytes=page.Records.Sum(record=>Encoding.UTF8.GetByteCount(record.Text));
                            Check(bytes>0&&bytes<=1024&&page.NextCursor!=cursor,"a minimum-size page advances without splitting UTF-8");
                            Check(page.Records[0].FragmentOffset==expectedOffset,"continuation records retain the original byte offset");
                            foreach(var record in page.Records)joined.Append(record.Text);expectedOffset+=bytes;cursor=page.NextCursor;pages++;
                            if(!page.HasMore)break;if(pages>20)throw new Exception("Log pagination failed to advance");
                        }
                        Check(pages>1&&joined.ToString()==original,"fragmented pages reassemble the exact original long record");
                    }
                }
            }
        }
        static void CursorIsBoundToJobAndGeneration()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string a=Id(),b=Id(),cursor;
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    store.Save(AgentJobTestSupport.Snapshot(temporary.Path,a,AgentJobState.Running,clock));store.Save(AgentJobTestSupport.Snapshot(temporary.Path,b,AgentJobState.Running,clock));
                    using(var first=new AgentJobTestSupport.Logs(store,a))using(var other=new AgentJobTestSupport.Logs(store,b))
                    {
                        first.Append("stdout",Encoding.UTF8.GetBytes("one"),clock.UtcNow);first.Flush();cursor=first.Read("",1024).NextCursor;
                        bool denied=false;try{other.Read(cursor,1024);}catch(ArgumentException){denied=true;}Check(denied,"a cursor from another job is rejected");
                        foreach(string invalid in new[]{"../outside","/absolute",(cursor[0]=='A'?"B":"A")+cursor.Substring(1)})
                        {denied=false;try{first.Read(invalid,1024);}catch(ArgumentException){denied=true;}Check(denied,"path-like or forged cursor is rejected");}
                        foreach(int limit in new[]{1023,65537}){denied=false;try{first.Read("",limit);}catch(ArgumentOutOfRangeException){denied=true;}Check(denied,"log page limits reject out-of-range values");}
                    }
                }
                using(var reopened=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    reopened.Load();using(var log=new AgentJobTestSupport.Logs(reopened,a))
                    {bool denied=false;try{log.Read(cursor,1024);}catch(ArgumentException){denied=true;}Check(denied,"a cursor from a previous recovery generation is rejected");Check(log.Read("",1024).Records.Single().Text=="one","recovery preserves readable output while replacing cursor generation");}
                }
            }
        }
        static void TwentyMiBStaysBoundedAndExpiredCursorReportsGap()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        byte[] chunk=Encoding.UTF8.GetBytes(new string('x',8192));log.Append("stdout",chunk,clock.UtcNow);log.Flush();string cursor=log.Read("",1024).NextCursor;int peak=0;
                        for(int i=0;i<2560;i++){log.Append("stdout",chunk,clock.UtcNow);peak=Math.Max(peak,log.PendingBytes);if(i%8==7)log.Flush();}
                        log.Flush();Check(peak<=256*1024&&log.PendingBytes==0,"serialized pending log data remains within 256 KiB and drains");
                        Check(log.DiskBytes>0&&log.DiskBytes<=8L*1024*1024,"more than 20 MiB output retains at most 8 MiB including framing and index");
                        var expired=log.Read(cursor,1024);Check(expired.Gap!=null&&expired.Gap.Reason=="rotated"&&expired.Gap.DroppedBytes>0,"an expired authenticated cursor reports rotation and a visible output gap");
                        Check(expired.Records.Count>0&&expired.NextCursor!=cursor,"expired cursor resumes at remaining data instead of stalling");
                    }
                }
            }
        }
        static void PartialTailDoesNotCorruptOtherJobs()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string a=Id(),b=Id(),directory;
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    directory=store.DirectoryPath;store.Save(AgentJobTestSupport.Snapshot(temporary.Path,a,AgentJobState.Running,clock));store.Save(AgentJobTestSupport.Snapshot(temporary.Path,b,AgentJobState.Running,clock));
                    using(var first=new AgentJobTestSupport.Logs(store,a))using(var second=new AgentJobTestSupport.Logs(store,b)){first.Append("stdout",Encoding.UTF8.GetBytes("first"),clock.UtcNow);second.Append("stdout",Encoding.UTF8.GetBytes("second"),clock.UtcNow);first.Flush();second.Flush();}
                }
                using(var tail=new FileStream(Path.Combine(directory,a+".0.log"),FileMode.Append,FileAccess.Write)){var bytes=Encoding.UTF8.GetBytes("{incomplete-tail");tail.Write(bytes,0,bytes.Length);}
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    store.Load();using(var first=new AgentJobTestSupport.Logs(store,a))using(var second=new AgentJobTestSupport.Logs(store,b))
                    {var page=first.Read("",1024);Check(page.Records.Single().Text=="first"&&page.Gap!=null&&page.Gap.Reason=="incomplete_tail","partial tail recovery retains complete records and reports the missing tail");Check(second.Read("",1024).Records.Single().Text=="second","one log's incomplete tail does not corrupt another job");}
                }
            }
        }
        static void SlowWriterDrainsPipe()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO{HoldWrites=true})
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        try
                        {
                            var chunk=Encoding.UTF8.GetBytes(new string('x',8192));log.Append("stdout",chunk,clock.UtcNow);Check(io.Entered.Wait(2000),"controlled writer reaches the held I/O boundary");
                            var producer=Task.Run(delegate{for(int i=0;i<128;i++)log.Append("stdout",chunk,clock.UtcNow);});bool returned=producer.Wait(2000);
                            Check(returned&&log.PendingBytes<=256*1024&&log.DroppedBytes>0,"a held disk cannot block pipe draining or grow the serialized queue beyond 256 KiB");
                        }
                        finally{io.Release.Set();}
                        log.Flush();var page=log.Read("",65536);Check(page.Gap!=null&&page.Gap.Reason=="buffer_overflow","dropped pending records produce an explicit output gap");
                    }
                }
            }
        }
        static void HeldWriterCleanupIsBoundedAndRetryable()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO{HoldWrites=true})
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        log.Append("stdout",Encoding.UTF8.GetBytes("held"),clock.UtcNow);Check(io.Entered.Wait(2000),"cleanup fixture holds a real writer operation");
                        Exception cleanup=null;var operation=Task.Run(delegate{try{log.Dispose();}catch(Exception error){cleanup=error;}});bool returned;
                        try{returned=operation.Wait(3000);}finally{io.Release.Set();}
                        Check(operation.Wait(5000),"held cleanup can finish once the injected disk is released");
                        Check(returned&&cleanup is IOException,"unconfirmed writer cleanup reports within its budget instead of hanging disposal");
                    }
                }
            }
        }
        static void FailedStorageIsIsolatedAndNotifiedOnce()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    string failed=Id(),other=Id();io.FailAppendFor=failed;store.Save(AgentJobTestSupport.Snapshot(temporary.Path,failed,AgentJobState.Running,clock));store.Save(AgentJobTestSupport.Snapshot(temporary.Path,other,AgentJobState.Running,clock));
                    using(var broken=new AgentJobTestSupport.Logs(store,failed))using(var healthy=new AgentJobTestSupport.Logs(store,other))
                    {
                        using(var notified=new ManualResetEventSlim(false))
                        {
                            int notifications=0;broken.Faulted+=error=>{Interlocked.Increment(ref notifications);notified.Set();};broken.Append("stdout",Encoding.UTF8.GetBytes("fail"),clock.UtcNow);
                            bool reported=false;try{broken.Flush();}catch(IOException){reported=true;}
                            healthy.Append("stdout",Encoding.UTF8.GetBytes("healthy"),clock.UtcNow);healthy.Flush();
                            Check(notified.Wait(2000)&&reported&&broken.Failure is IOException&&broken.PendingBytes==0&&notifications==1,"a failed writer reports one storage fault and releases pending waiters");
                            Check(healthy.Read("",1024).Records.Single().Text=="healthy"&&healthy.Failure==null,"one job's storage fault does not stop another job's log");
                        }
                    }
                }
            }
        }
        static void EmptyStoredRecordCannotStallPagination()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));
                    string prefix="{\"Version\":1,\"JobId\":\""+id+"\",\"Stream\":\"stdout\",\"TimestampUtc\":\"\\/Date(0)\\/\",\"Sequence\":";
                    File.WriteAllText(Path.Combine(store.DirectoryPath,id+".0.log"),prefix+"1,\"Text\":\"\"}\n"+prefix+"2,\"Text\":\"after empty\"}\n",new UTF8Encoding(false));
                    using(var log=new AgentJobTestSupport.Logs(store,id)){var page=log.Read("",1024);Check(String.Concat(page.Records.Select(record=>record.Text))=="after empty"&&!page.HasMore,"an empty stored record cannot trap the cursor ahead of readable output");}
                }
            }
        }
        static void IndexPersistsAfterOneSecondOfOutput()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        io.IndexSaved.Reset();clock.Advance(TimeSpan.FromMilliseconds(1001));log.Append("stdout",Encoding.UTF8.GetBytes("checkpoint"),clock.UtcNow);
                        Check(io.IndexSaved.Wait(2000),"output after one second commits an index without a read or explicit flush");
                        Check(File.ReadAllText(Path.Combine(store.DirectoryPath,id+".log-index")).Contains("\"NextSequence\":2"),"periodic checkpoint contains the next committed sequence");
                    }
                }
            }
        }
        static void ConcurrentReadAndRotationNeverRepeatRecords()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var ready=new ManualResetEventSlim(false))using(var committed=new SemaphoreSlim(0))using(var io=new AgentJobTestSupport.ControlledIO())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));
                    // Start just before the real rotation boundary; the separate 20 MiB test
                    // covers sustained throughput. This test isolates concurrent pagination.
                    long seededSequence=0;
                    for(int segment=0;segment<2;segment++)using(var seed=new StreamWriter(Path.Combine(store.DirectoryPath,id+"."+segment+".log"),false,new UTF8Encoding(false)))
                        for(int frame=0;frame<490;frame++)seed.WriteLine("{\"Version\":1,\"JobId\":\""+id+"\",\"Stream\":\"stdout\",\"Text\":\""+new string('s',8192)+"\",\"Sequence\":"+(++seededSequence)+",\"TimestampUtc\":\"/Date(0)/\"}");
                    using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        byte[] chunk=Encoding.UTF8.GetBytes(new string('x',8192));
                        string initialCursor="";AgentJobPage initial;
                        do{initial=log.Read(initialCursor,65536);initialCursor=initial.NextCursor;}while(initial.HasMore);
                        var producer=Task.Run(delegate{try{for(int i=0;i<64;i++){log.Append("stdout",chunk,clock.UtcNow);if(i%8==7){log.Flush();ready.Set();committed.Release();}}log.Flush();}finally{committed.Release();}});
                        try
                        {
                            Check(ready.Wait(5000),"concurrent reader starts after an actual committed batch");
                            string cursor=initialCursor;long sequence=seededSequence;int offset=-1,read=0,emptyPages=0;bool more=true;var budget=Stopwatch.StartNew();
                            while(!producer.IsCompleted||more)
                            {
                                if(budget.ElapsedMilliseconds>30000){Console.WriteLine("Concurrent diagnostics: producer="+producer.Status+" pending="+log.PendingBytes+" dropped="+log.DroppedBytes+" written="+Interlocked.Read(ref io.AppendedBytes)+" indexes="+io.IndexCommits+" read_records="+read+" empty_pages="+emptyPages);throw new TimeoutException("Concurrent log reader budget");}
                                // EOF is not a request to spin: wait for a committed batch (or producer
                                // completion). Reads still overlap subsequent writes and segment rotation.
                                if(!more&&!producer.IsCompleted&&!committed.Wait(Math.Max(1,30000-(int)budget.ElapsedMilliseconds)))throw new TimeoutException("Concurrent log commit budget");
                                var page=log.Read(cursor,65536);cursor=page.NextCursor;more=page.HasMore;
                                if(page.Records.Count==0)emptyPages++;
                                foreach(var record in page.Records){if(record.Sequence<sequence||(record.Sequence==sequence&&record.FragmentOffset<=offset))throw new Exception("Concurrent log cursor repeated an earlier record");sequence=record.Sequence;offset=record.FragmentOffset;read++;}
                            }
                            producer.GetAwaiter().GetResult();Console.WriteLine("Concurrent diagnostics: elapsed_ms="+budget.ElapsedMilliseconds+" written="+Interlocked.Read(ref io.AppendedBytes)+" indexes="+io.IndexCommits+" read_records="+read+" empty_pages="+emptyPages);Check(read==64&&log.DroppedBytes>0&&log.DiskBytes<=8L*1024*1024,"concurrent pages advance monotonically while complete segments rotate");
                        }
                        finally{producer.GetAwaiter().GetResult();}
                    }
                }
            }
        }
        static void ManyTinyRecordsUseBoundedPages()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    string id=Id();store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));using(var log=new AgentJobTestSupport.Logs(store,id))
                    {
                        for(int i=0;i<300;i++)log.Append("stdout",new byte[]{(byte)'x'},clock.UtcNow);log.Flush();
                        var first=log.Read("",65536);var second=log.Read(first.NextCursor,65536);
                        Check(first.Records.Count<=256&&first.HasMore,"tiny output records cannot expand a single page into unbounded JSON metadata");
                        Check(first.Records.Sum(record=>record.Text.Length)+second.Records.Sum(record=>record.Text.Length)==300&&!second.HasMore,"record-count paging preserves all tiny output");
                    }
                }
            }
        }
        public static int RunAll(){return Run(null);}
        public static int Main(string[] args)
        {
            if(args.Length==0)return RunAll();
            if(args.Length==2&&args[0]=="--case")return Run(args[1]);
            Console.Error.WriteLine("Usage: AgentJobLogTests [--case <exact-test-name>]");return 2;
        }
        static int Run(string selected)
        {
            checks=0;int failures=0;
            var tests=new Action[]{Utf8SplitPreservesChinese,MinPageAdvancesWithinLargeRecord,CursorIsBoundToJobAndGeneration,TwentyMiBStaysBoundedAndExpiredCursorReportsGap,PartialTailDoesNotCorruptOtherJobs,SlowWriterDrainsPipe,HeldWriterCleanupIsBoundedAndRetryable,FailedStorageIsIsolatedAndNotifiedOnce,EmptyStoredRecordCannotStallPagination,IndexPersistsAfterOneSecondOfOutput,ConcurrentReadAndRotationNeverRepeatRecords,ManyTinyRecordsUseBoundedPages};
            if(selected!=null){tests=tests.Where(test=>test.Method.Name==selected).ToArray();if(tests.Length!=1)return 2;}
            foreach(var test in tests)
                try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobLogTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
