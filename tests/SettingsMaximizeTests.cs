using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class SettingsMaximizeTests
    {
        [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int L,T,R,B;}
        [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
        [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out NativeRect r);
        [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref NativePoint p);
        static int passed,failed;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static ContentControl Host(DependencyObject parent){if(parent is ContentControl&&((FrameworkElement)parent).Name=="SettingsContentHost")return(ContentControl)parent;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var result=Host(VisualTreeHelper.GetChild(parent,i));if(result!=null)return result;}return null;}
        static void Bounds(SettingsWindow settings){var handle=new System.Windows.Interop.WindowInteropHelper(settings).Handle;var bounds=DesktopInterop.Bounds(handle);var work=DesktopInterop.WorkArea(handle);Check(bounds.Left<=work.Left+1&&bounds.Right>=work.Right-1,"right edge does not fill the monitor work area");NativeRect client;GetClientRect(handle,out client);var bottom=new NativePoint{X=client.R,Y=client.B};ClientToScreen(handle,ref bottom);Check(bottom.Y<=work.Bottom+1,"client content extends below the taskbar work area: "+bottom.Y+" > "+work.Bottom);}
        [STAThread]public static int Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed"};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";var root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-max-"+Guid.NewGuid().ToString("N"));var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);var settings=new SettingsWindow(pet);try{
            settings.Show();Pump();Run("settings has no permanent width cap",delegate{Check(Double.IsPositiveInfinity(settings.MaxWidth),"settings retains a primary-monitor MaxWidth cap");});
            settings.WindowState=WindowState.Maximized;Pump();Run("settings fills width and leaves taskbar unobscured",delegate{Bounds(settings);});
            Run("every settings page fits and stretches to its content host",delegate{for(int page=0;page<5;page++){if(page==0)settings.SelectCompanionSection(0);else if(page==1)settings.SelectAiSection(0);else if(page==2)settings.SelectWorkSettings();else settings.SelectPage(page==3?7:6);Pump();var host=Host(settings);Check(host!=null&&host.HorizontalContentAlignment==HorizontalAlignment.Stretch&&host.VerticalContentAlignment==VerticalAlignment.Stretch,"settings content host does not stretch");var child=host.Content as FrameworkElement;Check(child!=null,"page content missing");Check(Math.Abs(child.ActualWidth+child.Margin.Left+child.Margin.Right-host.ActualWidth)<=2&&child.ActualHeight+child.Margin.Top+child.Margin.Bottom<=host.ActualHeight+2,"page "+page+" bounds "+child.ActualWidth+"x"+child.ActualHeight+" host "+host.ActualWidth+"x"+host.ActualHeight+" margin "+child.Margin);Bounds(settings);}});
            settings.WindowState=WindowState.Normal;Pump();Run("restore keeps the original normal size",delegate{Check(settings.ActualWidth<DesktopInterop.WorkArea(new System.Windows.Interop.WindowInteropHelper(settings).Handle).Width,"restored settings stays screen-wide");});settings.Close();
            settings=new SettingsWindow(pet){WindowState=WindowState.Maximized};settings.Show();Pump();Run("opening already maximized is not repositioned beside the pet",delegate{Bounds(settings);});
        }finally{settings.Close();pet.Close();app.Shutdown();}Console.WriteLine("Settings maximize tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;}
    }
}
