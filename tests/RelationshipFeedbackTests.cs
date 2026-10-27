using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class RelationshipFeedbackTests
    {
        static int checks;
        static void Check(bool value,string text){RelationshipFixture.Check(value,text);checks++;Console.WriteLine("PASS "+text);}
        sealed class Clock : IRelationshipFeedbackScheduler
        {
            sealed class Ticket : IDisposable {public Action Action;public void Dispose(){Action=null;}}
            readonly List<Ticket> tickets=new List<Ticket>();
            public IDisposable Schedule(TimeSpan delay,Action action){var t=new Ticket{Action=action};tickets.Add(t);return t;}
            public Action[] PostedCallbacks(){return tickets.Where(t=>t.Action!=null).Select(t=>t.Action).ToArray();}
            public void Advance(){foreach(var t in tickets.ToArray()){var action=t.Action;t.Dispose();if(action!=null)action();}tickets.Clear();}
        }
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("RelationshipFeedbackTests: "+checks+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run()
        {
            var policy=typeof(RelationshipRuntime).Assembly.GetType("Dafeiyu.RelationshipFeedbackPolicy");Check(policy!=null,"shared brief feedback and greeting probability policy exists");
            var choose=policy.GetMethod("ShowInteraction",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);Check(choose!=null,"greeting admission has a deterministic probability boundary");
            Func<double,bool> show=value=>(bool)choose.Invoke(null,new object[]{value});int selected=0;for(int n=0;n<1000;n++)if(show(n/1000.0))selected++;
            Check(selected==100&&show(0)&&show(.099)&&!show(.1)&&!show(1)&&!show(-1)&&!show(Double.NaN),"greetings select ten percent and reject invalid samples");
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                var clock=new Clock();bool allowed=true;string current="official:e-bao";var notices=new List<RelationshipFeedbackNotice>();
                using(var feedback=new RelationshipFeedback(runtime,id=>allowed&&id==current,notices.Add,clock)){
                    var time=RelationshipFixture.Time("2026-10-01T12:00:00Z");
                    var one=f.Service.RecordChecked(current,RelationshipEventType.Visit,"visit",time);
                    await feedback.AcceptAsync(current,one);
                    var two=f.Service.RecordChecked(current,RelationshipEventType.CompletedConversation,"chat",time.AddSeconds(1));
                    await feedback.AcceptAsync(current,two);clock.Advance();await feedback.PendingDisplay;
                    Check(notices.Count==1&&notices[0].AwardedPoints==4&&notices[0].Milestones.Count==0,"nearby saved gains merge; first meeting is only a home keepsake");
                    allowed=false;await feedback.AcceptAsync(current,f.Service.RecordChecked(current,RelationshipEventType.CompletedConversation,"quiet",time.AddSeconds(2)));allowed=true;clock.Advance();await feedback.PendingDisplay;Check(notices.Count==1,"quiet gains have no delayed replay");
                    f.Service.UpdateSettings(true,false,true);await feedback.AcceptAsync(current,f.Service.RecordChecked(current,RelationshipEventType.CompletedConversation,"disabled",time.AddSeconds(3)));f.Service.UpdateSettings(true,true,true);clock.Advance();await feedback.PendingDisplay;Check(notices.Count==1,"disabled feedback has no delayed replay");
                    // Reach 80 through the real ledger, without submitting artificial snapshots.
                    RelationshipResult crossed=null;
                    for(int d=1;d<=10;d++)for(int n=0;n<4;n++)crossed=f.Service.RecordChecked(current,RelationshipEventType.CompletedConversation,"seed-"+d+"-"+n,time.AddSeconds(d*86400+n));
                    await feedback.AcceptAsync(current,crossed);clock.Advance();await feedback.PendingDisplay;Check(notices.Last().Milestones.Single().StageId=="familiar","real saved stage claims produce a grouped growth notice");
                    int count=notices.Count;await feedback.AcceptAsync(current,crossed);clock.Advance();await feedback.PendingDisplay;Check(notices.Count==count+1&&notices.Last().Milestones.Count==0,"repeated completion cannot repeat a stage popup");
                    using(var restarted=new RelationshipFeedback(runtime,id=>true,notices.Add,clock)){await restarted.AcceptAsync(current,crossed);clock.Advance();await restarted.PendingDisplay;Check(notices.Last().Milestones.Count==0,"restart cannot repeat durably claimed stage");}
                    Check(f.Service.GetSnapshot(current).State.Milestones.Count==2,"dismissal never removes retrievable home cards");
                    var oldCulture=System.Globalization.CultureInfo.CurrentCulture;
                    try{System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo(TextResources.CurrentLanguage=="zh_CN"?"en-US":"zh-CN");var data=RelationshipCardData.FromSnapshot(crossed.Snapshot,"familiar","Ebao");Check(data.DateText==DateTime.ParseExact(data.EarnedLocalDate,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture).ToString("D",System.Globalization.CultureInfo.GetCultureInfo(TextResources.CurrentLanguage.Replace('_','-'))),"card date follows selected UI locale when OS culture differs");}
                    finally{System.Globalization.CultureInfo.CurrentCulture=oldCulture;}
                    var saved=f.Service.GetSnapshot(current).State.Score;Check(saved==crossed.Snapshot.State.Score,"automatic feedback earns no interaction points");
                    count=notices.Count;current="official:marfu";await feedback.AcceptAsync("official:e-bao",crossed);clock.Advance();await feedback.PendingDisplay;Check(notices.Count==count,"old companion completion cannot publish for new identity");
                    current="official:e-bao";await feedback.AcceptAsync(current,crossed);f.Service.UpdateSettings(true,false,true);clock.Advance();await feedback.PendingDisplay;Check(notices.Count==count,"feedback disabled during debounce suppresses saved pending display");
                }
            }
            await FailedClaimAndSwitch();
            await SuppressionCycles();
            await SavedFeedbackSettings();
            await GroupedAndDisposed();
            using(var f=new RelationshipFixture()){
                Directory.CreateDirectory(f.Root);string target=Path.Combine(f.Root,"card.png");
                Check(await RelationshipCardFile.SaveAsync(null,new byte[]{1})==RelationshipCardSaveResult.Cancelled&&!File.Exists(target),"cancel export creates no file or success");
                File.WriteAllText(target,"original");
                Check(await RelationshipCardFile.SaveAsync(target,new byte[0])==RelationshipCardSaveResult.Failed&&File.ReadAllText(target)=="original","invalid export preserves existing destination");
                var validPng=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/p8AAAAASUVORK5CYII=");
                Check(await RelationshipCardFile.SaveAsync(f.Root,validPng)==RelationshipCardSaveResult.Failed&&Directory.GetFiles(f.Root,"*.tmp").Length==0,"denied destination has no false success or partial output");
                if(Environment.OSVersion.Platform==PlatformID.Win32NT)using(var held=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.None))Check(await RelationshipCardFile.SaveAsync(target,validPng)==RelationshipCardSaveResult.Failed,"locked actual destination reports disk error");
                Check(File.ReadAllText(target)=="original"&&Directory.GetFiles(f.Root,"*.tmp").Length==0,"failed atomic overwrite preserves destination and removes temporary output");
                Check(await RelationshipCardFile.SaveAsync(target,validPng)==RelationshipCardSaveResult.Saved&&File.ReadAllBytes(target).SequenceEqual(validPng),"approved existing destination atomically receives PNG bytes");
                Check(RelationshipCardData.SafeFileName("../A:*?", "familiar","2026-10-24").IndexOfAny(Path.GetInvalidFileNameChars())<0,"suggested filename is safe");
            }
        }
        static async Task SavedFeedbackSettings()
        {
            using(var f=new RelationshipFixture()){
                var publications=new List<Action>();var publicationGate=new object();
                using(var runtime=new RelationshipRuntime(f.Root,action=>{lock(publicationGate)publications.Add(action);})) {
                    var clock=new Clock();var notices=new List<RelationshipFeedbackNotice>();var model=new RelationshipHomeModel(runtime);int changes=0;Action changed=()=>changes++;runtime.FeedbackSettingsChanged+=changed;
                    var feedback=new RelationshipFeedback(runtime,_=>true,notices.Add,clock);var time=RelationshipFixture.Time("2026-10-24T12:00:00Z");string id="official:e-bao";
                    await feedback.AcceptAsync(id,f.Service.RecordChecked(id,RelationshipEventType.Visit,"settings-first",time));
                    Check((await model.UpdateSettingsAsync(true,false,true)).Success,"saved feedback disable succeeds");
                    Check(!runtime.FeedbackEnabled,"successful settings save publishes disabled state without UI disk reads");
                    Check((await model.UpdateSettingsAsync(true,true,true)).Success,"saved feedback enable succeeds");
                    List<Action> delayed;lock(publicationGate){delayed=publications.ToList();publications.Clear();}
                    delayed.Reverse();foreach(var publication in delayed)publication();clock.Advance();await feedback.PendingDisplay;
                    Check(runtime.FeedbackEnabled&&notices.Count==0,"out-of-order settings notifications cannot replay pre-disable pending feedback");
                    await feedback.AcceptAsync(id,f.Service.RecordChecked(id,RelationshipEventType.CompletedConversation,"settings-second",time.AddSeconds(1)));
                    string path=Path.Combine(f.Root,"relationships","state.json");byte[] original=File.ReadAllBytes(path);File.WriteAllText(path,"{broken");int before=changes;
                    Check(!(await model.UpdateSettingsAsync(true,false,true)).Success&&runtime.FeedbackEnabled,"failed settings save never pretends feedback was disabled");File.WriteAllBytes(path,original);
                    lock(publicationGate){delayed=publications.ToList();publications.Clear();}foreach(var publication in delayed)publication();clock.Advance();await feedback.PendingDisplay;
                    Check(changes==before&&notices.Count==1,"failed settings save emits no settings event and keeps allowed feedback");
                    feedback.Dispose();runtime.FeedbackSettingsChanged-=changed;
                    var subscriptions=(Delegate)typeof(RelationshipRuntime).GetField("FeedbackSettingsChanged",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(runtime);
                    Check(subscriptions==null,"disposed feedback detaches saved-settings subscription");
                }
            }
        }
        static async Task GroupedAndDisposed()
        {
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                var time=RelationshipFixture.Time("2026-10-01T12:00:00Z");RelationshipResult saved=null;string id="official:e-bao";
                for(int d=0;d<31;d++)for(int n=0;n<4;n++)saved=f.Service.RecordChecked(id,RelationshipEventType.CompletedConversation,"multi-"+d+"-"+n,time.AddSeconds(d*86400+n));
                var clock=new Clock();var notices=new List<RelationshipFeedbackNotice>();
                using(var feedback=new RelationshipFeedback(runtime,_=>true,notices.Add,clock)){
                    await feedback.AcceptAsync(id,saved);clock.Advance();await feedback.PendingDisplay;
                    Check(notices.Count==1&&notices[0].Milestones.Select(m=>m.StageId).SequenceEqual(new[]{"familiar","close"}),"one compact notice retains every newly claimed threshold");
                    Check(notices[0].Milestones.All(m=>m.EarnedUtc==saved.Snapshot.State.Milestones.Single(x=>x.StageId==m.StageId).EarnedUtc),"grouped stages retain actual saved timestamps");
                    await feedback.AcceptAsync(id,saved);feedback.Dispose();clock.Advance();await feedback.PendingDisplay;Check(notices.Count==1,"disposed feedback cancels timers and pending display");
                }
                using(var feedback=new RelationshipFeedback(runtime,_=>true,notices.Add,clock)){
                    string path=Path.Combine(f.Root,"relationships","state.json");byte[] bytes=File.ReadAllBytes(path);File.WriteAllText(path,"{broken");
                    var failed=f.Service.Record(id,RelationshipEventType.Visit,"failed",time.AddSeconds(32*86400));await feedback.AcceptAsync(id,failed);clock.Advance();await feedback.PendingDisplay;
                    Check(!failed.Success&&notices.Count==1,"failed growth save cannot display gained points");File.WriteAllBytes(path,bytes);
                }
            }
        }
        static async Task FailedClaimAndSwitch()
        {
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                string id="official:e-bao";var t=RelationshipFixture.Time("2026-10-01T12:00:00Z");RelationshipResult result=null;
                for(int d=0;d<10;d++)for(int n=0;n<4;n++)result=f.Service.RecordChecked(id,RelationshipEventType.CompletedConversation,"gain-"+d+"-"+n,t.AddSeconds(d*86400+n));
                var clock=new Clock();var notices=new List<RelationshipFeedbackNotice>();bool allowed=true;
                using(var feedback=new RelationshipFeedback(runtime,_=>allowed,notices.Add,clock)){
                    string file=Path.Combine(f.Root,"relationships","state.json");byte[] bytes=File.ReadAllBytes(file);File.WriteAllText(file,"{broken");
                    await feedback.AcceptAsync(id,result);clock.Advance();await feedback.PendingDisplay;Check(notices.Count==0,"claim save failure shows no success");
                    File.WriteAllBytes(file,bytes);
                    using(var lease=new FileStream(Path.Combine(f.Root,"relationships",".transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                        var pending=feedback.AcceptAsync(id,result);allowed=false;lease.Dispose();await pending;
                    }
                    allowed=true;clock.Advance();await feedback.PendingDisplay;Check(notices.Count==0&&f.Service.GetSnapshot(id).State.Milestones.All(m=>m.NotificationClaimed),"async claim rechecks suppression and consumes stages durably");
                    await feedback.AcceptAsync(id,result);allowed=false;clock.Advance();await feedback.PendingDisplay;allowed=true;clock.Advance();await feedback.PendingDisplay;Check(notices.Count==0,"suppression at scheduled display does not replay");
                }
            }
        }
        static void Invalidate(RelationshipRuntime runtime)
        {runtime.InvalidateFeedback();}
        static async Task SuppressionCycles()
        {
            foreach(string transition in new[]{"quiet", "visibility", "identity", "fullscreen", "session"})
            foreach(string phase in new[]{"claim", "timer", "read", "publication", "worker"}){
                using(var f=new RelationshipFixture()){
                    var publications=new List<Action>();var publicationGate=new object();
                    using(var runtime=new RelationshipRuntime(f.Root,a=>{lock(publicationGate)publications.Add(a);})){
                        var clock=new Clock();var notices=new List<RelationshipFeedbackNotice>();bool allowed=true;string current="official:e-bao";string id=current;
                        using(var feedback=new RelationshipFeedback(runtime,k=>allowed&&k==current,notices.Add,clock)){
                            var time=RelationshipFixture.Time("2026-10-01T12:00:00Z");
                            Action cycle=()=>{allowed=false;current="official:marfu";Invalidate(runtime);allowed=true;current=id;Invalidate(runtime);};
                            var saved=f.Service.RecordChecked(id,RelationshipEventType.Visit,"cycle-first",time);
                            if(phase=="claim"){
                                using(var held=new FileStream(Path.Combine(f.Root,"relationships",".transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                                    var accepting=feedback.AcceptAsync(id,saved);cycle();held.Dispose();await accepting;
                                }
                            }else if(phase=="publication"||phase=="worker"){
                                if(phase=="worker"){
                                    using(var held=new RelationshipStore(f.Root).Acquire()){
                                        var queued=runtime.Enqueue(id,RelationshipEventType.CompletedConversation,"cycle-queued",time.AddSeconds(1));cycle();held.Dispose();await queued;
                                    }
                                }else{await runtime.Enqueue(id,RelationshipEventType.CompletedConversation,"cycle-queued",time.AddSeconds(1));cycle();}
                                Action[] callbacks;lock(publicationGate){callbacks=publications.ToArray();publications.Clear();}foreach(var callback in callbacks)callback();
                                await feedback.PendingAcceptance;
                            }else{
                                await feedback.AcceptAsync(id,saved);
                                if(phase=="read"){
                                    using(var held=new FileStream(Path.Combine(f.Root,"relationships",".transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                                        clock.Advance();cycle();held.Dispose();await feedback.PendingDisplay;
                                    }
                                }else cycle();
                            }
                            clock.Advance();await feedback.PendingDisplay;
                            Check(notices.Count==0,transition+" complete suppression cycle discards old "+phase+" feedback");
                            Check(f.Service.GetSnapshot(id).State.Milestones.All(m=>m.NotificationClaimed),transition+" "+phase+" suppression retains durable milestone consumption");
                            await feedback.AcceptAsync(id,f.Service.RecordChecked(id,RelationshipEventType.CompletedConversation,"cycle-fresh",time.AddSeconds(2)));clock.Advance();await feedback.PendingDisplay;
                            Check(notices.Count==1&&notices[0].AwardedPoints==2,transition+" "+phase+" permits a fresh post-cycle event");
                        }
                    }
                }
            }
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                var clock=new Clock();var notices=new List<RelationshipFeedbackNotice>();string id="official:e-bao";var time=RelationshipFixture.Time("2026-10-01T12:00:00Z");
                using(var feedback=new RelationshipFeedback(runtime,_=>true,notices.Add,clock)){
                    await feedback.AcceptAsync(id,f.Service.RecordChecked(id,RelationshipEventType.Visit,"posted-old",time));var old=clock.PostedCallbacks();Invalidate(runtime);
                    await feedback.AcceptAsync(id,f.Service.RecordChecked(id,RelationshipEventType.CompletedConversation,"posted-new",time.AddSeconds(1)));
                    foreach(var callback in old)callback();await feedback.PendingDisplay;Check(notices.Count==0,"already-posted stale timer cannot take the new generation pending notice");
                    clock.Advance();await feedback.PendingDisplay;Check(notices.Count==1&&notices[0].AwardedPoints==2,"fresh timer retains its own notice after stale callback");
                }
            }
        }
    }
}
