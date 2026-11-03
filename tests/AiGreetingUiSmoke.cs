using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
 public static class AiGreetingUiSmoke
 {
  static int passed;static Application app;static PetWindow pet;static FakeApi api;static string folder;
  static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
  [STAThread] public static int Main()
  {
   app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
   folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ai-greeting-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   try {
    var cfg=new PetSettings{SkinId="dafeiyu",GreetingEnabled=true,AiGreetingEnabled=false,AutoUpdate=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,SaveChatHistory=false,ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture-active"}};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
    pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();Pump();int captureFactories=0;pet.ObservationCaptureFactory=()=>{captureFactories++;return new WindowsDesktopObservationCapture();};api=new FakeApi();pet.GreetingGenerator=new GreetingService(()=>api,TimeSpan.FromSeconds(3));
    pet.ShowSettings();Pump();var settings=Settings();settings.SelectCompanionSection(2);Pump();var toggle=(CheckBox)settings.FindControl("AiGreetingEnabled");
    Check(toggle!=null && toggle.IsChecked==false,"visible AI greeting switch defaults off");cfg.DesktopObservationEnabled=true;pet.PreviewGreeting();Pump();Check(Visible() && api.Calls==0 && captureFactories==0,"local preview works without network or screenshot even with observation enabled");cfg.DesktopObservationEnabled=false;
    toggle.IsChecked=true;Check(pet.Store.Load().AiGreetingEnabled,"turning AI on persists immediately");Save(settings,"settings.png");settings.HostWindow.Close();Pump();
    pet.Conversation.History.Commit("我们刚才聊过海边画作","可以补几朵小浪花");int historyCount=pet.Conversation.History.Messages.Count;
    var first=StartPreview();first.SetResult("画作里的小浪花还等着你呢，慢慢来呀。");Pump();Check(Visible() && Text().Contains("小浪花"),"AI greeting displays in bubble");Check(api.Model=="fixture-active" && api.Prompt.Contains("海边画作"),"active raw model and recent companion history reach fake API");
    Check(pet.Conversation.History.Messages.Count==historyCount && !pet.DialogueBusy,"AI greeting leaves chat history and main dialogue state unchanged");Save(app.Windows.OfType<GreetingBubbleWindow>().Single(),"ai-bubble.png");
    var duplicate=StartPreview();duplicate.SetResult("画作里的小浪花还等着你呢，慢慢来呀！");Pump();Check(Visible() && !Text().Contains("画作里的小浪花"),"repeated AI greeting becomes a different preset");
    CancelCase("pause",()=>pet.SetPaused(true),()=>pet.SetPaused(false));
    CancelCase("quiet mode",()=>pet.SetQuietMode(true),()=>pet.SetQuietMode(false));
    CancelCase("AI switch off",()=>{cfg.AiGreetingEnabled=false;pet.ConfigureGreetings();},()=>{cfg.AiGreetingEnabled=true;pet.ConfigureGreetings();});
    CancelCase("greeting switch off",()=>{cfg.GreetingEnabled=false;pet.ConfigureGreetings();},()=>{cfg.GreetingEnabled=true;pet.ConfigureGreetings();});
    CancelCase("hidden pet",()=>pet.HidePet(),()=>pet.RestorePet());
    CancelCase("model changed",()=>{cfg.Text.Model="fixture-other";pet.RefreshChatAppearance();},()=>{cfg.Text.Model="fixture-active";pet.RefreshChatAppearance();});
    CancelCase("partner changed",()=>Check(pet.SelectSkin(pet.Catalog.Find("e-bao")),"partner can switch during greeting generation"),()=>pet.SelectSkin(pet.Catalog.Find("dafeiyu")));
    CancelCase("chat opened",()=>pet.ShowChat(),()=>{foreach(var w in app.Windows.OfType<ChatWindow>().ToArray())w.Close();});
    pet.ShowSettings();Pump();settings=Settings();settings.SelectCompanionSection(2);var closingSettings=StartPreview();settings.HostWindow.Close();closingSettings.SetResult("不该在设置关闭后弹出");Pump();Check(!Visible(),"closing preview settings drops pending result");
    // A local read failure must use a preset, never crash async-void or call AI.
    typeof(PetWindow).GetField("companionMemory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,new CompanionMemory(()=>{throw new IOException("fixture memory read failure");}));int before=api.Calls;pet.ConfigureGreetings();pet.PreviewGreeting();Pump();Check(Visible() && api.Calls==before,"history read failure uses preset without request");typeof(PetWindow).GetField("companionMemory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,null);
    // Automatic due request cancelled by a brief interaction gets a short retry.
    pet.ConfigureGreetings();pet.PlayAction("idle",0);api.Completion=new TaskCompletionSource<string>();var abandoned=api.Completion;var start=DateTime.UtcNow;
    typeof(PetWindow).GetField("greetingSchedule",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,new GreetingSchedule(start.AddMinutes(-1),1,1,()=>0));Probe(start);Pump(.03);int requests=api.Calls;Check(api.Completion.Task.IsCompleted==false && Field("greetingRequest")!=null,"due automatic greeting starts generation");
    pet.PlayInteraction("poked");abandoned.SetResult("旧的自动问候");Pump();pet.PlayAction("idle",0);api.Completion=new TaskCompletionSource<string>();Probe(start.AddSeconds(6));Pump(.03);Check(api.Calls==requests+1,"short interaction retries due greeting after five seconds");api.Completion.SetResult("回来啦，慢慢做就好。");Pump();Check(Visible() && Text().Contains("回来啦"),"retried automatic greeting displays once");
    pet.ConfigureGreetings();pet.PlayAction("idle",0);var schedule=Field("greetingSchedule");var next=((GreetingSchedule)schedule).NextAt;var preview=StartPreview();pet.SetPaused(true);preview.SetResult("迟到预览");Pump();pet.SetPaused(false);Check(((GreetingSchedule)Field("greetingSchedule")).NextAt>DateTime.UtcNow.AddMinutes(29),"explicit pause resets full interval");
    pet.ConfigureGreetings();pet.PlayAction("idle",0);var originalSchedule=(GreetingSchedule)Field("greetingSchedule");var originalNext=originalSchedule.NextAt;api.Completion=new TaskCompletionSource<string>();pet.PreviewGreeting();pet.RefreshChatAppearance();api.Completion.SetResult("取消的预览");Pump();Check(Object.ReferenceEquals(originalSchedule,Field("greetingSchedule")) && originalSchedule.NextAt==originalNext,"cancelled preview does not change automatic schedule");
    cfg.OnboardingState="deferred";cfg.OnboardingStep=12;pet.ShowOnboarding();Pump();var guide=app.Windows.OfType<OnboardingWindow>().Single();settings=Settings();Check(guide.TargetName=="GreetingEnabled","guide is on greeting lesson");
    before=api.Calls;typeof(PetWindow).GetMethod("ShowGreeting",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);Pump(.03);Check(api.Calls==before,"guide blocks automatic greeting even on greeting lesson");api.Completion=new TaskCompletionSource<string>();((Button)settings.FindControl("PreviewGreeting")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.03);Check(api.Calls==before+1,"greeting lesson button starts explicit AI preview");api.Completion.SetResult("在引导里也能预览问候呀。");Pump();Check(Visible() && Text().Contains("引导"),"greeting lesson preview displays actual bubble");api.Completion=new TaskCompletionSource<string>();((Button)settings.FindControl("PreviewGreeting")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.03);guide.Close();api.Completion.SetResult("引导关闭后的迟到预览");Pump();Check(!Visible(),"closing guide cancels its pending preview");settings.HostWindow.Close();Pump();
    pet.ShowSettings();Pump();settings=Settings();settings.SelectCompanionSection(2);var oldPersona=StartPreview();settings.SelectCompanionSection(0);((TextBox)settings.FindControl("PersonaPrompt")).Text="你是新的温柔伙伴，讲话简洁。";((Button)settings.FindControl("SavePersona")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));oldPersona.SetResult("旧人格的迟到问候");Pump();Check(!Visible() && pet.Conversation.History.Messages.Count==0,"saving new persona clears history and rejects old greeting");settings.HostWindow.Close();Pump();
    var clearing=StartPreview();pet.Conversation.Clear();clearing.SetResult("被清除历史的迟到问候");Pump();Check(!Visible(),"clearing history independently rejects pending greeting");
    var exiting=StartPreview();pet.Close();exiting.SetResult("退出后的结果");Pump();Check(!Visible(),"closing pet drops late AI result");pet=null;
    Console.WriteLine("AI greeting UI: "+passed+" passed; "+folder);return 0;
   } catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
  }
  static SettingsPage Settings(){return (SettingsPage)typeof(PetWindow).GetField("preferences",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);}
  static object Field(string name){return typeof(PetWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);}
  static void Probe(DateTime now){typeof(PetWindow).GetMethod("ProbeGreetings",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{now,false});}
  static TaskCompletionSource<string> StartPreview(){pet.ConfigureGreetings();pet.PlayAction("idle",0);api.Completion=new TaskCompletionSource<string>();int calls=api.Calls;pet.PreviewGreeting();Pump(.03);Check(api.Calls==calls+1,"preview starts exactly one fake request");return api.Completion;}
  static void CancelCase(string name,Action cancel,Action restore){var request=StartPreview();cancel();request.SetResult("迟到问候不应显示");Pump();Check(!Visible(),name+" discards late result");restore();Pump(.03);}
  static bool Visible(){return app.Windows.OfType<GreetingBubbleWindow>().Any(w=>w.IsVisible);}
  static string Text(){return Descendants<TextBlock>(app.Windows.OfType<GreetingBubbleWindow>().Single()).Single(w=>w.Name=="GreetingMessage").Text;}
  static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Descendants<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
  static void Pump(double seconds=.12){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
  static void Save(FrameworkElement target,string file){var bitmap=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(target);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(folder,file)))encoder.Save(stream);}
  sealed class FakeApi:IConversationApi
  {
   public TaskCompletionSource<string> Completion;public int Calls;public string Model,Prompt;
   public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> messages,CancellationToken cancel){Calls++;Model=s.Model;Prompt=String.Join("\n",messages.Select(m=>m.Content));return Completion.Task;}
   public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){throw new NotSupportedException();}
   public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){throw new NotSupportedException();}public void Dispose(){}
  }
 }
}
