using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed partial class ConversationComposer : StackPanel
    {
        static readonly ControlTemplate hintTemplate=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'><Border Background='#202329' CornerRadius='8' Padding='9,6'><ContentPresenter/></Border></ControlTemplate>");
        PredictionSuggestions predictionSuggestions;AgentChangeStatus changeStatus;AgentApprovalSurface approvals;
        readonly PetWindow pet;readonly Window host;readonly MarkdownEditor input;readonly TextBlock status,aiNotice;ConversationQueue queue;ConversationSession session;
        readonly Button primary,options;readonly Border queueBox;readonly StackPanel rows;readonly TextBlock queueLabel;readonly Button resume;
        readonly Button modelPicker;readonly TextBlock modelLabel;readonly ContextMenu modelMenu;
        readonly Button modePicker;readonly TextBlock modeLabel;readonly Border modeIcon;
        readonly QueueReorderController reorder;
        internal Func<ApplicationSnapshotTarget> SnapshotTargetProvider=ApplicationSnapshots.Topmost;
        readonly bool conversationOnly;
        readonly Button addAttachment;readonly WrapPanel attachmentChips=new WrapPanel();readonly ScrollViewer attachmentArea;
        readonly System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<ChatAttachment>> attachmentDrafts=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<ChatAttachment>>();
        string attachmentPersona;bool loadingAttachments;
        string AttachmentKey {get{return (session.KernelSession==null?session.GetHashCode().ToString():session.KernelSession.SessionId)+":"+session.PersonaId;}}
        System.Collections.Generic.List<ChatAttachment> Attachments {get{System.Collections.Generic.List<ChatAttachment> files;string id=AttachmentKey;if(!attachmentDrafts.TryGetValue(id,out files))attachmentDrafts[id]=files=new System.Collections.Generic.List<ChatAttachment>();return files;}}
        readonly DictationSession dictation;readonly Button record,finishDictation,stopDictation;readonly FrameworkElement normalSurface;readonly Border recordingSurface;readonly DictationWaveform waveform;
        readonly System.Windows.Threading.DispatcherTimer meter=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
        readonly System.Diagnostics.Stopwatch recordingTime=new System.Diagnostics.Stopwatch();bool finishing;
        string signature="",editingId,lastDictationNotice="";TextBox editor;bool resumeAfterEdit,closed,subscribed;int editRevision;
        PetSettings WorkSettings {get{return pet.ConversationSettings(session);}}
        internal Func<Action> PrepareSubmission;internal Action Submitted;
        FrameworkElement projectHeader;
        internal void SetProjectHeader(FrameworkElement header){if(projectHeader!=null)Children.Remove(projectHeader);projectHeader=header;if(header!=null)Children.Insert(Children.IndexOf(normalSurface),header);}
        void SetPlanMode(bool enabled){pet.SetConversationPlanMode(session,enabled);RefreshAction();}
        // "/" and "@" palettes share one popup and are filtered as you type.
        sealed class TriggerItem{public string Title,Hint,Icon,Group;public Action Action;}
        Popup triggerPopup;readonly StackPanel triggerList=new StackPanel{Margin=new Thickness(4)};
        readonly ScrollViewer triggerScroll=new ScrollViewer{MaxHeight=360,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false};
        readonly TextBlock triggerEmpty=Ui.Text(TextResources.Get("conversationComposer.triggerEmpty.text"),12,AgentUi.Muted);
        readonly System.Collections.Generic.List<TriggerItem> triggerItems=new System.Collections.Generic.List<TriggerItem>();
        string triggerQuery="";int triggerIndex;
        public bool HasEdit {get{return editingId!=null || Attachments.Count>0 || loadingAttachments;}}
        internal bool DismissPrediction(){return predictionSuggestions!=null && predictionSuggestions.DismissGhost();}
        public ConversationComposer(PetWindow pet,Window host,MarkdownEditor input,Button record,TextBlock status,bool compact,bool conversationOnly=false,ConversationSession sessionOverride=null,ConversationQueue queueOverride=null)
        {
            this.pet=pet;this.host=host;this.input=input;this.status=status;queue=queueOverride??pet.InputQueue;session=sessionOverride??pet.Conversation;dictation=pet.Dictation;this.record=record;this.conversationOnly=conversationOnly;
            predictionSuggestions=new PredictionSuggestions(pet,sessionOverride,queueOverride,input,host){AdditionalBlocker=()=>HasEdit};Children.Add(predictionSuggestions);
            // The running task's real change count sits directly above the queue
            // rows and the input box, and collapses outside a running task.
            if(!(host is ChatWindow)){changeStatus=new AgentChangeStatus(pet,host,sessionOverride);Children.Add(changeStatus);}
            var queueBody=new StackPanel();var heading=new DockPanel{Margin=new Thickness(10,4,7,1)};
            resume=SmallText(TextResources.Get("conversationComposer.conversationComposer.smallText"),"ResumeQueue");resume.Foreground=Ui.Blue;resume.Click+=delegate{Try(delegate{queue.Resume();});};DockPanel.SetDock(resume,Dock.Right);heading.Children.Add(resume);
            queueLabel=Ui.Text("",10,Ui.Muted);queueLabel.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(queueLabel);queueBody.Children.Add(heading);
            rows=new StackPanel();var list=Ui.Scroll(rows);list.MaxHeight=compact?112:160;queueBody.Children.Add(list);
            reorder=new QueueReorderController(rows,list,(id,before)=>queue.MoveBefore(id,before),Error);
            Unloaded+=delegate{reorder.Cancel();};
            IsVisibleChanged+=delegate{if(!IsVisible)reorder.Cancel();};
            queueBox=new Border{Child=queueBody,BorderBrush=Ui.Line,BorderThickness=new Thickness(1,1,1,0),CornerRadius=new CornerRadius(14,14,0,0),Margin=new Thickness(10,0,10,-10),Padding=new Thickness(0,0,0,14),Background=Ui.Brush("#FAFBFD")};Children.Add(queueBox);
            var controls=new StackPanel{Orientation=Orientation.Horizontal};record.Margin=new Thickness(0);record.Width=32;record.Height=32;record.MinHeight=32;
            options=UiIcons.Button("down",TextResources.Get("conversationComposer.conversationComposer.uiIcons"));options.Name="SendOptions";options.Width=22;options.MinWidth=0;options.Margin=new Thickness(0,0,3,0);options.Padding=new Thickness(2);controls.Children.Add(options);
            record.Margin=new Thickness(0,0,5,0);controls.Children.Add(record);
            var menu=ChatUi.Menu();var interrupt=new MenuItem{Header=TextResources.Get("conversationComposer.conversationComposer.header")};interrupt.Click+=async delegate{await InterruptDraft();};menu.Items.Add(interrupt);var force=new MenuItem{Header=TextResources.Get("conversationComposer.conversationComposer.header.alternativeA")};force.Click+=async delegate{await InterruptDraft(true);};menu.Items.Add(force);options.ContextMenu=menu;options.Click+=delegate{menu.PlacementTarget=options;menu.IsOpen=true;};
            primary=ChatUi.Action("send",TextResources.Get("conversationComposer.conversationComposer.chatUiAction"));primary.Name=compact?"SendQuickMessage":"SendMessage";primary.Width=primary.Height=primary.MinHeight=32;primary.Background=Ui.Brush("#357FF5");primary.BorderBrush=primary.Background;controls.Children.Add(primary);
            primary.Click+=async delegate
            {
                if(!String.IsNullOrWhiteSpace(input.Text) || Attachments.Count>0){Submit();return;}
                if(session.IsBusy || queue.IsRunning){try{await queue.PauseAsync();}catch(Exception e){Error(e);}return;}
                pet.ShowLiveCall(host);
            };
            modelLabel=Ui.InlineText("",12,Ui.Muted);modelLabel.Name="CurrentTextModel";modelLabel.TextWrapping=TextWrapping.NoWrap;modelLabel.TextTrimming=TextTrimming.CharacterEllipsis;modelLabel.VerticalAlignment=VerticalAlignment.Center;
            var modelContent=new DockPanel();var arrow=UiIcons.Image("down",Ui.Muted);arrow.Width=arrow.Height=12;arrow.Margin=new Thickness(7,0,0,0);arrow.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(arrow,Dock.Right);modelContent.Children.Add(arrow);modelContent.Children.Add(modelLabel);
            modelPicker=Ui.Button("");modelPicker.Name="TextModelPicker";modelPicker.Content=modelContent;modelPicker.MinHeight=32;modelPicker.MinWidth=0;modelPicker.MaxWidth=280;modelPicker.Padding=new Thickness(7,3,7,3);modelPicker.Margin=new Thickness(0,0,6,0);modelPicker.Background=Brushes.Transparent;modelPicker.BorderBrush=Brushes.Transparent;modelPicker.HorizontalAlignment=HorizontalAlignment.Right;
            modelPicker.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            modelMenu=ChatUi.Menu();modelMenu.MaxHeight=420;modelMenu.MaxWidth=460;modelMenu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;modelPicker.ContextMenu=modelMenu;
            modelPicker.Click+=delegate{OpenModels();};modelPicker.ContextMenuOpening+=delegate(object sender,ContextMenuEventArgs e){e.Handled=true;OpenModels();};
            modelPicker.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Down || e.Key==Key.F4){e.Handled=true;OpenModels();}};
            // The status line stays outside both surfaces while recording replaces
            // the composer body with a real microphone waveform and explicit controls.
            addAttachment=UiIcons.Button("plus",TextResources.Get("conversationComposer.conversationComposer.uiIcons.alternativeA"));addAttachment.Name="AddAttachment";addAttachment.Width=addAttachment.Height=addAttachment.MinHeight=32;addAttachment.Padding=new Thickness(5);addAttachment.Margin=new Thickness(0);addAttachment.Content=new TextBlock{Text="+",FontSize=23,Foreground=Ui.Ink,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            addAttachment.Click+=delegate{OpenAddMenu();};Hint(addAttachment,TextResources.Get("conversationComposer.conversationComposer.hint"));
            // Quick chat always uses personal conversation settings. Permissions
            // belong to the full work entry; Tab only accepts predictions.
            if(!conversationOnly)
            {
                modeLabel=Ui.Text("",13,Ui.Muted);modeLabel.Name="AgentPermissionLabel";modeLabel.Height=18;modeLabel.LineHeight=18;modeLabel.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;modeLabel.VerticalAlignment=VerticalAlignment.Center;
                var modeContent=new StackPanel{Orientation=Orientation.Horizontal};modeIcon=new Border{Name="ConversationModeGlyph",Width=16,Height=18,Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};modeContent.Children.Add(modeIcon);modeContent.Children.Add(modeLabel);
                modePicker=SmallText("","AgentPermissionPicker");modePicker.Content=modeContent;modePicker.Width=118;modePicker.Height=32;modePicker.MinHeight=32;modePicker.Padding=new Thickness(5,3,5,3);modePicker.HorizontalAlignment=HorizontalAlignment.Left;modePicker.VerticalAlignment=VerticalAlignment.Center;modePicker.Click+=delegate{OpenPermissions();};Hint(modePicker,TextResources.Get("conversationComposer.conversationComposer.hint.alternativeA"));
            }
            attachmentArea=new ScrollViewer{Content=attachmentChips,MaxHeight=compact?124:146,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Visibility=Visibility.Collapsed};
            var modelControls=CreateContextControls();
            var leading=new Grid{MinWidth=0};leading.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36)});leading.Children.Add(addAttachment);
            if(conversationOnly){leading.ColumnDefinitions.Add(new ColumnDefinition{MinWidth=0});Grid.SetColumn(modelControls,1);leading.Children.Add(modelControls);}
            else{leading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});leading.ColumnDefinitions.Add(new ColumnDefinition{MinWidth=0});Grid.SetColumn(modePicker,1);leading.Children.Add(modePicker);Grid.SetColumn(modelControls,2);leading.Children.Add(modelControls);}
            modelPicker.HorizontalAlignment=HorizontalAlignment.Right;modelPicker.VerticalAlignment=VerticalAlignment.Center;modelPicker.Margin=new Thickness(0,0,2,0);modelPicker.RenderTransform=new TranslateTransform(0,4);
            record.Padding=new Thickness(5);primary.Padding=new Thickness(0);options.Padding=new Thickness(1);
            normalSurface=ChatUi.Composer(input,leading,controls,new TextBlock{Visibility=Visibility.Collapsed},compact,attachmentArea);if(conversationOnly)ChatUi.GetComposerPlaceholder(input).Text=TextResources.Get("conversationComposer.conversationComposer.chatUiGetComposerPlaceholder");Children.Add(normalSurface);
            var recordingBody=new StackPanel();var recordingLabel=Ui.Text(TextResources.Get("conversationComposer.conversationComposer.text"),12,Ui.Muted);recordingLabel.MinHeight=48;recordingLabel.Padding=new Thickness(4,6,4,6);recordingBody.Children.Add(recordingLabel);var recordingControls=new DockPanel{Margin=new Thickness(2,8,2,0)};
            stopDictation=UiIcons.Button("stop-solid",TextResources.Get("conversationComposer.conversationComposer.uiIcons.alternativeB"));stopDictation.Name="StopDictation";stopDictation.Width=32;stopDictation.Height=32;stopDictation.MinHeight=32;stopDictation.Content=UiIcons.Image("stop-solid",Ui.Brush("#202329"));stopDictation.Margin=new Thickness(0,0,5,0);stopDictation.Click+=async delegate{await FinishDictation(false);};
            finishDictation=ChatUi.Action("send",TextResources.Get("conversationComposer.conversationComposer.chatUiAction.alternativeA"));finishDictation.Name="FinishDictation";finishDictation.Width=finishDictation.Height=finishDictation.MinHeight=32;finishDictation.Background=ThemeManager.ActionSurface;finishDictation.BorderBrush=finishDictation.Background;finishDictation.Content=UiIcons.Image("send",Brushes.White);finishDictation.Click+=async delegate{await FinishDictation(true);};DockPanel.SetDock(finishDictation,Dock.Right);recordingControls.Children.Add(finishDictation);
            DockPanel.SetDock(stopDictation,Dock.Right);recordingControls.Children.Add(stopDictation);
            waveform=new DictationWaveform{Height=32};recordingControls.Children.Add(waveform);recordingBody.Children.Add(recordingControls);
            recordingSurface=new Border{Child=recordingBody,Background=ThemeManager.Surface,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(22),Padding=new Thickness(8),Visibility=Visibility.Collapsed};Children.Add(recordingSurface);
            var footer=new Grid{Name="ComposerFooter",Margin=new Thickness(3,3,3,0)};footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});footer.ColumnDefinitions.Add(new ColumnDefinition());
            status.Margin=new Thickness(0);status.MinHeight=0;status.MaxHeight=28;status.TextWrapping=TextWrapping.NoWrap;status.TextTrimming=TextTrimming.CharacterEllipsis;status.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(status);
            status.SetBinding(TextBlock.ToolTipProperty,new System.Windows.Data.Binding("Text"){Source=status});
            aiNotice=Ui.Text("",10,Ui.Brush("#969BA2"));aiNotice.Name="AiAccuracyNotice";aiNotice.TextAlignment=TextAlignment.Right;aiNotice.TextWrapping=TextWrapping.NoWrap;aiNotice.TextTrimming=TextTrimming.CharacterEllipsis;aiNotice.LineHeight=12;aiNotice.Margin=new Thickness(8,0,0,0);aiNotice.VerticalAlignment=VerticalAlignment.Center;aiNotice.SetBinding(TextBlock.ToolTipProperty,new System.Windows.Data.Binding("Text"){Source=aiNotice});Grid.SetColumn(aiNotice,1);if(!conversationOnly)footer.Children.Add(aiNotice);Children.Add(footer);
            footer.SizeChanged+=delegate{status.MaxWidth=Math.Max(80,footer.ActualWidth*(conversationOnly?1:.4));};
            Hint(record,TextResources.Get("conversationComposer.conversationComposer.hint.alternativeB"));Hint(stopDictation,TextResources.Get("conversationComposer.conversationComposer.hint.alternativeC"));Hint(finishDictation,TextResources.Get("conversationComposer.conversationComposer.hint.alternativeD"));record.Click+=delegate{Try(delegate{dictation.Start(this);recordingTime.Restart();waveform.Clear();});};
            meter.Tick+=async delegate{if(!dictation.Owns(this)){meter.Stop();return;}waveform.Push(dictation.InputLevel);if(dictation.Stage==DictationStage.Recording && recordingTime.Elapsed.TotalSeconds>=60)await FinishDictation(false);};
            host.PreviewKeyDown+=async delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape && dictation.Owns(this)){e.Handled=true;try{await dictation.CancelAsync(this);}catch(Exception error){Error(error);}}};
            input.ToolTip=TextResources.Get("conversationComposer.conversationComposer.inputToolTip");
            input.TextChanged+=InputChanged;session.InputDraftChanged+=RestoreDraft;RestoreDraft();
            triggerPopup=new Popup{Name="ComposerTriggerMenu",Placement=System.Windows.Controls.Primitives.PlacementMode.Top,PlacementTarget=input,StaysOpen=false,AllowsTransparency=true};
            triggerScroll.Content=new StackPanel{Children={triggerList,triggerEmpty}};triggerPopup.Child=new Border{Child=triggerScroll,Background=ThemeManager.Surface,BorderBrush=Ui.Brush("#E6E8EC"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(5),Margin=new Thickness(2,2,2,7)};
            input.LostKeyboardFocus+=delegate{CloseTrigger();};input.SelectionChanged+=delegate{RefreshTrigger();};input.PreviewKeyDown+=async delegate(object sender,KeyEventArgs e)
            {
                if(HandleTriggerKey(e))return;
                if(e.Key==Key.Tab && Keyboard.Modifiers==ModifierKeys.None && !input.IsComposing && predictionSuggestions!=null && predictionSuggestions.TryAcceptGhost()){e.Handled=true;return;}
                if(e.Key!=Key.Enter || input.IsComposing || (Keyboard.Modifiers&ModifierKeys.Shift)!=0)return;bool control=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
                if(!control&&predictionSuggestions!=null&&predictionSuggestions.GhostActive){e.Handled=true;if(predictionSuggestions.AllowEnter())predictionSuggestions.TryAcceptGhost();return;}
                if(predictionSuggestions!=null&&predictionSuggestions.EnterHeld){e.Handled=true;return;}
                if(!pet.Settings.SendOnEnter && !control)return;e.Handled=true;
                if(predictionSuggestions!=null && !predictionSuggestions.AllowEnter())return;
                // Only a plain Enter confirms the ghost; Ctrl+Enter keeps its
                // dedicated steer / stop-and-send meaning.
                if((Keyboard.Modifiers&ModifierKeys.Control)==0 && predictionSuggestions!=null && predictionSuggestions.TryAcceptGhost())return;
                if(control && session.IsBusy){if(pet.Settings.FollowUpBehavior=="steer")Submit(true);else await InterruptDraft();}else Submit();
            };
            CommandManager.AddPreviewExecutedHandler(input,async delegate(object sender,ExecutedRoutedEventArgs e)
            {
                if(e.Command!=ApplicationCommands.Paste)return;
                try
                {
                    if(Clipboard.ContainsFileDropList()){var files=Clipboard.GetFileDropList().Cast<string>().ToArray();e.Handled=true;await ImportDataObjectAsync(new DataObject(DataFormats.FileDrop,files));}
                    else if(Clipboard.ContainsImage()){var picture=Clipboard.GetImage();if(picture==null)return;e.Handled=true;await ImportDataObjectAsync(new DataObject(DataFormats.Bitmap,picture));}
                }
                catch(Exception error){if(!(error is System.Runtime.InteropServices.ExternalException || error is UnauthorizedAccessException))throw;e.Handled=true;status.Text=TextResources.Get("conversationComposer.conversationComposer.status");}
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
            Loaded+=delegate{if(!subscribed){queue.Changed+=Refresh;session.ContextUsageChanged+=Refresh;pet.Live.StateChanged+=Refresh;dictation.Changed+=Refresh;subscribed=true;}Refresh();};
            host.IsVisibleChanged+=async delegate{if(!host.IsVisible && dictation.Owns(this))try{await dictation.CancelAsync(this);}catch(Exception e){Error(e);}};
            host.Closed+=HostClosed;
            BindApprovals();RefreshModels();Refresh();
        }
        void BindApprovals()
        {
            if(approvals!=null){Children.Remove(approvals);approvals.Dispose();}
            var bound=session;approvals=new AgentApprovalSurface(bound.ApprovalProvider as AgentApprovalController,r=>AgentApprovalSurface.Matches(bound,r));
            Children.Insert(Children.IndexOf(normalSurface),approvals);
        }
        internal void Rebind(ConversationSession nextSession,ConversationQueue nextQueue)
        {
            if(nextSession==null || nextQueue==null)throw new ArgumentNullException("session");CloseContextPreview();session.InputDraftChanged-=RestoreDraft;if(subscribed){queue.Changed-=Refresh;session.ContextUsageChanged-=Refresh;}session=nextSession;queue=nextQueue;session.InputDraftChanged+=RestoreDraft;if(subscribed){queue.Changed+=Refresh;session.ContextUsageChanged+=Refresh;}predictionSuggestions.Rebind(nextSession,nextQueue);if(changeStatus!=null)changeStatus.Rebind(nextSession);RestoreDraft();BindApprovals();RefreshModels();Refresh();
        }
        void InputChanged(object sender,TextChangedEventArgs args){if(closed)return;session.InputDraft=input.Text;RefreshAction();RefreshTrigger();}
        void RestoreDraft(){if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RestoreDraft));return;}input.Text=session.InputDraft;}
        public void RefreshModels()
        {
            aiNotice.Text=TextResources.Format("conversationComposer.refreshModels.petSkinName",pet.Skin.Name);
            if(predictionSuggestions!=null)predictionSuggestions.RefreshState();
            input.PlainTextMode=pet.Settings.PlainTextEditor;input.ToolTip=TextResources.Format("conversationComposer.refreshModels.toolTipChatUiSendHint",ChatUi.SendHint(pet.Settings));if(status.Text.StartsWith(TextResources.Get("conversationComposer.refreshModels.statusStartsWith")) || status.Text.StartsWith(TextResources.Get("conversationComposer.refreshModels.statusStartsWith.alternativeA")))status.Text=ChatUi.SendHint(pet.Settings);RefreshAction();
            RefreshContextUsage();pet.Settings.EnsureTextProviders();var provider=pet.Settings.TextProviders.FirstOrDefault(p=>p.Id==pet.Settings.ActiveTextProviderId);
            string model=pet.Settings.Text==null?"":pet.Settings.Text.Model;
            modelLabel.Text=String.IsNullOrWhiteSpace(model)?TextResources.Get("conversationComposer.refreshModels.modelLabelStringIsNullOrWhiteSpace"):provider==null?model:provider.DisplayName(model);
            string description=provider==null?modelLabel.Text:provider.Name+" · "+modelLabel.Text;
            modelPicker.ToolTip=TextResources.Format("conversationComposer.refreshModels.modelPickerToolTipDescription",description,(String.IsNullOrWhiteSpace(model) || modelLabel.Text==model?"":"\n"+model));System.Windows.Automation.AutomationProperties.SetName(modelPicker,TextResources.Format("conversationComposer.refreshModels.automationAutomationPropertiesSetName",description));
        }
        void RefreshMode()
        {
            var cfg=WorkSettings;bool work=cfg.AgentEnabled;var shownMode=AgentAccessPolicy.DisplayMode(cfg);
            if(modeLabel!=null)modeLabel.Text=AgentAccessPolicy.Label(cfg).Split('·')[0].Trim();
            var color=shownMode==AgentApprovalMode.Full?Ui.Brush("#F36C21"):AgentUi.Muted;
            if(modeLabel!=null){modeLabel.Foreground=color;modeLabel.RenderTransform=null;}
            if(modeIcon!=null){modeIcon.Child=PermissionGlyph(shownMode,color);modeIcon.Tag="permissions";}
            if(modePicker!=null){modePicker.Width=Double.NaN;modePicker.MaxWidth=170;modePicker.Padding=new Thickness(6,0,8,0);modePicker.Margin=new Thickness(0,0,8,0);modePicker.Background=Brushes.Transparent;modePicker.BorderBrush=Brushes.Transparent;modePicker.Visibility=work?Visibility.Visible:Visibility.Collapsed;Hint(modePicker,TextResources.Get("conversationComposer.refreshMode.hint"));System.Windows.Automation.AutomationProperties.SetName(modePicker,TextResources.Format("conversationComposer.refreshMode.automationAutomationPropertiesSetName",AgentAccessPolicy.Label(cfg)));}
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
            var configure=new MenuItem{Name="ConfigureTextModels",Header=TextResources.Get("conversationComposer.openModels.header")};configure.Click+=delegate{pet.ShowSettings(true);};modelMenu.Items.Add(configure);
            modelMenu.PlacementTarget=modelPicker;modelMenu.IsOpen=true;
        }
        async void Submit(bool opposite=false)
        {
            if((String.IsNullOrWhiteSpace(input.Text) && Attachments.Count==0) || loadingAttachments || pet.LiveActive || pet.IsUpdating)return;
            if(TryExecutePermissionCommand(input.Text)){input.Clear();return;}
            if(session.IsBusy && !opposite && pet.Settings.FollowUpBehavior=="steer"){await InterruptDraft();return;}
            Try(delegate{Action rollback=PrepareSubmission==null?null:PrepareSubmission();try{queue.Enqueue(input.Text,Attachments,conversationOnly);}catch{if(rollback!=null)rollback();throw;}if(Submitted!=null)Submitted();Attachments.Clear();RefreshAttachments();input.Clear();input.Focus();});
        }
        internal void AddAttachments(System.Collections.Generic.IEnumerable<ChatAttachment> files)
        {
            var combined=ChatAttachment.Snapshot(Attachments.Concat(files));Attachments.Clear();Attachments.AddRange(combined);RefreshAttachments();RefreshAction();
        }
        void OpenAddMenu()
        {
            var menu=ChatUi.Menu();menu.Items.Add(new MenuItem{Header=TextResources.Get("conversationComposer.openAddMenu.header"),IsEnabled=false,Foreground=Ui.Muted});
            var files=new MenuItem{Header=TextResources.Get("conversationComposer.openAddMenu.header.alternativeA"),Name="AddFilesAndFolders",Icon=UiIcons.Image("attachment",Ui.Muted)};
            files.Click+=async delegate{await PickAttachments();};menu.Items.Add(files);
            var fullChat=host as ChatWindow;if(!conversationOnly&&fullChat!=null){var terminal=new MenuItem{Header=TextResources.Get("conversationComposer.openAddMenu.header.alternativeB"),Name="OpenTerminal",Icon=UiIcons.Image("terminal",Ui.Muted)};terminal.Click+=delegate{fullChat.OpenTerminal();};menu.Items.Add(terminal);}
            var target=SnapshotTargetProvider();
            var snapshots=new MenuItem{Header=target==null?TextResources.Get("conversationComposer.openAddMenu.nullTargetAppName.alternativeA"):TextResources.Format("conversationComposer.openAddMenu.nullTargetAppName",target.AppName),Name="AddApplicationSnapshot",Icon=UiIcons.Image("snapshot",Ui.Muted),IsEnabled=target!=null,ToolTip=target==null?TextResources.Get("conversationComposer.openAddMenu.nullTargetTitle"):target.Title};
            if(target!=null){snapshots.Icon=SnapshotIcon(target);snapshots.Click+=async delegate{await CaptureApplication(target);};}menu.Items.Add(snapshots);
            if(!conversationOnly && WorkSettings.AgentEnabled)
            {
                var goal=new MenuItem{Header=AddMenuLabel(TextResources.Get("conversationComposer.openAddMenu.addMenuLabel"),String.IsNullOrWhiteSpace(WorkSettings.AgentGoal)?TextResources.Get("conversationComposer.openAddMenu.addMenuLabel.alternativeA"):WorkSettings.AgentGoal),Name="SetAgentGoal",Icon=UiIcons.Image("target",Ui.Muted),IsEnabled=!pet.DialogueBusy};goal.Click+=delegate{ShowGoal();};menu.Items.Add(goal);
                var plan=new MenuItem{Header=AddMenuLabel(TextResources.Get("conversationComposer.openAddMenu.addMenuLabel.alternativeB"),WorkSettings.AgentPlanMode?TextResources.Get("conversationComposer.openAddMenu.addMenuLabel.alternativeC"):TextResources.Get("conversationComposer.openAddMenu.addMenuLabel.alternativeD")),Name="AgentPlanMode",IsCheckable=true,IsChecked=WorkSettings.AgentPlanMode,IsEnabled=!pet.DialogueBusy};plan.Click+=delegate{SetPlanMode(plan.IsChecked);};menu.Items.Add(plan);
            }
            addAttachment.ContextMenu=menu;menu.PlacementTarget=addAttachment;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;menu.IsOpen=true;
        }
        static FrameworkElement AddMenuLabel(string title,string description)
        {var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(Ui.InlineText(title,13));var hint=Ui.InlineText(description.Replace("\r"," ").Replace("\n"," "),12,Ui.Brush("#A0A4AB"));hint.MaxWidth=210;hint.TextWrapping=TextWrapping.NoWrap;hint.TextTrimming=TextTrimming.CharacterEllipsis;hint.Margin=new Thickness(9,0,0,0);row.Children.Add(hint);return row;}
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
            var dialog=new Window{Owner=host,WindowStartupLocation=WindowStartupLocation.CenterOwner};Ui.Window(dialog,TextResources.Get("conversationComposer.showGoal.window"),470,320);dialog.ResizeMode=ResizeMode.NoResize;
            var body=new StackPanel{Margin=new Thickness(20)};body.Children.Add(Ui.Text(TextResources.Get("conversationComposer.showGoal.text"),18));body.Children.Add(Ui.Text(TextResources.Get("conversationComposer.showGoal.text.alternativeA"),12,Ui.Muted));var goal=Ui.Field(WorkSettings.AgentGoal,true);goal.Name="AgentGoalInput";goal.MaxLength=4000;goal.Height=115;goal.Margin=new Thickness(0,12,0,12);body.Children.Add(goal);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var clear=Ui.Button(TextResources.Get("conversationComposer.showGoal.text.alternativeB"));clear.Click+=delegate{goal.Clear();};buttons.Children.Add(clear);var save=Ui.Button(TextResources.Get("common.save"),true);save.Name="SaveAgentGoal";save.Click+=delegate{pet.SetConversationGoal(session,goal.Text.Trim());dialog.Close();};buttons.Children.Add(save);body.Children.Add(buttons);dialog.Content=body;dialog.ShowDialog();
        }
        async System.Threading.Tasks.Task CaptureApplication(ApplicationSnapshotTarget target)
        {
            if(Attachments.Count>=4){status.Text=TextResources.Get("conversationComposer.captureApplication.status");return;}
            string persona=pet.Skin.Id;loadingAttachments=true;RefreshAction();status.Text=TextResources.Get("conversationComposer.captureApplication.status.alternativeA");
            try{byte[] png=await ApplicationSnapshots.CaptureAsync(target,System.Threading.CancellationToken.None);if(closed || pet.Skin.Id!=persona)return;AddAttachments(new[]{ChatAttachment.CreateImage(TextResources.Format("conversationComposer.captureApplication.chatAttachmentCreateImage",DateTime.Now.ToString("HHmmss")),png)});status.Text=TextResources.Get("conversationComposer.captureApplication.status.alternativeB");}
            catch(Exception e){if(!closed)status.Text=e is ApiException || e is InvalidOperationException?e.Message:TextResources.Get("conversationComposer.captureApplication.invalidOperationExceptionEMessage");}
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
            if(data.GetDataPresent(DataFormats.Bitmap)){var bitmap=data.GetData(DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource;if(bitmap==null)return false;await AddBitmapAsync(bitmap,TextResources.Get("conversationComposer.importDataObjectAsync.addBitmapAsync"));return true;}
            return false;
        }
        internal async System.Threading.Tasks.Task AddPathsAsync(string[] paths)
        {
            if(!CanAdd())return;string draftKey=AttachmentKey;loadingAttachments=true;RefreshAction();
            System.Collections.Generic.List<ChatAttachment> files=null;Exception failure=null;
            try
            {
                if(paths!=null&&paths.Length>0){if(paths.Length+Attachments.Count>4)throw new ApiException(TextResources.Get("conversationComposer.addPathsAsync.text"));files=await System.Threading.Tasks.Task.Run(()=>paths.Select(path=>
                {
                    if(!System.IO.Directory.Exists(path))return ChatAttachment.Read(path);
                    string full=System.IO.Path.GetFullPath(path);string name=System.IO.Path.GetFileName(full.TrimEnd('\\','/'));if(String.IsNullOrEmpty(name))name=TextResources.Get("conversationComposer.addPathsAsync.name");
                    return ChatAttachment.FromText(TextResources.Format("conversationComposer.addPathsAsync.chatAttachmentFromText",name),TextResources.Format("conversationComposer.addPathsAsync.chatAttachmentFromText.alternativeA",full));
                }).ToList());}
            }
            catch(Exception error){failure=error;}
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                try{if(!closed){if(failure!=null){if(failure is System.IO.IOException||failure is UnauthorizedAccessException||failure is System.IO.InvalidDataException||failure is System.Xml.XmlException)status.Text=TextResources.Get("conversationComposer.addPathsAsync.status");else Error(failure);}else if(files!=null&&AttachmentKey==draftKey)AddAttachments(files);}}
                finally{loadingAttachments=false;if(!closed)RefreshAction();}
            }));
        }
        async System.Threading.Tasks.Task AddBitmapAsync(System.Windows.Media.Imaging.BitmapSource source,string prefix)
        {
            if(!CanAdd())return;string draftKey=AttachmentKey;loadingAttachments=true;RefreshAction();byte[] png=null;Exception failure=null;
            try{var bitmap=source.Clone();bitmap.Freeze();png=await System.Threading.Tasks.Task.Run(delegate{var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var memory=new System.IO.MemoryStream()){encoder.Save(memory);return memory.ToArray();}});}
            catch(Exception error){failure=error;}
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                try{if(!closed){if(failure!=null){if(failure is ApiException)Error(failure);else status.Text=TextResources.Get("conversationComposer.addBitmapAsync.status");}else if(AttachmentKey==draftKey)AddAttachments(new[]{ChatAttachment.CreateImage(prefix+DateTime.Now.ToString("HHmmssfff")+".png",png)});}}
                finally{loadingAttachments=false;if(!closed)RefreshAction();}
            }));
        }
        void RefreshAttachments()
        {
            if(predictionSuggestions!=null)predictionSuggestions.RefreshState();
            attachmentPersona=AttachmentKey;attachmentChips.Children.Clear();
            foreach(var file in Attachments)
            {
                var card=new Grid{Width=file.IsImage?116:146,Height=116};card.RowDefinitions.Add(new RowDefinition());card.RowDefinitions.Add(new RowDefinition{Height=new GridLength(27)});
                FrameworkElement visual=UiIcons.Image("file",Ui.Muted);visual.Width=visual.Height=27;visual.HorizontalAlignment=HorizontalAlignment.Center;visual.VerticalAlignment=VerticalAlignment.Center;
                if(file.IsImage)try{var bitmap=new System.Windows.Media.Imaging.BitmapImage();using(var stream=new System.IO.MemoryStream(Convert.FromBase64String(file.Data))){bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=240;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();}visual=new Image{Name="AttachmentThumbnail",Source=bitmap,Stretch=Stretch.UniformToFill};}catch(Exception e){if(!(e is System.IO.IOException || e is FormatException || e is NotSupportedException || e is ArgumentException))throw;}
                card.Children.Add(visual);var name=Ui.Text(file.Name,11);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new Thickness(7,4,7,3);Grid.SetRow(name,1);card.Children.Add(name);
                var remove=SmallText("×","RemoveAttachment");remove.ToolTip=TextResources.Format("conversationComposer.refreshAttachments.toolTipFileName",file.Name);remove.Width=remove.Height=remove.MinHeight=22;remove.Background=ThemeManager.Surface;remove.HorizontalAlignment=HorizontalAlignment.Right;remove.VerticalAlignment=VerticalAlignment.Top;remove.Margin=new Thickness(0,3,3,0);remove.Click+=delegate{Attachments.Remove(file);RefreshAttachments();RefreshAction();};card.Children.Add(remove);
                var attachmentCard=new Border{Name="AttachmentCard",Child=card,Background=Ui.Brush("#F7F7F8"),BorderBrush=Ui.Brush("#E4E5E8"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,7,6),ClipToBounds=true,ToolTip=file.Name};
                if(file.IsImage)
                {
                    attachmentCard.Focusable=true;attachmentCard.Cursor=Cursors.Hand;attachmentCard.ToolTip=TextResources.Format("conversationComposer.refreshAttachments.toolTipFileName.alternativeA",file.Name);System.Windows.Automation.AutomationProperties.SetName(attachmentCard,TextResources.Format("conversationComposer.refreshAttachments.automationAutomationPropertiesSetName",file.Name));
                    attachmentCard.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){e.Handled=true;ChatImagePreview.OpenAttachment(host,file);};
                    attachmentCard.KeyDown+=delegate(object sender,KeyEventArgs e){if(ReferenceEquals(e.OriginalSource,attachmentCard)&&(e.Key==Key.Enter||e.Key==Key.Space)){e.Handled=true;ChatImagePreview.OpenAttachment(host,file);}};
                }
                attachmentChips.Children.Add(attachmentCard);
            }
            attachmentArea.Visibility=Attachments.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        async System.Threading.Tasks.Task InterruptDraft(bool force=false)
        {
            if((String.IsNullOrWhiteSpace(input.Text) && Attachments.Count==0) || loadingAttachments || pet.LiveActive || pet.IsUpdating)return;string text=input.Text;var files=ChatAttachment.Snapshot(Attachments);string persona=pet.Skin.Id;string attachmentKey=AttachmentKey;
            try{if(force)await queue.InterruptAsync(text,null,files,conversationOnly);else await queue.SteerAsync(text,null,files,conversationOnly);if(pet.Skin.Id==persona && input.Text==text)input.Clear();var draft=attachmentDrafts[attachmentKey];foreach(var file in files)draft.RemoveAll(f=>f.Name==file.Name && f.Data==file.Data && f.Text==file.Text);RefreshAttachments();}catch(Exception e){Error(e);}
        }
        // ---- "/" and "@" palettes -------------------------------------------------
        void RefreshTrigger()
        {
            if(closed || triggerPopup==null)return;
            int caret=input.CaretIndex;if(caret<0)caret=input.Text.Length;
            string before=input.Text.Substring(0,Math.Min(caret,input.Text.Length));
            int start=before.Length;string query="";char kind='\0';
            for(int i=before.Length-1;i>=0;i--)
            {
                char c=before[i];
                if(c=='/' || c=='@')
                {
                    if(i>0 && !Char.IsWhiteSpace(before[i-1]))break;
                    kind=c;start=i+1;query=before.Substring(start);break;
                }
                if(Char.IsWhiteSpace(c))break;
            }
            if(kind=='\0'){CloseTrigger();return;}
            triggerQuery=query;
            if(triggerPopup.IsOpen && triggerCacheKind==kind)RenderTrigger();else OpenTrigger(kind);
        }
        char triggerCacheKind;
        void OpenTrigger(char kind)
        {
            triggerCacheKind=kind;triggerItems.Clear();
            if(kind=='@')BuildAtItems();else BuildSlashItems();
            var chatHost=host as ChatWindow;if(!conversationOnly&&chatHost!=null&&chatHost.CanChooseProject)triggerItems.Insert(0,new TriggerItem{Title=TextResources.Get("conversationComposer.openTrigger.title"),Hint=TextResources.Get("conversationComposer.openTrigger.hint"),Icon="folder",Group=kind=='@'?TextResources.Get("conversationComposer.openTrigger.groupKind"):TextResources.Get("conversationComposer.openTrigger.groupKind.alternativeA"),Action=chatHost.OpenProjectSelection});
            foreach(var item in triggerItems)if(item.Group==null)item.Group=kind=='@'?TextResources.Get("conversationComposer.openTrigger.itemGroupKind"):TextResources.Get("conversationComposer.openTrigger.itemGroupKind.alternativeA");
            ((FrameworkElement)triggerPopup.Child).Width=Math.Max(260,Math.Min(620,normalSurface.ActualWidth-4));
            triggerIndex=0;RenderTrigger();triggerPopup.IsOpen=true;
        }
        readonly System.Collections.Generic.List<Border> triggerRows=new System.Collections.Generic.List<Border>();
        readonly System.Collections.Generic.List<TriggerItem> triggerMatches=new System.Collections.Generic.List<TriggerItem>();
        static bool TriggerMatch(TriggerItem item,string query){return query.Length==0 || (item.Title??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 || (item.Hint??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;}
        void RenderTrigger()
        {
            string query=triggerQuery??"";
            triggerMatches.Clear();foreach(var item in triggerItems)if(TriggerMatch(item,query))triggerMatches.Add(item);
            if(triggerIndex>=triggerMatches.Count)triggerIndex=Math.Max(0,triggerMatches.Count-1);
            triggerList.Children.Clear();triggerRows.Clear();triggerEmpty.Visibility=triggerMatches.Count==0?Visibility.Visible:Visibility.Collapsed;
            string group=null;for(int i=0;i<triggerMatches.Count;i++)
            {
                var item=triggerMatches[i];
                if(group!=item.Group){group=item.Group;var heading=Ui.Text(group,11,Ui.Brush("#969BA3"));heading.Name="ComposerCommandGroup";heading.Margin=new Thickness(9,8,9,6);triggerList.Children.Add(heading);}
                var row=new Grid{Name="ComposerCommand",Cursor=item.Action==null?Cursors.Arrow:Cursors.Hand,MinHeight=28,IsEnabled=item.Action!=null};
                row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());
                var icon=UiIcons.Image(item.Icon??"chat",AgentUi.Muted);icon.Width=icon.Height=15;icon.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(icon);
                var title=Ui.InlineText(item.Title??"",12,item.Action==null?Ui.Brush("#BABDC2"):AgentUi.Ink);title.MaxWidth=150;title.TextTrimming=TextTrimming.CharacterEllipsis;title.VerticalAlignment=VerticalAlignment.Center;title.Margin=new Thickness(2,0,10,0);Grid.SetColumn(title,1);row.Children.Add(title);
                var hint=Ui.InlineText(item.Hint??"",11,Ui.Brush("#A0A4AB"));hint.TextTrimming=TextTrimming.CharacterEllipsis;hint.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(hint,2);row.Children.Add(hint);
                var captured=item;int index=i;
                // Only the highlight moves: rebuilding the list here would yank the
                // row from under the pointer and make the menu flicker.
                row.MouseEnter+=delegate{SelectTrigger(index);};
                row.MouseLeftButtonUp+=delegate{CommitTrigger(captured);};
                var border=new Border{Child=row,CornerRadius=new CornerRadius(7),Padding=new Thickness(8,4,8,4),Margin=new Thickness(1,0,1,0)};
                triggerRows.Add(border);triggerList.Children.Add(border);
            }
            SelectTrigger(triggerIndex);
        }
        void SelectTrigger(int index)
        {
            if(triggerRows.Count==0)return;
            if(index<0)index=0;if(index>=triggerRows.Count)index=triggerRows.Count-1;
            triggerIndex=index;
            for(int i=0;i<triggerRows.Count;i++)triggerRows[i].Background=i==triggerIndex&&triggerMatches[i].Action!=null?Ui.Brush("#F0F1F2"):Brushes.Transparent;
            if(triggerPopup.IsOpen&&triggerIndex>=0&&triggerIndex<triggerRows.Count)triggerRows[triggerIndex].BringIntoView();
        }
        void SelectNextEnabledTrigger(int step){for(int i=1;i<=triggerMatches.Count;i++){int next=(triggerIndex+step*i+triggerMatches.Count)%triggerMatches.Count;if(triggerMatches[next].Action!=null){SelectTrigger(next);return;}}}
        void CommitTrigger(TriggerItem item)
        {
            if(item==null || item.Action==null)return;
            int caret=Math.Min(input.CaretIndex,input.Text.Length);string text=input.Text;int cut=caret;
            for(int i=caret-1;i>=0;i--){char c=text[i];if((c=='/'||c=='@')&&(i==0||Char.IsWhiteSpace(text[i-1]))){cut=i;break;}if(Char.IsWhiteSpace(c))break;}
            CloseTrigger();input.Text=text.Substring(0,cut)+text.Substring(caret);input.CaretIndex=cut;
            try{if(item.Action!=null)item.Action();}catch(Exception e){Error(e);}
        }
        void CloseTrigger(){if(triggerPopup!=null)triggerPopup.IsOpen=false;triggerItems.Clear();triggerMatches.Clear();triggerRows.Clear();triggerQuery="";}
        bool HandleTriggerKey(KeyEventArgs e)
        {
            if(triggerPopup==null || !triggerPopup.IsOpen)return false;
            var matches=triggerMatches;
            if(e.Key==Key.Escape){e.Handled=true;CloseTrigger();return true;}
            if(e.Key==Key.Down){e.Handled=true;if(matches.Count>0)SelectNextEnabledTrigger(1);return true;}
            if(e.Key==Key.Up){e.Handled=true;if(matches.Count>0)SelectNextEnabledTrigger(-1);return true;}
            if(e.Key==Key.Tab || (e.Key==Key.Enter && (Keyboard.Modifiers&ModifierKeys.Shift)==0))
            {
                if(matches.Count==0)return false;
                e.Handled=true;var item=matches[Math.Min(triggerIndex,matches.Count-1)];
                CommitTrigger(item);return true;
            }
            return false;
        }
        void BuildAtItems()
        {
            triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildAtItems.title"),Hint=TextResources.Get("conversationComposer.buildAtItems.hint"),Icon="attachment",Action=delegate{var ignored=PickAttachments();}});
            var target=SnapshotTargetProvider();
            triggerItems.Add(new TriggerItem{Title=target==null?TextResources.Get("conversationComposer.buildAtItems.nullTargetAppName.alternativeA"):TextResources.Format("conversationComposer.buildAtItems.nullTargetAppName",target.AppName),Hint=target==null?TextResources.Get("conversationComposer.buildAtItems.nullTargetTitle"):target.Title,Icon="snapshot",Action=target==null?(Action)null:delegate{var ignored=CaptureApplication(target);}});
            if(!conversationOnly && WorkSettings.AgentEnabled)
            {
                triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildAtItems.title.alternativeA"),Hint=String.IsNullOrWhiteSpace(WorkSettings.AgentGoal)?TextResources.Get("conversationComposer.buildAtItems.hintStringIsNullOrWhiteSpace"):WorkSettings.AgentGoal,Icon="target",Action=ShowGoal});
                triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildAtItems.title.alternativeB"),Hint=WorkSettings.AgentPlanMode?TextResources.Get("conversationComposer.buildAtItems.hintWorkSettingsAgentPlanMode"):TextResources.Get("conversationComposer.buildAtItems.hintWorkSettingsAgentPlanMode.alternativeA"),Icon="edit",Action=delegate{SetPlanMode(!WorkSettings.AgentPlanMode);}});
            }
            triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildAtItems.title.alternativeC"),Hint=TextResources.Get("conversationComposer.buildAtItems.hint.alternativeA"),Icon="settings",Group=TextResources.Get("conversationComposer.buildAtItems.group"),Action=delegate{pet.ShowSettings(true);}});
            triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildAtItems.title.alternativeD"),Hint=TextResources.Get("conversationComposer.buildAtItems.hint.alternativeB"),Icon="settings",Group=TextResources.Get("conversationComposer.buildAtItems.group"),Action=delegate{pet.ShowWorkSettings();}});
        }
        void BuildSlashItems()
        {
            triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint"),Icon="chat",Action=delegate{pet.NewActiveConversation();}});
            if(!conversationOnly)
            {
                if(WorkSettings.AgentEnabled){triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeA"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeA"),Icon="target",Action=OpenPermissions});triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeB"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeB"),Icon="target",Action=delegate{status.Text=AgentAccessPolicy.Label(WorkSettings);}});}
                if(WorkSettings.AgentEnabled)
                {
                    triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeC"),Hint=WorkSettings.AgentPlanMode?TextResources.Get("conversationComposer.buildSlashItems.hintWorkSettingsAgentPlanMode"):TextResources.Get("conversationComposer.buildSlashItems.hintWorkSettingsAgentPlanMode.alternativeA"),Icon="target",Action=delegate{SetPlanMode(!WorkSettings.AgentPlanMode);}});
                    triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeD"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeC"),Icon="target",Action=ShowGoal});
                }
            }
            var cfg=WorkSettings;
            if(!conversationOnly&&cfg.AgentEnabled&&AgentCapabilityResolver.Resolve(cfg,null).SupportsTools)
            {
                try{var projectDirectories=cfg.AgentTrustProjectInstructions?new[]{System.IO.Path.Combine(cfg.AgentWorkspace,".agents","skills"),cfg.AgentSkillsDirectory}:new string[0];IEnumerable<AgentSkillInfo> installedSkills=null;if(!String.IsNullOrWhiteSpace(ManagedSkillLibrary.ActiveDirectory))installedSkills=new ManagedSkillLibrary(ManagedSkillLibrary.ActiveDirectory).List().Where(skill=>skill.Enabled).Select(skill=>new AgentSkillInfo{Name=skill.Name,Description=skill.Description,Directory=skill.Directory});var catalog=new AgentSkills(projectDirectories,installedSkills);foreach(var entry in catalog.Catalog){var name=entry.Name;triggerItems.Add(new TriggerItem{Group=TextResources.Get("conversationComposer.buildSlashItems.group"),Title=name,Hint=entry.Description,Icon="file",Action=delegate{input.Text=TextResources.Format("conversationComposer.buildSlashItems.inputName",name);input.CaretIndex=input.Text.Length;input.Focus();}});}}
                catch(Exception e){status.Text=TextResources.Format("conversationComposer.buildSlashItems.statusEMessage",e.Message);}
            }
            triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeE"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeD"),Icon="settings",Action=OpenModels});
            triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeF"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeE"),Icon="mic",Action=delegate{Try(delegate{dictation.Start(this);recordingTime.Restart();waveform.Clear();});}});
            triggerItems.Add(new TriggerItem{Group=TextResources.Get("conversationComposer.buildSlashItems.group.alternativeA"),Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeG"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeF"),Icon="trash",Action=delegate{Try(delegate{session.Clear();status.Text=TextResources.Get("conversationComposer.buildSlashItems.status");});}});
            triggerItems.Add(new TriggerItem{Group=TextResources.Get("conversationComposer.buildSlashItems.group.alternativeA"),Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeH"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeG"),Icon="file",Action=delegate{status.Text=TextResources.Get("conversationComposer.buildSlashItems.status.alternativeA");}});
            triggerItems.Add(new TriggerItem{Title=TextResources.Get("conversationComposer.buildSlashItems.title.alternativeI"),Hint=TextResources.Get("conversationComposer.buildSlashItems.hint.alternativeH"),Icon="settings",Action=delegate{pet.ShowSettings(true);}});
        }
        internal void Detach()
        {
            if(closed)return;closed=true;input.TextChanged-=InputChanged;session.InputDraftChanged-=RestoreDraft;meter.Stop();reorder.Cancel();CloseTrigger();DetachContextPreview();
            if(predictionSuggestions!=null)predictionSuggestions.Dispose();
            if(changeStatus!=null)changeStatus.Dispose();
            if(approvals!=null)approvals.Dispose();
            if(subscribed)
            {
                queue.Changed-=Refresh;session.ContextUsageChanged-=Refresh;
                pet.Live.StateChanged-=Refresh;dictation.Changed-=Refresh;subscribed=false;
            }
            host.Closed-=HostClosed;CancelOwnedDictation();
        }
        void HostClosed(object sender,EventArgs args){Detach();}
        async void CancelOwnedDictation()
        {
            if(!dictation.Owns(this))return;
            try{await dictation.CancelAsync(this);}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception error){Error(error);}
        }
        void Refresh()
        {
            if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Refresh));return;}
            RefreshAction();var items=queue.Items.Where(m=>!m.IsGuidance || (queue.IsPaused && !String.IsNullOrEmpty(queue.Notice))).ToList();
            string next=String.Join("|",items.Select(m=>m.Id+":"+m.Text+":"+m.IsGuidance))+"/"+queue.IsPaused+"/"+editingId;
            queueBox.Visibility=items.Count==0?Visibility.Collapsed:Visibility.Visible;
            queueLabel.Text=(items.Count>0 && items.All(m=>m.IsGuidance)?TextResources.Get("conversationComposer.refresh.queueLabel"):queue.IsPaused?TextResources.Get("conversationComposer.refresh.queueLabel.alternativeA"):TextResources.Get("conversationComposer.refresh.queueLabel.alternativeB"))+items.Count+TextResources.Get("conversationComposer.refresh.queueLabel.alternativeC");
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
                    var editTools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var save=SmallText(TextResources.Get("common.save"),"SaveQueuedEdit");save.Click+=delegate{Try(delegate{queue.Edit(captured.Id,editor.Text);EndEdit();});};editTools.Children.Add(save);var cancel=SmallText(TextResources.Get("common.cancel"),"CancelQueuedEdit");cancel.Click+=delegate{EndEdit();};editTools.Children.Add(cancel);editPanel.Children.Add(editTools);Grid.SetColumnSpan(editPanel,3);row.Children.Add(editPanel);
                }
                else
                {
                    var leading=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(leading);
                    var grip=SmallText("","ReorderQueuedMessage");grip.Width=18;grip.Padding=new Thickness(2);grip.Cursor=Cursors.SizeAll;grip.ToolTip=TextResources.Get("conversationComposer.refresh.gripToolTip");grip.Opacity=0;grip.IsEnabled=items.Count>1 && editingId==null;grip.Visibility=items.Count>1?Visibility.Visible:Visibility.Collapsed;
                    System.Windows.Automation.AutomationProperties.SetName(grip,TextResources.Format("conversationComposer.refresh.automationAutomationPropertiesSetName",item.Text));
                    var dots=new System.Windows.Controls.Primitives.UniformGrid{Rows=3,Columns=2,Width=8,Height=12};for(int dot=0;dot<6;dot++)dots.Children.Add(new System.Windows.Shapes.Ellipse{Width=2,Height=2,Fill=Ui.Muted});grip.Content=dots;leading.Children.Add(grip);
                    var queueArrow=UiIcons.Image("queue",Ui.Muted);queueArrow.Name="QueueMessageIcon";queueArrow.Width=queueArrow.Height=13;queueArrow.VerticalAlignment=VerticalAlignment.Center;queueArrow.Margin=new Thickness(1,0,2,0);leading.Children.Add(queueArrow);
                    row.MouseEnter+=delegate{grip.Opacity=1;};row.MouseLeave+=delegate{if(!grip.IsKeyboardFocusWithin && !grip.IsMouseCaptured)grip.Opacity=0;};grip.GotKeyboardFocus+=delegate{grip.Opacity=1;};grip.LostKeyboardFocus+=delegate{if(!row.IsMouseOver)grip.Opacity=0;};reorder.Attach(grip,row);
                    var text=Ui.InlineText((item.IsGuidance?TextResources.Get("conversationComposer.refresh.inlineText"):"")+item.Text.Replace("\r"," ").Replace("\n"," "),12);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new Thickness(2,0,5,0);text.ToolTip=(item.IsGuidance?TextResources.Get("conversationComposer.refresh.textToolTip"):"")+item.Text;Grid.SetColumn(text,1);row.Children.Add(text);
                    var tools=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(tools,2);row.Children.Add(tools);
                    var guide=SmallText(TextResources.Get("conversationComposer.refresh.smallText"),"GuideQueuedMessage");var guideLabel=new StackPanel{Orientation=Orientation.Horizontal};var guideArrow=UiIcons.Image("steer",Ui.Muted);guideArrow.Width=guideArrow.Height=12;guideArrow.Margin=new Thickness(0,0,3,0);guideArrow.VerticalAlignment=VerticalAlignment.Center;guideLabel.Children.Add(guideArrow);guideLabel.Children.Add(Ui.InlineText(TextResources.Get("conversationComposer.refresh.inlineText.alternativeA"),11,Ui.Muted));guide.Content=guideLabel;guide.ToolTip=TextResources.Get("conversationComposer.refresh.guideToolTip");guide.Click+=async delegate{try{await queue.SteerAsync(captured.Text,captured.Id);}catch(Exception e){Error(e);}};tools.Children.Add(guide);
                    var remove=SmallIcon("trash",TextResources.Get("conversationComposer.refresh.smallIcon"),"RemoveQueuedMessage");remove.Click+=delegate{Try(()=>queue.Remove(captured.Id));};tools.Children.Add(remove);
                    var edit=SmallIcon("edit",TextResources.Get("conversationComposer.refresh.smallIcon.alternativeA"),"EditQueuedMessage");edit.Click+=delegate{resumeAfterEdit=!queue.IsPaused;editRevision=queue.PauseRevision;editingId=captured.Id;queue.Hold();Refresh();if(editor!=null){editor.Focus();editor.SelectAll();}};tools.Children.Add(edit);
                }
                rows.Children.Add(row);
            }
            if(editingId!=null && !items.Any(m=>m.Id==editingId)){editingId=null;editor=null;}
        }
        void EndEdit(){editingId=null;editor=null;signature="";if(resumeAfterEdit && editRevision==queue.PauseRevision)Try(()=>queue.Resume());Refresh();}
        void RefreshAction()
        {
            if(attachmentPersona!=AttachmentKey)RefreshAttachments();
            input.PlainTextMode=pet.Settings.PlainTextEditor;RefreshMode();RefreshContextUsage();
            bool text=!String.IsNullOrWhiteSpace(input.Text) || Attachments.Count>0,busy=session.IsBusy || queue.IsRunning;string action=text?"send":busy?"stop":"live";
            string label=text?(busy && pet.Settings.FollowUpBehavior=="steer"?TextResources.Get("conversationComposer.refreshAction.labelText"):busy || queue.Items.Count>0?TextResources.Get("conversationComposer.refreshAction.labelText.alternativeA"):TextResources.Get("conversationComposer.refreshAction.labelText.alternativeB")):busy?TextResources.Get("conversationComposer.refreshAction.labelText.alternativeC"):TextResources.Get("conversationComposer.refreshAction.labelText.alternativeD");
            primary.Tag=action;Hint(primary,label);
            primary.Content=UiIcons.Image(action=="stop"?"stop-solid":action,Brushes.White);((FrameworkElement)primary.Content).Width=18;((FrameworkElement)primary.Content).Height=18;
            primary.IsEnabled=!loadingAttachments && !pet.IsUpdating && (!pet.LiveActive || !text) && (text || busy || !dictation.IsBusy);options.Visibility=text && busy && !pet.LiveActive?Visibility.Visible:Visibility.Collapsed;
            addAttachment.IsEnabled=!loadingAttachments && !pet.IsUpdating && !pet.LiveActive && !dictation.IsBusy;
            Hint(addAttachment,TextResources.Get("conversationComposer.refreshAction.hint")+(conversationOnly || !WorkSettings.AgentPlanMode?"":TextResources.Get("conversationComposer.refreshAction.hint.alternativeA")));
            record.IsEnabled=!pet.IsUpdating && !pet.LiveActive && !dictation.IsBusy && session.Stage!=ConversationStage.Recording;
        }
        void RefreshDictation()
        {
            bool own=dictation.Owns(this);normalSurface.Visibility=own?Visibility.Collapsed:Visibility.Visible;recordingSurface.Visibility=own?Visibility.Visible:Visibility.Collapsed;
            finishDictation.IsEnabled=stopDictation.IsEnabled=own && dictation.Stage==DictationStage.Recording && !finishing;
            if(own){status.Text=dictation.Stage==DictationStage.Recognizing?TextResources.Get("conversationComposer.refreshDictation.stageDictationStageRecognizing"):"";if(!meter.IsEnabled)meter.Start();}else{meter.Stop();}
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
                status.Text=input.Text.Length>8000?TextResources.Get("conversationComposer.finishDictation.statusInputLength"):TextResources.Get("conversationComposer.finishDictation.statusInputLength.alternativeA");
                if(send)Submit();
            }
            catch(OperationCanceledException){if(!closed)status.Text=TextResources.Get("conversationComposer.finishDictation.status");}catch(Exception e){if(!closed)Error(e);}
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
        void Error(Exception e){status.Text=e is ApiException || e is InvalidOperationException?e.Message:e is OperationCanceledException?TextResources.Get("conversationComposer.error.eIsOperationCanceledException"):TextResources.Get("conversationComposer.error.eIsOperationCanceledException.alternativeA");}
    }
}
