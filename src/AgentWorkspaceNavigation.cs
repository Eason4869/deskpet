using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class AgentWorkspaceNavigation : Border,IDisposable
    {
        readonly PetWindow pet;readonly ConversationSession session;readonly string workspaceOverridePath;readonly string conversationId;readonly TextBlock project=Label(),task=Label(),identity=Label();ProjectSidebar projectNav;
        readonly Button taskButton;readonly Image avatar=new Image{Width=26,Height=26,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,10,0)};
        readonly Border taskDot=new Border{Width=6,Height=6,CornerRadius=new CornerRadius(3),Background=AgentUi.Muted,VerticalAlignment=VerticalAlignment.Center};
        bool disposed;
        public AgentWorkspaceNavigation(PetWindow companion,Action focusConversation,Action focusTask,ConversationSession sessionOverride=null,string workspaceOverride=null,string conversationIdOverride=null)
        {
            pet=companion;session=sessionOverride??pet.Conversation;workspaceOverridePath=workspaceOverride??"";conversationId=conversationIdOverride??"";Name="AgentWorkspaceSidebar";Background=Ui.Brush("#F8F9FB");BorderBrush=AgentUi.Line;BorderThickness=new Thickness(0,0,1,0);
            var layout=new DockPanel{Margin=new Thickness(8,20,8,12)};Child=layout;
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);

            var person=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(12,16,10,2)};person.Children.Add(avatar);identity.VerticalAlignment=VerticalAlignment.Center;identity.MaxWidth=110;person.Children.Add(identity);var settings=UiIcons.Button("settings","设置");settings.Name="WorkspaceProjectSettings";settings.Width=settings.Height=28;settings.MinWidth=0;settings.MinHeight=0;settings.Padding=new Thickness(5);settings.Margin=new Thickness(6,0,0,0);settings.Click+=delegate{pet.ShowWorkSettings();};person.Children.Add(settings);footer.Children.Add(person);
            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.Children.Add(body);
            var title=Ui.Text("DeskPet",18,AgentUi.Ink);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(12,0,12,23);Grid.SetRow(title,0);body.Children.Add(title);
            var conversation=Row("edit","新建对话","WorkspaceConversation");conversation.Click+=delegate{pet.NewActiveConversation();};Grid.SetRow(conversation,1);body.Children.Add(conversation);
            projectNav=new ProjectSidebar(pet.ProjectStore,pet.Projects,delegate(ProjectDefinition project){pet.ActivateProject(project);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.OpenProjectConversation(project,chatConversation);},delegate(ProjectDefinition project){pet.EditProject(project);},delegate(ProjectDefinition project){pet.OpenProjectWorkspace(project);},delegate(ProjectDefinition project){pet.NewProjectConversation(project);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.RenameConversation(project,chatConversation);},delegate(ProjectDefinition project){pet.RenameProject(project);},delegate(ProjectDefinition project){pet.ArchiveProject(project);},delegate(ProjectDefinition project){pet.DeleteProject(project);},delegate(ProjectDefinition project){pet.RestoreProject(project);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.ArchiveConversation(project,chatConversation);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.DeleteConversation(project,chatConversation);},delegate(ProjectDefinition project,ConversationDefinition chatConversation){pet.RestoreConversation(project,chatConversation);},delegate(ProjectDefinition from,ProjectDefinition to,ConversationDefinition chatConversation){return pet.MoveConversation(from,to,chatConversation);});projectNav.SetActiveConversation(conversationId);var projectScroll=Ui.Scroll(projectNav);projectScroll.Name="ProjectLabelsScroll";projectScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;projectScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;projectScroll.Margin=new Thickness(0,4,0,8);Grid.SetRow(projectScroll,2);body.Children.Add(projectScroll);

            taskButton=AgentUi.Nav("","WorkspaceTaskDetails");taskButton.Height=36;taskButton.Padding=new Thickness(40,7,12,7);taskButton.Background=Ui.Brush("#EBEDEF");
            var taskRow=new DockPanel{LastChildFill=true};DockPanel.SetDock(taskDot,Dock.Right);taskRow.Children.Add(taskDot);task.Margin=new Thickness(0,0,9,0);taskRow.Children.Add(task);taskButton.Content=taskRow;taskButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;taskButton.Click+=delegate{focusTask();};Grid.SetRow(taskButton,3);body.Children.Add(taskButton);
            session.AgentTaskChanged+=Refresh;session.StateChanged+=Refresh;session.HistoryChanged+=Refresh;Refresh();
        }
        static TextBlock Label(){return new TextBlock{FontSize=14,Foreground=AgentUi.Ink,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};}
        static Button Row(string icon,string text,string name){var label=Label();label.Text=text;return Row(icon,label,name);}
        static Button Row(string icon,TextBlock label,string name)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());var glyph=UiIcons.Image(icon,AgentUi.Ink);glyph.Width=glyph.Height=18;glyph.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(glyph);Grid.SetColumn(label,1);row.Children.Add(label);
            var button=AgentUi.Nav("",name);button.Height=38;button.FontSize=14;button.Padding=new Thickness(12,8,12,8);button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;return button;
        }
        internal void Refresh()
        {
            if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Refresh));return;}
            var current=session.CurrentAgentTask;string root=current!=null?current.Workspace:(workspaceOverridePath.Length>0?workspaceOverridePath:pet.Settings.AgentWorkspace??"");
            project.Text=String.IsNullOrEmpty(root)?"选择项目":Path.GetFileName(root.TrimEnd('\\','/'));if(String.IsNullOrEmpty(project.Text))project.Text=root;project.ToolTip=null;
            taskButton.Visibility=current==null?Visibility.Collapsed:Visibility.Visible;task.Text=current==null?"":current.Goal;taskButton.ToolTip=null;
            taskDot.Visibility=current!=null && current.Status=="running"?Visibility.Visible:Visibility.Collapsed;
            identity.Text=pet.Skin.Name;avatar.Source=SkinCatalog.GetAvatar(pet.Skin);if(projectNav!=null){projectNav.SetActiveConversation(conversationId);projectNav.RefreshIfNeeded();}
        }
        public void Dispose(){disposed=true;session.AgentTaskChanged-=Refresh;session.StateChanged-=Refresh;session.HistoryChanged-=Refresh;}
    }
}
