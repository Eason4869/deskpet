using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Dafeiyu
{
    public sealed class WorkSettingsPanel : UserControl
    {
        const string BackupFolder=".deskpet-agent-backups";
        readonly PetWindow pet;
        readonly ComboBox access;
        readonly TextBox projectName,workspace,goal,contextBudget;
        readonly CheckBox plan,backup,detailed,commandEnabled,predicted;
        readonly TextBox commandTimeout;readonly AgentCommandEditor commandEditor;
        readonly TextBlock notice,permissionDescription;
        PetSettings baseline;ProjectDefinition project;
        public bool HasUnsavedChanges
        {
            get
            {
                return baseline!=null && ((project!=null && projectName.Text.Trim()!=project.Name) || access.SelectedIndex!=(baseline.AgentFullAccess?0:1) || workspace.Text!=(baseline.AgentWorkspace??"") || goal.Text!=(baseline.AgentGoal??"") ||
                    plan.IsChecked!=baseline.AgentPlanMode || backup.IsChecked!=baseline.AgentBackupFiles || detailed.IsChecked!=baseline.AgentDetailedProcess ||
                    commandEnabled.IsChecked!=baseline.AgentCommandsEnabled || commandTimeout.Text!=baseline.AgentCommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture) || commandEditor.HasDraft || commandEditor.Json!=(baseline.AgentCommandRecipes??"[]") || contextBudget.Text!=baseline.AgentContextTokenLimit.ToString(CultureInfo.InvariantCulture) || predicted.IsChecked!=baseline.ShowPredictedMessages);
            }
        }
        public WorkSettingsPanel(PetWindow companion)
        {
            if(companion==null)throw new ArgumentNullException("companion");pet=companion;Name="WorkSettingsPanel";
            Background=System.Windows.Media.Brushes.White;
            var shell=new DockPanel{LastChildFill=true};
            notice=Ui.Text("设置仅影响下一次请求，保存不会启动任务。",11,AgentUi.Muted);notice.Name="WorkSettingsNotice";notice.VerticalAlignment=VerticalAlignment.Center;notice.Margin=new Thickness(0,0,12,0);
            var save=Ui.Button("保存工作设置",true);save.Name="SaveWorkSettings";save.Margin=new Thickness(0);save.Click+=delegate{Save();};
            var footer=new DockPanel{LastChildFill=true,Margin=new Thickness(24,14,24,16)};DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);footer.Children.Add(notice);var footerBorder=new Border{Child=footer,BorderBrush=AgentUi.Line,BorderThickness=new Thickness(0,1,0,0)};DockPanel.SetDock(footerBorder,Dock.Bottom);shell.Children.Add(footerBorder);
            var heading=new StackPanel{Margin=new Thickness(26,24,26,12)};heading.Children.Add(Ui.Text("工作设置",26,AgentUi.Ink));heading.Children.Add(Ui.Text("项目边界、上下文与执行方式",12,AgentUi.Muted));DockPanel.SetDock(heading,Dock.Top);shell.Children.Add(heading);
            var tabs=new WrapPanel{Margin=new Thickness(16,0,16,14)};DockPanel.SetDock(tabs,Dock.Top);shell.Children.Add(tabs);
            var pages=new Grid{Margin=new Thickness(24,0,24,0)};shell.Children.Add(pages);
            var permissions=new StackPanel();permissions.Children.Add(Heading("当前项目"));permissions.Children.Add(Label("项目名称"));projectName=Ui.Field();projectName.Name="ProjectName";projectName.MaxLength=80;permissions.Children.Add(projectName);permissions.Children.Add(Label("工作目录"));workspace=Ui.Field();workspace.Name="AgentWorkspace";workspace.MaxLength=32767;permissions.Children.Add(workspace);
            var directories=new WrapPanel{Margin=new Thickness(0,8,0,10)};var change=AgentUi.Nav("选择文件夹…","ChangeAgentWorkspace");change.Click+=delegate{ChangeDirectory();};directories.Children.Add(change);var reset=AgentUi.Nav("重置默认目录","ResetAgentWorkspace");reset.Click+=delegate{workspace.Text=DefaultWorkspace;notice.Text="默认目录已填入；保存后生效。";};directories.Children.Add(reset);permissions.Children.Add(directories);
            permissions.Children.Add(Label("文件访问范围"));access=new ComboBox{Name="AgentAccessMode",ItemsSource=new[]{"完整访问","仅工作目录"},MinHeight=36,Margin=new Thickness(0,0,0,8)};permissions.Children.Add(access);permissionDescription=Ui.Text("",12,AgentUi.Muted);permissions.Children.Add(permissionDescription);access.SelectionChanged+=delegate{RefreshPermissionDescription();};
            backup=Ui.Toggle("修改前自动备份",true,delegate{});backup.Name="AgentBackupFiles";backup.Margin=new Thickness(0,18,0,4);permissions.Children.Add(backup);var open=AgentUi.Nav("查看文件备份…","OpenAgentBackups");open.HorizontalAlignment=HorizontalAlignment.Left;open.Click+=delegate{OpenBackups();};permissions.Children.Add(open);
            var backupHelp=new Expander{Header=Ui.Text("备份保存在哪里？",11,AgentUi.Muted),Content=Ui.Text("完整访问：原文件旁的 "+BackupFolder+"；仅工作目录：项目目录下的 "+BackupFolder+"。恢复会核对文件与备份，避免覆盖后续改动。",11,AgentUi.Muted),Margin=new Thickness(0,8,0,0)};ChatUi.ProcessExpander(backupHelp);permissions.Children.Add(backupHelp);
            var tasks=new StackPanel();tasks.Children.Add(Heading("任务与上下文"));plan=Ui.Toggle("计划模式 · 只读分析",false,delegate{});plan.Name="AgentPlanMode";tasks.Children.Add(plan);tasks.Children.Add(Ui.Text("只阅读与规划，文件修改和命令执行会被禁用。",12,AgentUi.Muted));
            tasks.Children.Add(Label("持续目标"));goal=Ui.Field("",true);goal.Name="AgentGoal";goal.MaxLength=4000;goal.MinHeight=80;goal.MaxHeight=140;tasks.Children.Add(goal);
            tasks.Children.Add(Label("上下文预算（估算 Token）"));contextBudget=Ui.Field();contextBudget.Name="AgentContextTokenLimit";contextBudget.MaxLength=6;tasks.Children.Add(contextBudget);tasks.Children.Add(Ui.Text("8192–131072，建议小于模型上下文容量。接近预算时自动压缩旧输出和历史，保留当前要求与任务事实。摘要可能产生额外模型请求。",11,AgentUi.Muted));
            tasks.Children.Add(new Border{Child=Ui.Text("自动压缩已启用\n任务持续执行，直到完成或手动停止。费用按当前服务商规则计算。",12,AgentUi.Muted),Background=AgentUi.Soft,Padding=new Thickness(12),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,14,0,12)});
            detailed=Ui.Toggle("展开执行过程",false,delegate{});detailed.Name="AgentDetailedProcess";tasks.Children.Add(detailed);
            predicted=Ui.Toggle("显示预测消息",false,delegate{});predicted.Name="ShowPredictedMessages";tasks.Children.Add(predicted);tasks.Children.Add(Ui.Text("根据最近对话在输入框内显示浅色预测；首次 Enter 确认，再按 Enter 发送。开启会产生额外模型请求，工作或排队期间不会生成。",11,AgentUi.Muted));
            var commands=new StackPanel();commands.Children.Add(Heading("项目命令"));commandEnabled=Ui.Toggle("允许执行已配置命令",false,delegate{});commandEnabled.Name="AgentCommandsEnabled";commands.Children.Add(commandEnabled);commands.Children.Add(Ui.Text("仅运行预先配置且绑定当前项目的命令。使用当前 Windows 账户权限，项目目录不是系统沙箱。",12,AgentUi.Muted));commands.Children.Add(Label("单条命令超时（1–600 秒）"));commandTimeout=Ui.Field();commandTimeout.Name="AgentCommandTimeoutSeconds";commands.Children.Add(commandTimeout);commandEditor=new AgentCommandEditor(()=>workspace.Text);commands.Children.Add(commandEditor);
            var panels=new[]{permissions,tasks,commands};var labels=new[]{"项目与权限","任务与上下文","项目命令"};var buttons=new System.Collections.Generic.List<Button>();
            for(int i=0;i<panels.Length;i++){int index=i;var page=new ScrollViewer{Content=AgentUi.Card(panels[i]),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Visibility=i==0?Visibility.Visible:Visibility.Collapsed,Padding=new Thickness(0,0,5,0)};pages.Children.Add(page);var tab=AgentUi.Nav(labels[i],"WorkSettingsSection"+i);tab.Margin=new Thickness(4,0,4,0);buttons.Add(tab);tabs.Children.Add(tab);tab.Click+=delegate{for(int n=0;n<pages.Children.Count;n++){pages.Children[n].Visibility=n==index?Visibility.Visible:Visibility.Collapsed;buttons[n].Background=n==index?AgentUi.Soft:System.Windows.Media.Brushes.Transparent;buttons[n].FontWeight=n==index?FontWeights.SemiBold:FontWeights.Normal;}};}
            buttons[0].Background=AgentUi.Soft;buttons[0].FontWeight=FontWeights.SemiBold;Content=shell;RefreshState();
        }
        static string DefaultWorkspace {get{return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace"));}}
        static TextBlock Heading(string text){var value=Ui.Text(text,17);value.FontWeight=FontWeights.SemiBold;value.Margin=new Thickness(0,0,0,8);return value;}
        static TextBlock Label(string text){var value=Ui.Text(text,12,Ui.Muted);value.Margin=new Thickness(0,10,0,5);return value;}
        static Border Card(UIElement content){var card=Ui.Card(content);card.Background=System.Windows.Media.Brushes.White;card.Margin=new Thickness(0,16,0,0);return card;}
        void RefreshPermissionDescription()
        {
            if(permissionDescription==null)return;
            permissionDescription.Text=access.SelectedIndex==0?"使用当前 Windows 账户已有的文件权限，可访问桌面、用户目录与磁盘根目录；不会申请管理员权限或提权。":"文件工具仅访问所选工作目录。请选择具体项目目录；不接受磁盘根目录、整个用户目录或系统目录。";
        }
        public void RefreshState()
        {
            if(HasUnsavedChanges)return;
            project=pet.CurrentEditingProject;baseline=project==null?pet.Settings.Copy():ProjectSettings(project);projectName.Text=project==null?"全局默认设置":project.Name;projectName.IsEnabled=project!=null;access.SelectedIndex=baseline.AgentFullAccess?0:1;workspace.Text=baseline.AgentWorkspace??"";goal.Text=baseline.AgentGoal??"";
            commandEnabled.IsChecked=baseline.AgentCommandsEnabled;commandTimeout.Text=baseline.AgentCommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture);commandEditor.Load(baseline.AgentCommandRecipes);baseline.AgentCommandRecipes=commandEditor.Json;
            plan.IsChecked=baseline.AgentPlanMode;backup.IsChecked=baseline.AgentBackupFiles;detailed.IsChecked=baseline.AgentDetailedProcess;
            contextBudget.Text=baseline.AgentContextTokenLimit.ToString(CultureInfo.InvariantCulture);predicted.IsChecked=baseline.ShowPredictedMessages;RefreshPermissionDescription();
        }
        public bool Save()
        {
            PetSettings candidate;string projectNameValue;
            try
            {
                if(access.SelectedIndex!=0 && access.SelectedIndex!=1)throw new InvalidOperationException("请选择完整访问或仅工作目录。");
                int contextTokens;if(!Int32.TryParse(contextBudget.Text,NumberStyles.None,CultureInfo.InvariantCulture,out contextTokens) || contextTokens<8192 || contextTokens>131072)throw new InvalidOperationException("上下文预算应为 8192–131072 的整数。");
                string target=ValidateDirectory(workspace.Text,access.SelectedIndex==1);
                if(goal.Text.Length>4000)throw new InvalidOperationException("持续目标不能超过 4000 个字符，请缩短后保存。");
                int commandSeconds;if(!Int32.TryParse(commandTimeout.Text,out commandSeconds) || commandSeconds<1 || commandSeconds>600)throw new InvalidOperationException("命令超时应为 1～600 的整数秒。");
                if(!commandEditor.CommitDraft())throw new InvalidOperationException("项目命令草稿无效，请检查命令名称、程序路径和参数。");projectNameValue=projectName.Text.Trim();if(project!=null && (projectNameValue.Length<1 || projectNameValue.Length>80))throw new InvalidOperationException("项目名称应为 1～80 个字符。");
                candidate=pet.Settings.Copy();candidate.AgentCommandsEnabled=commandEnabled.IsChecked==true;candidate.AgentCommandRecipes=commandEditor.Json;candidate.AgentCommandTimeoutSeconds=commandSeconds;
                if(candidate.AgentCommandsEnabled){if(!Directory.Exists(target))throw new InvalidOperationException("执行命令前请选择已存在的项目目录。");new AgentWorkspace(target,access.SelectedIndex==1).ConfigureCommands(true,candidate.AgentCommandRecipes);}
                candidate.AgentFullAccess=access.SelectedIndex==0;candidate.AgentWorkspace=target;candidate.AgentPlanMode=plan.IsChecked==true;
                candidate.AgentGoal=goal.Text.Trim();candidate.AgentBackupFiles=backup.IsChecked==true;candidate.AgentContextTokenLimit=contextTokens;candidate.ShowPredictedMessages=predicted.IsChecked==true;candidate.AgentDetailedProcess=detailed.IsChecked==true;
            }
            catch(Exception e){if(!ValidationError(e))throw;notice.Text=e.Message;return false;}
            var previous=pet.Settings.Copy();var previousProject=project==null?null:project.Copy();
            try
            {
                if(project!=null){project.Name=projectNameValue;ApplyProjectFields(candidate,project);pet.ProjectStore.Save(pet.Projects);}else{ApplyWorkFields(candidate,pet.Settings);pet.SaveSettings();}
                if(project==null && !String.IsNullOrEmpty(pet.Notice)){ApplyWorkFields(previous,pet.Settings);notice.Text=pet.Notice;return false;}
                // Establish the clean baseline before refreshing other views, which may re-enter this panel.
                baseline=null;RefreshState();pet.RefreshChatAppearance();notice.Text="工作设置已保存，从下一次请求生效。";return true;
            }
            catch(Exception e){if(project==null)ApplyWorkFields(previous,pet.Settings);else if(previousProject!=null){project.Name=previousProject.Name;ApplyProjectFields(ProjectSettings(previousProject),project);}if(!ValidationError(e))throw;notice.Text="保存未完成："+e.Message;return false;}
        }
        static PetSettings ProjectSettings(ProjectDefinition value){var result=new PetSettings();result.AgentCommandsEnabled=value.CommandsEnabled;result.AgentCommandRecipes=value.CommandRecipes;result.AgentCommandTimeoutSeconds=value.CommandTimeoutSeconds;result.AgentFullAccess=value.FullAccess;result.AgentWorkspace=value.Workspace;result.AgentPlanMode=value.PlanMode;result.AgentGoal=value.Goal;result.AgentBackupFiles=value.BackupFiles;result.AgentContextTokenLimit=value.ContextTokenLimit;return result;}
        static void ApplyProjectFields(PetSettings from,ProjectDefinition to){to.CommandsEnabled=from.AgentCommandsEnabled;to.CommandRecipes=from.AgentCommandRecipes;to.CommandTimeoutSeconds=from.AgentCommandTimeoutSeconds;to.FullAccess=from.AgentFullAccess;to.Workspace=from.AgentWorkspace;to.PlanMode=from.AgentPlanMode;to.Goal=from.AgentGoal;to.BackupFiles=from.AgentBackupFiles;to.ContextTokenLimit=from.AgentContextTokenLimit;}
        static void ApplyWorkFields(PetSettings from,PetSettings to)
        {
            to.AgentCommandsEnabled=from.AgentCommandsEnabled;to.AgentCommandRecipes=from.AgentCommandRecipes;to.AgentCommandTimeoutSeconds=from.AgentCommandTimeoutSeconds;
            to.AgentFullAccess=from.AgentFullAccess;to.AgentWorkspace=from.AgentWorkspace;to.AgentPlanMode=from.AgentPlanMode;to.AgentGoal=from.AgentGoal;
            to.AgentBackupFiles=from.AgentBackupFiles;to.AgentContextTokenLimit=from.AgentContextTokenLimit;to.ShowPredictedMessages=from.ShowPredictedMessages;to.AgentDetailedProcess=from.AgentDetailedProcess;
        }
        static bool ValidationError(Exception e){return e is ArgumentException || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is System.Security.SecurityException;}
        static string ValidateDirectory(string text,bool restricted)
        {
            text=(text??"").Trim();if(text.Length==0 || !Path.IsPathRooted(text))throw new InvalidOperationException("请输入已存在文件夹的完整绝对路径。");
            string path=Path.GetFullPath(text);
            if(!Directory.Exists(path))
            {
                if(String.Equals(path.TrimEnd('\\','/'),DefaultWorkspace.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))return DefaultWorkspace;
                throw new InvalidOperationException("默认工作目录不存在，请选择已存在的文件夹；重置的程序默认目录会在首次执行时创建。");
            }
            return new AgentWorkspace(path,restricted).Root;
        }
        void ChangeDirectory()
        {
            try{string selected=PickFolder("选择默认工作目录",workspace.Text);if(selected!=null){workspace.Text=ValidateDirectory(selected,access.SelectedIndex==1);notice.Text="目录已更改，点击保存工作设置后生效。";}}
            catch(Exception e){if(!ValidationError(e))throw;notice.Text=e.Message;}
        }
        string PickFolder(string description,string initial)
        {
            using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description=description,SelectedPath=Directory.Exists(initial)?initial:"",ShowNewFolderButton=false})
            {
                var window=Window.GetWindow(this);var result=window==null?picker.ShowDialog():picker.ShowDialog(new FolderOwner(window));
                return result==System.Windows.Forms.DialogResult.OK?picker.SelectedPath:null;
            }
        }
        void OpenBackups()
        {
            try
            {
                var settings=pet.Settings.Copy();string parent;
                if(settings.AgentFullAccess){parent=PickFolder("选择原文件所在的文件夹，以查看其中的自动备份",settings.AgentWorkspace);if(parent==null)return;}
                else parent=ValidateDirectory(settings.AgentWorkspace,true);
                string path=Path.Combine(parent,BackupFolder);
                if(!Directory.Exists(path)){notice.Text="此文件夹尚无自动备份："+path;return;}
                Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});notice.Text="已打开备份文件夹。";
            }
            catch(Exception e){if(!(ValidationError(e) || e is System.ComponentModel.Win32Exception))throw;notice.Text="无法打开备份文件夹："+e.Message;}
        }
        sealed class FolderOwner:System.Windows.Forms.IWin32Window
        {
            readonly IntPtr handle;public FolderOwner(Window window){handle=new WindowInteropHelper(window).Handle;}public IntPtr Handle{get{return handle;}}
        }
    }
}
