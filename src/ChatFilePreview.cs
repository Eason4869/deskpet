using System;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        readonly ContentControl filePreviewDock=new ContentControl{Name="DockedFilePreview",Visibility=Visibility.Collapsed,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,Margin=new Thickness(0,48,12,6)};
        PreviewWorkspace filePreview;bool previewVisible=true;int terminalNumber;
        sealed class ConversationPreviewState
        {
            internal PreviewWorkspace Pane;internal ConversationWorkspace Owner;internal bool Visible,Full,Hidden,ConversationSelected;
            internal double Width;internal int TerminalNumber;
        }
        readonly System.Collections.Generic.Dictionary<ConversationSession,ConversationPreviewState> conversationPreviews=new System.Collections.Generic.Dictionary<ConversationSession,ConversationPreviewState>();
        void SuspendConversationPreview()
        {
            if(filePreview!=null)conversationPreviews[session]=new ConversationPreviewState{Pane=filePreview,Owner=workspace,Visible=previewVisible,Full=fullTabView,Hidden=tabsHidden,Width=previewWidth,TerminalNumber=terminalNumber,ConversationSelected=resumeConversation||ReferenceEquals(filePreview.Selected,conversationSurface)};
            RestoreConversationSurface();filePreviewDock.Content=null;filePreviewDock.Visibility=Visibility.Collapsed;filePreview=null;
            previewVisible=true;fullTabView=tabsHidden=resumeConversation=false;previewWidth=Double.NaN;previewOverdrag=0;terminalNumber=0;
        }
        void RestoreConversationPreview()
        {
            ConversationPreviewState state;if(!conversationPreviews.TryGetValue(session,out state))return;
            filePreview=state.Pane;previewVisible=state.Visible;fullTabView=state.Full;tabsHidden=state.Hidden;previewWidth=state.Width;terminalNumber=state.TerminalNumber;resumeConversation=state.ConversationSelected;
        }
        void CloseConversationPreviews()
        {
            CloseFilePreview();foreach(var state in conversationPreviews.Values)state.Pane.Dispose();conversationPreviews.Clear();
        }
        void PruneConversationPreviews()
        {
            if(closed||Dispatcher.HasShutdownStarted)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(PruneConversationPreviews));return;}
            var expired=new System.Collections.Generic.List<ConversationSession>();foreach(var pair in conversationPreviews)if(pair.Value.Owner!=null&&pair.Value.Owner.IsDisposed)expired.Add(pair.Key);
            foreach(var key in expired){if(ReferenceEquals(key,session))CloseFilePreview();else{conversationPreviews[key].Pane.Dispose();conversationPreviews.Remove(key);}}
        }
        internal void OpenFilePreview(string path)
        {
            if(closed)return;EnsurePreviewWorkspace();filePreview.OpenFile(path);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();
        }
        void EnsurePreviewWorkspace(){previewVisible=true;tabsHidden=false;resumeConversation=false;if(filePreview==null){var owner=session;filePreview=new PreviewWorkspace(CloseFilePreview,OpenBlankTab,message=>owner.ReportNotice(message));}}
        internal void OpenBrowserLink(Uri uri)
        {
            if(closed||uri==null||(uri.Scheme!="http"&&uri.Scheme!="https"))return;
            EnsurePreviewWorkspace();filePreview.AddPage(uri.Host,"globe",new BrowserPane(uri.AbsoluteUri));PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();
        }
        internal void OpenProjectFacts(AgentTaskState task){OpenDetailTab(()=>TextResources.Get("chatFilePreview.openProjectFacts.openDetailTab"),new AgentFactsPane(task));}
        internal void OpenBlankTab()
        {
            if(closed)return;EnsurePreviewWorkspace();BlankPreviewPage page=null;page=new BlankPreviewPage(address=>filePreview.Replace(page,()=> (TextResources.Get("browser.tab.title")),"globe",new BrowserPane(address)),delegate{var picker=LocalizedUi.With(new Microsoft.Win32.OpenFileDialog{Title=TextResources.Get("chatFilePreview.openBlankTab.title"),Filter=TextResources.Get("chatFilePreview.openBlankTab.filter")},"Title",()=>(TextResources.Get("chatFilePreview.openBlankTab.title")));if(picker.ShowDialog(this)==true){OpenFilePreview(picker.FileName);filePreview.Close(page);}},()=>{OpenTerminal();if(filePreview!=null&&filePreview.Selected!=page)filePreview.Close(page);},()=>{OpenSideChat();if(filePreview!=null&&filePreview.Selected!=page)filePreview.Close(page);},()=>{OpenChangesTab();if(filePreview!=null&&filePreview.Selected!=page)filePreview.Close(page);});filePreview.AddPage(()=> (TextResources.Get("chatFilePreview.openBlankTab.filePreviewAddPage")),"globe",page);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();
        }
        internal void OpenSideChat(){if(closed)return;EnsurePreviewWorkspace();string name=workspace==null?TextResources.Get("chatFilePreview.openSideChat.workspaceProjectName"):workspace.Project.Name;bool projectBound=workspace!=null&&workspace.ProjectId!=ProjectCatalogStore.UnassignedProjectId&&workspace.ProjectId!=ProjectCatalogStore.QuickProjectId;filePreview.AddPage(()=> (TextResources.Get("chatFilePreview.openSideChat.filePreviewAddPage")),"chat",new SideChatPane(pet,this,TaskSettings(),name,parent:session,allowProjectRead:projectBound));PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
        void OpenChangesTab(){if(closed)return;var task=session.CurrentAgentTask;if(task==null)OpenDetailTab(()=>TextResources.Get("chatFilePreview.openChangesTab.openDetailTab"),LocalizedUi.With(new TextBlock{Text=TextResources.Get("chatFilePreview.openChangesTab.text"),Margin=new Thickness(24),Foreground=AgentUi.Muted},"Text",()=>(TextResources.Get("chatFilePreview.openChangesTab.text"))));else OpenTaskChanges(task);}
        internal void OpenTerminal()
        {
            if(closed)return;
            try{var cfg=TaskSettings();string root=System.IO.Path.GetFullPath(cfg.AgentWorkspace);System.IO.Directory.CreateDirectory(root);bool sandbox=cfg.AgentSandboxEnabled&&cfg.AgentApprovalMode!=AgentApprovalMode.Full;string[] tools=sandbox?AgentSandboxDirectories.Parse(cfg.AgentSandboxReadDirectories):new string[0];EnsurePreviewWorkspace();filePreview.AddTerminal(new TerminalPane(root,sandbox,tools),++terminalNumber);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
            catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException))throw;session.ReportNotice(TextResources.Message("chatFilePreview.openTerminal.sessionReportNotice",error.Message));}
        }
        void PlaceFilePreview(bool visible)
        {
            if(filePreview==null)return;if(filePreviewDock.Content!=filePreview)filePreviewDock.Content=filePreview;filePreviewDock.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        }
        void CloseFilePreview()
        {
            if(filePreview==null)return;var pane=filePreview;RestoreConversationSurface();conversationPreviews.Remove(session);filePreview=null;fullTabView=false;tabsHidden=false;filePreviewDock.Content=null;filePreviewDock.Visibility=Visibility.Collapsed;pane.Dispose();if(!closed)RefreshWorkspaceLayout();
        }

    }
    internal sealed class OwnedPreviewContent:Border,IDisposable
    { readonly IDisposable owner;internal OwnedPreviewContent(UIElement view,IDisposable owner){Child=view;this.owner=owner;}public void Dispose(){owner.Dispose();} }
    internal static class FilePreviewLauncher
    {
        internal static void Show(Window owner,string path,PetWindow pet)
        {
            var chat=owner as ChatWindow;if(chat==null){pet.ShowChat();foreach(Window window in Application.Current.Windows)if(window is ChatWindow){chat=(ChatWindow)window;break;}}if(chat!=null)chat.OpenFilePreview(path);
        }
    }
}
