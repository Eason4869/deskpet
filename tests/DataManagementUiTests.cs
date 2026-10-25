using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class DataManagementUiTests
    {
        static int checks;static Application app;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(20);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("Data UI");Pump();}task.GetAwaiter().GetResult();Pump();}
        static T Find<T>(DependencyObject root,string name)where T:FrameworkElement{var found=root as T;if(found!=null&&found.Name==name)return found;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=Find<T>(VisualTreeHelper.GetChild(root,i),name);if(child!=null)return child;}return null;}
        static void Click(Button button){if(button==null)throw new Exception("Missing data management action");Wait(button.Dispatcher.InvokeAsync(delegate{button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}).Task);}
        static void Dialog(Button action,Action<Window> run)
        {
            Exception failure=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};timer.Tick+=delegate{var dialog=app.Windows.OfType<Window>().FirstOrDefault(w=>w.Name=="DataActionConfirmation");if(dialog==null)return;timer.Stop();try{run(dialog);}catch(Exception error){failure=error;}finally{dialog.Close();}};timer.Start();Click(action);if(failure!=null)throw failure;
        }
        static Task Operation(Window dialog){return (Task)dialog.GetType().GetProperty("PendingOperation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).GetValue(dialog,null);}
        static void SaveNativePosition(PetWindow pet,double x)
        {
            var handle=new WindowInteropHelper(pet).Handle;var before=DesktopInterop.Bounds(handle);
            DesktopInterop.Move(handle,new Point(x,before.Top));
            typeof(PetWindow).GetMethod("SavePosition",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(pet,null);
            Check(pet.Settings.X==x&&DesktopInterop.Bounds(handle).Left==x,"pending position autosave captures actual WPF window location");
        }
        static int SpeechVolume(PetWindow pet)
        {
            const System.Reflection.BindingFlags fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var speech=(SpeechOutput)typeof(ConversationSession).GetField("speech",fields).GetValue(pet.Conversation);
            return (int)typeof(SpeechOutput).GetField("currentVolume",fields).GetValue(speech);
        }
        [STAThread]public static int Main(string[] args)
        {
            TextResources.ConfigureStartupLanguage(args.Length==0?"zh_CN":args[0]);
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;
            string root=Path.GetFullPath(Path.Combine("work","data-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            Check(TextResources.CurrentLanguage==(args.Length==0?"zh_CN":args[0]),"actual WPF startup catalog matches requested locale");
            try{
                var cfg=new PetSettings{UiLanguage=args.Length==0?"zh_CN":args[0],AutoUpdate=false,OnboardingState="completed",AgentWorkspace=Path.Combine(root,"agent-workspace"),GreetingEnabled=false,GreetingMinMinutes=90,GreetingMaxMinutes=120,PlainTextEditor=true,FollowCursor=false,Wander=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();
                var page=new SettingsPage(pet);host=new Window{Content=page,Width=620,Height=680};host.Show();page.SelectWorkSettings();Pump();var workGoal=(TextBox)page.FindControl("AgentGoal");workGoal.Text="unsaved project goal";page.SelectGeneralSection(2);Pump();
                var clear=Find<Button>(page,"ClearAllHistory");var reset=Find<Button>(page,"RestoreDefaultPreferences");var attachments=Find<Button>(page,"OpenPreservedAttachments");
                Check(clear!=null&&reset!=null&&attachments!=null&&Find<CheckBox>(page,"SaveChatHistory")!=null,"real General Data management exposes independent actions and SaveChatHistory");
                var clearCaption=clear.Content;TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Pump();Check(ReferenceEquals(clearCaption,clear.Content)&&((TextBlock)clear.Content).Text==TextResources.Get("dataManagement.clearHistory")&&workGoal.Text=="unsaved project goal","retained styled data action switches language without replacing its label or work draft");TextResources.ApplyLanguage(TextResources.PrepareLanguage(args.Length==0?"zh_CN":args[0]));Pump();
                Dialog(clear,d=>{Check(d.Owner==host,"confirmation owned by settings host");Click(Find<Button>(d,"CancelDataAction"));});Check(pet.IsVisible,"cancel leaves pet alive");
                Dialog(reset,d=>d.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(d),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent}));Check(pet.IsVisible,"reset Esc leaves pet alive");
                Dialog(clear,d=>{});Check(pet.IsVisible,"confirmation X leaves pet alive");
                foreach(var action in new[]{clear,reset})foreach(string cancel in new[]{"cancel","escape","close"})Dialog(action,d=>{
                    if(cancel=="cancel")Click(Find<Button>(d,"CancelDataAction"));else if(cancel=="escape")d.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(d),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
                    Check(pet.IsVisible&&cfg.Scale==1.25,"cancel without preference change "+action.Name+" "+cancel);
                });
                Click(attachments);var deadline=DateTime.UtcNow.AddSeconds(10);while(!attachments.IsEnabled){if(DateTime.UtcNow>deadline)throw new TimeoutException("Attachment state");Pump();}Check(Find<TextBlock>(page,"DataManagementStatus").Text==TextResources.Get("dataManagement.attachmentsEmpty"),"absent attachment archive has truthful empty state");CaptureStates(host,root,"attachments-empty",false);
                using(pet.DataMaintenance.EnterActivity())Dialog(clear,d=>{Click(Find<Button>(d,"ConfirmDataAction"));Wait(Operation(d));Check(Find<TextBlock>(d,"DataActionStatus").Text==TextResources.Get("dataMaintenance.busy"),"clear rechecks busy after confirmation");});
                using(pet.DataMaintenance.EnterActivity())Dialog(reset,d=>{Click(Find<Button>(d,"ConfirmDataAction"));Wait(Operation(d));Check(Find<TextBlock>(d,"DataActionStatus").Text==TextResources.Get("dataMaintenance.busy"),"reset rechecks busy after confirmation");});
                var held=new TaskCompletionSource<int>();using(pet.DataMaintenance.Register(()=>held.Task))Dialog(clear,d=>{
                    Click(Find<Button>(d,"ConfirmDataAction"));Check(!Operation(d).IsCompleted&&!Find<Button>(d,"ConfirmDataAction").IsEnabled&&!Find<Button>(d,"CancelDataAction").IsEnabled,"maintenance disables repeated action and cancel while draining");
                    d.Close();pet.Close();Check(d.IsVisible&&pet.IsVisible,"busy maintenance keeps confirmation and pet alive during close attempts");held.SetResult(0);Wait(Operation(d));
                });
                string history=Path.Combine(root,"history");Directory.CreateDirectory(history);File.WriteAllText(Path.Combine(history,"broken.history"),"corrupt");
                Dialog(clear,d=>{Click(Find<Button>(d,"ConfirmDataAction"));Wait(Operation(d));Check(Find<TextBlock>(d,"DataActionStatus").Text.Contains(TextResources.Format("dataManagement.historyPartial",0,1)),"partial clearing reports actual retained failure");CaptureStates(d,root,"clear-partial",true);});File.Delete(Path.Combine(history,"broken.history"));
                pet.Conversation.InputDraft="unsent draft";pet.Conversation.History.Commit("owned private text","owned reply");Wait(pet.Conversation.SaveHistoryForCloseAsync());
                var chat=new ChatWindow(pet);chat.Show();Pump();var editor=Find<MarkdownEditor>(chat,"MessageInput");editor.Text="unsent draft";var composer=Find<ConversationComposer>(chat,"");composer.AddAttachments(new[]{ChatAttachment.CreateImage("draft.png",Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII="))});
                Dialog(clear,d=>{Click(Find<Button>(d,"ConfirmDataAction"));Wait(Operation(d));Check(Find<TextBlock>(d,"DataActionStatus").Text==TextResources.Format("dataManagement.historyComplete",2),"complete clearing reports actual removed files: "+Find<TextBlock>(d,"DataActionStatus").Text);Capture(d,Path.Combine(root,"clear-success.png"));});
                Check(pet.Conversation.InputDraft=="unsent draft","clear preserves composer draft");
                Check(editor.Text=="unsent draft"&&composer.HasEdit,"actual WPF main composer draft and draft attachment survive clearing");chat.Close();
                Wait(pet.Conversation.SaveHistoryForCloseAsync());Check(new HistoryStore(history).Load(pet.Conversation.PersonaId).Count==0,"actual UI clear and close-save never resurrect sent history");
                page.SelectAiSection(2);Pump();var volume=Find<Slider>(page,"VoiceVolume");volume.Value=20;var volumeLabel=((StackPanel)volume.Parent).Children.OfType<TextBlock>().Single();
                var voiceDraft=Find<ComboBox>(page,"SpeechVoice");voiceDraft.Text="unsaved voice selection";var providerDraft=Find<TextBox>(page,"SpeechProviderName");providerDraft.Text="unsaved speech provider";
                Check(cfg.Volume==20&&SpeechVolume(pet)==20,"actual speech volume editor applies nondefault runtime volume");page.SelectGeneralSection(2);Pump();
                cfg.AgentCommandsEnabled=true;cfg.Scale=1.6;Dialog(reset,d=>{Click(Find<Button>(d,"ConfirmDataAction"));Wait(Operation(d));Check(cfg.Scale==1.25&&pet.Width==240,"reset applies persisted scale at runtime");});
                page.SelectAiSection(2);Pump();Check(volume.Value==50&&volumeLabel.Text==TextResources.Format("connectionSettingsPanel.connectionSettingsPanel.labelValue",50)&&SpeechVolume(pet)==50&&new SettingsStore(Path.Combine(root,"settings.json")).Load().Volume==50,"reset refreshes retained WPF speech slider label durable and runtime volume");
                Check(cfg.AgentCommandsEnabled&&File.ReadAllText(Path.Combine(root,"settings.json")).Contains("\"AgentCommandsEnabled\":true"),"volume editor refresh neither saves nor normalizes retained settings");
                Check(ReferenceEquals(volume,Find<Slider>(page,"VoiceVolume"))&&voiceDraft.Text=="unsaved voice selection"&&providerDraft.Text=="unsaved speech provider","volume refresh preserves existing provider and voice editor drafts");
                volume.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(volume),0,Key.Right){RoutedEvent=Keyboard.KeyDownEvent});
                Check(volume.Value==55&&cfg.Volume==55&&SpeechVolume(pet)==55&&new SettingsStore(Path.Combine(root,"settings.json")).Load().Volume==55,"keyboard volume increment starts from reset value and persists runtime value");page.SelectGeneralSection(2);Pump();
                page.SelectCompanionSection(2);Pump();Check(((TextBox)page.FindControl("GreetingMinMinutes")).Text=="30"&&((TextBox)page.FindControl("GreetingMaxMinutes")).Text=="60","reset refreshes greeting editors so old intervals cannot be saved again");page.SelectGeneralSection(2);Pump();
                page.SelectWorkSettings();Pump();Check(workGoal.Text=="unsaved project goal"&&((CheckBox)page.FindControl("PlainTextEditor")).IsChecked==false,"reset refreshes chat preferences while preserving unrelated work draft");page.SelectGeneralSection(2);Pump();
                using(var locked=new FileStream(Path.Combine(root,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read))Dialog(reset,d=>{
                    Click(Find<Button>(d,"ConfirmDataAction"));cfg.X=444;pet.SaveSettings();Check(!Operation(d).IsCompleted,"arriving WPF position autosave does not block UI during held commit");locked.Dispose();Wait(Operation(d));
                    Check(new SettingsStore(Path.Combine(root,"settings.json")).Load().X==444&&cfg.Scale==1.25,"successful WPF reset flushes latest position after publication");
                });
                var reservation=pet.DataMaintenance.BeginAsync();Wait(reservation);using(reservation.Result){cfg.X=666;pet.SaveSettings();}
                using(var locked=new FileStream(Path.Combine(root,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read)){var flush=pet.FlushDeferredSettingsAsync();Wait(flush);Check(flush.Result==TextResources.Get("dataManagement.pendingSaveFailed")&&cfg.Scale==1.25&&new SettingsStore(Path.Combine(root,"settings.json")).Load().Scale==1.25,"later WPF autosave failure never reports durable reset rolled back");}
                Wait(pet.FlushDeferredSettingsAsync());Check(new SettingsStore(Path.Combine(root,"settings.json")).Load().X==666,"later WPF autosave failure remains retryable");
                cfg.Scale=1.7;var resetEntered=new TaskCompletionSource<bool>();var releaseReset=new TaskCompletionSource<bool>();
                using(pet.DataMaintenance.Register(()=>{resetEntered.TrySetResult(true);return releaseReset.Task;}))
                using(var locked=new FileStream(Path.Combine(root,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read)){
                    try{Dialog(reset,d=>{
                        Click(Find<Button>(d,"ConfirmDataAction"));
                        try{Wait(resetEntered.Task);Check(pet.DataMaintenance.IsMaintenanceActive&&!Operation(d).IsCompleted,"failed reset reserves maintenance before arriving position autosave");SaveNativePosition(pet,555);}
                        finally{releaseReset.TrySetResult(true);}
                        Wait(Operation(d));string status=Find<TextBlock>(d,"DataActionStatus").Text;
                        Check(cfg.Scale==1.7&&status.StartsWith(TextResources.Get("dataManagement.resetFailed"))&&status.Contains(TextResources.Get("dataManagement.pendingSaveFailed")),"actual reset save failure keeps old preferences and pending position accurately: scale="+cfg.Scale+" status="+status);Capture(d,Path.Combine(root,"reset-save-failure.png"));
                    });}finally{releaseReset.TrySetResult(true);}
                }
                // A deferred save must reject active producers rather than overwrite them.
                using(pet.DataMaintenance.EnterActivity()){
                    var busyRetry=pet.FlushDeferredSettingsAsync();Wait(busyRetry);
                    Check(busyRetry.Result==TextResources.Get("dataManagement.pendingSaveFailed")&&cfg.X==555,"busy retry retains latest unsaved position");
                }
                // Visible frames enqueue keepsake probes while Wait pumps the dispatcher.
                // End this fixture's visible encounter before draining its producers.
                pet.Hide();
                Wait(pet.Relationships.FlushAsync());Wait(pet.HolidayKeepsakes.FlushAsync());
                var retryErrors=new System.Collections.Generic.List<string>();int recordingRetryError=0;
                EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> retryDiagnostic=delegate(object sender,System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs eventArgs){
                    if(System.Threading.Interlocked.Exchange(ref recordingRetryError,1)!=0)return;
                    try{
                        var error=eventArgs.Exception;var stack=error.StackTrace??"";
                        if(!(error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException)||
                            !(stack.Contains("DataMaintenanceGate.BeginAsync")||stack.Contains("SettingsStore.SaveSnapshot")||stack.Contains("SettingsStore.SaveExactSnapshotAsync")||stack.Contains("AtomicFile.Commit")||stack.Contains("PetWindow.FlushDeferredSettingsAsync")))return;
                        string detail=error.GetType().Name+" HResult=0x"+error.HResult.ToString("X8")+" message="+error.Message.Replace(root,"<fixture>")+" stack="+stack.Replace(root,"<fixture>").Replace("\r"," ").Replace("\n"," / ");
                        lock(retryErrors){if(retryErrors.Count==4)retryErrors.RemoveAt(0);retryErrors.Add(detail);}
                    }catch{}finally{System.Threading.Interlocked.Exchange(ref recordingRetryError,0);}
                };
                Task<string> retryWarning;AppDomain.CurrentDomain.FirstChanceException+=retryDiagnostic;
                try{retryWarning=pet.FlushDeferredSettingsAsync();Wait(retryWarning);}
                finally{AppDomain.CurrentDomain.FirstChanceException-=retryDiagnostic;}
                var retryDisk=new SettingsStore(Path.Combine(root,"settings.json")).Load();bool retrySaved=retryDisk.X==555&&cfg.Scale==1.7;
                Check(retrySaved,"failed WPF reset retains latest position for retry: actual="+cfg.X+" durable="+retryDisk.X+" scale="+cfg.Scale+" warning="+retryWarning.Result+(retrySaved?"":" firstChance="+(retryErrors.Count==0?"none":String.Join(" | ",retryErrors))));
                pet.Show();
                CaptureStates(host,root,"data",false);
                foreach(var action in new[]{clear,reset})Dialog(action,d=>{CaptureStates(d,root,action==clear?"clear-confirm":"reset-confirm",true);foreach(string name in new[]{"ConfirmDataAction","CancelDataAction"}){var button=Find<Button>(d,name);var bounds=button.TransformToAncestor(d).TransformBounds(new Rect(button.RenderSize));Check(bounds.Left>=0&&bounds.Right<=d.ActualWidth&&bounds.Bottom<=d.ActualHeight,"confirmation action bounds "+name);Check(!button.IsDefault,"maintenance confirmation has no Enter default "+name);}});
                Check(pet.IsVisible,"successful maintenance leaves pet alive");Console.WriteLine("DataManagementUiTests: "+checks+" passed; captures="+root);host.Close();page.Dispose();return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}finally{if(host!=null)host.Close();if(pet!=null){pet.Close();Wait(pet.ImageServicesShutdown);}app.Shutdown();}
        }
        static void CaptureStates(Window target,string root,string scene,bool modal){foreach(string theme in new[]{"light","dark"})foreach(int width in modal?new[]{380,520}:new[]{570,900}){ThemeManager.Apply(theme);target.Width=width;if(!modal)target.Height=920;Pump();if(modal)foreach(string name in new[]{"ConfirmDataAction","CancelDataAction"}){var button=Find<Button>(target,name);var bounds=button.TransformToAncestor(target).TransformBounds(new Rect(button.RenderSize));Check(bounds.Left>=0&&bounds.Right<=target.ActualWidth&&bounds.Bottom<=target.ActualHeight,"actual narrow/wide confirmation bounds "+theme+" "+width+" "+name);}Capture(target,Path.Combine(root,scene+"-"+theme+"-"+width+".png"));}}
        static void Capture(FrameworkElement target,string path){target.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth),(int)Math.Ceiling(target.ActualHeight),96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var draw=background.RenderOpen())draw.DrawRectangle(ThemeManager.Surface,null,new Rect(0,0,target.ActualWidth,target.ActualHeight));bitmap.Render(background);bitmap.Render(target);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
    }
}
