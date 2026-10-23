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
    private readonly TextBlock notice = Ui.Text(TextResources.Get("workSettingsView.notice.text"), 12);
    private readonly TextBox directory;
    public WorkSettingsView(DesktopController controller, Window window)
    {
        owner = controller; host = window; Name = "WorkSettings"; Spacing = 14;
        var settings = owner.Settings; bool supported = OperatingSystem.IsWindows();
        Children.Add(Ui.Text(TextResources.Get("workSettingsView.workSettingsView.text"), 20)); Children.Add(notice);
        Children.Add(Ui.Text(TextResources.Get("workSettingsView.workSettingsView.text.alternativeA"), 12));
        if (!supported) Children.Add(Ui.Text(TextResources.Get("workSettingsView.workSettingsView.text.alternativeB"), 12));
        var permission = new ComboBox { Name = "AgentAccess", ItemsSource = new[] { TextResources.Get("workSettingsView.workSettingsView.itemsSource"), TextResources.Get("workSettingsView.workSettingsView.itemsSource.alternativeA") }, SelectedIndex = settings.AgentFullAccess ? 0 : 1, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch };
        permission.SelectionChanged += (_, _) => { if (permission.SelectedIndex < 0) return; bool full = permission.SelectedIndex == 0; if (settings.AgentFullAccess == full) return; settings.AgentFullAccess = full; Save(); };
        Children.Add(Ui.Field(TextResources.Get("workSettingsView.workSettingsView.field"), permission)); Children.Add(Ui.Text(TextResources.Get("workSettingsView.workSettingsView.text.alternativeC"), 12));
        directory = Ui.Input(settings.AgentWorkspace); directory.Name = "AgentWorkspaceInput"; directory.IsEnabled = supported;
        directory.LostFocus += (_, _) => { if ((directory.Text ?? "").Trim() != settings.AgentWorkspace) ApplyDirectory(); };
        var browse = Ui.AsyncButton(TextResources.Get("workSettingsView.workSettingsView.asyncButton"), BrowseDirectoryAsync, owner.Error); browse.Name = "BrowseAgentWorkspace"; browse.IsEnabled = supported;
        var apply = Ui.Button(TextResources.Get("workSettingsView.workSettingsView.text.alternativeD"), ApplyDirectory); apply.Name = "SaveAgentWorkspace"; apply.IsEnabled = supported;
        var reset = Ui.Button(TextResources.Get("workSettingsView.workSettingsView.text.alternativeE"), () => { var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "agent-workspace"); directory.Text = path; if (settings.AgentWorkspace != path) { settings.AgentWorkspace = path; Save(); } notice.Text = TextResources.Get("workSettingsView.workSettingsView.notice"); }); reset.Name = "ResetAgentWorkspace"; reset.IsEnabled = supported;
        Children.Add(Ui.Field(TextResources.Get("workSettingsView.workSettingsView.field.alternativeA"), directory)); Children.Add(Ui.Row(browse, apply, reset));
        var plan = Ui.Check(TextResources.Get("workSettingsView.workSettingsView.check"), settings.AgentPlanMode, value => { if (settings.AgentPlanMode == value) return; settings.AgentPlanMode = value; Save(); }); plan.Name = "WorkPlanMode"; plan.IsEnabled = supported; Children.Add(plan);
        var goal = Ui.Input(settings.AgentGoal ?? "", TextResources.Get("workSettingsView.workSettingsView.input")); goal.Name = "WorkGoal"; goal.AcceptsReturn = true; goal.MinHeight = 70; goal.TextWrapping = TextWrapping.Wrap; goal.MaxLength = 4000; goal.IsEnabled = supported;
        goal.TextChanged += (_, _) => { var value = (goal.Text ?? "").Trim(); if (settings.AgentGoal == value) return; settings.AgentGoal = value; Save(); }; Children.Add(Ui.Field(TextResources.Get("workSettingsView.workSettingsView.field.alternativeB"), goal));
        var backup = Ui.Check(TextResources.Get("workSettingsView.workSettingsView.check.alternativeA"), settings.AgentBackupFiles, value => { if (settings.AgentBackupFiles == value) return; settings.AgentBackupFiles = value; Save(); }); backup.Name = "AgentBackupFiles"; backup.IsEnabled = supported; Children.Add(backup);
        var openBackup = Ui.AsyncButton(TextResources.Get("workSettingsView.workSettingsView.asyncButton.alternativeA"), OpenBackupAsync, owner.Error); openBackup.Name = "OpenAgentBackups"; openBackup.IsEnabled = supported; Children.Add(openBackup);
        Children.Add(Ui.Text(TextResources.Get("workSettingsView.workSettingsView.text.alternativeF"), 12));
        var context = new NumericUpDown { Name = "AgentContextTokenLimit", Minimum = 8192, Maximum = 2000000, Value = settings.AgentContextTokenLimit, IsEnabled = supported };
        context.ValueChanged += (_, _) => { if (context.Value is not { } value || settings.AgentContextTokenLimit == (int)value) return; settings.AgentContextTokenLimit = (int)value; Save(); };
        Children.Add(Ui.Field(TextResources.Get("workSettingsView.workSettingsView.field.alternativeC"), context));
        Children.Add(Ui.Text(TextResources.Get("workSettingsView.workSettingsView.text.alternativeG"), 12));
        var detail = Ui.Check(TextResources.Get("workSettingsView.workSettingsView.check.alternativeB"), settings.AgentDetailedProcess, value => { if (settings.AgentDetailedProcess == value) return; settings.AgentDetailedProcess = value; Save(); }); detail.Name = "AgentDetailedProcess"; Children.Add(detail);
    }
    private void Save() { owner.Save(); notice.Text = TextResources.Get("workSettingsView.save.notice"); notice.Foreground = DesktopTheme.Brush("#687282"); }
    private void ApplyDirectory()
    {
        try
        {
            var value = (directory.Text ?? "").Trim(); if (owner.Settings.AgentWorkspace == value) return; if (!Path.IsPathRooted(value) || !Directory.Exists(value)) throw new InvalidOperationException(TextResources.Get("workSettingsView.applyDirectory.text"));
            value = Path.GetFullPath(value); if (owner.Settings.AgentWorkspace == value) return; owner.Settings.AgentWorkspace = value; directory.Text = value; Save();
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException) { notice.Text = e.Message; notice.Foreground = DesktopTheme.Brush("#A54A37"); }
    }
    private async Task BrowseDirectoryAsync()
    {
        var picked = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = TextResources.Get("workSettingsView.browseDirectoryAsync.title"), AllowMultiple = false });
        if (!host.IsVisible || picked.Count == 0) return; directory.Text = picked[0].TryGetLocalPath() ?? throw new InvalidOperationException(TextResources.Get("workSettingsView.browseDirectoryAsync.directoryPickedTryGetLocalPath")); ApplyDirectory();
    }
    private async Task OpenBackupAsync()
    {
        var root = owner.Settings.AgentWorkspace;
        if (owner.Settings.AgentFullAccess)
        {
            var picked = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = TextResources.Get("workSettingsView.openBackupAsync.title"), AllowMultiple = false });
            if (!host.IsVisible || picked.Count == 0) return; root = picked[0].TryGetLocalPath() ?? throw new InvalidOperationException(TextResources.Get("workSettingsView.openBackupAsync.rootPickedTryGetLocalPath"));
        }
        var path = Path.Combine(root, ".deskpet-agent-backups");
        if (!Directory.Exists(path)) { notice.Text = TextResources.Get("workSettingsView.openBackupAsync.notice"); return; }
        await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
