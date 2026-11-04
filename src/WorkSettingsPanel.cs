using System;
using System.Threading.Tasks;
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
        readonly TextBox projectName,workspace,goal,contextBudget,syncLimit,backgroundLimit;
        readonly CheckBox plan,backup;
        readonly TextBlock scopeLabel,contextSummary;readonly FrameworkElement fallbackRow;
        readonly TextBlock notice,permissionDescription;readonly TextBox workPrompt;string promptBaseline;bool promptVisible;
        readonly ArchivedConversationsPanel archives;readonly SettingsPageHeader tabs;readonly Border footerBorder;readonly Button useGlobalDefaults;
        PetSettings baseline;ProjectDefinition project;AgentApprovalMode draftApprovalMode;
        AgentApprovalMode SelectedApprovalMode {get{return baseline!=null&&draftApprovalMode==AgentApprovalMode.Legacy&&!permissionsEdited?AgentApprovalMode.Legacy:(AgentApprovalMode)(approvalMode.SelectedIndex+1);}}
        public bool HasUnsavedChanges
        {
            get
            {
                return baseline!=null && (SelectedApprovalMode!=baseline.AgentApprovalMode || sandbox.IsChecked!=baseline.AgentSandboxEnabled || sandboxTools.Text!=(baseline.AgentSandboxReadDirectories??"") || workPrompt.Text!=promptBaseline || (project!=null && projectName.Text.Trim()!=project.Name) || access.SelectedIndex!=(baseline.AgentFullAccess?0:1) || workspace.Text!=(baseline.AgentWorkspace??"") || goal.Text!=(baseline.AgentGoal??"") ||
                    plan.IsChecked!=baseline.AgentPlanMode || backup.IsChecked!=baseline.AgentBackupFiles || terminal.SelectedIndex!=(baseline.AgentTerminalPolicy==AgentTerminalPolicy.ConfirmEach?1:0) || trustInstructions.IsChecked!=baseline.AgentTrustProjectInstructions || globalInstructions.Text!=(baseline.AgentGlobalInstructions??"") || skillsDirectory.Text!=(baseline.AgentSkillsDirectory??"") ||
                    syncLimit.Text!=baseline.AgentCommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture) || backgroundLimit.Text!=baseline.AgentBackgroundJobTimeoutSeconds.ToString(CultureInfo.InvariantCulture) || contextBudget.Text!=baseline.AgentContextTokenLimit.ToString(CultureInfo.InvariantCulture));
            }
        }
        public WorkSettingsPanel(PetWindow companion)
        {
            if(companion==null)throw new ArgumentNullException("companion");pet=companion;Name="WorkSettingsPanel";
            Background=ThemeManager.Surface;
            var shell=new DockPanel{LastChildFill=true};
            notice=LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text")),11,AgentUi.Muted);notice.Name="WorkSettingsNotice";notice.VerticalAlignment=VerticalAlignment.Center;notice.Margin=new Thickness(0,0,12,0);
            var save=LocalizedUi.Button(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeA")),true);save.Name="SaveWorkSettings";save.Margin=new Thickness(0);save.Click+=async delegate{await SaveAsync();};
            var footer=new DockPanel{LastChildFill=true,Margin=new Thickness(24,14,24,16)};DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);var restore=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.restore"));restore.Name="RestoreWorkSettings";restore.Margin=new Thickness(0,0,10,0);restore.Click+=delegate{baseline=null;RefreshState();};DockPanel.SetDock(restore,Dock.Right);footer.Children.Add(restore);footer.Children.Add(notice);footerBorder=new Border{Child=footer,BorderBrush=AgentUi.Line,BorderThickness=new Thickness(0,1,0,0)};DockPanel.SetDock(footerBorder,Dock.Bottom);shell.Children.Add(footerBorder);
            tabs=new SettingsPageHeader(()=>TextResources.Get("settingsLayout.workTitle"),()=>"",()=>new[]{TextResources.Get("workSettingsPanel.workSettingsPanel.tabsSettingsPageHeader.alternativeA"),TextResources.Get("workSettingsPanel.workSettingsPanel.tabsSettingsPageHeader.alternativeB")},new[]{"WorkSettingsGeneralTab","WorkSettingsPromptTab"}){Margin=new Thickness(24,22,24,18)};scopeLabel=tabs.Description;scopeLabel.Name="WorkSettingsScope";useGlobalDefaults=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.useGlobalDefaults"));useGlobalDefaults.Name="UseGlobalWorkDefaults";useGlobalDefaults.HorizontalAlignment=HorizontalAlignment.Left;useGlobalDefaults.Margin=new Thickness(0,12,0,0);useGlobalDefaults.Click+=delegate{UseGlobalDefaults();};tabs.Children.Add(useGlobalDefaults);DockPanel.SetDock(tabs,Dock.Top);shell.Children.Add(tabs);
            var content=new StackPanel{Margin=new Thickness(24,0,24,24)};
            var location=new StackPanel();projectName=Ui.Field();projectName.Name="ProjectName";projectName.MaxLength=80;location.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeA")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeB")),projectName));
            workspace=Ui.Field();workspace.Name="AgentWorkspace";workspace.MaxLength=32767;var directory=new StackPanel();directory.Children.Add(workspace);var directoryActions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};var change=AgentUi.Nav(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.agentUiNav")),"ChangeAgentWorkspace");change.Click+=delegate{ChangeDirectory();};directoryActions.Children.Add(change);var reset=AgentUi.Nav(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.agentUiNav.alternativeA")),"ResetAgentWorkspace");reset.Click+=delegate{workspace.Text=DefaultWorkspace;LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.notice")));};directoryActions.Children.Add(reset);directory.Children.Add(directoryActions);location.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeC")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeD")),directory));AddGroup(content,()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup")),location);
            var permissions=new StackPanel();approvalMode=LocalizedUi.Options(new ComboBox{Name="AgentApprovalMode",ItemsSource=new[]{TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeA"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeB")},MinHeight=36},()=>(new[]{TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeA"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeB")}));permissions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeE")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeF")),approvalMode));sandbox=Ui.Toggle("",false,delegate{});sandbox.Name="AgentSandboxEnabled";permissions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeG")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeH")),sandbox));sandboxTools=Ui.Field("",true);sandboxTools.Name="SandboxReadDirectories";sandboxTools.MinHeight=52;sandboxTools.MaxHeight=110;permissions.Children.Add(Advanced("AdvancedWorkSandbox",()=>TextResources.Get("settingsLayout.advancedSandbox"),SettingRow(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeI"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeJ"),sandboxTools)));approvalMode.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e){
                if(loadingPermissions)return;
                if(approvalMode.SelectedIndex==2){
                    if(!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this))){loadingPermissions=true;approvalMode.SelectedIndex=e.RemovedItems.Count==0?1:approvalMode.Items.IndexOf(e.RemovedItems[0]);loadingPermissions=false;return;}
                    fullAccessConfirmed=true;
                }else fullAccessConfirmed=false;
                permissionsEdited=true;access.IsEnabled=false;sandbox.IsChecked=approvalMode.SelectedIndex!=2;RefreshPermissionDescription();
            };sandbox.Unchecked+=delegate{RefreshPermissionDescription();};sandbox.Checked+=delegate{if(!loadingPermissions&&SelectedApprovalMode==AgentApprovalMode.Legacy){permissionsEdited=true;access.IsEnabled=false;approvalMode.SelectedIndex=0;}else if(!loadingPermissions&&approvalMode.SelectedIndex==2)approvalMode.SelectedIndex=0;RefreshPermissionDescription();};access=LocalizedUi.Options(new ComboBox{Name="AgentAccessMode",ItemsSource=new[]{TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeC"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeD")},MinHeight=36},()=>(new[]{TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeC"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeD")}));permissions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeK")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeL")),access));permissionDescription=Ui.Text("",12,AgentUi.Muted);permissionDescription.Margin=new Thickness(0,8,0,12);permissions.Children.Add(permissionDescription);access.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e){
                if(!loadingPermissions&&SelectedApprovalMode==AgentApprovalMode.Legacy){
                    if(access.SelectedIndex==0&&baseline!=null&&!baseline.AgentFullAccess){
                        if(!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this))){loadingPermissions=true;access.SelectedIndex=e.RemovedItems.Count==0?1:access.Items.IndexOf(e.RemovedItems[0]);loadingPermissions=false;return;}
                        fullAccessConfirmed=true;
                    }else fullAccessConfirmed=false;
                    loadingPermissions=true;approvalMode.SelectedIndex=access.SelectedIndex==0?2:0;loadingPermissions=false;
                }RefreshPermissionDescription();
            };
            terminal=LocalizedUi.Options(new ComboBox{Name="AgentTerminalPolicy",ItemsSource=new[]{TextResources.Get("common.close"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeE")},MinHeight=36},()=>(new[]{TextResources.Get("common.close"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeE")}));permissions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeM")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeN")),terminal));
            permissions.Children.Add(new ImageDescriptionSettings(pet,"WorkImageDescriptionModel"));
            syncLimit=Ui.Field();syncLimit.Name="AgentCommandTimeoutSeconds";backgroundLimit=Ui.Field();backgroundLimit.Name="AgentBackgroundJobTimeoutSeconds";
            permissions.Children.Add(SettingRow(()=> (TextResources.Get("agent.jobs.ui.syncLimit")),()=> ("1–600"),syncLimit));permissions.Children.Add(SettingRow(()=> (TextResources.Get("agent.jobs.ui.backgroundLimit")),()=> ("1–28800"),backgroundLimit));
            plan=Ui.Toggle("",false,delegate{});plan.Name="AgentPlanMode";permissions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeO")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeP")),plan));AddGroup(content,()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeA")),permissions);
            var instructions=new StackPanel();trustInstructions=Ui.Toggle("",false,delegate{});trustInstructions.Name="AgentTrustProjectInstructions";instructions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeQ")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeR")),trustInstructions));
            globalInstructions=Ui.Field();globalInstructions.Name="AgentGlobalInstructions";instructions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeS")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeT")),globalInstructions));skillsDirectory=Ui.Field();skillsDirectory.Name="AgentSkillsDirectory";instructions.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeU")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeV")),skillsDirectory));AddGroup(content,()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeB")),instructions);
            var protection=new StackPanel();backup=Ui.Toggle("",true,delegate{});backup.Name="AgentBackupFiles";protection.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeW")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeX")),backup));var open=AgentUi.Nav(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.agentUiNav.alternativeB")),"OpenAgentBackups");open.HorizontalAlignment=HorizontalAlignment.Right;open.Click+=delegate{OpenBackups();};protection.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeY")),()=> (TextResources.Format("workSettingsPanel.workSettingsPanel.settingRow",BackupFolder)),open));protection.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeB")),11,AgentUi.Muted));AddGroup(content,()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeC")),protection);
            var tasks=new StackPanel();goal=Ui.Field("",true);goal.Name="AgentGoal";goal.MaxLength=4000;goal.MinHeight=80;goal.MaxHeight=150;tasks.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeZ")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAA")),goal));AddGroup(content,()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeD")),tasks);
            var context=new StackPanel();contextSummary=Ui.Text("",12,AgentUi.Ink);contextSummary.Name="WorkContextSummary";contextSummary.TextWrapping=TextWrapping.Wrap;context.Children.Add(SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAB")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAC")),contextSummary));contextBudget=Ui.Field();contextBudget.Name="AgentContextTokenLimit";contextBudget.MaxLength=7;contextBudget.TextChanged+=delegate{RefreshContextInfo();};fallbackRow=SettingRow(()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAD")),()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAE")),contextBudget);fallbackRow.Name="ContextFallbackRow";context.Children.Add(fallbackRow);context.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeC")),11,AgentUi.Muted));AddGroup(content,()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeE")),context);
            var abilities=new StackPanel();abilities.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeD")),12,AgentUi.Muted));abilities.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeE")),12,AgentUi.Muted));abilities.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeF")),12,AgentUi.Muted));var boundaryTitle=LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeG")),12,AgentUi.Muted);boundaryTitle.Margin=new Thickness(0,12,0,6);abilities.Children.Add(boundaryTitle);abilities.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeH")),11,AgentUi.Muted));AddGroup(content,()=> (TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeH")),abilities);
            var scroll=Ui.Scroll(content);scroll.Name="WorkSettingsScroll";var pages=new Grid();pages.Children.Add(scroll);var promptBody=new StackPanel{Margin=new Thickness(24,0,24,20)};promptBody.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeI")),20,AgentUi.Ink));promptBody.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeJ")),12,AgentUi.Muted));workPrompt=Ui.Field("",true);workPrompt.Name="AgentSystemPrompt";workPrompt.MaxLength=16000;workPrompt.MinHeight=230;workPrompt.MaxHeight=460;workPrompt.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;workPrompt.Margin=new Thickness(0,18,0,12);promptBody.Children.Add(workPrompt);var resetPrompt=LocalizedUi.Button(()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeK")));resetPrompt.Name="ResetAgentSystemPrompt";resetPrompt.HorizontalAlignment=HorizontalAlignment.Left;resetPrompt.Click+=delegate{workPrompt.Text=PetSettings.DefaultAgentSystemPrompt;LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.workSettingsPanel.notice.alternativeA")));};promptBody.Children.Add(resetPrompt);var promptScroll=Ui.Scroll(promptBody);promptScroll.Name="WorkPromptScroll";promptScroll.Visibility=Visibility.Collapsed;pages.Children.Add(promptScroll);archives=new ArchivedConversationsPanel(pet){Visibility=Visibility.Collapsed};pages.Children.Add(archives);shell.Children.Add(pages);tabs.SelectionChanged+=index=>{promptVisible=index==1;scroll.Visibility=index==0?Visibility.Visible:Visibility.Collapsed;promptScroll.Visibility=promptVisible?Visibility.Visible:Visibility.Collapsed;archives.Visibility=index==2?Visibility.Visible:Visibility.Collapsed;footerBorder.Visibility=index==2?Visibility.Collapsed:Visibility.Visible;if(index==2)archives.RefreshState();RefreshScope();LocalizedUi.Set(save,"Content",()=>(promptVisible?TextResources.Get("workSettingsPanel.workSettingsPanel.saveContentPromptVisible"):TextResources.Get("workSettingsPanel.workSettingsPanel.saveContentPromptVisible.alternativeA")));};Content=shell;AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler(delegate{DraftChanged();}));AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,new RoutedEventHandler(delegate{DraftChanged();}));AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,new RoutedEventHandler(delegate{DraftChanged();}));AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,new SelectionChangedEventHandler(delegate{DraftChanged();}));RefreshState();
        }
        static FrameworkElement SettingRow(System.Func<string> title,System.Func<string> description,FrameworkElement control)
        {
            return Ui.SettingRow(title,description,control);
        }
        static void AddGroup(StackPanel parent,System.Func<string> title,StackPanel body)
        {
            SettingsSurface.Section(parent,title,body);
        }
        void RefreshContextInfo()
        {
            if(contextSummary==null)return;var model=pet.Settings.Text;int capacity=model==null?0:model.ContextWindowTokens;string name=model==null||String.IsNullOrWhiteSpace(model.Model)?TextResources.Get("workSettingsPanel.refreshContextInfo.nullStringIsNullOrWhiteSpace"):model.Model;int fallback;Int32.TryParse(contextBudget==null?"":contextBudget.Text,out fallback);LocalizedUi.Set(contextSummary,"Text",()=>(capacity>0?TextResources.Format("workSettingsPanel.refreshContextInfo.nameCapacityToString",name,capacity.ToString("N0")):name+TextResources.Get("workSettingsPanel.refreshContextInfo.nameCapacityToString.alternativeA")+(fallback>=8192&&fallback<=ModelContextCapacity.Maximum?fallback.ToString("N0"):TextResources.Get("workSettingsPanel.refreshContextInfo.nameCapacityToString.alternativeB"))+" Token"));if(fallbackRow!=null)fallbackRow.Visibility=capacity>0?Visibility.Collapsed:Visibility.Visible;
        }
        static string DefaultWorkspace {get{return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace"));}}
        void RefreshPermissionDescription()
        {
            if(permissionDescription==null)return;
            if(SelectedApprovalMode!=AgentApprovalMode.Legacy){LocalizedUi.Set(permissionDescription,"Text",()=>(SelectedApprovalMode==AgentApprovalMode.Full?TextResources.Get("workSettingsPanel.refreshPermissionDescription.sandboxIsCheckedTrue"):sandbox.IsChecked==true?TextResources.Get("workSettingsPanel.refreshPermissionDescription.sandboxIsCheckedTrue.alternativeA"):TextResources.Get("workSettingsPanel.refreshPermissionDescription.sandboxIsCheckedTrue.alternativeB")));return;}
            LocalizedUi.Set(permissionDescription,"Text",()=>(access.SelectedIndex==0?TextResources.Get("workSettingsPanel.refreshPermissionDescription.permissionDescriptionAccessSelectedIndex"):TextResources.Get("workSettingsPanel.refreshPermissionDescription.permissionDescriptionAccessSelectedIndex.alternativeA")));
        }
        public void RefreshState()
        {
            if(archives!=null)archives.RefreshState();RefreshContextInfo();if(HasUnsavedChanges)return;
            loadingPermissions=true;permissionsEdited=false;fullAccessConfirmed=false;workPrompt.Text=PersonaProfiles.LocalizeDefaultPrompt(pet.Settings.AgentSystemPrompt);promptBaseline=workPrompt.Text;project=pet.CurrentEditingProject;baseline=project==null?pet.Settings.Copy():ProjectSettings(project);projectName.Text=project==null?TextResources.Get("workSettingsPanel.refreshState.nullProjectName.alternativeA"):project.Name;projectName.IsEnabled=project!=null;workspace.Text=baseline.AgentWorkspace??"";
            FillWorkControls(baseline);RefreshScope();DraftChanged();
        }
        void FillWorkControls(PetSettings value)
        {
            loadingPermissions=true;
            try{draftApprovalMode=value.AgentApprovalMode;approvalMode.SelectedIndex=(int)AgentAccessPolicy.DisplayMode(value)-1;sandbox.IsChecked=value.AgentSandboxEnabled;sandboxTools.Text=value.AgentSandboxReadDirectories??"";access.IsEnabled=value.AgentApprovalMode==AgentApprovalMode.Legacy;access.SelectedIndex=value.AgentFullAccess?0:1;
                trustInstructions.IsChecked=value.AgentTrustProjectInstructions;globalInstructions.Text=value.AgentGlobalInstructions??"";skillsDirectory.Text=value.AgentSkillsDirectory??"";terminal.SelectedIndex=value.AgentTerminalPolicy==AgentTerminalPolicy.ConfirmEach?1:0;plan.IsChecked=value.AgentPlanMode;backup.IsChecked=value.AgentBackupFiles;goal.Text=value.AgentGoal??"";
                syncLimit.Text=value.AgentCommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture);backgroundLimit.Text=value.AgentBackgroundJobTimeoutSeconds.ToString(CultureInfo.InvariantCulture);contextBudget.Text=value.AgentContextTokenLimit.ToString(CultureInfo.InvariantCulture);
            }finally{loadingPermissions=false;}RefreshPermissionDescription();RefreshContextInfo();
        }
        void RefreshScope(){LocalizedUi.Set(scopeLabel,"Text",()=>promptVisible||project==null?TextResources.Get("settingsLayout.globalDefaults"):TextResources.Format("settingsLayout.currentProject",project.Name));useGlobalDefaults.Visibility=project!=null&&!promptVisible?Visibility.Visible:Visibility.Collapsed;}
        void DraftChanged(){if(!loadingPermissions&&notice!=null)LocalizedUi.Set(notice,"Text",()=>(HasUnsavedChanges?TextResources.Get("settingsLayout.unsaved"):TextResources.Get("settingsLayout.saved")));}
        static Expander Advanced(string name,Func<string> title,UIElement content){var expander=new Expander{Name=name,Header=LocalizedUi.Text(title,13,Ui.Ink),Content=content};SettingsSurface.StyleExpander(expander);return expander;}
        void UseGlobalDefaults(){if(project==null)return;permissionsEdited=false;fullAccessConfirmed=false;FillWorkControls(pet.Settings);LocalizedUi.Set(notice,"Text",()=>TextResources.Get("settingsLayout.defaultsDraft"));}
        public void RefreshRestoredPreferences(){RefreshContextInfo();}
        internal int SelectedSection {get{return archives.Visibility==Visibility.Visible?2:tabs.SelectedIndex;}}
        internal void SelectSection(int index){if(index==2){SelectArchives();return;}tabs.Select(index);}
        internal void SelectArchives(){tabs.Select(0);foreach(UIElement child in ((Grid)archives.Parent).Children)child.Visibility=ReferenceEquals(child,archives)?Visibility.Visible:Visibility.Collapsed;footerBorder.Visibility=Visibility.Collapsed;useGlobalDefaults.Visibility=Visibility.Collapsed;archives.RefreshState();}

        async Task<bool> SavePromptAsync(Func<Action<PetSettings>,Task<bool>> commit)
        {
            string submitted=workPrompt.Text,value=submitted.Trim();if(value.Length<1 || value.Length>16000){LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.savePrompt.notice")));return false;}
            if(!await commit(target=>target.AgentSystemPrompt=value)){notice.Text=pet.Notice;return false;}
            if(workPrompt.Text==submitted)workPrompt.Text=value;promptBaseline=value;LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.savePrompt.notice.alternativeA")));return true;
        }
        public bool Save(){return SaveCoreAsync(change=>Task.FromResult(pet.CommitSettingsNow(change)),false).GetAwaiter().GetResult();}
        public Task<bool> SaveAsync(){return pet.RunSettingsEditAsync(this,()=>SaveCoreAsync(change=>pet.PersistSettingsAsync(change),true));}
        async Task<bool> SaveCoreAsync(Func<Action<PetSettings>,Task<bool>> commit,bool backgroundProject)
        {
            if(promptVisible)return await SavePromptAsync(commit);
            if(pet.Workspaces.OpenWorkspaces.Any(w=>(project==null||w.ProjectId==project.Id)&&(w.Session.IsBusy||w.Queue.HasPending))||pet.Conversation.IsBusy){LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.save.notice")));return false;}
            PetSettings candidate;string projectNameValue;
            try
            {
                if(access.SelectedIndex!=0 && access.SelectedIndex!=1)throw new InvalidOperationException(TextResources.Get("workSettingsPanel.save.text"));
                int contextTokens;if(!Int32.TryParse(contextBudget.Text,NumberStyles.None,CultureInfo.InvariantCulture,out contextTokens) || contextTokens<8192 || contextTokens>2000000)throw new InvalidOperationException(TextResources.Get("workSettingsPanel.save.text.alternativeA"));
                int syncSeconds,backgroundSeconds;
                if(!Int32.TryParse(syncLimit.Text,NumberStyles.None,CultureInfo.InvariantCulture,out syncSeconds)||syncSeconds<1||syncSeconds>600)throw new InvalidOperationException(TextResources.Get("agent.jobs.ui.syncLimit")+": 1–600");
                if(!Int32.TryParse(backgroundLimit.Text,NumberStyles.None,CultureInfo.InvariantCulture,out backgroundSeconds)||backgroundSeconds<1||backgroundSeconds>28800)throw new InvalidOperationException(TextResources.Get("agent.jobs.ui.backgroundLimit")+": 1–28800");
                string target=ValidateDirectory(workspace.Text,access.SelectedIndex==1);
                if(goal.Text.Length>4000)throw new InvalidOperationException(TextResources.Get("workSettingsPanel.save.text.alternativeB"));
                projectNameValue=projectName.Text.Trim();if(project!=null && (projectNameValue.Length<1 || projectNameValue.Length>80))throw new InvalidOperationException(TextResources.Get("workSettingsPanel.save.text.alternativeC"));
                candidate=pet.Settings.Copy();candidate.AgentApprovalMode=SelectedApprovalMode;candidate.AgentSandboxReadDirectories=sandboxTools.Text.Trim();AgentSandboxDirectories.Parse(candidate.AgentSandboxReadDirectories);candidate.AgentSandboxEnabled=sandbox.IsChecked==true;if(candidate.AgentApprovalMode==AgentApprovalMode.Full)candidate.AgentSandboxEnabled=false;candidate.AgentCommandsEnabled=false;candidate.AgentCommandRecipes="[]";candidate.AgentTerminalPolicy=terminal.SelectedIndex==1?AgentTerminalPolicy.ConfirmEach:AgentTerminalPolicy.Disabled;
                candidate.AgentTrustProjectInstructions=trustInstructions.IsChecked==true;candidate.AgentGlobalInstructions=globalInstructions.Text.Trim();candidate.AgentSkillsDirectory=skillsDirectory.Text.Trim();candidate.AgentFullAccess=access.SelectedIndex==0;candidate.AgentWorkspace=target;candidate.AgentPlanMode=plan.IsChecked==true;
                candidate.AgentCommandTimeoutSeconds=syncSeconds;candidate.AgentBackgroundJobTimeoutSeconds=backgroundSeconds;candidate.AgentGoal=goal.Text.Trim();candidate.AgentBackupFiles=backup.IsChecked==true;candidate.AgentContextTokenLimit=contextTokens;
            }
            catch(Exception e){if(!ValidationError(e))throw;notice.Text=e.Message;return false;}
            if(candidate.AgentApprovalMode==AgentApprovalMode.Full){
                if(!fullAccessConfirmed&&baseline.AgentApprovalMode!=AgentApprovalMode.Full&&!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this)))return false;
            }else if(candidate.AgentApprovalMode==AgentApprovalMode.Legacy&&candidate.AgentFullAccess&&!baseline.AgentFullAccess&&!fullAccessConfirmed){if(!PermissionChangeDialog.ConfirmFullAccess(Window.GetWindow(this)))return false;
            }else if(baseline.AgentSandboxEnabled&&!candidate.AgentSandboxEnabled){if(!PermissionChangeDialog.Confirm(Window.GetWindow(this),TextResources.Get("workSettingsPanel.save.permissionChangeDialogConfirm"),TextResources.Get("workSettingsPanel.save.permissionChangeDialogConfirm.alternativeA")))return false;}
            var saveProject=project;var previousProject=saveProject==null||backgroundProject?null:ProjectCatalogStore.ConfigurationSnapshot(saveProject);bool projectCommitted=false;
            try
            {
                if(saveProject!=null)
                {
                    using(backgroundProject?await pet.Workspaces.BeginProjectConfigurationAsync(saveProject.Id):pet.Workspaces.BeginProjectConfigurationAsync(saveProject.Id).GetAwaiter().GetResult()){
                    if(backgroundProject){
                        var original=ProjectCatalogStore.ConfigurationSnapshot(saveProject);var edited=ProjectCatalogStore.ConfigurationSnapshot(saveProject);edited.Name=projectNameValue;ApplyProjectFields(candidate,edited);
                        await pet.ChangeWorkBindingsAsync(new[]{saveProject.Id},null,async delegate{
                            using(var publication=await pet.ProjectStore.SaveProjectConfigurationAsync(original,edited)){
                                if(pet.Projects.Projects.Any(p=>p.Id==saveProject.Id))publication.Publish(saveProject);
                            }
                        },false);
                    }else{
                        Action apply=delegate{saveProject.Name=projectNameValue;ApplyProjectFields(candidate,saveProject);pet.ProjectStore.Save(pet.Projects);};
                        if(!pet.TryChangeWorkBindings(new[]{saveProject.Id},null,apply)){LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.save.notice")));return false;}
                    }
                    projectCommitted=true;
                    foreach(var workspaceOwner in pet.Workspaces.OpenWorkspaces.Where(w=>w.ProjectId==saveProject.Id))workspaceOwner.FollowProject(saveProject);
                    }
                }else if(!await commit(target=>ApplyWorkFields(candidate,target))){notice.Text=pet.Notice;return false;}
                // Establish the clean baseline before refreshing other views, which may re-enter this panel.
                string promptDraft=workPrompt.Text;bool promptDirty=promptDraft!=promptBaseline;baseline=null;RefreshState();if(promptDirty)workPrompt.Text=promptDraft;pet.RefreshChatAppearance();LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.save.notice.alternativeA")));return true;
            }
            catch(Exception e){if(!backgroundProject&&saveProject!=null&&previousProject!=null&&!projectCommitted){saveProject.Name=previousProject.Name;ApplyProjectFields(ProjectSettings(previousProject),saveProject);}if(!ValidationError(e))throw;notice.Text=TextResources.Format("workSettingsPanel.save.noticeEMessage",AgentJobUiText.Error(e));return false;}
        }
        static PetSettings ProjectSettings(ProjectDefinition value){var result=new PetSettings();result.AgentApprovalMode=value.ApprovalMode;result.AgentSandboxReadDirectories=value.SandboxReadDirectories;result.AgentSandboxEnabled=value.SandboxEnabled;result.AgentTrustProjectInstructions=value.TrustProjectInstructions;result.AgentGlobalInstructions=value.GlobalInstructions;result.AgentSkillsDirectory=value.SkillsDirectory;result.AgentTerminalPolicy=value.TerminalPolicy;result.AgentCommandTimeoutSeconds=value.CommandTimeoutSeconds;result.AgentBackgroundJobTimeoutSeconds=value.BackgroundJobTimeoutSeconds;result.AgentFullAccess=value.FullAccess;result.AgentWorkspace=value.Workspace;result.AgentPlanMode=value.PlanMode;result.AgentGoal=value.Goal;result.AgentBackupFiles=value.BackupFiles;result.AgentContextTokenLimit=value.ContextTokenLimit;return result;}
        static void ApplyProjectFields(PetSettings from,ProjectDefinition to){to.ApprovalMode=from.AgentApprovalMode;to.SandboxReadDirectories=from.AgentSandboxReadDirectories;to.SandboxEnabled=from.AgentSandboxEnabled;to.TrustProjectInstructions=from.AgentTrustProjectInstructions;to.GlobalInstructions=from.AgentGlobalInstructions;to.SkillsDirectory=from.AgentSkillsDirectory;to.TerminalPolicy=from.AgentTerminalPolicy;to.CommandTimeoutSeconds=from.AgentCommandTimeoutSeconds;to.BackgroundJobTimeoutSeconds=from.AgentBackgroundJobTimeoutSeconds;to.FullAccess=from.AgentFullAccess;to.Workspace=from.AgentWorkspace;to.PlanMode=from.AgentPlanMode;to.Goal=from.AgentGoal;to.BackupFiles=from.AgentBackupFiles;to.ContextTokenLimit=from.AgentContextTokenLimit;}
        static void ApplyWorkFields(PetSettings from,PetSettings to)
        {
            to.AgentApprovalMode=from.AgentApprovalMode;to.AgentSandboxReadDirectories=from.AgentSandboxReadDirectories;to.AgentSandboxEnabled=from.AgentSandboxEnabled;to.AgentTrustProjectInstructions=from.AgentTrustProjectInstructions;to.AgentGlobalInstructions=from.AgentGlobalInstructions;to.AgentSkillsDirectory=from.AgentSkillsDirectory;to.AgentTerminalPolicy=from.AgentTerminalPolicy;to.AgentCommandTimeoutSeconds=from.AgentCommandTimeoutSeconds;to.AgentBackgroundJobTimeoutSeconds=from.AgentBackgroundJobTimeoutSeconds;
            to.AgentFullAccess=from.AgentFullAccess;to.AgentWorkspace=from.AgentWorkspace;to.AgentPlanMode=from.AgentPlanMode;to.AgentGoal=from.AgentGoal;
            to.AgentBackupFiles=from.AgentBackupFiles;to.AgentContextTokenLimit=from.AgentContextTokenLimit;
        }
        static bool ValidationError(Exception e){return e is ArgumentException || e is InvalidOperationException || e is System.Runtime.Serialization.SerializationException || e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is System.Security.SecurityException;}
        static string ValidateDirectory(string text,bool restricted)
        {
            text=(text??"").Trim();if(text.Length==0 || !Path.IsPathRooted(text))throw new InvalidOperationException(TextResources.Get("workSettingsPanel.validateDirectory.text"));
            string path=Path.GetFullPath(text);
            if(!Directory.Exists(path))
            {
                if(String.Equals(path.TrimEnd('\\','/'),DefaultWorkspace.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))return DefaultWorkspace;
                throw new InvalidOperationException(TextResources.Get("workSettingsPanel.validateDirectory.text.alternativeA"));
            }
            return new AgentWorkspace(path,restricted).Root;
        }
        void ChangeDirectory()
        {
            try{string selected=PickFolder(TextResources.Get("workSettingsPanel.changeDirectory.pickFolder"),workspace.Text);if(selected!=null){workspace.Text=ValidateDirectory(selected,access.SelectedIndex==1);LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.changeDirectory.notice")));}}
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
                if(settings.AgentFullAccess){parent=PickFolder(TextResources.Get("workSettingsPanel.openBackups.pickFolder"),settings.AgentWorkspace);if(parent==null)return;}
                else parent=ValidateDirectory(settings.AgentWorkspace,true);
                string path=Path.Combine(parent,BackupFolder);
                if(!Directory.Exists(path)){LocalizedUi.Set(notice,"Text",()=>(TextResources.Format("workSettingsPanel.openBackups.noticePath",path)));return;}
                Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("workSettingsPanel.openBackups.notice")));
            }
            catch(Exception e){if(!(ValidationError(e) || e is System.ComponentModel.Win32Exception))throw;LocalizedUi.Set(notice,"Text",()=>(TextResources.Format("workSettingsPanel.openBackups.noticeEMessage",e.Message)));}
        }
        sealed class FolderOwner:System.Windows.Forms.IWin32Window
        {
            readonly IntPtr handle;public FolderOwner(Window window){handle=new WindowInteropHelper(window).Handle;}public IntPtr Handle{get{return handle;}}
        }
    }
}
