using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class ChatWindow
{
    private ConversationWorkspace? binding;
    private ConversationSession? boundSession;private ConversationQueue? boundQueue;
    private int bindingEpoch;
    private Button? workBindingButton,workJobsButton;
    private string DraftKey=>binding==null?persona:"work:"+binding.Id;
    internal ConversationSession BoundSession=>boundSession??owner.Session;
    internal ConversationQueue BoundQueue=>boundQueue??owner.Queue;
    internal PetSettings EffectiveSettings()=>binding?.EffectiveSettings()??owner.Settings;
    private Func<PetSettings> CaptureSettings(){var source=binding;return source==null?()=>owner.Settings:source.EffectiveSettings;}
    internal void FreezeForExit(){chatPage.IsEnabled=false;imageWorkspace.IsEnabled=false;}
    private async Task StopBoundAsync(){var queue=BoundQueue;var session=BoundSession;await owner.Dictation.CancelAsync(this);await queue.PauseAsync();await session.StopAsync();}
    internal async Task BindWorkAsync(string? projectId,string? conversationId)
    {
        if(closed)return;
        if(owner.WorkBindings.IsDraining)throw new InvalidOperationException(TextResources.Get("agentJobs.exitBlocked"));
        int request=++bindingEpoch;
        var next=projectId==null||conversationId==null?null:await owner.WorkBindings.OpenAsync(projectId,conversationId,CancellationToken.None);
        if(closed||request!=bindingEpoch)return;
        await owner.Dictation.CancelAsync(this);
        if(closed||request!=bindingEpoch)return;
        owner.Drafts[DraftKey]=draft.Text??"";
        string oldSession=QuestionSessionId;binding=next;boundSession=next?.Session;boundQueue=next?.Queue;uploadEpoch++;
        owner.WorkViewChanged(this,oldSession);
        foreach(var view in rendered.Values)view.DisposeImages();rendered.Clear();messages.Children.Clear();queuedEdits.Clear();questionSignature="";
        draft.Text=owner.Drafts.GetValueOrDefault(DraftKey,"");RefreshAttachments();Refresh();
        if(workBindingButton!=null)workBindingButton.Content=new TextBlock{Text=binding==null?TextResources.Get("agent.work.choose"):TextResources.Format("agent.work.current",owner.WorkBindings.Snapshot.Projects.Single(project=>project.Id==binding.ProjectId).Name,binding.Name),TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis,MaxWidth=210};
    }
    private Control CreateWorkBindingActions()
    {
        workBindingButton=Ui.Button(TextResources.Get("agent.work.choose"),()=>owner.Run(ShowWorkPickerAsync));workBindingButton.Name="ChooseWorkConversation";
        var settings=Ui.Button(TextResources.Get("settingsWindow.settingsWindow.text.alternativeE"),()=>
        {
            if(binding==null){owner.ShowSettingsSection("work");return;}
            var dialog=Ui.Window(TextResources.Get("settingsWindow.settingsWindow.text.alternativeE"),new Border(),600,650);dialog.Content=new ScrollViewer{Content=new WorkSettingsView(owner,dialog,binding.ProjectId)};dialog.Show(this);
        });settings.Name="BoundWorkSettings";workJobsButton=Ui.Button(TextResources.Get("agent.jobs.ui.title"),()=>{if(binding!=null)owner.ShowAgentJobs(binding.ProjectId);});workJobsButton.Name="BoundAgentJobs";workJobsButton.IsEnabled=binding!=null;var actions=new WrapPanel();actions.Children.Add(workBindingButton);actions.Children.Add(settings);actions.Children.Add(workJobsButton);return actions;
    }
    private async Task ShowWorkPickerAsync()
    {
        var dialog=new WorkProjectPicker(owner,this,BindWorkAsync);await dialog.ShowDialog(this);
    }
    private void ChangeWorkSetting(Action<PetSettings> change)=>ChangeWorkSettingFor(binding?.ProjectId,change);
    private void ChangeWorkSettingFor(string? projectId,Action<PetSettings> change)
    {
        if(projectId==null){change(owner.Settings);owner.Save();return;}
        var settings=owner.WorkBindings.ProjectSettings(projectId);change(settings);
        if(!owner.WorkBindings.TryChangeProject(projectId,project=>{project.PlanMode=settings.AgentPlanMode;project.Goal=settings.AgentGoal;}))throw new InvalidOperationException(TextResources.Get("agent.jobs.ui.bindingBusy"));
    }
}
