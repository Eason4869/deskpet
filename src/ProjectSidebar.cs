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
    internal sealed class ProjectSidebar : StackPanel
    {
        readonly ProjectCatalogStore store;readonly ProjectCatalog catalog;
        readonly Action<ProjectDefinition> projectActivated;readonly Action<ProjectDefinition,ConversationDefinition> conversationActivated;
        readonly Action<ProjectDefinition> editProject,openWorkspace;readonly Action<ProjectDefinition> newConversation;readonly Action<ProjectDefinition,ConversationDefinition> renameConversation;readonly Action<ProjectDefinition> renameProject;readonly Action<ProjectDefinition> archiveProject,deleteProject,restoreProject;readonly Action<ProjectDefinition,ConversationDefinition> archiveConversation,deleteConversation,restoreConversation;readonly Func<ProjectDefinition,ProjectDefinition,ConversationDefinition,bool> moveConversation;string activeConversationId="",renderedSignature="";
        public void SetActiveConversation(string id){activeConversationId=id??"";}
        Popup hover=new Popup{Name="ProjectHoverDetails",Placement=PlacementMode.Right,HorizontalOffset=6,VerticalOffset=-8,StaysOpen=false,AllowsTransparency=true};ProjectDefinition hovered;readonly HashSet<string> collapsedProjects=new HashSet<string>();
        // A short close delay keeps the details open while the pointer crosses the
        // gap between the row and the popup; without it the popup flickers.
        readonly System.Windows.Threading.DispatcherTimer hoverClose=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};bool hoverPinned;bool hoverLatched;
        public ProjectSidebar(ProjectCatalogStore store,ProjectCatalog catalog,Action<ProjectDefinition> projectActivated,Action<ProjectDefinition,ConversationDefinition> conversationActivated,Action<ProjectDefinition> editProject,Action<ProjectDefinition> openWorkspace,Action<ProjectDefinition> newConversation,Action<ProjectDefinition,ConversationDefinition> renameConversation=null,Action<ProjectDefinition> renameProject=null,Action<ProjectDefinition> archiveProject=null,Action<ProjectDefinition> deleteProject=null,Action<ProjectDefinition> restoreProject=null,Action<ProjectDefinition,ConversationDefinition> archiveConversation=null,Action<ProjectDefinition,ConversationDefinition> deleteConversation=null,Action<ProjectDefinition,ConversationDefinition> restoreConversation=null,Func<ProjectDefinition,ProjectDefinition,ConversationDefinition,bool> moveConversation=null)
        {
            this.store=store;this.catalog=catalog;this.projectActivated=projectActivated;this.conversationActivated=conversationActivated;this.editProject=editProject;this.openWorkspace=openWorkspace;this.newConversation=newConversation;this.renameConversation=renameConversation;this.renameProject=renameProject;this.archiveProject=archiveProject;this.deleteProject=deleteProject;this.restoreProject=restoreProject;this.archiveConversation=archiveConversation;this.deleteConversation=deleteConversation;this.restoreConversation=restoreConversation;this.moveConversation=moveConversation;
            hoverClose.Tick+=delegate{hoverClose.Stop();bool latched=hoverLatched;hoverLatched=false;if(latched)return;if(!hoverPinned)CloseHover();};
            Refresh();
        }
        internal Popup HoverDetails {get{return hover;}}
        internal void RefreshIfNeeded(){if(renderedSignature!=Signature())Refresh();}
        string Signature()
        {
            var parts=new List<string>();parts.Add(activeConversationId??"");foreach(var project in catalog.Projects.Where(p=>!p.Archived)){parts.Add(project.Id+"="+project.Name+"="+project.Conversations.Count);foreach(var conversation in project.Conversations.Where(c=>!c.Archived))parts.Add(conversation.Id+"="+conversation.Name+"="+conversation.Status);}parts.Add(String.Join(",",collapsedProjects.OrderBy(x=>x,StringComparer.Ordinal)));return String.Join("|",parts);
        }
        internal void Refresh()
        {
            // Rows are rebuilt in place: close the hover card first or it keeps
            // pointing at a button that just left the visual tree (flicker/stale).
            // Only latch when a card was actually open — otherwise a fresh sidebar
            // would refuse the first hover for the debounce window.
            bool wasShowing=hover!=null && hover.IsOpen;
            CloseHover();
            if(wasShowing)LatchHover();
            Children.Clear();
            // Mirror the project row grid so the "+" sits above the count column
            // instead of floating against the section title.
            var heading=new Grid{Margin=new Thickness(12,8,12,6)};heading.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});heading.ColumnDefinitions.Add(new ColumnDefinition());heading.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});
            var title=Ui.Text("项目",12,AgentUi.Muted);title.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,1);heading.Children.Add(title);
            var add=UiIcons.Button("add","新建项目");add.ToolTip=null;add.Name="NewProject";add.Width=add.Height=24;add.Padding=new Thickness(3);add.Margin=new Thickness(0);add.VerticalAlignment=VerticalAlignment.Center;add.HorizontalAlignment=HorizontalAlignment.Center;add.Click+=delegate{NewProjectRequested();};Grid.SetColumn(add,2);heading.Children.Add(add);Children.Add(heading);
            foreach(var project in catalog.Projects.Where(p=>!p.Archived))
            {
                bool activeProject=project.Id==catalog.ActiveProjectId,collapsed=collapsedProjects.Contains(project.Id);
                var row=new Grid{Margin=new Thickness(12,2,12,1)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});
                var toggle=AgentUi.Nav("","ToggleProject_"+project.Id);toggle.Name="ToggleProject";toggle.Width=24;toggle.Height=34;toggle.MinHeight=34;toggle.Padding=new Thickness(4);toggle.Margin=new Thickness(0);var folderToggle=UiIcons.Image(collapsed?"folder":"folder-open",AgentUi.Muted);folderToggle.Width=folderToggle.Height=17;toggle.Content=folderToggle;toggle.Click+=delegate{if(!collapsedProjects.Remove(project.Id))collapsedProjects.Add(project.Id);Refresh();};Grid.SetColumn(toggle,0);row.Children.Add(toggle);
                var button=AgentUi.Nav("",activeProject?"WorkspaceProject":"WorkspaceProject_"+project.Id);if(activeProject)button.Name="WorkspaceProject";button.Height=Double.NaN;button.MinHeight=36;button.Padding=new Thickness(7,6,7,6);if(activeProject)button.Background=Ui.Brush("#E9EDF3");
                var targetProject=project;row.AllowDrop=true;
                var restBackground=activeProject?Ui.Brush("#E9EDF3"):Brushes.Transparent;
                row.DragOver+=delegate(object sender,DragEventArgs e){if(!e.Data.GetDataPresent(DragFormat)){e.Effects=DragDropEffects.None;e.Handled=true;return;}string[] moved=ParseDrag(e.Data);if(moved==null || moved[0]==targetProject.Id){e.Effects=DragDropEffects.None;button.Background=restBackground;}else{e.Effects=DragDropEffects.Move;button.Background=Ui.Brush("#D8E4FA");}e.Handled=true;};
                row.DragLeave+=delegate{button.Background=restBackground;};
                row.Drop+=delegate(object sender,DragEventArgs e){
                    if(!e.Data.GetDataPresent(DragFormat)){return;}string[] moved=ParseDrag(e.Data);e.Handled=true;
                    if(moved==null || moved[0]==targetProject.Id)return;
                    var from=catalog.Projects.FirstOrDefault(x=>x.Id==moved[0]);var chat=moved==null?null:(from==null?null:from.Conversations.FirstOrDefault(c=>c.Id==moved[1]));
                    if(from==null || chat==null)return;
                    bool movedOk=true;
                    if(moveConversation!=null)movedOk=moveConversation(from,targetProject,chat);
                    else try{ProjectCatalogStore.MoveConversation(catalog,from.Id,targetProject.Id,chat.Id);}catch(Exception){movedOk=false;}
                    if(!movedOk)return;
                    Refresh();if(conversationActivated!=null)conversationActivated(targetProject,chat);
                };
                var projectLabel=Ui.Text(project.Name,13,AgentUi.Ink);projectLabel.FontWeight=activeProject?FontWeights.SemiBold:FontWeights.Normal;projectLabel.TextWrapping=TextWrapping.Wrap;projectLabel.TextTrimming=TextTrimming.None;projectLabel.Tag=project.Workspace;projectLabel.VerticalAlignment=VerticalAlignment.Center;button.Content=projectLabel;button.Click+=delegate{projectActivated(project);};button.ContextMenu=ProjectMenu(project);HookHover(button,project);Grid.SetColumn(button,1);row.Children.Add(button);
                var count=Ui.Text(project.Conversations.Count.ToString(),10,AgentUi.Muted);count.Name="ProjectConversationCount";count.HorizontalAlignment=HorizontalAlignment.Center;count.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(count,2);row.Children.Add(count);Children.Add(row);
                if(collapsed)continue;
                foreach(var conversation in project.Conversations.Where(c=>!c.Archived))
                {
                    bool activeChat=conversation.Id==activeConversationId;string chatName=activeChat?"ProjectConversation":"WorkspaceConversation_"+conversation.Id;var chatRow=new Grid{Margin=new Thickness(9,0,15,1)};chatRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(22)});chatRow.ColumnDefinitions.Add(new ColumnDefinition());chatRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});
                    var chat=AgentUi.Nav("",chatName);chat.Height=Double.NaN;chat.MinHeight=31;chat.Padding=new Thickness(4,4,7,4);chat.Margin=new Thickness(0);if(activeChat)chat.Background=Ui.Brush("#E1E5EA");
                    {bool dragArmed=false;Point dragOrigin=default(Point);var dragProject=project;var dragChat=conversation;
                        chat.MouseLeftButtonDown+=delegate{dragArmed=true;dragOrigin=Mouse.GetPosition(chat);};
                        chat.MouseLeftButtonUp+=delegate{dragArmed=false;};
                        chat.LostMouseCapture+=delegate{dragArmed=false;};
                        chat.MouseMove+=delegate{
                            if(!dragArmed)return;Point now=Mouse.GetPosition(chat);
                            if(Math.Abs(now.X-dragOrigin.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y-dragOrigin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                            dragArmed=false;CloseHover();
                            try{DragDrop.DoDragDrop(chat,new DataObject(DragFormat,dragProject.Id+"/"+dragChat.Id),DragDropEffects.Move);}catch(InvalidOperationException){}
                        };}
                    var chatContent=new DockPanel{LastChildFill=true};var bubble=UiIcons.Image("chat",activeChat?AgentUi.Ink:AgentUi.Muted);bubble.Width=bubble.Height=15;bubble.Margin=new Thickness(0,0,7,0);DockPanel.SetDock(bubble,Dock.Left);chatContent.Children.Add(bubble);var chatLabel=Ui.Text(conversation.Name,12,AgentUi.Ink);chatLabel.FontWeight=activeChat?FontWeights.SemiBold:FontWeights.Normal;chatLabel.TextWrapping=TextWrapping.Wrap;chatLabel.TextTrimming=TextTrimming.None;chatLabel.VerticalAlignment=VerticalAlignment.Center;chatContent.Children.Add(chatLabel);chat.Content=chatContent;chat.Click+=delegate{conversationActivated(project,conversation);};chat.MouseDoubleClick+=delegate{if(renameConversation!=null)renameConversation(project,conversation);};chat.ContextMenu=ConversationMenu(project,conversation);Grid.SetColumn(chat,0);Grid.SetColumnSpan(chat,2);chatRow.Children.Add(chat);
                    if(activeChat){var dot=new Border{Name="ActiveConversationDot",Width=6,Height=6,CornerRadius=new CornerRadius(3),Background=Ui.Brush("#357FF5"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(dot,2);chatRow.Children.Add(dot);}Children.Add(chatRow);
                }
            }
            var recent=Ui.Text("最近",12,AgentUi.Muted);recent.Margin=new Thickness(12,18,12,6);Children.Add(recent);
            foreach(var project in catalog.Projects.Where(p=>!p.Archived).OrderByDescending(p=>p.LastOpenedUtcTicks).Take(5))
            {
                var item=AgentUi.Nav(project.Name,"RecentProject_"+project.Id);item.Height=30;item.Margin=new Thickness(12,2,12,2);item.Click+=delegate{projectActivated(project);};Children.Add(item);
            }
            var archivedProjects=catalog.Projects.Where(p=>p.Archived).ToList();var archivedConversations=catalog.Projects.Where(p=>!p.Archived).SelectMany(p=>p.Conversations.Where(c=>c.Archived).Select(c=>new{Project=p,Conversation=c})).ToList();if(archivedProjects.Count>0 || archivedConversations.Count>0){var archivedHeading=Ui.Text("归档",12,AgentUi.Muted);archivedHeading.Margin=new Thickness(12,18,12,6);Children.Add(archivedHeading);foreach(var project in archivedProjects){var item=AgentUi.Nav(project.Name,"ArchivedProject_"+project.Id);item.Height=30;item.Margin=new Thickness(12,2,12,2);item.ContextMenu=ProjectMenu(project);item.Click+=delegate{projectActivated(project);};Children.Add(item);}foreach(var pair in archivedConversations){var item=AgentUi.Nav(pair.Conversation.Name,"ArchivedConversation_"+pair.Conversation.Id);item.Height=30;item.Margin=new Thickness(30,2,15,2);item.ContextMenu=ConversationMenu(pair.Project,pair.Conversation);item.Click+=delegate{conversationActivated(pair.Project,pair.Conversation);};Children.Add(item);}}
            renderedSignature=Signature();
        }
        ContextMenu ProjectMenu(ProjectDefinition project)
        {
            var menu=ChatUi.Menu();if(project.Archived){if(restoreProject!=null){var restore=new MenuItem{Header="恢复项目",Icon=UiIcons.Image("folder-open",AgentUi.Muted)};restore.Click+=delegate{restoreProject(project);};menu.Items.Add(restore);}}else{if(renameProject!=null){var rename=new MenuItem{Header="重命名项目",Icon=UiIcons.Image("edit",AgentUi.Muted),InputGestureText="Alt+Ctrl+R"};rename.Click+=delegate{renameProject(project);};menu.Items.Add(rename);}var edit=new MenuItem{Header="编辑项目",Icon=UiIcons.Image("settings",AgentUi.Muted)};edit.Click+=delegate{editProject(project);};menu.Items.Add(edit);var add=new MenuItem{Header="新建对话",Icon=UiIcons.Image("chat",AgentUi.Muted)};add.Click+=delegate{newConversation(project);};menu.Items.Add(add);if(archiveProject!=null){var archive=new MenuItem{Header="归档项目",Icon=UiIcons.Image("folder",AgentUi.Muted)};archive.Click+=delegate{archiveProject(project);};menu.Items.Add(archive);}}if(deleteProject!=null){var delete=new MenuItem{Header="永久删除项目",Icon=UiIcons.Image("trash",AgentUi.Muted)};delete.Click+=delegate{deleteProject(project);};menu.Items.Add(delete);}return menu;
        }
        ContextMenu ConversationMenu(ProjectDefinition project,ConversationDefinition conversation)
        {
            var menu=ChatUi.Menu();if(conversation.Archived){if(restoreConversation!=null){var restore=new MenuItem{Header="恢复对话",Icon=UiIcons.Image("chat",AgentUi.Muted)};restore.Click+=delegate{restoreConversation(project,conversation);};menu.Items.Add(restore);}}else{if(renameConversation!=null){var rename=new MenuItem{Header="重命名",Icon=UiIcons.Image("edit",AgentUi.Muted),InputGestureText="Alt+Ctrl+R"};rename.Click+=delegate{renameConversation(project,conversation);};menu.Items.Add(rename);}var open=new MenuItem{Header="打开对话",Icon=UiIcons.Image("chat",AgentUi.Muted)};open.Click+=delegate{conversationActivated(project,conversation);};menu.Items.Add(open);if(archiveConversation!=null){var archive=new MenuItem{Header="归档",Icon=UiIcons.Image("folder",AgentUi.Muted)};archive.Click+=delegate{archiveConversation(project,conversation);};menu.Items.Add(archive);}}if(deleteConversation!=null){var delete=new MenuItem{Header="永久删除",Icon=UiIcons.Image("trash",AgentUi.Muted)};delete.Click+=delegate{deleteConversation(project,conversation);};menu.Items.Add(delete);}return menu;
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
            var count=Ui.Text(project.Conversations.Count+" 个会话 · "+project.Conversations.Count(c=>c.Status=="active")+" 个活跃会话",11,AgentUi.Muted);count.Name="ProjectHoverCount";body.Children.Add(count);
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
        void CloseHover(){hoverClose.Stop();hoverPinned=false;hoverLatched=false;hovered=null;if(hover!=null)hover.IsOpen=false;}
        // Suppresses the next open after a rebuild so a refresh under the cursor
        // does not immediately pop the card back up.
        void LatchHover(){hoverLatched=true;hoverClose.Stop();hoverClose.Start();}
        internal void NewProjectRequested()
        {
            var dialog=new NewProjectDialog();if(dialog.ShowDialog()!=true)return;try{CreateProject(dialog.ProjectName,dialog.Workspace);}catch(Exception error){MessageBox.Show(error.Message,"新建项目未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        internal void CreateProject(string name,string workspace)
        {
            var previous=catalog.Copy();ProjectDefinition project;try{project=ProjectCatalogStore.CreateProject(catalog,name,workspace);store.Save(catalog);}catch(Exception){catalog.Projects.Clear();catalog.Projects.AddRange(previous.Projects);catalog.ActiveProjectId=previous.ActiveProjectId;throw;}Refresh();projectActivated(project);
        }
    }
    internal sealed class ConversationRenameDialog : Window
    {
        readonly TextBox name=new TextBox{MaxLength=80};
        public string ConversationName {get{return name.Text.Trim();}}
        public ConversationRenameDialog(string initial,string title="重命名对话",string label="对话标题")
        {
            Ui.Window(this,title,440,190);ResizeMode=ResizeMode.NoResize;var root=new StackPanel{Margin=new Thickness(20)};
            root.Children.Add(Ui.Text(label,12));name.Name="ConversationTitle";name.Text=initial??"";root.Children.Add(name);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
            var cancel=Ui.Button("取消");cancel.Click+=delegate{DialogResult=false;Close();};
            var save=Ui.Button("保存",true);save.Name="SaveConversationTitle";save.Click+=delegate{if(ConversationName.Length<1 || ConversationName.Length>80)return;DialogResult=true;Close();};
            actions.Children.Add(cancel);actions.Children.Add(save);root.Children.Add(actions);Content=root;name.Focus();name.SelectAll();
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
        public NewProjectDialog()
        {
            Ui.Window(this,"创建项目",520,332);ResizeMode=ResizeMode.NoResize;
            var root=new Grid{Margin=new Thickness(22,18,22,16)};
            for(int i=0;i<6;i++)root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

            var nameLabel=Ui.Text("项目名称",12,AgentUi.Muted);Grid.SetRow(nameLabel,0);root.Children.Add(nameLabel);

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

            var drop=new Border{Name="ProjectSourceFolder",BorderBrush=Ui.Brush("#D8DEE7"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Background=Ui.Brush("#FAFBFC"),MinHeight=124,Margin=new Thickness(0,7,0,0),Padding=new Thickness(12)};
            var dropBody=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
            var pick=Ui.Button("");pick.Name="BrowseProjectWorkspace";pick.Background=Brushes.Transparent;pick.BorderBrush=Brushes.Transparent;pick.Cursor=System.Windows.Input.Cursors.Hand;pick.Padding=new Thickness(8,2,8,2);pick.Margin=new Thickness(0);pick.MinHeight=28;
            var pickRow=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            pickRow.Children.Add(folderHint);var chevron=UiIcons.Image("down",AgentUi.Muted);chevron.Width=chevron.Height=11;chevron.Margin=new Thickness(6,0,0,0);chevron.VerticalAlignment=VerticalAlignment.Center;pickRow.Children.Add(chevron);pick.Content=pickRow;
            pick.Click+=delegate{Browse();};dropBody.Children.Add(pick);
            folderPath.TextWrapping=TextWrapping.Wrap;folderPath.HorizontalAlignment=HorizontalAlignment.Center;folderPath.Margin=new Thickness(0,2,0,0);folderPath.Visibility=Visibility.Collapsed;dropBody.Children.Add(folderPath);
            var add=Ui.Button("");add.Name="AddProjectSourceFolder";add.Background=Ui.Brush("#F1F4F9");add.BorderBrush=Ui.Brush("#D8DEE7");add.Padding=new Thickness(13,5,13,5);add.Margin=new Thickness(0,10,0,0);add.Cursor=System.Windows.Input.Cursors.Hand;
            var addRow=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var addIcon=UiIcons.Image("folder",Ui.Ink);addIcon.Width=addIcon.Height=15;addIcon.Margin=new Thickness(0,0,6,0);addIcon.VerticalAlignment=VerticalAlignment.Center;addRow.Children.Add(addIcon);addRow.Children.Add(Ui.Text("添加",13,Ui.Ink));add.Content=addRow;
            add.Click+=delegate{Browse();};dropBody.Children.Add(add);
            drop.Child=dropBody;
            // Dropping a folder is the fastest way to point at a source directory.
            drop.AllowDrop=true;
            drop.DragOver+=delegate(object sender,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Link;}else e.Effects=DragDropEffects.None;e.Handled=true;};
            drop.Drop+=async delegate(object sender,DragEventArgs e){
                if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;var paths=e.Data.GetData(DataFormats.FileDrop) as string[];e.Handled=true;
                if(paths==null || paths.Length==0)return;string picked=paths.FirstOrDefault(System.IO.Directory.Exists);if(picked==null)return;
                workspacePath=picked;RefreshFolder();
            };
            Grid.SetRow(drop,3);root.Children.Add(drop);

            notice.Name="NewProjectNotice";notice.Margin=new Thickness(0,10,0,0);Grid.SetRow(notice,4);root.Children.Add(notice);

            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
            var cancel=Ui.Button("取消");cancel.Margin=new Thickness(0,0,10,0);cancel.Click+=delegate{DialogResult=false;Close();};
            var create=Ui.Button("创建项目",true);create.Name="CreateProject";create.Background=Ui.Brush("#202329");create.BorderBrush=create.Background;create.Foreground=Brushes.White;create.FontWeight=FontWeights.SemiBold;
            create.Click+=delegate{
                if(ProjectName.Length<1 || ProjectName.Length>80){notice.Text="请输入 1～80 个字符的项目名称。";return;}
                if(!Path.IsPathRooted(Workspace) || !Directory.Exists(Workspace)){notice.Text="请选择已存在的绝对工作目录。";return;}
                DialogResult=true;Close();
            };
            actions.Children.Add(cancel);actions.Children.Add(create);
            Grid.SetRow(actions,5);root.Children.Add(actions);
            Content=root;name.Focus();
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
