using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed partial class ProjectSidebar : StackPanel,IDisposable
    {
        readonly ProjectCatalogStore store;readonly ProjectCatalog catalog;
        readonly Action<ProjectDefinition> projectActivated;readonly Action<ProjectDefinition,ConversationDefinition> conversationActivated;
        readonly Action<ProjectDefinition> editProject,openWorkspace;readonly Action<ProjectDefinition> newConversation;readonly Action<ProjectDefinition,ConversationDefinition> renameConversation;readonly Action<ProjectDefinition> renameProject;readonly Action<ProjectDefinition> archiveProject,deleteProject,restoreProject;readonly Action<ProjectDefinition,ConversationDefinition> archiveConversation,deleteConversation,restoreConversation;readonly Func<ProjectDefinition,ProjectDefinition,ConversationDefinition,bool> moveConversation;string activeConversationId="",renderedSignature="";
        string personaId="";
        internal void SetPersona(string id){personaId=id??"";}
        bool Visible(ConversationDefinition conversation){return ProjectCatalogStore.VisibleForPet(conversation,personaId);}
        public void SetActiveConversation(string id){activeConversationId=id??"";}
        Popup hover=new Popup{Name="ProjectHoverDetails",Placement=PlacementMode.Right,HorizontalOffset=6,VerticalOffset=-8,StaysOpen=false,AllowsTransparency=true};ProjectDefinition hovered;
        // A short close delay keeps the details open while the pointer crosses the
        // gap between the row and the popup; without it the popup flickers.
        readonly System.Windows.Threading.DispatcherTimer hoverClose=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};bool hoverPinned;bool hoverLatched;
        public ProjectSidebar(ProjectCatalogStore store,ProjectCatalog catalog,Action<ProjectDefinition> projectActivated,Action<ProjectDefinition,ConversationDefinition> conversationActivated,Action<ProjectDefinition> editProject,Action<ProjectDefinition> openWorkspace,Action<ProjectDefinition> newConversation,Action<ProjectDefinition,ConversationDefinition> renameConversation=null,Action<ProjectDefinition> renameProject=null,Action<ProjectDefinition> archiveProject=null,Action<ProjectDefinition> deleteProject=null,Action<ProjectDefinition> restoreProject=null,Action<ProjectDefinition,ConversationDefinition> archiveConversation=null,Action<ProjectDefinition,ConversationDefinition> deleteConversation=null,Action<ProjectDefinition,ConversationDefinition> restoreConversation=null,Func<ProjectDefinition,ProjectDefinition,ConversationDefinition,bool> moveConversation=null)
        {
            this.store=store;this.catalog=catalog;this.projectActivated=projectActivated;this.conversationActivated=conversationActivated;this.editProject=editProject;this.openWorkspace=openWorkspace;this.newConversation=newConversation;this.renameConversation=renameConversation;this.renameProject=renameProject;this.archiveProject=archiveProject;this.deleteProject=deleteProject;this.restoreProject=restoreProject;this.archiveConversation=archiveConversation;this.deleteConversation=deleteConversation;this.restoreConversation=restoreConversation;this.moveConversation=moveConversation;
            hoverClose.Tick+=delegate{hoverClose.Stop();bool latched=hoverLatched;hoverLatched=false;if(latched)return;if(!hoverPinned)CloseHover();};
            Unloaded+=delegate{CloseHover();};
            Refresh();
        }
        internal Popup HoverDetails {get{return hover;}}
        internal void RefreshIfNeeded(){if(renderedSignature!=Signature())Refresh();}
        ContextMenu ProjectMenu(ProjectDefinition project)
        {
            var menu=ChatUi.Menu();
            if(!project.Archived){var pin=new MenuItem{Name="PinProject",Header=project.Pinned?"取消置顶":"置顶",Icon=UiIcons.Image("pin",AgentUi.Muted)};pin.Click+=delegate{try{ToggleProjectPin(project);}catch(Exception e){MessageBox.Show(e.Message,"置顶未保存");}};menu.Items.Add(pin);}
            if(project.Archived){if(restoreProject!=null){var restore=new MenuItem{Header="恢复项目",Icon=UiIcons.Image("folder-open",AgentUi.Muted)};restore.Click+=delegate{restoreProject(project);};menu.Items.Add(restore);}}
            else{
                var edit=new MenuItem{Header="编辑项目",Icon=UiIcons.Image("settings",AgentUi.Muted)};edit.Click+=delegate{editProject(project);};menu.Items.Add(edit);
                if(renameProject!=null){var rename=new MenuItem{Header="重命名项目",Icon=UiIcons.Image("edit",AgentUi.Muted),InputGestureText="Alt+Ctrl+R"};rename.Click+=delegate{renameProject(project);};menu.Items.Add(rename);}
                var add=new MenuItem{Header="新建对话",Icon=UiIcons.Image("chat",AgentUi.Muted)};add.Click+=delegate{newConversation(project);};menu.Items.Add(add);menu.Items.Add(new Separator());
            }
            var moveSection=new MenuItem{Header="分区",Name="MoveProjectSection",Icon=UiIcons.Image("list",AgentUi.Muted)};foreach(var section in catalog.Sections){var target=section;var item=new MenuItem{Header=section.Name,IsCheckable=true,IsChecked=project.SectionId==section.Id};item.Click+=delegate{try{MoveProjectToSection(project,target.Id);}catch(Exception e){MessageBox.Show(e.Message,"分区未保存");}};moveSection.Items.Add(item);}menu.Items.Add(moveSection);
            var open=new MenuItem{Header="在资源管理器中打开",Icon=UiIcons.Image("folder-open",AgentUi.Muted)};open.Click+=delegate{openWorkspace(project);};menu.Items.Add(open);
            if(!project.Archived&&archiveProject!=null){menu.Items.Add(new Separator());var archive=new MenuItem{Header="归档项目",Icon=UiIcons.Image("folder",AgentUi.Muted)};archive.Click+=delegate{archiveProject(project);};menu.Items.Add(archive);}
            if(deleteProject!=null){menu.Items.Add(new Separator());var delete=new MenuItem{Header="移除项目",Icon=UiIcons.Image("trash",AgentUi.Muted)};delete.Click+=delegate{deleteProject(project);};menu.Items.Add(delete);}return menu;
        }
        ContextMenu ConversationMenu(ProjectDefinition project,ConversationDefinition conversation)
        {
            var menu=ChatUi.Menu();if(!conversation.Archived){var pin=new MenuItem{Header=conversation.Pinned?"取消置顶":"置顶",Icon=UiIcons.Image("pin",AgentUi.Muted)};pin.Click+=delegate{try{ToggleConversationPin(conversation);}catch(Exception e){MessageBox.Show(e.Message,"置顶未保存");}};menu.Items.Add(pin);}if(conversation.Archived){if(restoreConversation!=null){var restore=new MenuItem{Header="恢复对话",Icon=UiIcons.Image("chat",AgentUi.Muted)};restore.Click+=delegate{restoreConversation(project,conversation);};menu.Items.Add(restore);}}else{if(renameConversation!=null){var rename=new MenuItem{Header="重命名",Icon=UiIcons.Image("edit",AgentUi.Muted),InputGestureText="Alt+Ctrl+R"};rename.Click+=delegate{renameConversation(project,conversation);};menu.Items.Add(rename);}var open=new MenuItem{Header="打开对话",Icon=UiIcons.Image("chat",AgentUi.Muted)};open.Click+=delegate{conversationActivated(project,conversation);};menu.Items.Add(open);if(archiveConversation!=null){var archive=new MenuItem{Header="归档",Icon=UiIcons.Image("folder",AgentUi.Muted)};archive.Click+=delegate{archiveConversation(project,conversation);};menu.Items.Add(archive);}}if(deleteConversation!=null){var delete=new MenuItem{Header="永久删除",Icon=UiIcons.Image("trash",AgentUi.Muted)};delete.Click+=delegate{deleteConversation(project,conversation);};menu.Items.Add(delete);}return menu;
        }        internal const string DragFormat="DeskPet.Conversation";
        static string[] ParseDrag(IDataObject data)
        {
            if(data==null || !data.GetDataPresent(DragFormat))return null;string value=data.GetData(DragFormat) as string;
            if(String.IsNullOrEmpty(value))return null;int split=value.IndexOf('/');if(split<=0 || split>=value.Length-1)return null;
            return new[]{value.Substring(0,split),value.Substring(split+1)};
        }
        void HookHover(Control row,ProjectDefinition project)
        {
            row.MouseEnter+=delegate{ShowHover(row,project);};row.MouseLeave+=delegate{HideHover();};row.GotKeyboardFocus+=delegate{ShowHover(row,project);};row.LostKeyboardFocus+=delegate{HideHover();};
            row.MouseLeftButtonDown+=delegate{CloseHover();};
        }
        void ShowHover(Control row,ProjectDefinition project)
        {
            // A rebuild just happened under the cursor: stay closed so the card
            // does not flash open again immediately.
            if(hoverLatched)return;
            hoverClose.Stop();hoverPinned=false;
            // Already showing this project: keep the existing card so a re-enter
            // does not rebuild (and re-flash) the popup.
            if(hover.IsOpen && hovered!=null && hovered.Id==project.Id && ReferenceEquals(hover.PlacementTarget,row))return;
            CloseHover();hovered=project;var body=new StackPanel{Margin=new Thickness(11),Width=250};var name=Ui.Text(project.Name,15,AgentUi.Ink);name.Name="ProjectHoverName";body.Children.Add(name);
            var count=Ui.Text(project.Conversations.Count+" 个会话 · "+project.Conversations.Count(c=>c.Status=="running")+" 个活跃会话",11,AgentUi.Muted);count.Name="ProjectHoverCount";body.Children.Add(count);
            var workspace=Ui.Text(project.Workspace,11,AgentUi.Muted);workspace.Name="ProjectHoverWorkspace";workspace.TextWrapping=TextWrapping.Wrap;body.Children.Add(workspace);
            var actions=new WrapPanel();var edit=AgentUi.Nav("编辑项目","EditProject");edit.Click+=delegate{editProject(project);};var open=AgentUi.Nav("打开工作目录","OpenProjectWorkspace");open.Click+=delegate{openWorkspace(project);};var add=AgentUi.Nav("新建会话","NewProjectConversation");add.Click+=delegate{newConversation(project);};actions.Children.Add(edit);actions.Children.Add(open);actions.Children.Add(add);body.Children.Add(actions);
            body.Background=Brushes.White;var card=new Border{Child=body,Background=Brushes.White,BorderBrush=AgentUi.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(2)};
            // Pointer over the card must count as still hovering the row, otherwise
            // moving toward the details closes and reopens them in a loop.
            card.MouseEnter+=delegate{hoverPinned=true;hoverClose.Stop();};
            card.MouseLeave+=delegate{hoverPinned=false;HideHover();};
            hover.Child=card;hover.PlacementTarget=row;hover.IsOpen=true;
        }
        void HideHover(){if(hover==null || !hover.IsOpen)return;hoverClose.Stop();hoverClose.Start();}
        void CloseHover(){hoverClose.Stop();hoverPinned=false;hoverLatched=false;hovered=null;if(hover!=null){hover.IsOpen=false;hover.Child=null;hover.PlacementTarget=null;}}
        public void Dispose(){CloseHover();}
        // Suppresses the next open after a rebuild so a refresh under the cursor
        // does not immediately pop the card back up.
        void LatchHover(){hoverLatched=true;hoverClose.Stop();hoverClose.Start();}
        internal void NewProjectRequested(string sectionId="projects")
        {
            var dialog=new NewProjectDialog();if(dialog.ShowDialog()!=true)return;try{CreateProject(dialog.ProjectName,dialog.Workspace,sectionId);}catch(Exception error){MessageBox.Show(error.Message,"新建项目未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        internal void CreateProject(string name,string workspace,string sectionId="projects")
        {
            var previous=catalog.Copy();ProjectDefinition project;try{project=ProjectCatalogStore.CreateProject(catalog,name,workspace,sectionId);store.Save(catalog);}catch(Exception){catalog.Projects.Clear();catalog.Projects.AddRange(previous.Projects);catalog.ActiveProjectId=previous.ActiveProjectId;throw;}Refresh();projectActivated(project);
        }
    }
    internal sealed class ConversationRenameDialog : Window
    {
        readonly TextBox name=Ui.Field();
        public string ConversationName {get{return name.Text.Trim();}}
        public ConversationRenameDialog(string initial,string title="重命名聊天",string label="对话标题")
        {
            Ui.Window(this,title,520,258);MinWidth=420;MinHeight=240;ResizeMode=ResizeMode.NoResize;var root=new Grid{Margin=new Thickness(26,22,26,22)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var heading=new StackPanel{Margin=new Thickness(0,0,36,14)};var caption=Ui.Text(title,21,Ui.Brush("#25282D"));caption.FontWeight=FontWeights.SemiBold;heading.Children.Add(caption);var hint=Ui.Text(title.Contains("分区")?"为分区设置一个易于识别的名称":"保持简短且易于识别",12,Ui.Brush("#96999E"));hint.Margin=new Thickness(0,5,0,0);heading.Children.Add(hint);root.Children.Add(heading);
            name.Name="ConversationTitle";name.MaxLength=80;name.Text=initial??"";name.MinHeight=44;name.VerticalAlignment=VerticalAlignment.Top;Grid.SetRow(name,1);root.Children.Add(name);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};var cancel=Ui.Button("取消");cancel.Name="CancelConversationTitle";cancel.IsCancel=true;cancel.Margin=new Thickness(0,0,10,0);cancel.Click+=delegate{DialogResult=false;Close();};var save=Ui.Button("保存");save.Name="SaveConversationTitle";save.IsDefault=true;save.Background=Ui.Brush("#202329");save.Foreground=Brushes.White;save.BorderBrush=save.Background;save.Click+=delegate{if(ConversationName.Length<1){name.Focus();return;}DialogResult=true;Close();};actions.Children.Add(cancel);actions.Children.Add(save);Grid.SetRow(actions,2);root.Children.Add(actions);DialogUi.Apply(this,root);Loaded+=delegate{name.Focus();name.SelectAll();};
        }
    }
    internal sealed class NewProjectDialog : Window
    {
        readonly TextBox name=new TextBox{MaxLength=80,BorderThickness=new Thickness(0),Background=Brushes.Transparent};
        string workspacePath="";readonly TextBlock notice=Ui.Text("",11,AgentUi.Muted);
        readonly TextBlock folderHint=Ui.Text("在此电脑上添加文件夹",13,AgentUi.Muted);
        readonly TextBlock folderPath=Ui.Text("",12,AgentUi.Muted);
        public string ProjectName {get{return name.Text.Trim();}}
        public string Workspace {get{return workspacePath.Trim();}}
        public bool RemoveRequested {get;private set;}public bool WorkSettingsRequested {get;private set;}
        public NewProjectDialog(ProjectDefinition initial=null)
        {
            Ui.Window(this,initial==null?"创建项目":"编辑项目",520,420);ResizeMode=ResizeMode.NoResize;
            var root=new Grid{Margin=new Thickness(22,18,22,20)};
            for(int i=0;i<6;i++)root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions[3].Height=new GridLength(1,GridUnitType.Star);

            var nameLabel=Ui.Text(initial==null?"创建项目":"编辑项目",20,AgentUi.Ink);nameLabel.FontWeight=FontWeights.SemiBold;nameLabel.Margin=new Thickness(0,0,0,10);Grid.SetRow(nameLabel,0);root.Children.Add(nameLabel);

            // Name field: folder glyph sits inside the field, the prompt fades in
            // the same way the composer placeholder does.
            var nameField=new Border{BorderBrush=AgentUi.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Background=Brushes.White,Padding=new Thickness(11,0,11,0),Margin=new Thickness(0,6,0,0),Height=42};
            var nameGrid=new Grid();nameGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});nameGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph=UiIcons.Image("folder",AgentUi.Muted);glyph.Width=glyph.Height=17;glyph.VerticalAlignment=VerticalAlignment.Center;nameGrid.Children.Add(glyph);
            var nameHost=new Grid();name.Name="NewProjectName";name.FontSize=13;name.VerticalContentAlignment=VerticalAlignment.Center;
            var namePrompt=Ui.Text("项目名称",13,Ui.Brush("#B8BCC2"));namePrompt.IsHitTestVisible=false;namePrompt.VerticalAlignment=VerticalAlignment.Center;
            nameHost.Children.Add(name);nameHost.Children.Add(namePrompt);Grid.SetColumn(nameHost,1);nameGrid.Children.Add(nameHost);
            nameField.Child=nameGrid;Grid.SetRow(nameField,1);root.Children.Add(nameField);
            Action syncPrompt=delegate{namePrompt.Visibility=String.IsNullOrEmpty(name.Text)?Visibility.Visible:Visibility.Collapsed;};
            name.TextChanged+=delegate{syncPrompt();};syncPrompt();

            var sourceLabel=Ui.Text("源文件夹",12,AgentUi.Muted);sourceLabel.Margin=new Thickness(0,16,0,0);Grid.SetRow(sourceLabel,2);root.Children.Add(sourceLabel);

            var drop=new Border{Name="ProjectSourceFolder",BorderBrush=Ui.Brush("#E6E8EC"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Background=Brushes.White,Margin=new Thickness(0,7,0,0),Padding=new Thickness(12)};
            var dropBody=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
            var pick=Ui.Button("");pick.Name="BrowseProjectWorkspace";pick.Background=Brushes.Transparent;pick.BorderBrush=Brushes.Transparent;pick.Cursor=System.Windows.Input.Cursors.Hand;pick.Padding=new Thickness(8,2,8,2);pick.Margin=new Thickness(0);pick.MinHeight=28;
            var pickRow=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            pickRow.Children.Add(folderHint);var chevron=UiIcons.Image("down",AgentUi.Muted);chevron.Width=chevron.Height=11;chevron.Margin=new Thickness(6,0,0,0);chevron.VerticalAlignment=VerticalAlignment.Center;pickRow.Children.Add(chevron);pick.Content=pickRow;
            pick.Click+=delegate{Browse();};dropBody.Children.Add(pick);
            folderPath.TextWrapping=TextWrapping.Wrap;folderPath.HorizontalAlignment=HorizontalAlignment.Center;folderPath.Margin=new Thickness(0,2,0,0);folderPath.Visibility=Visibility.Collapsed;dropBody.Children.Add(folderPath);
            var add=Ui.Button("");add.Name="AddProjectSourceFolder";add.Background=Ui.Brush("#F1F4F9");add.BorderBrush=Ui.Brush("#D8DEE7");add.Padding=new Thickness(13,5,13,5);add.Margin=new Thickness(0,10,0,0);add.Cursor=System.Windows.Input.Cursors.Hand;
            var addRow=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var addIcon=UiIcons.Image("folder",Ui.Ink);addIcon.Width=addIcon.Height=15;addIcon.Margin=new Thickness(0,0,6,0);addIcon.VerticalAlignment=VerticalAlignment.Center;addRow.Children.Add(addIcon);addRow.Children.Add(Ui.Text("添加",13,Ui.Ink));add.Content=addRow;
            add.Click+=delegate{Browse();};dropBody.Children.Add(add);if(initial!=null){var removeSource=Ui.Button("移除源文件夹");removeSource.Name="RemoveProjectSourceFolder";removeSource.Background=Brushes.Transparent;removeSource.BorderBrush=Brushes.Transparent;removeSource.FontSize=11;removeSource.Click+=delegate{workspacePath="";RefreshFolder();};dropBody.Children.Add(removeSource);}
            drop.Child=new ScrollViewer{Content=dropBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Center};
            // Dropping a folder is the fastest way to point at a source directory.
            drop.AllowDrop=true;
            drop.DragOver+=delegate(object sender,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Link;}else e.Effects=DragDropEffects.None;e.Handled=true;};
            drop.Drop+=delegate(object sender,DragEventArgs e){
                if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;var paths=e.Data.GetData(DataFormats.FileDrop) as string[];e.Handled=true;
                if(paths==null || paths.Length==0)return;string picked=paths.FirstOrDefault(System.IO.Directory.Exists);if(picked==null)return;
                workspacePath=picked;RefreshFolder();
            };
            Grid.SetRow(drop,3);root.Children.Add(drop);

            notice.Name="NewProjectNotice";notice.Margin=new Thickness(0,8,0,0);notice.MaxHeight=34;notice.TextWrapping=TextWrapping.Wrap;Grid.SetRow(notice,4);root.Children.Add(notice);

            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
            var cancel=Ui.Button("取消");cancel.Name="CancelCreateProject";cancel.IsCancel=true;cancel.Background=Brushes.Transparent;cancel.BorderBrush=Brushes.Transparent;cancel.Foreground=AgentUi.Muted;cancel.Margin=new Thickness(0,0,10,0);cancel.Click+=delegate{DialogResult=false;Close();};
            var create=Ui.Button(initial==null?"创建项目":"保存",true);create.Name="CreateProject";create.IsDefault=true;create.Background=Ui.Brush("#202329");create.BorderBrush=create.Background;create.Foreground=Brushes.White;create.FontWeight=FontWeights.SemiBold;
            create.Click+=delegate{
                if(ProjectName.Length<1 || ProjectName.Length>80){notice.Text="请输入 1～80 个字符的项目名称。";return;}
                if(!Path.IsPathRooted(Workspace) || !Directory.Exists(Workspace)){notice.Text="请选择已存在的绝对工作目录。";return;}
                DialogResult=true;Close();
            };
            actions.Children.Add(cancel);actions.Children.Add(create);
            var footer=new Grid();footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetColumn(actions,1);footer.Children.Add(actions);
            if(initial!=null){var extras=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom};var remove=Ui.Button("移除项目");remove.Name="RemoveEditedProject";remove.Background=Ui.Brush("#FBE9E9");remove.Foreground=Ui.Brush("#D34646");remove.BorderBrush=Brushes.Transparent;remove.Click+=delegate{RemoveRequested=true;DialogResult=false;Close();};extras.Children.Add(remove);var advanced=Ui.Button("工作设置");advanced.Name="ProjectAdvancedSettings";advanced.Click+=delegate{WorkSettingsRequested=true;DialogResult=false;Close();};extras.Children.Add(advanced);footer.Children.Add(extras);}
            Grid.SetRow(footer,5);root.Children.Add(footer);
            if(initial!=null){name.Text=initial.Name;workspacePath=initial.Workspace;RefreshFolder();}DialogUi.Apply(this,root);Loaded+=delegate{name.Focus();name.SelectAll();};
        }
        void Browse()
        {
            var picker=new System.Windows.Forms.FolderBrowserDialog{ShowNewFolderButton=false};if(!String.IsNullOrEmpty(workspacePath))picker.SelectedPath=workspacePath;
            if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK){workspacePath=picker.SelectedPath;RefreshFolder();}
        }
        void RefreshFolder()
        {
            bool has=!String.IsNullOrWhiteSpace(workspacePath);
            folderHint.Text=has?"已选择源文件夹":"在此电脑上添加文件夹";
            folderPath.Text=has?workspacePath:"";folderPath.Visibility=has?Visibility.Visible:Visibility.Collapsed;
            notice.Text="";
        }
    }
}
