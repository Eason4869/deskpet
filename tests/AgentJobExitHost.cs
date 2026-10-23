using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu.Tests
{
    // Independent real WPF host. No live model or updater executable is invoked.
    internal static class AgentJobExitHost
    {
        sealed class Clock : IAgentJobClock
        {readonly Stopwatch elapsed=Stopwatch.StartNew();public DateTime UtcNow{get{return DateTime.UtcNow;}}public long ElapsedMilliseconds{get{return elapsed.ElapsedMilliseconds;}}}
        sealed class Lease : IDisposable {public void Dispose(){}}
        sealed class Approval : IAgentAuthorizationService
        {public Task<AgentPermissionDecision> AuthorizeAsync(AgentToolInvocation invocation,CancellationToken token){return Task.FromResult(new AgentPermissionDecision{Effect=AgentPermissionEffect.Allow});}}
        sealed class HeldFactory : IAgentJobProcessFactory
        {
            readonly WindowsAgentJobProcessFactory native;readonly string release;
            internal HeldFactory(IAgentJobClock clock,string release){native=new WindowsAgentJobProcessFactory(clock);this.release=release;}
            public async Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec,AgentJobPolicySnapshot policy,Action<AgentJobOutputChunk> output,CancellationToken token)
            {return new HeldProcess(await native.PrepareAsync(spec,policy,output,token).ConfigureAwait(false),release);}
        }
        sealed class HeldProcess : IAgentJobProcess
        {
            readonly IAgentJobProcess native;readonly string release;
            readonly TaskCompletionSource<AgentJobCleanupResult> blocked=new TaskCompletionSource<AgentJobCleanupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal HeldProcess(IAgentJobProcess native,string release){this.native=native;this.release=release;}
            public AgentJobProcessExit Status{get{return native.Status;}}
            public Task<AgentJobProcessExit> Completion{get{return native.Completion;}}
            public void Resume(){native.Resume();}
            public Task<AgentJobCleanupResult> StopAsync(CancellationToken token)
            {return File.Exists(release)?native.StopAsync(token):blocked.Task;}
            public void Dispose(){blocked.TrySetResult(new AgentJobCleanupResult(false,"fixture_disposed"));native.Dispose();}
        }
        static string Quote(string text){return "'"+text.Replace("'","''")+"'";}
        internal static async Task WaitAsync(Func<bool> condition,int milliseconds,string description)
        {
            var watch=Stopwatch.StartNew();
            while(!condition()){if(watch.ElapsedMilliseconds>milliseconds)throw new TimeoutException(description);await Task.Delay(25);}
        }
        static async Task RunAsync(string mode,string root)
        {
            var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,ReadReplies=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,OnboardingState="completed"};
            foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            var pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();pet.SetPaused(true);
            var field=typeof(PetWindow).GetField("agentJobServices",BindingFlags.Instance|BindingFlags.NonPublic);
            var services=(AgentJobApplicationService)field.GetValue(pet);
            if(mode=="held")
            {
                await services.InitializeAsync();await services.StopAllForExitAsync(CancellationToken.None);services.Dispose();
                services=new AgentJobApplicationService(root,clock=>new HeldFactory(clock,Path.Combine(root,"cleanup.allow")),new Clock());field.SetValue(pet,services);
            }
            typeof(PetWindow).GetField("AgentJobExitNotice",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,(Action<string>)(message=>File.WriteAllText(Path.Combine(root,"exit.held"),message)));
            if(!await services.InitializeAsync())throw new Exception("Host lease initialization failed");
            string pids=Path.Combine(root,"owned.pids");
            string command="$child=Start-Process -FilePath "+Quote(AgentCommandExecutor.PowerShellPath())+" -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 120' -WindowStyle Hidden -PassThru; [IO.File]::WriteAllText("+Quote(pids)+",([string]$PID+[Environment]::NewLine+[string]$child.Id)); Start-Sleep -Seconds 120";
            var owner=new AgentJobOwner("host-project","host-conversation","primary");
            var invocation=new AgentToolInvocation("start_job",new Dictionary<string,object>{{"command",command},{"title",command},{"working_directory",root},{"timeout_seconds",1800}},new AgentToolExecutionContext{ProjectId=owner.ProjectId,AgentId="primary",TaskId="host-task",CallId="host-start"});
            var context=new AgentJobStartContext(owner,invocation,new Approval(),()=>new AgentJobPolicySnapshot(root,"fixture-directory",1,1800,true,false,false,AgentApprovalMode.Full,null),()=>new Lease(),()=>{});
            var job=await services.Manager.StartAsync(context,new AgentJobSpec(command,null,root,1800),CancellationToken.None);
            await WaitAsync(()=>File.Exists(pids)&&File.ReadAllLines(pids).Length==2,5000,"Native host tree readiness");
            File.WriteAllText(Path.Combine(root,"job.id"),job.Id);
            File.WriteAllText(Path.Combine(root,"ready.tmp"),File.ReadAllText(pids));File.Move(Path.Combine(root,"ready.tmp"),Path.Combine(root,"host.ready"));
            await WaitAsync(()=>File.Exists(Path.Combine(root,"exit.request")),30000,"Parent exit request");
            if(mode=="update")await pet.PrepareUpdateAsync();
            if(mode=="tray")
            {
                var menu=(TrayContextMenu)typeof(PetWindow).GetField("trayMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                menu.Items.OfType<MenuItem>().Single(item=>item.Name=="TrayExit").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else pet.Close();
            if(mode=="held")
            {
                await WaitAsync(()=>File.Exists(Path.Combine(root,"exit.held")),5000,"Held-exit notice");
                if(!pet.IsVisible||services.Manager==null)throw new Exception("Unconfirmed cleanup discarded its window or process owner");
                await WaitAsync(()=>File.Exists(Path.Combine(root,"cleanup.allow")),15000,"Parent cleanup retry");pet.Close();
            }
            await WaitAsync(()=>!pet.IsVisible&&services.Manager==null,10000,"Confirmed WPF exit");
            File.WriteAllText(Path.Combine(root,"exit.complete"),"confirmed");
        }
        internal static int Run(string mode,string root)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
            app.Dispatcher.BeginInvoke(new Action(async delegate
            {
                try{await RunAsync(mode,root);result=0;}
                catch(Exception error){File.WriteAllText(Path.Combine(root,"fixture.error"),error.ToString());Console.Error.WriteLine(error);}
                finally{app.Shutdown();}
            }));
            app.Run();return result;
        }
    }
}
