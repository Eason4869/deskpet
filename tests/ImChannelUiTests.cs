using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    public static class ImChannelUiTests
    {
        static Application app;static PetWindow pet;
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static T Named<T>(DependencyObject root,string name)where T:FrameworkElement{var found=All<T>(root).Where(x=>x.Name==name).ToArray();if(found.Length==1)return found[0];return app.Windows.OfType<Window>().Where(w=>w.Name=="ImConnectionDialog").SelectMany(w=>All<T>(w)).Single(x=>x.Name==name);}
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
        static async Task Run()
        {
            Check(Dafeiyu.ImChannels.ImLoginQr.Png("local-qr-fixture").Length>100,"bundled QR assembly loads without an external DLL");
            TextResources.ConfigureStartupLanguage("zh_CN");string root=Path.GetFullPath(Path.Combine("work","im-ui-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            var cfg=new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,FollowCursor=false,Wander=false,SaveChatHistory=false};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            var store=new SettingsStore(Path.Combine(root,"data","settings.json"));store.Save(cfg);pet=new PetWindow(null,cfg,store,null);pet.ShowSettings();await Task.Delay(150);
            Window window=app.Windows.OfType<ChatWindow>().Single();var page=Named<SettingsPage>(window,"SettingsPage");page.SelectNavigation(5);window.Width=1100;window.Height=850;await Task.Delay(80);
            Check(Named<StackPanel>(page,"ImChannelOverview").IsVisible&&!Named<TextBox>(page,"ImAppId").IsVisible,"channel page starts with a platform overview rather than credentials");
            bool scanCancelled=false;var channelPanel=All<ImChannelSettingsPanel>(page).Single();channelPanel.RegistrationOverride=(profile,show,token)=>{show("https://example.invalid/local-ui-qr-fixture");var waiting=new TaskCompletionSource<Dafeiyu.ImChannels.ImChannelProfile>();token.Register(()=>{scanCancelled=true;waiting.TrySetCanceled();});return waiting.Task;};
            Capture(window,Path.Combine(root,"channels-overview-light.png"));
            Named<Button>(page,"ImPlatformCard_qq").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(40);
            window=app.Windows.OfType<Window>().Single(w=>w.Name=="ImConnectionDialog");Check(window.Owner!=null&&window.ResizeMode==ResizeMode.CanMinimize,"platform row opens an owned connection dialog without maximize");Check(Named<RadioButton>(page,"ImScanMode").IsChecked==true&&!Named<TextBox>(page,"ImAppId").IsVisible,"connection dialog defaults to QR mode");Check(Named<Image>(page,"ImLoginQr").Source!=null&&Named<Image>(page,"ImLoginQr").IsVisible,"scan mode automatically displays a generated QR code");Capture(window,Path.Combine(root,"channels-dialog-scan.png"));Named<RadioButton>(page,"ImManualMode").IsChecked=true;await Task.Delay(40);Check(scanCancelled&&Named<TextBox>(page,"ImAppId").IsVisible,"manual mode cancels QR polling and exposes credentials");window.Width=900;await Task.Delay(40);
            Check(Named<ComboBox>(page,"ImPlatform").Items.Count==5,"five channels available");Check(Named<PasswordBox>(page,"ImSecret")!=null,"secret uses PasswordBox");
            var platform=Named<ComboBox>(page,"ImPlatform");var channelName=Named<TextBox>(page,"ImChannelName");var add=Named<Button>(page,"ImChannelAdd");
            Check(Named<PasswordBox>(page,"ImSecret").TranslatePoint(new Point(),window).X>Named<TextBox>(page,"ImAppId").TranslatePoint(new Point(),window).X,"wide channel form uses two columns");
            Check(add.ActualWidth<250,"add connection button remains compact");
            page.SelectNavigation(0);page.SelectNavigation(5);await Task.Delay(30);Check(Named<ComboBox>(page,"ImPlatform").SelectedItem.ToString()=="qq","returning to channel page preserves stable platform ID");Check(Named<Button>(page,"ImOpenPlatform")!=null,"safe platform browser entry exists");
            foreach(string theme in new[]{"light","dark"}){ThemeManager.Apply(theme);window.Width=900;await Task.Delay(60);Capture(window,Path.Combine(root,"channels-"+theme+".png"));window.Width=540;await Task.Delay(60);Check(Named<Button>(page,"ImSave").ActualWidth>0,"narrow save remains reachable");Check(Math.Abs(Named<PasswordBox>(page,"ImSecret").TranslatePoint(new Point(),window).X-Named<TextBox>(page,"ImAppId").TranslatePoint(new Point(),window).X)<1,"narrow channel form uses one column");Capture(window,Path.Combine(root,"channels-"+theme+"-narrow.png"));}
            var name=Named<TextBox>(page,"ImChannelName");name.Text="unsaved bot";UiLanguageChange.Apply(store,cfg,"en_US");await Task.Delay(40);Check(name.Text=="unsaved bot","live language preserves unsaved channel name");Check(Named<Button>(page,"ImSave").Content.ToString()=="Save","new page refreshes language");ThemeManager.Apply("light");window.Width=1100;await Task.Delay(60);Check(All<TextBlock>(page).Any(t=>t.Text==TextResources.Get("im.title")),"English channel title is visible");Capture(window,Path.Combine(root,"channels-en-light.png"));window.Width=620;await Task.Delay(60);Capture(window,Path.Combine(root,"channels-en-light-narrow.png"));
            Console.WriteLine("ImChannelUiTests passed; captures="+root);
            name.Text=pet.Skin.Name;window.Close();await Task.Delay(50);
            foreach(string kind in Dafeiyu.ImChannels.ImChannelProfile.Kinds){Named<Button>(page,"ImPlatformCard_"+kind).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);var dialog=app.Windows.OfType<Window>().Single(w=>w.Name=="ImConnectionDialog");Check((string)Named<ComboBox>(page,"ImPlatform").SelectedItem==kind&&Named<Image>(page,"ImLoginQr").Source!=null,"platform popup starts its QR flow: "+kind);Named<RadioButton>(page,"ImManualMode").IsChecked=true;await Task.Delay(40);Check(!Named<Expander>(page,"ImAccessOptions").IsExpanded,"ID restrictions stay optional and collapsed: "+kind);dialog.Close();await Task.Delay(40);}
            Check(pet.ImChannels.Profiles.Count==0,"cancelling QR dialogs never stores fake credentials");
        }
        static void Capture(FrameworkElement view,string path){view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
        [STAThread]public static int Main(){int result=1;app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{if(pet!=null)pet.Close();foreach(var window in app.Windows.OfType<Window>().ToArray())if(window!=pet)window.Close();app.Shutdown();}}));app.Run();return result;}
    }
}
