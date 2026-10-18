using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Dafeiyu
{
    internal sealed partial class ProjectSidebar
    {
        const string ProjectDragFormat="DeskPet.Project";
        bool archiveExpanded;
        string Signature()
        {
            return personaId+"|"+String.Join(",",catalog.QuickConversations.Select(c=>c.Id+"="+c.Name+"="+c.PetId+"="+c.HasUnreadCompletion+"="+c.Pinned+"="+c.Archived+"="+c.Status))+"|"+String.Join(",",catalog.SidebarSectionExpansion.Select(x=>x.Key+"="+x.Value))+"|"+activeConversationId+"|"+archiveExpanded+"|"+catalog.RecentIncludeProjects+"|"+String.Join(",",catalog.UnassignedConversations.Select(c=>c.Id+"="+c.Name+"="+c.Status+"="+c.HasUnreadCompletion+"="+c.Pinned+"="+c.Archived))+"|"+String.Join("|",catalog.Sections.Select(s=>s.Id+"="+s.Name))+"|"+String.Join("|",catalog.Projects.Select(p=>p.Id+"="+p.Name+"="+p.SectionId+"="+p.Pinned+"="+p.Archived+"="+String.Join(",",p.Conversations.Select(c=>c.Id+"="+c.Name+"="+c.Status+"="+c.Archived+"="+c.HasUnreadCompletion+"="+c.Pinned))))+"|"+String.Join(",",catalog.SidebarExpansion.OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value));
        }
        internal SidebarSection CreateSection(string name)
        {
            var section=ProjectCatalogStore.CreateSection(catalog,name);try{store.Save(catalog);}catch{catalog.Sections.Remove(section);throw;}Refresh();return section;
        }
        internal void RenameSection(string id,string name)
        {
            var section=catalog.Sections.Single(s=>s.Id==id);string previous=section.Name;try{ProjectCatalogStore.RenameSection(catalog,id,name);store.Save(catalog);}catch{section.Name=previous;throw;}Refresh();
        }
        internal void MoveProjectToSection(ProjectDefinition project,string id)
        {
            if(!catalog.Sections.Any(s=>s.Id==id))throw new ArgumentException("分区不存在。");string previous=project.SectionId;try{project.SectionId=id;store.Save(catalog);}catch{project.SectionId=previous;throw;}Refresh();
        }
        internal void Refresh()
        {
            bool wasShowing=hover.IsOpen;CloseHover();if(wasShowing)LatchHover();Children.Clear();
            AddPinned();
            foreach(var section in catalog.Sections)
            {
                var header=new Grid{Margin=new Thickness(12,18,12,6),Background=Brushes.Transparent};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});
                var title=SectionToggle(section.Id,section.Name);header.Children.Add(title);
                var menu=ChatUi.Menu();var rename=new MenuItem{Header="重命名分区"};var currentSection=section;rename.Click+=delegate{var dialog=new ConversationRenameDialog(currentSection.Name,"重命名分区","分区名称");if(dialog.ShowDialog()==true)try{RenameSection(currentSection.Id,dialog.ConversationName);}catch(Exception e){MessageBox.Show(e.Message,"分区未保存");}};menu.Items.Add(rename);menu.Items.Add(new Separator());menu.Items.Add(NewSectionMenuItem());header.ContextMenu=menu;var more=UiIcons.Button("more","分区更多选项");more.Name="SectionMore_"+section.Id;more.ContextMenu=menu;more.Click+=delegate{menu.PlacementTarget=more;menu.IsOpen=true;};Grid.SetColumn(more,1);header.Children.Add(more);
                var add=UiIcons.Button("add","新建项目");add.Name=section.Id=="projects"?"NewProject":"NewProjectInSection_"+section.Id;add.Width=add.Height=24;add.Padding=new Thickness(3);add.Margin=new Thickness(0);add.Click+=delegate{NewProjectRequested(currentSection.Id);};Grid.SetColumn(add,2);header.Children.Add(add);HoverActions(header,menu,more,add);Children.Add(header);
                header.AllowDrop=true;header.DragOver+=delegate(object sender,DragEventArgs e){if(e.Data.GetDataPresent(ProjectDragFormat)){e.Effects=DragDropEffects.Move;e.Handled=true;}};
                header.Drop+=delegate(object sender,DragEventArgs e){var id=e.Data.GetData(ProjectDragFormat) as string;var project=catalog.Projects.FirstOrDefault(p=>p.Id==id);if(project!=null){e.Handled=true;try{MoveProjectToSection(project,currentSection.Id);}catch(Exception error){MessageBox.Show(error.Message,"分区未保存");}}};
                if(!SectionIsExpanded(section.Id))continue;var projects=catalog.Projects.Where(p=>!p.Archived&&!p.Pinned&&p.SectionId==section.Id).ToArray();
                foreach(var project in projects)AddProject(project);
                if(projects.Length==0){var hint=Ui.Text("将项目拖放到分区标题处",11,Ui.Brush("#ADB0B5"));hint.Margin=new Thickness(12,4,12,12);Children.Add(hint);}
            }
            AddQuick();AddRecent();AddArchive();renderedSignature=Signature();
        }
        MenuItem NewSectionMenuItem(){var item=new MenuItem{Header="新建分区",Name="NewSidebarSection",Icon=UiIcons.Image("add",AgentUi.Muted)};item.Click+=delegate{var dialog=new ConversationRenameDialog("","新建分区","分区名称");if(dialog.ShowDialog()==true)try{CreateSection(dialog.ConversationName);}catch(Exception e){MessageBox.Show(e.Message,"分区未保存");}};return item;}
        static void HoverActions(FrameworkElement header,ContextMenu menu,params Button[] actions){HoverActions(header,menu,null,actions);}
        static void HoverActions(FrameworkElement header,ContextMenu menu,Action<bool> onShow,params Button[] actions)
        {
            bool hovering=false;Action update=delegate{bool show=hovering||header.IsKeyboardFocusWithin||menu.IsOpen;foreach(var action in actions){action.Opacity=show?1:0;action.IsHitTestVisible=show;}if(onShow!=null)onShow(show);};
            foreach(var action in actions){action.Width=action.Height=24;action.MinWidth=action.MinHeight=0;action.Padding=new Thickness(3);action.Margin=new Thickness(0);action.Background=Brushes.Transparent;action.BorderBrush=Brushes.Transparent;action.Opacity=0;action.IsHitTestVisible=false;}
            header.MouseEnter+=delegate{hovering=true;update();};header.MouseLeave+=delegate{hovering=false;update();};header.IsKeyboardFocusWithinChanged+=delegate{update();};menu.Opened+=delegate{update();};menu.Closed+=delegate{update();};
        }
        void AddProject(ProjectDefinition project)
        {
            bool expanded;if(!catalog.SidebarExpansion.TryGetValue(project.Id,out expanded)){expanded=!project.IsQuickChatDefault;catalog.SidebarExpansion[project.Id]=expanded;}bool collapsed=!expanded,active=project.Id==catalog.ActiveProjectId;
            var button=AgentUi.Nav("",active?"WorkspaceProject":"WorkspaceProject_"+project.Id);button.Tag=project.Id;button.Background=Brushes.Transparent;button.Height=Double.NaN;button.MinHeight=35;button.Padding=new Thickness(0,6,6,6);button.Margin=new Thickness(12,1,12,1);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var content=new Grid();content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});content.ColumnDefinitions.Add(new ColumnDefinition());content.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var icon=UiIcons.Image(collapsed?"folder":"folder-open",Ui.Brush("#666B73"));icon.Name="ProjectFolderGlyph";icon.Tag=collapsed?"folder":"folder-open";icon.Width=icon.Height=17;icon.HorizontalAlignment=HorizontalAlignment.Left;icon.VerticalAlignment=VerticalAlignment.Center;content.Children.Add(icon);
            var label=Ui.Text(project.Name,13,AgentUi.Ink);label.Name="ProjectNameLabel";label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.Tag=project.Workspace;label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);content.Children.Add(label);
            var count=Ui.Text(project.Conversations.Count(c=>!c.Archived&&Visible(c)).ToString(),10,AgentUi.Muted);count.Name="ProjectConversationCount";count.HorizontalAlignment=HorizontalAlignment.Right;count.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(count,2);content.Children.Add(count);button.Content=content;
            button.Click+=delegate{catalog.SidebarExpansion[project.Id]=!catalog.SidebarExpansion[project.Id];Refresh();};button.ContextMenu=ProjectMenu(project);HookHover(button,project);var row=new Grid{Margin=button.Margin,Background=Brushes.Transparent};button.Margin=new Thickness(0);row.Children.Add(button);
            var projectActions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var more=SidebarAction("more","项目更多选项","ProjectMore_"+project.Id);more.ContextMenu=button.ContextMenu;more.Click+=delegate{CloseHover();more.ContextMenu.PlacementTarget=more;more.ContextMenu.IsOpen=true;};var compose=SidebarAction("edit","新建对话","NewProjectConversation_"+project.Id);compose.Click+=delegate{CloseHover();newConversation(project);};projectActions.Children.Add(more);projectActions.Children.Add(compose);row.Children.Add(projectActions);HoverActions(row,button.ContextMenu,show=>{count.Visibility=show?Visibility.Collapsed:Visibility.Visible;label.Margin=new Thickness(0,0,show?48:0,0);},more,compose);Children.Add(row);
            ArmDrag(button,()=>new DataObject(ProjectDragFormat,project.Id));
            row.AllowDrop=true;row.DragOver+=delegate(object sender,DragEventArgs e){var moved=ParseDrag(e.Data);if(moved==null||moved[0]==project.Id){e.Effects=DragDropEffects.None;}else{e.Effects=DragDropEffects.Move;button.Background=Ui.Brush("#E9EDF3");}e.Handled=true;};row.DragLeave+=delegate{button.Background=Brushes.Transparent;};
            row.Drop+=delegate(object sender,DragEventArgs e){button.Background=Brushes.Transparent;var moved=ParseDrag(e.Data);if(moved==null||moved[0]==project.Id)return;e.Handled=true;var from=ProjectCatalogStore.FindProject(catalog,moved[0]);var conversation=from==null?null:from.Conversations.FirstOrDefault(c=>c.Id==moved[1]);if(conversation==null)return;bool ok=false;if(moveConversation!=null)ok=moveConversation(from,project,conversation);else{var previous=catalog.Copy();try{ProjectCatalogStore.MoveConversation(catalog,from.Id,project.Id,conversation.Id);store.Save(catalog);ok=true;}catch{catalog.Projects=previous.Projects;catalog.UnassignedConversations=previous.UnassignedConversations;catalog.ActiveProjectId=previous.ActiveProjectId;}}if(ok){catalog.SidebarExpansion[project.Id]=true;Refresh();OpenConversation(project,conversation);}};
            if(!collapsed)foreach(var conversation in project.Conversations.Where(c=>!c.Archived&&!c.Pinned&&Visible(c)))AddConversation(project,conversation);
        }
        void AddConversation(ProjectDefinition project,ConversationDefinition conversation,bool recent=false,bool pinned=false)
        {
            if(!Visible(conversation))return;bool active=conversation.Id==activeConversationId;var row=new Grid{Margin=new Thickness(recent?12:34,0,12,1)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});
            var button=AgentUi.Nav("",pinned?"PinnedConversation_"+conversation.Id:project.Id==ProjectCatalogStore.QuickProjectId?"QuickConversation_"+conversation.Id:recent?"RecentConversation_"+conversation.Id:active?"ProjectConversation":"WorkspaceConversation_"+conversation.Id);button.Height=Double.NaN;button.MinHeight=31;button.Padding=new Thickness(6,5,6,5);button.Margin=new Thickness(0);if(active)button.Background=Ui.Brush("#EBEDEF");
            var text=Ui.Text(conversation.Name,12,AgentUi.Ink);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.Margin=new Thickness(0,0,16,0);text.VerticalAlignment=VerticalAlignment.Center;button.Content=text;button.Click+=delegate{OpenConversation(project,conversation);};button.MouseDoubleClick+=delegate{if(renameConversation!=null)renameConversation(project,conversation);};button.ContextMenu=ConversationMenu(project,conversation);Grid.SetColumnSpan(button,2);row.Children.Add(button);HookConversationHover(button,project,conversation);if(conversation.Mode=="work")ArmDrag(button,()=>new DataObject(DragFormat,project.Id+"/"+conversation.Id));
            FrameworkElement status=null;if(conversation.Status=="running")status=Spinner();else if(conversation.HasUnreadCompletion)status=new Border{Name="ConversationUnreadDot",Width=6,Height=6,CornerRadius=new CornerRadius(3),Background=Ui.Brush("#357FF5"),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};if(status!=null){Grid.SetColumn(status,1);row.Children.Add(status);}
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var pin=SidebarAction(conversation.Pinned?"pin-filled":"pin",conversation.Pinned?"取消置顶聊天":"置顶聊天","PinConversation_"+conversation.Id);pin.Click+=delegate{CloseHover();try{ToggleConversationPin(conversation);}catch(Exception e){MessageBox.Show(e.Message,"置顶未保存");}};var archive=SidebarAction("archive","归档聊天","ArchiveConversation_"+conversation.Id);archive.IsEnabled=archiveConversation!=null;archive.Click+=delegate{CloseHover();if(archiveConversation!=null){archiveConversation(project,conversation);Refresh();}};actions.Children.Add(pin);actions.Children.Add(archive);Grid.SetColumnSpan(actions,2);row.Children.Add(actions);HoverActions(row,button.ContextMenu,show=>{text.Margin=new Thickness(0,0,show?48:16,0);if(status!=null)status.Opacity=show?0:1;},pin,archive);Children.Add(row);
        }
        void OpenConversation(ProjectDefinition project,ConversationDefinition conversation)
        {
            bool unread=conversation.HasUnreadCompletion;conversation.HasUnreadCompletion=false;try{store.Save(catalog);}catch{conversation.HasUnreadCompletion=unread;throw;}conversationActivated(project,conversation);Refresh();
        }
        static FrameworkElement Spinner()
        {
            var rotate=new RotateTransform();var path=new System.Windows.Shapes.Path{Name="ConversationRunning",Width=12,Height=12,Data=Geometry.Parse("M 6 1 A 5 5 0 1 1 1 6"),Stroke=Ui.Brush("#92969D"),StrokeThickness=1.4,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,RenderTransformOrigin=new Point(.5,.5),RenderTransform=rotate,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};path.Loaded+=delegate{rotate.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(.9)){RepeatBehavior=RepeatBehavior.Forever});};path.Unloaded+=delegate{rotate.BeginAnimation(RotateTransform.AngleProperty,null);};return path;
        }
        static void ArmDrag(FrameworkElement row,Func<DataObject> payload)
        {
            bool armed=false;Point origin=new Point();row.PreviewMouseLeftButtonDown+=delegate{armed=true;origin=Mouse.GetPosition(row);};row.PreviewMouseLeftButtonUp+=delegate{armed=false;};row.LostMouseCapture+=delegate{armed=false;};row.MouseMove+=delegate{if(!armed||Mouse.LeftButton!=MouseButtonState.Pressed)return;Point now=Mouse.GetPosition(row);if(Math.Abs(now.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(now.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;armed=false;try{DragDrop.DoDragDrop(row,payload(),DragDropEffects.Move);}catch(InvalidOperationException){}};
        }
        void AddQuick()
        {
            var header=SectionToggle("quick","快速对话");header.Margin=new Thickness(12,20,12,7);Children.Add(header);if(!SectionIsExpanded("quick"))return;
            var owner=ProjectCatalogStore.QuickProject(catalog);var records=catalog.QuickConversations.Where(c=>!c.Archived&&!c.Pinned&&Visible(c)).OrderByDescending(c=>c.LastOpenedUtcTicks).ToArray();
            foreach(var conversation in records)AddConversation(owner,conversation,true);
            if(records.Length==0){var hint=Ui.Text("当前伙伴的聊天记录会显示在这里",11,Ui.Muted);hint.Margin=new Thickness(12,4,12,10);Children.Add(hint);}
        }
        void AddRecent()
        {
            var header=SectionToggle("recent","最近");header.Margin=new Thickness(12,20,12,7);Children.Add(header);if(!SectionIsExpanded("recent"))return;
            var owner=ProjectCatalogStore.UnassignedProject(catalog);var records=catalog.UnassignedConversations.Where(c=>!c.Archived&&!c.Pinned&&Visible(c)).Select(c=>new{Project=owner,Conversation=c})
                .Concat(catalog.Projects.Where(p=>!p.Archived).SelectMany(p=>p.Conversations.Where(c=>!c.Archived&&!c.Pinned&&Visible(c)).Select(c=>new{Project=p,Conversation=c})));
            foreach(var pair in records.OrderByDescending(x=>x.Conversation.LastOpenedUtcTicks).ThenByDescending(x=>x.Conversation.CreatedUtcTicks).Take(30))AddConversation(pair.Project,pair.Conversation,true);
        }
        void AddArchive()
        {
            var projects=catalog.Projects.Where(p=>p.Archived).ToArray();var conversations=catalog.Projects.Where(p=>!p.Archived).SelectMany(p=>p.Conversations.Where(c=>c.Archived&&Visible(c)).Select(c=>new{Project=p,Conversation=c})).Concat(catalog.UnassignedConversations.Where(c=>c.Archived&&Visible(c)).Select(c=>new{Project=ProjectCatalogStore.UnassignedProject(catalog),Conversation=c})).Concat(catalog.QuickConversations.Where(c=>c.Archived&&Visible(c)).Select(c=>new{Project=ProjectCatalogStore.QuickProject(catalog),Conversation=c})).ToArray();if(projects.Length==0&&conversations.Length==0)return;
            var toggle=AgentUi.Nav((archiveExpanded?"⌄  ":"›  ")+"已归档","ToggleArchived");toggle.FontSize=12;toggle.Foreground=AgentUi.Muted;toggle.Margin=new Thickness(12,20,12,6);toggle.Click+=delegate{archiveExpanded=!archiveExpanded;Refresh();};Children.Add(toggle);if(!archiveExpanded)return;
            foreach(var project in projects){var button=AgentUi.Nav(project.Name,"ArchivedProject_"+project.Id);button.Margin=new Thickness(20,1,12,1);button.ContextMenu=ProjectMenu(project);button.Click+=delegate{projectActivated(project);};Children.Add(button);}
            foreach(var item in conversations){var pair=item;var button=AgentUi.Nav(pair.Conversation.Name,"ArchivedConversation_"+pair.Conversation.Id);button.Margin=new Thickness(28,1,12,1);button.ContextMenu=ConversationMenu(pair.Project,pair.Conversation);button.Click+=delegate{OpenConversation(pair.Project,pair.Conversation);};Children.Add(button);}
        }
    }
}
