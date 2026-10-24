using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class DataManagementUiChecks
{
    internal static int Run(string directory)
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        void Until(Func<bool> ready){var deadline=DateTime.UtcNow.AddSeconds(20);while(!ready()){if(DateTime.UtcNow>deadline)throw new TimeoutException("Native Data UI");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        void Wait(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        T Named<T>(Control root,string name)where T:Control=>root.GetLogicalDescendants().OfType<T>().Distinct().Single(x=>x.Name==name);
        void Click(Button button){Dispatcher.UIThread.Post(()=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));Dispatcher.UIThread.RunJobs();}
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());var store=new SettingsStore(Path.Combine(directory,"settings.json"));store.Save(new PetSettings{AutoUpdate=false,OnboardingState="completed",UiLanguage="en_US",AgentWorkspace=Path.Combine(directory,"agent-workspace"),GreetingEnabled=false,FollowCursor=false});
        var runtime=new Runtime(directory);using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),runtime);owner.Pet.Show();var host=new SettingsWindow(owner);host.Show();host.SelectSection("storage");Dispatcher.UIThread.RunJobs();
        DataActionConfirmationWindow Open(string action){Click(Named<Button>(host,action));var dialog=host.OwnedWindows.OfType<DataActionConfirmationWindow>().Single();Check(dialog.Owner==host,"native actual settings action owns confirmation "+action);return dialog;}
        void Confirm(DataActionConfirmationWindow dialog){Click(Named<Button>(dialog,"ConfirmDataAction"));Wait(dialog.PendingOperation);}
        string Status(DataActionConfirmationWindow dialog)=>Named<TextBlock>(dialog,"DataActionStatus").Text!;
        var captures=Path.GetFullPath(Path.Combine("work","data-ui-native",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(captures);
        void Capture(Window target,string name){AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=target.CaptureRenderedFrame();image!.Save(Path.Combine(captures,name+".png"),new PngBitmapEncoderOptions());}
        try{
            host.SelectSection("behavior");Dispatcher.UIThread.RunJobs();Click(Named<Button>(host,"GeneralDataManagement"));Check(Named<Button>(host,"ClearAllHistory").IsVisible,"native actual General entry opens Data management");
            Check(Named<CheckBox>(host,"SaveChatHistory")!=null,"native General Data management retains chat saving toggle");
            foreach(string action in new[]{"ClearAllHistory","RestoreDefaultPreferences"})foreach(string cancel in new[]{"cancel","escape","close"}){
                var dialog=Open(action);if(cancel=="cancel")Click(Named<Button>(dialog,"CancelDataAction"));else if(cancel=="escape")dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});else dialog.Close();
                Check(!dialog.IsVisible&&owner.Pet.IsVisible,"native "+action+" "+cancel+" leaves pet alive");
            }
            var attachments=Named<Button>(host,"OpenPreservedAttachments");Click(attachments);Until(()=>attachments.IsEnabled);Check(Named<TextBlock>(host,"DataManagementStatus").Text==TextResources.Get("dataManagement.attachmentsEmpty"),"native missing attachment directory reports empty");
            foreach(string action in new[]{"ClearAllHistory","RestoreDefaultPreferences"})using(owner.DataMaintenance.EnterActivity()){
                var dialog=Open(action);Confirm(dialog);Check(Status(dialog)==TextResources.Get("dataMaintenance.busy"),"native confirmed action rechecks busy "+action);dialog.Close();
            }
            var held=new TaskCompletionSource<int>();using(owner.DataMaintenance.Register(()=>held.Task)){
                var dialog=Open("ClearAllHistory");Click(Named<Button>(dialog,"ConfirmDataAction"));Check(!dialog.PendingOperation.IsCompleted&&!Named<Button>(dialog,"ConfirmDataAction").IsEnabled&&!Named<Button>(dialog,"CancelDataAction").IsEnabled,"native drain disables repeated action and cancel");dialog.Close();Check(dialog.IsVisible&&owner.Pet.IsVisible,"native busy close leaves dialog and pet alive");held.SetResult(0);Wait(dialog.PendingOperation);dialog.Close();
            }
            string history=Path.Combine(directory,"history");Directory.CreateDirectory(history);string broken=Path.Combine(history,"broken.history");File.WriteAllBytes(broken,new byte[40]);
            var partial=Open("ClearAllHistory");Confirm(partial);Check(Status(partial)==TextResources.Format("dataManagement.historyPartial",0,1)&&File.Exists(broken),"native partial preserves unreadable history with honest count: "+Status(partial));Capture(partial,"partial");partial.Close();File.Delete(broken);
            var messages=new ConversationLog();messages.Commit("fixture private chat","fixture answer");var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");messages.Attach(messages.Messages[0].Id,new[]{ChatAttachment.CreateImage("screen.png",png)});Wait(new HistoryStore(history).SaveAsync("fixture",messages.Messages));
            var chat=new ChatWindow(owner,false);chat.Show();Dispatcher.UIThread.RunJobs();Named<TextBox>(chat,"ChatDraft").Text="unsent native composer";chat.AddAttachments(new[]{ChatAttachment.CreateImage("draft.png",png)});
            var complete=Open("ClearAllHistory");Confirm(complete);Check(Status(complete)==TextResources.Format("dataManagement.historyComplete",1),"native real clear reports exact stored file count");complete.Close();
            Check(Named<TextBox>(chat,"ChatDraft").Text=="unsent native composer"&&chat.GetLogicalDescendants().OfType<Button>().Any(b=>b.Name=="RemoveAttachment")&&chat.CurrentPage=="chat","native actual main composer text draft attachments and navigation survive clearing");chat.Close();
            var archived=Directory.GetFiles(Path.Combine(directory,"preserved-attachments"),"*.png").Single();Check(File.ReadAllBytes(archived).SequenceEqual(png),"native clear retains sole-copy screenshot as retrievable image");
            host.SelectSection("voice");Dispatcher.UIThread.RunJobs();host.GetLogicalDescendants().OfType<Slider>().Distinct().Single(s=>s.Maximum==100).Value=17;host.SelectSection("storage");Dispatcher.UIThread.RunJobs();
            owner.Settings.Scale=1.6;owner.Settings.QuietMode=true;var reset=Open("RestoreDefaultPreferences");Confirm(reset);Check(Status(reset)==TextResources.Get("dataManagement.resetComplete")&&owner.Settings.Scale==1.25&&owner.Pet.Width==240&&!owner.Settings.QuietMode,"native saved reset applies runtime preferences");Capture(reset,"reset-success");reset.Close();
            host.SelectSection("voice");Dispatcher.UIThread.RunJobs();Check(host.GetLogicalDescendants().OfType<Slider>().Distinct().Single(s=>s.Maximum==100).Value==50&&runtime.Voices.All(v=>v.Volume==50),"native reset refreshes volume editor and applies runtime speech volume");host.SelectSection("storage");Dispatcher.UIThread.RunJobs();
            using(var locked=new FileStream(Path.Combine(directory,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read)){
                var dialog=Open("RestoreDefaultPreferences");Click(Named<Button>(dialog,"ConfirmDataAction"));owner.Settings.X=444;owner.Save();Check(!dialog.PendingOperation.IsCompleted,"arriving native position autosave does not block UI during held commit");locked.Dispose();Wait(dialog.PendingOperation);Check(new SettingsStore(Path.Combine(directory,"settings.json")).Load().X==444,"successful native reset flushes latest position after publication");dialog.Close();
            }
            var reservation=owner.DataMaintenance.BeginAsync();Wait(reservation);using(reservation.Result){owner.Settings.X=666;owner.Save();}
            using(var locked=new FileStream(Path.Combine(directory,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read)){var flush=owner.FlushDeferredSettingsAsync();Wait(flush);Check(flush.Result==TextResources.Get("dataManagement.pendingSaveFailed")&&owner.Settings.Scale==1.25&&new SettingsStore(Path.Combine(directory,"settings.json")).Load().Scale==1.25,"later native autosave failure never reports durable reset rolled back");}
            Wait(owner.FlushDeferredSettingsAsync());Check(new SettingsStore(Path.Combine(directory,"settings.json")).Load().X==666,"later native autosave failure remains retryable");
            string settingsPath=Path.Combine(directory,"settings.json");owner.Settings.Scale=1.7;using(var locked=new FileStream(settingsPath,FileMode.Open,FileAccess.Read,FileShare.Read)){
                var failed=Open("RestoreDefaultPreferences");Click(Named<Button>(failed,"ConfirmDataAction"));owner.Settings.X=555;owner.Save();Wait(failed.PendingOperation);Check(Status(failed).StartsWith(TextResources.Get("dataManagement.resetFailed"))&&Status(failed).Contains(TextResources.Get("dataManagement.pendingSaveFailed"))&&owner.Settings.Scale==1.7,"native failed save preserves preferences and pending position accurately");Capture(failed,"reset-save-failure");failed.Close();
            }
            Wait(owner.FlushDeferredSettingsAsync());Check(new SettingsStore(settingsPath).Load().X==555&&owner.Settings.Scale==1.7,"failed native reset retains latest position for retry");
            foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark})foreach(int width in new[]{570,790}){Application.Current!.RequestedThemeVariant=theme;host.Width=width;host.Height=680;Dispatcher.UIThread.RunJobs();Capture(host,"data-"+theme+"-"+width);}
            Check(owner.Pet.IsVisible,"native successful operations leave pet alive");Console.WriteLine("Native data captures="+captures);return checks;
        }finally{host.Close();owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}
    }
    private sealed class Runtime(string directory):DesktopRuntime
    {
        internal readonly List<VolumeSpeech> Voices=new();
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api){var speech=new VolumeSpeech();Voices.Add(speech);return speech;}public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("Fixture has no audio");public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("Fixture has no audio");
    }
    private sealed class VolumeSpeech:IReplySpeech
    {
        internal int Volume;public void SetVolume(int value)=>Volume=value;public void Stop(){}public void Dispose(){}public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel)=>Task.CompletedTask;
    }
}
