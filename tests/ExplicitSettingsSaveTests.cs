using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ExplicitSettingsSaveTests
    {
        static int checks;
        const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Until(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>deadline)throw new TimeoutException();Pump();}}
        static void Wait(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        static object Field(object target,string name){return target.GetType().GetField(name,Members).GetValue(target);}
        sealed class SilentSpeech:IReplySpeech
        {public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int value){}public void Stop(){}public void Dispose(){}}
        static void Invoke(object target,string method){target.GetType().GetMethod(method,Members).Invoke(target,null);}
        static Task<bool> Pending(PetWindow pet){return (Task<bool>)Field(pet,"explicitSettingsSave");}
        static void Go(OnboardingWindow guide,string lesson)
        {typeof(OnboardingWindow).GetField("index",Members).SetValue(guide,Array.FindIndex(OnboardingLessons.All,l=>l.Id==lesson));Invoke(guide,"Navigate");Pump();}
        static void ReviewRegression(string mode,PetWindow pet,SettingsWindow window,PetSettings cfg,SettingsStore store,string root,ManualResetEventSlim entered,ManualResetEventSlim release)
        {
            OnboardingWindow guide=null;ConversationSession replacement=null;var original=pet.Conversation;
            try{
                if(mode=="--review-persona"){
                    var persona=(PersonaPanel)Field(window.Page,"persona");((TextBox)Field(persona,"prompt")).Text="review saved persona";
                    store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture timeout");};var saved=persona.ChangeAsync(false);Until(()=>entered.IsSet);
                    replacement=new ConversationSession(new ApiClient(),new SilentSpeech(),()=>null,new HistoryStore(Path.Combine(root,"other-history")),()=>true,maintenance:pet.DataMaintenance);replacement.SelectPersona(pet.Skin.Id);replacement.History.Commit("KEEP_OTHER_USER","KEEP_OTHER_REPLY");
                    typeof(PetWindow).GetField("conversation",Members).SetValue(pet,replacement);release.Set();Wait(saved);
                    Check(replacement.History.Messages.Count==2,"persona save never clears a different conversation selected during I/O");return;
                }
                if(mode=="--review-compat"){
                    cfg.Volume=64;pet.RequestVolumeSave();store.BeforePreferenceCommit=()=>{throw new IOException("fixture preference failure");};var failed=pet.FlushPreferenceSavesAsync();Wait(failed);
                    store.BeforePreferenceCommit=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture timeout");};var retry=pet.FlushPreferenceSavesAsync();Until(()=>entered.IsSet);Directory.CreateDirectory(Path.Combine(root,"settings.json.tmp"));
                    using(Dispatcher.CurrentDispatcher.DisableProcessing()){release.Set();((ConnectionSettingsPanel)Field(window.Page,"connections")).Save();}Wait(retry);
                    Check(pet.Notice==TextResources.Get("petWindow.saveSettings.notice"),"compatibility failure survives an older preference retry callback");return;
                }
                guide=new OnboardingWindow(pet,window.Page,window);guide.Show();Go(guide,mode=="--review-guide"?"text.save":"text.test");
                store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture timeout");};
                if(mode=="--review-guide"){
                    Invoke(guide,"Advance");Until(()=>entered.IsSet);guide.Close();Check(guide.IsVisible,"guide waits for in-flight configuration advance before closing");release.Set();var saved=Pending(pet);if(saved!=null)Wait(saved);return;
                }
                Invoke(guide,"TestText");Until(()=>entered.IsSet);var cancel=(CancellationTokenSource)Field(guide,"textTest");Check(cancel!=null,"guide text test reserves cancellation and reentry before its first await");
                Invoke(guide,"TestText");Check(ReferenceEquals(cancel,Field(guide,"textTest")),"repeated text test reuses one pending test");Go(guide,"asr.provider");Check(cancel.IsCancellationRequested,"navigation cancels a text test still waiting for save");release.Set();var task=Pending(pet);if(task!=null)Wait(task);Check(Field(guide,"textTest")==null,"cancelled old lesson never starts a late text test");
            }finally{
                release.Set();store.BeforePreferenceCommit=null;store.BeforeExactSave=null;var task=Pending(pet);if(task!=null)Wait(task);if(guide!=null)guide.Close();typeof(PetWindow).GetField("conversation",Members).SetValue(pet,original);if(replacement!=null)replacement.Dispose();string temp=Path.Combine(root,"settings.json.tmp");if(Directory.Exists(temp))Directory.Delete(temp);
            }
        }
        [STAThread]public static int Main(string[] args)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;SettingsWindow window=null;
            var release=new ManualResetEventSlim();var entered=new ManualResetEventSlim();
            string root=Path.GetFullPath(Path.Combine("work","explicit-save-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);string path=Path.Combine(root,"settings.json");
            try{
                Check(typeof(PetWindow).GetMethod("SaveSettingsAsync",Members)!=null,"explicit configuration exposes an awaited durable save");
                var cfg=new PetSettings{UiLanguage="zh_CN",ThemeMode="dark",AutoUpdate=false,GreetingEnabled=false,Wander=false,FollowCursor=false,SaveChatHistory=false,OnboardingState="completed",Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="offline-model"}};
                foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                Check(cfg.Copy().ThemeMode==cfg.ThemeMode,"detached settings copy preserves theme");
                var store=new SettingsStore(path);store.Save(cfg);pet=new PetWindow(null,cfg,store,null);pet.Show();window=new SettingsWindow(pet);window.Show();window.SelectAiSection(0);Pump();
                if(args.Contains("--project")){
                    var project=ProjectCatalogStore.CreateProject(pet.Projects,"project-original",root);pet.ProjectStore.Save(pet.Projects);typeof(PetWindow).GetField("editingProject",Members).SetValue(pet,project);
                    var work=new WorkSettingsPanel(pet);var workHost=new Window{Content=work,Width=1000,Height=800};workHost.Show();Pump();
                    var goal=(TextBox)Field(work,"goal");goal.Text="async-project-goal";pet.ProjectStore.BeforeConfigurationWrite=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture timeout");};var task=work.SaveAsync();Until(()=>entered.IsSet);
                    bool projectResponsive=false;Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>projectResponsive=true));Pump();Check(projectResponsive&&!work.IsEnabled&&project.Goal!="async-project-goal","project save button keeps UI responsive and delays publication");
                    release.Set();Wait(task);pet.ProjectStore.BeforeConfigurationWrite=null;Check(task.Result&&project.Goal=="async-project-goal"&&pet.ProjectStore.Load().Projects.Single(p=>p.Id==project.Id).Goal=="async-project-goal","project save persists and publishes through the binding guard");entered.Reset();release.Reset();goal.Text="failed-project-goal";pet.ProjectStore.BeforeConfigurationWrite=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture timeout");};pet.ProjectStore.BeforeConfigurationCommit=()=>{throw new IOException("fixture project write failure");};var failedProject=work.SaveAsync();Until(()=>entered.IsSet);
                    project.Name="renamed-during-project-save";pet.ProjectStore.Save(pet.Projects);release.Set();Wait(failedProject);pet.ProjectStore.BeforeConfigurationWrite=null;pet.ProjectStore.BeforeConfigurationCommit=null;
                    Check(!failedProject.Result&&project.Name=="renamed-during-project-save"&&pet.ProjectStore.Load().Projects.Single(p=>p.Id==project.Id).Name==project.Name,"failed project save preserves a concurrent successful rename in live and durable catalog");
                    workHost.Close();Console.WriteLine("Project UI saves: "+checks+" passed");return 0;
                }
                if(args.Length>0&&args[0].StartsWith("--review-")){ReviewRegression(args[0],pet,window,cfg,store,root,entered,release);Console.WriteLine("Review regression: "+checks+" passed");return 0;}
                var panel=(ConnectionSettingsPanel)Field(window.Page,"connections");
                var name=(TextBox)window.FindControl("TextProviderName");name.Text="async-first";
                int writes=0;store.BeforeExactSave=()=>{Interlocked.Increment(ref writes);entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};
                var save=(Button)window.FindControl("SaveConnections");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>entered.IsSet);
                bool responsive=false;Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>responsive=true));Pump();
                Check(responsive&&!panel.IsEnabled,"real save button leaves dispatcher responsive while disabling its editor");
                Check(store.Load().TextProviders[0].Name!="async-first"&&panel.HasUnsavedChanges,"pending write does not acknowledge provider draft");
                var repeated=panel.SaveAsync();Check(writes==1,"repeated explicit save joins the current editor transaction");
                name.Text="edited-during-save";release.Set();Wait(repeated);store.BeforeExactSave=null;
                Check(store.Load().TextProviders[0].Name=="async-first"&&panel.HasUnsavedChanges,"completion preserves an edit newer than the submitted draft");
                Wait(panel.SaveAsync());Check(!panel.HasUnsavedChanges&&store.Load().TextProviders[0].Name=="edited-during-save","retry persists newest draft and then acknowledges it");
                entered.Reset();release.Reset();store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};name.Text="maintenance-save";var pending=panel.SaveAsync();Until(()=>entered.IsSet);
                var maintenance=pet.DataMaintenance.BeginAsync();Pump();Check(!maintenance.IsCompleted,"maintenance waits for explicit configuration transaction");release.Set();Wait(pending);Wait(maintenance);store.BeforeExactSave=null;
                using(maintenance.Result)Check(store.Load().TextProviders[0].Name=="maintenance-save","maintenance acquires lease after provider is durable");
                entered.Reset();release.Reset();store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};name.Text="preference-interleave";var ordinary=panel.SaveAsync();Until(()=>entered.IsSet);
                pet.BeginPreferenceEdit();pet.PreviewScale(1.4);pet.RequestScaleSave();release.Set();Wait(ordinary);store.BeforeExactSave=null;pet.EndPreferenceEdit();Wait(pet.FlushPreferenceSavesAsync());
                Check(Math.Abs(store.Load().Scale-1.4)<.001,"explicit completion does not discard a newer held ordinary preference");
                entered.Reset();release.Reset();store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};name.Text="reset-interleave";var beforeReset=panel.SaveAsync();Until(()=>entered.IsSet);
                var reset=store.RestoreDefaultPreferencesAsync(cfg,pet.DataMaintenance);Pump();Check(!reset.IsCompleted,"restore defaults drains explicit save before taking its snapshot");release.Set();Wait(beforeReset);Wait(reset);store.BeforeExactSave=null;
                Check(store.Load().Scale==new PetSettings().Scale&&store.Load().TextProviders[0].Name=="reset-interleave","restore publishes defaults without dropping the saved provider");
                name.Text="failed-draft";store.BeforeExactSave=()=>{throw new IOException("fixture disk failure");};var failure=panel.SaveAsync();Wait(failure);
                Check(!failure.Result&&panel.IsEnabled&&panel.HasUnsavedChanges,"failed explicit write restores usable editor and retains dirty draft");
                Check(store.Load().TextProviders[0].Name=="reset-interleave"&&cfg.TextProviders[0].Name=="reset-interleave","failed provider save preserves previous live and durable settings");
                store.BeforeExactSave=null;Wait(panel.SaveAsync());
                entered.Reset();release.Reset();store.BeforeLanguageCommit=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};
                var language=pet.ChangeLanguageAsync("en_US");Until(()=>entered.IsSet);Check(cfg.UiLanguage=="zh_CN"&&store.Load().UiLanguage=="zh_CN","async language save publishes only after durable commit");release.Set();Wait(language);store.BeforeLanguageCommit=null;
                Check(language.Result&&cfg.UiLanguage=="en_US"&&store.Load().UiLanguage=="en_US","async language change commits then refreshes live language");
                entered.Reset();release.Reset();store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};name.Text="exit-save";var exiting=panel.SaveAsync();Until(()=>entered.IsSet);pet.Close();Pump();
                Check(pet.IsVisible&&!exiting.IsCompleted,"exit waits for the held explicit save");release.Set();Wait(exiting);store.BeforeExactSave=null;Until(()=>!pet.IsVisible);
                Check(store.Load().TextProviders[0].Name=="exit-save","exit preserves final explicit configuration");
                Console.WriteLine("Explicit settings saves: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
            finally{release.Set();if(window!=null)window.Close();if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
    }
}
