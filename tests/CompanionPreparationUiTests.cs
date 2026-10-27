using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class CompanionPreparationUiTests
    {
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
        static int checks;static Application app;static PetWindow pet;static IProgress<OfficialCompanionProgress> reporter;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static T Named<T>(Window window,string name)where T:FrameworkElement{return All<T>(window).Single(x=>x.Name==name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException();Pump();}task.GetAwaiter().GetResult();Pump();}
        static Window Open(){pet.ShowOfficialPreparation();Pump();return app.Windows.OfType<Window>().Single(w=>w.Name=="OfficialPreparationProgress");}
        static void Capture(Window window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);using(var file=File.Create(path)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(file);}}
        [STAThread]public static int Main()
        {
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};string root=Path.GetFullPath(Path.Combine("work","preparation-ui-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);Window window=null;
            try
            {
                var cfg=new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,GreetingEnabled=false,Wander=false,SaveChatHistory=false,OnboardingState="completed"};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);int starts=0;bool complete=false,fail=false;
                pet.PreparationDownloadOverride=(progress,token)=>{starts++;reporter=progress;if(fail)throw new IOException("fixture failure");return complete?Task.FromResult(0):Task.Delay(Timeout.Infinite,token);};
                window=Open();
                Check(window.ResizeMode==ResizeMode.CanMinimize||window.ResizeMode==ResizeMode.NoResize,"bulk preparation dialog disallows maximize");
                Check((GetWindowLong(new WindowInteropHelper(window).Handle,-16)&0x10000)==0,"native window style removes maximize box");
                Check(starts==0,"opening progress view does not start a transfer");
                var start=Named<Button>(window,"StartOfficialPreparation");var cancel=Named<Button>(window,"CancelOfficialPreparation");var bar=Named<ProgressBar>(window,"OfficialPreparationProgressBar");
                Check(start.IsEnabled&&!cancel.IsEnabled,"idle actions match preparation state");
                foreach(string language in new[]{"zh_CN","en_US"})
                {
                    TextResources.ApplyLanguage(TextResources.PrepareLanguage(language));
                    foreach(string theme in new[]{"light","dark"}){ThemeManager.Apply(theme);Pump();Capture(window,Path.Combine(root,language+"-"+theme+"-idle.png"));Check(start.IsVisible&&cancel.IsVisible,"actions remain visible "+language+" "+theme);}
                }
                TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));ThemeManager.Apply("light");
                start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(starts==1&&!start.IsEnabled&&cancel.IsEnabled,"running transfer has stop action and no duplicate start");
                reporter.Report(new OfficialCompanionProgress("e-bao","download","",20*1048576,50*1048576,40));Pump();Check(!bar.IsIndeterminate&&bar.Value==40,"download bytes update determinate progress");Capture(window,Path.Combine(root,"zh_CN-light-running.png"));
                window.Close();window=null;Pump();Check(pet.OfficialPreparation.State==OfficialCompanionPreparationState.Running,"closing view does not cancel background transfer");
                window=Open();var running=pet.OfficialPreparation.StartAsync();cancel=Named<Button>(window,"CancelOfficialPreparation");cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(running);
                Check(pet.OfficialPreparation.State==OfficialCompanionPreparationState.Cancelled,"explicit stop cancels shared preparation");Capture(window,Path.Combine(root,"zh_CN-light-cancelled.png"));
                fail=true;Wait(pet.OfficialPreparation.StartAsync());Check(Named<Button>(window,"StartOfficialPreparation").IsEnabled,"failed preparation offers retry");Capture(window,Path.Combine(root,"zh_CN-light-failed.png"));
                fail=false;complete=true;Wait(pet.OfficialPreparation.StartAsync());Check(Named<ProgressBar>(window,"OfficialPreparationProgressBar").Value==100,"completed preparation fills progress");Capture(window,Path.Combine(root,"zh_CN-light-complete.png"));
                Console.WriteLine("Companion preparation UI: "+checks+" passed; captures="+root);return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
            finally{if(window!=null)window.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
