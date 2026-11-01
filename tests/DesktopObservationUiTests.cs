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
    if(window.FindControl("DesktopObservationPreview")==null || window.FindControl("DesktopObservationUploadTest")==null || ((Button)window.FindControl("DesktopObservationUploadTest")).IsEnabled)throw new Exception("local preview exists and upload test stays disabled while off");
    if(settings.DesktopObservationEnabled || settings.DesktopObservationConsentVersion!=0)throw new Exception("opening settings cannot enable observation or grant consent");
    var excluded=(System.Collections.Generic.List<string>)typeof(SettingsPage).GetField("observationExcludedDraft",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);excluded.Add("C:\\private-app.exe");
    var originalLanguage=TextResources.CurrentLanguage;TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Pump();if(!excluded.Contains("C:\\private-app.exe") || toggle.IsChecked!=false)throw new Exception("language change preserves observation draft");TextResources.ApplyLanguage(TextResources.PrepareLanguage(originalLanguage));excluded.Clear();Pump();
    settings.Text.BaseUrl="https://long-provider-name-for-observation.example/v1/"+new string('x',90);settings.Text.Model="long-model-"+new string('m',80);settings.Text.ImageSupport=CapabilitySupport.Supported;
    typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
    var route=(TextBlock)window.FindControl("DesktopObservationRoute");if(route.Text!=TextResources.Get("desktopObservation.routeDirect"))throw new Exception("direct route status missing");
    var fallback=new TextProviderSettings{Id="fixture",Name="vision",Connection=new ConnectionSettings{BaseUrl="https://vision.example/v1",Model="image"},Models=new System.Collections.Generic.List<string>{"image"}};fallback.ModelCapabilities["image"]=new ModelCapabilitySetting{Images=CapabilitySupport.Supported};fallback.Normalize();settings.TextProviders.Add(fallback);settings.ImageDescriptionModel=new ModelReference{ProviderId="fixture",ModelId="image"};settings.Text.ImageSupport=CapabilitySupport.Unsupported;typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);var destinations=(TextBlock)window.FindControl("DesktopObservationDestinations");if(route.Text!=TextResources.Get("desktopObservation.routeDescribed") || !destinations.Text.Contains("vision.example") || !((TextBlock)window.FindControl("DesktopObservationTextDestination")).Text.Contains("long-provider-name"))throw new Exception("described route must show both recipients");settings.Text.ImageSupport=CapabilitySupport.Supported;typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
    var host=window.HostWindow;var panel=window.FindControl("DesktopObservationGroup");Directory.CreateDirectory(Path.Combine("work","task6b-screens"));
    foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{1000,600}){ThemeManager.Apply(theme);Pump();if(theme=="dark" && Ui.Ink.Color.R<160)throw new Exception("dark theme ink did not update");host.Width=width;host.UpdateLayout();Pump();panel.BringIntoView();Pump();host.UpdateLayout();panel.UpdateLayout();Save(host,Path.Combine("work","task6b-screens",theme+"-"+width+".png"));if((theme=="light" && width==1000)||(theme=="dark" && width==600))Save(host,Path.Combine("work","task6b-screens","full-"+theme+"-"+width+".png"));}
    settings.DesktopObservationEnabled=true;settings.Text.BaseUrl="http://127.0.0.1:1/v1";settings.Text.Model="vision";settings.Text.ImageSupport=CapabilitySupport.Supported;settings.DesktopObservationConsentVersion=DesktopObservationConsent.Version;settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(settings);pet.Store.Save(settings);typeof(SettingsPage).GetField("observationDraftEnabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,true);typeof(SettingsPage).GetMethod("RefreshObservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);if(!((Button)window.FindControl("DesktopObservationUploadTest")).IsEnabled)throw new Exception("confirmed enabled route permits explicit upload test");typeof(SettingsPage).GetField("observationDraftEnabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,false);pet.Store.BeforeExactSave=()=>{throw new IOException("fixture failure");};
    var save=(Task)typeof(SettingsPage).GetMethod("SaveObservationEnabledAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{false});while(!save.IsCompleted)Pump();save.GetAwaiter().GetResult();if(!pet.ObservationPaused || !settings.DesktopObservationEnabled || !pet.Store.Load().DesktopObservationEnabled || !window.HasUnsavedEdits)throw new Exception("failed disable stays paused and retains unsaved choice");
    ((Button)window.FindControl("DesktopObservationPause")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!pet.ObservationPaused || (string)((Button)window.FindControl("DesktopObservationPause")).Content!=TextResources.Get("desktopObservation.resume"))throw new Exception("resume cannot bypass failed disable and pause label remains accurate");pet.Store.BeforeExactSave=null;
    var entered=new ManualResetEventSlim(false);var release=new ManualResetEventSlim(false);pet.Store.BeforeExactSave=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture timeout");};var retry=(Task)typeof(SettingsPage).GetMethod("SaveObservationEnabledAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{false});PumpUntil(()=>entered.IsSet);settings.TextProviders[0].Name="newer-provider-name";pet.SaveSettings();release.Set();PumpUntil(()=>retry.IsCompleted);retry.GetAwaiter().GetResult();pet.Store.BeforeExactSave=null;var reloaded=pet.Store.Load();if(reloaded.DesktopObservationEnabled || reloaded.TextProviders[0].Name!="newer-provider-name")throw new Exception("observation save must retain concurrent provider edit");
    Console.WriteLine("DesktopObservationUiTests: 10 assertions passed");return 0;
   }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
  }
  static void Save(FrameworkElement element,string path){if(element.ActualWidth<1 || element.ActualHeight<1)throw new Exception("settings group not rendered");var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(ThemeManager.CardSurface,null,new Rect(0,0,element.ActualWidth,element.ActualHeight));drawing.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));}if(element is Window)bitmap.Render(element);else bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
  static void PumpUntil(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(15);while(!done() && DateTime.UtcNow<until)Pump();if(!done())throw new TimeoutException("fixture timed out");}
  static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 }
}
