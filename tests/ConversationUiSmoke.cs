using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ConversationUiSmoke
    {
        static int checks;
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"conversation-ui");Directory.CreateDirectory(folder);
            try
            {
                var cfg=new PetSettings{AgentEnabled=false,SaveChatHistory=false,GreetingEnabled=false,HideOnFullscreen=false,ReadReplies=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                cfg.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9876/v1",Model="GPT-6.1 Sol",StreamReplies=true};
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();Pump();
                pet.Conversation.Dispose();var api=new Model();var session=new ConversationSession(api,new Silent(),()=>{throw new Exception("No microphone");});session.SelectPersona(pet.Skin.Id);
                typeof(PetWindow).GetField("conversation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,session);
                session.AddLiveTurn(pet.Skin.Id,"这是一段测试发言","收到，测试消息正常。");
                var full=new ChatWindow(pet);full.Show();var quick=new QuickChatWindow(pet);quick.Show();Pump();
                Check(ReferenceEquals(quick.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)],full.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]),"quick chat reuses the full window's slim scrollbar style");
                Check(All<MarkdownView>(quick).Any(t=>t.Text=="这是一段测试发言"),"quick chat displays user text");
                foreach(Window window in new Window[]{full,quick})
                {
                    var user=All<Border>(window).Single(b=>b.Name=="UserMessage");var history=All<ScrollViewer>(window).First(s=>s.Name.EndsWith("HistoryScroll"));
                    Check(user.HorizontalAlignment==HorizontalAlignment.Right && user.ActualWidth<=history.ViewportWidth*.51,"user bubble occupies at most half the history");
                    var assistant=All<Border>(window).First(b=>b.Name=="AssistantMessage");Check(assistant.BorderThickness.Left==0 && (assistant.Background==null || assistant.Background==Brushes.Transparent),"assistant has no bubble");
                    var add=All<Button>(window).Single(b=>b.Name=="AddAttachment");var send=All<Button>(window).Single(b=>b.Name== (window==quick?"SendQuickMessage":"SendMessage"));
                    var surface=All<Border>(window).Single(b=>b.Name=="ComposerSurface");var a=add.TranslatePoint(new Point(0,16),surface);var sendOrigin=send.TranslatePoint(new Point(0,16),surface);
                    Check(Math.Abs(a.Y-sendOrigin.Y)<1 && sendOrigin.X+send.ActualWidth<=surface.ActualWidth-6,"plus and primary align and fit inside composer");
                    var model=All<Button>(window).Single(x=>x.Name=="TextModelPicker");var microphone=All<Button>(window).Single(x=>x.Name=="VoiceRecord");double gap=microphone.TranslatePoint(new Point(),surface).X-model.TranslatePoint(new Point(model.ActualWidth,0),surface).X;Check(gap>=0 && gap<=6,"model picker sits directly beside dictation icon");
                    var icon=(FrameworkElement)send.Content;var origin=icon.TranslatePoint(new Point(),send);Check(origin.X>=0 && origin.Y>=0 && origin.X+icon.ActualWidth<=send.ActualWidth && origin.Y+icon.ActualHeight<=send.ActualHeight,"primary glyph is fully inside button");
                }
                var task=session.SendTextAsync("请进行深度思考后，再回答",cfg,pet.Skin.Name);Pump();api.Think("先核对问题中的条件，再整理出清晰的回答。");Pump();
                foreach(Window window in new Window[]{full,quick})
                {var exp=All<Expander>(window).Last(e=>e.Name=="ReasoningDetails");Check(All<TextBlock>(exp.Header as DependencyObject).Any(t=>t.Name=="ProcessStatus"&&t.Text.Contains("先核对")),"process status is a separate stable heading");Check(!exp.IsExpanded && (SystemParameters.ClientAreaAnimation?All<TextBlock>(exp.Header as DependencyObject).Single(t=>t.Name=="ProcessStatus").Foreground is LinearGradientBrush:All<TextBlock>(exp.Header as DependencyObject).Single(t=>t.Name=="ProcessStatus").Foreground==AgentUi.Muted),"reasoning streams into a collapsed one-liner following system motion preference");exp.IsExpanded=true;}
                api.Think("\n接着检查边界情况。");Pump();
                Check(All<Expander>(full).Last(e=>e.Name=="ReasoningDetails").IsExpanded && All<Expander>(quick).Last(e=>e.Name=="ReasoningDetails").IsExpanded,"expansion survives streamed updates");
                api.Answer("这是经过检查后的答复。");Pump();
                foreach(Window window in new Window[]{full,quick})
                {var last=All<Border>(window).Last(b=>b.Name=="AssistantMessage");var process=All<StackPanel>(last).Single(p=>p.Name=="ThinkingProcess");var body=All<MarkdownView>(last).Single();Check(process.IsVisible&&process.TranslatePoint(new Point(0,process.ActualHeight),last).Y<=body.TranslatePoint(new Point(),last).Y,"live process remains above streaming final output");Check(!All<StackPanel>(last).Single(p=>p.Name=="ReplyActions").IsVisible,"reply actions stay hidden during streaming");}
                api.Finish();Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();Pump();
                Check(session.History.Messages.Last().Reasoning.Contains("边界") && session.History.Messages.Last().Content=="这是经过检查后的答复。","reasoning and answer remain separate in history");
                foreach(Window window in new Window[]{full,quick})
                {var last=All<Border>(window).Last(b=>b.Name=="AssistantMessage");var area=All<StackPanel>(last).Single(p=>p.Name=="ThinkingProcess");var exp=All<Expander>(last).Single(e=>e.Name=="ReasoningDetails");exp.IsExpanded=false;Pump();Check(area.IsVisible && !exp.IsExpanded && All<TextBlock>(exp.Header as DependencyObject).Single(t=>t.Name=="ProcessStatus").Text=="思考记录","completed chat reply keeps a collapsed grey thinking entry");}
                var processLog=new ConversationLog();ChatMessage processUser;var processReply=processLog.BeginTurn("工具任务","text",out processUser);processReply=processLog.StartProcessing(processReply.Id,true);
                var processTools=new[]{new AgentToolEvent{Id="process-tool",Name="read_file",Summary="读取 README.md\n完整文件内容",Status="running"}};
                processReply=processLog.Update(processReply.Id,"","pending","先检查仓库",1000,processTools);
                var processHost=new Window{Width=480,Height=360};var processView=new ChatMessageView(processReply,processHost,pet,processHost);processHost.Content=processView;processHost.Show();Pump();
                var processExp=All<Expander>(processView).Single(e=>e.Name=="ReasoningDetails");var processStatus=All<TextBlock>(processView).Single(t=>t.Name=="ProcessStatus");
                var processToggle=VisualAll<System.Windows.Controls.Primitives.ToggleButton>(processExp).First(t=>t.Name=="ProcessToggle");var processChevron=VisualAll<System.Windows.Shapes.Path>(processToggle).Single(p=>p.Name=="ProcessChevron");
                Check(processStatus.Text=="正在查看 README.md" && processStatus.IsVisible && !processExp.IsExpanded,"collapsed process shows the lightweight activity line");
                Check(All<TextBlock>(processExp.Header as DependencyObject).Contains(processStatus) && processChevron.Opacity==0,"activity line is the fixed header and idle chevron stays hidden");
                double chevronX=processChevron.TranslatePoint(new Point(),processHost).X;processTools[0].Summary="读取 "+new string('x',500)+".md\n完整内容";
                processReply=processLog.Update(processReply.Id,"","pending",tools:processTools);processView.Update(processReply);Pump();
                Check(Math.Abs(processChevron.TranslatePoint(new Point(),processHost).X-chevronX)<1 && processStatus.ActualWidth<processHost.ActualWidth && processStatus.TextWrapping==TextWrapping.NoWrap,"streaming long filenames keep chevron fixed and the activity line on one line");
                foreach(var stage in new[]{new[]{"edit_file","正在编辑"},new[]{"open_directory","正在运行"},new[]{"read_file","正在查看"}})
                {processTools[0].Name=stage[0];processTools[0].Summary=stage[0]+" README.md";processReply=processLog.Update(processReply.Id,"","pending",tools:processTools);processView.Update(processReply);Check(processStatus.Text.StartsWith(stage[1]),"active tool determines stage "+stage[0]);}
                processExp.IsExpanded=true;Pump();Check(All<Expander>(processExp).Any(e=>e.Name=="AgentToolDetails") && All<TextBox>(processExp).Any(t=>t.Text=="先检查仓库"),"one header expands both reasoning and tool history");
                processTools[0].Status="complete";processReply=processLog.Update(processReply.Id,"","pending",tools:processTools);processView.Update(processReply);
                processReply=processLog.Update(processReply.Id,"","pending","新的推理\n继续检查");processView.Update(processReply);processExp.IsExpanded=false;Pump();
                Check(processStatus.Text=="正在整理结果" && All<TextBlock>(processView).Any(t=>t.Name=="StageMilestone"),"finished tool shows a black stage milestone while the run settles");
                processReply=processLog.Update(processReply.Id,"最终回答","complete");processView.Update(processReply);Pump();var completedProcess=All<StackPanel>(processView).Single(p=>p.Name=="ThinkingProcess");var completedActions=All<StackPanel>(processView).Single(p=>p.Name=="ReplyActions");Check(completedProcess.IsVisible&&!processExp.IsExpanded&&completedProcess.TranslatePoint(new Point(),processView).Y>=completedActions.TranslatePoint(new Point(0,completedActions.ActualHeight),processView).Y,"completed reply retains a folded process record below actions");
                var inspect=All<Button>(processView).Single(b=>b.Name=="MoreReply").ContextMenu.Items.OfType<MenuItem>().Single(i=>i.Header as string=="查看思考与工具记录");inspect.IsChecked=true;inspect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();Check(processExp.IsExpanded && All<StackPanel>(processView).Single(p=>p.Name=="ThinkingProcess").IsVisible,"more menu reopens full process history after final answer");processHost.Close();
                foreach(Window window in new Window[]{full,quick})
                {
                    var last=All<Border>(window).Last(b=>b.Name=="AssistantMessage");var actions=All<StackPanel>(last).Single(p=>p.Name=="ReplyActions");Check(actions.IsVisible && actions.Children.Count==3 && All<Button>(actions).All(b=>b.Content is Viewbox),"exactly three graphical actions appear after completion");
                    foreach(var action in All<Button>(actions))
                    {
                        var glyph=(Viewbox)action.Content;Check(action.ActualWidth-action.Padding.Left-action.Padding.Right-2>=glyph.Width && action.ActualHeight-action.Padding.Top-action.Padding.Bottom-2>=glyph.Height,"footer buttons have room for the complete glyph");
                        var canvas=(System.Windows.Controls.Canvas)glyph.Child;foreach(var shape in canvas.Children.OfType<System.Windows.Shapes.Path>()){var paint=shape.Data.GetRenderBounds(new Pen(shape.Stroke,shape.StrokeThickness));Check(new Rect(0,0,24,24).Contains(paint),"footer glyph strokes remain inside the drawing area");}
                    }
                }
                var regeneration=session.RegenerateAsync(session.History.Messages.Last().Id,cfg,pet.Skin.Name);Pump();api.Think("重新检查。");api.Answer("## 检查结果\n\n- **条件一**：已核对\n- 条件二：通过\n\n```csharp\nvar ready = true;\n```\n\n| 项目 | 状态 |\n| --- | --- |\n| 测试 | 通过 |");api.Finish();Wait(()=>regeneration.IsCompleted);regeneration.GetAwaiter().GetResult();Pump();Check(session.History.Messages.Count==4 && session.History.Messages.Last().Content.StartsWith("## 检查结果"),"regeneration updates the same reply without duplicate user bubbles");
                var store=new HistoryStore(Path.Combine(folder,"history"));store.SaveAsync("test",session.History.Messages).GetAwaiter().GetResult();var restored=store.Load("test");Check(restored.Last().Reasoning.Contains("重新检查"),"reasoning survives save and reload");
                foreach(var exp in All<Expander>(full).Concat(All<Expander>(quick)))exp.IsExpanded=false;
                Save(full,Path.Combine(folder,"full.png"));Save(All<StackPanel>(All<Border>(full).Last(b=>b.Name=="AssistantMessage")).Single(p=>p.Name=="ReplyActions"),Path.Combine(folder,"reply-actions.png"));Save(quick,Path.Combine(folder,"quick.png"));
                pet.Settings.Text.Model=new string('m',150);full.RefreshAppearance();full.Width=520;Pump();var compactSurface=All<Border>(full).Single(b=>b.Name=="ComposerSurface");var primary=All<Button>(full).Single(b=>b.Name=="SendMessage");Check(primary.TranslatePoint(new Point(primary.ActualWidth,0),compactSurface).X<=compactSurface.ActualWidth-6,"primary fits at minimum window width with long model name");
                string textPath=Path.Combine(folder,"测试附件.txt");File.WriteAllText(textPath,"附件里的中文内容",new System.Text.UTF8Encoding(false));var file=ChatAttachment.Read(textPath);
                var composer=All<ConversationComposer>(quick).Single();typeof(ConversationComposer).GetMethod("AddAttachments",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(composer,new object[]{new[]{file}});Pump();Check(All<Button>(quick).Single(b=>b.Name=="SendQuickMessage").Tag as string=="send","attachment-only draft enables send");
                Check(All<Button>(quick).Any(b=>b.Name=="RemoveAttachment"),"draft attachment can be removed");
                All<Button>(quick).Single(b=>b.Name=="RemoveAttachment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(!All<Button>(quick).Any(b=>b.Name=="RemoveAttachment"),"removing attachment updates draft");
                Check(!All<Button>(quick).Any(b=>b.Name=="ConversationModePicker") && All<TextBlock>(quick).Any(t=>t.Name=="QuickChatBadge"),"quick chat shows its badge and removes the mode switch");
                var linkDraft=All<MarkdownEditor>(full).Single(t=>t.Name=="MessageInput");linkDraft.Text="[DeskPet](https://github.com/Eason4869/deskpet)";Pump();Check(linkDraft.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p=>p.Inlines.OfType<System.Windows.Documents.Hyperlink>()).Any(l=>l.NavigateUri.Host=="github.com"),"input box renders links in its editable Markdown document");linkDraft.Clear();Pump();var draft=All<MarkdownEditor>(quick).Single(t=>t.Name=="QuickMessageInput");pet.Settings.AgentEnabled=false;
                var tab=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(draft),Environment.TickCount,System.Windows.Input.Key.Tab){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};draft.RaiseEvent(tab);Check(!tab.Handled && !pet.Settings.AgentEnabled,"Tab no longer switches modes from the conversation-only quick chat");
                full.SwitchEntry(true);Pump();Check(full.WorkEntry&&All<Button>(full).Any(b=>b.Name=="AgentPermissionPicker"&&b.IsVisible),"work entry exposes permissions");
                full.SwitchEntry(false);Pump();Check(!full.WorkEntry&&!All<Button>(full).Any(b=>b.Name=="AgentPermissionPicker"&&b.IsVisible),"chat entry hides tool permissions");
                composer.SnapshotTargetProvider=()=>null;
                var plus=All<Button>(quick).Single(b=>b.Name=="AddAttachment");plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();var menu=plus.ContextMenu;
                Check(menu.Items.OfType<MenuItem>().Any(m=>m.Name=="AddFilesAndFolders") && menu.Items.OfType<MenuItem>().Any(m=>m.Name=="AddApplicationSnapshot") && !menu.Items.OfType<MenuItem>().Any(m=>m.Name=="SetAgentGoal"||m.Name=="AgentPlanMode"),"quick chat keeps attachment entries and drops work-only goal and planning");
                Check(menu.Items.OfType<MenuItem>().All(m=>!m.HasItems) && !menu.Items.OfType<MenuItem>().Single(m=>m.Name=="AddApplicationSnapshot").IsEnabled,"add menu is flat and snapshot is disabled without another application");
                menu.IsOpen=false;
                var fullPlus=All<Button>(full).Single(b=>b.Name=="AddAttachment");fullPlus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();var fullMenu=fullPlus.ContextMenu;
                Check(!fullMenu.Items.OfType<MenuItem>().Any(m=>m.Name=="AgentPlanMode"),"ordinary chat hides work-only planning controls");fullMenu.IsOpen=false;
                full.SwitchEntry(true);fullPlus=All<Button>(full).Single(b=>b.Name=="AddAttachment");fullPlus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();fullMenu=fullPlus.ContextMenu;
                var planningWorkspace=(ConversationWorkspace)typeof(ChatWindow).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(full);planningWorkspace.FollowProject(ProjectCatalogStore.CreateProject(pet.Projects,"界面测试项目",folder));
                var plan=fullMenu.Items.OfType<MenuItem>().Single(m=>m.Name=="AgentPlanMode");plan.IsChecked=true;plan.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(((ConversationWorkspace)typeof(ChatWindow).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(full)).EffectiveSettings().AgentPlanMode && !pet.Settings.AgentPlanMode,"planning mode applies to the active work draft without changing global defaults");fullMenu.IsOpen=false;
                var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(Ui.Brush("#E8F2FF"),null,new Rect(0,0,220,140));drawing.DrawText(new FormattedText("应用快照",System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),25,Ui.Ink,1.0),new Point(20,44));}var bitmap=new RenderTargetBitmap(220,140,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));byte[] picture;using(var memory=new MemoryStream()){encoder.Save(memory);picture=memory.ToArray();}
                composer.SnapshotTargetProvider=()=>new ApplicationSnapshotTarget{AppName="测试应用",Title="测试应用窗口",IconPng=picture};plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                var snapshot=plus.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Name=="AddApplicationSnapshot");Check(snapshot.IsEnabled && snapshot.Icon is Image && !snapshot.HasItems && ((string)snapshot.Header).Contains("测试应用"),"top application appears directly with its actual icon");Save(plus.ContextMenu,Path.Combine(folder,"flat-add-menu.png"));plus.ContextMenu.IsOpen=false;
                string oldWorkspace=pet.Settings.AgentWorkspace;bool oldAccess=pet.Settings.AgentFullAccess;var directories=composer.AddPathsAsync(new[]{folder,Path.GetDirectoryName(folder)});Wait(()=>directories.IsCompleted);directories.GetAwaiter().GetResult();
                Check(All<Border>(quick).Count(b=>b.Name=="AttachmentCard")==2 && pet.Settings.AgentWorkspace==oldWorkspace && pet.Settings.AgentFullAccess==oldAccess,"multiple directory attachments do not change work directory or permissions");foreach(var button in All<Button>(quick).Where(b=>b.Name=="RemoveAttachment").ToList())button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                composer.AddAttachments(new[]{ChatAttachment.CreateImage("应用快照.png",picture),file});draft.Text="参考这张快照和文档，整理一下内容。";Pump();Check(All<Border>(quick).Count(b=>b.Name=="AttachmentCard")==2 && All<Image>(quick).Any(i=>i.Name=="AttachmentThumbnail"),"draft uses real image thumbnail and file cards");Save(quick,Path.Combine(folder,"quick-attachments.png"));
                full.Width=780;All<ConversationComposer>(full).Single().AddAttachments(new[]{ChatAttachment.CreateImage("应用快照.png",picture),file});All<MarkdownEditor>(full).Single(t=>t.Name=="MessageInput").Text=draft.Text;Pump();Save(full,Path.Combine(folder,"full-attachments.png"));Save(All<Border>(full).Single(b=>b.Name=="ComposerSurface"),Path.Combine(folder,"composer-attachments.png"));
                foreach(var button in All<Button>(quick).Where(b=>b.Name=="RemoveAttachment").ToList())button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(draft.ContextMenu.Items.OfType<MenuItem>().Select(m=>m.Header as string).SequenceEqual(new[]{"剪切","复制","粘贴","全选"}),"editor context menu uses Chinese native editing commands");
                Check(draft.ContextMenu.Items.OfType<MenuItem>().All(m=>m.CommandTarget==draft),"Chinese edit menu retains the native text editor target");
                draft.Select(0,2);draft.ContextMenu.PlacementTarget=draft;draft.ContextMenu.IsOpen=true;Pump();Save(draft.ContextMenu,Path.Combine(folder,"editor-menu.png"));draft.ContextMenu.IsOpen=false;
                var import=composer.ImportDataObjectAsync(new DataObject(DataFormats.FileDrop,new[]{textPath}));Wait(()=>import.IsCompleted);Check(import.GetAwaiter().GetResult() && All<Border>(quick).Count(b=>b.Name=="AttachmentCard")==1,"clipboard/drop file payload uses the same attachment importer");
                import=composer.ImportDataObjectAsync(new DataObject(DataFormats.Bitmap,bitmap));Wait(()=>import.IsCompleted);Check(import.GetAwaiter().GetResult() && All<Image>(quick).Any(i=>i.Name=="AttachmentThumbnail"),"clipboard/drop bitmap payload becomes a PNG thumbnail");
                int before=All<Border>(quick).Count(b=>b.Name=="AttachmentCard");import=composer.ImportDataObjectAsync(new DataObject(DataFormats.UnicodeText,"普通文字"));Wait(()=>import.IsCompleted);Check(!import.GetAwaiter().GetResult() && All<Border>(quick).Count(b=>b.Name=="AttachmentCard")==before,"ordinary text paste is left to the native editor");
                var drop=new DataObject(DataFormats.FileDrop,new[]{textPath});var constructor=typeof(DragEventArgs).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).First(c=>c.GetParameters().Any(p=>p.ParameterType==typeof(IDataObject)));
                var arguments=constructor.GetParameters().Select(p=>p.ParameterType==typeof(IDataObject)?(object)drop:p.ParameterType==typeof(DragDropKeyStates)?DragDropKeyStates.None:p.ParameterType==typeof(DragDropEffects)?DragDropEffects.Copy:p.ParameterType==typeof(DependencyObject)?quick:p.ParameterType==typeof(Point)?(object)new Point(10,10):null).ToArray();
                var dropEvent=(DragEventArgs)constructor.Invoke(arguments);dropEvent.RoutedEvent=DragDrop.PreviewDropEvent;quick.RaiseEvent(dropEvent);Wait(()=>All<Border>(quick).Count(b=>b.Name=="AttachmentCard")==before+1);Check(dropEvent.Handled,"dropping files anywhere in the dialogue routes to attachments");
                full.SwitchWorkspace(null);session.History.Attach(session.History.Messages.First(m=>m.Role=="user").Id,new[]{ChatAttachment.CreateImage("应用快照.png",picture),file});full.ReloadHistory();typeof(QuickChatWindow).GetMethod("ReloadHistory",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(quick,null);Pump();
                foreach(Window window in new Window[]{full,quick})
                {
                    var user=All<Border>(window).First(b=>b.Name=="UserMessage");var cards=All<Border>(user).Where(b=>b.Name=="SentAttachmentCard").ToList();var bubble=All<Border>(user).Single(b=>b.Name=="SentUserBubble");
                    Check(cards.Count==2 && cards.First().Background is ImageBrush && cards.First().ActualWidth==88,"sent attachments use standalone rounded image and file cards");var attachmentActions=cards.Last().ContextMenu.Items.OfType<MenuItem>().Select(i=>(string)i.Header).ToArray();Check(new[]{"打开文件","打开方式…","在资源管理器中打开","复制路径","复制文件内容","另存为…"}.All(x=>attachmentActions.Contains(x)),"file card exposes open, reveal, copy and save actions");
                    Check(cards.All(c=>c.TranslatePoint(new Point(0,c.ActualHeight),user).Y<=bubble.TranslatePoint(new Point(),user).Y),"sent attachments are above the blue user bubble");if(window==full)Save(user,Path.Combine(folder,"sent-attachments.png"));
                }
                pet.Settings.Text.Model="GPT-6.1 Sol";full.RefreshAppearance();quick.RefreshPersona();Pump();Save(full,Path.Combine(folder,"full-final.png"));Save(quick,Path.Combine(folder,"quick-final.png"));
                full.Activate();var speech=All<CheckBox>(full).Single(c=>c.Name=="TtsOutput");speech.Focus();Pump();var speechSurface=(Border)speech.Template.FindName("b",speech);
                Check(speech.IsKeyboardFocused && speech.FocusVisualStyle==null && ((SolidColorBrush)speechSurface.Background).Color==((SolidColorBrush)Ui.Brush("#EFEFEF")).Color && speechSurface.BorderBrush==Brushes.Transparent,"speech keyboard focus uses gray fill with no blue frame");Save(speech,Path.Combine(folder,"gray-focus.png"));
                var appearance=pet.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Name=="QuickAppearanceMenu");pet.ContextMenu.IsOpen=true;appearance.IsSubmenuOpen=true;Pump();
                Check(appearance.Items.OfType<MenuItem>().All(m=>m.Icon is Image && ((Image)m.Icon).Source is BitmapSource && ((BitmapSource)((Image)m.Icon).Source).PixelWidth==256),"all four appearance menu entries display cached portrait avatars");var popup=(System.Windows.Controls.Primitives.Popup)appearance.Template.FindName("PART_Popup",appearance);Save((FrameworkElement)popup.Child,Path.Combine(folder,"avatar-menu.png"));Save(pet.ContextMenu,Path.Combine(folder,"appearance-menu.png"));appearance.IsSubmenuOpen=false;pet.ContextMenu.IsOpen=false;
                var preferences=new SettingsWindow(pet);preferences.Show();Pump();var picker=All<ComboBox>(preferences).Single(c=>c.Name=="SkinPicker");picker.IsDropDownOpen=true;Pump();var choicesPopup=(System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup",picker);Save((FrameworkElement)choicesPopup.Child,Path.Combine(folder,"avatar-picker.png"));
                Check(picker.ItemTemplate!=null && VisualAll<Image>(choicesPopup.Child).Count(i=>i.Source is BitmapSource && ((BitmapSource)i.Source).PixelWidth==256)>=4,"settings partner picker renders all four portrait avatars");picker.IsDropDownOpen=false;preferences.Close();
                var longMenu=ChatUi.Menu();longMenu.MaxHeight=220;for(int i=0;i<80;i++)longMenu.Items.Add(new MenuItem{Header="模型 "+i});longMenu.PlacementTarget=primary;longMenu.IsOpen=true;Pump();var menuScroll=VisualAll<ScrollViewer>(longMenu).First();var lastItem=(MenuItem)longMenu.Items[79];lastItem.Focus();lastItem.BringIntoView();Pump();Save(longMenu,Path.Combine(folder,"long-menu.png"));Check(menuScroll.ScrollableHeight>0 && menuScroll.VerticalOffset>0 && lastItem.TranslatePoint(new Point(),menuScroll).Y<menuScroll.ActualHeight,"long rounded menus scroll and keep the last focused model visible");longMenu.IsOpen=false;
                Console.WriteLine("Conversation UI: "+checks+" passed");full.Close();quick.Close();pet.Close();return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);if(pet!=null)pet.Close();return 1;}
        }
        sealed class Model:IConversationApi,IReasoningConversationApi
        {
            Action<string> reasoning,answer;TaskCompletionSource<string> done;string text="";
            public Task<string> ChatWithReasoningAsync(ConnectionSettings c,IList<ChatMessage> m,Action<string> a,Action<string> r,bool stream,CancellationToken t){text="";reasoning=r;answer=a;done=new TaskCompletionSource<string>();t.Register(()=>done.TrySetCanceled());return done.Task;}
            public void Think(string s){reasoning(s);}public void Answer(string s){text+=s;answer(s);}public void Finish(){done.SetResult(text);}
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> m,CancellationToken t){throw new Exception("Expected reasoning API");}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] w,CancellationToken t){throw new Exception();}public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string s,CancellationToken t){throw new Exception();}public void Dispose(){}
        }
        sealed class Silent:IReplySpeech{public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in All<T>(child))yield return node;}
        static IEnumerable<T> VisualAll<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in VisualAll<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Wait(Func<bool> ready){var limit=DateTime.UtcNow.AddSeconds(5);while(!ready() && DateTime.UtcNow<limit)Pump();if(!ready())throw new Exception("UI timeout");}
        static void Save(FrameworkElement w,string path){w.BringIntoView();var host=Window.GetWindow(w);if(host!=null)host.UpdateLayout();Pump();w.UpdateLayout();if(w.ActualWidth<=0||w.ActualHeight<=0)throw new Exception("Cannot capture unrendered control "+w.Name+" visibility="+w.Visibility+" actual="+w.ActualWidth+","+w.ActualHeight);var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);if(w is Window)bitmap.Render(w);else{var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(w),null,new Rect(0,0,w.ActualWidth,w.ActualHeight));bitmap.Render(visual);}var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))png.Save(stream);}
    }
}
