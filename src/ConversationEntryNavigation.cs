using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed class ConversationEntryTabs:Border
    {
        readonly Button chat,work;readonly Border busy;
        internal ConversationEntryTabs(Action<bool> select,bool compact=false)
        {
            Background=Ui.Brush("#ECEDEF");CornerRadius=new CornerRadius(10);Padding=new Thickness(3);VerticalAlignment=VerticalAlignment.Center;Name=compact?"CompactEntryTabs":"WorkspaceEntryTabs";
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());Child=row;
            chat=Choice(TextResources.Get("conversationEntryNavigation.conversationEntryTabs.choice"),compact?"CompactChatEntry":"ChatEntry",()=>select(false));work=Choice(TextResources.Get("conversationEntryNavigation.conversationEntryTabs.choice.alternativeA"),compact?"CompactWorkEntry":"WorkEntry",()=>select(true));
            row.Children.Add(chat);Grid.SetColumn(work,1);row.Children.Add(work);var workBody=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};workBody.Children.Add(Label(TextResources.Get("conversationEntryNavigation.conversationEntryTabs.text")));busy=new Border{Name="BackgroundWorkIndicator",Width=5,Height=5,CornerRadius=new CornerRadius(3),Background=Ui.Blue,Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};workBody.Children.Add(busy);work.Content=workBody;
        }
        static TextBlock Label(string title){return new TextBlock{Text=title,FontSize=13,Foreground=AgentUi.Ink,LineHeight=18,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,VerticalAlignment=VerticalAlignment.Center};}
        static Button Choice(string title,string name,Action action)
        {
            // A stronger neutral hover remains visible against the gray tab rail.
            var b=AgentUi.Nav(title,name,true);
            b.Content=Label(title);b.Height=30;b.MinHeight=30;b.Padding=new Thickness(14,0,14,0);b.Margin=new Thickness(0);b.HorizontalContentAlignment=HorizontalAlignment.Center;b.VerticalContentAlignment=VerticalAlignment.Center;b.Click+=delegate{action();};return b;
        }
        internal void Refresh(bool isWork,bool running){chat.Background=isWork?Brushes.Transparent:ThemeManager.Surface;work.Background=isWork?ThemeManager.Surface:Brushes.Transparent;chat.Tag=!isWork;work.Tag=isWork;busy.Visibility=running?Visibility.Visible:Visibility.Collapsed;work.ToolTip=running?TextResources.Get("conversationEntryNavigation.refresh.workToolTipRunning"):TextResources.Get("conversationEntryNavigation.refresh.workToolTipRunning.alternativeA");}
    }
    public sealed partial class ChatWindow
    {
        readonly ContentControl entryHeader=new ContentControl{Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};
        readonly Dictionary<string,ConversationWorkspace> entryWorkspaces=new Dictionary<string,ConversationWorkspace>();
        readonly HashSet<ConversationSession> entrySessions=new HashSet<ConversationSession>();
        internal bool WorkEntry {get{return pet.ConversationSettings(session).AgentEnabled;}}
        string EntryKey(bool work){return work?"work":"chat:"+pet.Skin.Id;}
        internal void SwitchEntry(bool work)
        {
            if(closed||pet.IsUpdating||pet.LiveActive||work==WorkEntry)return;
            ConversationWorkspace next;if(!entryWorkspaces.TryGetValue(EntryKey(work),out next)||next.Session==null||(!work&&!ProjectCatalogStore.VisibleForPet(next.Definition,pet.Skin.Id)))next=pet.CreateDraftConversation(null,work);
            SwitchWorkspace(next);input.Focus();
        }
        void RememberEntry()
        {
            // Archive/delete can close the outgoing workspace before the view switches.
            // Keep only a live session, and capture it before background close clears the property.
            if(workspace==null||workspace.IsDisposed)return;var rememberedSession=workspace.Session;if(rememberedSession==null)return;
            bool work=workspace.EffectiveSettings().AgentEnabled;entryWorkspaces[work?"work":"chat:"+workspace.Definition.PetId]=workspace;
            if(entrySessions.Add(rememberedSession))rememberedSession.StateChanged+=RefreshEntryControls;
        }
        void RefreshEntryControls()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshEntryControls));return;}
            var tabs=entryHeader.Content as ConversationEntryTabs;if(tabs!=null)tabs.Refresh(WorkEntry,pet.WorkIsRunning);if(navigation!=null)navigation.Refresh();
        }
        void DetachEntryControls(){foreach(var observed in entrySessions)observed.StateChanged-=RefreshEntryControls;entrySessions.Clear();}
    }
}
