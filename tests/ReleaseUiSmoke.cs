using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ReleaseUiSmoke
    {
        static int passed,failed;
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs args){Console.WriteLine("Dispatcher failure: "+args.Exception.GetType().FullName+": "+args.Exception.Message);Console.WriteLine(args.Exception.StackTrace);};
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"release-ui");Directory.CreateDirectory(folder);
            // Keep unrelated greeting/about checks independent of the desktop's
            // foreground window (including Windows lock screen).
            var pet=new PetWindow(new SpriteAtlas(),new PetSettings{ReadReplies=false,FollowCursor=false,HideOnFullscreen=false},new SettingsStore(Path.Combine(folder,"settings.json")),null);
            try
            {
                pet.Show();pet.ShowSettings();Pump();var settings=(SettingsPage)typeof(PetWindow).GetField("preferences",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);
                settings.SelectCompanionSection(1);Pump();
                Check(Find<CheckBox>(settings,"AutoStart")!=null,"settings expose the autostart switch");Save(settings.HostWindow,Path.Combine(folder,"companion.png"));
                settings.SelectCompanionSection(2);Pump();
                var hello=Find<CheckBox>(settings,"GreetingEnabled");Check(hello!=null && hello.IsChecked==true,"proactive greeting switch is enabled by default");
                var minimum=Find<TextBox>(settings,"GreetingMinMinutes");var maximum=Find<TextBox>(settings,"GreetingMaxMinutes");Check(minimum.Text=="30" && maximum.Text=="60","greeting interval defaults are visible");
                minimum.Text="10";maximum.Text="20";maximum.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Check(pet.Store.Load().GreetingMinMinutes==10 && pet.Store.Load().GreetingMaxMinutes==20,"edited greeting interval persists immediately");
                minimum.Text="50";maximum.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Check(pet.Store.Load().GreetingMinMinutes==10,"invalid greeting interval does not overwrite saved settings");minimum.Text="30";maximum.Text="60";maximum.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                hello.IsChecked=false;Check(!pet.Store.Load().GreetingEnabled,"greetings can be turned off");hello.IsChecked=true;
                Find<Button>(settings,"PreviewGreeting").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();var bubble=app.Windows.OfType<GreetingBubbleWindow>().Single();Check(bubble.IsVisible && !bubble.ShowActivated && settings.HostWindow.IsActive,"greeting preview appears without stealing focus");Save(bubble,Path.Combine(folder,"greeting.png"));
                settings.HostWindow.Hide();
                // Exercise the production automatic path with a controlled clock;
                // preview alone cannot detect a timer repeatedly starved by walks.
                pet.Settings.Wander=false;pet.ApplySettings();pet.PlayAction("idle",0);Pump();bubble.Hide();
                var scheduleField=typeof(PetWindow).GetField("greetingSchedule",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var probe=typeof(PetWindow).GetMethod("ProbeGreetings",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var walkingField=typeof(PetWindow).GetField("walking",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var start=DateTime.UtcNow;var schedule=new GreetingSchedule(start,1,1,()=>0);scheduleField.SetValue(pet,schedule);
                pet.PlayInteraction("petting");Check(Object.ReferenceEquals(scheduleField.GetValue(pet),schedule),"a short interaction does not restart the greeting interval");
                pet.PlayAction("idle",0);walkingField.SetValue(pet,true);probe.Invoke(pet,new object[]{start.AddMinutes(1),false});
                Check(!bubble.IsVisible,"automatic greeting waits while walking");walkingField.SetValue(pet,false);
                probe.Invoke(pet,new object[]{start.AddMinutes(1).AddSeconds(5),false});
                Check(bubble.IsVisible,"due automatic greeting appears after walking ends");bubble.Hide();pet.PlayAction("idle",0);
                probe.Invoke(pet,new object[]{start.AddMinutes(1).AddSeconds(6),false});Check(!bubble.IsVisible,"automatic greeting is not repeated on the next probe");
                start=DateTime.UtcNow;schedule=new GreetingSchedule(start,1,1,()=>0);scheduleField.SetValue(pet,schedule);
                pet.ShowSettings();Pump();settings=(SettingsPage)typeof(PetWindow).GetField("preferences",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);
                Check(Object.ReferenceEquals(scheduleField.GetValue(pet),schedule),"opening settings preserves the pending greeting interval");
                probe.Invoke(pet,new object[]{start.AddMinutes(1),false});Check(!bubble.IsVisible,"visible settings defer the due greeting");
                settings.HostWindow.WindowState=WindowState.Minimized;Pump();probe.Invoke(pet,new object[]{start.AddMinutes(1).AddSeconds(5),false});
                Check(bubble.IsVisible,"minimized settings no longer suppress greetings indefinitely");settings.HostWindow.Hide();pet.ConfigureGreetings();pet.PreviewGreeting();Pump();
                var fullscreen=new Window{Title="DeskPet fullscreen test",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Background=Brushes.White};
                // Maximized WPF windows may stop at the work area, leaving the taskbar uncovered.
                try{fullscreen.Show();Pump();var handle=new System.Windows.Interop.WindowInteropHelper(fullscreen).Handle;var bounds=System.Windows.Forms.Screen.FromHandle(handle).Bounds;SetWindowPos(handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0);fullscreen.Activate();Pump(1.3);var actual=DesktopInterop.Bounds(handle);Check(actual.Left<=bounds.Left && actual.Top<=bounds.Top && actual.Right>=bounds.Right && actual.Bottom>=bounds.Bottom,"fullscreen fixture covers its monitor");if(GetForegroundWindow()==handle){pet.Settings.HideOnFullscreen=true;pet.ApplySettings();Pump(.3);Check(bubble.IsVisible && !pet.AutoHidden,"own fullscreen window does not hide companion or greeting");}else{Console.WriteLine("SKIP own-foreground fullscreen policy: Windows foreground belongs to another process.");}}finally{pet.Settings.HideOnFullscreen=false;pet.ApplySettings();fullscreen.Close();}
                pet.PreviewGreeting();Pump();
                Find<Button>(bubble,"GreetingQuickChat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(!bubble.IsVisible && app.Windows.OfType<QuickChatWindow>().Any(w=>w.IsVisible),"greeting opens quick chat when clicked");foreach(var quick in app.Windows.OfType<QuickChatWindow>().ToArray())quick.Close();
                pet.ShowSettings();Pump();settings=(SettingsPage)typeof(PetWindow).GetField("preferences",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);settings.SelectPage(6);Pump();var repository=Find<TextBlock>(settings,"RepositoryUrl");
                Check(repository!=null && repository.Inlines.OfType<Hyperlink>().Any(link=>link.NavigateUri.AbsoluteUri=="https://github.com/Eason4869/deskpet"),"repository is a fixed clickable hyperlink");
                Check(Find<TextBox>(settings,"RepositoryUrl")==null && Find<Button>(settings,"SaveRepository")==null,"repository has no edit or save controls");
                Check(Find<Button>(settings,"CheckUpdates").IsEnabled && Find<Button>(settings,"DownloadUpdate")==null,"checking updates starts the automatic download and apply flow");
                Check(Find<CheckBox>(settings,"AutoUpdate").IsChecked==true,"startup automatic updates are enabled and configurable");
                var channels=Find<ComboBox>(settings,"UpdateChannel");Check(channels!=null && channels.Items.Count==2,"about exposes stable and preview channels");
                channels.SelectedIndex=1;Check(pet.Store.Load().UpdateChannel==UpdateChannel.Preview && !pet.Updates.IsBusy,"selecting preview saves the preference without starting a download");
                channels.SelectedIndex=0;Check(pet.Store.Load().UpdateChannel==UpdateChannel.Stable,"switching back to stable persists the selection");
                Check(All<TextBlock>(settings).Any(item=>item.Text.Contains("彧晟Eason")),"author attribution is present");
                var network=Find<Expander>(settings,"UpdateNetwork");Check(network!=null && (string)network.Header=="网络加速" && !network.IsExpanded,"network acceleration is collapsed by default");
                Check(Find<Button>(settings,"CopyRepository")==null,"clickable repository has no redundant copy button");
                Save(settings.HostWindow,Path.Combine(folder,"about-collapsed.png"));network.IsExpanded=true;Pump();
                Find<RadioButton>(settings,"UpdateProxy").IsChecked=true;Pump();
                var updateProxy=Find<TextBox>(settings,"UpdateProxyAddress");Check(updateProxy!=null && updateProxy.IsEnabled && updateProxy.Text=="http://127.0.0.1:7890","update proxy is editable and displays the default HTTP address");
                updateProxy.Text="localhost:17894";updateProxy.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Check(pet.Store.Load().UpdateProxyAddress=="http://localhost:17894","edited update proxy persists through the settings UI");
                updateProxy.Text="socks5://localhost:17894";updateProxy.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Check(pet.Store.Load().UpdateProxyAddress=="http://localhost:17894" && Find<TextBlock>(settings,"UpdateNetworkNotice").Text.Contains("HTTP 代理"),"invalid update proxy shows a clear error without overwriting the saved address");
                updateProxy.Text=ProxyConfiguration.DefaultAddress;updateProxy.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Save(settings.HostWindow,Path.Combine(folder,"about.png"));
                Console.WriteLine("Release UI: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
            }
            catch(Exception error){Console.WriteLine("Release UI failure: "+error.GetType().FullName+": "+error.Message);Console.WriteLine(error.StackTrace);return 1;}
            finally{pet.Close();app.Shutdown();}
        }
        static void Check(bool value,string label){if(value){passed++;Console.WriteLine("PASS "+label);}else{failed++;Console.WriteLine("FAIL "+label);}}
        static T Find<T>(DependencyObject root,string name)where T:FrameworkElement{return All<T>(root).FirstOrDefault(item=>item.Name==name);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Pump(double seconds=.2){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Save(FrameworkElement target,string path){var image=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))png.Save(file);}
    }
}
