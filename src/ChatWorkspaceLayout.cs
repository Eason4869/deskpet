using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        readonly ContentControl compactTaskHost=new ContentControl();readonly TextBlock conversationTitle=LocalizedUi.Text(()=>(TextResources.Get("chatWorkspaceLayout.conversationTitle.text")),13,AgentUi.Ink);
        readonly TextBlock summaryEmpty=LocalizedUi.Text(()=>(TextResources.Get("chatWorkspaceLayout.summaryEmpty.text")),12,AgentUi.Muted);
        readonly Button summaryToggle=UiIcons.Button("summary",()=>(TextResources.Get("chatWorkspaceLayout.summaryToggle.uiIcons"))),newTab=UiIcons.Button("new-tab",()=>(TextResources.Get("chatWorkspaceLayout.summaryToggle.uiIcons.alternativeA")));
        StackPanel workspaceActions;Border workspaceActionSeparator;Thumb navigationDivider,previewDivider;bool summaryPinned=true,summaryManuallyShown;double navigationWidth=252,summaryWidth=320,previewWidth=Double.NaN;
        Popup transientSummary;SummaryPreview transientSummaryView;readonly Button backToConversation=UiIcons.Button("back",()=>(TextResources.Get("chatWorkspaceLayout.backToConversation.uiIcons")));
        readonly TextBlock previewContextTitle=new TextBlock{Name="PreviewContextTitle",FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=AgentUi.Ink,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(50,17,90,0)};
        void InitializeWorkspaceTools()
        {
            var tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,18,14,0)};workspaceActions=tools;summaryToggle.Name="ToggleWorkspaceSummary";newTab.Name="NewWorkspaceTab";summaryToggle.Width=summaryToggle.Height=newTab.Width=newTab.Height=30;summaryToggle.MinHeight=newTab.MinHeight=30;summaryToggle.Padding=newTab.Padding=new Thickness(6);LocalizedUi.Set(newTab,"ToolTip",()=>(TextResources.Get("chatWorkspaceLayout.initializeWorkspaceTools.newTabToolTip")));summaryToggle.Click+=delegate{ToggleSummary();};newTab.Click+=delegate{OpenBlankTab();};tools.Children.Add(summaryToggle);workspaceActionSeparator=new Border{Width=1,Height=16,Background=Ui.Line,Margin=new Thickness(5,0,5,0)};tools.Children.Add(workspaceActionSeparator);tools.Children.Add(newTab);InitializeTabViewTools();Grid.SetColumn(tools,2);Grid.SetColumnSpan(tools,3);Panel.SetZIndex(tools,10);workspaceShell.Children.Add(tools);
            backToConversation.Name="BackToConversation";LocalizedUi.Set(backToConversation,"ToolTip",()=>(TextResources.Get("chatWorkspaceLayout.initializeWorkspaceTools.backToConversationToolTip")));backToConversation.HorizontalAlignment=HorizontalAlignment.Left;backToConversation.VerticalAlignment=VerticalAlignment.Top;backToConversation.Margin=new Thickness(8,10,0,0);backToConversation.Click+=delegate{previewVisible=false;RefreshWorkspaceLayout();};Grid.SetColumn(backToConversation,6);workspaceShell.Children.Add(backToConversation);
            previewContextTitle.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Text"){Source=conversationTitle});Grid.SetColumn(previewContextTitle,6);workspaceShell.Children.Add(previewContextTitle);navigationDivider=Divider("NavigationDivider",1,delta=>navigationWidth=Clamp(workspaceShell.ColumnDefinitions[0].ActualWidth+delta,180,420));previewDivider=Divider("PreviewDivider",5,ResizePreview);previewDivider.DragStarted+=delegate{previewOverdrag=0;};previewDivider.DragCompleted+=delegate{previewOverdrag=0;};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(shellReady)return;if(Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key==Key.B){e.Handled=true;OpenBlankTab();}else if(Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Alt)&&e.Key==Key.S){e.Handled=true;OpenSideChat();}else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Oem3){e.Handled=true;OpenTerminal();}};Deactivated+=delegate{CloseTransientSummary();};
        }
        Thumb Divider(string name,int column,Action<double> change)
        {
            var template=new FrameworkElementFactory(typeof(Border));template.SetValue(Border.BackgroundProperty,Brushes.Transparent);var line=new FrameworkElementFactory(typeof(Border));line.SetValue(Border.BackgroundProperty,Ui.Brush("#E6E9EE"));line.SetValue(Border.WidthProperty,1.0);template.AppendChild(line);var grip=LocalizedUi.With(new Thumb{Name=name,Cursor=Cursors.SizeWE,Focusable=false,Template=new ControlTemplate(typeof(Thumb)){VisualTree=template},Background=Brushes.Transparent,ToolTip=TextResources.Get("chatWorkspaceLayout.divider.toolTip"),Margin=new Thickness(0,48,0,6)},"ToolTip",()=>(TextResources.Get("chatWorkspaceLayout.divider.toolTip")));grip.DragDelta+=delegate(object sender,DragDeltaEventArgs e){change(e.HorizontalChange);RefreshWorkspaceLayout();};Grid.SetColumn(grip,column);workspaceShell.Children.Add(grip);return grip;
        }
        static double Clamp(double value,double min,double max){return Math.Max(min,Math.Min(max,value));}
        bool LayoutWorkspaceColumns(bool wide,bool previewDocked)
        {
            bool compact=unifiedLayout;RefreshTabViewTools(previewDocked);previewContextTitle.Visibility=Visibility.Collapsed;backToConversation.Visibility=compact&&ActualWidth<1200?Visibility.Visible:Visibility.Collapsed;workspaceShell.ColumnDefinitions[2].Width=compact?new GridLength(0):new GridLength(1,GridUnitType.Star);workspaceShell.ColumnDefinitions[6].Width=compact?new GridLength(1,GridUnitType.Star):new GridLength(0);double width=workspaceShell.ActualWidth>0?workspaceShell.ActualWidth:ActualWidth;double nav=wide?Clamp(navigationWidth,180,Math.Min(420,width*.28)):0;
            double desiredPreview=Double.IsNaN(previewWidth)?Math.Min(760,width*.46):previewWidth;
            double preview=compact?0:previewDocked?Clamp(desiredPreview,320,Math.Max(320,width-nav-430)):0;
            bool show=summaryPinned&&(WorkEntry||summaryManuallyShown)&&width-nav-preview-10>=summaryWidth+740;MoveSummaryHost(compact);
            double summary=show&&!compact?summaryWidth:0;var widths=new[]{nav,wide?5:0,0,0,summary,previewDocked&&!compact?5:0,preview};
            for(int i=0;i<widths.Length;i++)if(i!=2&&!(compact&&i==6))workspaceShell.ColumnDefinitions[i].Width=new GridLength(widths[i]);
            navigationDivider.Visibility=wide?Visibility.Visible:Visibility.Collapsed;previewDivider.Visibility=previewDocked&&!compact?Visibility.Visible:Visibility.Collapsed;
            compactTaskHost.Visibility=Visibility.Collapsed;summaryToggle.Background=show?AgentUi.Soft:Brushes.Transparent;summaryToggle.Content=ShellIcons.Image("summary",show?Ui.Brush("#1C2024"):AgentUi.Muted);System.Windows.Automation.AutomationProperties.SetItemStatus(summaryToggle,show?TextResources.Get("chatWorkspaceLayout.layoutWorkspaceColumns.automationAutomationPropertiesSetItemStatus"):"");summaryToggle.ToolTip=show?TextResources.Get("chatWorkspaceLayout.layoutWorkspaceColumns.toolTipShowSummaryHasRoom"):SummaryHasRoom()?TextResources.Get("chatWorkspaceLayout.layoutWorkspaceColumns.toolTipShowSummaryHasRoom.alternativeA"):TextResources.Get("chatWorkspaceLayout.layoutWorkspaceColumns.toolTipShowSummaryHasRoom.alternativeB");if(show)CloseTransientSummary();return show;
        }
        void LayoutConversationViewport(bool summaryVisible)
        {
            double inset=summaryVisible?summaryWidth:0;
            conversationSurface.MaxWidth=Double.PositiveInfinity;
            conversationHeader.MaxWidth=historyTools.MaxWidth=messages.MaxWidth=920;
            conversationHeader.Margin=new Thickness(0,0,inset,16);conversationIdentity.Margin=new Thickness(0,0,filePreview!=null&&!filePreviewDock.IsVisible?160:78,0);
            historyTools.Margin=new Thickness(0,0,inset,0);
            messages.Margin=new Thickness(2,8,inset+20,12);historyOverlay.Margin=new Thickness(0,0,inset,12);RefreshHistoryNavigation();
            if(composer!=null){composer.MaxWidth=920;composer.Margin=new Thickness(0,12,inset,0);}
        }
        bool SummaryHasRoom()
        {double nav=workspaceShell.ActualWidth>=900&&!sidebarHidden?Clamp(navigationWidth,180,Math.Min(420,workspaceShell.ActualWidth*.28)):0;double tools=unifiedLayout?0:workspaceShell.ColumnDefinitions[6].ActualWidth;return workspaceShell.ActualWidth-nav-tools-10>=summaryWidth+740;}
        void MoveSummaryHost(bool compact)
        {
            var parent=VisualTreeHelper.GetParent(inspector) as Panel;var target=compact?conversationSurface:workspaceShell;if(ReferenceEquals(parent,target))return;if(parent!=null)parent.Children.Remove(inspector);Grid.SetColumn(inspector,compact?0:4);Grid.SetColumnSpan(inspector,1);Grid.SetRow(inspector,compact?1:0);inspector.Width=compact?summaryWidth:Double.NaN;inspector.HorizontalAlignment=compact?HorizontalAlignment.Right:HorizontalAlignment.Stretch;inspector.Margin=compact?new Thickness(0,0,0,6):new Thickness(0,48,12,6);target.Children.Add(inspector);
        }
        void ToggleSummary()
        {
            if(inspectorLayout){summaryPinned=false;summaryManuallyShown=false;CloseTransientSummary();RefreshWorkspaceLayout();return;}
            if(SummaryHasRoom()){summaryPinned=true;summaryManuallyShown=true;if(unifiedLayout&&filePreview!=null)filePreview.SelectConversation();CloseTransientSummary();RefreshWorkspaceLayout();}
            else if(transientSummary!=null&&transientSummary.IsOpen)CloseTransientSummary();else ShowTransientSummary();
        }
        void ShowTransientSummary(){CloseTransientSummary();var view=new SummaryPreview(CreateSummaryPanel(),session.CurrentAgentTask==null,()=>OpenSummaryTab());var popup=new Popup{PlacementTarget=summaryToggle,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,Child=view};transientSummaryView=view;transientSummary=popup;view.Width=Math.Min(350,Math.Max(280,ActualWidth-40));view.MaxHeight=Math.Max(220,Math.Min(650,ActualHeight-100));popup.Closed+=delegate{view.Dispose();if(ReferenceEquals(transientSummary,popup)){transientSummaryView=null;transientSummary=null;}};view.LostKeyboardFocus+=delegate{Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(delegate{if(ReferenceEquals(view,transientSummaryView)&&!view.IsKeyboardFocusWithin)CloseTransientSummary();}));};popup.HorizontalOffset=summaryToggle.ActualWidth-view.Width;popup.IsOpen=true;view.Focus();}
        void CloseTransientSummary(){if(transientSummary!=null)transientSummary.IsOpen=false;}
        AgentTaskPanel CreateSummaryPanel(){var panel=new AgentTaskPanel(pet,session,queue);panel.SetTaskWorkspace(TaskSettings().AgentWorkspace);panel.SetWorkspaceLayout(true);panel.IsExpanded=true;WireSummaryNavigation(panel);return panel;}
        void WireSummaryNavigation(AgentTaskPanel panel){panel.OpenFileRequested=path=>{CloseTransientSummary();OpenFilePreview(path);};panel.OpenDetailRequested=(title,view)=>{CloseTransientSummary();OpenDetailTab(title,view);};}
        void OpenSummaryTab(){CloseTransientSummary();OpenDetailTab(()=>TextResources.Get("chatWorkspaceLayout.openSummaryTab.openDetailTab"),new SummaryPreview(CreateSummaryPanel(),session.CurrentAgentTask==null,null));}
        void OpenDetailTab(string title,FrameworkElement view){if(closed){var owned=view as IDisposable;if(owned!=null)owned.Dispose();return;}EnsurePreviewWorkspace();filePreview.AddPage(title,"file",view);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
        void OpenDetailTab(Func<string> title,FrameworkElement view){if(closed){var owned=view as IDisposable;if(owned!=null)owned.Dispose();return;}EnsurePreviewWorkspace();filePreview.AddPage(title,"file",view);PlaceFilePreview(ActualWidth>=1200);RefreshWorkspaceLayout();}
        void RefreshConversationTitle()
        {
            Func<string> title=()=>{string value=workspace==null?null:workspace.Definition.Name;if(String.IsNullOrWhiteSpace(value)){var first=session.History.Messages.FirstOrDefault(m=>m.Role=="user");value=first==null?TextResources.Get("chatWorkspaceLayout.refreshConversationTitle.nullMarkdownContentPlainText"):MarkdownContent.PlainText(first.Content??"").Replace("\r"," ").Replace("\n"," ").Trim();}return String.IsNullOrWhiteSpace(value)?TextResources.Get("chatWorkspaceLayout.refreshConversationTitle.title"):value;};LocalizedUi.Set(conversationTitle,"Text",title);LocalizedUi.Tooltip(conversationTitle,title);if(filePreview!=null)filePreview.UpdateConversationTitle(title);
        }
    }
    internal sealed class SummaryPreview:Border,IDisposable
    {
        readonly AgentTaskPanel panel;
        internal SummaryPreview(AgentTaskPanel panel,bool empty,Action open){this.panel=panel;Name="TransientSummary";Margin=new Thickness(8);Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=20,ShadowDepth=3,Opacity=.10,Color=Colors.Black};Background=ThemeManager.Surface;BorderBrush=Ui.Line;BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(14);Padding=new Thickness(5);Focusable=true;var body=new StackPanel();var header=new DockPanel{Margin=new Thickness(12,8,8,8)};if(open!=null){var tab=UiIcons.Button("expand",()=>(TextResources.Get("chatWorkspaceLayout.summaryPreview.uiIcons")));tab.Name="OpenSummaryTab";tab.Click+=delegate{open();};DockPanel.SetDock(tab,Dock.Right);header.Children.Add(tab);}header.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("chatWorkspaceLayout.summaryPreview.text")),14,AgentUi.Ink));body.Children.Add(header);if(empty)body.Children.Add(LocalizedUi.With(new TextBlock{Text=TextResources.Get("chatWorkspaceLayout.summaryPreview.text.alternativeA"),Margin=new Thickness(12),Foreground=AgentUi.Muted},"Text",()=>(TextResources.Get("chatWorkspaceLayout.summaryPreview.text.alternativeA"))));body.Children.Add(panel);Child=Ui.Scroll(body);}
        public void Dispose(){panel.Dispose();}
    }
}
