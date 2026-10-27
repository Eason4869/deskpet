using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        readonly object agentJobServicesGate=new object();
        AgentJobApplicationService agentJobServices;
        internal Action<string> AgentJobExitNotice;
        internal AgentJobApplicationService AgentJobs
        {
            get
            {
                lock(agentJobServicesGate)
                    return agentJobServices??(agentJobServices=new AgentJobApplicationService(Store.DirectoryPath,clock=>new WindowsAgentJobProcessFactory(clock),new ApplicationJobClock(),DataMaintenance));
            }
        }
        async void InitializeAgentJobs()
        {
            AgentJobs.TerminalCompleted+=AgentJobCompleted;
            if(!await AgentJobs.InitializeAsync()||Dispatcher.HasShutdownStarted)return;
            try{await Dispatcher.InvokeAsync(delegate{if(!closing&&projectCatalog!=null)CompleteProjectOwnership();});}
            catch(OperationCanceledException){}
        }
        internal bool HasActiveAgentJobs
        {
            get{AgentJobApplicationService services;lock(agentJobServicesGate)services=agentJobServices;var manager=services==null?null:services.Manager;return manager!=null&&manager.ListAll().Any(AgentJobUiText.IsActive);}
        }
        readonly System.Collections.Generic.List<Window> agentJobWindows=new System.Collections.Generic.List<Window>();
        internal void ShowAgentJobs(string projectId,string conversationId=null)
        {
            if(closing||String.IsNullOrWhiteSpace(projectId))return;
            var window=new Window{MinWidth=360,MinHeight=420};LocalizedUi.Window(window,()=>(TextResources.Get("agent.jobs.ui.title")),720,680);window.MinWidth=360;
            var root=new System.Windows.Controls.DockPanel{Margin=new Thickness(16)};window.Content=root;
            var actions=new System.Windows.Controls.WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};System.Windows.Controls.DockPanel.SetDock(actions,System.Windows.Controls.Dock.Bottom);root.Children.Add(actions);
            var panel=new AgentJobsPanel(AgentJobs,projectId,conversationId,null,false,OpenChatBrowserLink);var retry=LocalizedUi.Button(()=>(TextResources.Get("agent.jobs.ui.retry")));retry.Click+=delegate{panel.Retry();};actions.Children.Add(retry);
            var close=LocalizedUi.Button(()=>(TextResources.Get("agent.jobs.ui.close")));close.Click+=delegate{window.Close();};actions.Children.Add(close);root.Children.Add(panel);
            agentJobWindows.Add(window);window.Closed+=delegate{panel.Dispose();agentJobWindows.Remove(window);};window.Show();
        }
        void AgentJobCompleted(AgentJobSnapshot job)
        {
            if(Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(delegate
            {
                if(closing)return;Notice=TextResources.Format("agent.jobs.ui.finishedNotice",job.Spec.Title,AgentJobUiText.State(job.State));
                if(workspaces!=null){var owner=workspaces.OpenWorkspaces.FirstOrDefault(w=>w.Id==job.Owner.ConversationId&&w.ProjectId==job.Owner.ProjectId);if(owner!=null)owner.Session.ReportNotice(Notice);}
                if(tray!=null)tray.ShowBalloonTip(4000,TextResources.Get("agent.jobs.ui.title"),Notice.Length>240?Notice.Substring(0,240):Notice,System.Windows.Forms.ToolTipIcon.Info);
            }));
        }
        internal bool TryChangeWorkBindings(string[] projects,string conversationId,Action change)
        {
            return new AgentJobCatalogGuard(AgentJobs).TryRun(projects,conversationId,change);
        }
        Task OnCatalogUiAsync(Func<Task> mutation)
        {return Dispatcher.CheckAccess()?mutation():Dispatcher.InvokeAsync(mutation).Task.Unwrap();}
        internal Task ChangeWorkBindingsAsync(string[] projects,string conversationId,Func<Task> change)
        {return new AgentJobCatalogGuard(AgentJobs).RunAsync(projects,conversationId,true,()=>OnCatalogUiAsync(change),CancellationToken.None);}

        async Task<bool> StopAgentJobsForExitAsync()
        {
            AgentJobApplicationService services;lock(agentJobServicesGate)services=agentJobServices;
            if(services==null)return true;
            var report=await services.StopAllForExitAsync(CancellationToken.None);
            if(report.CleanupConfirmed)return true;
            Notice=TextResources.Get("agentJobs.exitBlocked");
            if(AgentJobExitNotice!=null)AgentJobExitNotice(Notice);
            else MessageBox.Show(Notice,TextResources.Get("app.displayName"),MessageBoxButton.OK,MessageBoxImage.Warning);
            return false;
        }
        void DisposeAgentJobs()
        {lock(agentJobServicesGate)if(agentJobServices!=null){agentJobServices.TerminalCompleted-=AgentJobCompleted;agentJobServices.Dispose();}}
        sealed class ApplicationJobClock : IAgentJobClock
        {
            readonly Stopwatch elapsed=Stopwatch.StartNew();
            public DateTime UtcNow {get{return DateTime.UtcNow;}}
            public long ElapsedMilliseconds {get{return elapsed.ElapsedMilliseconds;}}
        }
    }
}
