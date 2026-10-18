using System;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        readonly ContentControl filePreviewDock=new ContentControl{Name="DockedFilePreview",Visibility=Visibility.Collapsed,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,Margin=new Thickness(0,48,12,6)};
        PreviewWorkspace filePreview;bool previewVisible=true;int terminalNumber;
        internal void OpenFilePreview(string path)
        {
            if(closed)return;EnsurePreviewWorkspace();filePreview.OpenFile(path);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();
        }
        void EnsurePreviewWorkspace(){previewVisible=true;tabsHidden=false;resumeConversation=false;if(filePreview==null)filePreview=new PreviewWorkspace(CloseFilePreview,OpenBlankTab,message=>session.ReportNotice(message));}
        internal void OpenBlankTab()
        {
            if(closed)return;EnsurePreviewWorkspace();BlankPreviewPage page=null;page=new BlankPreviewPage(address=>filePreview.Replace(page,"网页浏览器","globe",new BrowserPane(address)),delegate{var picker=new Microsoft.Win32.OpenFileDialog{Title="打开文件预览",Filter="所有文件|*.*"};if(picker.ShowDialog(this)==true){OpenFilePreview(picker.FileName);filePreview.Close(page);}},()=>{OpenTerminal();if(filePreview!=null&&filePreview.Selected!=page)filePreview.Close(page);},()=>{OpenSideChat();if(filePreview!=null&&filePreview.Selected!=page)filePreview.Close(page);},()=>{OpenChangesTab();if(filePreview!=null&&filePreview.Selected!=page)filePreview.Close(page);});filePreview.AddPage("新标签页","globe",page);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();
        }
        internal void OpenSideChat(){if(closed)return;EnsurePreviewWorkspace();string name=workspace==null?"当前工作区":workspace.Project.Name;filePreview.AddPage("侧边聊天","chat",new SideChatPane(pet,this,TaskSettings(),name));PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
        void OpenChangesTab(){if(closed)return;var task=session.CurrentAgentTask;if(task==null)OpenDetailTab("文件变更",new TextBlock{Text="当前没有任务变更。",Margin=new Thickness(24),Foreground=AgentUi.Muted});else OpenTaskChanges(task);}
        internal void OpenTerminal()
        {
            if(closed)return;
            try{var cfg=TaskSettings();string root=System.IO.Path.GetFullPath(cfg.AgentWorkspace);System.IO.Directory.CreateDirectory(root);bool sandbox=cfg.AgentSandboxEnabled&&cfg.AgentApprovalMode!=AgentApprovalMode.Full;string[] tools=sandbox?AgentSandboxDirectories.Parse(cfg.AgentSandboxReadDirectories):new string[0];EnsurePreviewWorkspace();filePreview.AddTerminal(new TerminalPane(root,sandbox,tools),++terminalNumber);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
            catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException))throw;session.ReportNotice("终端未打开："+error.Message);}
        }
        void PlaceFilePreview(bool visible)
        {
            if(filePreview==null)return;if(filePreviewDock.Content!=filePreview)filePreviewDock.Content=filePreview;filePreviewDock.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        }
        void CloseFilePreview()
        {
            if(filePreview==null)return;var pane=filePreview;RestoreConversationSurface();filePreview=null;fullTabView=false;tabsHidden=false;filePreviewDock.Content=null;filePreviewDock.Visibility=Visibility.Collapsed;pane.Dispose();if(!closed)RefreshWorkspaceLayout();
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
