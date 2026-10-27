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
using Dafeiyu.Maintenance;

namespace Dafeiyu.Tests
{
    public static class CacheMaintenanceUiTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(20);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("Cache UI");Pump();}task.GetAwaiter().GetResult();Pump();}
        static T Find<T>(DependencyObject root,string name)where T:FrameworkElement{var found=root as T;if(found!=null&&found.Name==name)return found;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=Find<T>(VisualTreeHelper.GetChild(root,i),name);if(child!=null)return child;}return null;}
        static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        [STAThread] public static int Main(string[] args)
        {
            TextResources.ConfigureStartupLanguage(args.Length==0?"zh_CN":args[0]);
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            string root=Path.GetFullPath(Path.Combine("work","cache-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            Check(TextResources.CurrentLanguage==(args.Length==0?"zh_CN":args[0]),"actual WPF startup catalog matches requested locale");
            try{
                var cfg=new PetSettings{UiLanguage=args.Length==0?"zh_CN":args[0],AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,FollowCursor=false,Wander=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();
                var about=new AboutPanel(pet);var window=new Window{Content=about,Width=460,Height=340,Background=ThemeManager.Surface};window.Show();Pump();
                var clean=Find<Button>(about,"ClearCache");var uninstall=Find<Button>(about,"UninstallApplication");
                Check(clean!=null&&clean.IsVisible&&clean.TransformToAncestor(about).Transform(new Point()).X<uninstall.TransformToAncestor(about).Transform(new Point()).X,"secondary cache action immediately left of uninstall");
                foreach(var button in new[]{clean,uninstall}){var bounds=button.TransformToAncestor(about).TransformBounds(new Rect(button.RenderSize));Check(bounds.Left>=0&&bounds.Right<=about.ActualWidth&&bounds.Top>=0&&bounds.Bottom<=about.ActualHeight,"footer visible at narrow size "+button.Name);}
                // Exercise the actual About click, owned modal, and close path.
                var close=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};bool owned=false;close.Tick+=delegate{var dialog=app.Windows.OfType<CacheMaintenanceWindow>().FirstOrDefault();if(dialog!=null){owned=dialog.Owner==window;close.Stop();dialog.Close();}};close.Start();Click(clean);Check(owned&&pet.IsVisible,"about opens owned dialog; close leaves pet alive");
                string path=Path.Combine(root,"updates",new string('a',32));using(CacheResourceLease.Acquire(root,path))File.WriteAllBytes(Path.Combine(path,"package.zip"),new byte[123]);File.SetLastWriteTimeUtc(Path.Combine(path,"package.zip"),DateTime.UtcNow.AddDays(-8));Directory.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-8));
                var cache=new CacheMaintenanceWindow(root){Owner=window};cache.Show();Wait(cache.PendingOperation);
                Check(cache.Scan.TotalBytes==123&&Find<Button>(cache,"CleanCache").IsEnabled,"async scan renders actual cleanable bytes");
                Check(Find<TextBlock>(cache,"CacheSummary").Text==CachePresentation.FormatBytes(123)&&Find<TextBlock>(cache,"CacheUpdates").Text==CachePresentation.FormatBytes(123)&&Find<TextBlock>(cache,"CacheDownloads").Text==CachePresentation.FormatBytes(0),"visible cache total and categories reflect actual scan fields");
                CaptureStates(cache,root,"nonzero");
                Click(Find<Button>(cache,"CleanCache"));Wait(cache.PendingOperation);Check(cache.LastResult.FreedBytes==123&&cache.Scan.TotalBytes==0&&!Find<Button>(cache,"CleanCache").IsEnabled,"clean reports actual bytes then rescans and disables zero");
                CaptureStates(cache,root,"result");Click(Find<Button>(cache,"CancelCache"));Check(!cache.IsVisible&&pet.IsVisible,"cancel keeps pet alive");
                var escape=new CacheMaintenanceWindow(root){Owner=window};escape.Show();Wait(escape.PendingOperation);escape.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(escape),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});Check(!escape.IsVisible&&pet.IsVisible,"Esc keeps pet alive");
                foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{460,760}){
                    ThemeManager.Apply(theme);window.Width=width;window.Height=width==460?340:560;Pump();
                    foreach(var button in new[]{clean,uninstall}){var bounds=button.TransformToAncestor(about).TransformBounds(new Rect(button.RenderSize));Check(bounds.Left>=0&&bounds.Right<=about.ActualWidth&&bounds.Top>=0&&bounds.Bottom<=about.ActualHeight,"footer bounds "+theme+" "+width+" "+button.Name);}
                    Capture(window,Path.Combine(root,"about-"+theme+"-"+width+".png"));
                    var themed=new CacheMaintenanceWindow(root){Owner=window};themed.Show();Wait(themed.PendingOperation);Capture(themed,Path.Combine(root,"cache-"+theme+"-"+width+".png"));themed.Close();
                }
                var zero=new CacheMaintenanceWindow(root){Owner=window};zero.Show();Wait(zero.PendingOperation);CaptureStates(zero,root,"zero");zero.Close();
                string failedRoot=Path.Combine(root,"scan-file");using(var link=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe","/c mklink /J \""+failedRoot+"\" \""+root+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){link.WaitForExit();if(link.ExitCode!=0)throw new IOException("Owned junction fixture failed: "+link.StandardError.ReadToEnd());}var failed=new CacheMaintenanceWindow(failedRoot){Owner=window};failed.Show();Wait(failed.PendingOperation);Check(failed.Scan.FailedItems==1&&!Find<Button>(failed,"CleanCache").IsEnabled&&Find<TextBlock>(failed,"CacheProtectedCount").Text==TextResources.Format("cache.protected",0,1),"failed scan is explicit and cannot enable cleanup");CaptureStates(failed,root,"scan-failure");failed.Close();Directory.Delete(failedRoot);
                var closing=new CacheMaintenanceWindow(root){Owner=window};closing.Show();closing.Close();Wait(closing.PendingOperation);Check(!closing.IsVisible,"async scan never reopens a closed modal");
                window.Close();about.Dispose();Console.WriteLine("CacheMaintenanceUiTests: "+checks+" passed; captures="+root);return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}finally{if(pet!=null)pet.Close();app.Shutdown();}
        }
        static void CaptureStates(Window target,string root,string scene){foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{380,500}){ThemeManager.Apply(theme);target.Width=width;target.Height=500;Pump();Capture(target,Path.Combine(root,scene+"-"+theme+"-"+width+".png"));}}
        static void Capture(FrameworkElement target,string path){target.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth),(int)Math.Ceiling(target.ActualHeight),96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var draw=background.RenderOpen())draw.DrawRectangle(ThemeManager.Surface,null,new Rect(0,0,target.ActualWidth,target.ActualHeight));bitmap.Render(background);bitmap.Render(target);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
    }
}
