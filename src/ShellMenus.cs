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
        internal string Group,Title,GestureLabel,Icon,ShortcutId;internal string GroupTitle{get{return ShortcutCatalog.GroupTitle(Group);}}internal Action Execute;internal Func<bool> CanExecute;internal KeyGesture Gesture;
    }
    public sealed partial class ChatWindow
    {
        const string ProjectHome="https://github.com/Eason4869/deskpet";
        void AddShellCommand(string group,string title,string icon,Action action,string label=null,Key key=Key.None,ModifierKeys modifiers=ModifierKeys.None,Func<bool> enabled=null,string shortcutId=null)
        {var definition=ShortcutCatalog.All.FirstOrDefault(d=>!d.Global&&d.Id==shortcutId);shellCommands.Add(new ShellCommand{Group=group,Title=title,Icon=icon,Execute=action,ShortcutId=definition==null?null:definition.Id,GestureLabel=label,Gesture=key==Key.None?null:new KeyGesture(key,modifiers),CanExecute=enabled});}
        void RefreshShellShortcuts(){foreach(var command in shellCommands)if(command.ShortcutId!=null)command.GestureLabel=ShortcutCatalog.Label(pet.Settings.LocalShortcuts[command.ShortcutId]);}
        bool HomeAvailable(){return shellPage=="home";}
        void BuildShellCommands()
        {
            AddShellCommand("文件",TextResources.Get("shellMenus.buildShellCommands.addShellCommand"),"new-chat",pet.NewShellConversation,"Ctrl+N",Key.N,ModifierKeys.Control,shortcutId:"newchat");
            AddShellCommand("文件",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeA"),"people",delegate{ShowHomePage();OpenSideChat();},"Ctrl+Shift+N",Key.N,ModifierKeys.Control|ModifierKeys.Shift,shortcutId:"temporary");
            AddShellCommand("文件",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeB"),"folder",pet.ShowNewProjectDialog,shortcutId:"newproject");
            AddShellCommand("文件",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeC"),"folder",OpenProjectFolder,"Ctrl+O",Key.O,ModifierKeys.Control,shortcutId:"folder");
            AddShellCommand("文件",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeD"),"file",Export,null,Key.None,ModifierKeys.None,()=>session.History.Messages.Count>0,shortcutId:"export");
            AddShellCommand("文件",TextResources.Get("common.close"),"close",CloseShellContent,"Ctrl+W",Key.W,ModifierKeys.Control,shortcutId:"close");
            AddShellCommand("文件",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeE"),"exit",pet.Close,"Ctrl+Q",Key.Q,ModifierKeys.Control,shortcutId:"quit");
            AddEdit(TextResources.Get("shellMenus.buildShellCommands.addEdit"),ApplicationCommands.Undo,"Ctrl+Z");AddEdit(TextResources.Get("shellMenus.buildShellCommands.addEdit.alternativeA"),ApplicationCommands.Redo,"Ctrl+Y");AddEdit(TextResources.Get("shellMenus.buildShellCommands.addEdit.alternativeB"),ApplicationCommands.Cut,"Ctrl+X");AddEdit(TextResources.Get("common.copy"),ApplicationCommands.Copy,"Ctrl+C");AddEdit(TextResources.Get("shellMenus.buildShellCommands.addEdit.alternativeC"),ApplicationCommands.Paste,"Ctrl+V");AddEdit(TextResources.Get("common.delete"),ApplicationCommands.Delete,"Delete");AddEdit(TextResources.Get("shellMenus.buildShellCommands.addEdit.alternativeD"),ApplicationCommands.SelectAll,"Ctrl+A");
            AddShellCommand("编辑",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeF"),"settings",()=>pet.ShowSettings(),"Ctrl+,",Key.OemComma,ModifierKeys.Control,shortcutId:"settings");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeG"),"sidebar",ToggleAppSidebar,"Ctrl+Shift+S",Key.S,ModifierKeys.Control|ModifierKeys.Shift,HomeAvailable,shortcutId:"sidebar");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeH"),"summary",ToggleSummary,null,Key.None,ModifierKeys.None,HomeAvailable,shortcutId:"summary");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeI"),"terminal",OpenTerminal,"Ctrl+`",Key.Oem3,ModifierKeys.Control,HomeAvailable,shortcutId:"terminal");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeJ"),"new",OpenBlankTab,"Ctrl+Shift+B",Key.B,ModifierKeys.Control|ModifierKeys.Shift,HomeAvailable,shortcutId:"tab");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeK"),"tabs",ToggleToolArea,"Ctrl+Alt+B",Key.B,ModifierKeys.Control|ModifierKeys.Alt,HomeAvailable,shortcutId:"tools");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeL"),"tabs",ToggleFullTabView,"Ctrl+Shift+F",Key.F,ModifierKeys.Control|ModifierKeys.Shift,()=>HomeAvailable()&&ActualWidth>=1200,shortcutId:"fulltab");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeM"),"people",OpenSideChat,"Ctrl+Alt+S",Key.S,ModifierKeys.Control|ModifierKeys.Alt,HomeAvailable,shortcutId:"sidechat");
            AddShellCommand("视图",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeN"),"book",()=>OpenShellUrl(null),"Ctrl+T",Key.T,ModifierKeys.Control,shortcutId:"browser");
            AddShellCommand("导航",TextResources.Get("common.back"),"back",()=>TravelPage(-1),"Alt+←",Key.Left,ModifierKeys.Alt,()=>historyIndex>0,shortcutId:"back");
            AddShellCommand("导航",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeP"),"forward",()=>TravelPage(1),"Alt+→",Key.Right,ModifierKeys.Alt,()=>historyIndex<appHistory.Count-1,shortcutId:"forward");
            AddShellCommand("导航",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeQ"),"new",delegate{ShowHomePage();input.Focus();},"Alt+L",Key.L,ModifierKeys.Alt,shortcutId:"focus");
            AddShellCommand("帮助",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeR"),"book",()=>OpenShellUrl(ProjectHome),shortcutId:"docs");
            AddShellCommand("帮助",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeS"),"keys",delegate{RefreshShellShortcuts();new ShortcutHelpDialog(this,shellCommands).ShowDialog();},"Ctrl+/",Key.OemQuestion,ModifierKeys.Control,shortcutId:"help");
            AddShellCommand("帮助",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeT"),"news",()=>OpenShellUrl(ProjectHome+"/blob/main/CHANGELOG.md"),shortcutId:"news");
            AddShellCommand("帮助",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeU"),"feedback",()=>OpenShellUrl(ProjectHome+"/issues"),shortcutId:"feedback");
            AddShellCommand("帮助",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeV"),"help",pet.ShowOnboarding,shortcutId:"guide");
            AddShellCommand("帮助",TextResources.Get("shellMenus.buildShellCommands.addShellCommand.alternativeW"),"help",delegate{pet.ShowSettings();if(settingsPage!=null)settingsPage.SelectNavigation(4);},shortcutId:"about");
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
                string captured=group;var button=Ui.Button(ShortcutCatalog.GroupTitle(group));button.Name="AppMenu"+(group=="文件"?"File":group=="编辑"?"Edit":group=="视图"?"View":"Help");button.FontSize=13;button.MinHeight=0;button.Height=30;button.Padding=new Thickness(11,0,11,0);button.Margin=new Thickness(2,0,2,0);button.Background=button.BorderBrush=Brushes.Transparent;button.Foreground=AgentUi.Muted;WindowChrome.SetIsHitTestVisibleInChrome(button,true);
                button.PreviewMouseLeftButtonDown+=delegate{var target=EditTarget();if(target!=null)lastEditingTarget=target;};
                button.Click+=delegate{var menu=ChatUi.Menu();menu.Name="ShellMenu"+captured;menu.MinWidth=captured=="视图"?320:255;int i=0;foreach(var command in shellCommands.Where(c=>c.Group==captured)){if((captured=="文件"&&(i==3||i==4||i==5))||(captured=="编辑"&&(i==2||i==7))||(captured=="帮助"&&(i==3||i==4)))menu.Items.Add(new Separator());menu.Items.Add(CommandMenuItem(command));i++;}button.ContextMenu=menu;menu.PlacementTarget=button;menu.IsOpen=true;};host.Children.Add(button);
            }
        }
        MenuItem CommandMenuItem(ShellCommand command)
        {
            RefreshShellShortcuts();var item=new MenuItem{Header=command.Title,InputGestureText=command.GestureLabel??"",Icon=command.Icon==null?null:ShellIcons.Image(command.Icon),IsEnabled=command.CanExecute==null||command.CanExecute()};item.Click+=delegate{if(command.CanExecute==null||command.CanExecute())try{command.Execute();}catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is InvalidOperationException||error is System.ComponentModel.Win32Exception))throw;ShowStatus(TextResources.Get("shellMenus.commandMenuItem.showStatus"));}};return item;
        }
        void ShellKeyDown(object sender,KeyEventArgs e)
        {
            if(e.Handled)return;RefreshShellShortcuts();foreach(var menu in new[]{new[]{"menufile","AppMenuFile"},new[]{"menuedit","AppMenuEdit"},new[]{"menuview","AppMenuView"},new[]{"menuhelp","AppMenuHelp"}})if(ShortcutCatalog.Matches(pet.Settings.LocalShortcuts[menu[0]],e)){var button=FindTitleMenu(this,menu[1]);if(button!=null){e.Handled=true;button.Focus();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}return;}if(e.Key==Key.Escape&&sidebarTemporary){e.Handled=true;sidebarTemporary=false;RefreshWorkspaceLayout();return;}foreach(var command in shellCommands){if(command.ShortcutId!=null?ShortcutCatalog.Matches(pet.Settings.LocalShortcuts[command.ShortcutId],e):command.Gesture!=null&&command.Gesture.Matches(this,e)){e.Handled=true;if(command.CanExecute==null||command.CanExecute())command.Execute();return;}}
        }
        static Button FindTitleMenu(DependencyObject root,string name)
        {
            var button=root as Button;if(button!=null&&button.Name==name)return button;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindTitleMenu(VisualTreeHelper.GetChild(root,i),name);if(found!=null)return found;}return null;
        }
        void OpenShellUrl(string url)
        {ShowHomePage();EnsurePreviewWorkspace();filePreview.AddPage(url==null?TextResources.Get("shellMenus.openShellUrl.filePreviewAddPage"):url.EndsWith("/issues",StringComparison.Ordinal)?TextResources.Get("shellMenus.openShellUrl.filePreviewAddPage.alternativeA"):TextResources.Get("shellMenus.openShellUrl.filePreviewAddPage.alternativeB"),"globe",new BrowserPane(url));PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
        void OpenProjectFolder()
        {
            using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description=TextResources.Get("shellMenus.openProjectFolder.description"),ShowNewFolderButton=true}){if(picker.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;string path=System.IO.Path.GetFullPath(picker.SelectedPath);var project=pet.Projects.Projects.FirstOrDefault(p=>String.Equals(System.IO.Path.GetFullPath(p.Workspace),path,StringComparison.OrdinalIgnoreCase));if(project==null){project=ProjectCatalogStore.CreateProject(pet.Projects,System.IO.Path.GetFileName(path),path);pet.ProjectStore.Save(pet.Projects);}pet.ActivateProject(project);}
        }
        void CloseShellContent(){if(shellPage!="home"){ShowHomePage();return;}if(filePreview!=null&&filePreview.Selected!=null&&!ReferenceEquals(filePreview.Selected,conversationSurface)){filePreview.Close(filePreview.Selected);return;}Close();}
    }
}
