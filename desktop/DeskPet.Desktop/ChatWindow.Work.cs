using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class ChatWindow
{
    private ConversationWorkspace? binding;
    private ConversationSession? boundSession;private ConversationQueue? boundQueue;
    private int bindingEpoch;
    private Button? workBindingButton,workJobsButton;
    private StackPanel? agentTaskProgress;
    private TextBlock agentTaskStatus = null!, agentTaskRequests = null!, agentTaskTokens = null!, agentTaskNotice = null!;
    private Button stopAgentTask = null!, continueAgentTask = null!, acknowledgeAgentTask = null!;
    private (ConversationSession Session, string Persona, string Workspace)? loadedAgentTaskScope;
    internal ProactiveConversationBinding? ProactiveBinding {get;}
    private string DraftKey=>ProactiveBinding!=null?"proactive:"+ProactiveBinding.Id:(quick?"quick:":"full:")+ComposerDraftStore.Scope(persona,
        !quick&&(binding!=null||OperatingSystem.IsWindows()&&owner.Settings.AgentEnabled)?"work":"chat",
        binding?.ProjectId??"",binding?.Id??"");
    internal ConversationSession BoundSession=>ProactiveBinding?.Session??boundSession??owner.Session;
    internal ConversationQueue BoundQueue=>ProactiveBinding?.Queue??boundQueue??owner.Queue;
    internal PetSettings EffectiveSettings()=>ProactiveBinding?.EffectiveSettings()??binding?.EffectiveSettings()??owner.WorkBindings.RootSettings(BoundSession.PersonaId);
    private Func<PetSettings> CaptureSettings(){if(ProactiveBinding!=null)return ProactiveBinding.EffectiveSettings;var source=binding;return source==null?()=>owner.WorkBindings.RootSettings(owner.Session.PersonaId):source.EffectiveSettings;}
    internal void FreezeForExit(){chatPage.IsEnabled=false;imageWorkspace.IsEnabled=false;}
    private async Task StopBoundAsync(){var queue=BoundQueue;var session=BoundSession;await owner.Dictation.CancelAsync(this);await queue.PauseAsync();await session.StopAsync();}
    private Control CreateAgentTaskProgress()
    {
        agentTaskStatus = Ui.Text("", 12);
        agentTaskStatus.Name = "AgentTaskStatus";
        agentTaskRequests = Ui.Text("", 12);
        agentTaskRequests.Name = "AgentTaskRequestCount";
        agentTaskTokens = Ui.Text("", 12);
        agentTaskTokens.Name = "AgentTaskTokenEstimate";
        agentTaskNotice = Ui.Text("", 12);
        agentTaskNotice.Name = "AgentTaskNotice";
        stopAgentTask = LocalizedUi.Button(() => TextResources.Get("agent.jobs.ui.stop"), () => owner.Run(StopBoundAsync));
        stopAgentTask.Name = "StopAgentTask";
        continueAgentTask = LocalizedUi.Button(() => TextResources.Get("agentTaskPanel.refresh.small.alternativeE"), () => owner.Run(ContinueAgentTaskAsync));
        continueAgentTask.Name = "ContinueAgentTask";
        acknowledgeAgentTask = LocalizedUi.Button(() => TextResources.Get("agentTaskPanel.refresh.small.alternativeD"),
            () => owner.Run(() => BoundSession.AcknowledgeAgentExecutionsAsync(EffectiveSettings())));
        acknowledgeAgentTask.Name = "AcknowledgeAgentExecutions";
        var actions = new WrapPanel { Children = { stopAgentTask, acknowledgeAgentTask, continueAgentTask } };
        acknowledgeAgentTask.Margin = new Thickness(0, 0, 8, 0);
        agentTaskProgress = Ui.Column(agentTaskStatus, agentTaskRequests, agentTaskTokens, agentTaskNotice, actions);
        agentTaskProgress.Spacing = 4;
        agentTaskProgress.IsVisible = false;
        return agentTaskProgress;
    }

    private void RefreshAgentTaskProgress()
    {
        if (agentTaskProgress == null) return;
        var session = BoundSession;
        var settings = EffectiveSettings();
        bool work = OperatingSystem.IsWindows() && settings.AgentEnabled;
        var scope = (session, session.PersonaId, settings.AgentWorkspace);
        if (work && !session.IsBusy && loadedAgentTaskScope != scope)
        {
            loadedAgentTaskScope = scope;
            session.LoadAgentTask(settings);
        }
        var task = session.CurrentAgentTask;
        agentTaskProgress.IsVisible = work && task != null && (session.IsBusy || loadedAgentTaskScope == scope);
        if (!agentTaskProgress.IsVisible || task == null) return;
        LocalizedUi.Set(agentTaskStatus, "Text", () => AgentTaskProgressText.Status(task));
        LocalizedUi.Set(agentTaskRequests, "Text", () => AgentTaskProgressText.RequestCount(task));
        LocalizedUi.Set(agentTaskTokens, "Text", () => AgentTaskProgressText.TokenEstimate(task));
        agentTaskNotice.IsVisible = !string.IsNullOrEmpty(LocalizedUi.Set(agentTaskNotice, "Text", () => AgentTaskProgressText.Notice(task)));
        bool requiresReconciliation = task.Status == "needs_reconciliation";
        bool canChangeTask = !owner.IsExiting && !owner.Live.IsActive && !owner.Dictation.IsBusy && !session.IsBusy;
        stopAgentTask.IsVisible = session.IsBusy && task.Status == "running";
        stopAgentTask.IsEnabled = !owner.IsExiting;
        acknowledgeAgentTask.IsVisible = requiresReconciliation;
        acknowledgeAgentTask.IsEnabled = canChangeTask;
        continueAgentTask.IsVisible = !stopAgentTask.IsVisible;
        continueAgentTask.IsEnabled = canChangeTask && task.Status != "running" && !requiresReconciliation;
    }

    private async Task ContinueAgentTaskAsync()
    {
        var session = BoundSession;
        var settings = EffectiveSettings();
        var queue = BoundQueue;
        bool resumeQueue = !queue.IsPaused;
        queue.Hold();
        try { await session.ResumeAgentTaskAsync(settings, ChatCompanion.Name); }
        finally { if (resumeQueue) queue.Resume(); }
    }

    internal async Task BindWorkAsync(string? projectId,string? conversationId)
    {
        if(ProactiveBinding!=null)return;
        if(closed)return;
        if(owner.WorkBindings.IsDraining)throw new InvalidOperationException(TextResources.Get("agentJobs.exitBlocked"));
        int request=++bindingEpoch;
        var next=projectId==null||conversationId==null?null:await owner.WorkBindings.OpenAsync(projectId,conversationId,CancellationToken.None);
        if(closed||request!=bindingEpoch)return;
        await owner.Dictation.CancelAsync(this);
        if(closed||request!=bindingEpoch)return;
        SaveDraft();
        string oldSession=QuestionSessionId;binding=next;boundSession=next?.Session;boundQueue=next?.Queue;uploadEpoch++;
        owner.WorkViewChanged(this,oldSession);owner.ObserveNotificationSession(BoundSession);
        foreach(var view in rendered.Values)view.DisposeImages();rendered.Clear();messages.Children.Clear();queuedEdits.Clear();questionSignature="";
        draft.Text=LoadDraft();RefreshAttachments();Refresh();
        if(workBindingButton!=null)workBindingButton.Content=new TextBlock{Text=binding==null?TextResources.Get("agent.work.choose"):TextResources.Format("agent.work.current",owner.WorkBindings.Snapshot.Projects.Single(project=>project.Id==binding.ProjectId).Name,binding.Name),TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis,MaxWidth=210};
    }
    private Control CreateWorkBindingActions()
    {
        workBindingButton=LocalizedUi.Button(()=>(TextResources.Get("agent.work.choose")),()=>owner.Run(ShowWorkPickerAsync));workBindingButton.Name="ChooseWorkConversation";
        var settings=LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.settingsWindow.text.alternativeE")),()=>
        {
            if(binding==null){owner.ShowSettingsSection("work");return;}
            var dialog=LocalizedUi.Window(()=>(TextResources.Get("settingsWindow.settingsWindow.text.alternativeE")),new Border(),600,650);dialog.Content=new ScrollViewer{Content=new WorkSettingsView(owner,dialog,binding.ProjectId)};dialog.Show(this);
        });settings.Name="BoundWorkSettings";workJobsButton=LocalizedUi.Button(()=>(TextResources.Get("agent.jobs.ui.title")),()=>{if(binding!=null)owner.ShowAgentJobs(binding.ProjectId);});workJobsButton.Name="BoundAgentJobs";workJobsButton.IsEnabled=binding!=null;var actions=new WrapPanel();actions.Children.Add(workBindingButton);actions.Children.Add(settings);actions.Children.Add(workJobsButton);return actions;
    }
    private async Task ShowWorkPickerAsync()
    {
        var dialog=new WorkProjectPicker(owner,this,BindWorkAsync);await dialog.ShowDialog(this);
    }
    private void ChangeWorkSetting(Action<PetSettings> change)=>ChangeWorkSettingFor(binding?.ProjectId,change);
    private void ChangeWorkSettingFor(string? projectId,Action<PetSettings> change)
    {
        if(ProactiveBinding!=null)return;
        if(projectId==null){change(owner.Settings);owner.Save();return;}
        var settings=owner.WorkBindings.ProjectSettings(projectId);change(settings);
        if(!owner.WorkBindings.TryChangeProject(projectId,project=>{project.PlanMode=settings.AgentPlanMode;project.Goal=settings.AgentGoal;}))throw new InvalidOperationException(TextResources.Get("agent.jobs.ui.bindingBusy"));
    }
}
