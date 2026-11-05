using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class WorkSettingsView : StackPanel
{
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly TextBlock notice = LocalizedUi.Text(()=>(TextResources.Get("workSettingsView.notice.text")), 12);
    private readonly TextBox directory;
    private PetSettings settings;
    private readonly string? projectId;
    public WorkSettingsView(DesktopController controller, Window window,string? projectId=null)
    {
        owner = controller; host = window; Name = "WorkSettings"; Spacing = 36; MaxWidth=900;
        this.projectId=projectId;settings=projectId==null?owner.Settings:owner.WorkBindings.ProjectSettings(projectId); bool supported = OperatingSystem.IsWindows();
        var scope=SettingsVisual.Text(()=>projectId==null?TextResources.Get("settingsLayout.globalDefaults"):TextResources.Format("settingsLayout.currentProject",owner.WorkBindings.Snapshot.Projects.FirstOrDefault(p=>p.Id==projectId)?.Name??projectId),13,true);scope.Name="WorkSettingsScope";
        var header=Ui.Column(LocalizedUi.Text(()=>TextResources.Get("settingsLayout.workTitle"),20),scope,
            SettingsVisual.Text(()=>TextResources.Get("settingsLayout.workInstantHint"),12,true),notice,
            SettingsVisual.Text(()=>TextResources.Get("workSettingsView.workSettingsView.text.alternativeA"),12,true));
        if(!supported)header.Children.Add(SettingsVisual.Text(()=>TextResources.Get("workSettingsView.workSettingsView.text.alternativeB"),12,true));
        Children.Add(header);
        var permissions=Ui.Column();var workspace=Ui.Column();var terminals=Ui.Column();var task=Ui.Column();var backups=Ui.Column();var budget=Ui.Column();
        var permission = LocalizedUi.Options(new ComboBox { Name = "AgentAccess", ItemsSource = new[] { TextResources.Get("workSettingsView.workSettingsView.itemsSource"), TextResources.Get("workSettingsView.workSettingsView.itemsSource.alternativeA") }, SelectedIndex = settings.AgentFullAccess ? 0 : 1, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch },()=>(new[] { TextResources.Get("workSettingsView.workSettingsView.itemsSource"), TextResources.Get("workSettingsView.workSettingsView.itemsSource.alternativeA") }));
        permission.SelectionChanged += (_, _) => { if (permission.SelectedIndex < 0) return; bool full = permission.SelectedIndex == 0; if (settings.AgentFullAccess == full) return; settings.AgentFullAccess = full;settings.AgentApprovalMode=full?AgentApprovalMode.Full:AgentApprovalMode.Auto;settings.AgentSandboxEnabled=!full; Save(); };
        permissions.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeK"),()=>TextResources.Get("workSettingsView.workSettingsView.text.alternativeC"),permission));
        directory = Ui.Input(settings.AgentWorkspace); directory.Name = "AgentWorkspaceInput"; directory.IsEnabled = supported;
        directory.LostFocus += (_, _) => { if ((directory.Text ?? "").Trim() != settings.AgentWorkspace) ApplyDirectory(); };
        var browse = LocalizedUi.AsyncButton(()=>(TextResources.Get("workSettingsView.workSettingsView.asyncButton")), BrowseDirectoryAsync, owner.Error); browse.Name = "BrowseAgentWorkspace"; browse.IsEnabled = supported;
        var apply = LocalizedUi.Button(()=>(TextResources.Get("workSettingsView.workSettingsView.text.alternativeD")), ApplyDirectory); apply.Name = "SaveAgentWorkspace"; apply.IsEnabled = supported;
        var reset = LocalizedUi.Button(()=>(TextResources.Get("workSettingsView.workSettingsView.text.alternativeE")), () => { var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "agent-workspace"); directory.Text = path; if (settings.AgentWorkspace != path) { settings.AgentWorkspace = path; Save(); } LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsView.workSettingsView.notice"))); }); reset.Name = "ResetAgentWorkspace"; reset.IsEnabled = supported;
        workspace.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeC"),()=>"",directory)); workspace.Children.Add(SettingsVisual.Wrap(browse, apply, reset));
        var terminal=LocalizedUi.Options(new ComboBox{Name="AgentTerminalPolicy",ItemsSource=new[]{TextResources.Get("common.close"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeE")},SelectedIndex=settings.AgentTerminalPolicy==AgentTerminalPolicy.ConfirmEach?1:0,IsEnabled=supported,HorizontalAlignment=HorizontalAlignment.Stretch},()=>(new[]{TextResources.Get("common.close"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeE")}));
        terminal.SelectionChanged+=(_,_)=>{if(terminal.SelectedIndex<0)return;var value=terminal.SelectedIndex==1?AgentTerminalPolicy.ConfirmEach:AgentTerminalPolicy.Disabled;if(settings.AgentTerminalPolicy==value)return;settings.AgentTerminalPolicy=value;Save();};
        terminals.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeM"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeN"),terminal));
        var syncLimit=new NumericUpDown{Name="AgentCommandTimeoutSeconds",Minimum=1,Maximum=600,Value=settings.AgentCommandTimeoutSeconds,IsEnabled=supported};
        syncLimit.ValueChanged+=(_,_)=>{if(syncLimit.Value is not {} value||settings.AgentCommandTimeoutSeconds==(int)value)return;settings.AgentCommandTimeoutSeconds=(int)value;Save();};terminals.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("agent.jobs.ui.syncLimit"),()=>"",syncLimit));
        var backgroundLimit=new NumericUpDown{Name="AgentBackgroundJobTimeoutSeconds",Minimum=1,Maximum=28800,Value=settings.AgentBackgroundJobTimeoutSeconds,IsEnabled=supported};
        backgroundLimit.ValueChanged+=(_,_)=>{if(backgroundLimit.Value is not {} value||settings.AgentBackgroundJobTimeoutSeconds==(int)value)return;settings.AgentBackgroundJobTimeoutSeconds=(int)value;Save();};terminals.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("agent.jobs.ui.backgroundLimit"),()=>"",backgroundLimit));
        var plan = LocalizedUi.Check(()=>"", settings.AgentPlanMode, value => { if (settings.AgentPlanMode == value) return; settings.AgentPlanMode = value; Save(); }); plan.Name = "WorkPlanMode"; LocalizedUi.Automation(plan,()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeO")); plan.IsEnabled = supported; task.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeO"),()=>"",plan));
        var goal = LocalizedUi.Input(settings.AgentGoal ?? "", ()=>(TextResources.Get("workSettingsView.workSettingsView.input"))); goal.Name = "WorkGoal"; goal.AcceptsReturn = true; goal.MinHeight = 70; goal.TextWrapping = TextWrapping.Wrap; goal.MaxLength = 4000; goal.IsEnabled = supported;
        goal.TextChanged += (_, _) => { var value = (goal.Text ?? "").Trim(); if (settings.AgentGoal == value) return; settings.AgentGoal = value; Save(); }; task.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeZ"),()=>"",goal));
        var backup = LocalizedUi.Check(()=>"", settings.AgentBackupFiles, value => { if (settings.AgentBackupFiles == value) return; settings.AgentBackupFiles = value; Save(); }); backup.Name = "AgentBackupFiles"; LocalizedUi.Automation(backup,()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeW")); backup.IsEnabled = supported; backups.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeW"),()=>TextResources.Get("workSettingsView.workSettingsView.text.alternativeF"),backup));
        var openBackup = LocalizedUi.AsyncButton(()=>(TextResources.Get("workSettingsView.workSettingsView.asyncButton.alternativeA")), OpenBackupAsync, owner.Error); openBackup.Name = "OpenAgentBackups"; openBackup.IsEnabled = supported; backups.Children.Add(openBackup);
        var context = new NumericUpDown { Name = "AgentContextTokenLimit", Minimum = 8192, Maximum = 2000000, Value = settings.AgentContextTokenLimit, IsEnabled = supported };
        context.ValueChanged += (_, _) => { if (context.Value is not { } value || settings.AgentContextTokenLimit == (int)value) return; settings.AgentContextTokenLimit = (int)value; Save(); };
        budget.Children.Add(SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAD"),()=>TextResources.Get("workSettingsView.workSettingsView.text.alternativeG"),context));
        Children.Add(SettingsVisual.Group(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeK"),permissions));
        Children.Add(SettingsVisual.Group(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeC"),workspace));
        Children.Add(SettingsVisual.Group(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeM"),terminals));
        Children.Add(SettingsVisual.Group(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeZ"),task));
        Children.Add(SettingsVisual.Group(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeW"),backups));
        Children.Add(SettingsVisual.Group(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAD"),budget));
    }
    private void Save()
    {
        try
        {
            if(projectId==null)owner.Save();
            else if(!owner.WorkBindings.TryChangeProject(projectId,project=>{project.FullAccess=settings.AgentFullAccess;project.ApprovalMode=settings.AgentApprovalMode;project.SandboxEnabled=settings.AgentSandboxEnabled;project.Workspace=settings.AgentWorkspace;project.PlanMode=settings.AgentPlanMode;project.Goal=settings.AgentGoal;project.BackupFiles=settings.AgentBackupFiles;project.ContextTokenLimit=settings.AgentContextTokenLimit;project.TerminalPolicy=settings.AgentTerminalPolicy;project.CommandTimeoutSeconds=settings.AgentCommandTimeoutSeconds;project.BackgroundJobTimeoutSeconds=settings.AgentBackgroundJobTimeoutSeconds;}))throw new InvalidOperationException(TextResources.Get("agent.jobs.ui.bindingBusy"));
            LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsView.save.notice")));notice.Foreground=DesktopTheme.Brush("#687282");
        }
        catch(Exception error){if(projectId!=null)settings=owner.WorkBindings.ProjectSettings(projectId);notice.Text=AgentJobUiText.Error(error);notice.Foreground=DesktopTheme.Brush("#A54A37");}
    }
    private void ApplyDirectory()
    {
        try
        {
            var value = (directory.Text ?? "").Trim(); if (settings.AgentWorkspace == value) return; if (!Path.IsPathRooted(value) || !Directory.Exists(value)) throw new InvalidOperationException(TextResources.Get("workSettingsView.applyDirectory.text"));
            value = Path.GetFullPath(value); if (settings.AgentWorkspace == value) return; settings.AgentWorkspace = value; directory.Text = value; Save();
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException) { notice.Text = e.Message; notice.Foreground = DesktopTheme.Brush("#A54A37"); }
    }
    private async Task BrowseDirectoryAsync()
    {
        var picked = await host.StorageProvider.OpenFolderPickerAsync(LocalizedUi.With(new FolderPickerOpenOptions { Title = TextResources.Get("workSettingsView.browseDirectoryAsync.title"), AllowMultiple = false },"Title",()=>(TextResources.Get("workSettingsView.browseDirectoryAsync.title"))));
        if (!host.IsVisible || picked.Count == 0) return; directory.Text = picked[0].TryGetLocalPath() ?? throw new InvalidOperationException(TextResources.Get("workSettingsView.browseDirectoryAsync.directoryPickedTryGetLocalPath")); ApplyDirectory();
    }
    private async Task OpenBackupAsync()
    {
        var root = settings.AgentWorkspace;
        if (settings.AgentFullAccess)
        {
            var picked = await host.StorageProvider.OpenFolderPickerAsync(LocalizedUi.With(new FolderPickerOpenOptions { Title = TextResources.Get("workSettingsView.openBackupAsync.title"), AllowMultiple = false },"Title",()=>(TextResources.Get("workSettingsView.openBackupAsync.title"))));
            if (!host.IsVisible || picked.Count == 0) return; root = picked[0].TryGetLocalPath() ?? throw new InvalidOperationException(TextResources.Get("workSettingsView.openBackupAsync.rootPickedTryGetLocalPath"));
        }
        var path = Path.Combine(root, ".deskpet-agent-backups");
        if (!Directory.Exists(path)) { LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsView.openBackupAsync.notice"))); return; }
        await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
