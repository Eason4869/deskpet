using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;

namespace Dafeiyu.Tests
{
    public static class TrayInteractionTests
    {
        static Application app;static PetWindow pet;static TrayContextMenu menu;static int checks;
        static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static object Field(string name){return typeof(PetWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);}
        static MenuItem Item(string name){return menu.Items.OfType<MenuItem>().Single(i=>i.Name==name);}
        static async Task Settle(){await Task.Delay(150);}
        static async Task Invoke(string name){menu.IsOpen=false;Item(name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Settle();}
        static void Capture()
        {
            menu.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(menu);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory(Path.Combine("work","permissions"));using(var file=File.Create(Path.Combine("work","permissions","tray-context-menu.png")))encoder.Save(file);
        }
        static async Task Run()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","tray-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ShowPredictedMessages=false,SaveChatHistory=false,ReadReplies=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();pet.SetPaused(true);await Settle();menu=(TrayContextMenu)Field("trayMenu");var icon=(Forms.NotifyIcon)Field("tray");
            Check(icon.ContextMenuStrip==null&&menu.Template!=null,"notification icon uses the styled WPF tray menu instead of a native menu strip");
            string[] expected={"显示 / 隐藏伙伴","免打扰","鼠标穿透","快速对话","打开完整对话","设置","回到屏幕角落","退出桌宠"};Check(menu.Items.OfType<MenuItem>().Select(i=>(string)i.Header).SequenceEqual(expected)&&menu.Items.OfType<Separator>().Count()==1,"modern tray menu retains all eight original actions and its separator");
            pet.HidePet();await Settle();menu.ShowMenu(new Point(80,80));await Settle();
            Check(menu.IsOpen&&!pet.IsPetVisible&&!Item("TrayVisibility").IsChecked,"tray menu opens independently while the companion remains hidden");
            Check(menu.ActualWidth>=220&&menu.FontFamily.Source=="Microsoft YaHei UI"&&menu.FontSize==12,"tray menu renders at the shared UI font and usable width");
            Check(All<Border>(menu).Any(b=>b.CornerRadius.TopLeft>=10&&b.Background is SolidColorBrush&&((SolidColorBrush)b.Background).Color==Colors.White),"rendered tray popup has a white rounded surface");
            Check(All<Border>(Item("TrayFullChat")).Any(b=>b.CornerRadius.TopLeft>=7)&&Item("TrayFullChat").Icon!=null,"rendered tray actions use rounded rows and matching application icons");var actionRows=menu.Items.OfType<MenuItem>().ToArray();Check(actionRows.All(row=>row.Icon!=null),"all tray actions including the three toggles have complete icons");var starts=actionRows.Select(row=>All<ContentPresenter>(row).Single(p=>p.Content as string==row.Header as string).TranslatePoint(new Point(),menu).X).ToArray();Check(starts.Max()-starts.Min()<1,"all tray action labels share one consistent horizontal starting position");Capture();
            await Invoke("TrayQuietMode");Check(cfg.QuietMode&&Item("TrayQuietMode").IsChecked,"quiet-mode action updates settings and its checked state");
            await Invoke("TrayClickThrough");Check(cfg.ClickThrough&&Item("TrayClickThrough").IsChecked,"click-through action retains its setting behavior");await Invoke("TrayClickThrough");Check(!cfg.ClickThrough&&!Item("TrayClickThrough").IsChecked,"click-through can be disabled again from the same menu");
            await Invoke("TrayVisibility");Check(pet.IsPetVisible&&Item("TrayVisibility").IsChecked,"visibility action restores the companion");
            menu.ShowMenu(new Point(80,80));await Settle();pet.SetGuideLock(true);Check(!menu.Enabled&&!menu.IsOpen,"guide or update disabling closes the tray popup and blocks menu input");pet.ShowTrayMenu();pet.HandleTrayDoubleClick();await Settle();Check(!menu.IsOpen&&!app.Windows.OfType<ChatWindow>().Any(),"disabled tray handlers do not open a menu or a conversation");pet.SetGuideLock(false);
            pet.HidePet();pet.HandleTrayDoubleClick();await Settle();var chat=app.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible);Check(pet.IsPetVisible&&chat.IsVisible&&!app.Windows.OfType<SettingsWindow>().Any(),"tray double-click dispatch opens the full conversation and restores the companion without opening settings");
            chat.WindowState=WindowState.Minimized;pet.HandleTrayDoubleClick();await Settle();Check(app.Windows.OfType<ChatWindow>().Count()==1&&chat.WindowState==WindowState.Normal,"repeated tray double-click restores the existing full conversation");
            await Invoke("TraySettings");var settings=All<SettingsPage>(chat).Single(w=>w.IsVisible);Check(settings.IsVisible&&!app.Windows.OfType<SettingsWindow>().Any(w=>w.IsVisible),"explicit Settings action opens its page in the same main window");chat.ShowHomePage();
            await Invoke("TrayQuickChat");var quick=app.Windows.OfType<QuickChatWindow>().Single(w=>w.IsVisible);Check(quick.IsVisible,"explicit Quick Chat action remains available");quick.Close();
            await Invoke("TrayFullChat");Check(chat.IsVisible&&app.Windows.OfType<ChatWindow>().Count()==1,"explicit Full Chat action also reuses the main conversation");
            await Invoke("TrayReturnHome");IntPtr handle=new WindowInteropHelper(pet).Handle;Check(DesktopInterop.WorkArea(handle).Contains(DesktopInterop.Bounds(handle)),"return-home action keeps the companion inside the work area");
            menu.ShowMenu(new Point(80,80));await Settle();Check(Item("TrayQuietMode").IsChecked&&Item("TrayVisibility").IsChecked&&!Item("TrayClickThrough").IsChecked,"opening the menu refreshes current visibility and preference checkmarks");
            await Invoke("TrayExit");for(int i=0;i<30&&icon.Visible;i++)await Task.Delay(100);Check(!icon.Visible&&!menu.IsOpen&&menu.Items.Count==0,"exit action disposes the tray icon and rounded popup");
            Console.WriteLine("Tray interaction tests: "+checks+" passed; screenshot: work/permissions/tray-context-menu.png");
        }
        [STAThread]public static int Main(){app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{if(menu!=null)menu.Dispose();foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}}));app.Run();return result;}
    }
}
