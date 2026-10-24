using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class DataMaintenanceHostTests
    {
        static void Wait(Task task)
        {
            var deadline=DateTime.UtcNow.AddSeconds(20);
            while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("Admission host fixture");var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(5)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}task.GetAwaiter().GetResult();
        }
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            string root=Path.GetFullPath(Path.Combine("work","data-admission-host",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            try
            {
                var cfg=new PetSettings{AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,FollowCursor=false,Wander=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();Wait(pet.AgentJobs.InitializeAsync());
                var gate=pet.DataMaintenance;
                if(!ReferenceEquals(gate,pet.Conversation.Maintenance)||!ReferenceEquals(gate,pet.Dictation.Maintenance)||!ReferenceEquals(gate,pet.Live.Maintenance)||!ReferenceEquals(gate,pet.Workspaces.Maintenance)||!ReferenceEquals(gate,pet.AgentJobs.Maintenance))throw new Exception("WPF producer gates differ");
                pet.Activate();if(!pet.CanApplyUpdate(true))throw new Exception("Update admission fixture was not initially idle");
                var acquired=gate.BeginAsync();Wait(acquired);using(acquired.Result)
                {
                    if(pet.CanApplyUpdate(true)||pet.CanApplyUpdate(false))throw new Exception("Update can close the host during maintenance");
                    var previous=pet.Skin;string id=cfg.SkinId,key=cfg.SelectedCompanionKey,persona=pet.Conversation.PersonaId;
                    var target=pet.Catalog.Skins.First(s=>s.Id!=previous.Id);if(pet.SelectSkin(target))throw new Exception("Selection admitted during maintenance");
                    if(!ReferenceEquals(pet.Skin,previous)||cfg.SkinId!=id||cfg.SelectedCompanionKey!=key||pet.Conversation.PersonaId!=persona)throw new Exception("WPF maintenance changed companion identity");
                    if(String.IsNullOrWhiteSpace(pet.Notice))throw new Exception("Missing localized refusal");
                    pet.Close();if(!pet.IsVisible||(bool)typeof(PetWindow).GetField("closing",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(pet))throw new Exception("Ordinary exit started during maintenance");
                }
                pet.Close();var deadline=DateTime.UtcNow.AddSeconds(20);while(pet.IsVisible){if(DateTime.UtcNow>deadline)throw new TimeoutException("Normal close did not finish after maintenance");Wait(Task.Delay(5));}Wait(pet.ImageServicesShutdown);
                using(gate.BeginAsync().GetAwaiter().GetResult()){}
                Console.WriteLine("DataMaintenanceHostTests: 6 passed (shared gate, companion coherence, refusal, update exclusion, exit exclusion, normal close drain)");return 0;
            }
            catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}
            finally{if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
