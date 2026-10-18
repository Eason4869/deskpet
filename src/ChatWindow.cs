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
        private readonly MarkdownEditor input;
        public string ConversationId {get{return workspace==null?null:workspace.Id;}}
        internal bool IsPersonalConversation {get{return workspace!=null&&!workspace.EffectiveSettings().AgentEnabled;}}
        public bool HasDraft { get { return !String.IsNullOrWhiteSpace(input.Text) || (composer!=null && composer.HasEdit); } }
        private readonly TextBlock status;
        private readonly Image avatar;
        private readonly Button record;readonly Button chatSettings;
        private ConversationComposer composer;private readonly Dictionary<string,string> drafts=new Dictionary<string,string>();
        private bool closed,showingWelcome,reloading;
        private int visibleTurns=50;
        private readonly Button earlier,export;
        private readonly Dictionary<string,ChatMessageView> rendered=new Dictionary<string,ChatMessageView>();
        private readonly Dictionary<string,ChatMessage> displayed=new Dictionary<string,ChatMessage>();
        private MessageUpdatePump messagePump;
        private AgentTaskPanel taskPanel;readonly AgentChangeSummaryCard changeSummary;readonly TextBlock contextLabel=Ui.Text("",12,AgentUi.Muted);readonly StackPanel contextNotice=new StackPanel{Name="ContextCompactionNotice",Orientation=Orientation.Horizontal,Margin=new Thickness(2,5,2,18)};
        readonly Grid workspaceShell,messageArea,conversationSurface;readonly ContentControl inspectorHost;readonly Border inspector;AgentWorkspaceNavigation navigation;
        bool workspaceLayout,inspectorLayout,compactTaskExpanded;readonly double normalMaxHeight;
        public ChatWindow(PetWindow companion,ConversationWorkspace workspaceOverride=null)
        {
            pet=companion;workspace=workspaceOverride;session=workspaceOverride==null?pet.Conversation:workspaceOverride.Session;queue=workspaceOverride==null?pet.InputQueue:workspaceOverride.Queue;Ui.Window(this,"桌面伙伴 · 对话",700,860);MinWidth=520;MinHeight=560;
            Background=Brushes.White;
            normalMaxHeight=MaxHeight;
            var root=new Grid {Margin=new Thickness(22,18,22,32)};
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var header=new DockPanel {Margin=new Thickness(0,0,0,16)};
            var settings=UiIcons.Button("settings","模型配置");chatSettings=settings;settings.Name="ChatSettings";settings.Margin=new Thickness(10,0,0,0);settings.VerticalAlignment=VerticalAlignment.Center;
            settings.Click+=delegate {pet.ShowSettings(true);};DockPanel.SetDock(settings,Dock.Right);header.Children.Add(settings);
            avatar=new Image {Name="ChatAvatar",Width=34,Height=34,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,34,34),6.8,6.8),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};RenderOptions.SetBitmapScalingMode(avatar,BitmapScalingMode.HighQuality);DockPanel.SetDock(avatar,Dock.Left);header.Children.Add(avatar);
            var identity=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};identity.Children.Add(UiIcons.Speech(pet));header.Children.Add(identity);root.Children.Add(header);
            messages=new StackPanel {Margin=new Thickness(2,8,2,12)};scroll=Ui.Scroll(messages);scroll.Name="ChatHistoryScroll";
            conversationSurface=root;messageArea=new Grid();messageArea.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});messageArea.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});messageArea.RowDefinitions.Add(new RowDefinition());
            var historyTools=new DockPanel{LastChildFill=false};
            earlier=Ui.Button("加载更早记录");earlier.Name="HistoryEarlier";earlier.FontSize=11;earlier.Click+=delegate{double offset=scroll.VerticalOffset,extent=scroll.ExtentHeight;visibleTurns+=50;ReloadHistory(false);Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate{scroll.UpdateLayout();scroll.ScrollToVerticalOffset(offset+Math.Max(0,scroll.ExtentHeight-extent));}));};historyTools.Children.Add(earlier);
            export=Ui.Button("导出记录");export.Name="ExportChat";export.FontSize=11;export.Click+=delegate{Export();};DockPanel.SetDock(export,Dock.Right);historyTools.Children.Add(export);messageArea.Children.Add(historyTools);
            changeSummary=new AgentChangeSummaryCard(path=>{var ignored=taskPanel.ShowDiff(path);});var contextIcon=UiIcons.Image("file",AgentUi.Muted);contextIcon.Width=contextIcon.Height=15;contextIcon.Margin=new Thickness(0,0,7,0);contextNotice.Children.Add(contextIcon);contextLabel.Name="ContextCompactionLabel";contextNotice.Children.Add(contextLabel);
            Grid.SetRow(scroll,2);messageArea.Children.Add(scroll);Grid.SetRow(messageArea,1);root.Children.Add(messageArea);
            status=Ui.Text("",11,Ui.Muted);status.Name="ChatStatus";
            input=new MarkdownEditor();input.Name="MessageInput";input.MaxLength=8000;
            record=UiIcons.Button("mic","听写");record.Name="VoiceRecord";

            workspaceShell=new Grid();workspaceShell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});workspaceShell.ColumnDefinitions.Add(new ColumnDefinition());workspaceShell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});
            Grid.SetColumn(root,1);workspaceShell.Children.Add(root);

            inspectorHost=new ContentControl{VerticalContentAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            var inspectorBody=new Grid();inspectorBody.MaxHeight=720;inspectorBody.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});inspectorBody.RowDefinitions.Add(new RowDefinition());var inspectorTitle=Ui.Text("工作空间",14,AgentUi.Ink);inspectorTitle.FontWeight=FontWeights.SemiBold;inspectorTitle.Margin=new Thickness(16,18,16,14);inspectorBody.Children.Add(inspectorTitle);Grid.SetRow(inspectorHost,1);inspectorBody.Children.Add(inspectorHost);
            inspector=new Border{Name="AgentWorkspaceInspector",Child=inspectorBody,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Margin=new Thickness(0,22,18,22),VerticalAlignment=VerticalAlignment.Top,Background=Brushes.White,Visibility=Visibility.Collapsed};Grid.SetColumn(inspector,2);workspaceShell.Children.Add(inspector);Content=workspaceShell;
            StateChanged+=delegate{RefreshWorkspaceLayout();};SizeChanged+=delegate{RefreshWorkspaceLayout();};
            MountWorkspace();
            Closed+=delegate {closed=true;UnmountWorkspace(true);};
            Loaded+=delegate {input.Focus();};
        }
        void RefreshWorkspaceLayout()
        {
            if(workspaceShell==null || closed || taskPanel==null)return;
            // Maximised must fill the whole work area. Capping MaxHeight below it
            // (and capping the shell to the same value) leaves a dead strip at the
            // bottom of the window, so only constrain size in the normal state.
            bool maximized=WindowState==WindowState.Maximized;double maxHeight=maximized?Double.PositiveInfinity:normalMaxHeight;
            MaxHeight=maxHeight;workspaceShell.MaxHeight=maxHeight;workspaceShell.ClipToBounds=!maximized;conversationSurface.ClipToBounds=!maximized;
            bool wide=ActualWidth>=960;chatSettings.Visibility=wide?Visibility.Collapsed:Visibility.Visible;bool showInspector=wide && ActualWidth>=1280;
            if(workspaceLayout!=wide){if(wide)compactTaskExpanded=taskPanel.IsExpanded;workspaceLayout=wide;conversationSurface.MaxWidth=wide?920:Double.PositiveInfinity;navigation.Visibility=wide?Visibility.Visible:Visibility.Collapsed;}
            workspaceShell.ColumnDefinitions[0].Width=new GridLength(wide?Math.Min(252,Math.Max(208,ActualWidth*.17)):0);workspaceShell.ColumnDefinitions[2].Width=new GridLength(showInspector?Math.Min(340,Math.Max(300,ActualWidth*.23)):0);
            if(inspectorLayout==showInspector)return;bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;double offset=scroll.VerticalOffset;inspectorLayout=showInspector;
            if(showInspector){messageArea.Children.Remove(taskPanel);inspectorHost.Content=taskPanel;taskPanel.SetWorkspaceLayout(true);taskPanel.IsExpanded=true;inspector.Visibility=Visibility.Visible;}
            else{inspectorHost.Content=null;taskPanel.SetWorkspaceLayout(false);Grid.SetRow(taskPanel,1);messageArea.Children.Add(taskPanel);taskPanel.IsExpanded=compactTaskExpanded;inspector.Visibility=Visibility.Collapsed;}
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate{if(closed)return;if(follow)scroll.ScrollToEnd();else scroll.ScrollToVerticalOffset(offset);}));
        }
        string DraftKey(){return workspace==null?"":workspace.Id;}
        internal void ChangeConversationMode(bool work)
        {
            if(pet.ConversationSettings(session).AgentEnabled==work){RefreshProjectPicker();return;}
            string text=input.Text;int caret=input.CaretIndex;
            SwitchWorkspace(pet.CreateDraftConversation(null,work));
            input.Text=text;input.CaretIndex=Math.Min(caret,text.Length);input.Focus();
        }
        public void SwitchWorkspace(ConversationWorkspace replacement)
        {
            if(closed)return;if(ReferenceEquals(workspace,replacement))return;drafts[DraftKey()]=input.Text;UnmountWorkspace(false);
            workspace=replacement;session=replacement==null?pet.Conversation:replacement.Session;queue=replacement==null?pet.InputQueue:replacement.Queue;MountWorkspace();
        }
        void MountWorkspace()
        {
            taskPanel=new AgentTaskPanel(pet,session,queue);taskPanel.SetTaskWorkspace(workspace==null?null:workspace.ProjectWorkspace);if(inspectorLayout){inspectorHost.Content=taskPanel;taskPanel.SetWorkspaceLayout(true);taskPanel.IsExpanded=true;}else{Grid.SetRow(taskPanel,1);messageArea.Children.Add(taskPanel);}
            if(composer==null){composer=new ConversationComposer(pet,this,input,record,status,false,false,session,queue);composer.Margin=new Thickness(0,12,0,0);Grid.SetRow(composer,2);conversationSurface.Children.Add(composer);}else composer.Rebind(session,queue);
            composer.PrepareSubmission=delegate{return pet.PrepareConversationSubmission(workspace);};composer.Submitted=delegate{pet.FinishConversationSubmission(workspace);RefreshProjectPicker();};RefreshProjectPicker();
            navigation=new AgentWorkspaceNavigation(pet,delegate{taskPanel.IsExpanded=true;taskPanel.Focus();},session,workspace==null?null:workspace.Id);navigation.Visibility=workspaceLayout?Visibility.Visible:Visibility.Collapsed;Grid.SetColumn(navigation,0);workspaceShell.Children.Add(navigation);
            messagePump=new MessageUpdatePump(session,UpdateMessage);session.AgentTaskChanged+=RefreshSummary;session.MessageAdded+=AddMessage;session.HistoryChanged+=HistoryChanged;session.StateChanged+=RefreshState;pet.Live.StateChanged+=RefreshState;
            string draft;input.Text=drafts.TryGetValue(DraftKey(),out draft)?draft:"";ReloadHistory();RefreshAppearance();RefreshState();
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
            if(taskPanel!=null){if(ReferenceEquals(inspectorHost.Content,taskPanel))inspectorHost.Content=null;else messageArea.Children.Remove(taskPanel);taskPanel.Dispose();taskPanel=null;}
            if(navigation!=null){workspaceShell.Children.Remove(navigation);navigation.Dispose();navigation=null;}
        }
        public void RefreshAppearance()
        {if(showingWelcome)ReloadHistory(false);if(!(Content is LiveCallView))Title=pet.Skin.Name+" · 对话";avatar.Source=SkinCatalog.GetAvatar(pet.Skin);avatar.ToolTip=pet.Skin.Name;if(composer!=null)composer.RefreshModels();RefreshProjectPicker();if(taskPanel!=null){taskPanel.SetTaskWorkspace(workspace==null?null:workspace.ProjectWorkspace);if(!session.IsBusy)session.LoadAgentTask(TaskSettings());}if(navigation!=null)navigation.Refresh();}
        public void ReloadHistory(bool scrollToBottom=true)
        {
            reloading=true;if(welcomeSuggestions!=null){welcomeSuggestions.Dispose();welcomeSuggestions=null;}messages.Children.Clear();rendered.Clear();displayed.Clear();showingWelcome=false;
            var turns=session.History.Messages.GroupBy(m=>m.TurnId).ToList();
            foreach(var turn in turns.Skip(Math.Max(0,turns.Count-visibleTurns)))foreach(var message in turn)AddMessage(message);
            earlier.IsEnabled=turns.Count>visibleTurns;export.IsEnabled=turns.Count>0;
            if(messages.Children.Count==0)Welcome();reloading=false;RefreshSummary();if(scrollToBottom)Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{if(!closed)scroll.ScrollToEnd();}));
        }
        private void HistoryChanged(){if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(HistoryChanged));return;}visibleTurns=50;ReloadHistory();}
        private void Export()
        {
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Markdown 记录 (*.md)|*.md",FileName="DeskPet-chat-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".md"};
            if(dialog.ShowDialog(this)!=true)return;
            try{File.WriteAllText(dialog.FileName,session.ExportHistory(),new System.Text.UTF8Encoding(false));ShowStatus("当前伙伴记录已导出。");}
            catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException))throw;ShowStatus("导出失败，请选择可写入的位置。");}
        }
        private void UpdateMessage(ChatMessage message)
        {
            if(closed || !rendered.ContainsKey(message.Id))return;
            bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;
            displayed[message.Id]=message;rendered[message.Id].Update(message);RefreshSummary();
            if(follow)Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{if(!closed)scroll.ScrollToEnd();}));
        }
        WelcomeSuggestions welcomeSuggestions;
        private void Welcome()
        {
            showingWelcome=true;
            var panel=new StackPanel{Margin=new Thickness(26,28,26,26)};
            var character=new Image{Name="WelcomeCharacter",Source=SkinCatalog.GetAvatar(pet.Skin),Width=154,Height=154,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,154,154),30.8,30.8),HorizontalAlignment=HorizontalAlignment.Left};
            RenderOptions.SetBitmapScalingMode(character,BitmapScalingMode.HighQuality);panel.Children.Add(character);
            var heading=Ui.Text("一起聊聊今天的想法",24);heading.Name="WelcomeHeading";heading.FontWeight=FontWeights.SemiBold;panel.Children.Add(heading);
            bool work=pet.ConversationSettings(session).AgentEnabled;
            panel.Children.Add(Ui.Text(work?"聊代码、设计，或刚想到的小点子。\n工作记录和工作提示词在伙伴间共享。":"聊代码、设计，或刚想到的小点子。\n每位伙伴拥有各自的人格与会话。",13,Ui.Muted));
            welcomeSuggestions=new WelcomeSuggestions(pet,this,session,workspace,input);panel.Children.Add(welcomeSuggestions);messages.Children.Add(panel);
        }

        private void AddMessage(ChatMessage message)
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(delegate {AddMessage(message);}));return;}
            if(!session.History.Messages.Any(m=>m.Id==message.Id))return;if(rendered.ContainsKey(message.Id)){UpdateMessage(message);return;}bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;
            if(showingWelcome){messages.Children.Clear();showingWelcome=false;}bool user=message.Role=="user";if(!reloading && !follow && user)visibleTurns=Math.Min(200,visibleTurns+1);displayed[message.Id]=message;
            var view=new ChatMessageView(message,messages,pet,this,session,queue);rendered[message.Id]=view;messages.Children.Add(view);RefreshSummary();
            while(messages.Children.Count>visibleTurns*2){var old=messages.Children[0] as Border;if(old!=null){string id=old.Tag as string;if(id!=null){rendered.Remove(id);displayed.Remove(id);}}messages.Children.RemoveAt(0);}
            if(!reloading){earlier.IsEnabled=session.History.Messages.Select(m=>m.TurnId).Distinct().Count()>visibleTurns;export.IsEnabled=true;if(follow)Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{if(!closed)scroll.ScrollToEnd();}));}
        }
        void RefreshSummary()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshSummary));return;}
            messages.Children.Remove(changeSummary);messages.Children.Remove(contextNotice);var task=session.CurrentAgentTask;var reply=task==null?null:session.History.Messages.LastOrDefault(m=>m.Role=="assistant" && m.AgentTaskId==task.Id);
            changeSummary.Refresh(reply==null || reply.Status=="pending"?null:task);ChatMessageView view;if(reply!=null && changeSummary.Visibility==Visibility.Visible && rendered.TryGetValue(reply.Id,out view)){int index=messages.Children.IndexOf(view);if(index>=0)messages.Children.Insert(index+1,changeSummary);}
            if(reply!=null && (task.ContextCompactions>0 || task.ContextStatus=="compacting" || task.ContextStatus=="limited" || task.ContextStatus=="cancelled") && rendered.TryGetValue(reply.Id,out view)){int index=messages.Children.IndexOf(view);if(index>=0){contextLabel.Text=task.ContextStatus=="compacting"?"正在压缩，请稍后":task.ContextStatus=="limited"?"上下文压缩未完成，请查看任务详情":task.ContextStatus=="cancelled"?"上下文压缩已停止":"上下文已自动压缩";contextNotice.ToolTip="本任务已自动压缩 "+task.ContextCompactions+" 次；任务目标和最近操作已保留。";messages.Children.Insert(index,contextNotice);}}
        }
        private void RefreshState()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshState));return;}
            RefreshAppearance();if(pet.DictationActive)return;
            if(pet.LiveActive){ShowStatus(pet.Live.Status);return;}
            ShowStatus(session.Stage==ConversationStage.Recognizing?"正在识别语音…":session.Stage==ConversationStage.Thinking?"正在思考…":session.Stage==ConversationStage.Speaking?"正在朗读 · 可以随时停止":String.IsNullOrEmpty(session.AgentTaskWarning)?String.IsNullOrEmpty(session.HistoryWarning)?(String.IsNullOrEmpty(session.LastNotice)?ChatUi.SendHint(pet.Settings):session.LastNotice):session.HistoryWarning:session.AgentTaskWarning);
        }
        private void ShowStatus(string text){if(!closed)status.Text=text;}
        private void ShowError(Exception error)
        {if(error is OperationCanceledException)ShowStatus("已停止。");else if(error is ApiException || error is InvalidOperationException)ShowStatus(error.Message);else ShowStatus("操作未完成，请检查连接设置、麦克风或音频输出。当前文字回复会保留。");}
    }
}
