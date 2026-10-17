using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    // Both Windows chat surfaces render the same message and keep expansion state
    // while streaming snapshots update only their text.
    internal sealed class ChatMessageView : Border
    {
        public readonly MarkdownView Body;
        readonly TextBlock state,preview,thinkingLabel;
        readonly TextBlock elapsed;
        readonly System.Windows.Threading.DispatcherTimer clock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        readonly TextBox details;
        readonly Expander thinking;
        readonly StackPanel toolLog=new StackPanel();
        readonly Dictionary<string,Expander> toolRows=new Dictionary<string,Expander>();
        readonly StackPanel thoughtArea=new StackPanel{Name="ThinkingProcess"};
        readonly Border previewRow;
        string previousReasoning="",previousTools="";bool latestIsTool;
        bool inspectThinking;bool detailedProcess;readonly PetWindow pet;
        readonly bool user;readonly ConversationSession session;readonly ConversationQueue queue;readonly Window host;
        readonly StackPanel actions;ChatMessage current;
        public ChatMessageView(ChatMessage message,FrameworkElement history,PetWindow pet,Window host,ConversationSession sessionOverride=null,ConversationQueue queueOverride=null)
        {
            this.pet=pet;this.host=host;session=sessionOverride??pet.Conversation;queue=queueOverride??pet.InputQueue;detailedProcess=pet.Settings.AgentDetailedProcess;user=message.Role=="user";Name=user?"UserMessage":"AssistantMessage";Tag=message.Id;
            HorizontalAlignment=user?HorizontalAlignment.Right:HorizontalAlignment.Stretch;
            Background=Brushes.Transparent;BorderThickness=new Thickness(0);
            Padding=user?new Thickness(0):new Thickness(2,8,2,4);Margin=new Thickness(0,0,0,user?24:28);
            if(user)SetBinding(MaxWidthProperty,new Binding("ActualWidth"){Source=history,Converter=new ChatWidthConverter(.5,0)});
            var panel=new StackPanel();Child=panel;
            elapsed=Ui.Text("",12,Ui.Brush("#969BA3"));elapsed.Name="ReplyElapsedTime";
            if(!user)panel.Children.Add(new Border{Child=elapsed,BorderBrush=Ui.Brush("#ECECEC"),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(0,0,0,8),Margin=new Thickness(0,0,0,10)});
            clock.Tick+=delegate{RefreshElapsed();};Loaded+=delegate{RefreshElapsed();};Unloaded+=delegate{clock.Stop();};
            thinkingLabel=Ui.Text("",12,Ui.Muted);thinkingLabel.Name="ProcessStatus";
            preview=Ui.Text("",14,Ui.Ink);preview.Name="ProcessPreview";preview.TextWrapping=TextWrapping.NoWrap;preview.TextTrimming=TextTrimming.CharacterEllipsis;
            previewRow=new Border{BorderBrush=Ui.Brush("#ECECEC"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,9,0,0),Margin=new Thickness(0,5,0,14),Child=preview};
            details=TextBox("",14,Ui.Ink);details.MaxHeight=240;details.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;details.Margin=new Thickness(20,7,0,8);
            var processDetails=new StackPanel();processDetails.Children.Add(details);processDetails.Children.Add(toolLog);
            thinking=new Expander{Name="ReasoningDetails",Header=thinkingLabel,Content=processDetails,IsExpanded=detailedProcess,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Visibility=Visibility.Collapsed};
            ChatUi.ProcessExpander(thinking);
            thinking.Expanded+=delegate{previewRow.Visibility=Visibility.Collapsed;};thinking.Collapsed+=delegate{previewRow.Visibility=String.IsNullOrEmpty(preview.Text)?Visibility.Collapsed:Visibility.Visible;};
            if(!user){thoughtArea.Children.Add(thinking);thoughtArea.Children.Add(previewRow);panel.Children.Add(thoughtArea);}
            if(message.Attachments!=null && message.Attachments.Count>0)
            {
                var files=new WrapPanel{Name="SentAttachments",HorizontalAlignment=user?HorizontalAlignment.Right:HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8)};
                foreach(var file in message.Attachments)files.Children.Add(AttachmentCard(file));
                panel.Children.Add(files);
            }
            Body=new MarkdownView{Text=message.Content};
            if(user)panel.Children.Add(new Border{Name="SentUserBubble",Background=Ui.Brush("#E8F2FF"),CornerRadius=new CornerRadius(18),Padding=new Thickness(15,10,15,10),Child=Body,Visibility=String.IsNullOrEmpty(message.Content)?Visibility.Collapsed:Visibility.Visible});
            else panel.Children.Add(Body);
            state=Ui.Text("",11,Ui.Muted);state.Margin=new Thickness(0,6,0,0);if(!user)panel.Children.Add(state);
            actions=new StackPanel{Name="ReplyActions",Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,7,0,0)};
            if(user)
            {
                var userActions=new StackPanel{Name="UserMessageActions",Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,3,0,0),Opacity=0};
                var sent=Ui.Text(message.Timestamp.ToLocalTime().ToString("HH:mm"),11,Ui.Brush("#969BA3"));sent.Name="UserMessageTime";sent.VerticalAlignment=VerticalAlignment.Center;sent.Margin=new Thickness(0,0,7,0);sent.ToolTip=message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");userActions.Children.Add(sent);
                var copyUser=ActionIcon("copy","复制消息","CopyUserMessage");copyUser.Margin=new Thickness(0);copyUser.Click+=delegate{try{Clipboard.SetText(current.Content??"");}catch(System.Runtime.InteropServices.COMException){session.ReportNotice("剪贴板正忙，请再试一次。");}};userActions.Children.Add(copyUser);panel.Children.Add(userActions);
                Action syncActions=delegate{userActions.Opacity=IsMouseOver || IsKeyboardFocusWithin?1:0;};MouseEnter+=delegate{syncActions();};MouseLeave+=delegate{syncActions();};GotKeyboardFocus+=delegate{syncActions();};LostKeyboardFocus+=delegate{Dispatcher.BeginInvoke(syncActions);};
            }
            if(!user)
            {
                var copy=ActionIcon("copy","复制","CopyReply");copy.Click+=delegate{try{Clipboard.SetText(current.Content);}catch(System.Runtime.InteropServices.COMException){session.ReportNotice("剪贴板正忙，请再试一次。");}};actions.Children.Add(copy);
                var speak=ActionIcon("speaker","朗读","ReadReply");speak.Click+=async delegate{try{if(session.Stage==ConversationStage.Speaking)pet.StopReplySpeech();else {queue.Hold();await session.SpeakReplyAsync(current.Id,pet.Settings);}}catch(Exception e){session.ReportNotice(e is ApiException || e is InvalidOperationException?e.Message:"朗读未完成，请检查语音设置。");}};actions.Children.Add(speak);
                var more=ActionIcon("more","更多","MoreReply");var menu=ChatUi.Menu();var regenerate=new MenuItem{Header="重新生成此回复",Name="RegenerateReply"};regenerate.Click+=async delegate{try{queue.Hold();await session.RegenerateAsync(current.Id,pet.Settings,pet.Skin.Name);}catch(Exception e){session.ReportNotice(e is ApiException || e is InvalidOperationException?e.Message:"重新生成未完成。");}};menu.Items.Add(regenerate);
                var inspect=new MenuItem{Header="查看思考与工具记录",IsCheckable=true};inspect.Click+=delegate{inspectThinking=inspect.IsChecked;thinking.IsExpanded=inspectThinking;Update(current);};menu.Items.Add(inspect);
                var plain=new MenuItem{Header="复制纯文本"};plain.Click+=delegate{try{Clipboard.SetText(MarkdownContent.PlainText(current.Content));}catch(System.Runtime.InteropServices.COMException){session.ReportNotice("剪贴板正忙，请再试一次。");}};menu.Items.Add(plain);
                var export=new MenuItem{Header="导出本条回复…"};export.Click+=delegate{var picker=new Microsoft.Win32.SaveFileDialog{Filter="Markdown (*.md)|*.md",FileName="DeskPet-reply-"+current.Timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss")+".md"};if(picker.ShowDialog(host)==true)try{File.WriteAllText(picker.FileName,current.Content,new System.Text.UTF8Encoding(false));}catch(IOException){session.ReportNotice("导出失败，请选择可写入的位置。");}catch(UnauthorizedAccessException){session.ReportNotice("导出失败，请检查目录权限。");}};menu.Items.Add(export);more.ContextMenu=menu;more.Click+=delegate{regenerate.IsEnabled=!pet.DialogueBusy && !queue.IsRunning;menu.PlacementTarget=more;menu.IsOpen=true;};actions.Children.Add(more);panel.Children.Add(actions);
            }
            Update(message);
        }
        void RefreshElapsed()
        {
            if(user || current==null){clock.Stop();return;}
            elapsed.Text=current.ProcessingStartedAt.HasValue?current.ProcessingLabel(DateTime.UtcNow):"";
            ((Border)elapsed.Parent).Visibility=current.ProcessingStartedAt.HasValue?Visibility.Visible:Visibility.Collapsed;
            if(IsLoaded && current.Status=="pending" && current.ProcessingStartedAt.HasValue)clock.Start();else clock.Stop();
        }
        Border AttachmentCard(ChatAttachment file)
        {
            var card=new Border{Name="SentAttachmentCard",Width=file.IsImage?88:220,Height=80,CornerRadius=new CornerRadius(10),BorderBrush=Ui.Brush("#E2E3E6"),BorderThickness=new Thickness(1),Background=Ui.Brush("#FAFAFA"),Margin=new Thickness(6,0,0,0),ToolTip=String.IsNullOrWhiteSpace(file.SourcePath)?file.Name:file.SourcePath,ContextMenu=AttachmentMenu(file)};
            if(file.IsImage)try
            {
                var bitmap=new BitmapImage();using(var stream=new MemoryStream(Convert.FromBase64String(file.Data))){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=160;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();}
                // Painting the border itself clips the thumbnail to its rounded corners.
                card.Background=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill};return card;
            }
            catch(Exception e){if(!(e is IOException || e is FormatException || e is NotSupportedException || e is ArgumentException))throw;}
            var content=new Grid{Margin=new Thickness(10,8,10,6)};content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});content.ColumnDefinitions.Add(new ColumnDefinition());var icon=UiIcons.Image("file",Ui.Muted);icon.Width=icon.Height=24;icon.HorizontalAlignment=HorizontalAlignment.Left;icon.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(icon,0);content.Children.Add(icon);
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var label=Ui.Text(file.Name,11,Ui.Blue);label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxHeight=34;text.Children.Add(label);var detail=Ui.Text(file.MediaType=="text/plain"?"文本附件":file.MediaType??"文件",10,Ui.Muted);detail.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(detail);Grid.SetColumn(text,1);content.Children.Add(text);card.Child=content;return card;
        }
        ContextMenu AttachmentMenu(ChatAttachment file)
        {
            var menu=ChatUi.Menu();var open=new MenuItem{Header="打开文件",Icon=UiIcons.Image("file",AgentUi.Muted)};open.Click+=delegate{OpenAttachment(file);};menu.Items.Add(open);
            var openWith=new MenuItem{Header="打开方式…",Icon=UiIcons.Image("more",AgentUi.Muted)};openWith.Click+=delegate{OpenAttachmentWith(file);};menu.Items.Add(openWith);
            var reveal=new MenuItem{Header="在资源管理器中打开",Icon=UiIcons.Image("folder-open",AgentUi.Muted)};reveal.Click+=delegate{RevealAttachment(file);};menu.Items.Add(reveal);
            var copyPath=new MenuItem{Header="复制路径",Icon=UiIcons.Image("copy",AgentUi.Muted)};copyPath.Click+=delegate{CopyAttachmentPath(file);};menu.Items.Add(copyPath);
            var copyContent=new MenuItem{Header="复制文件内容",Icon=UiIcons.Image("copy",AgentUi.Muted)};copyContent.Click+=delegate{CopyAttachmentContent(file);};menu.Items.Add(copyContent);
            var saveAs=new MenuItem{Header="另存为…",Icon=UiIcons.Image("file",AgentUi.Muted)};saveAs.Click+=delegate{SaveAttachmentAs(file);};menu.Items.Add(saveAs);return menu;
        }
        void OpenAttachment(ChatAttachment file){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EnsureAttachmentPath(file)){UseShellExecute=true});}catch(Exception e){ReportAttachmentError("打开文件失败",e);}}
        void OpenAttachmentWith(ChatAttachment file){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe","shell32.dll,OpenAs_RunDLL \""+EnsureAttachmentPath(file)+"\""){UseShellExecute=true});}catch(Exception e){ReportAttachmentError("打开方式失败",e);}}
        void RevealAttachment(ChatAttachment file){try{string path=EnsureAttachmentPath(file);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+path+"\""){UseShellExecute=true});}catch(Exception e){ReportAttachmentError("打开资源管理器失败",e);}}
        void CopyAttachmentPath(ChatAttachment file){try{Clipboard.SetText(AttachmentPath(file));session.ReportNotice("文件路径已复制。");}catch(Exception e){ReportAttachmentError("复制路径失败",e);}}
        void CopyAttachmentContent(ChatAttachment file)
        {
            try{string content=file.Text; if(String.IsNullOrEmpty(content)){byte[] bytes=AttachmentBytes(file);content=System.Text.Encoding.UTF8.GetString(bytes);}if(String.IsNullOrWhiteSpace(content))throw new InvalidOperationException("此文件没有可复制的文字内容。");Clipboard.SetText(content);session.ReportNotice("文件内容已复制。");}catch(Exception e){ReportAttachmentError("复制文件内容失败",e);}
        }
        void SaveAttachmentAs(ChatAttachment file)
        {
            var picker=new Microsoft.Win32.SaveFileDialog{FileName=file.Name,Filter="所有文件|*.*"};if(picker.ShowDialog(host)!=true)return;try{byte[] bytes=AttachmentBytes(file);if(file.MediaType=="text/plain" && String.IsNullOrEmpty(file.Data))File.WriteAllText(picker.FileName,file.Text??"",new System.Text.UTF8Encoding(false));else File.WriteAllBytes(picker.FileName,bytes);session.ReportNotice("文件已保存。");}catch(Exception e){ReportAttachmentError("另存为失败",e);}
        }
        string AttachmentPath(ChatAttachment file)
        {
            if(!String.IsNullOrWhiteSpace(file.SourcePath) && File.Exists(file.SourcePath))return file.SourcePath;string directory=Path.Combine(Path.GetTempPath(),"DeskPet-attachments");Directory.CreateDirectory(directory);string path=Path.Combine(directory,Guid.NewGuid().ToString("N")+"-"+file.Name);byte[] bytes=AttachmentBytes(file);File.WriteAllBytes(path,bytes);return path;
        }
        string EnsureAttachmentPath(ChatAttachment file){return AttachmentPath(file);}
        byte[] AttachmentBytes(ChatAttachment file)
        {
            if(!String.IsNullOrEmpty(file.Data))return Convert.FromBase64String(file.Data);return System.Text.Encoding.UTF8.GetBytes(file.Text??"");
        }
        void ReportAttachmentError(string prefix,Exception error)
        {
            if(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception || error is FormatException)session.ReportNotice(prefix+"："+error.Message);else throw error;
        }
        static Button ActionIcon(string icon,string label,string name){var button=UiIcons.Button(icon,label);button.Name=name;button.Width=button.Height=button.MinHeight=28;button.Padding=new Thickness(3);button.Content=UiIcons.Image(icon,Ui.Brush("#969BA3"));button.Margin=new Thickness(0,0,5,0);return button;}
        static TextBox TextBox(string text,int size,Brush color)
        {return new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=color,Padding=new Thickness(0),FontSize=size,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};}
        public void Update(ChatMessage message)
        {
            current=message;
            RefreshElapsed();
            bool detailChanged=detailedProcess!=pet.Settings.AgentDetailedProcess;
            if(detailChanged){detailedProcess=pet.Settings.AgentDetailedProcess;thinking.IsExpanded=detailedProcess;}
            Body.Text=message.Content;Body.Visibility=String.IsNullOrEmpty(message.Content)?Visibility.Collapsed:Visibility.Visible;
            bool hasThinking=!String.IsNullOrWhiteSpace(message.Reasoning),active=message.Status=="pending" && String.IsNullOrEmpty(message.Content);
            bool hasTools=message.ToolEvents!=null && message.ToolEvents.Count>0;
            thoughtArea.Visibility=active || inspectThinking && (hasThinking || hasTools)?Visibility.Visible:Visibility.Collapsed;
            thinking.Visibility=Visibility.Visible;
            details.Visibility=hasThinking?Visibility.Visible:Visibility.Collapsed;
            var events=message.ToolEvents??new List<AgentToolEvent>();
            foreach(var id in toolRows.Keys.Where(id=>!events.Any(e=>e.Id==id)).ToArray()){toolLog.Children.Remove(toolRows[id]);toolRows.Remove(id);}
            foreach(var item in events)
            {
                Expander row;if(!toolRows.TryGetValue(item.Id,out row))
                {
                    var line=Ui.Text("",12,Ui.Brush("#969BA3"));line.Name="AgentToolLog";line.TextWrapping=TextWrapping.NoWrap;line.TextTrimming=TextTrimming.CharacterEllipsis;line.SetBinding(MaxWidthProperty,new Binding("ActualWidth"){Source=this,Converter=new ChatWidthConverter(1,30)});
                    var toolDetails=TextBox("",12,Ui.Muted);toolDetails.MaxHeight=200;toolDetails.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
                    row=new Expander{Name="AgentToolDetails",Header=line,Content=toolDetails,IsExpanded=detailedProcess,Margin=new Thickness(3,2,0,3)};ChatUi.ProcessExpander(row);toolRows[item.Id]=row;toolLog.Children.Add(row);
                }
                else if(active && detailChanged)row.IsExpanded=detailedProcess;
                string summary=(item.Summary??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??item.Name;
                ((TextBlock)row.Header).Text=(item.Status=="running"?"◌ ":item.Status=="error"?"! ":"⌘ ")+summary+(item.Status=="running"?" · 执行中":"");((TextBox)row.Content).Text=item.Summary??"";
            }
            details.Text=message.Reasoning??"";
            UpdateProcess(message,events,active);
            previewRow.Visibility=!thinking.IsExpanded && !String.IsNullOrEmpty(preview.Text)?Visibility.Visible:Visibility.Collapsed;
            state.Text=active?"":MessagePresentation.Status(message.Status);
            state.Visibility=String.IsNullOrEmpty(state.Text)?Visibility.Collapsed:Visibility.Visible;
            actions.Visibility=message.Status=="complete" && !String.IsNullOrWhiteSpace(message.Content)?Visibility.Visible:Visibility.Collapsed;
        }
        void UpdateProcess(ChatMessage message,List<AgentToolEvent> events,bool active)
        {
            string text=message.Reasoning??"",signature=String.Join("\n",events.Select(t=>t.Id+"\t"+t.Status+"\t"+t.Summary));
            if(text!=previousReasoning)latestIsTool=false;
            if(signature!=previousTools)latestIsTool=events.Count>0;
            previousReasoning=text;previousTools=signature;
            var running=events.LastOrDefault(t=>t.Status=="running" || t.Status=="pending");
            var latest=running??(latestIsTool || String.IsNullOrWhiteSpace(text)?events.LastOrDefault():null);
            preview.Text=latest!=null?((latest.Summary??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??latest.Name):text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"";
            preview.Foreground=latest!=null?Ui.Muted:Ui.Ink;
            string stage=running==null?(latest!=null || String.IsNullOrWhiteSpace(text) && message.WorkMode?"处理中":"思考中"):
                running.Name=="write_file" || running.Name=="edit_file" || running.Name=="apply_patch" || running.Name=="create_directory"?"编辑中":
                running.Name=="run_command" || running.Name=="exec_command" || running.Name=="shell" || running.Name=="open_directory"?"运行中":"处理中";
            thinkingLabel.Text=active?stage:"处理记录";
        }
    }
    internal sealed class ChatWidthConverter : IValueConverter
    {
        readonly double factor,inset;public ChatWidthConverter(double factor,double inset){this.factor=factor;this.inset=inset;}
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture){return Math.Max(1,(double)value*factor-inset);}
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture){throw new NotSupportedException();}
    }
}
