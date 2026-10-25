using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        bool fullTabView,tabsHidden,unifiedLayout,resumeConversation;
        double previewOverdrag;StackPanel previewViewActions;
        readonly Button fullTabToggle=ShellIcons.Button("expand-tabs",()=>(TextResources.Get("chatUnifiedWorkspace.fullTabToggle.shellIcons")),null),sideTabsToggle=ShellIcons.Button("collapse-tools",()=>(TextResources.Get("chatUnifiedWorkspace.fullTabToggle.shellIcons.alternativeA")),null);
        void InitializeTabViewTools()
        {
            var tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,18,16,0)};previewViewActions=tools;Grid.SetColumn(tools,6);Panel.SetZIndex(tools,10);workspaceShell.Children.Add(tools);
            fullTabToggle.Name="ToggleFullTabView";sideTabsToggle.Name="ToggleSideTabs";
            foreach(var button in new[]{fullTabToggle,sideTabsToggle}){button.Width=button.Height=button.MinHeight=30;button.Padding=new Thickness(6);tools.Children.Add(button);}
            fullTabToggle.Click+=delegate{ToggleFullTabView();};sideTabsToggle.Click+=delegate{ToggleToolArea();};
            ToolTipService.SetShowOnDisabled(fullTabToggle,true);
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(shellReady)return;if(Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key==Key.F){e.Handled=true;ToggleFullTabView();}};
        }
        internal void ToggleFullTabView()
        {
            if(closed || ActualWidth<1200)return;if(filePreview==null)OpenBlankTab();
            // Keep the chosen layout independent of whether the tool area is currently hidden.
            fullTabView=!fullTabView;if(!fullTabView)resumeConversation=false;tabsHidden=false;previewVisible=true;if(!fullTabView&&filePreview!=null&&filePreview.ToolCount==0){CloseFilePreview();return;}RefreshWorkspaceLayout();
        }
        internal void ToggleToolArea()
        {
            if(closed)return;if(filePreview==null){OpenBlankTab();return;}
            bool shown=!tabsHidden&&(ActualWidth>=1200||previewVisible);tabsHidden=shown;if(!tabsHidden)previewVisible=true;RefreshWorkspaceLayout();
        }
        internal void ResizePreview(double delta)
        {
            if(filePreview==null||unifiedLayout)return;
            double width=workspaceShell.ActualWidth,nav=workspaceShell.ColumnDefinitions[0].ActualWidth,current=workspaceShell.ColumnDefinitions[6].ActualWidth;
            double maximum=Math.Max(320,width-nav-430),desired=current-delta;
            if(delta<0)previewOverdrag+=Math.Max(0,desired-maximum);else previewOverdrag=0;
            if(desired>=width-nav-180||previewOverdrag>=140){fullTabView=true;tabsHidden=false;previewVisible=true;previewOverdrag=0;}
            else previewWidth=Clamp(desired,320,maximum);
        }
        void SyncConversationTab(bool unified)
        {
            if(unifiedLayout==unified)return;double offset=scroll.VerticalOffset;bool focus=input.IsKeyboardFocusWithin;
            unifiedLayout=unified;
            if(unified){workspaceShell.Children.Remove(conversationSurface);Grid.SetColumn(conversationSurface,0);Grid.SetColumnSpan(conversationSurface,1);filePreview.AttachConversation(conversationSurface,conversationTitle.Text);if(resumeConversation){filePreview.SelectConversation();resumeConversation=false;}}
            else{if(tabsHidden||!previewVisible)resumeConversation=ReferenceEquals(filePreview.Selected,conversationSurface);RestoreConversationSurface();}
            // Reparent the existing controls, preserving both the draft and history position.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate{if(closed)return;scroll.ScrollToVerticalOffset(offset);if(focus&&conversationSurface.IsVisible)input.Focus();}));
        }
        void RestoreConversationSurface()
        {
            if(filePreview!=null&&filePreview.HasConversation)filePreview.DetachConversation();
            unifiedLayout=false;Grid.SetColumn(conversationSurface,2);Grid.SetColumnSpan(conversationSurface,3);if(!workspaceShell.Children.Contains(conversationSurface))workspaceShell.Children.Add(conversationSurface);conversationSurface.Visibility=Visibility.Visible;
        }
        void RefreshTabViewTools(bool shown)
        {
            fullTabToggle.Visibility=sideTabsToggle.Visibility=filePreview!=null?Visibility.Visible:Visibility.Collapsed;
            filePreviewDock.Margin=new Thickness(0,ActualWidth>=1200?16:48,12,6);if(filePreview!=null)filePreview.SetToolbarInset(ActualWidth>=1200?(unifiedLayout?132:90):0);newTab.Visibility=shown?Visibility.Collapsed:Visibility.Visible;
            workspaceActionSeparator.Visibility=shown?Visibility.Collapsed:Visibility.Visible;
            Grid.SetColumn(workspaceActions,unifiedLayout?6:2);Grid.SetColumnSpan(workspaceActions,unifiedLayout?1:3);workspaceActions.Margin=new Thickness(0,18,filePreview!=null&&(unifiedLayout||!shown)?94:14,0);Grid.SetColumn(previewViewActions,shown?6:2);Grid.SetColumnSpan(previewViewActions,shown?1:3);
            previewViewActions.Margin=new Thickness(0,18,shown?16:24,0);
            bool canSwitch=ActualWidth>=1200,returnToSplit=canSwitch&&(fullTabView||unifiedLayout);
            fullTabToggle.IsEnabled=canSwitch;
            fullTabToggle.Content=ShellIcons.Image(returnToSplit?"split-panels":"expand-tabs",Ui.Ink);
            Func<string> layoutAction=()=>!canSwitch?TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.layoutActionCanSwitchReturnToSplit"):returnToSplit?TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.layoutActionCanSwitchReturnToSplit.alternativeA"):TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.layoutActionCanSwitchReturnToSplit.alternativeB");
            LocalizedUi.Tooltip(fullTabToggle,layoutAction);LocalizedUi.Automation(fullTabToggle,layoutAction);
            LocalizedUi.HelpText(fullTabToggle,()=>(fullTabView||unifiedLayout?TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.automationAutomationPropertiesSetHelpText"):TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.automationAutomationPropertiesSetHelpText.alternativeA"))+(shown?TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.automationAutomationPropertiesSetHelpText.alternativeB"):TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.automationAutomationPropertiesSetHelpText.alternativeC")));
            var visibilityIcon=ShellIcons.Image("collapse-tools",Ui.Ink);if(!shown){visibilityIcon.RenderTransformOrigin=new Point(.5,.5);visibilityIcon.RenderTransform=new ScaleTransform(-1,1);}sideTabsToggle.Content=visibilityIcon;
            Func<string> visibilityAction=()=>shown?TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.visibilityActionShown"):TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.visibilityActionShown.alternativeA");
            LocalizedUi.Tooltip(sideTabsToggle,visibilityAction);LocalizedUi.Automation(sideTabsToggle,visibilityAction);LocalizedUi.HelpText(sideTabsToggle,()=>shown?TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.automationAutomationPropertiesSetHelpText.alternativeD"):TextResources.Get("chatUnifiedWorkspace.refreshTabViewTools.automationAutomationPropertiesSetHelpText.alternativeE"));sideTabsToggle.Background=shown?AgentUi.Soft:Brushes.Transparent;
        }
    }
}
