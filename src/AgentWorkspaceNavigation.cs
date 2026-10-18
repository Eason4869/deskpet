using System;
using System.Windows;
using System.Windows.Controls;using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class AgentWorkspaceNavigation : Border,IDisposable
    {
        readonly PetWindow pet;readonly ConversationSession session;readonly string conversationId;readonly TextBlock task=Label(),identity=Label();ProjectSidebar projectNav;
        readonly Button taskButton;readonly Image avatar=new Image{Name="WorkspacePetAvatar",Width=26,Height=26,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,10,0)};
        readonly Border taskDot=new Border{Width=6,Height=6,CornerRadius=new CornerRadius(3),Background=AgentUi.Muted,VerticalAlignment=VerticalAlignment.Center};
        bool disposed;SkinDefinition avatarSkin;readonly ConversationEntryTabs entries;readonly bool workEntry;
        public AgentWorkspaceNavigation(PetWindow companion,Action focusTask,ConversationSession sessionOverride=null,string conversationIdOverride=null,Action<bool> switchEntry=null)
        {
            pet=companion;session=sessionOverride??pet.Conversation;conversationId=conversationIdOverride??"";workEntry=pet.ConversationSettings(session).AgentEnabled;Name="AgentWorkspaceSidebar";Background=Ui.Brush("#F8F9FB");BorderBrush=AgentUi.Line;BorderThickness=new Thickness(0,0,1,0);
            var layout=new DockPanel{Margin=new Thickness(8,20,8,12)};Child=layout;
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);

            var person=new Grid{Visibility=Visibility.Collapsed,Name="WorkspaceIdentityRow",Margin=new Thickness(12,16,10,2)};person.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});person.ColumnDefinitions.Add(new ColumnDefinition());person.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});avatar.VerticalAlignment=VerticalAlignment.Center;person.Children.Add(avatar);identity.VerticalAlignment=VerticalAlignment.Center;identity.Margin=new Thickness(0,0,8,0);Grid.SetColumn(identity,1);person.Children.Add(identity);var settings=UiIcons.Button("settings","设置");settings.Name="WorkspaceProjectSettings";settings.Width=settings.Height=28;settings.MinWidth=0;settings.MinHeight=0;settings.Padding=new Thickness(5);settings.Margin=new Thickness(0);settings.VerticalAlignment=VerticalAlignment.Center;settings.HorizontalAlignment=HorizontalAlignment.Right;settings.Click+=delegate{pet.ShowWorkSettings();};Grid.SetColumn(settings,2);person.Children.Add(settings);footer.Children.Add(person);
            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});layout.Children.Add(body);
            var brandRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(12,0,12,23)};var appLogo=new Image{Name="WorkspaceAppLogo",Source=Ui.ApplicationIcon(),Width=29,Height=29,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};RenderOptions.SetBitmapScalingMode(appLogo,BitmapScalingMode.HighQuality);brandRow.Children.Add(appLogo);var title=Ui.Text("DeskPet",19,AgentUi.Ink);title.Name="WorkspaceAppTitle";title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;title.RenderTransform=new TranslateTransform(0,1.5);brandRow.Children.Add(title);var brandArea=new StackPanel();brandArea.Children.Add(brandRow);entries=new ConversationEntryTabs(switchEntry??delegate(bool work){});entries.Margin=new Thickness(8,0,8,12);brandArea.Children.Add(entries);Grid.SetRow(brandArea,0);body.Children.Add(brandArea);
            var conversation=Row("edit","新建对话","WorkspaceConversation");conversation.Click+=delegate{pet.NewActiveConversation();};Grid.SetRow(conversation,1);body.Children.Add(conversation);
            projectNav=new ProjectSidebar(pet.ProjectStore,pet.Projects,delegate(ProjectDefinition project){pet.ActivateProject(project);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.OpenProjectConversation(project,chatConversation);},delegate(ProjectDefinition project){pet.EditProject(project);},delegate(ProjectDefinition project){pet.OpenProjectWorkspace(project);},delegate(ProjectDefinition project){pet.NewProjectConversation(project);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.RenameConversation(project,chatConversation);},delegate(ProjectDefinition project){pet.RenameProject(project);},delegate(ProjectDefinition project){pet.ArchiveProject(project);},delegate(ProjectDefinition project){pet.DeleteProject(project);},delegate(ProjectDefinition project){pet.RestoreProject(project);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.ArchiveConversation(project,chatConversation);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.DeleteConversation(project,chatConversation);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.RestoreConversation(project,chatConversation);},delegate(ProjectDefinition from,ProjectDefinition to,ConversationDefinition chatConversation){return pet.MoveConversation(from,to,chatConversation);});projectNav.SetPersona(pet.Skin.Id);projectNav.SetEntry(workEntry);projectNav.SetActiveConversation(conversationId);var projectScroll=Ui.Scroll(projectNav);projectScroll.Name="ProjectLabelsScroll";projectScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;projectScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;projectScroll.Margin=new Thickness(0,4,0,8);Grid.SetRow(projectScroll,2);body.Children.Add(projectScroll);

            taskButton=AgentUi.Nav("","WorkspaceTaskDetails");taskButton.Height=36;taskButton.Padding=new Thickness(12,7,12,7);taskButton.Margin=new Thickness(12,2,12,0);taskButton.Background=Ui.Brush("#EBEDEF");
            var taskRow=new DockPanel{LastChildFill=true};DockPanel.SetDock(taskDot,Dock.Right);taskRow.Children.Add(taskDot);task.Margin=new Thickness(0,0,9,0);taskRow.Children.Add(task);taskButton.Content=taskRow;taskButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;taskButton.Click+=delegate{focusTask();};projectNav.SetCurrentTaskShortcut(taskButton,false);
            session.AgentTaskChanged+=Refresh;session.StateChanged+=Refresh;session.HistoryChanged+=Refresh;pet.ProjectsChanged+=Refresh;Refresh();
        }
        static TextBlock Label(){return new TextBlock{FontSize=14,Foreground=AgentUi.Ink,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};}
        static Button Row(string icon,string text,string name){var label=Label();label.Text=text;return Row(icon,label,name);}
        static Button Row(string icon,TextBlock label,string name)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());var glyph=icon=="edit"?ShellIcons.Image("new-chat",AgentUi.Ink):UiIcons.Image(icon,AgentUi.Ink);glyph.Width=glyph.Height=18;glyph.HorizontalAlignment=HorizontalAlignment.Left;glyph.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(glyph);Grid.SetColumn(label,1);row.Children.Add(label);
            var button=AgentUi.Nav("",name);button.Height=38;button.FontSize=14;button.Padding=new Thickness(12,8,12,8);button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;return button;
        }
        internal void Refresh()
        {
            if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Refresh));return;}
            entries.Refresh(workEntry,pet.WorkIsRunning);var current=session.CurrentAgentTask;
            taskButton.Visibility=current==null?Visibility.Collapsed:Visibility.Visible;task.Text=current==null?"":current.Goal;taskButton.ToolTip=null;
            taskDot.Visibility=current!=null && current.Status=="running"?Visibility.Visible:Visibility.Collapsed;
            identity.Text=pet.Skin.Name;if(!ReferenceEquals(avatarSkin,pet.Skin)){avatarSkin=pet.Skin;avatar.Source=SkinCatalog.GetAvatar(avatarSkin);}if(projectNav!=null){projectNav.SetPersona(pet.Skin.Id);projectNav.SetActiveConversation(conversationId);projectNav.RefreshIfNeeded();projectNav.SetCurrentTaskShortcut(taskButton,workEntry&&current!=null);}
        }
        public void Dispose(){if(disposed)return;disposed=true;if(projectNav!=null)projectNav.Dispose();session.AgentTaskChanged-=Refresh;session.StateChanged-=Refresh;session.HistoryChanged-=Refresh;pet.ProjectsChanged-=Refresh;}
    }
}
