using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    private readonly List<Window> agentJobWindows=new();
    private Window? agentJobNotice;
    internal void ShowAgentJobs(string projectId,string? conversationId=null)
    {
        if(exiting)return;
        var panel=new DesktopAgentJobsPanel(WorkBindings.Jobs,projectId,conversationId);
        var window=Ui.Window(TextResources.Get("agent.jobs.ui.title"),new Border(),720,680);window.MinWidth=390;
        var actions=new WrapPanel();actions.Children.Add(Ui.AsyncButton(TextResources.Get("agent.jobs.ui.retry"),panel.RetryAsync,Error));actions.Children.Add(Ui.Button(TextResources.Get("common.close"),window.Close));
        var root=new DockPanel{Margin=new Thickness(16)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);root.Children.Add(panel);window.Content=root;
        agentJobWindows.Add(window);window.Closed+=(_,_)=>{panel.Dispose();agentJobWindows.Remove(window);};window.Show();
    }
    internal void AgentJobCompleted(AgentJobSnapshot job)
    {
        Dispatcher.UIThread.Post(()=>
        {
            if(exiting)return;string text=TextResources.Format("agent.jobs.ui.finishedNotice",job.Spec.Title,AgentJobUiText.State(job.State));
            var source=WorkBindings.Manager.OpenWorkspaces.FirstOrDefault(workspace=>workspace.Id==job.Owner.ConversationId&&workspace.ProjectId==job.Owner.ProjectId);source?.Session.ReportNotice(text);
            agentJobNotice?.Close();var notice=Ui.Window(TextResources.Get("agent.jobs.ui.title"),new Border(),420,240);notice.Name="AgentJobCompletionNotification";notice.ShowActivated=false;
            notice.Content=Ui.Column(Ui.Text(text),Ui.Row(Ui.Button(TextResources.Get("agent.jobs.ui.openLog"),()=>{notice.Close();ShowAgentJobs(job.Owner.ProjectId,job.Owner.ConversationId);}),Ui.Button(TextResources.Get("common.close"),notice.Close)));
            agentJobNotice=notice;notice.Closed+=(_,_)=>{if(ReferenceEquals(agentJobNotice,notice))agentJobNotice=null;};notice.Show();
        });
    }
    private void CloseAgentJobViews()
    {foreach(var window in agentJobWindows.ToArray())window.Close();agentJobNotice?.Close();agentJobNotice=null;}
}
