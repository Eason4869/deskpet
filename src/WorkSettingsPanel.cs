using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
        readonly ComboBox terminal,approvalMode;readonly CheckBox sandbox;readonly TextBox sandboxTools;bool loadingPermissions,permissionsEdited,fullAccessConfirmed;
        readonly CheckBox trustInstructions;readonly TextBox globalInstructions,skillsDirectory;
        readonly TextBox projectName,workspace,goal,contextBudget;
        readonly CheckBox plan,backup,detailed,predicted,plainText,showContext;readonly ComboBox sendShortcut,followUp;
        readonly TextBlock scopeLabel,contextSummary;readonly FrameworkElement fallbackRow;
        readonly TextBlock notice,permissionDescription;readonly TextBox workPrompt;string promptBaseline;bool promptVisible;
        PetSettings baseline;ProjectDefinition project;
        AgentApprovalMode SelectedApprovalMode {get{return baseline!=null&&baseline.AgentApprovalMode==AgentApprovalMode.Legacy&&!permissionsEdited?AgentApprovalMode.Legacy:(AgentApprovalMode)(approvalMode.SelectedIndex+1);}}
        public bool HasUnsavedChanges
        {
            get
            {
                return baseline!=null && (SelectedApprovalMode!=baseline.AgentApprovalMode || sandbox.IsChecked!=baseline.AgentSandboxEnabled || sandboxTools.Text!=(baseline.AgentSandboxReadDirectories??"") || workPrompt.Text!=promptBaseline || (project!=null && projectName.Text.Trim()!=project.Name) || access.SelectedIndex!=(baseline.AgentFullAccess?0:1) || workspace.Text!=(baseline.AgentWorkspace??"") || goal.Text!=(baseline.AgentGoal??"") ||
                    plan.IsChecked!=baseline.AgentPlanMode || backup.IsChecked!=baseline.AgentBackupFiles || terminal.SelectedIndex!=(baseline.AgentTerminalPolicy==AgentTerminalPolicy.ConfirmEach?1:0) || trustInstructions.IsChecked!=baseline.AgentTrustProjectInstructions || globalInstructions.Text!=(baseline.AgentGlobalInstructions??"") || skillsDirectory.Text!=(baseline.AgentSkillsDirectory??"") || (project==null && detailed.IsChecked!=baseline.AgentDetailedProcess) ||
                    contextBudget.Text!=baseline.AgentContextTokenLimit.ToString(CultureInfo.InvariantCulture) || (project==null && (predicted.IsChecked!=baseline.ShowPredictedMessages || plainText.IsChecked!=baseline.PlainTextEditor || showContext.IsChecked!=baseline.ShowContextUsage || sendShortcut.SelectedIndex!=(baseline.SendOnEnter?0:1) || followUp.SelectedIndex!=(baseline.FollowUpBehavior=="steer"?1:0))));
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
            var heading=new StackPanel{Margin=new Thickness(26,24,26,16)};heading.Children.Add(Ui.Text("工作设置",26,AgentUi.Ink));scopeLabel=Ui.Text("",12,AgentUi.Muted);scopeLabel.Name="WorkSettingsScope";scopeLabel.Margin=new Thickness(0,7,0,0);heading.Children.Add(scopeLabel);DockPanel.SetDock(heading,Dock.Top);shell.Children.Add(heading);
            var tabs=new WrapPanel{Margin=new Thickness(26,0,26,12)};var generalTab=AgentUi.Nav("常规设置","WorkSettingsGeneralTab");var promptTab=AgentUi.Nav("工作提示词","WorkSettingsPromptTab");tabs.Children.Add(generalTab);tabs.Children.Add(promptTab);DockPanel.SetDock(tabs,Dock.Top);shell.Children.Add(tabs);
            var content=new StackPanel{Margin=new Thickness(26,0,26,24)};
            var location=new StackPanel();projectName=Ui.Field();projectName.Name="ProjectName";projectName.MaxLength=80;location.Children.Add(SettingRow("项目名称","在侧栏显示的名称。",projectName));
            workspace=Ui.Field();workspace.Name="AgentWorkspace";workspace.MaxLength=32767;var directory=new StackPanel();directory.Children.Add(workspace);var directoryActions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};var change=AgentUi.Nav("选择文件夹…","ChangeAgentWorkspace");change.Click+=delegate{ChangeDirectory();};directoryActions.Children.Add(change);var reset=AgentUi.Nav("重置默认","ResetAgentWorkspace");reset.Click+=delegate{workspace.Text=DefaultWorkspace;notice.Text="默认目录已填入，保存后生效。";};directoryActions.Children.Add(reset);directory.Children.Add(directoryActions);location.Children.Add(SettingRow("工作目录","相对路径的起点；限制访问时也作为文件边界。",directory));AddGroup(content,"项目",location);
            var permissions=new StackPanel();approvalMode=new ComboBox{Name="AgentApprovalMode",ItemsSource=new[]{"请求批准","帮我批准","完全访问权限"},MinHeight=36};permissions.Children.Add(SettingRow("审批模式","请求批准：越界操作由你确认；帮我批准：独立审核，不确定时转人工；完全访问：以当前账户权限执行。",approvalMode));sandbox=Ui.Toggle("",false,delegate{});sandbox.Name="AgentSandboxEnabled";permissions.Children.Add(SettingRow("工作区沙箱","Windows AppContainer 限制终端及子进程访问项目文件，终端网络默认阻止。启动失败将停止执行，不会回退到无隔离。",sandbox));sandboxTools=Ui.Field("",true);sandboxTools.Name="SandboxReadDirectories";sandboxTools.MinHeight=52;sandboxTools.MaxHeight=110;permissions.Children.Add(SettingRow("只读工具目录","可选，每行一个工具安装目录，例如用户目录中的 Python。仅授予读取和运行，命令仍不能写入这些目录。",sandboxTools));approvalMode.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e){
                if(loadingPermissions)return;
                if(approvalMode.SelectedIndex==2){
                    if(!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this))){loadingPermissions=true;approvalMode.SelectedIndex=e.RemovedItems.Count==0?1:approvalMode.Items.IndexOf(e.RemovedItems[0]);loadingPermissions=false;return;}
                    fullAccessConfirmed=true;
                }else fullAccessConfirmed=false;
                permissionsEdited=true;access.IsEnabled=false;sandbox.IsChecked=approvalMode.SelectedIndex!=2;RefreshPermissionDescription();
            };sandbox.Unchecked+=delegate{RefreshPermissionDescription();};sandbox.Checked+=delegate{if(!loadingPermissions&&SelectedApprovalMode==AgentApprovalMode.Legacy){permissionsEdited=true;access.IsEnabled=false;approvalMode.SelectedIndex=0;}else if(!loadingPermissions&&approvalMode.SelectedIndex==2)approvalMode.SelectedIndex=0;RefreshPermissionDescription();};access=new ComboBox{Name="AgentAccessMode",ItemsSource=new[]{"完整文件访问","仅工作目录"},MinHeight=36};permissions.Children.Add(SettingRow("文件访问范围","两种范围互斥，不影响模型本身的能力。",access));permissionDescription=Ui.Text("",12,AgentUi.Muted);permissionDescription.Margin=new Thickness(0,8,0,12);permissions.Children.Add(permissionDescription);access.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e){
                if(!loadingPermissions&&SelectedApprovalMode==AgentApprovalMode.Legacy){
                    if(access.SelectedIndex==0&&baseline!=null&&!baseline.AgentFullAccess){
                        if(!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this))){loadingPermissions=true;access.SelectedIndex=e.RemovedItems.Count==0?1:access.Items.IndexOf(e.RemovedItems[0]);loadingPermissions=false;return;}
                        fullAccessConfirmed=true;
                    }else fullAccessConfirmed=false;
                    loadingPermissions=true;approvalMode.SelectedIndex=access.SelectedIndex==0?2:0;loadingPermissions=false;
                }RefreshPermissionDescription();
            };
            terminal=new ComboBox{Name="AgentTerminalPolicy",ItemsSource=new[]{"关闭","启用（按审批模式执行）"},MinHeight=36};permissions.Children.Add(SettingRow("终端执行","独立开关，按当前审批与沙箱设置执行；已有逐条确认设置保持不变。关闭时任何模式均不能执行终端。",terminal));
            plan=Ui.Toggle("",false,delegate{});plan.Name="AgentPlanMode";permissions.Children.Add(SettingRow("计划模式","只读取和分析；禁止修改文件、打开目录及执行操作。",plan));AddGroup(content,"权限",permissions);
            var instructions=new StackPanel();trustInstructions=Ui.Toggle("",false,delegate{});trustInstructions.Name="AgentTrustProjectInstructions";instructions.Children.Add(SettingRow("信任项目说明与技能","读取项目内适用目录的 AGENTS.md 和 .agents/skills。技能只按需读取文本，不会自动执行脚本或扩大权限。",trustInstructions));
            globalInstructions=Ui.Field();globalInstructions.Name="AgentGlobalInstructions";instructions.Children.Add(SettingRow("附加说明文件","可选，填写一个明确允许读取的 UTF-8 文件绝对路径，最多 64 KiB。",globalInstructions));skillsDirectory=Ui.Field();skillsDirectory.Name="AgentSkillsDirectory";instructions.Children.Add(SettingRow("附加技能目录","可选，填写用户技能目录；不会联网安装。",skillsDirectory));AddGroup(content,"说明与技能",instructions);
            var protection=new StackPanel();backup=Ui.Toggle("",true,delegate{});backup.Name="AgentBackupFiles";protection.Children.Add(SettingRow("修改前自动备份","保存原文件，发生外部修改冲突时拒绝覆盖。",backup));var open=AgentUi.Nav("查看文件备份…","OpenAgentBackups");open.HorizontalAlignment=HorizontalAlignment.Right;open.Click+=delegate{OpenBackups();};protection.Children.Add(SettingRow("文件备份","完整访问存放在原文件旁；仅工作目录时存放在项目下的 "+BackupFolder+"。",open));protection.Children.Add(Ui.Text("停止任务会保留已完成的修改。恢复操作会核对后续改动。",11,AgentUi.Muted));AddGroup(content,"文件保护",protection);
            var tasks=new StackPanel();goal=Ui.Field("",true);goal.Name="AgentGoal";goal.MaxLength=4000;goal.MinHeight=80;goal.MaxHeight=150;tasks.Children.Add(SettingRow("持续目标","为后续请求补充任务目标；保存不会自动开始任务。",goal));AddGroup(content,"任务",tasks);
            var context=new StackPanel();contextSummary=Ui.Text("",12,AgentUi.Ink);contextSummary.Name="WorkContextSummary";contextSummary.TextWrapping=TextWrapping.Wrap;context.Children.Add(SettingRow("上下文自动管理","跟随模型配置中的上下文容量，自动预留输出和工具空间。",contextSummary));contextBudget=Ui.Field();contextBudget.Name="AgentContextTokenLimit";contextBudget.MaxLength=7;contextBudget.TextChanged+=delegate{RefreshContextInfo();};fallbackRow=SettingRow("未知模型兜底值","模型未提供容量且未手动配置时使用，单位 Token。",contextBudget);fallbackRow.Name="ContextFallbackRow";context.Children.Add(fallbackRow);context.Children.Add(Ui.Text("接近可用容量时自动压缩旧历史与输出，保留当前要求和完整工具协议组。用量为估算；摘要可能产生额外模型请求。",11,AgentUi.Muted));AddGroup(content,"上下文",context);
            var preferences=new StackPanel();detailed=Ui.Toggle("",false,delegate{});detailed.Name="AgentDetailedProcess";preferences.Children.Add(SettingRow("展开执行过程","全局偏好，控制回复中执行过程的默认展开方式。",detailed));predicted=Ui.Toggle("",false,delegate{});predicted.Name="ShowPredictedMessages";var editorSettings=new StackPanel();editorSettings.Children.Add(SettingRow("显示预测消息","全局偏好；根据最近对话预测输入，可能产生额外模型请求。",predicted));AddGroup(content,"界面偏好",preferences);
            plainText=Ui.Toggle("",false,delegate{});plainText.Name="PlainTextEditor";editorSettings.Children.Add(SettingRow("纯文本编辑器","开启后保留代码、Markdown 和链接的原始语法；关闭后直接编辑排版后的内容。仅影响输入区，发送后的消息仍支持 Markdown。",plainText));
            showContext=Ui.Toggle("",true,delegate{});showContext.Name="ShowContextUsage";editorSettings.Children.Add(SettingRow("显示上下文窗口使用情况","在模型选择器旁显示用量。隐藏不会关闭自动压缩。",showContext));
            sendShortcut=new ComboBox{Name="SendShortcut",ItemsSource=new[]{"按 Enter 键","按 Ctrl + Enter 键"},MinHeight=36};editorSettings.Children.Add(SettingRow("发送快捷键","Enter 发送时，Shift + Enter 换行；Ctrl + Enter 发送时，Enter 换行。",sendShortcut));
            followUp=new ComboBox{Name="FollowUpBehavior",ItemsSource=new[]{"排队","引导"},MinHeight=36};editorSettings.Children.Add(SettingRow("跟进处理方式","运行中发送新消息时：排队等待本轮完成；引导在下一处可调整的执行边界纠偏，不取消当前操作。Ctrl + Enter 执行相反方式。",followUp));AddGroup(content,"编辑器",editorSettings);
            var abilities=new StackPanel();abilities.Children.Add(Ui.Text("文件：查找、搜索、读取和修改文本及支持的文档；创建目录，查看与恢复本任务变更。",12,AgentUi.Muted));abilities.Children.Add(Ui.Text("网络：可读取公开网页；GitHub 读取依赖已配置的仓库与连接。",12,AgentUi.Muted));abilities.Children.Add(Ui.Text("终端默认关闭，启用后遵循所选审批模式。完全访问以当前账户权限执行，不会获得管理员权限。",12,AgentUi.Muted));var details=new Expander{Header=Ui.Text("更多能力边界",12,AgentUi.Muted),Margin=new Thickness(0,12,0,0),Content=Ui.Text("具体工具取决于模型能力、项目权限和已连接扩展。GitHub 写入每次确认；MCP 在扩展页手动连接且未知工具每次确认。子任务使用真实独立只读 worker，最多并行 4、深度 1；Windows 工作区沙箱隔离终端及其子进程；文件和网页工具由宿主按权限执行，MCP 扩展不在终端沙箱内。",11,AgentUi.Muted)};ChatUi.ProcessExpander(details);abilities.Children.Add(details);AddGroup(content,"能力说明",abilities);
            var scroll=Ui.Scroll(content);scroll.Name="WorkSettingsScroll";var pages=new Grid();pages.Children.Add(scroll);var promptBody=new StackPanel{Margin=new Thickness(26,10,26,20)};promptBody.Children.Add(Ui.Text("工作模式提示词",20,AgentUi.Ink));promptBody.Children.Add(Ui.Text("所有桌宠、项目和工作对话共用。普通聊天仍使用各自桌宠的人格提示词。项目目标和权限会另外加入实际请求，修改此处不会改变权限。",12,AgentUi.Muted));workPrompt=Ui.Field("",true);workPrompt.Name="AgentSystemPrompt";workPrompt.MaxLength=16000;workPrompt.MinHeight=230;workPrompt.MaxHeight=460;workPrompt.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;workPrompt.Margin=new Thickness(0,18,0,12);promptBody.Children.Add(workPrompt);var resetPrompt=Ui.Button("恢复默认提示词");resetPrompt.Name="ResetAgentSystemPrompt";resetPrompt.HorizontalAlignment=HorizontalAlignment.Left;resetPrompt.Click+=delegate{workPrompt.Text=PetSettings.DefaultAgentSystemPrompt;notice.Text="默认提示词已填入，点击保存后生效。";};promptBody.Children.Add(resetPrompt);var promptScroll=Ui.Scroll(promptBody);promptScroll.Name="WorkPromptScroll";promptScroll.Visibility=Visibility.Collapsed;pages.Children.Add(promptScroll);shell.Children.Add(pages);generalTab.Background=AgentUi.Soft;generalTab.Click+=delegate{promptVisible=false;scroll.Visibility=Visibility.Visible;promptScroll.Visibility=Visibility.Collapsed;generalTab.Background=AgentUi.Soft;promptTab.Background=System.Windows.Media.Brushes.Transparent;save.Content="保存工作设置";};promptTab.Click+=delegate{promptVisible=true;scroll.Visibility=Visibility.Collapsed;promptScroll.Visibility=Visibility.Visible;promptTab.Background=AgentUi.Soft;generalTab.Background=System.Windows.Media.Brushes.Transparent;save.Content="保存工作提示词";};Content=shell;RefreshState();
        }
        static FrameworkElement SettingRow(string title,string description,FrameworkElement control)
        {
            var row=new Grid{Margin=new Thickness(0,0,0,14)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});var labels=new StackPanel{Margin=new Thickness(0,4,20,0)};var caption=Ui.Text(title,13,AgentUi.Ink);caption.FontWeight=FontWeights.SemiBold;labels.Children.Add(caption);var help=Ui.Text(description,11,AgentUi.Muted);help.Margin=new Thickness(0,5,0,0);help.TextWrapping=TextWrapping.Wrap;labels.Children.Add(help);row.Children.Add(labels);System.Windows.Automation.AutomationProperties.SetName(control,title);control.HorizontalAlignment=control is CheckBox || control is Button?HorizontalAlignment.Right:HorizontalAlignment.Stretch;control.VerticalAlignment=VerticalAlignment.Center;control.Margin=new Thickness(0);Grid.SetColumn(control,1);row.Children.Add(control);return row;
        }
        static void AddGroup(StackPanel parent,string title,StackPanel body)
        {
            var heading=Ui.Text(title,15,AgentUi.Ink);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,14,0,12);parent.Children.Add(heading);parent.Children.Add(new Border{Child=body,Padding=new Thickness(20,18,20,4),CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),BorderBrush=AgentUi.Line,Background=System.Windows.Media.Brushes.White});
        }
        void RefreshContextInfo()
        {
            if(contextSummary==null)return;var model=pet.Settings.Text;int capacity=model==null?0:model.ContextWindowTokens;string name=model==null||String.IsNullOrWhiteSpace(model.Model)?"尚未选择模型":model.Model;int fallback;Int32.TryParse(contextBudget==null?"":contextBudget.Text,out fallback);contextSummary.Text=capacity>0?name+"\n"+capacity.ToString("N0")+" Token · 跟随模型配置":name+"\n容量未知 · 兜底 "+(fallback>=8192&&fallback<=ModelContextCapacity.Maximum?fallback.ToString("N0"):"待填写")+" Token";if(fallbackRow!=null)fallbackRow.Visibility=capacity>0?Visibility.Collapsed:Visibility.Visible;
        }
        static string DefaultWorkspace {get{return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace"));}}
        void RefreshPermissionDescription()
        {
            if(permissionDescription==null)return;
            if(SelectedApprovalMode!=AgentApprovalMode.Legacy){permissionDescription.Text=SelectedApprovalMode==AgentApprovalMode.Full?"以当前账户权限执行，不使用工作区沙箱。终端仍需单独启用。":sandbox.IsChecked==true?"项目内文件操作可直接执行，越界和联网操作按审批模式确认。终端及其子进程由 Windows 隔离，默认禁止联网。":"工作区沙箱已关闭；终端操作需确认，文件与网络操作遵循所选审批模式。";return;}
            permissionDescription.Text=access.SelectedIndex==0?"使用当前 Windows 账户已有的文件权限，可访问桌面、用户目录与磁盘根目录；不会申请管理员权限或提权。":"文件工具仅访问所选工作目录。请选择具体项目目录；不接受磁盘根目录、整个用户目录或系统目录。";
        }
        public void RefreshState()
        {
            RefreshContextInfo();if(HasUnsavedChanges)return;
            loadingPermissions=true;permissionsEdited=false;fullAccessConfirmed=false;workPrompt.Text=pet.Settings.AgentSystemPrompt;promptBaseline=workPrompt.Text;project=pet.CurrentEditingProject;baseline=project==null?pet.Settings.Copy():ProjectSettings(project);approvalMode.SelectedIndex=(int)AgentAccessPolicy.DisplayMode(baseline)-1;sandbox.IsChecked=baseline.AgentSandboxEnabled;sandboxTools.Text=baseline.AgentSandboxReadDirectories??"";access.IsEnabled=baseline.AgentApprovalMode==AgentApprovalMode.Legacy;access.SelectedIndex=baseline.AgentFullAccess?0:1;loadingPermissions=false;projectName.Text=project==null?"全局默认设置":project.Name;projectName.IsEnabled=project!=null;workspace.Text=baseline.AgentWorkspace??"";goal.Text=baseline.AgentGoal??"";
            trustInstructions.IsChecked=baseline.AgentTrustProjectInstructions;globalInstructions.Text=baseline.AgentGlobalInstructions??"";skillsDirectory.Text=baseline.AgentSkillsDirectory??"";terminal.SelectedIndex=baseline.AgentTerminalPolicy==AgentTerminalPolicy.ConfirmEach?1:0;plan.IsChecked=baseline.AgentPlanMode;backup.IsChecked=baseline.AgentBackupFiles;detailed.IsChecked=pet.Settings.AgentDetailedProcess;detailed.IsEnabled=project==null;predicted.IsEnabled=project==null;plainText.IsEnabled=showContext.IsEnabled=sendShortcut.IsEnabled=followUp.IsEnabled=project==null;plainText.IsChecked=pet.Settings.PlainTextEditor;showContext.IsChecked=pet.Settings.ShowContextUsage;sendShortcut.SelectedIndex=pet.Settings.SendOnEnter?0:1;followUp.SelectedIndex=pet.Settings.FollowUpBehavior=="steer"?1:0;scopeLabel.Text=project==null?"全局默认 · 用于无项目对话":"当前项目 · "+project.Name+"（界面偏好在全局设置中修改）";
            contextBudget.Text=baseline.AgentContextTokenLimit.ToString(CultureInfo.InvariantCulture);predicted.IsChecked=pet.Settings.ShowPredictedMessages;RefreshPermissionDescription();RefreshContextInfo();
        }
        bool SavePrompt()
        {
            string value=workPrompt.Text.Trim();if(value.Length<1 || value.Length>16000){notice.Text="工作提示词需为 1～16000 个字符。";return false;}string previous=pet.Settings.AgentSystemPrompt;pet.Settings.AgentSystemPrompt=value;pet.SaveSettings();if(!String.IsNullOrEmpty(pet.Notice)){pet.Settings.AgentSystemPrompt=previous;notice.Text=pet.Notice;return false;}workPrompt.Text=value;promptBaseline=value;notice.Text="工作提示词已保存，所有工作对话从下一次请求生效。";return true;
        }
        public bool Save()
        {
            if(promptVisible)return SavePrompt();
            if(pet.Workspaces.OpenWorkspaces.Any(w=>(project==null||w.ProjectId==project.Id)&&(w.Session.IsBusy||w.Queue.HasPending))||pet.Conversation.IsBusy){notice.Text="请先停止相关任务并处理排队消息，再保存权限设置。";return false;}
            PetSettings candidate;string projectNameValue;
            try
            {
                if(access.SelectedIndex!=0 && access.SelectedIndex!=1)throw new InvalidOperationException("请选择完整访问或仅工作目录。");
                int contextTokens;if(!Int32.TryParse(contextBudget.Text,NumberStyles.None,CultureInfo.InvariantCulture,out contextTokens) || contextTokens<8192 || contextTokens>2000000)throw new InvalidOperationException("上下文兜底值应为 8192–2000000 的整数。");
                string target=ValidateDirectory(workspace.Text,access.SelectedIndex==1);
                if(goal.Text.Length>4000)throw new InvalidOperationException("持续目标不能超过 4000 个字符，请缩短后保存。");
                projectNameValue=projectName.Text.Trim();if(project!=null && (projectNameValue.Length<1 || projectNameValue.Length>80))throw new InvalidOperationException("项目名称应为 1～80 个字符。");
                candidate=pet.Settings.Copy();candidate.AgentApprovalMode=SelectedApprovalMode;candidate.AgentSandboxReadDirectories=sandboxTools.Text.Trim();AgentSandboxDirectories.Parse(candidate.AgentSandboxReadDirectories);candidate.AgentSandboxEnabled=sandbox.IsChecked==true;if(candidate.AgentApprovalMode==AgentApprovalMode.Full)candidate.AgentSandboxEnabled=false;candidate.AgentCommandsEnabled=false;candidate.AgentCommandRecipes="[]";candidate.AgentTerminalPolicy=terminal.SelectedIndex==1?AgentTerminalPolicy.ConfirmEach:AgentTerminalPolicy.Disabled;
                candidate.AgentTrustProjectInstructions=trustInstructions.IsChecked==true;candidate.AgentGlobalInstructions=globalInstructions.Text.Trim();candidate.AgentSkillsDirectory=skillsDirectory.Text.Trim();candidate.AgentFullAccess=access.SelectedIndex==0;candidate.AgentWorkspace=target;candidate.AgentPlanMode=plan.IsChecked==true;
                candidate.AgentGoal=goal.Text.Trim();candidate.AgentBackupFiles=backup.IsChecked==true;candidate.AgentContextTokenLimit=contextTokens;candidate.ShowPredictedMessages=predicted.IsChecked==true;candidate.PlainTextEditor=plainText.IsChecked==true;candidate.ShowContextUsage=showContext.IsChecked==true;candidate.SendOnEnter=sendShortcut.SelectedIndex==0;candidate.FollowUpBehavior=followUp.SelectedIndex==1?"steer":"queue";candidate.AgentDetailedProcess=detailed.IsChecked==true;
            }
            catch(Exception e){if(!ValidationError(e))throw;notice.Text=e.Message;return false;}
            if(candidate.AgentApprovalMode==AgentApprovalMode.Full){
                if(!fullAccessConfirmed&&baseline.AgentApprovalMode!=AgentApprovalMode.Full&&!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this)))return false;
            }else if(candidate.AgentApprovalMode==AgentApprovalMode.Legacy&&candidate.AgentFullAccess&&!baseline.AgentFullAccess&&!fullAccessConfirmed){if(!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this)))return false;
            }else if(baseline.AgentSandboxEnabled&&!candidate.AgentSandboxEnabled){if(!PermissionChangeDialog.Confirm(Window.GetWindow(this),"关闭工作区沙箱？","保存后将按当前账户权限执行。系统沙箱关闭后，不限制终端访问的文件和网络。"))return false;}
            var previous=pet.Settings.Copy();var previousProject=project==null?null:project.Copy();
            try
            {
                if(project!=null){project.Name=projectNameValue;ApplyProjectFields(candidate,project);pet.ProjectStore.Save(pet.Projects);}else{ApplyWorkFields(candidate,pet.Settings);pet.SaveSettings();}
                if(project==null && !String.IsNullOrEmpty(pet.Notice)){ApplyWorkFields(previous,pet.Settings);notice.Text=pet.Notice;return false;}
                // Establish the clean baseline before refreshing other views, which may re-enter this panel.
                string promptDraft=workPrompt.Text;bool promptDirty=promptDraft!=promptBaseline;baseline=null;RefreshState();if(promptDirty)workPrompt.Text=promptDraft;pet.RefreshChatAppearance();notice.Text="工作设置已保存，从下一次请求生效。";return true;
            }
            catch(Exception e){if(project==null)ApplyWorkFields(previous,pet.Settings);else if(previousProject!=null){project.Name=previousProject.Name;ApplyProjectFields(ProjectSettings(previousProject),project);}if(!ValidationError(e))throw;notice.Text="保存未完成："+e.Message;return false;}
        }
        static PetSettings ProjectSettings(ProjectDefinition value){var result=new PetSettings();result.AgentApprovalMode=value.ApprovalMode;result.AgentSandboxReadDirectories=value.SandboxReadDirectories;result.AgentSandboxEnabled=value.SandboxEnabled;result.AgentTrustProjectInstructions=value.TrustProjectInstructions;result.AgentGlobalInstructions=value.GlobalInstructions;result.AgentSkillsDirectory=value.SkillsDirectory;result.AgentTerminalPolicy=value.TerminalPolicy;result.AgentCommandTimeoutSeconds=value.CommandTimeoutSeconds;result.AgentFullAccess=value.FullAccess;result.AgentWorkspace=value.Workspace;result.AgentPlanMode=value.PlanMode;result.AgentGoal=value.Goal;result.AgentBackupFiles=value.BackupFiles;result.AgentContextTokenLimit=value.ContextTokenLimit;return result;}
        static void ApplyProjectFields(PetSettings from,ProjectDefinition to){to.ApprovalMode=from.AgentApprovalMode;to.SandboxReadDirectories=from.AgentSandboxReadDirectories;to.SandboxEnabled=from.AgentSandboxEnabled;to.TrustProjectInstructions=from.AgentTrustProjectInstructions;to.GlobalInstructions=from.AgentGlobalInstructions;to.SkillsDirectory=from.AgentSkillsDirectory;to.TerminalPolicy=from.AgentTerminalPolicy;to.CommandTimeoutSeconds=from.AgentCommandTimeoutSeconds;to.FullAccess=from.AgentFullAccess;to.Workspace=from.AgentWorkspace;to.PlanMode=from.AgentPlanMode;to.Goal=from.AgentGoal;to.BackupFiles=from.AgentBackupFiles;to.ContextTokenLimit=from.AgentContextTokenLimit;}
        static void ApplyWorkFields(PetSettings from,PetSettings to)
        {
            to.AgentApprovalMode=from.AgentApprovalMode;to.AgentSandboxReadDirectories=from.AgentSandboxReadDirectories;to.AgentSandboxEnabled=from.AgentSandboxEnabled;to.AgentTrustProjectInstructions=from.AgentTrustProjectInstructions;to.AgentGlobalInstructions=from.AgentGlobalInstructions;to.AgentSkillsDirectory=from.AgentSkillsDirectory;to.AgentTerminalPolicy=from.AgentTerminalPolicy;to.AgentCommandTimeoutSeconds=from.AgentCommandTimeoutSeconds;
            to.AgentFullAccess=from.AgentFullAccess;to.AgentWorkspace=from.AgentWorkspace;to.AgentPlanMode=from.AgentPlanMode;to.AgentGoal=from.AgentGoal;
            to.AgentBackupFiles=from.AgentBackupFiles;to.AgentContextTokenLimit=from.AgentContextTokenLimit;to.ShowPredictedMessages=from.ShowPredictedMessages;to.PlainTextEditor=from.PlainTextEditor;to.ShowContextUsage=from.ShowContextUsage;to.SendOnEnter=from.SendOnEnter;to.FollowUpBehavior=from.FollowUpBehavior;to.AgentDetailedProcess=from.AgentDetailedProcess;
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
                var settings=baseline??pet.Settings;string parent;
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
