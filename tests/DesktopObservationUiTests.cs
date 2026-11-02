using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
 public static class DesktopObservationUiTests
 {
  [STAThread] public static int Main()
  {
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-observation-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);PetWindow pet=null;
   try{
    var settings=new PetSettings{GreetingEnabled=true,AiGreetingEnabled=true,AutoUpdate=false,FollowCursor=false,Wander=false,SaveChatHistory=false,OnboardingState="completed"};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
    pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();Pump();pet.ShowSettings();Pump();var window=(SettingsPage)typeof(PetWindow).GetField("preferences",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);window.SelectCompanionSection(2);Pump();
    var toggle=window.FindControl("DesktopObservationEnabled") as CheckBox;if(toggle==null || toggle.IsChecked!=false)throw new Exception("observation toggle exists and defaults off");
    var details=(Expander)window.FindControl("DesktopObservationDetails");if(details==null || details.IsExpanded)throw new Exception("technical details must be collapsed by default");details.IsExpanded=true;Pump();
    if(window.FindControl("DesktopObservationPreview")==null || window.FindControl("DesktopObservationUploadTest")==null || ((Button)window.FindControl("DesktopObservationUploadTest")).IsEnabled)throw new Exception("local preview exists and upload test stays disabled while off");
    if(settings.DesktopObservationEnabled || settings.DesktopObservationConsentVersion!=0)throw new Exception("opening settings cannot enable observation or grant consent");
    var captureMode=window.FindControl("DesktopObservationScope") as ComboBox;
    if(captureMode==null || captureMode.SelectedIndex!=0 || captureMode.Items.Count!=2)throw new Exception("capture mode dropdown must have two choices and default to application");
    var exclusionPanel=(FrameworkElement)window.FindControl("DesktopObservationExclusions");if(exclusionPanel==null || !exclusionPanel.IsEnabled)throw new Exception("application mode permits exclusion editing");
    captureMode.SelectedIndex=1;PumpUntil(()=>captureMode.IsEnabled);Pump();if(exclusionPanel.IsEnabled || ((Button)window.FindControl("DesktopObservationAddApp")).IsEnabled)throw new Exception("full screen mode disables exclusion list and actions");
    if(pet.Store.Load().GetType().GetProperty("DesktopObservationCaptureScope").GetValue(pet.Store.Load(),null).ToString()!="FullScreen")throw new Exception("mode selection must persist");
    captureMode.SelectedIndex=0;PumpUntil(()=>captureMode.IsEnabled);Pump();if(!exclusionPanel.IsEnabled)throw new Exception("switching back restores exclusion editing");
    var pending=(System.Collections.Generic.List<string>)typeof(SettingsPage).GetField("observationExcludedDraft",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
    typeof(SettingsPage).GetMethod("PauseForExclusionEdit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);pending.Add("C:\\draft-private.exe");
    captureMode.SelectedIndex=1;PumpUntil(()=>captureMode.IsEnabled);Pump();if(pet.ObservationPaused || !pending.Contains("C:\\draft-private.exe"))throw new Exception("full screen must ignore unsaved exclusions without losing the draft or staying paused");
    pet.SetObservationPaused(true);captureMode.SelectedIndex=0;PumpUntil(()=>captureMode.IsEnabled);captureMode.SelectedIndex=1;PumpUntil(()=>captureMode.IsEnabled);Pump();if(!pet.ObservationPaused)throw new Exception("switching modes must preserve a manual pause after full screen ignored a draft");pet.SetObservationPaused(false);
    captureMode.SelectedIndex=0;PumpUntil(()=>captureMode.IsEnabled);Pump();if(!pet.ObservationPaused)throw new Exception("application mode must pause until pending exclusions are saved");
    ((Button)window.FindControl("DesktopObservationSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>captureMode.IsEnabled);Pump();if((pet.Settings.DesktopObservationEnabled && pet.ObservationPaused) || !pet.Store.Load().DesktopObservationExcludedApps.Contains("C:\\draft-private.exe"))throw new Exception("saving retained exclusions resumes application mode");
    var pendingList=(ListBox)window.FindControl("DesktopObservationExcludedApps");pendingList.Items.Refresh();pendingList.SelectedItem="C:\\draft-private.exe";((Button)window.FindControl("DesktopObservationRemoveApp")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));((Button)window.FindControl("DesktopObservationSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>captureMode.IsEnabled);Pump();
    var excluded=(System.Collections.Generic.List<string>)typeof(SettingsPage).GetField("observationExcludedDraft",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);excluded.Add("C:\\private-app.exe");
    var originalLanguage=TextResources.CurrentLanguage;TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Pump();if(!excluded.Contains("C:\\private-app.exe") || toggle.IsChecked!=false)throw new Exception("language change preserves observation draft");TextResources.ApplyLanguage(TextResources.PrepareLanguage(originalLanguage));excluded.Clear();Pump();
    settings.Text.BaseUrl="https://long-provider-name-for-observation.example/v1/"+new string('x',90);settings.Text.Model="long-model-"+new string('m',80);settings.Text.ImageSupport=CapabilitySupport.Supported;
    typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
    var route=(TextBlock)window.FindControl("DesktopObservationRoute");if(route.Text!=TextResources.Get("desktopObservation.route.direct"))throw new Exception("direct route status missing");
    var fallback=new TextProviderSettings{Id="fixture",Name="vision",Connection=new ConnectionSettings{BaseUrl="https://vision.example/v1",Model="image"},Models=new System.Collections.Generic.List<string>{"image"}};fallback.ModelCapabilities["image"]=new ModelCapabilitySetting{Images=CapabilitySupport.Supported};fallback.Normalize();settings.TextProviders.Add(fallback);settings.ImageDescriptionModel=new ModelReference{ProviderId="fixture",ModelId="image"};settings.Text.ImageSupport=CapabilitySupport.Unsupported;typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);var destinations=(TextBlock)window.FindControl("DesktopObservationDestinations");if(route.Text!=TextResources.Get("desktopObservation.route.described") || !destinations.Text.Contains("vision.example") || !((TextBlock)window.FindControl("DesktopObservationTextDestination")).Text.Contains("long-provider-name"))throw new Exception("described route must show both recipients");settings.Text.ImageSupport=CapabilitySupport.Supported;typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
    var host=window.HostWindow;var panel=window.FindControl("DesktopObservationGroup");Directory.CreateDirectory(Path.Combine("work","task6b-screens"));
    foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{1000,600}){ThemeManager.Apply(theme);Pump();if(theme=="dark" && Ui.Ink.Color.R<160)throw new Exception("dark theme ink did not update");host.Width=width;host.UpdateLayout();Pump();panel.BringIntoView();Pump();host.UpdateLayout();panel.UpdateLayout();Save(host,Path.Combine("work","task6b-screens",theme+"-"+width+".png"));if((theme=="light" && width==1000)||(theme=="dark" && width==600))Save(host,Path.Combine("work","task6b-screens","full-"+theme+"-"+width+".png"));}
    details.IsExpanded=false;ThemeManager.Apply("light");host.Width=1000;host.UpdateLayout();Pump();panel.BringIntoView();Pump();Save(panel,Path.Combine("work","task6b-screens","application-mode.png"));
    excluded.Add("C:\\Windows\\System32\\notepad.exe");excluded.Add("C:\\Program Files\\Private App\\private.exe");((ListBox)window.FindControl("DesktopObservationExcludedApps")).Items.Refresh();typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);Pump();Save(panel,Path.Combine("work","task6b-screens","application-list.png"));
    captureMode.SelectedIndex=1;PumpUntil(()=>captureMode.IsEnabled);Pump();Save(panel,Path.Combine("work","task6b-screens","full-screen-mode.png"));captureMode.SelectedIndex=0;PumpUntil(()=>captureMode.IsEnabled);excluded.Clear();((ListBox)window.FindControl("DesktopObservationExcludedApps")).Items.Refresh();details.IsExpanded=true;Pump();
    pet.SetObservationPaused(false);settings.DesktopObservationEnabled=true;settings.Text.BaseUrl="http://127.0.0.1:1/v1";settings.Text.Model="vision";settings.Text.ImageSupport=CapabilitySupport.Supported;settings.DesktopObservationConsentVersion=DesktopObservationConsent.Version;settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(settings);pet.Store.Save(settings);typeof(SettingsPage).GetField("observationDraftEnabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,true);typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);if(!((Button)window.FindControl("DesktopObservationUploadTest")).IsEnabled)throw new Exception("confirmed enabled route permits explicit upload test");var statusText=(TextBlock)window.FindControl("DesktopObservationStatus");
    foreach(var statusReason in new[]{ObservationReason.Allowed,ObservationReason.ModelFailed,ObservationReason.NoRoute,ObservationReason.Busy}){
        typeof(PetWindow).GetField("lastObservationReason",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,(int)statusReason);window.RefreshObservation();Pump();
        Save(panel,Path.Combine("work","task6b-screens","status-"+statusReason+".png"));
    }
    typeof(PetWindow).GetField("lastObservationReason",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,-1);window.RefreshObservation();
    typeof(SettingsPage).GetField("observationDraftEnabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,false);pet.Store.BeforeExactSave=()=>{throw new IOException("fixture failure");};
    var save=(Task)typeof(SettingsPage).GetMethod("SaveObservationEnabledAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{false});while(!save.IsCompleted)Pump();save.GetAwaiter().GetResult();if(!pet.ObservationPaused || !settings.DesktopObservationEnabled || !pet.Store.Load().DesktopObservationEnabled || !window.HasUnsavedEdits)throw new Exception("failed disable stays paused and retains unsaved choice");
    ((Button)window.FindControl("DesktopObservationPause")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!pet.ObservationPaused || (string)((Button)window.FindControl("DesktopObservationPause")).Content!=TextResources.Get("desktopObservation.resume"))throw new Exception("resume cannot bypass failed disable and pause label remains accurate");pet.Store.BeforeExactSave=null;
    var entered=new ManualResetEventSlim(false);var release=new ManualResetEventSlim(false);pet.Store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture timeout");};var retry=(Task)typeof(SettingsPage).GetMethod("SaveObservationEnabledAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{false});PumpUntil(()=>entered.IsSet);settings.TextProviders[0].Name="newer-provider-name";pet.SaveSettings();release.Set();PumpUntil(()=>retry.IsCompleted);retry.GetAwaiter().GetResult();pet.Store.BeforeExactSave=null;var reloaded=pet.Store.Load();if(reloaded.DesktopObservationEnabled || reloaded.TextProviders[0].Name!="newer-provider-name")throw new Exception("observation save must retain concurrent provider edit");
    foreach(string popupTheme in new[]{"light","dark"}){
        ThemeManager.Apply(popupTheme);var center=DesktopObservationPopup.ScreenCenter;
        foreach(bool uploadTest in new[]{false,true}){
            var consentSnapshot=pet.ObservationSettingsSnapshot();consentSnapshot.Text.BaseUrl="https://api.example.com/v1";consentSnapshot.Text.Model="vision";consentSnapshot.DesktopObservationCaptureScope=DesktopObservationCaptureScope.FullScreen;
            var consent=new DesktopObservationConsentWindow(consentSnapshot,uploadTest){Owner=host};
            consent.Loaded+=delegate{consent.Dispatcher.BeginInvoke(new Action(delegate{
                Save(consent,Path.Combine("work","task6b-screens","consent-"+(uploadTest?"test-":"enable-")+popupTheme+".png"));
                ((Button)LogicalTreeHelper.FindLogicalNode(consent,uploadTest?"DesktopObservationConsentAccept":"DesktopObservationConsentCancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }),DispatcherPriority.ApplicationIdle);};
            if(consent.ShowDialog()!=uploadTest)throw new Exception("consent dialog must preserve accept and cancel results");
        }
        var countdownText=Ui.Text(TextResources.Format("desktopObservation.preview.countdown",3),14,Ui.Blue);countdownText.VerticalAlignment=VerticalAlignment.Center;countdownText.HorizontalAlignment=HorizontalAlignment.Center;var countdown=new DesktopObservationPopup(TextResources.Get("desktopObservation.settings.title"),countdownText,420,156,center,true);
        countdown.Show();Pump();Save(countdown,Path.Combine("work","task6b-screens","countdown-"+popupTheme+".png"));countdown.Close();
        var unavailable=pet.ShowObservationMessage(TextResources.Get("desktopObservation.preview.empty"),TextResources.Get("desktopObservation.settings.title"));Pump();Save(unavailable,Path.Combine("work","task6b-screens","unavailable-"+popupTheme+".png"));unavailable.Close();
        var image=new Image{Source=new BitmapImage(new Uri(Path.GetFullPath(Path.Combine("work","task6b-screens","application-list.png")))),Stretch=Stretch.Uniform};
        var previewPopup=new DesktopObservationPopup(TextResources.Get("desktopObservation.previewLocal"),image,840,640,center,false);previewPopup.Show();Pump();Save(previewPopup,Path.Combine("work","task6b-screens","preview-"+popupTheme+".png"));previewPopup.Close();
    }
    ThemeManager.Apply("light");host.Width=1000;Pump();var greetingSection=window.FindControl("GuideGreetingSection");greetingSection.BringIntoView();Pump();
    var intervalField=(TextBox)window.FindControl("GreetingIntervalMinutes");var jitterField=(TextBox)window.FindControl("GreetingJitterMinutes");intervalField.Text="60";jitterField.Text="10";
    if(!window.SaveGreetingInterval())throw new Exception("interval and jitter must save");PumpUntil(()=>pet.Store.Load().GreetingIntervalMinutes==60 && pet.Store.Load().GreetingJitterMinutes==10);
    var schedule=(GreetingSchedule)typeof(PetWindow).GetField("greetingSchedule",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
    if(schedule.NextAt<DateTime.UtcNow.AddMinutes(49.9) || schedule.NextAt>DateTime.UtcNow.AddMinutes(70.1))throw new Exception("saved UI values must drive the actual timer");
    jitterField.Text="-1";if(window.SaveGreetingInterval() || pet.Settings.GreetingJitter!=10)throw new Exception("negative jitter must not overwrite the saved interval");jitterField.Text="10";Pump();
    pet.ShowOnboarding();Pump();var guide=(OnboardingWindow)typeof(PetWindow).GetField("guide",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
    typeof(OnboardingWindow).GetField("index",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(guide,Array.FindIndex(OnboardingLessons.All,l=>l.Id=="greeting.interval"));typeof(OnboardingWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(guide,null);Pump();
    var guideNext=(Button)LogicalTreeHelper.FindLogicalNode(guide,"GuideNext");jitterField.Text="-1";Pump();Pump();if(guideNext.IsEnabled)throw new Exception("greeting guide must reject negative jitter");
    intervalField.Text="80";jitterField.Text="10";Pump();Pump();if(!guideNext.IsEnabled)throw new Exception("greeting guide must accept interval with jitter");guideNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>pet.Store.Load().GreetingIntervalMinutes==80 && pet.Store.Load().GreetingJitterMinutes==10);guide.Close();Pump();
    intervalField.Text="60";jitterField.Text="10";window.SaveGreetingInterval();Pump();window.SelectCompanionSection(2);greetingSection.BringIntoView();Pump();
    Save(host,Path.Combine("work","task6b-screens","greeting-copy.png"));
    Console.WriteLine("DesktopObservationUiTests: passed (modes, exclusions, layout and save failures)");return 0;
   }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
  }
  static void Save(FrameworkElement element,string path){if(element.ActualWidth<1 || element.ActualHeight<1)throw new Exception("settings group not rendered");var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(ThemeManager.CardSurface,null,new Rect(0,0,element.ActualWidth,element.ActualHeight));drawing.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));}if(element is Window)bitmap.Render(element);else bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
  static void PumpUntil(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(15);while(!done() && DateTime.UtcNow<until)Pump();if(!done())throw new TimeoutException("fixture timed out");}
  static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 }
}
