using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed partial class ChatWindow : Window
    {
        private readonly PetWindow pet;
        private ConversationSession session;private ConversationQueue queue;private ConversationWorkspace workspace;
        private readonly StackPanel messages;
        private readonly ScrollViewer scroll;
        private readonly ChatAtmosphere atmosphere;
        private readonly MarkdownEditor input;
        public string ConversationId {get{return workspace==null?null:workspace.Id;}}
        internal bool IsPersonalConversation {get{return workspace!=null&&!workspace.EffectiveSettings().AgentEnabled;}}
        public bool HasDraft { get { return !String.IsNullOrWhiteSpace(input.Text) || (composer!=null && composer.HasEdit); } }
        private readonly TextBlock status;
        private readonly Image avatar;
        private readonly Button record;readonly Button chatSettings;
        private ConversationComposer composer;
        private bool closed,showingWelcome,reloading;
        private int visibleTurns=50;
        private readonly Button earlier,export;
        private readonly Dictionary<string,ChatMessageView> rendered=new Dictionary<string,ChatMessageView>();
        private readonly Dictionary<string,ChatMessage> displayed=new Dictionary<string,ChatMessage>();
        private MessageUpdatePump messagePump;AgentQuestionSurface questionSurface;
        internal bool CanAnswerQuestion(AgentQuestionRequest request){return AgentQuestionSurface.Matches(session,request);}
        private AgentTaskPanel taskPanel;readonly TextBlock contextLabel=Ui.Text("",12,AgentUi.Muted);readonly StackPanel contextNotice=new StackPanel{Name="ContextCompactionNotice",Orientation=Orientation.Horizontal,Margin=new Thickness(2,5,2,18)};
        readonly DockPanel conversationHeader,conversationIdentity,historyTools;readonly Grid workspaceShell,messageArea,conversationSurface;readonly ContentControl inspectorHost;readonly Border inspector;AgentWorkspaceNavigation navigation;
        bool workspaceLayout,inspectorLayout,compactTaskExpanded;readonly double normalMaxHeight;
        public ChatWindow(PetWindow companion,ConversationWorkspace workspaceOverride=null)
        {
            pet=companion;workspace=workspaceOverride;session=workspaceOverride==null?pet.Conversation:workspaceOverride.Session;queue=workspaceOverride==null?pet.InputQueue:workspaceOverride.Queue;LocalizedUi.Window(this,()=>(TextResources.Get("chatWindow.chatWindow.window")),1280,860);MinWidth=520;MinHeight=560;Width=Math.Min(1280,Math.Max(MinWidth,SystemParameters.WorkArea.Width-36));
            Background=ThemeManager.Surface;
            normalMaxHeight=MaxHeight;
            var root=new Grid {Background=ThemeManager.Surface,Margin=new Thickness(22,18,22,6)};
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var header=new DockPanel {Margin=new Thickness(0,0,78,16)};conversationHeader=header;entryHeader.Content=new ConversationEntryTabs(SwitchEntry,true);DockPanel.SetDock(entryHeader,Dock.Left);header.Children.Add(entryHeader);
            var settings=UiIcons.Button("settings",TextResources.Get("chatWindow.chatWindow.uiIcons"));chatSettings=settings;settings.Name="ChatSettings";settings.Margin=new Thickness(10,0,0,0);settings.VerticalAlignment=VerticalAlignment.Center;
            settings.Click+=delegate {pet.ShowSettings(true);};DockPanel.SetDock(settings,Dock.Right);
            avatar=new Image {Name="ChatAvatar",Width=34,Height=34,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,34,34),6.8,6.8),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};RenderOptions.SetBitmapScalingMode(avatar,BitmapScalingMode.HighQuality);DockPanel.SetDock(avatar,Dock.Left);header.Children.Add(avatar);
            var identity=new DockPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,78,0)};conversationIdentity=identity;var speech=UiIcons.Speech(pet);speech.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(speech,Dock.Left);identity.Children.Add(speech);conversationTitle.Name="ConversationTitle";conversationTitle.FontWeight=FontWeights.SemiBold;conversationTitle.TextWrapping=TextWrapping.NoWrap;conversationTitle.TextTrimming=TextTrimming.CharacterEllipsis;conversationTitle.VerticalAlignment=VerticalAlignment.Center;identity.Children.Add(conversationTitle);header.Children.Add(identity);root.Children.Add(header);
            messages=new StackPanel {Margin=new Thickness(2,8,2,12)};scroll=Ui.Scroll(messages);scroll.Name="ChatHistoryScroll";scroll.Margin=new Thickness(0,0,-18,0);
            conversationSurface=root;messageArea=new Grid();messageArea.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});messageArea.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});messageArea.RowDefinitions.Add(new RowDefinition());Grid.SetRow(compactTaskHost,1);messageArea.Children.Add(compactTaskHost);
            historyTools=new DockPanel{LastChildFill=false};
            earlier=LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeI")));earlier.Name="HistoryEarlier";earlier.FontSize=11;earlier.Click+=delegate{double offset=scroll.VerticalOffset,extent=scroll.ExtentHeight;visibleTurns+=50;ReloadHistory(false);Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate{scroll.UpdateLayout();scroll.ScrollToVerticalOffset(offset+Math.Max(0,scroll.ExtentHeight-extent));}));};historyTools.Children.Add(earlier);
            export=LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeJ")));export.Name="ExportChat";export.FontSize=11;export.Click+=delegate{Export();};DockPanel.SetDock(export,Dock.Right);scroll.Content=null;var historyBody=new StackPanel();historyBody.Children.Add(historyTools);historyBody.Children.Add(messages);scroll.Content=historyBody;
            var contextIcon=UiIcons.Image("file",AgentUi.Muted);contextIcon.Width=contextIcon.Height=15;contextIcon.Margin=new Thickness(0,0,7,0);contextNotice.Children.Add(contextIcon);contextLabel.Name="ContextCompactionLabel";contextNotice.Children.Add(contextLabel);
            Grid.SetRow(scroll,2);messageArea.Children.Add(scroll);scroll.Background=Brushes.Transparent;atmosphere=ChatAtmosphere.AttachSurface(root,1,2);atmosphere.ArtworkOpacity=.20;atmosphere.SetSkin(pet.Skin);Grid.SetRow(messageArea,1);root.Children.Add(messageArea);InitializeHistoryNavigation();
            status=Ui.Text("",11,Ui.Muted);status.Name="ChatStatus";
            input=new MarkdownEditor();input.Name="MessageInput";input.MaxLength=8000;
            record=UiIcons.Button("mic",TextResources.Get("chatWindow.chatWindow.uiIcons.alternativeA"));record.Name="VoiceRecord";

            workspaceShell=new Grid();for(int column=0;column<7;column++)workspaceShell.ColumnDefinitions.Add(new ColumnDefinition{Width=column==2?new GridLength(1,GridUnitType.Star):new GridLength(0)});
            Grid.SetColumn(root,2);Grid.SetColumnSpan(root,3);workspaceShell.Children.Add(root);

            inspectorHost=new ContentControl{VerticalContentAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            var inspectorBody=new Grid();inspectorBody.MaxHeight=720;inspectorBody.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});inspectorBody.RowDefinitions.Add(new RowDefinition());var inspectorTitle=LocalizedUi.Text(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeK")),14,AgentUi.Ink);inspectorTitle.FontWeight=FontWeights.SemiBold;inspectorTitle.Margin=new Thickness(16,18,16,14);inspectorBody.Children.Add(inspectorTitle);Grid.SetRow(inspectorHost,1);inspectorBody.Children.Add(inspectorHost);
            summaryEmpty.Margin=new Thickness(16,0,16,18);Grid.SetRow(summaryEmpty,1);inspectorBody.Children.Add(summaryEmpty);inspector=new Border{Name="AgentWorkspaceInspector",Child=inspectorBody,Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=20,ShadowDepth=3,Opacity=.08,Color=Colors.Black},BorderBrush=Ui.Brush("#E6E7EA"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Margin=new Thickness(0,48,12,6),VerticalAlignment=VerticalAlignment.Top,Background=ThemeManager.Surface,Visibility=Visibility.Collapsed};Grid.SetColumn(inspector,4);Panel.SetZIndex(inspector,2);workspaceShell.Children.Add(inspector);Grid.SetColumn(filePreviewDock,6);workspaceShell.Children.Add(filePreviewDock);Content=workspaceShell;InitializeWorkspaceTools();
            StateChanged+=delegate{RefreshWorkspaceLayout();};SizeChanged+=delegate{RefreshWorkspaceLayout();};workspaceShell.SizeChanged+=delegate{RefreshWorkspaceLayout();};
            MountWorkspace();InitializeAppShell();questionSurface=new AgentQuestionSurface(pet.QuestionController,r=>AgentQuestionSurface.Matches(session,r));Grid.SetRow(questionSurface,0);messageArea.Children.Add(questionSurface);Closed+=delegate{questionSurface.Dispose();pet.CancelQuestionsWithoutHost(this);};
            Closed+=delegate {closed=true;CloseTransientSummary();CloseFilePreview();UnmountWorkspace(true);DetachEntryControls();};
            Loaded+=delegate {input.Focus();};
        }
        void RefreshWorkspaceLayout()
        {
            if(workspaceShell==null || closed || taskPanel==null || shellReady&&shellPage!="home")return;
            if(filePreview!=null&&filePreview.ToolCount==0&&filePreview.HasConversation&&!fullTabView&&ActualWidth>=1200){CloseFilePreview();return;}
            // Maximised must fill the whole work area. Capping MaxHeight below it
            // (and capping the shell to the same value) leaves a dead strip at the
            // bottom of the window, so only constrain size in the normal state.
            bool maximized=WindowState==WindowState.Maximized;double maxHeight=maximized?Double.PositiveInfinity:normalMaxHeight;
            MaxHeight=maxHeight;workspaceShell.MaxHeight=maxHeight;workspaceShell.ClipToBounds=!maximized;conversationSurface.ClipToBounds=!maximized;
            bool wide=workspaceShell.ActualWidth>=900&&!sidebarHidden;bool previewDocked=filePreview!=null&&!tabsHidden&&(ActualWidth>=1200||previewVisible);SyncConversationTab(previewDocked&&(fullTabView||ActualWidth<1200));entryHeader.Visibility=wide?Visibility.Collapsed:Visibility.Visible;chatSettings.Visibility=Visibility.Collapsed;bool showInspector=LayoutWorkspaceColumns(wide,previewDocked);LayoutConversationViewport(showInspector);PlaceFilePreview(previewDocked);
            if(workspaceLayout!=wide){if(wide)compactTaskExpanded=taskPanel.IsExpanded;workspaceLayout=wide;navigation.Visibility=wide?Visibility.Visible:Visibility.Collapsed;}LayoutAppSidebar(wide);
            if(inspectorLayout==showInspector)return;bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;double offset=scroll.VerticalOffset;inspectorLayout=showInspector;
            if(showInspector){compactTaskHost.Content=null;inspectorHost.Content=taskPanel;taskPanel.SetWorkspaceLayout(true);taskPanel.IsExpanded=true;inspector.Visibility=Visibility.Visible;}
            else{inspectorHost.Content=null;taskPanel.SetWorkspaceLayout(false);compactTaskHost.Content=taskPanel;taskPanel.IsExpanded=compactTaskExpanded;inspector.Visibility=Visibility.Collapsed;}
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate{if(closed)return;if(follow){if(Math.Abs(scroll.VerticalOffset-offset)<1||scroll.ScrollableHeight-scroll.VerticalOffset<32)scroll.ScrollToEnd();}else scroll.ScrollToVerticalOffset(offset);}));
        }
        public void SwitchWorkspace(ConversationWorkspace replacement)
        {
            if(closed)return;if(ReferenceEquals(workspace,replacement)){if(filePreview!=null&&filePreview.HasConversation)filePreview.SelectConversation();return;}CloseTransientSummary();RememberEntry();UnmountWorkspace(false);
            workspace=replacement;session=replacement==null?pet.Conversation:replacement.Session;queue=replacement==null?pet.InputQueue:replacement.Queue;MountWorkspace();if(filePreview!=null&&filePreview.HasConversation)filePreview.SelectConversation();if(shellReady){ShowHomePage(false);RecordPage("home");}
        }
        void MountWorkspace()
        {
            RememberEntry();
            taskPanel=new AgentTaskPanel(pet,session,queue);WireSummaryNavigation(taskPanel);taskPanel.SetTaskWorkspace(workspace==null?null:workspace.ProjectWorkspace);if(inspectorLayout){inspectorHost.Content=taskPanel;taskPanel.SetWorkspaceLayout(true);taskPanel.IsExpanded=true;}else{compactTaskHost.Content=taskPanel;}
            if(composer==null){composer=new ConversationComposer(pet,this,input,record,status,false,false,session,queue);composer.Margin=new Thickness(0,12,0,0);Grid.SetRow(composer,2);conversationSurface.Children.Add(composer);}else composer.Rebind(session,queue);
            composer.PrepareSubmission=delegate{return pet.PrepareConversationSubmission(workspace);};composer.Submitted=delegate{pet.FinishConversationSubmission(workspace);RefreshProjectPicker();};RefreshProjectPicker();
            navigation=new AgentWorkspaceNavigation(pet,delegate{if(!inspectorLayout&&(transientSummary==null||!transientSummary.IsOpen))ToggleSummary();taskPanel.IsExpanded=true;if(inspectorLayout)taskPanel.Focus();else if(transientSummaryView!=null)transientSummaryView.Focus();},session,workspace==null?null:workspace.Id,SwitchEntry);navigation.Visibility=workspaceLayout?Visibility.Visible:Visibility.Collapsed;Grid.SetColumn(navigation,0);workspaceShell.Children.Add(navigation);
            messagePump=new MessageUpdatePump(session,UpdateMessage);session.AgentTaskChanged+=RefreshSummary;session.MessageAdded+=AddMessage;session.HistoryChanged+=HistoryChanged;session.StateChanged+=RefreshState;pet.Live.StateChanged+=RefreshState;
            ReloadHistory();RefreshAppearance();RefreshState();RefreshEntryControls();
        }
        // Task records live under the project workspace, not the global setting.
        PetSettings TaskSettings()
        {
            var cfg=pet.ConversationSettings(session);
            if(workspace!=null && !String.IsNullOrEmpty(workspace.ProjectWorkspace))try{cfg.AgentWorkspace=System.IO.Path.GetFullPath(workspace.ProjectWorkspace);}catch(ArgumentException){}
            return cfg;
        }
        void UnmountWorkspace(bool disposeComposer)
        {
            if(welcomeSuggestions!=null){welcomeSuggestions.Dispose();welcomeSuggestions=null;}
            if(messagePump!=null){messagePump.Dispose();messagePump=null;}if(session!=null){session.AgentTaskChanged-=RefreshSummary;session.MessageAdded-=AddMessage;session.HistoryChanged-=HistoryChanged;session.StateChanged-=RefreshState;}pet.Live.StateChanged-=RefreshState;
            if(disposeComposer && composer!=null){conversationSurface.Children.Remove(composer);composer.Detach();composer=null;}
            if(taskPanel!=null){if(ReferenceEquals(inspectorHost.Content,taskPanel))inspectorHost.Content=null;else compactTaskHost.Content=null;taskPanel.Dispose();taskPanel=null;}
            if(navigation!=null){navigation.Dispose();workspaceShell.Children.Remove(navigation);navigation=null;}
        }
        public void RefreshAppearance()
        {if(questionSurface!=null)questionSurface.Refresh();RefreshShellAppearance();if(showingWelcome)ReloadHistory(false);if(!(Content is LiveCallView))LocalizedUi.Set(this,"Title",()=>(TextResources.Format("chatWindow.refreshAppearance.petSkinName",pet.Skin.DisplayName)));avatar.Source=SkinCatalog.GetAvatar(pet.Skin);LocalizedUi.Set(avatar,"ToolTip",()=>(pet.Skin.DisplayName));atmosphere.SetSkin(pet.Skin);RefreshConversationTitle();if(composer!=null)composer.RefreshModels();RefreshProjectPicker();if(taskPanel!=null){taskPanel.SetTaskWorkspace(workspace==null?null:workspace.ProjectWorkspace);if(!session.IsBusy)session.LoadAgentTask(TaskSettings());}if(navigation!=null)navigation.Refresh();}
        public void ReloadHistory(bool scrollToBottom=true)
        {
            reloading=true;if(welcomeSuggestions!=null){welcomeSuggestions.Dispose();welcomeSuggestions=null;}messages.Children.Clear();rendered.Clear();displayed.Clear();showingWelcome=false;
            var turns=session.History.Messages.GroupBy(m=>m.TurnId).ToList();
            foreach(var turn in turns.Skip(Math.Max(0,turns.Count-visibleTurns)))foreach(var message in turn)AddMessage(message);
            earlier.IsEnabled=turns.Count>visibleTurns;earlier.Visibility=earlier.IsEnabled?Visibility.Visible:Visibility.Collapsed;historyTools.Visibility=earlier.IsEnabled?Visibility.Visible:Visibility.Collapsed;export.IsEnabled=turns.Count>0;
            if(messages.Children.Count==0)Welcome();reloading=false;RefreshSummary();if(scrollToBottom)QueueHistoryFollow();
        }
        private void HistoryChanged(){if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(HistoryChanged));return;}visibleTurns=50;RefreshConversationTitle();ReloadHistory();}
        private void Export()
        {
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter=TextResources.Get("chatWindow.export.filter"),FileName="DeskPet-chat-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".md"};
            if(dialog.ShowDialog(this)!=true)return;
            try{File.WriteAllText(dialog.FileName,session.ExportHistory(),new System.Text.UTF8Encoding(false));ShowStatus(()=>(TextResources.Get("chatWindow.export.showStatus")));}
            catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException))throw;ShowStatus(()=>(TextResources.Get("chatWindow.export.showStatus.alternativeA")));}
        }
        private void UpdateMessage(ChatMessage message)
        {
            if(closed || !rendered.ContainsKey(message.Id))return;
            bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;
            displayed[message.Id]=message;rendered[message.Id].Update(message);RefreshSummary();
            if(follow)QueueHistoryFollow();
        }
        WelcomeSuggestions welcomeSuggestions;
        private void Welcome()
        {
            showingWelcome=true;
            var panel=new StackPanel{Margin=new Thickness(26,28,26,26)};
            var character=new Image{Name="WelcomeCharacter",Source=SkinCatalog.GetAvatar(pet.Skin),Width=154,Height=154,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,154,154),30.8,30.8),HorizontalAlignment=HorizontalAlignment.Left};
            RenderOptions.SetBitmapScalingMode(character,BitmapScalingMode.HighQuality);panel.Children.Add(character);
            var heading=LocalizedUi.Text(()=>(TextResources.Get("chatWindow.welcome.text")),24);heading.Name="WelcomeHeading";heading.FontWeight=FontWeights.SemiBold;panel.Children.Add(heading);
            bool work=pet.ConversationSettings(session).AgentEnabled;
            panel.Children.Add(LocalizedUi.Text(()=>(work?TextResources.Get("chatWindow.welcome.text.alternativeA"):TextResources.Get("chatWindow.welcome.text.alternativeB")),13,Ui.Muted));
            welcomeSuggestions=new WelcomeSuggestions(pet,this,session,workspace,input);panel.Children.Add(welcomeSuggestions);messages.Children.Add(panel);
        }

        private void AddMessage(ChatMessage message)
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(delegate {AddMessage(message);}));return;}
            if(!session.History.Messages.Any(m=>m.Id==message.Id))return;if(rendered.ContainsKey(message.Id)){UpdateMessage(message);return;}bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;
            if(showingWelcome){messages.Children.Clear();showingWelcome=false;}bool user=message.Role=="user";if(!reloading && !follow && user)visibleTurns=Math.Min(200,visibleTurns+1);displayed[message.Id]=message;
            var view=new ChatMessageView(message,messages,pet,this,session,queue);rendered[message.Id]=view;messages.Children.Add(view);RefreshSummary();
            while(messages.Children.Count>visibleTurns*2){var old=messages.Children[0] as Border;if(old!=null){string id=old.Tag as string;if(id!=null){rendered.Remove(id);displayed.Remove(id);}}messages.Children.RemoveAt(0);}
            if(!reloading){earlier.IsEnabled=session.History.Messages.Select(m=>m.TurnId).Distinct().Count()>visibleTurns;export.IsEnabled=true;if(follow)QueueHistoryFollow();}
        }
        void RefreshSummary()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshSummary));return;}summaryEmpty.Visibility=session.CurrentAgentTask==null?Visibility.Visible:Visibility.Collapsed;
            messages.Children.Remove(contextNotice);var task=session.CurrentAgentTask;var reply=task==null?null:session.History.Messages.LastOrDefault(m=>m.Role=="assistant" && m.AgentTaskId==task.Id);
            RefreshChangeSummaries(task);ChatMessageView view;if(reply!=null && rendered.TryGetValue(reply.Id,out view))view.RefreshOutputFiles();
            if(reply!=null && (task.ContextCompactions>0 || task.ContextStatus=="compacting" || task.ContextStatus=="limited" || task.ContextStatus=="cancelled") && rendered.TryGetValue(reply.Id,out view)){int index=messages.Children.IndexOf(view);if(index>=0){LocalizedUi.Set(contextLabel,"Text",()=>(task.ContextStatus=="compacting"?TextResources.Get("chatWindow.refreshSummary.contextStatusTaskContextStatus"):task.ContextStatus=="limited"?TextResources.Get("chatWindow.refreshSummary.contextStatusTaskContextStatus.alternativeA"):task.ContextStatus=="cancelled"?TextResources.Get("chatWindow.refreshSummary.contextStatusTaskContextStatus.alternativeB"):TextResources.Get("chatWindow.refreshSummary.contextStatusTaskContextStatus.alternativeC")));LocalizedUi.Set(contextNotice,"ToolTip",()=>(TextResources.Format("chatWindow.refreshSummary.toolTipTaskContextCompactions",task.ContextCompactions)));messages.Children.Insert(index,contextNotice);}}
        }
        private void RefreshState()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshState));return;}
            RefreshAppearance();if(pet.DictationActive)return;
            if(pet.LiveActive){ShowStatus(pet.Live.Status);return;}
            ShowStatus(()=>(session.Stage==ConversationStage.Recognizing?TextResources.Get("chatWindow.refreshState.showStatus"):session.Stage==ConversationStage.Thinking?TextResources.Get("chatWindow.refreshState.showStatus.alternativeA"):session.Stage==ConversationStage.Speaking?TextResources.Get("chatWindow.refreshState.showStatus.alternativeB"):String.IsNullOrEmpty(session.AgentTaskWarning)?String.IsNullOrEmpty(session.HistoryWarning)?(String.IsNullOrEmpty(session.LastNotice)?ChatUi.SendHint(pet.Settings):session.LastNotice):session.HistoryWarning:session.AgentTaskWarning));
        }
        private void ShowStatus(Func<string> text){if(!closed)LocalizedUi.Set(status,"Text",text);}
        private void ShowStatus(string text){if(!closed)status.Text=text;}
        private void ShowError(Exception error)
        {if(error is OperationCanceledException)ShowStatus(()=>(TextResources.Get("chatWindow.showError.showStatus")));else if(error is ApiException || error is InvalidOperationException)ShowStatus(error.Message);else ShowStatus(()=>(TextResources.Get("chatWindow.showError.showStatus.alternativeA")));}
    }
}
