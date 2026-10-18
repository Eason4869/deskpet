using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class AboutSmoke
    {
        static int passed;static string folder;
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"about-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                pet=new PetWindow(null,new PetSettings{AutoUpdate=false,FollowCursor=false,Wander=false,GreetingEnabled=false,HideOnFullscreen=false},new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();pet.ShowSettings();Pump();var settings=app.Windows.OfType<SettingsWindow>().Single();settings.SelectPage(6);Pump();
                var network=Find<Expander>(settings,"UpdateNetwork");Check((string)network.Header=="网络加速" && !network.IsExpanded,"network acceleration stays collapsed by default");
                Check(Find<Button>(settings,"UninstallApplication")!=null && UpdateInstall.GetUninstallerPath(AppDomain.CurrentDomain.BaseDirectory)==null,"uninstall entry exists and portable test directory has no registered uninstaller");Save(settings,"about-collapsed.png");
                network.IsExpanded=true;Pump();var direct=Find<RadioButton>(settings,"UpdateDirect");var mirror=Find<RadioButton>(settings,"UpdateMirror");var proxy=Find<RadioButton>(settings,"UpdateProxy");
                Check(direct.IsChecked==true && mirror.IsChecked==false && proxy.IsChecked==false,"three exclusive network choices default to direct");
                mirror.IsChecked=true;Pump();Check(!pet.Store.Load().UpdateUseMirror && Find<Button>(settings,"TestUpdateMirror").IsVisible && !Find<RadioButton>(settings,"DefaultUpdateMirror").IsEnabled,"untested mirrors cannot be selected or silently enabled");
                var ordered=new[]{Find<RadioButton>(settings,"DefaultUpdateMirror"),Find<RadioButton>(settings,"BuiltinUpdateMirror1"),Find<RadioButton>(settings,"BuiltinUpdateMirror2"),Find<RadioButton>(settings,"BuiltinUpdateMirror3")}.OrderBy(row=>row.TransformToAncestor(settings).Transform(new Point()).Y).Select(row=>((string)row.Tag).Length).ToArray();Check(ordered.SequenceEqual(ordered.OrderBy(n=>n)),"built-in mirror rows are ordered by address length");
                TestMirrors(settings);Check(!pet.Store.Load().UpdateUseMirror && Find<RadioButton>(settings,"BuiltinUpdateMirror1").IsEnabled && !Find<RadioButton>(settings,"BuiltinUpdateMirror3").IsEnabled,"batch test enables successful rows only and does not auto select");
                Check(Find<TextBlock>(settings,"MirrorStatus1").Text=="可用" && Find<TextBlock>(settings,"MirrorLatency1").Text.EndsWith("ms"),"successful mirror shows availability and measured latency");
                var alternate=Find<RadioButton>(settings,"BuiltinUpdateMirror1");Check(alternate!=null,"multiple built-in mirror choices are exposed");alternate.IsChecked=true;Pump();Check(pet.Store.Load().UpdateMirrorAddress=="https://gh-proxy.com","alternate built-in mirror persists");
                settings.Close();pet.ShowSettings();Pump();settings=app.Windows.OfType<SettingsWindow>().Single();settings.SelectPage(6);Pump();network=Find<Expander>(settings,"UpdateNetwork");Check(!network.IsExpanded,"reopened network options remain collapsed");network.IsExpanded=true;Pump();Check(Find<RadioButton>(settings,"BuiltinUpdateMirror1").IsChecked==true,"alternate mirror selection survives reopening");direct=Find<RadioButton>(settings,"UpdateDirect");mirror=Find<RadioButton>(settings,"UpdateMirror");proxy=Find<RadioButton>(settings,"UpdateProxy");
                var address=Find<TextBox>(settings,"UpdateMirrorAddress");address.Text="https://mirror.example/prefix";Check(!Find<RadioButton>(settings,"CustomUpdateMirror").IsEnabled,"custom URL must be tested before selection");TestMirrors(settings);Find<RadioButton>(settings,"CustomUpdateMirror").IsChecked=true;Blur(address);Check(pet.Store.Load().UpdateMirrorAddress==address.Text && !settings.HasUnsavedEdits,"tested custom mirror selection persists");Save(settings,"about-mirror.png");
                address.Text="http://unsafe.example";Blur(address);Check(pet.Store.Load().UpdateMirrorAddress=="https://mirror.example/prefix" && settings.HasUnsavedEdits && Find<TextBlock>(settings,"UpdateNetworkNotice").Text.Contains("HTTPS"),"invalid mirror preserves saved value and marks draft");
                direct.IsChecked=true;Check(!pet.Store.Load().UpdateUseMirror && !pet.Store.Load().UpdateUseProxy && !settings.HasUnsavedEdits,"direct mode remains available after invalid hidden mirror draft");
                proxy.IsChecked=true;Pump();var proxyAddress=Find<TextBox>(settings,"UpdateProxyAddress");Check(proxyAddress.IsVisible && proxyAddress.Text=="http://127.0.0.1:7890" && !pet.Store.Load().UpdateUseMirror,"proxy mode restores the default address and excludes mirror");
                proxyAddress.Text="localhost:17894";Blur(proxyAddress);Check(pet.Store.Load().UpdateProxyAddress=="http://localhost:17894" && pet.Store.Load().UpdateUseProxy,"proxy address is saved");Save(settings,"about-proxy.png");
                settings.Close();pet.ShowSettings();Pump();settings=app.Windows.OfType<SettingsWindow>().Single();settings.SelectPage(6);Pump();network=Find<Expander>(settings,"UpdateNetwork");network.IsExpanded=true;Pump();
                Check(Find<RadioButton>(settings,"UpdateProxy").IsChecked==true && Find<TextBox>(settings,"UpdateProxyAddress").Text=="http://localhost:17894","reopening restores the visible selected mode and address");
                settings.Width=700;settings.Height=430;Pump();var button=Find<Button>(settings,"UninstallApplication");var bounds=button.TransformToAncestor(settings).TransformBounds(new Rect(button.RenderSize));Check(bounds.Left>=0 && bounds.Right<=settings.ActualWidth && bounds.Top>=0 && bounds.Bottom<=settings.ActualHeight,"uninstall entry is visible without scrolling at minimum size");Save(settings,"about-minimum.png");
                settings.Close();pet.ShowOnboarding();Pump();var guide=app.Windows.OfType<OnboardingWindow>().Single();Check(guide.Title.StartsWith("配置引导") && (string)Find<Button>(guide,"GuideLater").Content=="跳过本步","configuration guide uses updated labels");
                var skip=Find<Button>(guide,"GuideSkip");var step=Find<Button>(guide,"GuideLater");Check(step.TransformToAncestor(guide).Transform(new Point()).X<skip.TransformToAncestor(guide).Transform(new Point()).X && (string)skip.Content=="结束引导","step skip and explicit guide finish have separate reachable actions");Save(guide,"configuration-guide.png");guide.Close();
                Console.WriteLine("About smoke: "+passed+" passed; screenshots: "+folder);return 0;
            }
            catch(Exception e){Console.WriteLine(e);return 1;}finally{if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
        static void Blur(TextBox field){field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));}
        internal static void TestMirrors(SettingsWindow settings)
        {
            var about=(AboutPanel)typeof(SettingsWindow).GetField("about",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(settings);
            about.MirrorProbe=async (address,token)=>{await System.Threading.Tasks.Task.Delay(30,token);if(address.Contains("hk.gh-proxy"))throw new ApiException("fixture unavailable");};
            var task=about.TestMirrorsAsync();var deadline=DateTime.UtcNow.AddSeconds(5);while(!task.IsCompleted && DateTime.UtcNow<deadline)Pump();if(!task.IsCompleted)throw new Exception("mirror UI test timed out");task.GetAwaiter().GetResult();
        }
        static void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
        static T Find<T>(DependencyObject node,string name)where T:FrameworkElement{var found=node as T;if(found!=null && found.Name==name)return found;foreach(object item in LogicalTreeHelper.GetChildren(node)){var child=item as DependencyObject;if(child!=null){found=Find<T>(child,name);if(found!=null)return found;}}return null;}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Save(FrameworkElement target,string name){target.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth),(int)Math.Ceiling(target.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,name)))png.Save(output);}
    }
}
