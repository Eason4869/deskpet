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
    internal sealed partial class ChatMessageView : Border
    {
        public readonly MarkdownView Body;
        readonly TextBlock state,thinkingLabel;
        readonly TextBlock elapsed;
        // A lightweight activity line shared by chat and work processing.
        readonly Border activityIcon=new Border{Name="ProcessActivityIcon",Width=14,Height=14,Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
        readonly StackPanel stageFeed=new StackPanel{Name="StageMilestones"};
        LinearGradientBrush shimmerBrush;System.Windows.Media.Animation.DoubleAnimation shimmerAnimation;
        readonly System.Windows.Threading.DispatcherTimer clock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        readonly TextBox details;
        readonly Expander thinking;
        readonly StackPanel toolLog=new StackPanel();
        readonly Dictionary<string,Expander> toolRows=new Dictionary<string,Expander>();
        readonly StackPanel thoughtArea=new StackPanel{Name="ThinkingProcess"};

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
            clock.Tick+=delegate{RefreshElapsed();};Loaded+=delegate{RefreshElapsed();LoadProcessMotion();};Unloaded+=delegate{clock.Stop();UnloadProcessMotion();};
            thinkingLabel=Ui.InlineText("",12,Ui.Muted);thinkingLabel.Name="ProcessStatus";thinkingLabel.TextWrapping=TextWrapping.NoWrap;thinkingLabel.TextTrimming=TextTrimming.CharacterEllipsis;
            thinkingLabel.SetBinding(MaxWidthProperty,new Binding("ActualWidth"){Source=this,Converter=new ChatWidthConverter(1,90)});
            details=TextBox("",13,AgentUi.Muted);details.MaxHeight=240;details.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;details.Margin=new Thickness(20,7,0,8);
            var processDetails=new StackPanel();InitializeProcessAvatar(message,processDetails);processDetails.Children.Add(details);processDetails.Children.Add(toolLog);
            // Collapsed by default: only the single activity line is visible.
            var activityHeader=new StackPanel{Orientation=Orientation.Horizontal};if(!user)activityHeader.Children.Add(processAvatar);activityHeader.Children.Add(activityIcon);activityHeader.Children.Add(thinkingLabel);
            thinking=new Expander{Name="ReasoningDetails",Header=activityHeader,Content=processDetails,IsExpanded=inspectThinking || detailedProcess,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Visibility=Visibility.Collapsed};
            ChatUi.ProcessExpander(thinking);
            // The black milestone feed sits outside the collapsed expander so it is
            // actually visible while the work is running.
            if(!user){thoughtArea.Children.Add(thinking);thoughtArea.Children.Add(stageFeed);panel.Children.Add(thoughtArea);}
            if(user && message.Attachments!=null && message.Attachments.Count>0)
            {
                var files=new WrapPanel{Name="SentAttachments",HorizontalAlignment=user?HorizontalAlignment.Right:HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8)};
                foreach(var file in message.Attachments)files.Children.Add(AttachmentCard(file));
                panel.Children.Add(files);
            }
            Body=new MarkdownView{Text=message.Content,FitContentWidth=user};
            if(user)panel.Children.Add(new Border{Name="SentUserBubble",HorizontalAlignment=HorizontalAlignment.Right,Background=Ui.Brush("#E8F2FF"),CornerRadius=new CornerRadius(18),Padding=new Thickness(15,10,15,10),Child=Body,Visibility=String.IsNullOrEmpty(message.Content)?Visibility.Collapsed:Visibility.Visible});
            else panel.Children.Add(Body);
            state=Ui.Text("",11,Ui.Muted);state.Margin=new Thickness(0,6,0,0);if(!user)panel.Children.Add(state);
            if(!user){panel.Children.Add(agentJobsHost);panel.Children.Add(generatedImagesHost);panel.Children.Add(changeSummaryHost);panel.Children.Add(outputFiles);}
            actions=new StackPanel{Name="ReplyActions",Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,7,0,0)};
            if(user)
            {
                var userActions=new StackPanel{Name="UserMessageActions",Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,3,0,0),Opacity=0};
                var sent=Ui.Text(message.Timestamp.ToLocalTime().ToString("HH:mm"),11,Ui.Brush("#969BA3"));sent.Name="UserMessageTime";sent.VerticalAlignment=VerticalAlignment.Center;sent.Margin=new Thickness(0,0,7,0);sent.ToolTip=message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");userActions.Children.Add(sent);
                var copyUser=ActionIcon("copy",TextResources.Get("chatMessageView.chatMessageView.actionIcon"),"CopyUserMessage");copyUser.Margin=new Thickness(0);copyUser.Click+=delegate{try{Clipboard.SetText(current.Content??"");}catch(System.Runtime.InteropServices.COMException){session.ReportNotice(TextResources.Get("chatMessageView.chatMessageView.sessionReportNotice"));}};userActions.Children.Add(copyUser);panel.Children.Add(userActions);
                Action syncActions=delegate{userActions.Opacity=IsMouseOver || IsKeyboardFocusWithin?1:0;};MouseEnter+=delegate{syncActions();};MouseLeave+=delegate{syncActions();};GotKeyboardFocus+=delegate{syncActions();};LostKeyboardFocus+=delegate{Dispatcher.BeginInvoke(syncActions);};
            }
            if(!user)
            {
                var copy=ActionIcon("copy",TextResources.Get("common.copy"),"CopyReply");copy.Click+=delegate{try{Clipboard.SetText(current.Content);}catch(System.Runtime.InteropServices.COMException){session.ReportNotice(TextResources.Get("chatMessageView.chatMessageView.sessionReportNotice"));}};actions.Children.Add(copy);
                var speak=ActionIcon("speaker",TextResources.Get("chatMessageView.chatMessageView.actionIcon.alternativeA"),"ReadReply");speak.Click+=async delegate{try{if(session.Stage==ConversationStage.Speaking)pet.StopReplySpeech();else await session.SpeakReplyAsync(current.Id,pet.Settings);}catch(Exception e){session.ReportNotice(e is ApiException || e is InvalidOperationException?e.Message:TextResources.Get("chatMessageView.chatMessageView.sessionReportNotice.alternativeA"));}};actions.Children.Add(speak);
                var more=ActionIcon("more",TextResources.Get("chatMessageView.chatMessageView.actionIcon.alternativeB"),"MoreReply");var menu=ChatUi.Menu();var regenerate=new MenuItem{Header=TextResources.Get("chatMessageView.chatMessageView.header.alternativeD"),Name="RegenerateReply"};regenerate.Click+=async delegate{bool resumeQueue=!queue.IsPaused;queue.Hold();try{await session.RegenerateAsync(current.Id,pet.ConversationSettings(session),pet.Skin.Name);}catch(Exception e){session.ReportNotice(e is ApiException || e is InvalidOperationException?e.Message:TextResources.Get("chatMessageView.chatMessageView.sessionReportNotice.alternativeB"));}finally{if(resumeQueue)queue.Resume();}};menu.Items.Add(regenerate);
                var inspect=new MenuItem{Header=TextResources.Get("chatMessageView.chatMessageView.header.alternativeE"),IsCheckable=true};inspect.Click+=delegate{inspectThinking=inspect.IsChecked;thinking.IsExpanded=inspectThinking;Update(current);};menu.Items.Add(inspect);
                var plain=new MenuItem{Header=TextResources.Get("chatMessageView.chatMessageView.header.alternativeA")};plain.Click+=delegate{try{Clipboard.SetText(MarkdownContent.PlainText(current.Content));}catch(System.Runtime.InteropServices.COMException){session.ReportNotice(TextResources.Get("chatMessageView.chatMessageView.sessionReportNotice"));}};menu.Items.Add(plain);
                var export=new MenuItem{Header=TextResources.Get("chatMessageView.chatMessageView.header.alternativeF")};export.Click+=delegate{var picker=new Microsoft.Win32.SaveFileDialog{Filter="Markdown (*.md)|*.md",FileName="DeskPet-reply-"+current.Timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss")+".md"};if(picker.ShowDialog(host)==true)try{File.WriteAllText(picker.FileName,current.Content,new System.Text.UTF8Encoding(false));}catch(IOException){session.ReportNotice(TextResources.Get("chatMessageView.chatMessageView.sessionReportNotice.alternativeC"));}catch(UnauthorizedAccessException){session.ReportNotice(TextResources.Get("chatMessageView.chatMessageView.sessionReportNotice.alternativeD"));}};menu.Items.Add(export);more.ContextMenu=menu;more.Click+=delegate{regenerate.IsEnabled=!pet.DialogueBusy && !queue.IsRunning;menu.PlacementTarget=more;menu.IsOpen=true;};actions.Children.Add(more);panel.Children.Add(actions);
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
            if(file.IsImage)
            {
                card.Focusable=true;card.Cursor=System.Windows.Input.Cursors.Hand;card.ToolTip=TextResources.Format("chatMessageView.attachmentCard.toolTipFileName",file.Name);System.Windows.Automation.AutomationProperties.SetName(card,TextResources.Format("chatMessageView.attachmentCard.automationAutomationPropertiesSetName",file.Name));
                card.MouseLeftButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){e.Handled=true;ChatImagePreview.OpenAttachment(host,file);};
                card.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Space){e.Handled=true;ChatImagePreview.OpenAttachment(host,file);}};
            }
            if(file.IsImage)try
            {
                int width,height;bool limited;var bitmap=ChatImagePreview.DecodeImage(ChatImagePreview.DecodeBase64(file.Data,ChatAttachment.MaximumBytes),160,out width,out height,out limited);
                // Painting the border itself clips the thumbnail to its rounded corners.
                card.Background=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill};return card;
            }
            catch(Exception e){if(!(e is IOException || e is FormatException || e is NotSupportedException || e is ArgumentException || e is InvalidOperationException || e is System.Runtime.InteropServices.ExternalException))throw;}
            var content=new Grid{Margin=new Thickness(10,8,10,6)};content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});content.ColumnDefinitions.Add(new ColumnDefinition());var icon=UiIcons.Image("file",Ui.Muted);icon.Width=icon.Height=24;icon.HorizontalAlignment=HorizontalAlignment.Left;icon.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(icon,0);content.Children.Add(icon);
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var label=Ui.Text(file.Name,11,Ui.Blue);label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxHeight=34;text.Children.Add(label);var detail=Ui.Text(file.MediaType=="text/plain"?TextResources.Get("chatMessageView.attachmentCard.text"):file.MediaType??TextResources.Get("chatMessageView.attachmentCard.text.alternativeA"),10,Ui.Muted);detail.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(detail);Grid.SetColumn(text,1);content.Children.Add(text);card.Child=content;return card;
        }
        ContextMenu AttachmentMenu(ChatAttachment file)
        {
            return FileActionsMenu.Create(()=>OpenAttachment(file),()=>OpenAttachmentWith(file),()=>RevealAttachment(file),()=>CopyAttachmentPath(file),()=>CopyAttachmentContent(file),()=>SaveAttachmentAs(file),true,file.MediaType=="text/plain"||!String.IsNullOrEmpty(file.Text));
        }
        void OpenAttachment(ChatAttachment file){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EnsureAttachmentPath(file)){UseShellExecute=true});}catch(Exception e){ReportAttachmentError(TextResources.Get("chatMessageView.openAttachment.reportAttachmentError"),e);}}
        void OpenAttachmentWith(ChatAttachment file){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe","shell32.dll,OpenAs_RunDLL \""+EnsureAttachmentPath(file)+"\""){UseShellExecute=true});}catch(Exception e){ReportAttachmentError(TextResources.Get("chatMessageView.openAttachmentWith.reportAttachmentError"),e);}}
        void RevealAttachment(ChatAttachment file){try{string path=EnsureAttachmentPath(file);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+path+"\""){UseShellExecute=true});}catch(Exception e){ReportAttachmentError(TextResources.Get("chatMessageView.revealAttachment.reportAttachmentError"),e);}}
        void CopyAttachmentPath(ChatAttachment file){try{Clipboard.SetText(AttachmentPath(file));session.ReportNotice(TextResources.Get("chatMessageView.copyAttachmentPath.sessionReportNotice"));}catch(Exception e){ReportAttachmentError(TextResources.Get("chatMessageView.copyAttachmentPath.reportAttachmentError"),e);}}
        void CopyAttachmentContent(ChatAttachment file)
        {
            try{string content=file.Text; if(String.IsNullOrEmpty(content)){byte[] bytes=AttachmentBytes(file);content=System.Text.Encoding.UTF8.GetString(bytes);}if(String.IsNullOrWhiteSpace(content))throw new InvalidOperationException(TextResources.Get("chatMessageView.copyAttachmentContent.text"));Clipboard.SetText(content);session.ReportNotice(TextResources.Get("chatMessageView.copyAttachmentContent.sessionReportNotice"));}catch(Exception e){ReportAttachmentError(TextResources.Get("chatMessageView.copyAttachmentContent.reportAttachmentError"),e);}
        }
        void SaveAttachmentAs(ChatAttachment file)
        {
            var picker=new Microsoft.Win32.SaveFileDialog{FileName=file.Name,Filter=TextResources.Get("chatMessageView.saveAttachmentAs.filter")};if(picker.ShowDialog(host)!=true)return;try{byte[] bytes=AttachmentBytes(file);if(file.MediaType=="text/plain" && String.IsNullOrEmpty(file.Data))File.WriteAllText(picker.FileName,file.Text??"",new System.Text.UTF8Encoding(false));else File.WriteAllBytes(picker.FileName,bytes);session.ReportNotice(TextResources.Get("chatMessageView.saveAttachmentAs.sessionReportNotice"));}catch(Exception e){ReportAttachmentError(TextResources.Get("chatMessageView.saveAttachmentAs.reportAttachmentError"),e);}
        }
        string AttachmentPath(ChatAttachment file)
        {
            if(!String.IsNullOrWhiteSpace(file.SourcePath) && File.Exists(file.SourcePath))return file.SourcePath;string directory=Path.Combine(Path.GetTempPath(),"DeskPet-attachments");Directory.CreateDirectory(directory);string safe=Path.GetFileName(file.Name??"attachment");if(String.IsNullOrWhiteSpace(safe))safe="attachment";string path=Path.Combine(directory,Guid.NewGuid().ToString("N")+"-"+safe);byte[] bytes=AttachmentBytes(file);File.WriteAllBytes(path,bytes);return path;
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
            bool firstSnapshot=current==null;current=message;
            UpdateOutputFiles(message);
            if(!user)RefreshGeneratedImages(message);
            RefreshElapsed();
            bool detailChanged=detailedProcess!=pet.Settings.AgentDetailedProcess;
            if(detailChanged)detailedProcess=pet.Settings.AgentDetailedProcess;
            Body.Text=message.Content;Body.Visibility=String.IsNullOrEmpty(message.Content)?Visibility.Collapsed:Visibility.Visible;
            bool hasThinking=!String.IsNullOrWhiteSpace(message.Reasoning),active=message.Status=="pending";
            bool hasTools=message.ToolEvents!=null && message.ToolEvents.Count>0,workMode=message.WorkMode;
            // Keep the live status above streamed output. Real process records
            // remain available beneath the completed reply and its actions.
            bool hasRecord=hasThinking || hasTools || (workMode && message.ProcessingStartedAt.HasValue);
            bool showProcess=!user && (active || hasRecord);
            thoughtArea.Visibility=showProcess?Visibility.Visible:Visibility.Collapsed;
            thinking.Visibility=Visibility.Visible;
            details.Visibility=!workMode && hasThinking?Visibility.Visible:Visibility.Collapsed;
            stageFeed.Visibility=workMode && active?Visibility.Visible:Visibility.Collapsed;
            // "详细过程" keeps its meaning as an expand-by-default override; the
            // resting state stays collapsed so only the single line shows.
            if(inspectThinking && hasRecord)thinking.IsExpanded=true;
            else if(!active && (processWasPending || firstSnapshot))thinking.IsExpanded=false;
            else if(active && detailChanged)thinking.IsExpanded=detailedProcess;
            PositionProcessRecord(active);processWasPending=active;
            var events=message.ToolEvents??new List<AgentToolEvent>();
            foreach(var id in toolRows.Keys.Where(id=>!events.Any(e=>e.Id==id)).ToArray()){toolLog.Children.Remove(toolRows[id]);toolRows.Remove(id);}
            foreach(var item in events)
            {
                Expander row;if(!toolRows.TryGetValue(item.Id,out row))
                {
                    var line=Ui.Text("",12,AgentUi.Muted);line.Name="AgentToolLog";line.TextWrapping=TextWrapping.NoWrap;line.TextTrimming=TextTrimming.CharacterEllipsis;line.SetBinding(MaxWidthProperty,new Binding("ActualWidth"){Source=this,Converter=new ChatWidthConverter(1,30)});
                    var toolDetails=TextBox("",12,AgentUi.Muted);toolDetails.MaxHeight=200;toolDetails.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
                    row=new Expander{Name="AgentToolDetails",Header=line,Content=toolDetails,IsExpanded=inspectThinking,Margin=new Thickness(3,2,0,3)};ChatUi.ProcessExpander(row);toolRows[item.Id]=row;toolLog.Children.Add(row);
                }
                else if(active && detailChanged)row.IsExpanded=inspectThinking;
                string summary=(item.Summary??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??item.Name;
                ((TextBlock)row.Header).Text=(item.Status=="running"?"◌ ":item.Status=="error"?"! ":"⌘ ")+summary+(item.Status=="running"?TextResources.Get("chatMessageView.update.text"):"");((TextBox)row.Content).Text=item.Summary??"";
            }
            RefreshAgentJobCards(message);
            details.Text=message.Reasoning??"";
            RefreshProcessRecordSummary(message,events);
            UpdateProcess(message,events,active,workMode);
            state.Text=active?"":MessagePresentation.Status(message.Status);
            state.Visibility=String.IsNullOrEmpty(state.Text)?Visibility.Collapsed:Visibility.Visible;
            actions.Visibility=message.Status=="complete" && !String.IsNullOrWhiteSpace(message.Content)?Visibility.Visible:Visibility.Collapsed;
        }
        void UpdateProcess(ChatMessage message,List<AgentToolEvent> events,bool active,bool workMode)
        {
            string text=message.Reasoning??"";
            var running=events.LastOrDefault(t=>t.Status=="running" || t.Status=="pending");
            string icon=running==null?null:ActivityIcon(running.Name);
            activityIcon.Visibility=active&&workMode&&icon!=null?Visibility.Visible:Visibility.Collapsed;
            if(icon!=null&&!String.Equals(activityIcon.Tag as string,icon,StringComparison.Ordinal)){activityIcon.Tag=icon;var glyph=UiIcons.Image(icon,AgentUi.Muted);glyph.Width=activityIcon.Width;glyph.Height=activityIcon.Height;activityIcon.Child=glyph;}
            if(active && workMode)
            {
                // Work mode: one lightweight line describing the current action.
                thinkingLabel.Text=running!=null?ActivityVerb(running.Name)+" "+ActivityTarget(running):
                    events.Count>0?TextResources.Get("chatMessageView.updateProcess.runningNullActivityVerb"):TextResources.Get("chatMessageView.updateProcess.runningNullActivityVerb.alternativeA");
            }
            else if(active)
            {
                string last=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"";
                thinkingLabel.Text=String.IsNullOrEmpty(last)?TextResources.Get("chatMessageView.updateProcess.thinkingLabelStringIsNullOrEmpty"):last;
            }
            else
            {
                StopShimmer();
                thinkingLabel.Foreground=AgentUi.Muted;
                thinkingLabel.Text=workMode?TextResources.Get("chatMessageView.updateProcess.thinkingLabelWorkMode"):TextResources.Get("chatMessageView.updateProcess.thinkingLabelWorkMode.alternativeA");
            }
            RefreshStageFeed(events,active,workMode);
            RefreshProcessMotion(SystemParameters.ClientAreaAnimation);
        }
        // Legacy names remain renderable in saved chat history.
        static string ActivityIcon(string name)
        {
            switch(name){case "write_file":case "edit_file":case "apply_patch":return "edit";case "run_command":case "exec_command":return "terminal";case "open_directory":return "folder-open";case "create_directory":return "folder";case "find_files":case "search_files":case "github_search_code":return "search";case "update_plan":case "list_commands":return "list";case "fetch_url":return "globe";case "restore_file":return "back";default:return "file";}
        }
        // Black, temporary milestone lines for a work run; they disappear with the
        // process area as soon as the reply is complete.
        void RefreshStageFeed(List<AgentToolEvent> events,bool active,bool workMode)
        {
            if(!workMode || !active){if(stageFeed.Children.Count>0)stageFeed.Children.Clear();return;}
            var done=events.Where(t=>t.Status=="complete").Reverse().Take(4).Reverse().ToList();
            string signature=String.Join("\n",done.Select(t=>t.Id+"\t"+t.Summary));
            if(signature==(stageFeed.Tag as string))return;stageFeed.Tag=signature;
            stageFeed.Children.Clear();
            foreach(var item in done)
            {
                string summary=(item.Summary??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??item.Name;
                var line=Ui.Text("✓ "+summary,12,Ui.Ink);line.TextWrapping=TextWrapping.NoWrap;line.TextTrimming=TextTrimming.CharacterEllipsis;line.Margin=new Thickness(20,1,0,1);line.Name="StageMilestone";
                stageFeed.Children.Add(line);
            }
        }
        static string ActivityVerb(string name)
        {
            switch(name)
            {
                case "write_file":case "edit_file":case "apply_patch":case "create_directory":return TextResources.Get("chatMessageView.activityVerb.text");
                case "run_command":case "exec_command":case "open_directory":return TextResources.Get("chatMessageView.activityVerb.text.alternativeA");
                case "read_file":case "read_files":case "list_files":case "find_files":case "search_files":case "list_changes":case "read_output":case "fetch_url":
                case "github_read_file":case "github_list_files":case "github_search_code":case "github_read_issue":case "github_list_pull_requests":return TextResources.Get("chatMessageView.activityVerb.text.alternativeB");
                case "restore_file":return TextResources.Get("chatMessageView.activityVerb.text.alternativeC");
                case "update_plan":case "list_commands":return TextResources.Get("chatMessageView.activityVerb.text.alternativeD");
                default:return TextResources.Get("chatMessageView.activityVerb.text.alternativeE");
            }
        }
        static string ActivityTarget(AgentToolEvent item)
        {
            string summary=(item.Summary??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
            int split=summary.IndexOf(' ');
            if(split>0 && split<summary.Length-1)return summary.Substring(split+1);
            return String.IsNullOrEmpty(summary)?item.Name:summary;
        }
        // A light band sweeps across the activity text so the working state reads
        // as live without animating the rest of the UI.
        void StartShimmer()
        {
            if(user || !IsLoaded || !SystemParameters.ClientAreaAnimation)return;
            // Re-bind even when the brush already exists: the activity line's
            // Foreground is reset on every snapshot, which would otherwise drop
            // the animated brush after the first update.
            if(shimmerBrush!=null){thinkingLabel.Foreground=shimmerBrush;return;}
            shimmerBrush=new LinearGradientBrush{StartPoint=new Point(0,0.5),EndPoint=new Point(1,0.5),MappingMode=BrushMappingMode.RelativeToBoundingBox,SpreadMethod=GradientSpreadMethod.Repeat};
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B,0x92,0x9C),0));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xD9,0xDD,0xE4),0.45));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B,0x92,0x9C),0.9));
            // Transform uses device-independent pixels. RelativeTransform moves
            // in brush coordinates, so the sweep actually crosses the text.
            var sweep=new TranslateTransform();shimmerBrush.RelativeTransform=sweep;
            shimmerAnimation=new System.Windows.Media.Animation.DoubleAnimation(-1,1,TimeSpan.FromSeconds(1.9)){RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever};
            thinkingLabel.Foreground=shimmerBrush;sweep.BeginAnimation(TranslateTransform.XProperty,shimmerAnimation);
        }
        void StopShimmer()
        {
            var sweep=shimmerBrush==null?null:shimmerBrush.RelativeTransform as TranslateTransform;if(sweep!=null)sweep.BeginAnimation(TranslateTransform.XProperty,null);
            shimmerBrush=null;shimmerAnimation=null;
            if(thinkingLabel!=null)thinkingLabel.Foreground=AgentUi.Muted;
        }
    }
    internal sealed class ChatWidthConverter : IValueConverter
    {
        readonly double factor,inset;public ChatWidthConverter(double factor,double inset){this.factor=factor;this.inset=inset;}
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture){return Math.Max(1,(double)value*factor-inset);}
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture){throw new NotSupportedException();}
    }
}
