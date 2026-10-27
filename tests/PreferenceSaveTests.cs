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
        static bool Pending(object scheduler){return (bool)scheduler.GetType().GetProperty("HasPendingChanges",Members).GetValue(scheduler,null);}
        static int Writes(SettingsStore store){return (int)typeof(SettingsStore).GetProperty("CompletedSnapshotWrites",Members).GetValue(store,null);}
        static void Pump(int milliseconds=60){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException();Pump(10);}task.GetAwaiter().GetResult();Pump();}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;SettingsPage page=null;Window host=null;
            string root=Path.GetFullPath(Path.Combine("work","preference-save-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);string path=Path.Combine(root,"settings.json");
            try
            {
                var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.PreferenceSaveScheduler");Check(type!=null,"ordinary preference writes have one coalescing scheduler");
                int calls=0,current=0,saved=0;bool succeed=true;
                Func<bool> write=delegate{calls++;if(succeed)saved=current;return succeed;};
                var scheduler=Activator.CreateInstance(type,Members,null,new object[]{write,TimeSpan.FromMilliseconds(20)},null);
                for(int i=0;i<20;i++){current=i;Call(scheduler,"Request");}
                Check(calls==0&&Pending(scheduler),"requests do not synchronously write");Pump();
                Check(calls==1&&saved==19&&!Pending(scheduler),"trailing debounce commits current state once");
                Call(scheduler,"Request");Call(scheduler,"MarkSaved");Pump();Check(calls==1&&!Pending(scheduler),"explicit successful save supersedes queued ordinary write");
                succeed=false;Call(scheduler,"Request");Check(!(bool)Call(scheduler,"Flush")&&Pending(scheduler),"failed flush retains dirty state");int failedCalls=calls;Pump();Check(calls==failedCalls,"failed flush does not retry indefinitely");
                succeed=true;Check((bool)Call(scheduler,"Flush")&&!Pending(scheduler),"explicit retry saves pending changes");((IDisposable)scheduler).Dispose();
                var cfg=new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,GreetingEnabled=false,Wander=false,FollowCursor=false,SaveChatHistory=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                var store=new SettingsStore(path);store.Save(cfg);pet=new PetWindow(null,cfg,store,null);pet.Show();page=new SettingsPage(pet);host=new Window{Width=1030,Height=760,Content=page};host.Show();page.SelectCompanionSection(1);Pump();
                var size=(Slider)page.FindControl("SizeSlider");int before=Writes(store);
                size.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
                for(int i=0;i<20;i++)size.Value=.8+i*.015;
                Pump(400);Check(Writes(store)==before,"scale pointer drag previews without disk commits");
                size.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Pump(400);
                Console.WriteLine("DIAGNOSTIC scale commits="+(Writes(store)-before)+" live="+cfg.Scale+" disk="+store.Load().Scale+" notice="+pet.Notice);
                Check(Writes(store)==before+1&&Math.Abs(store.Load().Scale-cfg.Scale)<.001,"scale drag end commits latest value exactly once");
                page.SelectAiSection(2);Pump();var volume=(Slider)page.FindControl("VoiceVolume");before=Writes(store);
                volume.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});volume.Value=62;Pump(400);
                Check(Writes(store)==before,"volume pointer drag stays in preview while held");
                volume.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Pump(400);
                Check(Writes(store)==before+1&&store.Load().Volume==62,"volume pointer release commits once");
                before=Writes(store);volume.Value=63;
                volume.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});volume.Value=64;Pump(400);
                Check(Writes(store)==before,"new pointer drag suspends an already pending debounce");
                volume.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Pump(400);
                Check(Writes(store)==before+1&&store.Load().Volume==64,"resumed drag commits newest value once");
                cfg.Volume=71;Call(pet,"RequestPreferenceSave");cfg.Text.Model="explicit-latest-model";pet.SaveSettings();before=Writes(store);Pump(400);
                Check(Writes(store)==before&&store.Load().Text.Model=="explicit-latest-model"&&store.Load().Volume==71,"ordinary pending state cannot overwrite explicit model save");
                cfg.Volume=74;Call(pet,"RequestPreferenceSave");UiLanguageChange.Apply(store,cfg,"en_US");Pump(400);
                Check(store.Load().UiLanguage=="en_US"&&store.Load().Volume==74,"pending preference commit preserves a newer language selection");
                before=Writes(store);var maintenance=pet.DataMaintenance.BeginAsync();Wait(maintenance);
                using(maintenance.Result){cfg.Volume=69;Call(pet,"RequestPreferenceSave");Pump(400);Check(Writes(store)==before,"maintenance blocks ordinary timer writes");}
                Wait(pet.FlushDeferredSettingsAsync());Check(store.Load().Volume==69,"maintenance drain saves the latest deferred preference");
                cfg.Scale=1.2;Call(pet,"RequestPreferenceSave");Wait(store.RestoreDefaultPreferencesAsync(cfg,pet.DataMaintenance));pet.ApplyRestoredPreferences();Pump(400);
                Check(Math.Abs(cfg.Scale-new PetSettings().Scale)<.001&&Math.Abs(store.Load().Scale-cfg.Scale)<.001,"restore defaults supersedes older pending preferences");
                Directory.CreateDirectory(path+".tmp");volume.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});volume.Value=63;
                Check(!(bool)Call(pet,"FlushPreferenceSaves"),"disk failure is reported by preference flush");pet.Close();Pump();Check(pet.IsVisible,"failed preference flush cancels window close");
                volume.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Directory.Delete(path+".tmp");Check((bool)Call(pet,"FlushPreferenceSaves")&&store.Load().Volume==63,"retry after disk recovery saves latest value");
                Console.WriteLine("Preference saves: "+checks+" passed; evidence="+root);return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
            finally{if(Directory.Exists(path+".tmp"))Directory.Delete(path+".tmp");if(host!=null)host.Close();if(page!=null)page.Dispose();if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
    }
}
