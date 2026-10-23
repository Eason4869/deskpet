using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Dafeiyu
{
    public sealed class QuickChatWindow : Window
    {
        internal void OpenBrowserLink(Uri uri){pet.OpenChatBrowserLink(uri);}
        readonly PetWindow pet;readonly ConversationSession session;readonly ConversationQueue queue;readonly MarkdownEditor input;MarkdownView reply;readonly StackPanel messages=new StackPanel();readonly System.Collections.Generic.Dictionary<string,ChatMessageView> rendered=new System.Collections.Generic.Dictionary<string,ChatMessageView>();readonly ScrollViewer scroll;readonly TextBlock status;readonly Button record;readonly ConversationComposer composer;readonly Polygon tail;readonly Image avatar;
        bool closed;string renderedPersona;readonly MessageUpdatePump messagePump;
        readonly Button close;readonly Button taskDetails;GuideFocusScope guideScope;
        readonly AgentQuestionSurface questionSurface;
        internal bool CanAnswerQuestion(AgentQuestionRequest request){return AgentQuestionSurface.Matches(session,request);}
        public QuickChatWindow(PetWindow companion,ConversationWorkspace workspace=null)
        {
            pet=companion;session=workspace==null?pet.Conversation:workspace.Session;queue=workspace==null?pet.InputQueue:workspace.Queue;Icon=Ui.ApplicationIcon();Width=400;Height=Math.Min(470,Math.Max(300,SystemParameters.WorkArea.Height-24));AllowsTransparency=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=pet.Settings.Topmost;Background=Brushes.Transparent;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=12;Foreground=Ui.Ink;SlimScrollBars.Install(this);UseLayoutRounding=true;
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(14)});
            var panel=new Grid{Margin=new Thickness(18,14,18,4)};
            panel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});panel.RowDefinitions.Add(new RowDefinition());panel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new DockPanel();
            close=UiIcons.Button("close",TextResources.Get("quickChatWindow.quickChatWindow.uiIcons"));close.Name="CloseQuickChat";close.Margin=new Thickness(0);close.Click+=delegate {Close();};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            var expand=UiIcons.Button("expand",TextResources.Get("quickChatWindow.quickChatWindow.uiIcons.alternativeA"));expand.Name="ExpandChat";expand.Click+=delegate {pet.ShowChat();};DockPanel.SetDock(expand,Dock.Right);header.Children.Add(expand);
            avatar=new Image{Source=SkinCatalog.GetAvatar(pet.Skin),Width=34,Height=34,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,34,34),6.8,6.8),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};avatar.Name="QuickAvatar";RenderOptions.SetBitmapScalingMode(avatar,BitmapScalingMode.HighQuality);DockPanel.SetDock(avatar,Dock.Left);header.Children.Add(avatar);
            taskDetails=Ui.Button(TextResources.Get("quickChatWindow.quickChatWindow.text"));taskDetails.Name="QuickAgentTask";taskDetails.FontSize=11;taskDetails.MinHeight=24;taskDetails.Click+=delegate{pet.ShowChat();};DockPanel.SetDock(taskDetails,Dock.Right);header.Children.Add(taskDetails);
            var identity=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};identity.Children.Add(UiIcons.Speech(pet));
            var quickBadge=Ui.Text(TextResources.Get("quickChatWindow.quickChatWindow.text.alternativeA"),11,Ui.Muted);quickBadge.Name="QuickChatBadge";quickBadge.Margin=new Thickness(8,1,0,0);quickBadge.VerticalAlignment=VerticalAlignment.Center;identity.Children.Add(quickBadge);
            header.Children.Add(identity);panel.Children.Add(header);
            scroll=Ui.Scroll(messages);scroll.Name="QuickHistoryScroll";scroll.Margin=new Thickness(0,12,0,8);Grid.SetRow(scroll,1);panel.Children.Add(scroll);questionSurface=new AgentQuestionSurface(pet.QuestionController,r=>AgentQuestionSurface.Matches(session,r));var feed=new Grid();feed.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});feed.RowDefinitions.Add(new RowDefinition());panel.Children.Remove(scroll);feed.Children.Add(questionSurface);Grid.SetRow(scroll,1);feed.Children.Add(scroll);Grid.SetRow(feed,1);panel.Children.Add(feed);
            status=Ui.Text("",11,Ui.Muted);status.Name="QuickStatus";
            input=new MarkdownEditor();input.Name="QuickMessageInput";input.MaxLength=8000;input.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){if(composer!=null && composer.DismissPrediction()){e.Handled=true;return;}e.Handled=true;Close();}};
            record=UiIcons.Button("mic",TextResources.Get("quickChatWindow.quickChatWindow.uiIcons.alternativeB"));record.Name="VoiceRecord";
            composer=new ConversationComposer(pet,this,input,record,status,true,true,session,queue);Grid.SetRow(composer,2);panel.Children.Add(composer);
            if(workspace!=null){composer.PrepareSubmission=delegate{return pet.PrepareConversationSubmission(workspace);};composer.Submitted=delegate{pet.FinishConversationSubmission(workspace);};}
            root.Children.Add(new Border{Child=panel,CornerRadius=new CornerRadius(20),BorderThickness=new Thickness(1),BorderBrush=Ui.Line,Background=ThemeManager.Surface});tail=new Polygon{Fill=ThemeManager.Surface,Stroke=Ui.Line,StrokeThickness=1,Points=new PointCollection(new[]{new Point(168,0),new Point(180,13),new Point(192,0)})};Grid.SetRow(tail,1);root.Children.Add(tail);Content=root;
            messagePump=new MessageUpdatePump(session,Message);session.MessageAdded+=Message;session.HistoryChanged+=HistoryChanged;session.StateChanged+=RefreshState;session.AgentTaskChanged+=RefreshState;pet.Live.StateChanged+=RefreshState;pet.Live.PartialTranscript+=Partial;Loaded+=delegate {FollowPet();FocusInput();};IsVisibleChanged+=delegate {if(IsVisible)RefreshState();};Closed+=delegate {closed=true;questionSurface.Dispose();pet.CancelQuestionsWithoutHost(this);composer.Detach();messagePump.Dispose();session.MessageAdded-=Message;session.HistoryChanged-=HistoryChanged;session.StateChanged-=RefreshState;session.AgentTaskChanged-=RefreshState;pet.Live.StateChanged-=RefreshState;pet.Live.PartialTranscript-=Partial;};RefreshPersona();RefreshState();
        }
        public bool HasDraft { get { return !String.IsNullOrWhiteSpace(input.Text) || (composer!=null && composer.HasEdit); } }
        internal void SetGuidePreview(bool active)
        {
            if(guideScope!=null){guideScope.Dispose();guideScope=null;RefreshState();}
            if(active){UpdateLayout();guideScope=new GuideFocusScope((UIElement)Content,close,reply,avatar);}
        }
        public void FocusInput(){if(input.IsVisible)input.Focus();}
        public void FollowPet(){if(!closed)BubblePlacement.Follow(this,pet,tail);}
        public void RefreshPersona(){if(composer!=null)composer.RefreshModels();avatar.Source=SkinCatalog.GetAvatar(pet.Skin);avatar.ToolTip=pet.Skin.Name;if(!(Content is LiveCallView))Title=TextResources.Format("quickChatWindow.refreshPersona.petSkinName",pet.Skin.Name);if(renderedPersona!=pet.Skin.Id){renderedPersona=pet.Skin.Id;ReloadHistory();}RefreshState();}
        void ReloadHistory()
        {
            messages.Children.Clear();rendered.Clear();
            reply=new MarkdownView{Name="QuickReply",Text=TextResources.Get("quickChatWindow.reloadHistory.text"),Margin=new Thickness(3,14,3,12)};
            var history=session.History.Messages;foreach(var message in history.Skip(Math.Max(0,history.Count-20)))Message(message);
            if(rendered.Count==0)messages.Children.Add(reply);
            Dispatcher.BeginInvoke(new Action(delegate{if(!closed)scroll.ScrollToEnd();}));
        }
        void Message(ChatMessage message)
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>Message(message)));return;}
            if(!session.History.Messages.Any(m=>m.Id==message.Id))return;
            bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;ChatMessageView view;
            if(rendered.TryGetValue(message.Id,out view))view.Update(message);
            else
            {
                if(rendered.Count==0)messages.Children.Clear();
                view=new ChatMessageView(message,messages,pet,this,session,queue);rendered[message.Id]=view;messages.Children.Add(view);
                if(message.Role=="assistant"){reply.Name="";reply=view.Body;reply.Name="QuickReply";}
                while(messages.Children.Count>20){var old=(ChatMessageView)messages.Children[0];rendered.Remove((string)old.Tag);messages.Children.RemoveAt(0);}
            }
            if(follow)Dispatcher.BeginInvoke(new Action(delegate{if(!closed)scroll.ScrollToEnd();}));
        }
        void HistoryChanged(){if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(HistoryChanged));return;}renderedPersona=null;RefreshPersona();}
        void Partial(string role,string text){if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>Partial(role,text)));return;}if(pet.LiveActive && role=="assistant")reply.Text=text;}
        void RefreshState()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshState));return;}
            taskDetails.Visibility=session.CurrentAgentTask==null?Visibility.Collapsed:Visibility.Visible;
            if(pet.DictationActive)return;
            status.Text=pet.LiveActive?pet.Live.Status:session.Stage==ConversationStage.Thinking?TextResources.Get("quickChatWindow.refreshState.recognizingStringIsNullOrEmpty"):session.Stage==ConversationStage.Speaking?TextResources.Get("quickChatWindow.refreshState.recognizingStringIsNullOrEmpty.alternativeA"):session.Stage==ConversationStage.Recording?TextResources.Get("quickChatWindow.refreshState.recognizingStringIsNullOrEmpty.alternativeB"):session.Stage==ConversationStage.Recognizing?TextResources.Get("quickChatWindow.refreshState.recognizingStringIsNullOrEmpty.alternativeC"):!String.IsNullOrEmpty(session.AgentTaskWarning)?session.AgentTaskWarning:String.IsNullOrEmpty(session.HistoryWarning)?(String.IsNullOrEmpty(session.LastNotice)?ChatUi.SendHint(pet.Settings):session.LastNotice):session.HistoryWarning;
        }
    }
}
