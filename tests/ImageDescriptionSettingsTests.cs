using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ImageDescriptionSettingsTests
    {
        static int checks;
        static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static T Find<T>(DependencyObject root,string name)where T:FrameworkElement
        {var item=root as T;if(item!=null && item.Name==name)return item;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var result=Find<T>(VisualTreeHelper.GetChild(root,i),name);if(result!=null)return result;}return null;}
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(5);}
        static void Until(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>deadline)throw new TimeoutException("setting did not synchronize");Pump();}}
        static void Save(FrameworkElement element,string path){element.UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(element);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
        [STAThread]public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());PetWindow pet=null;Window workHost=null;
            try{
                if(args.Length==2 && args[0]=="--inspect-settings"){
                    var inspected=new SettingsStore(args[1]).Load();var candidates=ImageModelResolver.Candidates(inspected);Console.WriteLine("Provider count: "+inspected.TextProviders.Count+"; configured model count: "+inspected.TextProviders.Sum(p=>p.Models.Count)+"; supported image candidate count: "+candidates.Count);return 0;
                }
                var cfg=new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,FollowCursor=false,Wander=false,GreetingEnabled=false,SaveChatHistory=false,OnboardingState="completed"};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                var provider=new TextProviderSettings{Id="shared-vision",Name="Vision",Connection=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="vision"},Models=new System.Collections.Generic.List<string>{"vision"}};
                provider.ModelCapabilities["vision"]=new ModelCapabilitySetting{Images=CapabilitySupport.Supported};provider.Normalize();cfg.TextProviders.Add(provider);
                var automatic=new TextProviderSettings{Id="mimo-automatic",Name="MiMo",Connection=new ConnectionSettings{BaseUrl="https://token-plan-cn.xiaomimimo.com/v1",Model="mimo-v2.6-flash"},Models=new System.Collections.Generic.List<string>{"mimo-v2.6-flash","mimo-v2.6-pro"}};automatic.Normalize();cfg.TextProviders.Add(automatic);
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"description-sync-"+Guid.NewGuid().ToString("N"),"settings.json");
                pet=new PetWindow(null,cfg,new SettingsStore(path),null);pet.Show();pet.ShowSettings();Pump();
                var page=app.Windows.OfType<Window>().Select(w=>Find<SettingsPage>(w,"SettingsPage")).FirstOrDefault(p=>p!=null);
                if(page==null)page=(SettingsPage)typeof(PetWindow).GetField("preferences",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(pet);
                page.SelectCompanionSection(2);Pump();var observation=page.FindControl("ObservationImageDescriptionModel") as ComboBox;
                Check(observation!=null,"observation settings contain the global image description selector");
                workHost=new Window{Content=new WorkSettingsPanel(pet)};Ui.Window(workHost,"Work settings",900,760);workHost.Show();Pump();var work=Find<ComboBox>(workHost,"WorkImageDescriptionModel");
                Check(work!=null,"work permissions contain the same global image description setting");
                Check(observation.Items.OfType<ComboBoxItem>().Count(i=>i.Tag is ModelReference && ((ModelReference)i.Tag).ProviderId=="mimo-automatic")==2 && work.Items.OfType<ComboBoxItem>().Count(i=>i.Tag is ModelReference && ((ModelReference)i.Tag).ProviderId=="mimo-automatic")==2,"both pickers recognize automatic MiMo image capabilities without manual overrides");
                work.BringIntoView();Pump();System.IO.Directory.CreateDirectory("work/description-screens");Save(workHost,"work/description-screens/permissions.png");
                var choice=observation.Items.OfType<ComboBoxItem>().Single(i=>i.Tag is ModelReference && ((ModelReference)i.Tag).ProviderId=="shared-vision");observation.SelectedItem=choice;
                Until(()=>observation.IsEnabled && cfg.ImageDescriptionModel!=null);Check(((ModelReference)((ComboBoxItem)work.SelectedItem).Tag).ProviderId=="shared-vision" && pet.Store.Load().ImageDescriptionModel.ModelId=="vision","observation selection updates work selector and persisted settings");
                work.SelectedIndex=0;Until(()=>work.IsEnabled && cfg.ImageDescriptionModel==null);Check(((ComboBoxItem)observation.SelectedItem).Tag==null && pet.Store.Load().ImageDescriptionModel==null,"work selection updates observation selector and persisted settings");
                pet.Store.BeforeExactSave=()=>{throw new IOException("fixture save failure");};observation.SelectedItem=observation.Items.OfType<ComboBoxItem>().Single(i=>i.Tag is ModelReference && ((ModelReference)i.Tag).ProviderId=="shared-vision");
                Until(()=>observation.IsEnabled);Check(cfg.ImageDescriptionModel==null && ((ComboBoxItem)observation.SelectedItem).Tag==null && ((ComboBoxItem)work.SelectedItem).Tag==null,"failed save rolls back both selectors");pet.Store.BeforeExactSave=null;
                page.SelectNavigation(1);Pump();Check(page.FindControl("ImageDescriptionModel")==null && page.FindControl("ObservationImageDescriptionModel")==null,"model configuration no longer contains image description settings");
                page.SelectCompanionSection(2);Pump();Check(((ComboBoxItem)((ComboBox)page.FindControl("ObservationImageDescriptionModel")).SelectedItem).Tag==null,"reopening observation settings retains shared saved value");
                Console.WriteLine("ImageDescriptionSettingsTests: "+checks+" passed");return 0;
            }catch(Exception error){Console.WriteLine(error);return 1;}
            finally{if(workHost!=null)workHost.Close();if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
    }
}
