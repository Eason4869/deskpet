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
    private readonly TextBlock notice = Ui.Text("更改将用于下一次任务；保存不会自动发送请求。", 12);
    private readonly TextBox directory;
    public WorkSettingsView(DesktopController controller, Window window)
    {
        owner = controller; host = window; Name = "WorkSettings"; Spacing = 14;
        var settings = owner.Settings; bool supported = OperatingSystem.IsWindows();
        Children.Add(Ui.Text("工作设置", 20)); Children.Add(notice);
        Children.Add(Ui.Text("每条命令确认、MCP 连接及技能配置目前由 Windows 经典界面提供；本跨平台界面尚未提供这些配置入口。系统沙箱保持禁用。", 12));
        if (!supported) Children.Add(Ui.Text("macOS 版暂不支持文件 Agent；专属设置可查看，暂不能使用。", 12));
        var permission = new ComboBox { Name = "AgentAccess", ItemsSource = new[] { "完整访问", "仅工作目录" }, SelectedIndex = settings.AgentFullAccess ? 0 : 1, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch };
        permission.SelectionChanged += (_, _) => { if (permission.SelectedIndex < 0) return; bool full = permission.SelectedIndex == 0; if (settings.AgentFullAccess == full) return; settings.AgentFullAccess = full; Save(); };
        Children.Add(Ui.Field("1. 电脑访问权限", permission)); Children.Add(Ui.Text("完整访问包括桌面等本机目录，可按请求创建文件/文件夹、打开目录；计划模式始终只读。", 12));
        directory = Ui.Input(settings.AgentWorkspace); directory.Name = "AgentWorkspaceInput"; directory.IsEnabled = supported;
        directory.LostFocus += (_, _) => { if ((directory.Text ?? "").Trim() != settings.AgentWorkspace) ApplyDirectory(); };
        var browse = Ui.AsyncButton("更改目录", BrowseDirectoryAsync, owner.Error); browse.Name = "BrowseAgentWorkspace"; browse.IsEnabled = supported;
        var apply = Ui.Button("保存目录", ApplyDirectory); apply.Name = "SaveAgentWorkspace"; apply.IsEnabled = supported;
        var reset = Ui.Button("重置", () => { var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "agent-workspace"); directory.Text = path; if (settings.AgentWorkspace != path) { settings.AgentWorkspace = path; Save(); } notice.Text = "已恢复默认目录；首次工作任务需要时再创建，不会立即创建文件夹。"; }); reset.Name = "ResetAgentWorkspace"; reset.IsEnabled = supported;
        Children.Add(Ui.Field("2. 工作目录", directory)); Children.Add(Ui.Row(browse, apply, reset));
        var plan = Ui.Check("3. 计划模式（只读分析，不写文件）", settings.AgentPlanMode, value => { if (settings.AgentPlanMode == value) return; settings.AgentPlanMode = value; Save(); }); plan.Name = "WorkPlanMode"; plan.IsEnabled = supported; Children.Add(plan);
        var goal = Ui.Input(settings.AgentGoal ?? "", "可留空；设置后不会自动开始任务"); goal.Name = "WorkGoal"; goal.AcceptsReturn = true; goal.MinHeight = 70; goal.TextWrapping = TextWrapping.Wrap; goal.MaxLength = 4000; goal.IsEnabled = supported;
        goal.TextChanged += (_, _) => { var value = (goal.Text ?? "").Trim(); if (settings.AgentGoal == value) return; settings.AgentGoal = value; Save(); }; Children.Add(Ui.Field("4. 持续目标", goal));
        var backup = Ui.Check("5. 写文件前保留原文件备份", settings.AgentBackupFiles, value => { if (settings.AgentBackupFiles == value) return; settings.AgentBackupFiles = value; Save(); }); backup.Name = "AgentBackupFiles"; backup.IsEnabled = supported; Children.Add(backup);
        var openBackup = Ui.AsyncButton("打开备份目录", OpenBackupAsync, owner.Error); openBackup.Name = "OpenAgentBackups"; openBackup.IsEnabled = supported; Children.Add(openBackup);
        Children.Add(Ui.Text("完整访问时，备份在原文件所在目录的 .deskpet-agent-backups；仅工作目录时，备份集中在工作目录中。", 12));
        var context = new NumericUpDown { Name = "AgentContextTokenLimit", Minimum = 8192, Maximum = 2000000, Value = settings.AgentContextTokenLimit, IsEnabled = supported };
        context.ValueChanged += (_, _) => { if (context.Value is not { } value || settings.AgentContextTokenLimit == (int)value) return; settings.AgentContextTokenLimit = (int)value; Save(); };
        Children.Add(Ui.Field("6. 未知模型上下文容量兜底（已知容量跟随模型配置）", context));
        Children.Add(Ui.Text("自动压缩旧上下文；任务持续到完成或手动停止。", 12));
        var detail = Ui.Check("7. 显示详细工作过程", settings.AgentDetailedProcess, value => { if (settings.AgentDetailedProcess == value) return; settings.AgentDetailedProcess = value; Save(); }); detail.Name = "AgentDetailedProcess"; Children.Add(detail);
    }
    private void Save() { owner.Save(); notice.Text = "已保存；将在下一次任务生效，不会自动发送请求。"; notice.Foreground = DesktopTheme.Brush("#687282"); }
    private void ApplyDirectory()
    {
        try
        {
            var value = (directory.Text ?? "").Trim(); if (owner.Settings.AgentWorkspace == value) return; if (!Path.IsPathRooted(value) || !Directory.Exists(value)) throw new InvalidOperationException("请选择已存在的本机目录。");
            value = Path.GetFullPath(value); if (owner.Settings.AgentWorkspace == value) return; owner.Settings.AgentWorkspace = value; directory.Text = value; Save();
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException) { notice.Text = e.Message; notice.Foreground = DesktopTheme.Brush("#A54A37"); }
    }
    private async Task BrowseDirectoryAsync()
    {
        var picked = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择工作目录", AllowMultiple = false });
        if (!host.IsVisible || picked.Count == 0) return; directory.Text = picked[0].TryGetLocalPath() ?? throw new InvalidOperationException("请选择已存在的本机目录。"); ApplyDirectory();
    }
    private async Task OpenBackupAsync()
    {
        var root = owner.Settings.AgentWorkspace;
        if (owner.Settings.AgentFullAccess)
        {
            var picked = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择原文件所在目录以查看备份", AllowMultiple = false });
            if (!host.IsVisible || picked.Count == 0) return; root = picked[0].TryGetLocalPath() ?? throw new InvalidOperationException("请选择本机目录。");
        }
        var path = Path.Combine(root, ".deskpet-agent-backups");
        if (!Directory.Exists(path)) { notice.Text = "此目录目前没有文件备份。"; return; }
        await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
