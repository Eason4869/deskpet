using System;
using System.IO;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class AgentJobStoreTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static void TerminalRecordsSurviveReopen()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string id=Guid.NewGuid().ToString("N");
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Succeeded,clock));
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    var records=store.Load();Check(records.Count==1&&records[0].Id==id&&records[0].State==AgentJobState.Succeeded,"terminal metadata survives store disposal and reopen");
                    Check(records[0].Spec.Command=="echo fixture"&&records[0].Owner.ConversationId=="conversation"&&records[0].RootExitCode==0,"recovered metadata retains exact command, owner and actual exit code");
                }
            }
        }
        static void SecondWriterDenied()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();
                using(var first=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    bool denied=false;try{using(var second=new AgentJobTestSupport.Store(temporary.Path,clock)){} }catch(IOException){denied=true;}
                    Check(denied,"a second store cannot obtain the same data-directory writer lease");
                }
                using(var next=new AgentJobTestSupport.Store(temporary.Path,clock))Check(next.Load().Count==0,"disposing the writer releases its owned lease");
            }
        }
        static void InvalidJobIdRejectedBeforePathUse()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                    foreach(var id in new[]{"../escape",temporary.Path,new string('A',32),new string('a',31),"."})
                    {
                        bool denied=false;try{store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Succeeded,clock));}catch(ArgumentException){denied=true;}
                        Check(denied,"invalid job ID cannot become a storage path: "+id);
                    }
                Check(!File.Exists(Path.Combine(temporary.Path,"escape.json")),"invalid metadata IDs create no external file");
            }
        }
        static void RecoverNonTerminalAsInterrupted()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string id=Guid.NewGuid().ToString("N");
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Running,clock));
                clock.Advance(TimeSpan.FromMinutes(1));
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    var recovered=store.Load().Single();Check(recovered.State==AgentJobState.Interrupted&&recovered.FinishedUtc==clock.UtcNow,"startup recovery terminates the stored logical state without relaunching it");
                    Check(recovered.MayHaveExecuted&&recovered.RootExitCode==null,"interruption does not invent a natural exit code or promise no side effects");
                }
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))Check(store.Load().Single().State==AgentJobState.Interrupted,"interrupted recovery state is itself persisted");
            }
        }
        static void CorruptRecordDoesNotEraseOtherJob()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string bad=Guid.NewGuid().ToString("N"),good=Guid.NewGuid().ToString("N");string directory;
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    store.Save(AgentJobTestSupport.Snapshot(temporary.Path,bad,AgentJobState.Succeeded,clock));store.Save(AgentJobTestSupport.Snapshot(temporary.Path,good,AgentJobState.Succeeded,clock));directory=store.DirectoryPath;
                }
                File.WriteAllText(Path.Combine(directory,bad+".json"),"{incomplete");
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))Check(store.Load().Single().Id==good,"one corrupt record is isolated without losing another completed job");
                Check(Directory.EnumerateFiles(directory,bad+"*.corrupt").Any(),"corrupt metadata is retained in quarantine for diagnosis");
            }
        }
        static void ReparseRootsAndMetadataRejected()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string target=Path.Combine(temporary.Path,"target"),link=Path.Combine(temporary.Path,"alias");Directory.CreateDirectory(target);
                AgentJobTestSupport.DirectoryLink(link,target);
                try{bool denied=false;try{using(var store=new AgentJobTestSupport.Store(link,clock)){} }catch(UnauthorizedAccessException){denied=true;}Check(denied,"a reparse-point data root cannot obtain a writer lease");}
                finally{Directory.Delete(link);}
                string sentinel=Path.Combine(target,"sentinel");File.WriteAllText(sentinel,"untouched");
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    string id=Guid.NewGuid().ToString("N"),metadata=Path.Combine(store.DirectoryPath,id+".json");AgentJobTestSupport.DirectoryLink(metadata,target);
                    try{bool denied=false;try{store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Succeeded,clock));}catch(UnauthorizedAccessException){denied=true;}Check(denied&&File.ReadAllText(sentinel)=="untouched","metadata reparse points cannot redirect a write outside the owned job root");}
                    finally{Directory.Delete(metadata);}
                }
            }
        }
        static void RetentionUsesFinishedTime()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string fresh=Guid.NewGuid().ToString("N"),expired=Guid.NewGuid().ToString("N");
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    var recent=AgentJobTestSupport.Snapshot(temporary.Path,fresh,AgentJobState.Succeeded,clock).ToRecord();recent.CreatedUtc=clock.UtcNow-TimeSpan.FromDays(100);store.Save(new AgentJobSnapshot(recent));
                    var old=AgentJobTestSupport.Snapshot(temporary.Path,expired,AgentJobState.Succeeded,clock).ToRecord();old.FinishedUtc=clock.UtcNow-TimeSpan.FromDays(8);store.Save(new AgentJobSnapshot(old));
                    var records=store.Load();Check(records.Count==1&&records[0].Id==fresh,"retention counts age from completion, not original creation");
                }
            }
        }
        static void HistoryBoundNeverDeletesActiveRecord()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string active=Guid.NewGuid().ToString("N"),oldest=null;
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    var running=AgentJobTestSupport.Snapshot(temporary.Path,active,AgentJobState.Running,clock).ToRecord();running.CreatedUtc-=TimeSpan.FromDays(100);store.Save(new AgentJobSnapshot(running));
                    for(int i=0;i<201;i++){string id=Guid.NewGuid().ToString("N");if(i==0)oldest=id;clock.Advance(TimeSpan.FromSeconds(1));store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Succeeded,clock));}
                    Check(File.Exists(Path.Combine(store.DirectoryPath,active+".json")),"history pruning never removes an old active job");
                    Check(Directory.GetFiles(store.DirectoryPath,"*.json").Length==201&&!File.Exists(Path.Combine(store.DirectoryPath,oldest+".json")),"history is bounded to 200 terminal records plus active jobs");
                }
            }
        }
        static void OversizedMetadataKeepsPreviousRecord()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();string id=Guid.NewGuid().ToString("N");
                using(var store=new AgentJobTestSupport.Store(temporary.Path,clock))
                {
                    var previous=AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Succeeded,clock);store.Save(previous);
                    string path=Path.Combine(store.DirectoryPath,id+".json");var before=File.ReadAllBytes(path);var oversized=previous.ToRecord();oversized.LogWarning=new string('x',70000);
                    bool denied=false;try{store.Save(new AgentJobSnapshot(oversized));}catch(IOException){denied=true;}
                    Check(denied&&before.SequenceEqual(File.ReadAllBytes(path)),"an oversized replacement cannot destroy the last committed metadata");
                    Check(!Directory.GetFiles(store.DirectoryPath,"*.tmp").Any(),"rejected metadata leaves no temporary write behind");
                }
            }
        }
        static void AtomicReplaceFailureKeepsPrevious()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    string id=Guid.NewGuid().ToString("N");var previous=AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Succeeded,clock);store.Save(previous);
                    var bytes=File.ReadAllBytes(Path.Combine(store.DirectoryPath,id+".json"));var changed=previous.ToRecord();changed.Spec=new AgentJobSpec("echo replacement",null,temporary.Path,1800);
                    io.FailCommit=true;bool denied=false;try{store.Save(new AgentJobSnapshot(changed));}catch(IOException){denied=true;}finally{io.FailCommit=false;}
                    Check(denied&&bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(store.DirectoryPath,id+".json"))),"a commit failure after temporary write preserves previous metadata bytes");
                    Check(store.Load().Single().Spec.Command=="echo fixture"&&!Directory.GetFiles(store.DirectoryPath,"*.tmp").Any(),"failed replacement leaves the old job readable and no temporary file");
                }
            }
        }
        static void QuotaReservesActiveJobs()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    string oldest=null;
                    for(int i=0;i<15;i++)
                    {
                        string id=Guid.NewGuid().ToString("N");if(i==0)oldest=id;clock.Advance(TimeSpan.FromSeconds(1));store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Succeeded,clock));
                        io.ReportedLengths[id+".0.log"]=4L*1024*1024;io.ReportedLengths[id+".1.log"]=4L*1024*1024;
                    }
                    string first=Guid.NewGuid().ToString("N"),second=Guid.NewGuid().ToString("N");
                    store.Save(AgentJobTestSupport.Snapshot(temporary.Path,first,AgentJobState.CleanupFailed,clock));store.Save(AgentJobTestSupport.Snapshot(temporary.Path,second,AgentJobState.Starting,clock));
                    Check(!File.Exists(Path.Combine(store.DirectoryPath,oldest+".json")),"a new active reservation evicts old terminal history before exceeding 128 MiB");
                    Check(File.Exists(Path.Combine(store.DirectoryPath,first+".json"))&&File.Exists(Path.Combine(store.DirectoryPath,second+".json")),"cleanup-failed and starting jobs retain their full log reservations");
                }
            }
        }
        static void OrphanQuotaRejectsStartBeforeMetadataCommit()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())using(var io=new AgentJobTestSupport.ControlledIO())
            {
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(temporary.Path,clock,io))
                {
                    for(int i=0;i<16;i++)foreach(int segment in new[]{0,1}){string name=i.ToString("x32")+"."+segment+".log";File.WriteAllBytes(Path.Combine(store.DirectoryPath,name),new byte[0]);io.ReportedLengths[name]=4L*1024*1024;}
                    string id=Guid.NewGuid().ToString("N");bool denied=false;try{store.Save(AgentJobTestSupport.Snapshot(temporary.Path,id,AgentJobState.Starting,clock));}catch(IOException){denied=true;}
                    Check(denied&&!File.Exists(Path.Combine(store.DirectoryPath,id+".json")),"unreclaimable orphan log usage prevents a new starting record from committing");
                }
            }
        }
        public static int Main()
        {
            checks=0;int failures=0;
            foreach(var test in new Action[]{TerminalRecordsSurviveReopen,SecondWriterDenied,InvalidJobIdRejectedBeforePathUse,RecoverNonTerminalAsInterrupted,CorruptRecordDoesNotEraseOtherJob,ReparseRootsAndMetadataRejected,RetentionUsesFinishedTime,HistoryBoundNeverDeletesActiveRecord,OversizedMetadataKeepsPreviousRecord,AtomicReplaceFailureKeepsPrevious,QuotaReservesActiveJobs,OrphanQuotaRejectsStartBeforeMetadataCommit})
                try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobStoreTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
