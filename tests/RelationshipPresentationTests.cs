using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class RelationshipPresentationTests
    {
        static void Check(bool value,string label){RelationshipFixture.Check(value,label);Console.WriteLine("PASS "+label);}
        public static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run()
        {
            var restart=typeof(ApplicationRestart).GetMethod("StartInfo",new[]{typeof(string),typeof(string),typeof(bool)});
            Check(restart!=null,"safe restart supports an explicit onboarding resume reason");
            var recovery=(System.Diagnostics.ProcessStartInfo)restart.Invoke(null,new object[]{@"C:\Program Files\dotnet\dotnet.exe",@"C:\Desk Pet\DeskPet.dll",false});
            Check(recovery.Arguments=="\"C:\\Desk Pet\\DeskPet.dll\"","relationship recovery quotes the DLL and does not resume onboarding");
            Check(ApplicationRestart.StartInfo(@"C:\App\DeskPet.exe",@"C:\App\DeskPet.dll").Arguments==ApplicationRestart.ResumeArgument,"existing default restart still resumes onboarding");
            ApplicationRestart.Request(false);Check(ApplicationRestart.Requested&&ApplicationRestart.StartInfo(@"C:\App\DeskPet.exe",@"C:\App\DeskPet.dll").Arguments=="","actual recovery restart request uses normal launch without guide flag");
            ApplicationRestart.Requested=false;ApplicationRestart.Requested=true;Check(ApplicationRestart.StartInfo(@"C:\App\DeskPet.exe",@"C:\App\DeskPet.dll").Arguments==ApplicationRestart.ResumeArgument,"a later existing onboarding request resets recovery restart reason");ApplicationRestart.Requested=false;
            using(var capFixture=new RelationshipFixture())using(var capRuntime=new RelationshipRuntime(capFixture.Root)){
                var start=RelationshipFixture.Time("2026-10-24T12:00:00Z");int eventIndex=0;
                var types=new[]{RelationshipEventType.Visit,RelationshipEventType.SuccessfulWorkTask,RelationshipEventType.SuccessfulWorkTask,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction};
                foreach(var type in types){eventIndex++;capFixture.Service.RecordChecked("official:e-bao",type,"cap-"+eventIndex,new RelationshipTime(start.UtcNow.AddSeconds(eventIndex*25),start.LocalOffset));}
                var capModel=new RelationshipHomeModel(capRuntime);await capModel.UpdateSettingsAsync(false,true,true);var cap=await capModel.LoadAsync("official:e-bao",RelationshipFixture.Time("2026-10-24T12:10:00Z"));
                Check(cap.TodayPoints==20&&cap.DailyLimitReached&&cap.GrowthPaused,"saved daily cap and pause can apply together");
                Check(cap.StatusTexts.Contains(TextResources.Get("relationship.home.dailyLimit"))&&cap.StatusTexts.Contains(TextResources.Get("relationship.home.growthPaused")),"shared status explains both cap and pause without a daily task list");
            }
            using(var blockedFixture=new RelationshipFixture()){
                Directory.CreateDirectory(Path.Combine(blockedFixture.Root,"relationships"));
                string pending=Path.Combine(blockedFixture.Root,"relationships","pending.json");File.WriteAllText(pending,"{broken");
                using(var blockedRuntime=new RelationshipRuntime(blockedFixture.Root)){
                    var blockedModel=new RelationshipHomeModel(blockedRuntime);
                    Check(!(await blockedModel.RetryAsync()).Success,"corrupt outbox retry cannot report recovery success");
                    Check(File.ReadAllText(pending)=="{broken","blocked outbox original is preserved");
                }
            }
            await DelayedRetryOrdersNewerEdit();
            var p=RelationshipProgress.FromScore(156);
            Check(p.StageId=="familiar"&&p.NextStageId=="close"&&p.NextThreshold==240&&p.RemainingPoints==84,"156 uses authoritative familiar/close/240/84 progress");
            p=RelationshipProgress.FromScore(1000);Check(p.NextStageId==null&&p.NextThreshold==null&&p.RemainingPoints==0,"1000 has no impossible next stage");
            Check(p.Nodes.Length==6&&p.Nodes[5].Current&&p.Fraction==1,"maximum stage sequence and circle fraction share authoritative rules");
            var familiar=RelationshipProgress.FromScore(156);Check(familiar.Nodes[0].Completed&&familiar.Nodes[1].Current&&!familiar.Nodes[2].Completed&&Math.Abs(familiar.Fraction-.65)<.00001,"stage order is separate from cumulative 156 of 240 fraction");
            using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root)){
                var model=new RelationshipHomeModel(runtime);string id="official:e-bao";
                var empty=await model.LoadAsync(id,RelationshipFixture.Time("2026-10-24T12:00:00Z"));
                Check(empty.Snapshot!=null&&empty.Snapshot.State==null&&!empty.ReadFailed,"successful empty snapshot is distinct from failure");
                f.Service.RecordChecked(id,RelationshipEventType.Visit,"first",RelationshipFixture.Time("2026-10-20T12:00:00Z"));
                f.Service.RecordChecked(id,RelationshipEventType.Visit,"second",RelationshipFixture.Time("2026-10-24T12:00:00Z"));
                var saved=await model.LoadAsync(id,RelationshipFixture.Time("2026-10-24T12:00:00Z"));
                Check(saved.Progress.Score==4&&saved.ActiveDays==2&&saved.AcquaintanceDays==5&&saved.TodayPoints==2,"saved score, actual active days and acquaintance span are separate");
                var tomorrow=await model.LoadAsync(id,RelationshipFixture.Time("2026-10-25T12:00:00Z"));Check(tomorrow.TodayPoints==0,"today resets for elapsed natural midnight without manufacturing a visit");
                Check((await model.UpdateSettingsAsync(false,false,false)).Success,"global settings save succeeds");
                Check(!f.Service.GetSnapshot(id).Settings.GrowthEnabled&&f.Service.GetSnapshot(id).State.Score==4,"disabling persists and preserves relationship");
                var paused=await model.LoadAsync(id,RelationshipFixture.Time("2026-10-24T12:00:00Z"));
                var statuses=typeof(RelationshipHomeView).GetProperty("StatusTexts");
                Check(statuses!=null,"shared home projection owns the visible status explanations");
                Check(((System.Collections.Generic.IEnumerable<string>)statuses.GetValue(paused,null)).Contains(TextResources.Get("relationship.home.growthPaused")),"saved global growth setting produces pause explanation");
                string file=Path.Combine(f.Root,"relationships","state.json");
                byte[] savedBytes=File.ReadAllBytes(file);
                File.WriteAllText(file,"{broken");byte[] bytes=File.ReadAllBytes(file);
                var failed=await model.LoadAsync(id,RelationshipTime.Current());
                Check(failed.ReadFailed&&failed.Snapshot.State.Score==4,"read failure retains last saved snapshot");
                var other=await model.LoadAsync("official:marfu",RelationshipTime.Current());
                Check(other.ReadFailed&&other.Snapshot==null,"unknown identity never borrows another companion snapshot on failure");
                Check(!(await model.UpdateSettingsAsync(true,true,true)).Success,"failed settings save is recoverable");
                Check(Convert.ToBase64String(bytes)==Convert.ToBase64String(File.ReadAllBytes(file)),"read/settings failure preserves corrupt original bytes");
                File.WriteAllBytes(file,savedBytes);await model.RetryAsync();
                Check(f.Service.GetSnapshot(id).Settings.GrowthEnabled,"retry commits the exact failed settings intent after storage recovers");
            }
        }
        static async Task DelayedRetryOrdersNewerEdit()
        {
            using(var f=new RelationshipFixture()){
                var entered=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();int hold=0;
                using(var runtime=new RelationshipRuntime(f.Root,delegate(Action action){
                    if(System.Threading.Interlocked.CompareExchange(ref hold,0,1)==1){entered.TrySetResult(true);release.Task.GetAwaiter().GetResult();}action();
                })){
                    var model=new RelationshipHomeModel(runtime);
                    Check((await model.UpdateSettingsAsync(true,true,true)).Success,"retry ordering fixture has saved settings");
                    string path=Path.Combine(f.Root,"relationships","state.json");byte[] original=File.ReadAllBytes(path);
                    File.WriteAllText(path,"{broken");Check(!(await model.UpdateSettingsAsync(false,false,false)).Success,"retry ordering fixture retains a failed older intent");File.WriteAllBytes(path,original);
                    System.Threading.Interlocked.Exchange(ref hold,1);var retry=model.RetryAsync();
                    Task<RelationshipResult> newer=null;bool overtook;
                    try{
                        Check(await Task.WhenAny(entered.Task,Task.Delay(5000))==entered.Task,"runtime notification gate holds the admitted retry");
                        newer=model.UpdateSettingsAsync(true,false,true);
                        // The gate establishes the exact pending operation; this deadline is a
                        // bounded negative assertion, not a sleep used to order worker writes.
                        await Task.WhenAny(newer,Task.Delay(2000));overtook=newer.IsCompleted;
                    }finally{release.TrySetResult(true);}
                    await retry;if(newer!=null)await newer;
                    Check(!overtook,"a newer edit cannot commit ahead of an admitted delayed settings retry");
                    var settings=f.Service.GetSnapshot("official:e-bao").Settings;
                    Check(settings.GrowthEnabled&&!settings.FeedbackEnabled&&settings.AiContextEnabled,"newer settings remain authoritative after delayed retry settles");
                }
            }
        }
    }
}
