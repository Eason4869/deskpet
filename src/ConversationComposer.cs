using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class ConversationComposer : StackPanel
    {
        static readonly ControlTemplate hintTemplate=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'><Border Background='#202329' CornerRadius='8' Padding='9,6'><ContentPresenter/></Border></ControlTemplate>");
        PredictionSuggestions predictionSuggestions;AgentChangeStatus changeStatus;
        readonly PetWindow pet;readonly Window host;readonly TextBox input;readonly TextBlock status;ConversationQueue queue;ConversationSession session;
        readonly Button primary,options;readonly Border queueBox;readonly StackPanel rows;readonly TextBlock queueLabel;readonly Button resume;
        readonly Button modelPicker;readonly TextBlock modelLabel;readonly ContextMenu modelMenu;
        readonly Button modePicker;readonly TextBlock modeLabel;readonly Border modeIcon;
        readonly QueueReorderController reorder;
        internal Func<ApplicationSnapshotTarget> SnapshotTargetProvider=ApplicationSnapshots.Topmost;
        readonly bool conversationOnly;
        readonly Button addAttachment;readonly WrapPanel attachmentChips=new WrapPanel();readonly ScrollViewer attachmentArea;
        readonly System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<ChatAttachment>> attachmentDrafts=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<ChatAttachment>>();
        string attachmentPersona;bool loadingAttachments;
        System.Collections.Generic.List<ChatAttachment> Attachments {get{System.Collections.Generic.List<ChatAttachment> files;string id=pet.Skin.Id;if(!attachmentDrafts.TryGetValue(id,out files))attachmentDrafts[id]=files=new System.Collections.Generic.List<ChatAttachment>();return files;}}
        readonly DictationSession dictation;readonly Button record,finishDictation,stopDictation;readonly FrameworkElement normalSurface;readonly Border recordingSurface;readonly DictationWaveform waveform;
        readonly System.Windows.Threading.DispatcherTimer meter=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
        readonly System.Diagnostics.Stopwatch recordingTime=new System.Diagnostics.Stopwatch();bool finishing;
        string signature="",editingId,lastDictationNotice="";TextBox editor;bool resumeAfterEdit,closed,subscribed;int editRevision;
        public bool HasEdit {get{return editingId!=null || Attachments.Count>0 || loadingAttachments;}}
        internal bool DismissPrediction(){return predictionSuggestions!=null && predictionSuggestions.DismissGhost();}
        public ConversationComposer(PetWindow pet,Window host,TextBox input,Button record,TextBlock status,bool compact,bool conversationOnly=false,ConversationSession sessionOverride=null,ConversationQueue queueOverride=null)
        {
            this.pet=pet;this.host=host;this.input=input;this.status=status;queue=queueOverride??pet.InputQueue;session=sessionOverride??pet.Conversation;dictation=pet.Dictation;this.record=record;this.conversationOnly=conversationOnly;
            predictionSuggestions=new PredictionSuggestions(pet,sessionOverride,queueOverride,input,host){AdditionalBlocker=()=>HasEdit};Children.Add(predictionSuggestions);
            // The running task's real change count sits directly above the queue
            // rows and the input box, and collapses outside a running task.
            changeStatus=new AgentChangeStatus(pet,host,sessionOverride);Children.Add(changeStatus);
            var queueBody=new StackPanel();var heading=new DockPanel{Margin=new Thickness(10,4,7,1)};
            resume=SmallText("继续队列","ResumeQueue");resume.Foreground=Ui.Blue;resume.Click+=delegate{Try(delegate{queue.Resume();});};DockPanel.SetDock(resume,Dock.Right);heading.Children.Add(resume);
            queueLabel=Ui.Text("",10,Ui.Muted);queueLabel.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(queueLabel);queueBody.Children.Add(heading);
            rows=new StackPanel();var list=Ui.Scroll(rows);list.MaxHeight=compact?112:160;queueBody.Children.Add(list);
            reorder=new QueueReorderController(rows,list,(id,before)=>queue.MoveBefore(id,before),Error);
            Unloaded+=delegate{reorder.Cancel();};
            IsVisibleChanged+=delegate{if(!IsVisible)reorder.Cancel();};
            queueBox=new Border{Child=queueBody,BorderBrush=Ui.Line,BorderThickness=new Thickness(1,1,1,0),CornerRadius=new CornerRadius(14,14,0,0),Margin=new Thickness(10,0,10,-10),Padding=new Thickness(0,0,0,14),Background=Ui.Brush("#FAFBFD")};Children.Add(queueBox);
            var controls=new StackPanel{Orientation=Orientation.Horizontal};record.Margin=new Thickness(0);record.Width=32;record.Height=32;record.MinHeight=32;
            options=UiIcons.Button("down","发送选项");options.Name="SendOptions";options.Width=22;options.MinWidth=0;options.Margin=new Thickness(0,0,3,0);options.Padding=new Thickness(2);controls.Children.Add(options);
            record.Margin=new Thickness(0,0,5,0);controls.Children.Add(record);
            var menu=ChatUi.Menu();var interrupt=new MenuItem{Header="引导当前任务",InputGestureText="Ctrl+Enter"};interrupt.Click+=async delegate{await InterruptDraft();};menu.Items.Add(interrupt);var force=new MenuItem{Header="停止并立即发送"};force.Click+=async delegate{await InterruptDraft(true);};menu.Items.Add(force);options.ContextMenu=menu;options.Click+=delegate{menu.PlacementTarget=options;menu.IsOpen=true;};
            primary=ChatUi.Action("send","发送");primary.Name=compact?"SendQuickMessage":"SendMessage";primary.Width=primary.Height=primary.MinHeight=32;primary.Background=Ui.Brush("#357FF5");primary.BorderBrush=primary.Background;controls.Children.Add(primary);
            primary.Click+=async delegate
            {
                if(!String.IsNullOrWhiteSpace(input.Text) || Attachments.Count>0){Submit();return;}
                if(pet.DialogueBusy){try{await pet.StopDialogueAsync();}catch(Exception e){Error(e);}return;}
                pet.ShowLiveCall(host);
            };
            modelLabel=Ui.Text("",12,Ui.Muted);modelLabel.Name="CurrentTextModel";modelLabel.TextWrapping=TextWrapping.NoWrap;modelLabel.TextTrimming=TextTrimming.CharacterEllipsis;modelLabel.VerticalAlignment=VerticalAlignment.Center;
            var modelContent=new DockPanel();var arrow=UiIcons.Image("down",Ui.Muted);arrow.Width=arrow.Height=12;arrow.Margin=new Thickness(7,0,0,0);arrow.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(arrow,Dock.Right);modelContent.Children.Add(arrow);modelContent.Children.Add(modelLabel);
            modelPicker=Ui.Button("");modelPicker.Name="TextModelPicker";modelPicker.Content=modelContent;modelPicker.MinHeight=32;modelPicker.MinWidth=0;modelPicker.MaxWidth=280;modelPicker.Padding=new Thickness(7,3,7,3);modelPicker.Margin=new Thickness(0,0,6,0);modelPicker.Background=Brushes.Transparent;modelPicker.BorderBrush=Brushes.Transparent;modelPicker.HorizontalAlignment=HorizontalAlignment.Right;
            modelPicker.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            modelMenu=ChatUi.Menu();modelMenu.MaxHeight=420;modelMenu.MaxWidth=460;modelMenu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;modelPicker.ContextMenu=modelMenu;
            modelPicker.Click+=delegate{OpenModels();};modelPicker.ContextMenuOpening+=delegate(object sender,ContextMenuEventArgs e){e.Handled=true;OpenModels();};
            modelPicker.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Down || e.Key==Key.F4){e.Handled=true;OpenModels();}};
            // The status line stays outside both surfaces while recording replaces
            // the composer body with a real microphone waveform and explicit controls.
            addAttachment=UiIcons.Button("plus","添加文件或图片");addAttachment.Name="AddAttachment";addAttachment.Width=addAttachment.Height=addAttachment.MinHeight=32;addAttachment.Padding=new Thickness(5);addAttachment.Margin=new Thickness(0);addAttachment.Content=new TextBlock{Text="+",FontSize=23,Foreground=Ui.Ink,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            addAttachment.Click+=delegate{OpenAddMenu();};Hint(addAttachment,"添加文件等内容");
            // Quick chat is conversation-only, so its switch and Tab shortcut
            // are removed; the full composer keeps both.
            if(!conversationOnly)
            {
                modeLabel=Ui.Text("",12,Ui.Muted);modeLabel.VerticalAlignment=VerticalAlignment.Center;modeLabel.RenderTransform=new TranslateTransform(0,4);
                var modeContent=new StackPanel{Orientation=Orientation.Horizontal,RenderTransform=new TranslateTransform(-4,0)};modeIcon=new Border{Name="ConversationModeGlyph",Width=13,Height=13,Margin=new Thickness(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};modeContent.Children.Add(modeIcon);modeContent.Children.Add(modeLabel);
                modePicker=SmallText("","ConversationModePicker");modePicker.Content=modeContent;modePicker.Width=100;modePicker.Height=32;modePicker.MinHeight=32;modePicker.Padding=new Thickness(5,3,5,3);modePicker.HorizontalAlignment=HorizontalAlignment.Left;modePicker.VerticalAlignment=VerticalAlignment.Center;modePicker.Click+=delegate{OpenModes();};Hint(modePicker,"Tab 切换对话／工作模式，从下一次请求生效");
            }
            attachmentArea=new ScrollViewer{Content=attachmentChips,MaxHeight=compact?124:146,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Visibility=Visibility.Collapsed};
            var leading=new Grid{MinWidth=0};leading.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36)});leading.Children.Add(addAttachment);
            if(conversationOnly){leading.ColumnDefinitions.Add(new ColumnDefinition{MinWidth=0});Grid.SetColumn(modelPicker,1);leading.Children.Add(modelPicker);}
            else{leading.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});leading.ColumnDefinitions.Add(new ColumnDefinition{MinWidth=0});Grid.SetColumn(modePicker,1);leading.Children.Add(modePicker);Grid.SetColumn(modelPicker,2);leading.Children.Add(modelPicker);}
            modelPicker.HorizontalAlignment=HorizontalAlignment.Right;modelPicker.VerticalAlignment=VerticalAlignment.Center;modelPicker.Margin=new Thickness(0,0,2,0);modelPicker.RenderTransform=new TranslateTransform(0,4);
            record.Padding=new Thickness(5);primary.Padding=new Thickness(0);options.Padding=new Thickness(1);
            normalSurface=ChatUi.Composer(input,leading,controls,new TextBlock{Visibility=Visibility.Collapsed},compact,attachmentArea);Children.Add(normalSurface);
            var recordingBody=new StackPanel();var recordingLabel=Ui.Text("正在听写…",12,Ui.Muted);recordingLabel.MinHeight=48;recordingLabel.Padding=new Thickness(4,6,4,6);recordingBody.Children.Add(recordingLabel);var recordingControls=new DockPanel{Margin=new Thickness(2,8,2,0)};
            stopDictation=UiIcons.Button("stop-solid","停止听写");stopDictation.Name="StopDictation";stopDictation.Width=32;stopDictation.Height=32;stopDictation.MinHeight=32;stopDictation.Content=UiIcons.Image("stop-solid",Ui.Brush("#202329"));stopDictation.Margin=new Thickness(0,0,5,0);stopDictation.Click+=async delegate{await FinishDictation(false);};
            finishDictation=ChatUi.Action("send","发送听写");finishDictation.Name="FinishDictation";finishDictation.Width=finishDictation.Height=finishDictation.MinHeight=32;finishDictation.Background=Ui.Brush("#202329");finishDictation.BorderBrush=finishDictation.Background;finishDictation.Content=UiIcons.Image("send",Brushes.White);finishDictation.Click+=async delegate{await FinishDictation(true);};DockPanel.SetDock(finishDictation,Dock.Right);recordingControls.Children.Add(finishDictation);
            DockPanel.SetDock(stopDictation,Dock.Right);recordingControls.Children.Add(stopDictation);
            waveform=new DictationWaveform{Height=32};recordingControls.Children.Add(waveform);recordingBody.Children.Add(recordingControls);
            recordingSurface=new Border{Child=recordingBody,Background=Brushes.White,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(22),Padding=new Thickness(8),Visibility=Visibility.Collapsed};Children.Add(recordingSurface);
            status.Margin=new Thickness(3,7,3,0);status.MinHeight=16;status.MaxHeight=38;Children.Add(status);
            Hint(record,"听写");Hint(stopDictation,"停止听写");Hint(finishDictation,"发送听写");record.Click+=delegate{Try(delegate{dictation.Start(this);recordingTime.Restart();waveform.Clear();});};
            meter.Tick+=async delegate{if(!dictation.Owns(this)){meter.Stop();return;}waveform.Push(dictation.InputLevel);if(dictation.Stage==DictationStage.Recording && recordingTime.Elapsed.TotalSeconds>=60)await FinishDictation(false);};
            host.PreviewKeyDown+=async delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape && dictation.Owns(this)){e.Handled=true;try{await dictation.CancelAsync(this);}catch(Exception error){Error(error);}}};
            input.ToolTip="Enter 发送或排队（显示预测时先填入草稿）· Shift + Enter 换行 · Ctrl + Enter 引导当前任务";
            input.TextChanged+=delegate{RefreshAction();};input.PreviewKeyDown+=async delegate(object sender,KeyEventArgs e)
            {
                if(!conversationOnly && e.Key==Key.Tab && Keyboard.Modifiers==ModifierKeys.None){e.Handled=true;SetMode(!pet.Settings.AgentEnabled);return;}
                if(e.Key!=Key.Enter || (Keyboard.Modifiers&ModifierKeys.Shift)!=0)return;e.Handled=true;
                if(predictionSuggestions!=null && !predictionSuggestions.AllowEnter())return;
                // Only a plain Enter confirms the ghost; Ctrl+Enter keeps its
                // dedicated steer / stop-and-send meaning.
                if((Keyboard.Modifiers&ModifierKeys.Control)==0 && predictionSuggestions!=null && predictionSuggestions.TryAcceptGhost())return;
                if((Keyboard.Modifiers&ModifierKeys.Control)!=0 && pet.DialogueBusy)await InterruptDraft();else Submit();
            };
            CommandManager.AddPreviewExecutedHandler(input,async delegate(object sender,ExecutedRoutedEventArgs e)
            {
                if(e.Command!=ApplicationCommands.Paste)return;
                try
                {
                    if(Clipboard.ContainsFileDropList()){var files=Clipboard.GetFileDropList().Cast<string>().ToArray();e.Handled=true;await ImportDataObjectAsync(new DataObject(DataFormats.FileDrop,files));}
                    else if(Clipboard.ContainsImage()){var picture=Clipboard.GetImage();if(picture==null)return;e.Handled=true;await ImportDataObjectAsync(new DataObject(DataFormats.Bitmap,picture));}
                }
                catch(Exception error){if(!(error is System.Runtime.InteropServices.ExternalException || error is UnauthorizedAccessException))throw;e.Handled=true;status.Text="剪贴板暂时无法读取，请重新复制或通过 + 添加附件。";}
            });
            CommandManager.AddPreviewCanExecuteHandler(input,delegate(object sender,CanExecuteRoutedEventArgs e)
            {
                if(e.Command!=ApplicationCommands.Paste)return;
                e.Handled=true;
                try{e.CanExecute=!input.IsReadOnly && (Clipboard.ContainsText() || (CanAdd() && (Clipboard.ContainsFileDropList() || Clipboard.ContainsImage())));}
                catch(Exception error){if(!(error is System.Runtime.InteropServices.ExternalException || error is UnauthorizedAccessException))throw;e.CanExecute=false;}
            });
            input.ContextMenu=ChatUi.EditorMenu(input,false);
            host.AllowDrop=true;
            host.PreviewDragOver+=delegate(object sender,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap)){e.Effects=CanAdd()?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}};
            host.PreviewDrop+=async delegate(object sender,DragEventArgs e)
            {
                if(e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap)){e.Handled=true;await ImportDataObjectAsync(e.Data);}
            };
            Loaded+=delegate{if(!subscribed){queue.Changed+=Refresh;pet.Live.StateChanged+=Refresh;dictation.Changed+=Refresh;subscribed=true;}Refresh();};
            host.IsVisibleChanged+=async delegate{if(!host.IsVisible && dictation.Owns(this))try{await dictation.CancelAsync(this);}catch(Exception e){Error(e);}};
            host.Closed+=async delegate{closed=true;meter.Stop();if(subscribed){queue.Changed-=Refresh;pet.Live.StateChanged-=Refresh;dictation.Changed-=Refresh;subscribed=false;}if(dictation.Owns(this))try{await dictation.CancelAsync(this);}catch(OperationCanceledException){}catch(ObjectDisposedException){}};
            RefreshModels();Refresh();
        }
        internal void Rebind(ConversationSession nextSession,ConversationQueue nextQueue)
        {
            if(nextSession==null || nextQueue==null)throw new ArgumentNullException("session");session=nextSession;queue=nextQueue;predictionSuggestions.Rebind(nextSession,nextQueue);changeStatus.Rebind(nextSession);RefreshModels();Refresh();
        }
        public void RefreshModels()
        {
            if(predictionSuggestions!=null)predictionSuggestions.RefreshState();
            RefreshMode();
            pet.Settings.EnsureTextProviders();var provider=pet.Settings.TextProviders.FirstOrDefault(p=>p.Id==pet.Settings.ActiveTextProviderId);
            string model=pet.Settings.Text==null?"":pet.Settings.Text.Model;
            modelLabel.Text=String.IsNullOrWhiteSpace(model)?"选择模型":provider==null?model:provider.DisplayName(model);
            string description=provider==null?modelLabel.Text:provider.Name+" · "+modelLabel.Text;
            modelPicker.ToolTip=description+(String.IsNullOrWhiteSpace(model) || modelLabel.Text==model?"":"\n"+model)+"\n切换后从下一条请求生效";System.Windows.Automation.AutomationProperties.SetName(modelPicker,"选择文字模型，当前 "+description);
        }
        void RefreshMode()
        {
            bool work=pet.Settings.AgentEnabled;if(modeLabel!=null)modeLabel.Text=work?"工作模式":"对话模式";
            if(modeIcon!=null){var image=UiIcons.Image(work?"edit":"chat",Ui.Muted);image.Width=image.Height=13;modeIcon.Child=image;modeIcon.Tag=work?"work":"chat";}
        }
        void SetMode(bool work){if(pet.IsUpdating || pet.LiveActive)return;pet.Settings.AgentEnabled=work;pet.SaveSettings();pet.RefreshChatAppearance();RefreshMode();}
        void OpenModes()
        {
            var menu=ChatUi.Menu();var chat=new MenuItem{Name="SelectConversationMode",Header=AddMenuLabel("对话模式","仅聊天，不调用任何 Agent 工具"),Icon=UiIcons.Image("chat",Ui.Muted),IsCheckable=true,IsChecked=!pet.Settings.AgentEnabled};chat.Click+=delegate{SetMode(false);};menu.Items.Add(chat);
            var work=new MenuItem{Name="SelectWorkingMode",Header=AddMenuLabel("工作模式","按工作设置使用文件和目录工具"),Icon=UiIcons.Image("edit",Ui.Muted),IsCheckable=true,IsChecked=pet.Settings.AgentEnabled};work.Click+=delegate{SetMode(true);};menu.Items.Add(work);modePicker.ContextMenu=menu;menu.PlacementTarget=modePicker;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;menu.IsOpen=true;
        }
        void OpenModels()
        {
            RefreshModels();modelMenu.Items.Clear();
            foreach(var provider in pet.Settings.TextProviders)
            {
                if(provider.Models.Count==0)continue;
                if(modelMenu.Items.Count>0)modelMenu.Items.Add(new Separator());
                modelMenu.Items.Add(new MenuItem{Header=provider.Name,IsEnabled=false,FontWeight=FontWeights.SemiBold});
                foreach(string model in provider.Models)
                {
                    string providerId=provider.Id,selectedModel=model;
                    string displayName=provider.DisplayName(model);
                    var label=Ui.Text(displayName,12);label.MaxWidth=360;label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;
                    var item=new MenuItem{Name="SelectTextModel",Header=label,ToolTip=provider.Name+" · "+displayName+(displayName==model?"":"\n"+model),IsCheckable=true,IsChecked=provider.Id==pet.Settings.ActiveTextProviderId && model==pet.Settings.Text.Model,Tag=provider.Id+"/"+model};
                    System.Windows.Automation.AutomationProperties.SetName(item,provider.Name+" · "+displayName+"，"+model);
                    item.Click+=delegate{Try(delegate{if(!pet.Settings.SelectTextModel(providerId,selectedModel))return;pet.SaveSettings();pet.RefreshChatAppearance();RefreshModels();if(!String.IsNullOrEmpty(pet.Notice))status.Text=pet.Notice;});};modelMenu.Items.Add(item);
                }
            }
            if(modelMenu.Items.Count>0)modelMenu.Items.Add(new Separator());
            var configure=new MenuItem{Name="ConfigureTextModels",Header="管理服务商与模型…"};configure.Click+=delegate{pet.ShowSettings(true);};modelMenu.Items.Add(configure);
            modelMenu.PlacementTarget=modelPicker;modelMenu.IsOpen=true;
        }
        void Submit()
        {
            if((String.IsNullOrWhiteSpace(input.Text) && Attachments.Count==0) || loadingAttachments || pet.LiveActive || pet.IsUpdating)return;
            Try(delegate{queue.Enqueue(input.Text,Attachments,conversationOnly);Attachments.Clear();RefreshAttachments();input.Clear();input.Focus();});
        }
        internal void AddAttachments(System.Collections.Generic.IEnumerable<ChatAttachment> files)
        {
            var combined=ChatAttachment.Snapshot(Attachments.Concat(files));Attachments.Clear();Attachments.AddRange(combined);RefreshAttachments();RefreshAction();
        }
        void OpenAddMenu()
        {
            var menu=ChatUi.Menu();menu.Items.Add(new MenuItem{Header="添加",IsEnabled=false,Foreground=Ui.Muted});
            var files=new MenuItem{Header="文件和文件夹",Name="AddFilesAndFolders",Icon=UiIcons.Image("attachment",Ui.Muted)};
            files.Click+=async delegate{await PickAttachments();};menu.Items.Add(files);
            var target=SnapshotTargetProvider();
            var snapshots=new MenuItem{Header=target==null?"附加应用快照":"附加 "+target.AppName,Name="AddApplicationSnapshot",Icon=UiIcons.Image("snapshot",Ui.Muted),IsEnabled=target!=null,ToolTip=target==null?"没有可附加的应用窗口":target.Title};
            if(target!=null){snapshots.Icon=SnapshotIcon(target);snapshots.Click+=async delegate{await CaptureApplication(target);};}menu.Items.Add(snapshots);
            if(!conversationOnly)
            {
                var goal=new MenuItem{Header=AddMenuLabel("目标",String.IsNullOrWhiteSpace(pet.Settings.AgentGoal)?"设置要持续追求的目标":pet.Settings.AgentGoal),Name="SetAgentGoal",Icon=UiIcons.Image("target",Ui.Muted),IsEnabled=!pet.DialogueBusy};goal.Click+=delegate{ShowGoal();};menu.Items.Add(goal);
                var plan=new MenuItem{Header=AddMenuLabel("计划模式",pet.Settings.AgentPlanMode?"已开启，仅分析与读取":"开启计划模式"),Name="AgentPlanMode",IsCheckable=true,IsChecked=pet.Settings.AgentPlanMode,IsEnabled=!pet.DialogueBusy};plan.Click+=delegate{pet.Settings.AgentPlanMode=plan.IsChecked;pet.SaveSettings();RefreshAction();};menu.Items.Add(plan);
            }
            addAttachment.ContextMenu=menu;menu.PlacementTarget=addAttachment;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;menu.IsOpen=true;
        }
        static FrameworkElement AddMenuLabel(string title,string description)
        {var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(Ui.Text(title,13));var hint=Ui.Text(description.Replace("\r"," ").Replace("\n"," "),12,Ui.Brush("#A0A4AB"));hint.MaxWidth=210;hint.TextWrapping=TextWrapping.NoWrap;hint.TextTrimming=TextTrimming.CharacterEllipsis;hint.Margin=new Thickness(9,0,0,0);row.Children.Add(hint);return row;}
        static FrameworkElement SnapshotIcon(ApplicationSnapshotTarget target)
        {
            if(target.IconPng!=null)try
            {
                var bitmap=new System.Windows.Media.Imaging.BitmapImage();using(var stream=new System.IO.MemoryStream(target.IconPng)){bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();}
                return new Image{Source=bitmap,Width=18,Height=18,Stretch=Stretch.Uniform};
            }
            catch(Exception e){if(!(e is System.IO.IOException || e is NotSupportedException || e is ArgumentException || e is FormatException))throw;}
            return UiIcons.Image("snapshot",Ui.Muted);
        }
        void ShowGoal()
        {
            var dialog=new Window{Owner=host,WindowStartupLocation=WindowStartupLocation.CenterOwner};Ui.Window(dialog,"持续目标",470,320);dialog.ResizeMode=ResizeMode.NoResize;
            var body=new StackPanel{Margin=new Thickness(20)};body.Children.Add(Ui.Text("想持续推进什么？",18));body.Children.Add(Ui.Text("目标会随每次请求保留；清空即可取消。",12,Ui.Muted));var goal=Ui.Field(pet.Settings.AgentGoal,true);goal.Name="AgentGoalInput";goal.MaxLength=4000;goal.Height=115;goal.Margin=new Thickness(0,12,0,12);body.Children.Add(goal);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var clear=Ui.Button("清除目标");clear.Click+=delegate{goal.Clear();};buttons.Children.Add(clear);var save=Ui.Button("保存",true);save.Name="SaveAgentGoal";save.Click+=delegate{pet.Settings.AgentGoal=goal.Text.Trim();pet.SaveSettings();dialog.Close();};buttons.Children.Add(save);body.Children.Add(buttons);dialog.Content=body;dialog.ShowDialog();
        }
        async System.Threading.Tasks.Task CaptureApplication(ApplicationSnapshotTarget target)
        {
            if(Attachments.Count>=4){status.Text="最多添加 4 个附件，请先移除一项。";return;}
            string persona=pet.Skin.Id;loadingAttachments=true;RefreshAction();status.Text="正在获取应用快照…";
            try{byte[] png=await ApplicationSnapshots.CaptureAsync(target,System.Threading.CancellationToken.None);if(closed || pet.Skin.Id!=persona)return;AddAttachments(new[]{ChatAttachment.CreateImage("应用快照-"+DateTime.Now.ToString("HHmmss")+".png",png)});status.Text="应用快照已添加，发送前可以移除。";}
            catch(Exception e){if(!closed)status.Text=e is ApiException || e is InvalidOperationException?e.Message:"无法获取此应用快照，请尝试其他窗口。";}
            finally{loadingAttachments=false;if(!closed)RefreshAction();}
        }
        async System.Threading.Tasks.Task PickAttachments()
        {
            try{var paths=AttachmentPicker.Show(new System.Windows.Interop.WindowInteropHelper(host).Handle);if(paths.Length>0)await AddPathsAsync(paths);}catch(Exception e){Error(e);}
        }
        bool CanAdd(){return !closed && !loadingAttachments && !pet.IsUpdating && !pet.LiveActive && !dictation.IsBusy;}
        internal async System.Threading.Tasks.Task<bool> ImportDataObjectAsync(IDataObject data)
        {
            if(data.GetDataPresent(DataFormats.FileDrop)){var paths=data.GetData(DataFormats.FileDrop) as string[];if(paths==null)return false;await AddPathsAsync(paths);return true;}
            if(data.GetDataPresent(DataFormats.Bitmap)){var bitmap=data.GetData(DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource;if(bitmap==null)return false;await AddBitmapAsync(bitmap,"附加图片-");return true;}
            return false;
        }
        internal async System.Threading.Tasks.Task AddPathsAsync(string[] paths)
        {
            if(!CanAdd())return;string persona=pet.Skin.Id;loadingAttachments=true;RefreshAction();
            try
            {
                if(paths==null || paths.Length==0)return;
                if(paths.Length+Attachments.Count>4)throw new ApiException("最多添加 4 个附件。");
                var files=await System.Threading.Tasks.Task.Run(()=>paths.Select(path=>
                {
                    if(!System.IO.Directory.Exists(path))return ChatAttachment.Read(path);
                    string full=System.IO.Path.GetFullPath(path);string name=System.IO.Path.GetFileName(full.TrimEnd('\\','/'));if(String.IsNullOrEmpty(name))name="文件夹";
                    return ChatAttachment.FromText(name+"-文件夹.txt","附加文件夹："+full+"\n按用户要求读取需要的文件。此附件不会改变工作设置中的目录或权限。");
                }).ToList());
                if(!closed && pet.Skin.Id==persona)AddAttachments(files);
            }
            catch(Exception e){if(e is System.IO.IOException || e is UnauthorizedAccessException || e is System.IO.InvalidDataException || e is System.Xml.XmlException)status.Text="无法读取附件，请检查文件格式、占用及访问权限。";else Error(e);}
            finally{loadingAttachments=false;if(!closed)RefreshAction();}
        }
        async System.Threading.Tasks.Task AddBitmapAsync(System.Windows.Media.Imaging.BitmapSource source,string prefix)
        {
            if(!CanAdd())return;string persona=pet.Skin.Id;loadingAttachments=true;RefreshAction();
            try
            {
                var bitmap=source.Clone();bitmap.Freeze();
                byte[] png=await System.Threading.Tasks.Task.Run(delegate{var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var memory=new System.IO.MemoryStream()){encoder.Save(memory);return memory.ToArray();}});
                if(!closed && pet.Skin.Id==persona)AddAttachments(new[]{ChatAttachment.CreateImage(prefix+DateTime.Now.ToString("HHmmssfff")+".png",png)});
            }
            catch(Exception e){if(e is ApiException)Error(e);else status.Text="无法添加这张图片，请通过 + 选择图片文件重试。";}
            finally{loadingAttachments=false;if(!closed)RefreshAction();}
        }
        void RefreshAttachments()
        {
            if(predictionSuggestions!=null)predictionSuggestions.RefreshState();
            attachmentPersona=pet.Skin.Id;attachmentChips.Children.Clear();
            foreach(var file in Attachments)
            {
                var card=new Grid{Width=file.IsImage?116:146,Height=116};card.RowDefinitions.Add(new RowDefinition());card.RowDefinitions.Add(new RowDefinition{Height=new GridLength(27)});
                FrameworkElement visual=UiIcons.Image("file",Ui.Muted);visual.Width=visual.Height=27;visual.HorizontalAlignment=HorizontalAlignment.Center;visual.VerticalAlignment=VerticalAlignment.Center;
                if(file.IsImage)try{var bitmap=new System.Windows.Media.Imaging.BitmapImage();using(var stream=new System.IO.MemoryStream(Convert.FromBase64String(file.Data))){bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=240;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();}visual=new Image{Name="AttachmentThumbnail",Source=bitmap,Stretch=Stretch.UniformToFill};}catch(Exception e){if(!(e is System.IO.IOException || e is FormatException || e is NotSupportedException || e is ArgumentException))throw;}
                card.Children.Add(visual);var name=Ui.Text(file.Name,11);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new Thickness(7,4,7,3);Grid.SetRow(name,1);card.Children.Add(name);
                var remove=SmallText("×","RemoveAttachment");remove.ToolTip="移除 "+file.Name;remove.Width=remove.Height=remove.MinHeight=22;remove.Background=Brushes.White;remove.HorizontalAlignment=HorizontalAlignment.Right;remove.VerticalAlignment=VerticalAlignment.Top;remove.Margin=new Thickness(0,3,3,0);remove.Click+=delegate{Attachments.Remove(file);RefreshAttachments();RefreshAction();};card.Children.Add(remove);
                attachmentChips.Children.Add(new Border{Name="AttachmentCard",Child=card,Background=Ui.Brush("#F7F7F8"),BorderBrush=Ui.Brush("#E4E5E8"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,7,6),ClipToBounds=true,ToolTip=file.Name});
            }
            attachmentArea.Visibility=Attachments.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        async System.Threading.Tasks.Task InterruptDraft(bool force=false)
        {
            if((String.IsNullOrWhiteSpace(input.Text) && Attachments.Count==0) || loadingAttachments || pet.LiveActive || pet.IsUpdating)return;string text=input.Text;var files=ChatAttachment.Snapshot(Attachments);string persona=pet.Skin.Id;
            try{if(force)await queue.InterruptAsync(text,null,files,conversationOnly);else await queue.SteerAsync(text,null,files,conversationOnly);if(pet.Skin.Id==persona && input.Text==text)input.Clear();var draft=attachmentDrafts[persona];foreach(var file in files)draft.RemoveAll(f=>f.Name==file.Name && f.Data==file.Data && f.Text==file.Text);RefreshAttachments();}catch(Exception e){Error(e);}
        }
        internal void Detach(){closed=true;meter.Stop();if(subscribed){queue.Changed-=Refresh;pet.Live.StateChanged-=Refresh;dictation.Changed-=Refresh;subscribed=false;}if(dictation.Owns(this)){var ignored=dictation.CancelAsync(this);}}
        void Refresh()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Refresh));return;}
            RefreshAction();var items=queue.Items;
            string next=String.Join("|",items.Select(m=>m.Id+":"+m.Text+":"+m.IsGuidance))+"/"+queue.IsPaused+"/"+editingId;
            queueBox.Visibility=items.Count==0?Visibility.Collapsed:Visibility.Visible;
            queueLabel.Text=(queue.IsPaused?"已暂停 · ":"排队中 · ")+items.Count+" 条";
            resume.Visibility=queue.IsPaused && items.Count>0 && editingId==null?Visibility.Visible:Visibility.Collapsed;resume.IsEnabled=!pet.LiveActive;
            if(!String.IsNullOrEmpty(queue.Notice))status.Text=queue.Notice;
            RefreshDictation();
            if(signature==next)return;signature=next;reorder.Cancel();
            string draft=editor==null?null:editor.Text;rows.Children.Clear();editor=null;
            foreach(var item in items)
            {
                var captured=item;var row=new Grid{Name="QueuedMessageRow",Tag=item.Id,Margin=new Thickness(9,2,7,2),Background=Brushes.Transparent};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                if(item.Id==editingId)
                {
                    var editPanel=new StackPanel();editor=Ui.Field(draft??item.Text,true);editor.Name="QueueEditInput";editor.MaxLength=8000;editor.MinHeight=46;editor.MaxHeight=72;editor.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;editPanel.Children.Add(editor);
                    var editTools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var save=SmallText("保存","SaveQueuedEdit");save.Click+=delegate{Try(delegate{queue.Edit(captured.Id,editor.Text);EndEdit();});};editTools.Children.Add(save);var cancel=SmallText("取消","CancelQueuedEdit");cancel.Click+=delegate{EndEdit();};editTools.Children.Add(cancel);editPanel.Children.Add(editTools);Grid.SetColumnSpan(editPanel,3);row.Children.Add(editPanel);
                }
                else
                {
                    var leading=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(leading);
                    var grip=SmallText("","ReorderQueuedMessage");grip.Width=18;grip.Padding=new Thickness(2);grip.Cursor=Cursors.SizeAll;grip.ToolTip="拖动调整发送顺序 · Alt+↑/↓";grip.Opacity=0;grip.IsEnabled=editingId==null;
                    System.Windows.Automation.AutomationProperties.SetName(grip,"调整排队消息顺序："+item.Text);
                    var dots=new System.Windows.Controls.Primitives.UniformGrid{Rows=3,Columns=2,Width=8,Height=12};for(int dot=0;dot<6;dot++)dots.Children.Add(new System.Windows.Shapes.Ellipse{Width=2,Height=2,Fill=Ui.Muted});grip.Content=dots;leading.Children.Add(grip);
                    var queueArrow=UiIcons.Image("queue",Ui.Muted);queueArrow.Name="QueueMessageIcon";queueArrow.Width=queueArrow.Height=13;queueArrow.VerticalAlignment=VerticalAlignment.Center;queueArrow.Margin=new Thickness(1,0,2,0);leading.Children.Add(queueArrow);
                    row.MouseEnter+=delegate{grip.Opacity=1;};row.MouseLeave+=delegate{if(!grip.IsKeyboardFocusWithin && !grip.IsMouseCaptured)grip.Opacity=0;};grip.GotKeyboardFocus+=delegate{grip.Opacity=1;};grip.LostKeyboardFocus+=delegate{if(!row.IsMouseOver)grip.Opacity=0;};reorder.Attach(grip,row);
                    var text=Ui.Text((item.IsGuidance?"引导待接入 · ":"")+item.Text.Replace("\r"," ").Replace("\n"," "),12);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new Thickness(2,0,5,0);text.ToolTip=(item.IsGuidance?"当前操作结束后接入\n":"")+item.Text;Grid.SetColumn(text,1);row.Children.Add(text);
                    var tools=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(tools,2);row.Children.Add(tools);
                    var guide=SmallText("引导","GuideQueuedMessage");var guideLabel=new StackPanel{Orientation=Orientation.Horizontal};var guideArrow=UiIcons.Image("steer",Ui.Muted);guideArrow.Width=guideArrow.Height=12;guideArrow.Margin=new Thickness(0,0,3,0);guideArrow.VerticalAlignment=VerticalAlignment.Center;guideLabel.Children.Add(guideArrow);guideLabel.Children.Add(Ui.Text("引导",11,Ui.Muted));guide.Content=guideLabel;guide.ToolTip="当前操作完成后接入引导，不停止正在执行的工作";guide.Click+=async delegate{try{await queue.SteerAsync(captured.Text,captured.Id);}catch(Exception e){Error(e);}};tools.Children.Add(guide);
                    var remove=SmallIcon("trash","删除排队消息","RemoveQueuedMessage");remove.Click+=delegate{Try(()=>queue.Remove(captured.Id));};tools.Children.Add(remove);
                    var edit=SmallIcon("edit","编辑消息","EditQueuedMessage");edit.Click+=delegate{resumeAfterEdit=!queue.IsPaused;editRevision=queue.PauseRevision;editingId=captured.Id;queue.Hold();Refresh();if(editor!=null){editor.Focus();editor.SelectAll();}};tools.Children.Add(edit);
                }
                rows.Children.Add(row);
            }
            if(editingId!=null && !items.Any(m=>m.Id==editingId)){editingId=null;editor=null;}
        }
        void EndEdit(){editingId=null;editor=null;signature="";if(resumeAfterEdit && editRevision==queue.PauseRevision)Try(()=>queue.Resume());Refresh();}
        void RefreshAction()
        {
            if(attachmentPersona!=pet.Skin.Id)RefreshAttachments();
            RefreshMode();
            bool text=!String.IsNullOrWhiteSpace(input.Text) || Attachments.Count>0,busy=pet.DialogueBusy;string action=text?"send":busy?"stop":"live";
            string label=text?(busy || queue.Items.Count>0?"加入队列":"发送"):busy?"停止并暂停队列":"通话";
            primary.Tag=action;Hint(primary,label);
            primary.Content=UiIcons.Image(action=="stop"?"stop-solid":action,Brushes.White);((FrameworkElement)primary.Content).Width=18;((FrameworkElement)primary.Content).Height=18;
            primary.IsEnabled=!loadingAttachments && !pet.IsUpdating && (!pet.LiveActive || !text) && (text || busy || !dictation.IsBusy);options.Visibility=text && busy && !pet.LiveActive?Visibility.Visible:Visibility.Collapsed;
            addAttachment.IsEnabled=!loadingAttachments && !pet.IsUpdating && !pet.LiveActive && !dictation.IsBusy;
            Hint(addAttachment,"添加文件等内容"+(conversationOnly || !pet.Settings.AgentPlanMode?"":" · 计划模式"));
            record.IsEnabled=!pet.IsUpdating && !pet.LiveActive && !dictation.IsBusy && session.Stage!=ConversationStage.Recording;
        }
        void RefreshDictation()
        {
            bool own=dictation.Owns(this);normalSurface.Visibility=own?Visibility.Collapsed:Visibility.Visible;recordingSurface.Visibility=own?Visibility.Visible:Visibility.Collapsed;
            finishDictation.IsEnabled=stopDictation.IsEnabled=own && dictation.Stage==DictationStage.Recording && !finishing;
            if(own){status.Text=dictation.Stage==DictationStage.Recognizing?"正在识别…":"";if(!meter.IsEnabled)meter.Start();}else{meter.Stop();}
            string notice=dictation.LastNotice;
            if(!own && !String.IsNullOrEmpty(notice) && notice!=lastDictationNotice)status.Text=notice;
            lastDictationNotice=notice;
        }
        async System.Threading.Tasks.Task FinishDictation(bool send)
        {
            if(finishing || !dictation.Owns(this))return;finishing=true;RefreshDictation();
            try
            {
                string text=await dictation.FinishAsync(this);if(closed || String.IsNullOrWhiteSpace(text))return;
                input.Text=String.IsNullOrWhiteSpace(input.Text)?text:input.Text+Environment.NewLine+text;input.CaretIndex=input.Text.Length;input.Focus();
                status.Text=input.Text.Length>8000?"听写内容已保留，请缩短到 8000 字以内再发送。":"听写已填入输入框，可修改后发送。";
                if(send)Submit();
            }
            catch(OperationCanceledException){if(!closed)status.Text="已取消听写。";}catch(Exception e){if(!closed)Error(e);}
            finally{finishing=false;RefreshAction();RefreshDictation();}
        }
        static Button SmallText(string text,string name){var b=Ui.Button(text);b.Name=name;b.FontSize=11;b.MinHeight=26;b.Padding=new Thickness(6,2,6,2);b.Margin=new Thickness(0);b.Background=Brushes.Transparent;b.BorderBrush=Brushes.Transparent;b.Foreground=Ui.Muted;return b;}
        static void Hint(Button button,string text)
        {
            var hint=button.ToolTip as ToolTip;
            if(hint==null)
            {
                hint=new ToolTip{Template=hintTemplate,Foreground=Brushes.White,FontSize=12,PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Custom};
                hint.CustomPopupPlacementCallback=delegate(Size popup,Size target,Point offset){return new[]{new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point((target.Width-popup.Width)/2,-popup.Height-6),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)};};
                button.ToolTip=hint;ToolTipService.SetInitialShowDelay(button,350);
            }
            hint.Content=text;System.Windows.Automation.AutomationProperties.SetName(button,text);
        }
        static Button SmallIcon(string icon,string label,string name){var b=UiIcons.Button(icon,label);b.Name=name;b.Width=26;b.Height=26;b.MinHeight=26;b.Margin=new Thickness(0);b.Padding=new Thickness(4);var glyph=(FrameworkElement)b.Content;glyph.Width=glyph.Height=14;return b;}
        void Try(Action action){try{action();}catch(Exception e){Error(e);}}
        void Error(Exception e){status.Text=e is ApiException || e is InvalidOperationException?e.Message:e is OperationCanceledException?"已停止。":"操作未完成，消息已保留。请检查配置或数据目录。";}
    }
}
