using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class LanSettingsUiTests
    {
        static int checks;
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static T Control<T>(DependencyObject root,string name)where T:FrameworkElement{return All<T>(root).Single(c=>c.Name==name);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
        static void WaitSave(PetWindow pet){var saving=(System.Threading.Tasks.Task<bool>)typeof(PetWindow).GetField("explicitSettingsSave",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);if(saving!=null){var deadline=DateTime.UtcNow.AddSeconds(15);while(!saving.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("LAN settings save");Pump();System.Threading.Thread.Sleep(1);}saving.GetAwaiter().GetResult();}Pump();}
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static void SaveImage(FrameworkElement page,string path){page.UpdateLayout();var bmp=new RenderTargetBitmap((int)page.ActualWidth,(int)page.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))encoder.Save(file);}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;string root=Path.Combine(Directory.GetCurrentDirectory(),"work","lan-settings-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                var settings=new PetSettings{AgentEnabled=false,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed"};settings.Text.BaseUrl="https://relay.example/v1";settings.Text.Model="lan-model";foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);var panel=new ConnectionSettingsPanel(pet);host=new Window{Content=panel};Ui.Window(host,"局域网服务配置验收",1000,850);host.Show();panel.ShowSection(0);panel.ExpandTextConnection();Pump();
                var url=Control<TextBox>(panel,"TextServiceUrl");var secret=Control<PasswordBox>(panel,"TextServiceKey");url.Text="http://192.168.1.50:3000/relay/v1";secret.Password="";Control<Button>(panel,"SaveConnections").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitSave(pet);
                var saved=pet.Store.Load();Check(saved.Text.BaseUrl==url.Text&&saved.Text.ApiKey==""&&Control<TextBlock>(panel,"ConnectionNotice").Text.Contains("已保存"),"visible configuration saves a LAN relay without a key");
                secret.Password="lan-ui-test-key";Control<Button>(panel,"SaveConnections").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitSave(pet);Check(pet.Store.Load().Text.ApiKey=="lan-ui-test-key"&&!File.ReadAllText(Path.Combine(root,"settings.json")).Contains("lan-ui-test-key"),"LAN relay key remains encrypted on disk");
                ThemeManager.Apply("dark");Pump();SaveImage(panel,Path.Combine(root,"lan-settings-dark.png"));ThemeManager.Apply("light");Pump();SaveImage(panel,Path.Combine(root,"lan-settings-light.png"));
                host.Content=null;panel=new ConnectionSettingsPanel(pet);host.Content=panel;panel.ShowSection(0);panel.ExpandTextConnection();Pump();url=Control<TextBox>(panel,"TextServiceUrl");Check(url.Text=="http://192.168.1.50:3000/relay/v1"&&Control<PasswordBox>(panel,"TextServiceKey").Password=="lan-ui-test-key","reopening restores the visible LAN endpoint and key");
                url.Text="http://public.example/v1";Control<Button>(panel,"SaveConnections").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitSave(pet);Check(pet.Store.Load().Text.BaseUrl=="http://192.168.1.50:3000/relay/v1"&&Control<TextBlock>(panel,"ConnectionNotice").Text.Contains("局域网"),"public HTTP failure is actionable and preserves the saved LAN configuration");
                Console.WriteLine("LAN settings UI tests: "+checks+" passed. Evidence: "+root);return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
