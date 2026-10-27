using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    // This session has no store, queue, kernel session, or workspace registration.
    // Its only context is the settings snapshot and messages typed into this pane.
    internal sealed class SideChatPane:Grid,IDisposable
    {
        sealed class SilentSpeech:IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}
            public void Stop(){}public void SetVolume(int volume){}public void Dispose(){}
        }
        sealed class MessageRow
        {
            internal MarkdownView Body;
            internal TextBlock State;
        }
        readonly PetSettings snapshot;
        readonly Window host;
        readonly string companionName,companionDisplayName;
        readonly ImageSource avatar;
        readonly StackPanel messages=new StackPanel{Margin=new Thickness(14,12,14,10)};
        readonly Dictionary<string,MessageRow> rendered=new Dictionary<string,MessageRow>();
        readonly MarkdownEditor input=new MarkdownEditor{Name="SideChatInput",MaxLength=8000,MinHeight=60,MaxHeight=150,Padding=new Thickness(2,4,2,4),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        readonly TextBlock status=LocalizedUi.Text(()=>(TextResources.Get("sideChatPane.status.text")),11,AgentUi.Muted);
        readonly Button send=LocalizedUi.Button(()=>(TextResources.Get("sideChatPane.send.text")),true),stop=LocalizedUi.Button(()=>(TextResources.Get("sideChatPane.send.text.alternativeA")));
        readonly ScrollViewer scroll;
        readonly MessageUpdatePump updates;
        bool disposed;
        internal ConversationSession Session{get;private set;}

        internal SideChatPane(PetWindow pet,Window host,PetSettings settings,string projectName,IConversationApi api=null)
        {
            if(pet==null)throw new ArgumentNullException("pet");if(host==null)throw new ArgumentNullException("host");if(settings==null)throw new ArgumentNullException("settings");
            this.host=host;companionName=pet.Skin==null?TextResources.Get("sideChatPane.sideChatPane.petSkinName"):pet.Skin.Name;
            companionDisplayName=pet.Skin==null?companionName:pet.Skin.DisplayName;
            snapshot=settings.Copy();snapshot.AgentEnabled=false;snapshot.SaveChatHistory=false;snapshot.ReadReplies=false;snapshot.ShowPredictedMessages=false;
            string project=String.IsNullOrWhiteSpace(projectName)?TextResources.Get("sideChatPane.sideChatPane.projectStringIsNullOrWhiteSpace"):projectName;
            snapshot.SystemPrompt=(settings.AgentSystemPrompt??PetSettings.DefaultAgentSystemPrompt)+"\n\n当前是项目内独立的临时聊天。项目名称："+project+"\n工作目录："+(snapshot.AgentWorkspace??"")+"\n以上仅为项目背景，并不代表已读取项目。没有文件、目录、终端或其他 Agent 工具，不会自动读取文件，也无法看到主会话及其他临时聊天的历史。只根据本聊天中用户提供的内容交流，不要声称已经查看或修改电脑文件。";
            Session=new ConversationSession(api??new ApiClient(),new SilentSpeech(),()=>null,null,()=>false,maintenance:pet.DataMaintenance);
            try{avatar=SkinCatalog.GetAvatar(pet.Skin);}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is NotSupportedException))throw;}
            Name="SideChatPane";Background=Ui.Brush("#F8F9FB");
            RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var heading=new StackPanel{Margin=new Thickness(18,14,18,10)};var title=LocalizedUi.Text(()=>(TextResources.Get("sideChatPane.sideChatPane.text")),16,AgentUi.Ink);title.FontWeight=FontWeights.SemiBold;heading.Children.Add(title);
            var projectLabel=Ui.Text(project,12,AgentUi.Muted);projectLabel.Name="SideChatProjectName";projectLabel.TextTrimming=TextTrimming.CharacterEllipsis;projectLabel.ToolTip=snapshot.AgentWorkspace;projectLabel.Margin=new Thickness(0,4,0,4);heading.Children.Add(projectLabel);
            var detail=LocalizedUi.Text(()=>(TextResources.Get("sideChatPane.sideChatPane.text.alternativeA")),11,AgentUi.Muted);detail.TextWrapping=TextWrapping.Wrap;heading.Children.Add(detail);Children.Add(heading);
            scroll=Ui.Scroll(messages);scroll.Name="SideChatHistory";Grid.SetRow(scroll,1);Children.Add(scroll);
            var footer=new StackPanel{Margin=new Thickness(14,8,14,12)};
            footer.Children.Add(new Border{Child=input,Background=Ui.Brush("#F7F8FA"),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(10,4,10,4)});
            var actions=new DockPanel{Margin=new Thickness(2,8,0,0)};var buttons=new StackPanel{Orientation=Orientation.Horizontal};send.Name="SendSideChat";send.Margin=new Thickness(8,0,0,0);send.MinHeight=30;stop.Name="StopSideChat";stop.MinHeight=30;buttons.Children.Add(stop);buttons.Children.Add(send);DockPanel.SetDock(buttons,Dock.Right);actions.Children.Add(buttons);status.Name="SideChatStatus";status.VerticalAlignment=VerticalAlignment.Center;status.TextWrapping=TextWrapping.Wrap;actions.Children.Add(status);footer.Children.Add(actions);Grid.SetRow(footer,2);Children.Add(footer);
            send.Click+=async delegate{await SendDraftAsync();};stop.Click+=async delegate{try{await Session.StopAsync();}catch(Exception error){ShowError(error);}};
            input.TextChanged+=delegate{RefreshState();};input.PreviewKeyDown+=async delegate(object sender,KeyEventArgs e){if(e.Key!=Key.Enter||input.IsComposing||(Keyboard.Modifiers&ModifierKeys.Shift)!=0)return;e.Handled=true;if(!Session.IsBusy)await SendDraftAsync();};
            Session.MessageAdded+=MessageAdded;Session.StateChanged+=StateChanged;updates=new MessageUpdatePump(Session,MessageAdded);host.Closed+=HostClosed;RefreshState();
        }

        internal async Task SendAsync(string text)
        {
            if(disposed)throw new ObjectDisposedException("SideChatPane");
            await Session.SendTextAsync(text,snapshot,companionName);
        }
        async Task SendDraftAsync()
        {
            if(disposed||Session.IsBusy||String.IsNullOrWhiteSpace(input.Text))return;
            string text=input.Text;int previous=Session.History.Messages.Count;input.Clear();
            try{await SendAsync(text);}catch(Exception error){if(!disposed&&Session.History.Messages.Count==previous&&String.IsNullOrEmpty(input.Text))input.Text=text;ShowError(error);}
        }
        void MessageAdded(ChatMessage message)
        {
            if(disposed||Dispatcher.HasShutdownStarted)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>MessageAdded(message)));return;}
            bool follow=scroll.ScrollableHeight-scroll.VerticalOffset<32;MessageRow row;
            if(!rendered.TryGetValue(message.Id,out row))
            {
                bool user=message.Role=="user";var line=new Grid{Margin=new Thickness(0,0,0,15)};line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});line.ColumnDefinitions.Add(new ColumnDefinition());
                FrameworkElement icon=user?(FrameworkElement)LocalizedUi.Text(()=>(TextResources.Get("sideChatPane.messageAdded.text")),11,Ui.Blue):avatar==null?(FrameworkElement)LocalizedUi.Text(()=>(TextResources.Get("sideChatPane.messageAdded.text.alternativeA")),11,AgentUi.Muted):new Image{Source=avatar,Width=22,Height=22,Stretch=Stretch.Uniform};
                line.Children.Add(new Border{Child=icon,Width=24,Height=24,CornerRadius=new CornerRadius(7),Background=Ui.Brush(user?"#EAF2FF":"#F0F1F4"),VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(2)});
                var body=new StackPanel();var label=LocalizedUi.Text(()=>(user?TextResources.Get("sideChatPane.messageAdded.text"):companionDisplayName),11,AgentUi.Muted);label.Margin=new Thickness(0,0,0,4);body.Children.Add(label);
                row=new MessageRow{Body=new MarkdownView{Name="SideChatMessage",FontSize=13},State=Ui.Text("",10,AgentUi.Muted)};body.Children.Add(new Border{Child=row.Body,Background=user?Ui.Brush("#EDF4FF"):Brushes.Transparent,CornerRadius=new CornerRadius(10),Padding=user?new Thickness(10,7,10,7):new Thickness(0)});body.Children.Add(row.State);Grid.SetColumn(body,1);line.Children.Add(body);messages.Children.Add(line);rendered.Add(message.Id,row);
            }
            row.Body.Text=message.Content;LocalizedUi.Set(row.State,"Text",()=>(message.Status=="pending"?TextResources.Get("sideChatPane.messageAdded.statusMessageStatus"):message.Status=="cancelled"||message.Status=="interrupted"?TextResources.Get("sideChatPane.messageAdded.statusMessageStatus.alternativeA"):message.Status=="error"?TextResources.Get("sideChatPane.messageAdded.statusMessageStatus.alternativeB"):""));row.State.Visibility=String.IsNullOrEmpty(row.State.Text)?Visibility.Collapsed:Visibility.Visible;
            if(follow)Dispatcher.BeginInvoke(new Action(delegate{if(!disposed)scroll.ScrollToEnd();}));
        }
        void StateChanged()
        {if(disposed||Dispatcher.HasShutdownStarted)return;if(Dispatcher.CheckAccess())RefreshState();else Dispatcher.BeginInvoke(new Action(RefreshState));}
        void RefreshState()
        {if(disposed)return;bool busy=Session.IsBusy;send.IsEnabled=!busy&&!String.IsNullOrWhiteSpace(input.Text);stop.Visibility=busy?Visibility.Visible:Visibility.Collapsed;status.Text=busy?TextResources.Get("sideChatPane.refreshState.busyStringIsNullOrEmpty"):String.IsNullOrEmpty(Session.LastNotice)?TextResources.Get("sideChatPane.refreshState.busyStringIsNullOrEmpty.alternativeA"):Session.LastNotice;}
        void ShowError(Exception error){if(disposed)return;LocalizedUi.Set(status,"Text",()=>(error is OperationCanceledException?TextResources.Get("sideChatPane.showError.invalidOperationExceptionErrorMessage"):error is ApiException||error is InvalidOperationException?error.Message:TextResources.Get("sideChatPane.showError.invalidOperationExceptionErrorMessage.alternativeA")));}
        void HostClosed(object sender,EventArgs args){Dispose();}
        static async void ObserveStop(Task task){try{await task.ConfigureAwait(false);}catch(Exception error){System.Diagnostics.Trace.WriteLine("Temporary chat shutdown: "+error.GetType().Name);}}
        public void Dispose()
        {
            if(disposed)return;disposed=true;host.Closed-=HostClosed;Session.MessageAdded-=MessageAdded;Session.StateChanged-=StateChanged;updates.Dispose();
            // Cancel synchronously, but never wait for a network continuation on
            // the dispatcher that the request may need in order to finish.
            try{ObserveStop(Session.StopAsync());}finally{Session.Dispose();}
        }
    }
}
