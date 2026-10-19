using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed partial class ProjectSidebar
    {
        string ConversationOwnerLabel(ProjectDefinition project,ConversationDefinition conversation)
        {
            if(conversation.Mode=="work")return project.Name;if(conversation.Mode=="quick")return SectionLabel("quick","快速对话");var section=catalog.Sections.FirstOrDefault(s=>s.Id==conversation.SectionId&&s.Mode=="chat");return section==null?"默认分区":section.Name;
        }
        void AddChatSections()
        {
            foreach(var section in catalog.Sections.Where(s=>s.Mode=="chat"))
            {
                var current=section;var header=new Grid{Margin=new Thickness(12,18,12,6),Background=Brushes.Transparent};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});
                var title=SectionToggle(section.Id,section.Name);header.Children.Add(title);var menu=ChatUi.Menu();var rename=new MenuItem{Header="重命名分区"};rename.Click+=delegate{var dialog=new ConversationRenameDialog(current.Name,"重命名分区","分区名称");if(dialog.ShowDialog()==true)try{RenameSection(current.Id,dialog.ConversationName);}catch(Exception error){MessageBox.Show(error.Message,"分区未保存");}};menu.Items.Add(rename);menu.Items.Add(new Separator());menu.Items.Add(NewSectionMenuItem());header.ContextMenu=menu;title.ContextMenu=menu;
                var more=UiIcons.Button("more","分区更多选项");more.Name="SectionMore_"+section.Id;more.ContextMenu=menu;more.Click+=delegate{menu.PlacementTarget=more;menu.IsOpen=true;};Grid.SetColumn(more,1);header.Children.Add(more);
                var add=UiIcons.Button("add","新建对话");add.Name="NewChatInSection_"+section.Id;add.Click+=delegate{if(newChatConversation!=null)newChatConversation(current.Id);};Grid.SetColumn(add,2);header.Children.Add(add);HoverActions(header,menu,more,add);Children.Add(header);
                header.AllowDrop=true;header.DragOver+=delegate(object sender,DragEventArgs e){if(!e.Data.GetDataPresent(DragFormat))return;e.Effects=ChatSectionDrag(e.Data,current.Id)!=null?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
                header.Drop+=delegate(object sender,DragEventArgs e){if(!e.Data.GetDataPresent(DragFormat))return;e.Handled=true;var moving=ChatSectionDrag(e.Data,current.Id);if(moving==null)return;try{MoveChatToSection(moving.Id,current.Id);}catch(Exception error){MessageBox.Show(error.Message,"分区未保存");}};
                if(!SectionIsExpanded(section.Id))continue;
                var owner=ProjectCatalogStore.UnassignedProject(catalog);var records=catalog.UnassignedConversations.Where(c=>c.Mode=="chat"&&!c.Archived&&!c.Pinned&&c.SectionId==section.Id&&Visible(c));
                foreach(var conversation in OrderedRows(records,c=>c,"chat/"+section.Id))AddConversation(owner,conversation,true,false,"chat/"+section.Id);
            }
        }
        ConversationDefinition ChatSectionDrag(IDataObject data,string sectionId)
        {
            var drag=ParseDrag(data);if(drag==null||drag[0]!=ProjectCatalogStore.UnassignedProjectId)return null;
            return catalog.UnassignedConversations.FirstOrDefault(c=>c.Id==drag[1]&&c.Mode=="chat"&&!c.Archived&&c.SectionId!=sectionId&&Visible(c));
        }
        internal void MoveChatToSection(string conversationId,string sectionId)
        {
            if(!catalog.Sections.Any(s=>s.Id==sectionId&&s.Mode=="chat"))throw new ArgumentException("聊天分区不存在。");
            var conversation=catalog.UnassignedConversations.Single(c=>c.Id==conversationId&&c.Mode=="chat"&&Visible(c));string previous=conversation.SectionId;conversation.SectionId=sectionId;
            try{store.Save(catalog);}catch{conversation.SectionId=previous;throw;}Refresh();
        }
    }
}
