using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using Dafeiyu.Maintenance;
namespace Dafeiyu.Tests
{
    public static class UninstallUiTests
    {
        static int passed;static Application app;
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static void Pump(){var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};t.Tick+=delegate{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
        static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(30);while(!task.IsCompleted){if(DateTime.UtcNow>end)throw new TimeoutException("uninstall fixture");Pump();}task.GetAwaiter().GetResult();}
        static T Find<T>(DependencyObject root,string name)where T:FrameworkElement{var element=root as T;if(element!=null&&element.Name==name)return element;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=Find<T>(VisualTreeHelper.GetChild(root,i),name);if(found!=null)return found;}return null;}
        static void Click(Button b){if(b==null)throw new Exception("missing action");b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static object Field(object instance,string name){return instance.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(instance);}
        static UninstallOwner Prepare(PetWindow pet){var task=pet.PrepareUninstallAsync(CancellationToken.None);Wait(task);return task.Result;}
        static void Usable(PetWindow pet,string reason){using(pet.DataMaintenance.EnterActivity()){}Check(pet.IsVisible&&((DispatcherTimer)Field(pet,"timer")).IsEnabled,reason+" retains actual timer and gate");pet.Conversation.SelectPersona(pet.Conversation.PersonaId);Check(!(bool)Field(pet.Conversation,"disposed"),reason+" retains usable session");}
        static UninstallTicket Ticket(string root){var dir=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);return new UninstallTicket(new UninstallJob{WorkDirectory=dir},Process.GetCurrentProcess());}
        sealed class HeldImage:IImageGenerationExecutor
        {internal readonly TaskCompletionSource<GeneratedImage> Result=new TaskCompletionSource<GeneratedImage>();public Task<GeneratedImage> GenerateAsync(ImageGenerationRequest request,IProgress<ImageGenerationPhase> phase,CancellationToken token){return Result.Task;}}
        static void OwnerChecks(PetWindow pet,string root)
        {
            var session=pet.Conversation;
            using(pet.DataMaintenance.EnterActivity()){bool refused=false;try{Prepare(pet);}catch(InvalidOperationException){refused=true;}Check(refused,"busy preflight refuses admitted work");}Usable(pet,"busy refusal");
            var owner=Prepare(pet);Check(pet.DataMaintenance.IsMaintenanceActive&&File.Exists(Path.Combine(root,"settings.json")),"preflight owns actual gate and durably saves settings");
            Check(!pet.CanApplyUpdate(true),"update remains excluded during owned preparation");
            bool rejected=false;try{session.SelectPersona("other");}catch(InvalidOperationException){rejected=true;}Check(rejected,"unrelated session mutation has no owner bypass");
            owner.Dispose();Usable(pet,"abort");rejected=false;try{Wait(session.SaveForUninstallAsync(owner));}catch(InvalidOperationException){rejected=true;}Check(rejected,"disposed capability cannot write");
            var foreignTask=UninstallOwner.AcquireAsync(new DataMaintenanceGate());Wait(foreignTask);using(var foreign=foreignTask.Result){rejected=false;try{Wait(session.SaveForUninstallAsync(foreign));}catch(InvalidOperationException){rejected=true;}Check(rejected,"foreign gate capability cannot write");}
            using(var held=new FileStream(Path.Combine(root,"settings.json.tmp"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                rejected=false;try{Prepare(pet);}catch(IOException){rejected=true;}Check(rejected&&(bool)Field(pet,"deferredSettingsSave"),"failed actual settings write retains deferred save intent");
            }Usable(pet,"settings failure");using(Prepare(pet)){}Check(!(bool)Field(pet,"deferredSettingsSave"),"retry durably clears deferred intent");
            string history=Directory.GetFiles(Path.Combine(root,"history"),"*.history").First();
            using(var held=new FileStream(history,FileMode.Open,FileAccess.Read,FileShare.Read)){
                rejected=false;try{Prepare(pet);}catch(IOException){rejected=true;}Check(rejected&&!String.IsNullOrEmpty(session.HistoryWarning),"completed history writer warning refuses uninstall");
            }Usable(pet,"history failure");using(Prepare(pet)){}
            Wait(pet.Relationships.EnqueueVisit(pet.RelationshipCompanionId));string pending=Path.Combine(root,"relationships","pending.json");
            using(var held=new FileStream(pending,FileMode.Open,FileAccess.Read,FileShare.Read)){Wait(pet.Relationships.EnqueueVisit(pet.RelationshipCompanionId));Check(pet.Relationships.PendingCount>0&&!String.IsNullOrEmpty(pet.Relationships.Warning),"real relationship write failure remains pending");rejected=false;try{Prepare(pet);}catch(IOException){rejected=true;}Check(rejected,"relationship pending warning refuses removal");}
            Usable(pet,"relationship failure");Wait(pet.Relationships.RetryPendingAsync());using(Prepare(pet)){}
            using(var assets=new GeneratedImageStore(Path.Combine(root,"controlled-image"))){
                var imageGate=new DataMaintenanceGate();var executor=new HeldImage();var images=new ImageGenerationTaskManager(new ImageGenerationTaskStore(Path.Combine(root,"controlled-image")),assets,executor,new ImageAssetUsageRegistry(),imageGate);Wait(images.RestoreAsync(CancellationToken.None));
                var submission=new ImageGenerationTaskSubmission("owned",new ImageGenerationRequest{Provider=new ImageGenerationProvider{Id="owned",TimeoutSeconds=60},Prompt="owned",Persistent=true},new ImageTaskOrigin{Source=ImageGenerationSource.Workspace});
                var accepted=images.EnqueueAsync(submission,CancellationToken.None);Wait(accepted);rejected=false;try{Wait(UninstallOwner.AcquireAsync(imageGate));}catch(InvalidOperationException){rejected=true;}Check(rejected,"real running image manager refuses maintenance without Stop");
                Wait(images.CancelAsync(accepted.Result.Id));executor.Result.TrySetCanceled();try{Wait(accepted.Result.Completion);}catch(OperationCanceledException){}
                var acquired=UninstallOwner.AcquireAsync(imageGate);Wait(acquired);using(var imageOwner=acquired.Result){images.CheckForUninstall(imageOwner);rejected=false;try{Wait(images.EnqueueAsync(submission,CancellationToken.None));}catch(InvalidOperationException){rejected=true;}Check(rejected,"shared image admission rejects unrelated enqueue under owner");}
                Check(!(bool)Field(images,"stopping"),"aborted image preparation never irreversibly stops manager");
            }
            var blocked=new TaskCompletionSource<bool>();using(pet.DataMaintenance.Register(()=>blocked.Task))using(var cancel=new CancellationTokenSource()){
                var preparing=pet.PrepareUninstallAsync(cancel.Token);Pump();Check(pet.DataMaintenance.IsMaintenanceActive&&!preparing.IsCompleted,"actual preparation waits admitted drain");cancel.Cancel();blocked.SetResult(true);rejected=false;try{Wait(preparing);}catch(OperationCanceledException){rejected=true;}Check(rejected,"cancel pending preflight returns without close");
            }Usable(pet,"pending cancel");
            var writes=(SemaphoreSlim)Field(pet.Store,"writes");writes.Wait();var saving=pet.PrepareUninstallAsync(CancellationToken.None);Pump();pet.Settings.AgentGoal="latest during owned save";pet.SaveSettings();writes.Release();Wait(saving);saving.Result.Dispose();
            Check(new SettingsStore(Path.Combine(root,"settings.json")).Load().AgentGoal=="latest during owned save","latest coalesced settings survive pending async save");
            foreach(bool mutate in new[]{false,true}){
                owner=Prepare(pet);var ticket=Ticket(root);string commit=Path.Combine(ticket.WorkDirectory,"commit");
                System.ComponentModel.CancelEventHandler veto=delegate(object s,System.ComponentModel.CancelEventArgs e){if(mutate)pet.Settings.AgentGoal="changed by closing subscriber";else e.Cancel=true;};pet.Closing+=veto;
                var close=pet.CloseForUninstallAsync(owner,ticket,CancellationToken.None);Wait(close);pet.Closing-=veto;
                Check(!close.Result&&!File.Exists(commit),mutate?"late settings mutation cancels actual close and ticket":"actual Closing veto never commits ticket");Usable(pet,"close veto");
            }
            Console.WriteLine("UninstallUiTests owner: "+passed+" passed");
        }
        static void RelationshipSettingsChecks(PetWindow pet)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            using(var page=new CompanionHomePage(pet)){
                var window=new Window{Width=900,Height=780,Content=page};window.Show();
                try{
                    Wait(page.RefreshAsync());Wait(pet.Relationships.FlushAsync());
                    var exclusive=pet.DataMaintenance.BeginAsync();Wait(exclusive);
                    using(exclusive.Result){
                        Find<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;window.UpdateLayout();Pump();
                        var toggle=Find<CheckBox>(page,"RelationshipGrowthEnabled");toggle.IsChecked=false;toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                        Wait(page.PendingSettings);
                        Check(!(bool)Field(page,"saving")&&Find<CheckBox>(page,"RelationshipGrowthEnabled").IsEnabled,"busy settings refusal restores actual WPF controls");
                        var retry=Find<Button>(page,"RelationshipRetry");Check(retry.IsVisible&&retry.IsEnabled,"busy settings refusal shows enabled actionable WPF retry");
                        Click(retry);Wait(page.PendingRetry);Check(!(bool)Field(page,"saving")&&Find<Button>(page,"RelationshipRetry").IsEnabled,"busy WPF retry remains recoverable");
                    }
                    Click(Find<Button>(page,"RelationshipRetry"));Wait(page.PendingRetry);
                    Check(!pet.Relationships.Service.GetSnapshot(pet.RelationshipCompanionId).Settings.GrowthEnabled,"WPF retry persists refused exact settings intent after rollback");
                    Usable(pet,"relationship settings rollback");
                    Console.WriteLine("UninstallUiTests relationship settings: "+passed+" passed");
                }finally{window.Close();}
            }
        }
        [STAThread]public static int Main(string[] args)
        {
            if(args.Contains("--compile-only"))return 0;
            string locale=args.FirstOrDefault(a=>a.StartsWith("--locale="));locale=locale==null?"zh_CN":locale.Substring(9);TextResources.ConfigureStartupLanguage(locale);
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;
            string root=Path.GetFullPath(Path.Combine("work","uninstall-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            try{
                var settings=new PetSettings{UiLanguage=locale,ThemeMode=args.Contains("--dark")?"dark":"light",AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,FollowCursor=false,Wander=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                if(args.Contains("--job-warning")){var jobs=Path.Combine(root,".deskpet-agent-kernel","jobs");Directory.CreateDirectory(jobs);File.WriteAllText(Path.Combine(jobs,new string('a',32)+".json"),"corrupt owned job metadata");}
                Check(UpdateInstall.GetUninstallerPath(AppDomain.CurrentDomain.BaseDirectory)==null,"fixture executable directory has no matching registered uninstaller");
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();Wait(pet.AgentJobs.InitializeAsync());
                if(args.Contains("--relationship-settings")){RelationshipSettingsChecks(pet);return 0;}
                if(args.Contains("--job-warning")){bool refused=false;try{using(Prepare(pet)){}}catch(IOException){refused=true;}Check(refused,"real corrupt job metadata warning blocks uninstall after drain");Usable(pet,"job storage warning");return 0;}
                if(args.Contains("--closed")||args.Contains("--late")){
                    bool late=args.Contains("--late"),closed=false,committedDuringClosed=false;
                    var owner=Prepare(pet);var ticket=Ticket(root);string commit=Path.Combine(ticket.WorkDirectory,"commit");
                    if(late)Directory.CreateDirectory(Path.Combine(ticket.WorkDirectory,"commit-stage"));
                    pet.Closed+=delegate{closed=true;committedDuringClosed=File.Exists(commit);pet.Settings.AgentGoal="saved by Closed subscriber";pet.SaveSettings();};
                    bool failed=false;try{Wait(pet.CloseForUninstallAsync(owner,ticket,CancellationToken.None));}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
                    Check(closed&&!committedDuringClosed,"actual Closed notification precedes durable ticket commit");
                    Check(!pet.DataMaintenance.IsMaintenanceActive,"final closed completion releases exact maintenance owner");
                    Check(late?failed&&!File.Exists(commit):!failed&&File.Exists(commit),late?"late commit I/O failure is reported and never authorizes removal":"successful actual Closed drains then commits real ticket");
                    Check(File.Exists(Path.Combine(root,"settings.json")),"closed flow preserves actual data");
                    Check(new SettingsStore(Path.Combine(root,"settings.json")).Load().AgentGoal=="saved by Closed subscriber","final Closed settings callback is saved before helper commit");
                    pet=null;Console.WriteLine("UninstallUiTests closed: "+passed+" passed; "+root);return 0;
                }
                if(args.Contains("--owner")){
                    OwnerChecks(pet,root);return 0;
                }
                var about=new AboutPanel(pet);host=new Window{Content=about,Width=700,Height=680};host.Show();Pump();
                foreach(string action in new[]{"cancel","escape","close","portable"}){
                    bool found=false;Exception error=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
                    timer.Tick+=delegate{var dialog=app.Windows.OfType<Window>().FirstOrDefault(w=>w.Name=="UninstallConfirmation");if(dialog==null)return;timer.Stop();found=true;try{
                        Check(dialog.Owner==host,"confirmation belongs to actual About host");
                        if(action=="cancel"&&args.Contains("--capture")){dialog.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));string capture=Path.Combine(root,"uninstall-confirmation-"+locale+"-"+settings.ThemeMode+".png");using(var stream=File.Create(capture))encoder.Save(stream);Console.WriteLine("CAPTURE "+capture);}
                        if(action=="cancel")Click(Find<Button>(dialog,"CancelUninstall"));else if(action=="escape")dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(dialog),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
                        else if(action=="portable"){Click(Find<Button>(dialog,"ConfirmUninstall"));Wait(((UninstallConfirmationWindow)dialog).PendingOperation);Check(Find<TextBlock>(dialog,"UninstallStatus").Text==TextResources.Get("uninstall.portable"),"actual portable confirmation explains absence of automatic uninstall");dialog.Close();}
                        else dialog.Close();
                    }catch(Exception e){error=e;dialog.Close();}};
                    timer.Start();Click(Find<Button>(about,"UninstallApplication"));timer.Stop();if(error!=null)throw error;
                    Check(found,"actual About uninstall opens explicit confirmation before installation lookup");
                    Check(pet.IsVisible&&!pet.DataMaintenance.IsMaintenanceActive,"actual "+action+" retains live pet and admission");
                    using(pet.DataMaintenance.EnterActivity()){}Check(!Directory.Exists(Path.Combine(root,"uninstall")),"actual "+action+" launches no helper");
                }
                Console.WriteLine("SKIP actual input-focus keyboard injection: inactive owned desktop; routed Escape exercised");
                Console.WriteLine("UninstallUiTests: "+passed+" passed; "+root);return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(host!=null)host.Close();if(pet!=null){pet.Close();Wait(pet.ImageServicesShutdown);}app.Shutdown();}
        }
    }
}
