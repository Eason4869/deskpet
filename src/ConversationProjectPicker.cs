using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        Button projectPickerAnchor;
        internal bool CanChooseProject {get{return workspace!=null&&workspace.IsDraft&&workspace.EffectiveSettings().AgentEnabled;}}
        internal void OpenProjectSelection(){if(CanChooseProject&&projectPickerAnchor!=null)OpenProjectPicker(projectPickerAnchor,workspace);}
        void RefreshProjectPicker()
        {
            if(composer==null)return;
            if(workspace==null||!workspace.EffectiveSettings().AgentEnabled){composer.SetProjectHeader(null);projectPickerAnchor=null;return;}
            var current=workspace;
            var row=new DockPanel{LastChildFill=true};var settings=UiIcons.Button("settings","项目工作设置");settings.Name="ConversationProjectSettings";settings.Width=settings.Height=26;settings.Padding=new Thickness(4);settings.Margin=new Thickness(0);settings.Click+=delegate{if(current.ProjectId==ProjectCatalogStore.UnassignedProjectId)pet.ShowWorkSettings();else pet.EditProject(current.Project);};DockPanel.SetDock(settings,Dock.Right);row.Children.Add(settings);
            var button=Ui.Button("");projectPickerAnchor=button;button.Name="ConversationProjectPicker";button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;button.HorizontalAlignment=HorizontalAlignment.Left;button.Padding=new Thickness(5,2,7,2);button.MinHeight=26;button.IsEnabled=current.IsDraft;
            var content=new StackPanel{Orientation=Orientation.Horizontal};var icon=UiIcons.Image("folder",AgentUi.Muted);icon.Width=icon.Height=15;icon.Margin=new Thickness(0,0,7,0);icon.VerticalAlignment=VerticalAlignment.Center;content.Children.Add(icon);content.Children.Add(Ui.InlineText(current.ProjectId==ProjectCatalogStore.UnassignedProjectId?(current.IsDraft?"选择项目":"无项目"):current.Project.Name,12,AgentUi.Muted));button.Content=content;row.Children.Add(button);
            var panel=new Border{Name="ConversationProjectHeader",Child=row,Background=Ui.Brush("#F2F3F5"),CornerRadius=new CornerRadius(14,14,0,0),Padding=new Thickness(10,5,10,13),Margin=new Thickness(2,6,2,-9)};
            button.Click+=delegate{OpenProjectPicker(button,current);};composer.SetProjectHeader(panel);
        }
        void OpenProjectPicker(Button anchor,ConversationWorkspace current)
        {
            if(!current.IsDraft||!current.EffectiveSettings().AgentEnabled)return;
            var popup=new Popup{Name="ConversationProjectMenu",Placement=PlacementMode.Top,PlacementTarget=anchor,AllowsTransparency=true,StaysOpen=false};
            var body=new StackPanel{Width=310,Margin=new Thickness(8)};var search=Ui.Field("");search.Name="ProjectPickerSearch";search.ToolTip="搜索项目";search.Margin=new Thickness(2,2,2,6);body.Children.Add(search);var list=new StackPanel();body.Children.Add(new ScrollViewer{Content=list,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            Action<ProjectDefinition> select=delegate(ProjectDefinition project){current.FollowProject(project??ProjectCatalogStore.UnassignedProject(pet.Projects,pet.Settings));popup.IsOpen=false;RefreshProjectPicker();taskPanel.SetTaskWorkspace(current.ProjectWorkspace);input.Focus();};
            Action render=delegate{
                list.Children.Clear();var none=AgentUi.Nav("无项目","SelectNoProject");none.Click+=delegate{select(null);};list.Children.Add(none);
                foreach(var project in pet.Projects.Projects.Where(p=>!p.Archived&&(String.IsNullOrWhiteSpace(search.Text)||p.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0))){var choice=project;var item=AgentUi.Nav(project.Name,"SelectConversationProject");item.Tag=project.Id;item.Click+=delegate{select(choice);};list.Children.Add(item);}
            };search.TextChanged+=delegate{render();};render();body.Children.Add(new Separator{Margin=new Thickness(2,5,2,4)});var create=AgentUi.Nav("+  新建项目","NewProjectFromPicker");create.Click+=delegate{popup.IsOpen=false;try{var project=pet.CreateProjectForConversation();if(project!=null)select(project);}catch(Exception error){ShowStatus(error.Message);}};body.Children.Add(create);
            popup.Child=new Border{Child=body,Background=ThemeManager.Surface,BorderBrush=Ui.Brush("#E6E8EC"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Margin=new Thickness(3)};anchor.Unloaded+=delegate{popup.IsOpen=false;};popup.Opened+=delegate{search.Focus();};popup.IsOpen=true;
        }
    }
}
