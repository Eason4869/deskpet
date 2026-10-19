using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed partial class ProjectSidebar
    {
        bool SectionIsExpanded(string id){bool expanded;if(!catalog.SidebarSectionExpansion.TryGetValue(id,out expanded)){expanded=true;catalog.SidebarSectionExpansion[id]=true;}return expanded;}
        string FixedSectionKey(string id){return (workEntry==false?"chat:":"work:")+id;}
        string SectionLabel(string id,string fallback){string label;return catalog.SidebarSectionLabels.TryGetValue(FixedSectionKey(id),out label)&&!String.IsNullOrWhiteSpace(label)?label:fallback;}
        internal void RenameFixedSection(string id,string name)
        {
            if(id!="quick"&&id!="recent"&&id!="pinned")throw new ArgumentException("分区不存在。");name=(name??"").Trim();if(name.Length<1||name.Length>40)throw new ArgumentException("分区名称应为 1～40 个字符。");
            string key=FixedSectionKey(id),previous;bool existed=catalog.SidebarSectionLabels.TryGetValue(key,out previous);catalog.SidebarSectionLabels[key]=name;try{store.Save(catalog);}catch{if(existed)catalog.SidebarSectionLabels[key]=previous;else catalog.SidebarSectionLabels.Remove(key);throw;}Refresh();
        }
        void FixedSectionMenu(Button title,string id,string name)
        {
            var menu=ChatUi.Menu();var rename=new MenuItem{Header="重命名分区"};rename.Click+=delegate{var dialog=new ConversationRenameDialog(SectionLabel(id,name),"重命名分区","分区名称");if(dialog.ShowDialog()==true)try{RenameFixedSection(id,dialog.ConversationName);}catch(Exception error){MessageBox.Show(error.Message,"分区未保存");}};menu.Items.Add(rename);menu.Items.Add(new Separator());menu.Items.Add(NewSectionMenuItem());title.ContextMenu=menu;
        }
        Button SectionToggle(string id,string name)
        {
            bool expanded=SectionIsExpanded(id);var button=AgentUi.Nav("","ToggleSection_"+id);button.MinHeight=24;button.Height=24;button.Padding=new Thickness(0);button.Margin=new Thickness(0);button.HorizontalAlignment=HorizontalAlignment.Left;
            var content=new StackPanel{Orientation=Orientation.Horizontal};var title=Ui.InlineText(SectionLabel(id,name),12,AgentUi.Muted);title.Name="SidebarSectionTitle";title.Tag=id;title.VerticalAlignment=VerticalAlignment.Center;content.Children.Add(title);var arrow=UiIcons.Image("down",AgentUi.Muted);arrow.Width=arrow.Height=11;arrow.Margin=new Thickness(5,0,0,0);arrow.VerticalAlignment=VerticalAlignment.Center;if(!expanded){arrow.RenderTransformOrigin=new Point(.5,.5);arrow.RenderTransform=new RotateTransform(-90);}content.Children.Add(arrow);button.Content=content;button.Click+=delegate{catalog.SidebarSectionExpansion[id]=!SectionIsExpanded(id);Refresh();};if(id=="quick"||id=="recent"||id=="pinned")FixedSectionMenu(button,id,name);return button;
        }
        internal void ToggleConversationPin(ConversationDefinition conversation){bool previous=conversation.Pinned;conversation.Pinned=!previous;try{store.Save(catalog);}catch{conversation.Pinned=previous;throw;}Refresh();}
        internal void ToggleProjectPin(ProjectDefinition project){bool previous=project.Pinned;project.Pinned=!previous;try{store.Save(catalog);}catch{project.Pinned=previous;throw;}Refresh();}
        void AddPinned()
        {
            var projects=catalog.Projects.Where(p=>!p.Archived&&p.Pinned&&workEntry!=false).ToArray();var unassigned=ProjectCatalogStore.UnassignedProject(catalog);var conversations=catalog.Projects.Where(p=>!p.Archived).SelectMany(p=>p.Conversations.Where(c=>c.Pinned&&!c.Archived&&Visible(c)).Select(c=>new{Project=p,Conversation=c})).Concat(catalog.UnassignedConversations.Where(c=>c.Pinned&&!c.Archived&&Visible(c)).Select(c=>new{Project=unassigned,Conversation=c})).Concat(catalog.QuickConversations.Where(c=>c.Pinned&&!c.Archived&&Visible(c)).Select(c=>new{Project=ProjectCatalogStore.QuickProject(catalog),Conversation=c})).ToArray();if(projects.Length==0&&conversations.Length==0)return;
            var header=SectionToggle("pinned","置顶");header.Margin=new Thickness(12,18,12,6);Children.Add(header);if(!SectionIsExpanded("pinned"))return;foreach(var project in projects)AddProject(project);foreach(var pair in OrderedRows(conversations,x=>x.Conversation,"pinned"))AddConversation(pair.Project,pair.Conversation,true,true);
        }
        static Button SidebarAction(string icon,string hint,string name)
        {
            var button=UiIcons.Button(icon,hint);var glyph=(FrameworkElement)button.Content;glyph.Width=glyph.Height=14;button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;button.Name=name;button.ToolTip=new ToolTip{Content=hint,Background=Ui.Brush("#202329"),Foreground=Brushes.White,BorderBrush=Brushes.Transparent,Placement=System.Windows.Controls.Primitives.PlacementMode.Top};ToolTipService.SetInitialShowDelay(button,350);return button;
        }
        void HookConversationHover(Control row,ProjectDefinition project,ConversationDefinition conversation)
        {
            row.MouseEnter+=delegate{if(hoverLatched)return;hoverClose.Stop();if(hover.IsOpen&&ReferenceEquals(hover.PlacementTarget,row))return;CloseHover();var body=new StackPanel{Margin=new Thickness(13),Width=280};var title=Ui.Text(conversation.Name,14,AgentUi.Ink);title.Name="ConversationHoverName";title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.Wrap;body.Children.Add(title);var owner=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};var folder=UiIcons.Image(conversation.Mode=="work"?"folder":"list",AgentUi.Muted);folder.Width=folder.Height=15;folder.Margin=new Thickness(0,0,8,0);folder.VerticalAlignment=VerticalAlignment.Center;owner.Children.Add(folder);owner.Children.Add(Ui.InlineText(ConversationOwnerLabel(project,conversation),12,AgentUi.Muted));body.Children.Add(owner);var card=new Border{Child=body,Background=Brushes.White,BorderBrush=AgentUi.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12)};var cardPopup=hover;card.MouseEnter+=delegate{if(!ReferenceEquals(hover,cardPopup))return;hoverPinned=true;hoverClose.Stop();};card.MouseLeave+=delegate{if(!ReferenceEquals(hover,cardPopup))return;hoverPinned=false;HideHover();};hover.Child=card;hover.PlacementTarget=row;hover.IsOpen=true;};row.MouseLeave+=delegate{HideHover();};row.MouseLeftButtonDown+=delegate{CloseHover();};
        }
    }
}
