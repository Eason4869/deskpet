using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal sealed class ArchivedConversationsPanel : ScrollViewer
    {
        readonly PetWindow pet;
        readonly StackPanel rows=new StackPanel{Margin=new Thickness(24,0,24,24)};
        bool subscribed;
        internal ArchivedConversationsPanel(PetWindow companion)
        {
            if(companion==null)throw new ArgumentNullException("companion");pet=companion;Name="ArchivedConversationsPanel";
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;Content=rows;
            Loaded+=delegate{if(!subscribed){pet.ProjectsChanged+=RefreshState;subscribed=true;}RefreshState();};
            Unloaded+=delegate{if(subscribed){pet.ProjectsChanged-=RefreshState;subscribed=false;}};RefreshState();
        }
        internal void RefreshState()
        {
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshState));return;}
            rows.Children.Clear();rows.Children.Add(Ui.Text(TextResources.Get("archivedConversationsPanel.refreshState.text"),18,AgentUi.Ink));
            rows.Children.Add(Ui.Text(TextResources.Get("archivedConversationsPanel.refreshState.text.alternativeA"),12,AgentUi.Muted));
            int count=0;
            foreach(var project in pet.Projects.Projects.Where(p=>p.Archived).OrderByDescending(p=>p.LastOpenedUtcTicks))
            {
                count++;var body=new StackPanel();body.Children.Add(ActionRow(project.Name,TextResources.Format("archivedConversationsPanel.refreshState.actionRow",project.Conversations.Count),"ArchivedProject_"+project.Id,
                    TextResources.Get("archivedConversationsPanel.refreshState.actionRow.alternativeA"),()=>{pet.RestoreProject(project);RefreshState();},TextResources.Get("archivedConversationsPanel.refreshState.actionRow.alternativeB"),()=>{pet.DeleteProject(project);RefreshState();}));
                foreach(var conversation in project.Conversations.OrderByDescending(c=>c.LastOpenedUtcTicks))AddConversation(body,project,conversation,true);
                rows.Children.Add(GlassSurface.Card(body));
            }
            var ordinary=new StackPanel();
            foreach(var project in pet.Projects.Projects.Where(p=>!p.Archived))foreach(var conversation in project.Conversations.Where(c=>c.Archived).OrderByDescending(c=>c.LastOpenedUtcTicks)){AddConversation(ordinary,project,conversation,false);count++;}
            var unassigned=ProjectCatalogStore.UnassignedProject(pet.Projects,pet.Settings);var quick=ProjectCatalogStore.QuickProject(pet.Projects,pet.Settings);
            foreach(var conversation in unassigned.Conversations.Where(c=>c.Archived).OrderByDescending(c=>c.LastOpenedUtcTicks)){AddConversation(ordinary,unassigned,conversation,false);count++;}
            foreach(var conversation in quick.Conversations.Where(c=>c.Archived).OrderByDescending(c=>c.LastOpenedUtcTicks)){AddConversation(ordinary,quick,conversation,false);count++;}
            if(ordinary.Children.Count>0)rows.Children.Add(GlassSurface.Card(ordinary));
            if(count==0){var empty=new StackPanel{Margin=new Thickness(0,46,0,36)};var title=Ui.Text(TextResources.Get("archivedConversationsPanel.refreshState.text.alternativeB"),16,AgentUi.Ink);title.Name="ArchivedConversationsEmpty";empty.Children.Add(title);empty.Children.Add(Ui.Text(TextResources.Get("archivedConversationsPanel.refreshState.text.alternativeC"),12,AgentUi.Muted));rows.Children.Add(empty);}
        }
        void AddConversation(Panel host,ProjectDefinition project,ConversationDefinition conversation,bool ownerArchived)
        {
            string source=project.Id==ProjectCatalogStore.QuickProjectId?TextResources.Get("archivedConversationsPanel.addConversation.idProjectCatalogStoreUnassignedProjectId"):project.Id==ProjectCatalogStore.UnassignedProjectId?(conversation.Mode=="chat"?TextResources.Get("archivedConversationsPanel.addConversation.idProjectCatalogStoreUnassignedProjectId.alternativeA"):TextResources.Get("archivedConversationsPanel.addConversation.idProjectCatalogStoreUnassignedProjectId.alternativeB")):project.Name;
            var skin=pet.Catalog.Skins.FirstOrDefault(s=>s.Id==conversation.PetId);if(skin!=null)source+=" · "+skin.DisplayName;
            if(ownerArchived)source+=TextResources.Get("archivedConversationsPanel.addConversation.source");
            host.Children.Add(ActionRow(conversation.Name,source,"ArchivedConversation_"+conversation.Id,ownerArchived?TextResources.Get("archivedConversationsPanel.addConversation.actionRow"):TextResources.Get("archivedConversationsPanel.addConversation.actionRow.alternativeA"),delegate{
                if(project.Archived)pet.RestoreProject(project);if(conversation.Archived)pet.RestoreConversation(project,conversation);RefreshState();
            },TextResources.Get("archivedConversationsPanel.addConversation.actionRow.alternativeB"),()=>{pet.DeleteConversation(project,conversation);RefreshState();}));
        }
        static FrameworkElement ActionRow(string title,string description,string name,string restoreLabel,Action restore,string deleteLabel,Action delete)
        {
            var row=new Grid{Name=name,Margin=new Thickness(0,10,0,10)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var labels=new StackPanel{Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};var caption=Ui.Text(title,13,AgentUi.Ink);caption.TextWrapping=TextWrapping.Wrap;labels.Children.Add(caption);labels.Children.Add(Ui.Text(description,11,AgentUi.Muted));row.Children.Add(labels);
            var actions=new WrapPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};var recover=Ui.Button(restoreLabel);recover.Name="Restore"+name;recover.Margin=new Thickness(0,0,6,0);recover.Click+=delegate{restore();};actions.Children.Add(recover);
            var remove=Ui.Button(deleteLabel);remove.Name="Delete"+name;remove.Margin=new Thickness(0);remove.Click+=delegate{delete();};actions.Children.Add(remove);Grid.SetColumn(actions,1);row.Children.Add(actions);return row;
        }
    }
}
