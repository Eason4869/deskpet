using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class DesktopObservationToggleTests
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);Thread.Sleep(5);}
        static void Until(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>until)throw new TimeoutException();Pump();}}
        static SettingsPage Page(PetWindow pet){return (SettingsPage)typeof(PetWindow).GetField("preferences",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);}
        [STAThread]public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());PetWindow pet=null;
            try{
                bool restarted=args.Length==2 && args[0]=="--verify-restart";
                string path=restarted?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"toggle-save-"+Guid.NewGuid().ToString("N"),"settings.json");
                var store=new SettingsStore(path);var settings=restarted?store.Load():new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,FollowCursor=false,Wander=false,OnboardingState="completed",GreetingEnabled=true,AiGreetingEnabled=true};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,store,null);pet.Show();pet.ShowSettings();Pump();var page=Page(pet);page.SelectCompanionSection(2);Pump();var toggle=(CheckBox)page.FindControl("DesktopObservationEnabled");var group=(FrameworkElement)page.FindControl("DesktopObservationGroup");
                if(restarted){
                    Check(settings.DesktopObservationEnabled && toggle.IsChecked==true,"fresh process restores the enabled switch without a configured model");
                    Check(settings.DesktopObservationCaptureScope==DesktopObservationCaptureScope.FullScreen,"fresh process restores the selected scope");
                    toggle.IsChecked=false;Until(()=>group.IsEnabled);Check(!store.Load().DesktopObservationEnabled,"disabling the restored switch persists");
                    Console.WriteLine("Restart checks: "+checks+" passed");return 0;
                }
                Check(ImageModelResolver.Resolve(settings,null).Route==ImageRouteKind.Unavailable,"fixture has no usable image model");
                toggle.IsChecked=true;Until(()=>group.IsEnabled);
                Check(settings.DesktopObservationEnabled && store.Load().DesktopObservationEnabled,"enabling without a model must persist instead of changing only appearance");
                Check(!DesktopObservationConsent.IsConfirmed(settings),"saving enablement does not grant upload consent");
                var mode=(ComboBox)page.FindControl("DesktopObservationScope");mode.SelectedIndex=1;Until(()=>group.IsEnabled);
                Check(settings.DesktopObservationCaptureScope==DesktopObservationCaptureScope.FullScreen && store.Load().DesktopObservationCaptureScope==DesktopObservationCaptureScope.FullScreen,"scope remains editable and durable while model setup is pending");
                page.HostWindow.Close();Until(()=>!pet.DialogueBusy);pet.ShowSettings();Pump();page=Page(pet);page.SelectCompanionSection(2);Pump();
                Check(((CheckBox)page.FindControl("DesktopObservationEnabled")).IsChecked==true,"closing and reopening settings retains the enabled switch");
                pet.SaveSettings();Check(store.Load().DesktopObservationEnabled,"ordinary settings save preserves enabled intent");
                pet.Close();Until(()=>!pet.IsVisible);pet=null;
                using(var child=Process.Start(new ProcessStartInfo{FileName=typeof(DesktopObservationToggleTests).Assembly.Location,Arguments="--verify-restart \""+path+"\"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
                    Until(()=>child.HasExited);string output=child.StandardOutput.ReadToEnd();string errors=child.StandardError.ReadToEnd();Console.Write(output);Check(child.ExitCode==0,"restart verification child succeeds: "+errors);
                }
                Check(!store.Load().DesktopObservationEnabled,"disabled state from restarted process remains saved");
                Console.WriteLine("DesktopObservationToggleTests: "+checks+" parent checks passed");return 0;
            }catch(Exception error){Console.WriteLine(error);return 1;}
            finally{if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
    }
}
