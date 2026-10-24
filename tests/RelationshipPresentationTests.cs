using System;
using System.IO;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class RelationshipPresentationTests
    {
        static void Check(bool value,string label){RelationshipFixture.Check(value,label);Console.WriteLine("PASS "+label);}
        public static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run()
        {
            await DelayedRetryOrdersNewerEdit();
            var p=RelationshipProgress.FromScore(156);
            Check(p.StageId=="familiar"&&p.NextStageId=="close"&&p.NextThreshold==240&&p.RemainingPoints==84,"156 uses authoritative familiar/close/240/84 progress");
            p=RelationshipProgress.FromScore(1000);Check(p.NextStageId==null&&p.NextThreshold==null&&p.RemainingPoints==0,"1000 has no impossible next stage");
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
