using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class EmbeddedSettingsTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static object Call(object page,string name,params object[] args){return page.GetType().GetMethod(name).Invoke(page,args);}
        static FrameworkElement Find(object page,string name){return (FrameworkElement)Call(page,"FindControl",name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        [STAThread] public static int Main(string[] args)
        {
            Application app=null;PetWindow pet=null;Window host=null;IDisposable disposable=null;OnboardingWindow guide=null;
            try
            {
                // Missing embeddable content used to force a second Window for all settings entry points.
                var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.SettingsPage");
                Check(type!=null && typeof(UserControl).IsAssignableFrom(type) && typeof(IDisposable).IsAssignableFrom(type),"settings content is an embeddable disposable page");
                Check(type.GetProperty("HostWindow")!=null && type.GetMethod("SelectNavigation")!=null && type.GetEvent("NavigationChanged")!=null,"page exposes host and navigation contract");
                if(args.Contains("--contract-only"))return 0;
                app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";cfg.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="offline-fixture"};
                string scratch=Path.Combine(Directory.GetCurrentDirectory(),"work","embedded-settings-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(scratch,"settings.json")),null);
                var page=(UserControl)Activator.CreateInstance(type,new object[]{pet});disposable=(IDisposable)page;
                var slot=new ContentControl{Content=page};host=new Window{Width=1030,Height=760,Content=slot};host.Show();Pump();
                Check(Object.ReferenceEquals(type.GetProperty("HostWindow").GetValue(page,null),host),"embedded settings resolve the actual host Window");
                Call(page,"SelectAiSection",0);Pump();var name=(TextBox)Find(page,"TextProviderName");name.Text="unsaved embedded provider";
                Call(page,"RefreshTextProviders");Check(name.Text=="unsaved embedded provider","external provider refresh preserves pending local configuration");
                Call(page,"SelectCompanionSection",0);Pump();var prompt=(TextBox)Find(page,"PersonaPrompt");prompt.Text="unsaved embedded persona";
                Call(page,"SelectWorkSettings");Pump();var goal=(TextBox)Find(page,"AgentGoal");goal.Text="unsaved embedded work goal";
                var scroll=(ScrollViewer)Find(page,"WorkSettingsScroll");scroll.ScrollToVerticalOffset(240);Pump();double offset=scroll.VerticalOffset;
                slot.Content=new TextBlock{Text="Home"};Pump();Call(page,"RefreshState");slot.Content=page;Pump();
                Check(Object.ReferenceEquals(goal,Find(page,"AgentGoal")) && goal.Text=="unsaved embedded work goal" && Math.Abs(scroll.VerticalOffset-offset)<1,"home and settings switching retains work controls draft and scroll");
                Call(page,"SelectAiSection",0);Pump();Check(((TextBox)Find(page,"TextProviderName")).Text=="unsaved embedded provider","background refresh retains AI draft");
                Call(page,"SelectCompanionSection",0);Pump();Check(((TextBox)Find(page,"PersonaPrompt")).Text=="unsaved embedded persona","background refresh retains persona draft");
                Call(page,"SelectWorkSettings");Pump();
                var editing=typeof(PetWindow).GetField("editingProject",BindingFlags.Instance|BindingFlags.NonPublic);editing.SetValue(pet,new ProjectDefinition{Id="another-project",Name="Other project"});
                Check(!(bool)Call(page,"TrySelectWorkSettings") && goal.Text=="unsaved embedded work goal","changing work editing object requires resolving existing draft");editing.SetValue(pet,null);
                int changed=-1;Action<int> onChanged=delegate(int index){changed=index;};type.GetEvent("NavigationChanged").AddEventHandler(page,onChanged);Call(page,"SelectNavigation",4);Pump();Check(changed==4 && (int)type.GetProperty("SelectedNavigationIndex").GetValue(page,null)==4,"navigation reports the actual selected destination");
                // Existing Live test controls resolve Window.GetWindow; the page must not insert a phantom owner.
                Call(page,"SelectAiSection",3);Pump();Check(Object.ReferenceEquals(Window.GetWindow(Find(page,"PreviewLive")),host),"live test control is owned by the visible main host");
                var constructor=typeof(OnboardingWindow).GetConstructor(new[]{typeof(PetWindow),type,typeof(Window)});Check(constructor!=null,"onboarding accepts page and actual host");guide=(OnboardingWindow)constructor.Invoke(new object[]{pet,page,host});Check(Object.ReferenceEquals(guide.Owner,host),"onboarding uses actual host as owner");guide.Close();guide=null;
                Check(!app.Windows.OfType<SettingsWindow>().Any(),"embedded settings create no hidden compatibility window");
                Console.WriteLine("Embedded settings tests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}
            finally{if(guide!=null)guide.Close();if(host!=null)host.Close();if(disposable!=null)disposable.Dispose();if(pet!=null)pet.Close();if(app!=null)app.Shutdown();}
        }
    }
}
