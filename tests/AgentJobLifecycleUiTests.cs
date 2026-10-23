using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
namespace Dafeiyu.Tests
{
    public static class AgentJobLifecycleUiTests
    {
        static int checks;static Application app;static PetWindow pet;
        static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        static async Task Run()
        {
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,ReadReplies=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,OnboardingState="completed"};
                foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root.Path,"settings.json")),null);pet.Show();pet.SetPaused(true);
                Exception failure=null;
                try
                {
                    var property=typeof(PetWindow).GetProperty("AgentJobs",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
                    Check(property!=null,"WPF application exposes its owned background job service");
                    var service=(AgentJobApplicationService)property.GetValue(pet,null);
                    Check(await service.InitializeAsync(),"WPF recovers its task store asynchronously");
                    Check(Object.ReferenceEquals(pet.Workspaces.JobServices,service),"all WPF persistent workspaces receive the application service");
                    var manager=service.Manager;pet.HidePet();await Task.Delay(50);
                    Check(Object.ReferenceEquals(manager,service.Manager),"hiding the companion preserves application task ownership");
                    pet.RestorePet();pet.Close();
                    for(int attempt=0;attempt<100&&service.Manager!=null;attempt++)await Task.Delay(50);
                    Check(service.Manager==null,"normal WPF exit drains and releases its task store");
                    using(var next=new AgentJobStore(root.Path,new Clock()))Check(next!=null,"WPF exit releases the exclusive data-directory lease");
                }
                catch(Exception error){failure=error;}
                if(pet!=null){pet.Close();for(int attempt=0;attempt<100&&pet.IsVisible;attempt++)await Task.Delay(50);}
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        sealed class Clock : IAgentJobClock
        {readonly System.Diagnostics.Stopwatch elapsed=System.Diagnostics.Stopwatch.StartNew();public DateTime UtcNow{get{return DateTime.UtcNow;}}public long ElapsedMilliseconds{get{return elapsed.ElapsedMilliseconds;}}}
        static async Task IndependentExitHosts()
        {
            foreach(string mode in new[]{"normal","tray","update","kill","held"})
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            using(var host=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--exit-fixture "+mode+" \""+root.Path+"\""){UseShellExecute=false,CreateNoWindow=true}))
            {
                var owned=new System.Collections.Generic.List<System.Diagnostics.Process>();
                Exception failure=null;
                try
                {
                    await AgentJobExitHost.WaitAsync(()=>File.Exists(Path.Combine(root.Path,"host.ready"))||host.HasExited,15000,mode+" host readiness");
                    Check(!host.HasExited,mode+" independent WPF host starts a real owned tree");
                    foreach(string pid in File.ReadAllLines(Path.Combine(root.Path,"host.ready")))
                    {var process=System.Diagnostics.Process.GetProcessById(Int32.Parse(pid));var handle=process.Handle;owned.Add(process);}
                    Check(owned.Count==2&&owned.All(process=>!process.HasExited),mode+" root and child are alive before exit");
                    var elapsed=System.Diagnostics.Stopwatch.StartNew();
                    if(mode=="kill")host.Kill();else File.WriteAllText(Path.Combine(root.Path,"exit.request"),"exit");
                    if(mode=="held")
                    {
                        await AgentJobExitHost.WaitAsync(()=>File.Exists(Path.Combine(root.Path,"exit.held"))||host.HasExited,6000,"held-exit state");
                        Check(elapsed.ElapsedMilliseconds>=4000,"independent WPF cleanup timeout consumes the bounded stop deadline");
                        Check(!host.HasExited&&owned.All(process=>!process.HasExited),"cleanup failure retains the independent host and its owned tree");
                        bool leased=false;try{using(var denied=new AgentJobStore(root.Path,new Clock())){}}catch(IOException){leased=true;}
                        Check(leased,"held WPF exit keeps the writer lease until retry");
                        File.WriteAllText(Path.Combine(root.Path,"cleanup.allow"),"retry");elapsed.Restart();
                    }
                    await AgentJobExitHost.WaitAsync(()=>host.HasExited&&owned.All(process=>process.HasExited),mode=="kill"?5000:10000,mode+" host and tree exit");
                    Check(elapsed.ElapsedMilliseconds<=(mode=="kill"?5000:10000),mode+" host and process tree meet the exit budget");
                    if(mode!="kill")Check(host.ExitCode==0&&File.Exists(Path.Combine(root.Path,"exit.complete")),mode+" exit confirms cleanup before closing");
                    string id=File.ReadAllText(Path.Combine(root.Path,"job.id"));
                    using(var store=new AgentJobStore(root.Path,new Clock()))
                    {
                        var snapshots=await store.LoadAsync(CancellationToken.None);
                        Check(snapshots.Count==1&&snapshots[0].Id==id&&snapshots[0].State==(mode=="kill"?AgentJobState.Interrupted:AgentJobState.Stopped),mode+" reopen preserves identity and never reruns the task");
                    }
                }
                catch(Exception error){failure=new Exception(mode+" host failure"+(File.Exists(Path.Combine(root.Path,"fixture.error"))?": "+File.ReadAllText(Path.Combine(root.Path,"fixture.error")):""),error);}
                if(!host.HasExited){host.Kill();await AgentJobExitHost.WaitAsync(()=>host.HasExited,5000,"fixture host cleanup");}
                await AgentJobExitHost.WaitAsync(()=>owned.All(process=>process.HasExited),5000,"fixture owned-tree cleanup");
                foreach(var process in owned)process.Dispose();
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        [STAThread]public static int Main(string[] args)
        {
            if(args.Length==3&&args[0]=="--exit-fixture")return AgentJobExitHost.Run(args[1],args[2]);
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
            app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();await IndependentExitHosts();Console.WriteLine("AgentJobLifecycleUiTests: "+checks+" passed");result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{app.Shutdown();}}));
            app.Run();return result;
        }
    }
}
