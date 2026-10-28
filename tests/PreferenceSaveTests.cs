using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class PreferenceSaveTests
    {
        static int checks;
        const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static object Call(object target,string method,params object[] args){try{return target.GetType().GetMethod(method,Members).Invoke(target,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        static bool FlushPet(PetWindow pet){var task=(Task<bool>)Call(pet,"FlushPreferenceSavesAsync");Wait(task);return task.Result;}
        static bool Flush(object target){var task=(Task<bool>)Call(target,"FlushAsync");Wait(task);return task.Result;}
        static bool Pending(object scheduler){return (bool)scheduler.GetType().GetProperty("HasPendingChanges",Members).GetValue(scheduler,null);}
        static int Writes(SettingsStore store){return (int)typeof(SettingsStore).GetProperty("CompletedSnapshotWrites",Members).GetValue(store,null);}
        static void Pump(int milliseconds=60){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Until(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>deadline)throw new TimeoutException();Pump(10);}}
        static void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException();Pump(10);}task.GetAwaiter().GetResult();Pump();}
        [STAThread]public static int Main()
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;SettingsPage page=null;Window host=null;
            string root=Path.GetFullPath(Path.Combine("work","preference-save-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);string path=Path.Combine(root,"settings.json");
            try
            {
                var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.PreferenceSaveScheduler");Check(type!=null,"ordinary preference writes have one coalescing scheduler");
                int calls=0,current=0,saved=0;bool succeed=true;
                Func<Task<bool>> write=delegate{calls++;if(succeed)saved=current;return Task.FromResult(succeed);};
                var scheduler=Activator.CreateInstance(type,Members,null,new object[]{write,TimeSpan.FromMilliseconds(20)},null);
                for(int i=0;i<20;i++){current=i;Call(scheduler,"Request");}
                Check(calls==0&&Pending(scheduler),"requests do not synchronously write");Pump();
                Check(calls==1&&saved==19&&!Pending(scheduler),"trailing debounce commits current state once");
                Call(scheduler,"Request");Call(scheduler,"MarkSaved");Pump();Check(calls==1&&!Pending(scheduler),"explicit successful save supersedes queued ordinary write");
                succeed=false;Call(scheduler,"Request");Check(!Flush(scheduler)&&Pending(scheduler),"failed flush retains dirty state");int failedCalls=calls;Pump();Check(calls==failedCalls,"failed flush does not retry indefinitely");
                succeed=true;Check(Flush(scheduler)&&!Pending(scheduler),"explicit retry saves pending changes");((IDisposable)scheduler).Dispose();
                var release=new TaskCompletionSource<bool>();
                var asyncScheduler=Activator.CreateInstance(type,Members,null,new object[]{new Func<Task<bool>>(()=>release.Task),TimeSpan.FromMilliseconds(20)},null);
                Call(asyncScheduler,"Request");Pump();Check(Pending(asyncScheduler),"dispatcher responds while asynchronous save is held");
                release.SetResult(true);Wait((Task<bool>)Call(asyncScheduler,"FlushAsync"));Check(!Pending(asyncScheduler),"async save drains before flush completes");((IDisposable)asyncScheduler).Dispose();
                var cfg=new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,GreetingEnabled=false,Wander=false,FollowCursor=false,SaveChatHistory=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                var store=new SettingsStore(path);store.Save(cfg);pet=new PetWindow(null,cfg,store,null);pet.Show();
                var firstMaintenance=pet.DataMaintenance.BeginAsync();Wait(firstMaintenance);bool firstDragSafe=true;
                using(firstMaintenance.Result){try{Call(pet,"BeginPreferenceEdit");Call(pet,"EndPreferenceEdit");}catch(InvalidOperationException){firstDragSafe=false;}}
                Check(firstDragSafe,"first preference drag during maintenance does not register a producer or throw");
                page=new SettingsPage(pet);host=new Window{Width=1030,Height=760,Content=page};host.Show();page.SelectCompanionSection(1);Pump();
                var size=(Slider)page.FindControl("SizeSlider");int before=Writes(store);
                size.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
                for(int i=0;i<20;i++)size.Value=.8+i*.015;
                Pump(400);Check(Writes(store)==before,"scale pointer drag previews without disk commits");
                size.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Pump(400);FlushPet(pet);
                Console.WriteLine("DIAGNOSTIC scale commits="+(Writes(store)-before)+" live="+cfg.Scale+" disk="+store.Load().Scale+" notice="+pet.Notice);
                Check(Writes(store)==before+1&&Math.Abs(store.Load().Scale-cfg.Scale)<.001,"scale drag end commits latest value exactly once");
                page.SelectAiSection(2);Pump();var volume=(Slider)page.FindControl("VoiceVolume");before=Writes(store);
                volume.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});volume.Value=62;Pump(400);
                Check(Writes(store)==before,"volume pointer drag stays in preview while held");
                volume.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Pump(400);FlushPet(pet);
                Check(Writes(store)==before+1&&store.Load().Volume==62,"volume pointer release commits once");
                before=Writes(store);volume.Value=63;
                volume.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});volume.Value=64;Pump(400);
                Check(Writes(store)==before,"new pointer drag suspends an already pending debounce");
                volume.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Pump(400);FlushPet(pet);
                Check(Writes(store)==before+1&&store.Load().Volume==64,"resumed drag commits newest value once");
                cfg.Volume=71;Call(pet,"RequestPreferenceSave");cfg.Text.Model="explicit-latest-model";pet.SaveSettings();before=Writes(store);Pump(400);
                Check(Writes(store)==before&&store.Load().Text.Model=="explicit-latest-model"&&store.Load().Volume==71,"ordinary pending state cannot overwrite explicit model save");
                cfg.Volume=74;Call(pet,"RequestPreferenceSave");UiLanguageChange.Apply(store,cfg,"en_US");Pump(400);FlushPet(pet);
                Check(store.Load().UiLanguage=="en_US"&&store.Load().Volume==74,"pending preference commit preserves a newer language selection");
                before=Writes(store);var maintenance=pet.DataMaintenance.BeginAsync();Wait(maintenance);
                using(maintenance.Result){cfg.Volume=69;Call(pet,"RequestPreferenceSave");Pump(400);Check(Writes(store)==before,"maintenance blocks ordinary timer writes");}
                Wait(pet.FlushDeferredSettingsAsync());Check(store.Load().Volume==69,"maintenance drain saves the latest deferred preference");
                cfg.Scale=1.2;Call(pet,"RequestPreferenceSave");Wait(store.RestoreDefaultPreferencesAsync(cfg,pet.DataMaintenance));pet.ApplyRestoredPreferences();Pump(400);
                Check(Math.Abs(cfg.Scale-new PetSettings().Scale)<.001&&Math.Abs(store.Load().Scale-cfg.Scale)<.001,"restore defaults supersedes older pending preferences");
                var oldPatch=store.CapturePreferences(cfg,"Volume");cfg.Text.Model="newer-provider";cfg.Volume=81;new SettingsStore(path).Save(cfg);
                Wait(store.SavePreferencePatchAsync(oldPatch));Check(store.Load().Volume==81&&store.Load().Text.Model=="newer-provider","older accepted patch cannot overwrite newer full save across stores");
                cfg.Volume=82;var languagePatch=store.CapturePreferences(cfg,"Volume");new SettingsStore(path).SaveUiLanguage(cfg,"zh_CN");Wait(store.SavePreferencePatchAsync(languagePatch));
                Check(store.Load().UiLanguage=="zh_CN"&&store.Load().Volume==82,"cross-store language save preserves pending preference and language");
                cfg.Volume=83;var heldPatch=store.CapturePreferences(cfg,"Volume");var entered=new System.Threading.ManualResetEventSlim();var releaseIO=new System.Threading.ManualResetEventSlim();
                store.BeforePreferenceCommit=()=>{entered.Set();if(!releaseIO.Wait(5000))throw new IOException("fixture release timeout");};
                var background=store.SavePreferencePatchAsync(heldPatch);while(!entered.IsSet)Pump(10);bool responsive=false;Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>responsive=true));Pump();
                Check(responsive&&!background.IsCompleted,"real durable preference I/O leaves dispatcher responsive");releaseIO.Set();Wait(background);store.BeforePreferenceCommit=null;
                string valid=File.ReadAllText(path);File.WriteAllText(path,"{broken");bool rejected=false;try{Wait(store.SavePreferencePatchAsync(store.CapturePreferences(cfg,"Volume")));}catch(Exception){rejected=true;}
                Check(rejected&&File.ReadAllText(path)=="{broken","invalid existing configuration is never replaced by defaults");File.WriteAllText(path,valid);
                cfg.Scale=1.4;var staleScale=store.CapturePreferences(cfg,"Scale");cfg.Scale=1.1;store.Save(cfg);cfg.Volume=84;
                var merged=store.MergePreferences(staleScale,store.CapturePreferences(cfg,"Volume"));Wait(store.SavePreferencePatchAsync(merged));
                Check(Math.Abs(store.Load().Scale-1.1)<.001&&store.Load().Volume==84,"merging a newer field never resurrects a superseded older field");
                entered.Reset();releaseIO.Reset();store.BeforePreferenceCommit=()=>{entered.Set();if(!releaseIO.Wait(5000))throw new IOException("fixture release timeout");};
                cfg.Volume=85;Call(pet,"RequestVolumeSave");var inFlight=(Task<bool>)Call(pet,"FlushPreferenceSavesAsync");Until(()=>entered.IsSet);
                var maintenanceDrain=pet.DataMaintenance.BeginAsync();Pump();Check(!maintenanceDrain.IsCompleted,"maintenance waits for the in-flight durable preference write");
                cfg.Volume=86;Call(pet,"RequestVolumeSave");releaseIO.Set();Wait(inFlight);Wait(maintenanceDrain);store.BeforePreferenceCommit=null;
                using(maintenanceDrain.Result){Check(store.Load().Volume==85,"maintenance lease begins after old preference reaches disk");}
                Wait(pet.FlushDeferredSettingsAsync());Check(store.Load().Volume==86,"edit deferred by maintenance survives resume");
                entered.Reset();releaseIO.Reset();store.BeforePreferenceCommit=()=>{entered.Set();if(!releaseIO.Wait(5000))throw new IOException("fixture release timeout");};
                cfg.Volume=87;Call(pet,"RequestVolumeSave");var noticeFlush=(Task<bool>)Call(pet,"FlushPreferenceSavesAsync");Until(()=>entered.IsSet);
                Directory.CreateDirectory(path+".tmp");using(Dispatcher.CurrentDispatcher.DisableProcessing()){releaseIO.Set();cfg.Text.Model="unsaved-after-failure";pet.SaveSettings();}Wait(noticeFlush);
                Check(pet.Notice==TextResources.Get("petWindow.saveSettings.notice")&&store.Load().Text.Model!="unsaved-after-failure","older background success does not erase a newer explicit save failure notice");
                store.BeforePreferenceCommit=null;Directory.Delete(path+".tmp");pet.SaveSettings();
                cfg.OnboardingState="completed";cfg.OnboardingLessonId="old";cfg.OnboardingStep=4;store.Save(cfg);entered.Reset();releaseIO.Reset();
                store.BeforeExactSave=()=>{entered.Set();if(!releaseIO.Wait(5000))throw new IOException("fixture release timeout");};
                var guideSnapshot=store.SaveExactSnapshotAsync(cfg);Until(()=>entered.IsSet);store.SaveLanguageSelection(cfg,"en_US",true);releaseIO.Set();Wait(guideSnapshot);store.BeforeExactSave=null;
                var guideSaved=store.Load();Check(guideSaved.UiLanguage=="en_US"&&guideSaved.OnboardingState=="deferred"&&guideSaved.OnboardingLessonId=="welcome"&&guideSaved.OnboardingStep==0,"newer language guide transaction survives an older asynchronous full candidate");
                store.BeforePreferenceCommit=()=>{throw new IOException("fixture failure");};volume.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});volume.Value=63;
                Check(!FlushPet(pet),"disk failure is reported by preference flush");pet.Close();Pump();Check(pet.IsVisible,"failed preference flush cancels window close");
                volume.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});store.BeforePreferenceCommit=null;Check(FlushPet(pet)&&store.Load().Volume==63,"retry after disk recovery saves latest value");
                Directory.CreateDirectory(path+".tmp");pet.Close();Until(()=>!(bool)typeof(PetWindow).GetField("closing",Members).GetValue(pet));
                Check(pet.IsVisible&&pet.IsEnabled&&host.IsEnabled,"final configuration save failure restores usable windows before teardown");Directory.Delete(path+".tmp");
                pet.Close();Until(()=>!pet.IsVisible);Check(store.Load().Volume==63,"successful asynchronous exit preserves final configuration");
                Console.WriteLine("Preference saves: "+checks+" passed; evidence="+root);return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
            finally{if(Directory.Exists(path+".tmp"))Directory.Delete(path+".tmp");if(host!=null)host.Close();if(page!=null)page.Dispose();if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
    }
}
