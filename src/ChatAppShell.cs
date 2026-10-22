using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        Grid appPages,homePage;Border appRail;Image shellAvatarImage;Button shellBack,shellForward,shellHome,shellUsage,shellMcp,shellSettings,shellSidebar;
        SettingsPage settingsPage;UsageStatisticsPage usagePage;PluginsPage mcpPage;ImageWorkspacePage imagePage;Button shellImages;string shellPage="home";bool shellReady,restoringRoute,sidebarHidden,sidebarTemporary;
        readonly List<AppRoute> appHistory=new List<AppRoute>();int historyIndex=-1;
        readonly List<ShellCommand> shellCommands=new List<ShellCommand>();IInputElement lastEditingTarget;
        string liveReturnPage;int liveReturnSection,liveReturnHistory;
        sealed class AppRoute{internal string Page;internal ConversationWorkspace Workspace;internal int Section;}
        internal string CurrentPage {get{return shellPage;}}
        internal bool HasUnsavedMcpEdits {get{return mcpPage!=null&&mcpPage.HasUnsavedEdits;}}
        internal void InitializeAppShell()
        {
            Content=null;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=46,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
            var frame=new Grid{Background=Ui.Brush("#EDF3F8")};frame.RowDefinitions.Add(new RowDefinition{Height=new GridLength(46)});frame.RowDefinitions.Add(new RowDefinition());
            var top=new DockPanel{Name="AppTitleBar",LastChildFill=true,Margin=new Thickness(8,0,4,0)};
            var captionButtons=new StackPanel{Orientation=Orientation.Horizontal};
            var minimize=ShellIcons.Button("minimize","最小化",()=>SystemCommands.MinimizeWindow(this));minimize.Name="AppMinimize";
            var maximize=ShellIcons.Button("maximize","最大化／还原",delegate{if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);});maximize.Name="AppMaximize";
            var close=ShellIcons.Button("close","关闭窗口",Close);close.Name="AppClose";
            foreach(var b in new[]{minimize,maximize,close}){b.Width=42;b.Height=36;WindowChrome.SetIsHitTestVisibleInChrome(b,true);captionButtons.Children.Add(b);}DockPanel.SetDock(captionButtons,Dock.Right);top.Children.Add(captionButtons);
            var navButtons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            shellBack=ShellIcons.Button("back","返回 · Alt+←",()=>TravelPage(-1));shellBack.Name="AppBack";shellForward=ShellIcons.Button("forward","前进 · Alt+→",()=>TravelPage(1));shellForward.Name="AppForward";
            shellSidebar=ShellIcons.Button("sidebar","显示／隐藏侧边栏 · Ctrl+Shift+S",ToggleAppSidebar);shellSidebar.Name="ToggleAppSidebar";
            foreach(var b in new[]{shellBack,shellForward,shellSidebar}){WindowChrome.SetIsHitTestVisibleInChrome(b,true);navButtons.Children.Add(b);}DockPanel.SetDock(navButtons,Dock.Left);top.Children.Add(navButtons);
            var menuButtons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};BuildShellCommands();BuildTitleMenus(menuButtons);DockPanel.SetDock(menuButtons,Dock.Left);top.Children.Add(menuButtons);top.Children.Add(new Border{Background=Brushes.Transparent});frame.Children.Add(top);
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(60)});body.ColumnDefinitions.Add(new ColumnDefinition());
            var railBody=new Grid{Margin=new Thickness(6,8,6,10)};railBody.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});railBody.RowDefinitions.Add(new RowDefinition());railBody.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var railTop=new StackPanel();shellHome=ShellIcons.Button("home","首页 · 完整对话",()=>ShowHomePage());shellHome.Name="ShellHome";shellUsage=ShellIcons.Button("usage","用量统计",ShowUsagePage);shellUsage.Name="ShellUsage";shellMcp=ShellIcons.Button("mcp","插件",ShowPluginsPage);shellMcp.Name="ShellMcp";shellSettings=ShellIcons.Button("settings","设置",()=>pet.ShowSettings());shellSettings.Name="ShellSettings";shellImages=ShellIcons.Button("image","图像",()=>ShowImagesPage());shellImages.Name="ShellImages";foreach(var b in new[]{shellHome,shellImages,shellUsage,shellMcp,shellSettings}){b.Width=b.Height=40;b.Margin=new Thickness(0,3,0,3);railTop.Children.Add(b);}railBody.Children.Add(railTop);
            shellAvatarImage=new Image{Width=30,Height=30,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,30,30),10,10)};RenderOptions.SetBitmapScalingMode(shellAvatarImage,BitmapScalingMode.HighQuality);var profile=ShellIcons.Button("pet","伙伴菜单",OpenProfileMenu);profile.Name="ShellAvatar";profile.Width=profile.Height=40;profile.Content=shellAvatarImage;Grid.SetRow(profile,2);railBody.Children.Add(profile);appRail=new Border{Name="AppIconRail",Background=ThemeManager.Brush("#EDF3F8","#202733"),Child=railBody};body.Children.Add(appRail);
            appPages=new Grid{Name="AppPageHost",Background=ThemeManager.Surface,ClipToBounds=true};homePage=new Grid{Name="AppHomePage"};homePage.Children.Add(workspaceShell);appPages.Children.Add(homePage);
            var pageFrame=new Border{Name="AppContentFrame",Background=ThemeManager.Surface,CornerRadius=new CornerRadius(16),BorderBrush=Ui.Brush("#E3E7EC"),BorderThickness=new Thickness(1),Child=appPages,ClipToBounds=true};pageFrame.SizeChanged+=delegate{pageFrame.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,pageFrame.ActualWidth),Math.Max(0,pageFrame.ActualHeight)),16,16);};Grid.SetColumn(pageFrame,1);body.Children.Add(pageFrame);Grid.SetRow(body,1);frame.Children.Add(body);Content=frame;
            sidebarHidden=false;shellReady=true;RecordPage("home");RefreshShellAppearance();
            PreviewKeyDown+=ShellKeyDown;PreviewMouseDown+=delegate(object sender,MouseButtonEventArgs e){if(sidebarTemporary&&!navigation.IsMouseOver&&!shellSidebar.IsMouseOver){sidebarTemporary=false;RefreshWorkspaceLayout();}if(e.ChangedButton==MouseButton.XButton1){e.Handled=true;TravelPage(-1);}else if(e.ChangedButton==MouseButton.XButton2){e.Handled=true;TravelPage(1);}};
            AddHandler(Keyboard.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler(delegate(object sender,KeyboardFocusChangedEventArgs e){var element=e.NewFocus as FrameworkElement;if(element!=null&&(element is System.Windows.Controls.Primitives.TextBoxBase||element is PasswordBox)&&element.IsVisible)lastEditingTarget=e.NewFocus;}),true);
            Deactivated+=delegate{CloseProfileMenu();if(sidebarTemporary){sidebarTemporary=false;RefreshWorkspaceLayout();}};
            StateChanged+=delegate{maximize.Content=ShellIcons.Image(WindowState==WindowState.Maximized?"restore":"maximize");};
            SourceInitialized+=delegate{HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(ShellWindowMessage);};
            var compose=ShellIcons.Button("new-chat","新建对话",pet.NewShellConversation);compose.Name="ShellNewChat";compose.Margin=new Thickness(0,0,10,0);DockPanel.SetDock(compose,Dock.Left);conversationHeader.Children.Insert(0,compose);
            Closed+=delegate{if(settingsPage!=null)settingsPage.Dispose();if(usagePage!=null)usagePage.Dispose();if(mcpPage!=null)mcpPage.Dispose();if(imagePage!=null)imagePage.Dispose();pet.ReleaseSettingsPage(settingsPage);};
        }
        internal void ShowHomePage(bool record=true)
        {
            if(!shellReady)return;SetShellPage("home");if(record)RecordPage("home");Dispatcher.BeginInvoke(new Action(delegate{if(!closed)input.Focus();}));
        }
        internal void ShowSettingsPage(SettingsPage page)
        {
            if(!shellReady||closed||page==null)return;
            if(settingsPage==null){settingsPage=page;settingsPage.Visibility=Visibility.Collapsed;appPages.Children.Add(page);page.NavigationChanged+=SettingsSectionChanged;}
            if(!ReferenceEquals(settingsPage,page))throw new InvalidOperationException("设置页已属于其他主窗口。");SetShellPage("settings");RecordPage("settings");
        }
        void SettingsSectionChanged(int section){if(shellPage=="settings"&&!restoringRoute)RecordPage("settings");}
        internal void ShowUsagePage()
        {
            if(!shellReady||closed)return;if(usagePage==null){usagePage=new UsageStatisticsPage(pet.UsageStats){Visibility=Visibility.Collapsed};appPages.Children.Add(usagePage);}SetShellPage("usage");RecordPage("usage");
        }
        internal void ShowMcpPage(bool edit=false){ShowPluginsPage();if(mcpPage!=null){if(edit)mcpPage.OpenMcpEditor();else mcpPage.SelectMcp();}}
        internal void ShowPluginsPage()
        {
            if(!shellReady||closed)return;if(mcpPage==null){mcpPage=new PluginsPage(pet){Visibility=Visibility.Collapsed};appPages.Children.Add(mcpPage);}SetShellPage("mcp");RecordPage("mcp");
        }
        internal void ShowImageTask(string id){ShowImagesPage();if(imagePage!=null)imagePage.ShowTasks(id);}
        internal void ShowImagesPage(GeneratedImage previous=null)
        {
            if(!shellReady||closed)return;if(imagePage==null){imagePage=new ImageWorkspacePage(pet){Visibility=Visibility.Collapsed};appPages.Children.Add(imagePage);}imagePage.RefreshProviders();SetShellPage("images");RecordPage("images");if(previous!=null)imagePage.Prepare(previous);
        }
        void SetShellPage(string page)
        {
            CloseTransientSummary();CloseProfileMenu();shellPage=page;if(imagePage!=null)imagePage.Visibility=page=="images"?Visibility.Visible:Visibility.Collapsed;homePage.Visibility=page=="home"?Visibility.Visible:Visibility.Collapsed;if(settingsPage!=null)settingsPage.Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed;if(usagePage!=null)usagePage.Visibility=page=="usage"?Visibility.Visible:Visibility.Collapsed;if(mcpPage!=null)mcpPage.Visibility=page=="mcp"?Visibility.Visible:Visibility.Collapsed;RefreshShellAppearance();if(page=="home")Dispatcher.BeginInvoke(new Action(RefreshWorkspaceLayout));
        }
        void RecordPage(string page)
        {
            if(restoringRoute||!shellReady)return;var route=new AppRoute{Page=page,Workspace=workspace,Section=settingsPage==null?0:settingsPage.SelectedNavigationIndex};var prior=historyIndex>=0?appHistory[historyIndex]:null;
            if(prior!=null&&prior.Page==page&&(page!="home"||ReferenceEquals(prior.Workspace,workspace))&&(page!="settings"||prior.Section==route.Section)){RefreshShellAppearance();return;}
            if(historyIndex+1<appHistory.Count)appHistory.RemoveRange(historyIndex+1,appHistory.Count-historyIndex-1);appHistory.Add(route);if(appHistory.Count>100)appHistory.RemoveAt(0);historyIndex=appHistory.Count-1;RefreshShellAppearance();
        }
        void TravelPage(int direction)
        {
            int next=historyIndex+direction;if(next<0||next>=appHistory.Count)return;var route=appHistory[next];restoringRoute=true;
            try{if(route.Page=="settings"){if(settingsPage==null)return;SetShellPage("settings");settingsPage.SelectNavigation(route.Section);}else if(route.Page=="images"){ShowImagesPage();}else if(route.Page=="usage"){ShowUsagePage();}else if(route.Page=="mcp"){ShowPluginsPage();}else{if(!ReferenceEquals(workspace,route.Workspace)&&(route.Workspace==null||pet.Workspaces.OpenWorkspaces.Contains(route.Workspace)))SwitchWorkspace(route.Workspace);SetShellPage("home");}historyIndex=next;}
            finally{restoringRoute=false;RefreshShellAppearance();}
        }
        void RefreshShellAppearance()
        {
            if(!shellReady)return;shellBack.IsEnabled=historyIndex>0;shellForward.IsEnabled=historyIndex>=0&&historyIndex<appHistory.Count-1;shellSidebar.IsEnabled=shellPage=="home";SelectRailIcon(shellHome,"home",shellPage=="home");SelectRailIcon(shellImages,"image",shellPage=="images");SelectRailIcon(shellUsage,"usage",shellPage=="usage");SelectRailIcon(shellMcp,"mcp",shellPage=="mcp");SelectRailIcon(shellSettings,"settings",shellPage=="settings");shellAvatarImage.Source=SkinCatalog.GetAvatar(pet.Skin);shellAvatarImage.ToolTip=pet.Skin.Name;
        }
        void SelectRailIcon(Button button,string icon,bool selected)
        {button.Content=ShellIcons.Image(icon,selected?Ui.Brush("#1C2024"):AgentUi.Muted,18,selected);button.Background=selected?Ui.Brush("#E1E8EE"):Brushes.Transparent;System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"已选中":"");}
        void ToggleAppSidebar()
        {if(shellPage!="home")return;bool shown=navigation!=null&&navigation.IsVisible;sidebarHidden=shown;sidebarTemporary=!shown&&workspaceShell.ActualWidth<900;pet.Settings.ShowProjectSidebar=!shown;pet.SaveSettings();RefreshWorkspaceLayout();}
        void LayoutAppSidebar(bool wide)
        {
            if(navigation==null)return;if(wide)sidebarTemporary=false;bool overlay=sidebarTemporary&&!sidebarHidden&&!wide&&shellPage=="home";navigation.Visibility=wide||overlay?Visibility.Visible:Visibility.Collapsed;Grid.SetColumn(navigation,overlay?2:0);Grid.SetColumnSpan(navigation,overlay?3:1);navigation.Width=overlay?Math.Max(210,Math.Min(navigationWidth,workspaceShell.ActualWidth-40)):Double.NaN;navigation.HorizontalAlignment=overlay?HorizontalAlignment.Left:HorizontalAlignment.Stretch;Panel.SetZIndex(navigation,overlay?40:0);navigation.Effect=overlay?new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=18,ShadowDepth=5,Opacity=.14}:null;
            if(shellReady){shellSidebar.Background=wide||overlay?Ui.Brush("#E1E8EE"):Brushes.Transparent;shellSidebar.ToolTip=(wide||overlay?"隐藏":"显示")+"侧边栏 · Ctrl+Shift+S";var compose=conversationHeader.Children.OfType<Button>().FirstOrDefault(b=>b.Name=="ShellNewChat");if(compose!=null)compose.Visibility=wide?Visibility.Collapsed:Visibility.Visible;}
        }
        void OpenProfileMenu()
        {
            var button=FindShellButton("ShellAvatar");if(button.ContextMenu!=null&&button.ContextMenu.IsOpen){button.ContextMenu.IsOpen=false;return;}
            var menu=ChatUi.Menu();menu.Name="ShellProfileMenu";menu.MinWidth=258;var heading=new StackPanel{Margin=new Thickness(0,4,0,8)};heading.Children.Add(Ui.Text(pet.Skin.Name,14,AgentUi.Ink));heading.Children.Add(Ui.Text("桌面伙伴",11,AgentUi.Muted));menu.Items.Add(new MenuItem{Header=heading,Icon=new Image{Source=SkinCatalog.GetAvatar(pet.Skin),Width=24,Height=24},IsEnabled=false});menu.Items.Add(new Separator());
            AddProfile(menu,"ProfileUsage","用量统计","usage",ShowUsagePage);
            AddProfile(menu,"ProfilePetVisibility",pet.IsPetVisible?"隐藏桌宠":"显示桌宠","pet",delegate{if(pet.IsPetVisible)pet.HidePet();else pet.RestorePet();});
            var skins=new MenuItem{Name="ProfileCompanions",Header="切换伙伴",Icon=ShellIcons.Image("people")};foreach(var skin in pet.Catalog.Skins){var selected=skin;var entry=new MenuItem{Header=skin.Name,IsCheckable=true,IsChecked=skin.PickerKey==pet.Skin.PickerKey,IsEnabled=!pet.DialogueBusy&&(settingsPage==null||!settingsPage.HasUnsavedEdits),Icon=new Image{Source=SkinCatalog.GetAvatar(skin),Width=20,Height=20}};entry.Click+=async delegate{await pet.RequestSkinAsync(selected,CompanionSelectionOrigin.MainWindow,this);};skins.Items.Add(entry);}menu.Items.Add(skins);
            AddProfile(menu,"ProfileSettings","设置","settings",()=>pet.ShowSettings());menu.Items.Add(new Separator());var help=new MenuItem{Name="ProfileHelp",Header="帮助",Icon=ShellIcons.Image("help")};foreach(var command in shellCommands.Where(c=>c.Group=="帮助"))help.Items.Add(CommandMenuItem(command));menu.Items.Add(help);button.ContextMenu=menu;menu.PlacementTarget=button;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;menu.StaysOpen=false;menu.IsOpen=true;
        }
        void AddProfile(ContextMenu menu,string name,string label,string icon,Action action){var item=new MenuItem{Name=name,Header=label,Icon=ShellIcons.Image(icon)};item.Click+=delegate{menu.IsOpen=false;action();};menu.Items.Add(item);}
        Button FindShellButton(string name){return appRail.Child is Grid?((Grid)appRail.Child).Children.OfType<Button>().First(b=>b.Name==name):null;}
        void CloseProfileMenu(){if(appRail==null)return;var button=FindShellButton("ShellAvatar");if(button!=null&&button.ContextMenu!=null)button.ContextMenu.IsOpen=false;}
        internal void MountLiveContent(FrameworkElement view){liveReturnPage=shellPage;liveReturnSection=settingsPage==null?0:settingsPage.SelectedNavigationIndex;liveReturnHistory=historyIndex;ShowHomePage(false);workspaceShell.Visibility=Visibility.Collapsed;homePage.Children.Add(view);}
        internal void UnmountLiveContent(FrameworkElement view)
        {
            homePage.Children.Remove(view);workspaceShell.Visibility=Visibility.Visible;
            if(shellPage=="home"&&historyIndex==liveReturnHistory&&liveReturnPage=="settings"&&settingsPage!=null){restoringRoute=true;try{SetShellPage("settings");settingsPage.SelectNavigation(liveReturnSection);}finally{restoringRoute=false;}}
            else if(shellPage=="home"&&historyIndex==liveReturnHistory&&liveReturnPage=="usage")SetShellPage("usage");
            else if(shellPage=="home"&&historyIndex==liveReturnHistory&&liveReturnPage=="mcp")SetShellPage("mcp");
            liveReturnPage=null;RefreshWorkspaceLayout();
        }
        IntPtr ShellWindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(message==0x0024){var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};if(GetMonitorInfo(monitor,ref info)){var bounds=(MinMaxInfo)Marshal.PtrToStructure(lParam,typeof(MinMaxInfo));bounds.MaxPosition.X=info.Work.Left-info.Monitor.Left;bounds.MaxPosition.Y=info.Work.Top-info.Monitor.Top;bounds.MaxSize.X=info.Work.Right-info.Work.Left;bounds.MaxSize.Y=info.Work.Bottom-info.Work.Top;Marshal.StructureToPtr(bounds,lParam,false);handled=true;}}return IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
        [StructLayout(LayoutKind.Sequential)]struct MinMaxInfo{public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
        [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public int Flags;}
        [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    }
}
