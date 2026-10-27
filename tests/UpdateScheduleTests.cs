using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class UpdateScheduleTests
    {
        static int checks;
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                var cfg=new PetSettings{AutoUpdate=true,GreetingEnabled=false,SaveChatHistory=false,ReadReplies=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-schedule-"+Guid.NewGuid().ToString("N"),"settings.json")),null);
                Directory.CreateDirectory(pet.Store.DirectoryPath);
                using(var feed=new Feed{Fail=true})using(var updates=new UpdateCoordinator(pet,feed,job=>{throw new Exception("No installation expected");},ReleaseInfo.DisplayVersion))
                {
                    cfg.LastUpdateCheckUtcTicks=0;var failed=updates.CheckAndUpdateAsync(true);Wait(()=>failed.IsCompleted);
                    Check(cfg.LastUpdateCheckUtcTicks==0 && pet.Store.Load().LastUpdateCheckUtcTicks==0,"failed checks do not persist a successful-check timestamp");
                    updates.StartAutomaticChecks();Timer(updates).Interval=TimeSpan.FromMilliseconds(20);Pump(.12);
                    Check(feed.Checks==2 && Timer(updates).IsEnabled && Timer(updates).Interval<=TimeSpan.FromMinutes(10),"automatic failures schedule a bounded retry instead of waiting a day: checks="+feed.Checks+", enabled="+Timer(updates).IsEnabled+", interval="+Timer(updates).Interval+", status="+updates.Status);
                    feed.Fail=false;Timer(updates).Interval=TimeSpan.FromMilliseconds(20);Pump(.12);
                    Check(feed.Checks==3 && cfg.LastUpdateCheckUtcTicks>0 && updates.Status.Contains("最新"),"automatic retry succeeds and records the successful check");
                    Check(Timer(updates).Interval==TimeSpan.FromHours(4),"successful automatic checks repeat every four hours");
                    feed.Fail=true;var recentFailure=updates.CheckAndUpdateAsync(true);Wait(()=>recentFailure.IsCompleted);feed.Fail=false;
                    Timer(updates).Interval=TimeSpan.FromMilliseconds(20);Pump(.12);
                    Check(feed.Checks==5,"retry after a failed manual check bypasses an older successful-check cooldown");
                }
                using(var feed=new Feed())using(var updates=new UpdateCoordinator(pet,feed,null,ReleaseInfo.DisplayVersion))
                {
                    cfg.LastUpdateCheckUtcTicks=DateTime.UtcNow.AddHours(-4).AddSeconds(50).Ticks;updates.StartAutomaticChecks();Timer(updates).Interval=TimeSpan.FromMilliseconds(20);Pump(.12);
                    Check(feed.Checks==0 && Timer(updates).Interval<TimeSpan.FromSeconds(51),"recent successful check schedules its remaining cooldown instead of another four hours");
                    Check(updates.Status.Contains("上次") && updates.Status.Contains("自动"),"skipped startup check explains the prior success and future automatic check");
                    cfg.AutoUpdate=false;updates.AutomaticChanged();Check(!Timer(updates).IsEnabled,"disabling automatic updates stops the automatic timer");
                    cfg.LastUpdateCheckUtcTicks=0;cfg.AutoUpdate=true;updates.AutomaticChanged();Check(Timer(updates).IsEnabled && Timer(updates).Interval<=TimeSpan.FromSeconds(1),"enabling automatic updates rearms a prompt check");
                    Timer(updates).Interval=TimeSpan.FromMilliseconds(20);Pump(.12);Check(feed.Checks==1,"enabling after a previously skipped check actually queries the feed");
                }
                using(var feed=new Feed{Block=true})using(var updates=new UpdateCoordinator(pet,feed,null,ReleaseInfo.DisplayVersion))
                {
                    cfg.LastUpdateCheckUtcTicks=0;updates.StartAutomaticChecks();var task=updates.CheckAndUpdateAsync(true);Timer(updates).Interval=TimeSpan.FromMilliseconds(20);Pump(.12);
                    Check(feed.Checks==1 && Timer(updates).Interval<=TimeSpan.FromMinutes(1),"busy startup does not lose its scheduled check or overlap requests");updates.Cancel();Wait(()=>task.IsCompleted);
                }
                using(var feed=new DownloadFeed())using(var updates=new UpdateCoordinator(pet,feed,null,ReleaseInfo.DisplayVersion)){
                    var task=updates.CheckAndUpdateAsync(true);Wait(()=>feed.Directory!=null);string resource=feed.Directory;
                    Check(Maintenance.CacheResourceLease.Registered(pet.Store.DirectoryPath,resource),"actual update download registers cache ownership before transport entry");
                    bool protectedResource=false;try{using(Maintenance.CacheResourceLease.TryExclusive(pet.Store.DirectoryPath,resource)){}}catch(IOException){protectedResource=true;}
                    Check(protectedResource,"actual update download holds a cross-process cache lease");updates.Cancel();Wait(()=>task.IsCompleted);
                    using(Maintenance.CacheResourceLease.TryExclusive(pet.Store.DirectoryPath,resource))Check(true,"cancelled transport releases cache lease after completion");
                }
                Console.WriteLine("Update schedule tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(pet!=null)pet.Close();app.Shutdown();}
        }
        static DispatcherTimer Timer(UpdateCoordinator updates){return (DispatcherTimer)typeof(UpdateCoordinator).GetField("startup",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(updates);}
        static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        static void Wait(Func<bool> done){var limit=DateTime.UtcNow.AddSeconds(3);while(!done() && DateTime.UtcNow<limit)Pump(.02);if(!done())throw new Exception("Check timed out");}
        static void Pump(double seconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        sealed class Feed:IUpdateFeed
        {
            public bool Fail,Block;public int Checks;
            public async Task<UpdateRelease> CheckAsync(string repo,bool proxy,string address,bool portable,CancellationToken token){Checks++;if(Block)await Task.Delay(Timeout.Infinite,token);if(Fail)throw new ApiException("模拟启动网络尚未就绪");return new UpdateRelease{VersionTag="v"+ReleaseInfo.DisplayVersion};}
            public Task<UpdateRelease> CheckAsync(string repository,bool useProxy,string address,bool portable,UpdateChannel channel,CancellationToken token){return CheckAsync(repository,useProxy,address,portable,token);}
            public Task<string> DownloadAsync(UpdateRelease release,string folder,bool proxy,string address,IProgress<double> progress,CancellationToken token){throw new Exception("No download expected");}
            public void Dispose(){}
        }
        sealed class DownloadFeed:IUpdateFeed
        {
            internal string Directory;
            public Task<UpdateRelease> CheckAsync(string repo,bool proxy,string address,bool portable,CancellationToken token){return Task.FromResult(new UpdateRelease{VersionTag="v99.0.0",AssetName="DeskPet-Windows-99.0.0.zip",Digest="sha256:"+new string('a',64)});}
            public Task<UpdateRelease> CheckAsync(string repo,bool proxy,string address,bool portable,UpdateChannel channel,CancellationToken token){return CheckAsync(repo,proxy,address,portable,token);}
            public async Task<string> DownloadAsync(UpdateRelease release,string folder,bool proxy,string address,IProgress<double> progress,CancellationToken token){Directory=folder;await Task.Delay(Timeout.Infinite,token);return null;}
            public void Dispose(){}
        }
    }
}
