using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
namespace Dafeiyu.Tests
{
    internal static class RelationshipAtomicWriteChecks
    {
        const string Id="official:e-bao";
        static readonly RelationshipTime Time=RelationshipFixture.Time("2026-10-25T12:00:00Z");
        static bool WindowsSharing {get{return Environment.OSVersion.Platform==PlatformID.Win32NT;}}
        [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
        [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
        static extern bool CreateSymbolicLink(string link,string target,int flags);
        static void Check(bool value,string label){RelationshipFixture.Check(value,label);Console.WriteLine("PASS atomic write: "+label);}
        // A real reader refuses deletion. Release it only after observing the
        // actual sharing error on this writer, never by guessing the write time.
        static void TransientReader(string path,Action write)
        {
            var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            var signal=new ManualResetEvent(false);bool observed=false;int writer=Thread.CurrentThread.ManagedThreadId;
            EventHandler<FirstChanceExceptionEventArgs> trace=delegate(object sender,FirstChanceExceptionEventArgs args){if(Thread.CurrentThread.ManagedThreadId==writer&&args.Exception is IOException&&(args.Exception.HResult&65535)==32){observed=true;signal.Set();}};
            var release=new Thread(delegate(){signal.WaitOne(5000);Thread.Sleep(60);held.Dispose();});release.IsBackground=true;
            AppDomain.CurrentDomain.FirstChanceException+=trace;release.Start();
            try{write();}finally{signal.Set();if(!release.Join(10000))throw new TimeoutException("Owned sharing release");AppDomain.CurrentDomain.FirstChanceException-=trace;held.Dispose();signal.Dispose();}
            Check(observed,"transient fixture observed an actual native sharing error");
        }
        // Model the documented missing-destination aftermath using actual owned
        // files. This produces sharing error 32, not a fabricated 1176/1177.
        static string MissingDuringCommit(string path,string pattern,Action write)
        {
            byte[] original=File.ReadAllBytes(path);string displaced=path+".owned-displaced",candidate=null;Exception fixtureError=null;
            var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);FileStream candidateHeld=null;bool switched=false;int failures=0,writer=Thread.CurrentThread.ManagedThreadId;
            EventHandler<FirstChanceExceptionEventArgs> trace=delegate(object sender,FirstChanceExceptionEventArgs args){
                if(Thread.CurrentThread.ManagedThreadId!=writer||!(args.Exception is IOException)||(args.Exception.HResult&65535)!=32)return;
                failures++;if(switched){if(failures==6&&candidateHeld!=null){candidateHeld.Dispose();candidateHeld=null;}return;}
                switched=true;try{held.Dispose();candidate=Directory.GetFiles(Path.GetDirectoryName(path),pattern).Single();File.Move(path,displaced);candidateHeld=new FileStream(candidate,FileMode.Open,FileAccess.Read,FileShare.Read);}catch(Exception error){fixtureError=error;}
            };
            AppDomain.CurrentDomain.FirstChanceException+=trace;try{write();}finally{AppDomain.CurrentDomain.FirstChanceException-=trace;held.Dispose();if(candidateHeld!=null)candidateHeld.Dispose();}
            Check(switched&&fixtureError==null,"owned missing-destination fixture reached the real replacement boundary");
            Check(!File.Exists(path)&&File.Exists(candidate)&&File.ReadAllBytes(displaced).SequenceEqual(original),"terminal partial replacement preserves displaced original and complete surviving candidate");
            return candidate;
        }
        internal static void Ledger()
        {
            LedgerNeutral();if(!WindowsSharing){Console.WriteLine("SKIP Win32 ledger delete-sharing and partial-replace mechanism; platform-neutral ledger contracts passed");return;}
            using(var f=new RelationshipFixture()){
                Directory.CreateDirectory(f.Store.FilePath);Check(!f.Service.Record(Id,RelationshipEventType.Visit,"first-refused",Time).Success,"failed first creation never claims a saved ledger");Directory.Delete(f.Store.FilePath);
                Check(f.Service.GetSnapshot(Id).State==null&&f.Service.Record(Id,RelationshipEventType.Visit,"first-refused",Time).AwardedPoints==2,"known failed first creation cleans only its uncommitted candidate and can retry");
            }
            using(var f=new RelationshipFixture()){
                Check(f.Service.Record(Id,RelationshipEventType.Visit,"seed",Time).Success,"seed real ledger");
                TransientReader(f.Store.FilePath,delegate{Check(f.Service.Record(Id,RelationshipEventType.CompletedConversation,"transient",Time).Success,"transient ledger sharing recovers within existing bounded policy");});
                var saved=f.Service.GetSnapshot(Id).State;Check(saved.Score==4&&saved.Receipts.Count==2,"ledger recovery commits score and receipt exactly once");
                Check(f.Service.Record(Id,RelationshipEventType.CompletedConversation,"transient",Time).AwardedPoints==0,"ledger replay remains deduplicated");
                byte[] before=File.ReadAllBytes(f.Store.FilePath);
                using(var held=new FileStream(f.Store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))Check(!f.Service.Record(Id,RelationshipEventType.CompletedConversation,"persistent",Time.AddSeconds(30)).Success,"persistent ledger sharing is a handled failure");
                Check(File.ReadAllBytes(f.Store.FilePath).SequenceEqual(before),"persistent ledger sharing preserves exact original bytes");
                Check(f.Service.Record(Id,RelationshipEventType.CompletedConversation,"persistent",Time.AddSeconds(30)).AwardedPoints==2,"persistent failure can later commit the same logical event once");
                string candidate=Path.Combine(Path.GetDirectoryName(f.Store.FilePath),".state-"+Guid.NewGuid().ToString("N")+".tmp");File.Move(f.Store.FilePath,candidate);before=File.ReadAllBytes(candidate);
                bool refused=false;try{f.Service.GetSnapshot(Id);}catch(IOException){refused=true;}
                Check(refused&&File.ReadAllBytes(candidate).SequenceEqual(before)&&!File.Exists(f.Store.FilePath),"missing ledger with surviving complete candidate fails closed without guessed recovery");
                Check(!f.Service.Record(Id,RelationshipEventType.Visit,"must-not-reset",Time).Success&&File.ReadAllBytes(candidate).SequenceEqual(before),"orphan ledger cannot be overwritten by a fresh event");
            }
            using(var f=new RelationshipFixture()){
                Directory.CreateDirectory(Path.Combine(f.Root,"relationships"));File.WriteAllText(Path.Combine(f.Root,"relationships/.state-unrelated.tmp"),"unrelated");
                Check(f.Service.GetSnapshot(Id).State==null,"fresh empty ledger ignores names outside the controlled transaction pattern");
            }
            using(var f=new RelationshipFixture()){
                string directory=Path.Combine(f.Root,"relationships");Directory.CreateDirectory(directory);string candidate=Path.Combine(directory,".state-"+Guid.NewGuid().ToString("N")+".tmp");File.WriteAllText(candidate,"interrupted first write");
                Check(!f.Service.Record(Id,RelationshipEventType.Visit,"first",Time).Success&&File.ReadAllText(candidate)=="interrupted first write","interrupted first-write candidate blocks silent default initialization");
            }
            using(var f=new RelationshipFixture()){
                Check(f.Service.Record(Id,RelationshipEventType.Visit,"seed",Time).Success,"partial ledger fixture has authoritative bytes");
                string candidate=MissingDuringCommit(f.Store.FilePath,".state-*.tmp",delegate{Check(!f.Service.Record(Id,RelationshipEventType.CompletedConversation,"partial",Time).Success,"terminal partial ledger commit never reports success");});
                bool refused=false;try{f.Service.GetSnapshot(Id);}catch(IOException){refused=true;}Check(refused&&File.Exists(candidate),"partial ledger restart cannot silently expose empty history");
            }
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)using(var f=new RelationshipFixture()){
                string directory=Path.Combine(f.Root,"relationships");Directory.CreateDirectory(directory);string target=Path.Combine(f.Root,"protected.txt");File.WriteAllText(target,"protected");string candidate=Path.Combine(directory,".state-"+Guid.NewGuid().ToString("N")+".tmp");
                if(CreateSymbolicLink(candidate,target,2)){Check(File.Exists(candidate)&&(File.GetAttributes(candidate)&FileAttributes.ReparsePoint)!=0,"owned orphan link fixture is a real existing reparse file");Check(!f.Service.Record(Id,RelationshipEventType.Visit,"link",Time).Success&&File.ReadAllText(target)=="protected","controlled-name reparse candidate is refused without changing its target");}
                else Console.WriteLine("SKIP atomic orphan file symlink: privilege unavailable, error="+System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
        }
        internal static void Pending()
        {
            PendingNeutral();if(!WindowsSharing){Console.WriteLine("SKIP Win32 pending delete-sharing and partial-replace mechanism; platform-neutral pending contracts passed");return;}
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                string blocked=Path.Combine(f.Root,"relationships/pending.json");Directory.CreateDirectory(blocked);runtime.Record(Id,RelationshipEventType.Visit,"first-refused",Time);Check(runtime.Warning!=""&&runtime.PendingCount==1,"first outbox creation failure remains pending");Directory.Delete(blocked);runtime.RetryPending();Check(runtime.Warning==""&&runtime.PendingCount==0&&f.Service.GetSnapshot(Id).State.Score==2,"known failed first outbox creation remains retryable");
            }
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                runtime.RetryPending();string path=Path.Combine(f.Root,"relationships/pending.json");int callbacks=0,award=0;
                runtime.Committed+=delegate(string id,RelationshipResult result){callbacks++;award=result.AwardedPoints;};
                TransientReader(path,delegate{runtime.Record(Id,RelationshipEventType.Visit,"pending-transient",Time);Check(runtime.PendingCount==0&&runtime.Warning==""&&callbacks==1&&award==2,"transient pending sharing commits and notifies exactly once");});
                runtime.RetryPending();Check(callbacks==1&&f.Service.GetSnapshot(Id).State.Receipts.Count==1,"pending retry never duplicates a settled callback or receipt");
                byte[] before=File.ReadAllBytes(path);using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    runtime.Record(Id,RelationshipEventType.CompletedConversation,"pending-persistent",Time);Check(runtime.PendingCount==1&&runtime.Warning!=""&&callbacks==1,"persistent pending sharing retains the event without false success");
                }
                Check(File.ReadAllBytes(path).SequenceEqual(before),"persistent pending sharing preserves original outbox bytes");runtime.RetryPending();Check(callbacks==2&&runtime.PendingCount==0&&f.Service.GetSnapshot(Id).State.Score==4,"later pending recovery commits one original event");
            }
            using(var f=new RelationshipFixture()){
                string directory=Path.Combine(f.Root,"relationships");Directory.CreateDirectory(directory);string candidate=Path.Combine(directory,Guid.NewGuid().ToString("N")+".pending.tmp");File.WriteAllText(candidate,"[]");
                using(var runtime=new RelationshipRuntime(f.Root)){Check(runtime.RequiresRestart&&runtime.Warning!="","missing outbox with retained candidate blocks replay instead of assuming an empty queue");runtime.RecordVisit(Id);Check(!File.Exists(Path.Combine(directory,"pending.json"))&&File.ReadAllText(candidate)=="[]","blocked outbox retains candidate and never initializes over it");}
            }
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                runtime.RetryPending();string path=Path.Combine(f.Root,"relationships/pending.json");int callbacks=0;runtime.Committed+=delegate{callbacks++;};
                string candidate=MissingDuringCommit(path,"*.pending.tmp",delegate{runtime.Record(Id,RelationshipEventType.Visit,"partial",Time);Check(runtime.Warning!=""&&callbacks==0,"terminal partial outbox commit never claims delivery");});
                using(var restarted=new RelationshipRuntime(f.Root))Check(restarted.RequiresRestart&&File.Exists(candidate),"partial outbox restart preserves candidate and blocks replay");
            }
        }
        internal static void Holiday()
        {
            HolidayNeutral();if(!WindowsSharing){Console.WriteLine("SKIP Win32 holiday delete-sharing and partial-replace mechanism; platform-neutral holiday contracts passed");return;}
            using(var f=new RelationshipFixture()){
                var store=new HolidayKeepsakeStore(f.Root);using(store.Acquire())store.Save(new HolidayKeepsakeData());string path=Path.Combine(f.Root,"commemorations/holiday-state.json");
                TransientReader(path,delegate{using(store.Acquire())store.Save(store.Load());});byte[] before=File.ReadAllBytes(path);bool refused=false;
                using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){try{using(store.Acquire())store.Save(store.Load());}catch(IOException){refused=true;}}
                Check(refused&&File.ReadAllBytes(path).SequenceEqual(before),"persistent holiday sharing preserves complete original state");
                string candidate=MissingDuringCommit(path,".state-*.tmp",delegate{bool failed=false;try{using(store.Acquire())store.Save(store.Load());}catch(IOException){failed=true;}Check(failed,"terminal partial holiday commit is a failure");});
                bool blocked=false;try{store.Load();}catch(IOException){blocked=true;}Check(blocked&&File.Exists(candidate),"missing holiday state with candidate is never exposed as an empty album");
            }
            using(var f=new RelationshipFixture()){
                var store=new HolidayKeepsakeStore(f.Root);Check(store.Load().Records.Count==0,"fresh holiday store remains valid empty state");string path=Path.Combine(f.Root,"commemorations/holiday-state.json");Directory.CreateDirectory(path);bool failed=false;
                try{using(store.Acquire())store.Save(new HolidayKeepsakeData());}catch(IOException){failed=true;}Directory.Delete(path);using(store.Acquire())store.Save(new HolidayKeepsakeData());Check(failed&&store.Load().Records.Count==0,"known holiday first-creation failure remains retryable");
            }
        }
        internal static void PostLedgerRetirement()
        {
            if(!WindowsSharing){Console.WriteLine("SKIP Win32 post-ledger delete-sharing transition; persisted replay contracts remain shared");return;}
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                runtime.Record(Id,RelationshipEventType.Visit,"seed",Time);string pending=Path.Combine(f.Root,"relationships/pending.json");
                FileStream ledger=new FileStream(f.Store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read),outbox=null;int writer=Thread.CurrentThread.ManagedThreadId;bool switched=false;
                EventHandler<FirstChanceExceptionEventArgs> trace=delegate(object sender,FirstChanceExceptionEventArgs args){if(!switched&&Thread.CurrentThread.ManagedThreadId==writer&&args.Exception is IOException&&(args.Exception.HResult&65535)==32){outbox=new FileStream(pending,FileMode.Open,FileAccess.Read,FileShare.Read);switched=true;ledger.Dispose();}};
                int callbacks=0,award=0;runtime.Committed+=delegate(string id,RelationshipResult result){callbacks++;award=result.AwardedPoints;};
                AppDomain.CurrentDomain.FirstChanceException+=trace;
                try{runtime.Record(Id,RelationshipEventType.CompletedConversation,"retirement",Time);}
                finally{AppDomain.CurrentDomain.FirstChanceException-=trace;ledger.Dispose();if(outbox!=null)outbox.Dispose();}
                Check(switched&&f.Service.GetSnapshot(Id).State.Score==4,"controlled lock transition commits ledger before outbox retirement fails");
                Check(callbacks==0&&runtime.PendingCount==1&&runtime.Warning!="","failed post-ledger retirement retains its head without premature callback");
                runtime.RetryPending();Check(callbacks==1&&award==2&&runtime.PendingCount==0&&runtime.Warning=="","retirement recovery publishes the original award exactly once");
                runtime.RetryPending();Check(callbacks==1&&f.Service.GetSnapshot(Id).State.Score==4&&f.Service.GetSnapshot(Id).State.Receipts.Count==2,"retirement retry cannot accept or notify the event twice");
            }
        }
        internal static void PlatformNeutral()
        {LedgerNeutral();PendingNeutral();HolidayNeutral();Console.WriteLine("PASS platform-neutral atomic contracts on actual host "+Environment.OSVersion.Platform+"; no alternate-OS execution claimed");}
        static void LedgerNeutral()
        {
            using(var f=new RelationshipFixture()){
                Directory.CreateDirectory(f.Store.FilePath);Check(!f.Service.Record(Id,RelationshipEventType.Visit,"first",Time).Success,"shared failed first ledger creation remains failure");Directory.Delete(f.Store.FilePath);
                Check(f.Service.Record(Id,RelationshipEventType.Visit,"first",Time).Success,"shared first ledger retry saves one event");Check(f.Service.Record(Id,RelationshipEventType.Visit,"first",Time).AwardedPoints==0,"shared persisted ledger replay is deduplicated");
                string candidate=Path.Combine(Path.GetDirectoryName(f.Store.FilePath),".state-"+Guid.NewGuid().ToString("N")+".tmp");File.Move(f.Store.FilePath,candidate);byte[] bytes=File.ReadAllBytes(candidate);bool refused=false;try{f.Service.GetSnapshot(Id);}catch(IOException){refused=true;}
                Check(refused&&File.ReadAllBytes(candidate).SequenceEqual(bytes)&&!f.Service.Record(Id,RelationshipEventType.Visit,"new",Time).Success,"shared missing authoritative ledger preserves orphan and refuses defaults");
            }
            using(var f=new RelationshipFixture()){
                string directory=Path.Combine(f.Root,"relationships");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,".state-unrelated.tmp"),"unrelated");Check(f.Service.GetSnapshot(Id).State==null,"shared fresh ledger ignores uncontrolled names");
                string candidate=Path.Combine(directory,".state-"+Guid.NewGuid().ToString("N")+".tmp");File.WriteAllText(candidate,"interrupted first creation");Check(!f.Service.Record(Id,RelationshipEventType.Visit,"first",Time).Success&&File.ReadAllText(candidate)=="interrupted first creation","shared interrupted first creation is protected");
            }
        }
        static void PendingNeutral()
        {
            using(var f=new RelationshipFixture()){
                using(var runtime=new RelationshipRuntime(f.Root)){string path=Path.Combine(f.Root,"relationships/pending.json");Directory.CreateDirectory(path);runtime.Record(Id,RelationshipEventType.Visit,"first",Time);Check(runtime.Warning!=""&&runtime.PendingCount==1,"shared first pending creation failure remains pending");Directory.Delete(path);runtime.RetryPending();Check(runtime.PendingCount==0&&f.Service.GetSnapshot(Id).State.Score==2,"shared first pending retry succeeds");}
                using(var runtime=new RelationshipRuntime(f.Root)){runtime.Record(Id,RelationshipEventType.Visit,"first",Time);Check(f.Service.GetSnapshot(Id).State.Score==2&&f.Service.GetSnapshot(Id).State.Receipts.Count==1,"shared persisted pending replay remains deduplicated");}
            }
            using(var f=new RelationshipFixture()){
                string directory=Path.Combine(f.Root,"relationships");Directory.CreateDirectory(directory);string candidate=Path.Combine(directory,Guid.NewGuid().ToString("N")+".pending.tmp");File.WriteAllText(candidate,"[]");using(var runtime=new RelationshipRuntime(f.Root))Check(runtime.RequiresRestart&&File.ReadAllText(candidate)=="[]"&&!File.Exists(Path.Combine(directory,"pending.json")),"shared orphan pending candidate blocks empty initialization");
            }
        }
        static void HolidayNeutral()
        {
            using(var f=new RelationshipFixture()){
                var store=new HolidayKeepsakeStore(f.Root);Check(store.Load().Records.Count==0,"shared fresh holiday state remains empty");string path=Path.Combine(f.Root,"commemorations/holiday-state.json");Directory.CreateDirectory(path);bool failed=false;
                try{using(store.Acquire())store.Save(new HolidayKeepsakeData());}catch(IOException){failed=true;}Directory.Delete(path);using(store.Acquire())store.Save(new HolidayKeepsakeData());Check(failed&&store.Load().Records.Count==0,"shared first holiday creation failure can retry");
                string candidate=Path.Combine(Path.GetDirectoryName(path),".state-"+Guid.NewGuid().ToString("N")+".tmp");File.Move(path,candidate);byte[] bytes=File.ReadAllBytes(candidate);bool refused=false;try{store.Load();}catch(IOException){refused=true;}Check(refused&&File.ReadAllBytes(candidate).SequenceEqual(bytes),"shared orphan holiday candidate never becomes an empty album");
            }
        }
    }
}
