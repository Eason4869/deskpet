using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed partial class ProjectSidebar
    {
        const string ConversationOrderFormat="DeskPet.ConversationOrderGroup";
        string ConversationOrderSignature(){return catalog.SidebarConversationOrder==null?"":"|"+String.Join("|",catalog.SidebarConversationOrder.OrderBy(x=>x.Key).Select(x=>x.Key+"="+String.Join(",",x.Value??new List<string>())))+"|"+String.Join("|",catalog.SidebarSectionLabels.OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value));}
        static string ProjectOrderGroup(ProjectDefinition project){return "project/"+project.Id;}
        IEnumerable<T> OrderedRows<T>(IEnumerable<T> rows,Func<T,ConversationDefinition> conversation,string group)
        {
            // Opening/renaming only changes activity metadata, never list position.
            var initial=group.StartsWith("project/",StringComparison.Ordinal)?rows:rows.OrderByDescending(x=>conversation(x).CreatedUtcTicks);
            List<string> saved;if(catalog.SidebarConversationOrder==null||!catalog.SidebarConversationOrder.TryGetValue(group,out saved)||saved==null)return initial;
            var ranks=new Dictionary<string,int>();foreach(string id in saved)if(id!=null&&!ranks.ContainsKey(id))ranks[id]=ranks.Count;
            Func<T,int> rankOf=x=>{int rank;return ranks.TryGetValue(conversation(x).Id,out rank)?rank:Int32.MaxValue;};
            // A newly created record must still enter the capped recent list.
            return group.StartsWith("project/",StringComparison.Ordinal)?initial.OrderBy(rankOf):initial.OrderBy(x=>ranks.ContainsKey(conversation(x).Id)?1:0).ThenBy(rankOf);
        }
        IEnumerable<ConversationDefinition> OrderMembers(string group)
        {
            IEnumerable<ConversationDefinition> records;
            if(group=="quick")records=catalog.QuickConversations;
            else if(group.StartsWith("chat/",StringComparison.Ordinal))records=catalog.UnassignedConversations.Where(c=>c.Mode=="chat"&&c.SectionId==group.Substring(5));
            else if(group.StartsWith("project/",StringComparison.Ordinal)){var project=ProjectCatalogStore.FindProject(catalog,group.Substring(8));records=project==null?Enumerable.Empty<ConversationDefinition>():project.Conversations;}
            else{records=catalog.Projects.Where(p=>!p.Archived).SelectMany(p=>p.Conversations).Concat(catalog.UnassignedConversations);if(group=="pinned"||group=="recent"&&workEntry==false)records=records.Concat(catalog.QuickConversations);else if(group!="recent")return Enumerable.Empty<ConversationDefinition>();}
            return records.Where(c=>!c.Archived&&c.Pinned==(group=="pinned")&&Visible(c));
        }
        internal bool ReorderConversation(string group,string movingId,string anchorId,bool after)
        {
            var ids=OrderedRows(OrderMembers(group),c=>c,group).Select(c=>c.Id).ToList();
            if(movingId==anchorId||!ids.Contains(movingId)||!ids.Contains(anchorId))return false;
            var previousOrder=ids.ToArray();ids.Remove(movingId);ids.Insert(ids.IndexOf(anchorId)+(after?1:0),movingId);if(ids.SequenceEqual(previousOrder))return false;
            if(catalog.SidebarConversationOrder==null)catalog.SidebarConversationOrder=new Dictionary<string,List<string>>();List<string> previous;bool hadPrevious=catalog.SidebarConversationOrder.TryGetValue(group,out previous);
            // Other personas/modes may share this section. Retain their ranks.
            var visibleIds=new HashSet<string>(ids);catalog.SidebarConversationOrder[group]=ids.Concat((previous??new List<string>()).Where(id=>!visibleIds.Contains(id))).ToList();
            try{store.Save(catalog);}catch{if(hadPrevious)catalog.SidebarConversationOrder[group]=previous;else catalog.SidebarConversationOrder.Remove(group);throw;}
            Refresh();return true;
        }
        bool CanMoveToProject(IDataObject data,ProjectDefinition project)
        {
            var drag=ParseDrag(data);if(drag==null||drag[0]==project.Id||project.Archived)return false;
            var from=ProjectCatalogStore.FindProject(catalog,drag[0]);var item=from==null?null:from.Conversations.FirstOrDefault(c=>c.Id==drag[1]);return item!=null&&item.Mode=="work"&&!item.Archived;
        }
        bool CanReorderConversation(IDataObject data,ProjectDefinition project,ConversationDefinition anchor,string group)
        {
            var drag=ParseDrag(data);if(drag==null||drag[1]==anchor.Id)return false;
            string origin=data.GetData(ConversationOrderFormat) as string;if(origin!=group&&drag[0]!=project.Id)return false;
            var from=ProjectCatalogStore.FindProject(catalog,drag[0]);if(from==null||!from.Conversations.Any(c=>c.Id==drag[1]))return false;
            return OrderMembers(group).Any(c=>c.Id==drag[1]);
        }
        void HookConversationOrder(Grid row,Button button,ProjectDefinition project,ConversationDefinition conversation,string group)
        {
            ArmDrag(button,delegate{var data=new DataObject(DragFormat,project.Id+"/"+conversation.Id);data.SetData(ConversationOrderFormat,group);return data;});
            var marker=new Border{Name="ConversationDropIndicator",Height=2,Background=Ui.Brush("#AEB4BD"),Margin=new Thickness(2,0,2,0),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed,IsHitTestVisible=false};Grid.SetColumnSpan(marker,2);row.Children.Add(marker);row.AllowDrop=true;
            row.DragOver+=delegate(object sender,DragEventArgs e){if(!e.Data.GetDataPresent(DragFormat))return;bool allowed=CanReorderConversation(e.Data,project,conversation,group);marker.Visibility=allowed?Visibility.Visible:Visibility.Collapsed;marker.VerticalAlignment=e.GetPosition(row).Y<row.ActualHeight/2?VerticalAlignment.Top:VerticalAlignment.Bottom;e.Effects=allowed?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
            row.DragLeave+=delegate{marker.Visibility=Visibility.Collapsed;};
            row.Drop+=delegate(object sender,DragEventArgs e){marker.Visibility=Visibility.Collapsed;if(!e.Data.GetDataPresent(DragFormat))return;e.Handled=true;if(!CanReorderConversation(e.Data,project,conversation,group))return;var drag=ParseDrag(e.Data);try{ReorderConversation(group,drag[1],conversation.Id,e.GetPosition(row).Y>=row.ActualHeight/2);}catch(Exception error){MessageBox.Show(error.Message,"排序未保存");}};
        }
    }
}
