using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
namespace Dafeiyu
{
    internal sealed class ShellCommand
    {
        internal string Group,Title,GestureLabel,Icon;internal Action Execute;internal Func<bool> CanExecute;internal KeyGesture Gesture;
    }
    public sealed partial class ChatWindow
    {
        const string ProjectHome="https://github.com/Eason4869/deskpet";
        void AddShellCommand(string group,string title,string icon,Action action,string label=null,Key key=Key.None,ModifierKeys modifiers=ModifierKeys.None,Func<bool> enabled=null)
        {shellCommands.Add(new ShellCommand{Group=group,Title=title,Icon=icon,Execute=action,GestureLabel=label,Gesture=key==Key.None?null:new KeyGesture(key,modifiers),CanExecute=enabled});}
        bool HomeAvailable(){return shellPage=="home";}
        void BuildShellCommands()
        {
            AddShellCommand("文件","新聊天","new-chat",pet.NewShellConversation,"Ctrl+N",Key.N,ModifierKeys.Control);
            AddShellCommand("文件","新建临时聊天","people",delegate{ShowHomePage();OpenSideChat();},"Ctrl+Shift+N",Key.N,ModifierKeys.Control|ModifierKeys.Shift);
            AddShellCommand("文件","新建项目…","folder",pet.ShowNewProjectDialog);
            AddShellCommand("文件","打开文件夹…","folder",OpenProjectFolder,"Ctrl+O",Key.O,ModifierKeys.Control);
            AddShellCommand("文件","导出当前会话…","file",Export,null,Key.None,ModifierKeys.None,()=>session.History.Messages.Count>0);
            AddShellCommand("文件","关闭","close",CloseShellContent,"Ctrl+W",Key.W,ModifierKeys.Control);
            AddShellCommand("文件","退出桌宠","exit",pet.Close,"Ctrl+Q",Key.Q,ModifierKeys.Control);
            AddEdit("撤销",ApplicationCommands.Undo,"Ctrl+Z");AddEdit("重做",ApplicationCommands.Redo,"Ctrl+Y");AddEdit("剪切",ApplicationCommands.Cut,"Ctrl+X");AddEdit("复制",ApplicationCommands.Copy,"Ctrl+C");AddEdit("粘贴",ApplicationCommands.Paste,"Ctrl+V");AddEdit("删除",ApplicationCommands.Delete,"Delete");AddEdit("全选",ApplicationCommands.SelectAll,"Ctrl+A");
            AddShellCommand("编辑","设置","settings",()=>pet.ShowSettings(),"Ctrl+,",Key.OemComma,ModifierKeys.Control);
            AddShellCommand("视图","显示／隐藏侧边栏","sidebar",ToggleAppSidebar,"Ctrl+Shift+S",Key.S,ModifierKeys.Control|ModifierKeys.Shift,HomeAvailable);
            AddShellCommand("视图","显示／隐藏摘要面板","summary",ToggleSummary,null,Key.None,ModifierKeys.None,HomeAvailable);
            AddShellCommand("视图","打开终端","terminal",OpenTerminal,"Ctrl+`",Key.Oem3,ModifierKeys.Control,HomeAvailable);
            AddShellCommand("视图","新建标签页","new",OpenBlankTab,"Ctrl+Shift+B",Key.B,ModifierKeys.Control|ModifierKeys.Shift,HomeAvailable);
            AddShellCommand("视图","显示／隐藏工具标签","tabs",ToggleToolArea,"Ctrl+Alt+B",Key.B,ModifierKeys.Control|ModifierKeys.Alt,HomeAvailable);
            AddShellCommand("视图","分屏／完整标签视图","tabs",ToggleFullTabView,"Ctrl+Shift+F",Key.F,ModifierKeys.Control|ModifierKeys.Shift,()=>HomeAvailable()&&ActualWidth>=1200);
            AddShellCommand("视图","打开侧边聊天","people",OpenSideChat,"Ctrl+Alt+S",Key.S,ModifierKeys.Control|ModifierKeys.Alt,HomeAvailable);
            AddShellCommand("视图","打开浏览器标签页","book",()=>OpenShellUrl(null),"Ctrl+T",Key.T,ModifierKeys.Control);
            AddShellCommand("导航","返回","back",()=>TravelPage(-1),"Alt+←",Key.Left,ModifierKeys.Alt,()=>historyIndex>0);
            AddShellCommand("导航","前进","forward",()=>TravelPage(1),"Alt+→",Key.Right,ModifierKeys.Alt,()=>historyIndex<appHistory.Count-1);
            AddShellCommand("导航","聚焦主聊天输入框","new",delegate{ShowHomePage();input.Focus();},"Alt+L",Key.L,ModifierKeys.Alt);
            AddShellCommand("帮助","文档","book",()=>OpenShellUrl(ProjectHome));
            AddShellCommand("帮助","显示键盘快捷键","keys",()=>new ShortcutHelpDialog(this,shellCommands).ShowDialog(),"Ctrl+/",Key.OemQuestion,ModifierKeys.Control);
            AddShellCommand("帮助","新功能","news",()=>OpenShellUrl(ProjectHome+"/blob/main/CHANGELOG.md"));
            AddShellCommand("帮助","反馈","feedback",()=>OpenShellUrl(ProjectHome+"/issues"));
            AddShellCommand("帮助","配置引导","help",pet.ShowOnboarding);
            AddShellCommand("帮助","检查更新／关于桌面伙伴","help",delegate{pet.ShowSettings();if(settingsPage!=null)settingsPage.SelectNavigation(4);});
        }
        void AddEdit(string title,RoutedCommand command,string label)
        {AddShellCommand("编辑",title,null,delegate{var target=EditTarget();if(target!=null&&command.CanExecute(null,target)){Keyboard.Focus(target);command.Execute(null,target);}},label,Key.None,ModifierKeys.None,()=>EditTarget()!=null&&command.CanExecute(null,EditTarget()));}
        IInputElement EditTarget()
        {
            var focused=Keyboard.FocusedElement as FrameworkElement;if(focused!=null&&focused.IsVisible&&(focused is System.Windows.Controls.Primitives.TextBoxBase||focused is PasswordBox))return focused;
            var previous=lastEditingTarget as FrameworkElement;return previous!=null&&previous.IsVisible&&previous.IsEnabled?lastEditingTarget:null;
        }
        void BuildTitleMenus(StackPanel host)
        {
            foreach(string group in new[]{"文件","编辑","视图","帮助"}){
                string captured=group;var button=Ui.Button(group);button.Name="AppMenu"+(group=="文件"?"File":group=="编辑"?"Edit":group=="视图"?"View":"Help");button.FontSize=13;button.MinHeight=0;button.Height=30;button.Padding=new Thickness(11,0,11,0);button.Margin=new Thickness(2,0,2,0);button.Background=button.BorderBrush=Brushes.Transparent;button.Foreground=AgentUi.Muted;WindowChrome.SetIsHitTestVisibleInChrome(button,true);
                button.PreviewMouseLeftButtonDown+=delegate{var target=EditTarget();if(target!=null)lastEditingTarget=target;};
                button.Click+=delegate{var menu=ChatUi.Menu();menu.Name="ShellMenu"+captured;menu.MinWidth=captured=="视图"?320:255;int i=0;foreach(var command in shellCommands.Where(c=>c.Group==captured)){if((captured=="文件"&&(i==3||i==4||i==5))||(captured=="编辑"&&(i==2||i==7))||(captured=="帮助"&&(i==3||i==4)))menu.Items.Add(new Separator());menu.Items.Add(CommandMenuItem(command));i++;}button.ContextMenu=menu;menu.PlacementTarget=button;menu.IsOpen=true;};host.Children.Add(button);
            }
        }
        MenuItem CommandMenuItem(ShellCommand command)
        {
            var item=new MenuItem{Header=command.Title,InputGestureText=command.GestureLabel??"",Icon=command.Icon==null?null:ShellIcons.Image(command.Icon),IsEnabled=command.CanExecute==null||command.CanExecute()};item.Click+=delegate{if(command.CanExecute==null||command.CanExecute())try{command.Execute();}catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is InvalidOperationException||error is System.ComponentModel.Win32Exception))throw;ShowStatus("操作未完成，请检查本地文件或设置。");}};return item;
        }
        void ShellKeyDown(object sender,KeyEventArgs e)
        {
            if(e.Handled)return;var key=e.Key==Key.System?e.SystemKey:e.Key;if(Keyboard.Modifiers==ModifierKeys.Alt&&(key==Key.F||key==Key.E||key==Key.V||key==Key.H)){string name=key==Key.F?"AppMenuFile":key==Key.E?"AppMenuEdit":key==Key.V?"AppMenuView":"AppMenuHelp";var button=FindTitleMenu(this,name);if(button!=null){e.Handled=true;button.Focus();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}return;}if(e.Key==Key.Escape&&sidebarTemporary){e.Handled=true;sidebarTemporary=false;RefreshWorkspaceLayout();return;}foreach(var command in shellCommands){if(command.Gesture!=null&&command.Gesture.Matches(this,e)){e.Handled=true;if(command.CanExecute==null||command.CanExecute())command.Execute();return;}}
        }
        static Button FindTitleMenu(DependencyObject root,string name)
        {
            var button=root as Button;if(button!=null&&button.Name==name)return button;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindTitleMenu(VisualTreeHelper.GetChild(root,i),name);if(found!=null)return found;}return null;
        }
        void OpenShellUrl(string url)
        {ShowHomePage();EnsurePreviewWorkspace();filePreview.AddPage(url==null?"网页浏览器":url.EndsWith("/issues",StringComparison.Ordinal)?"反馈":"文档与帮助","globe",new BrowserPane(url));PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
        void OpenProjectFolder()
        {
            using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description="打开项目文件夹",ShowNewFolderButton=true}){if(picker.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;string path=System.IO.Path.GetFullPath(picker.SelectedPath);var project=pet.Projects.Projects.FirstOrDefault(p=>String.Equals(System.IO.Path.GetFullPath(p.Workspace),path,StringComparison.OrdinalIgnoreCase));if(project==null){project=ProjectCatalogStore.CreateProject(pet.Projects,System.IO.Path.GetFileName(path),path);pet.ProjectStore.Save(pet.Projects);}pet.ActivateProject(project);}
        }
        void CloseShellContent(){if(shellPage!="home"){ShowHomePage();return;}if(filePreview!=null&&filePreview.Selected!=null&&!ReferenceEquals(filePreview.Selected,conversationSurface)){filePreview.Close(filePreview.Selected);return;}Close();}
    }
}
