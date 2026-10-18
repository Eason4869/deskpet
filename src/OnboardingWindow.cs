using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu
{
    public sealed partial class OnboardingWindow : Window
    {
        CancellationTokenSource approvalDemo;bool approvalDemoReviewed;
        readonly PetWindow pet; readonly SettingsPage settings; readonly Window settingsHost; readonly TextBlock title, instruction, status, progress; readonly StackPanel actions; readonly Button next, back, locate; readonly WrapPanel exitActions; readonly StackPanel directory; readonly TextBlock guideHeading; readonly ScrollViewer bodyScroll; readonly ComboBox contents;
        readonly DispatcherTimer readiness = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) }; readonly List<GuideFocusScope> otherScopes = new List<GuideFocusScope>();
        GuideFocusScope focusScope, petScope; FrameworkElement target; AdornerLayer layer; GuideAdorner highlight; Expander networkPreview; bool networkWasExpanded;
        bool finished, navigating, positioning, ownerClosing, gestureDone, updatingContents, textTestSucceeded; int index, speechRevision, sectionCount, projectCount, liveRevision, dictationRevision; string draftId; CheckBox heard; CancellationTokenSource textTest;
        OnboardingLesson Current { get { return OnboardingLessons.All[index]; } }
        public int Step { get { return Current.Chapter; } }
        public string CurrentLessonId { get { return Current.Id; } }
        public string TargetName { get; private set; }
        public bool HighlightVisible { get { return highlight != null && target != null && target.IsVisible; } }
        bool Bookend {get{return Current.Action=="intro"||Current.Action=="complete";}}
        bool PetLesson { get { return Step >= 3 && Step <= 7; } }
        bool Completed { get { return pet.Settings.OnboardingCompletedLessons.Contains(CurrentLessonId); } }
        public OnboardingWindow(PetWindow companion, SettingsWindow editor)
            : this(companion, editor==null?null:editor.Page, editor) { }
        public OnboardingWindow(PetWindow companion, SettingsPage editor)
            : this(companion, editor, editor==null?null:editor.HostWindow) { }
        public OnboardingWindow(PetWindow companion, SettingsPage editor, Window actualHost)
        {
            if(companion==null)throw new ArgumentNullException("companion");
            if(editor==null)throw new ArgumentNullException("editor");
            if(actualHost==null)throw new ArgumentNullException("actualHost");
            if(editor.HostWindow!=null && !ReferenceEquals(editor.HostWindow,actualHost))throw new ArgumentException("The settings page must belong to the supplied host.","actualHost");
            pet = companion; settings = editor; settingsHost=actualHost; bool restart = pet.Settings.OnboardingState == "completed" || pet.Settings.OnboardingState == "skipped";
            if (restart) { pet.Settings.OnboardingLessonId = ""; pet.Settings.OnboardingCompletedLessons.Clear(); pet.Settings.OnboardingSkippedLessons.Clear(); pet.Settings.OnboardingVerifiedLessons.Clear(); }
            index = Array.FindIndex(OnboardingLessons.All, l => l.Id == pet.Settings.OnboardingLessonId); if (index < 0) index = Array.FindIndex(OnboardingLessons.All, l => l.Chapter == (restart ? 0 : pet.Settings.OnboardingStep)); if (index < 0) index = 0; pet.Settings.OnboardingState = "deferred";
            Ui.Window(this, "配置引导 · 桌面伙伴", 390, 540); Owner = settingsHost; ShowInTaskbar = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; MinWidth = 280; MinHeight = 300; WindowStartupLocation = WindowStartupLocation.Manual;
            var root = new Grid { Margin = new Thickness(20, 18, 20, 18) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            guideHeading = Ui.InlineText("和" + pet.Skin.Name + "一起开始", 18); guideHeading.FontWeight = FontWeights.SemiBold; guideHeading.Margin = new Thickness(0, 0, 32, 12); var dragArea=new Border{Name="GuideDragArea",Child=guideHeading,Background=Brushes.Transparent};dragArea.MouseLeftButtonDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){if(e.ButtonState!=System.Windows.Input.MouseButtonState.Pressed)return;try{DragMove();e.Handled=true;}catch(InvalidOperationException){}};root.Children.Add(dragArea);
            directory = new StackPanel { Margin = new Thickness(0, 0, 0, 16) }; contents = new ComboBox { Name = "GuideContents", MinHeight = 33, ItemsSource = OnboardingLessons.Chapters.Select((s, i) => (i + 1) + " · " + s).ToArray() }; contents.SelectionChanged += delegate { if (!updatingContents && contents.SelectedIndex >= 0) { if (!CanLeave()) { updatingContents = true; contents.SelectedIndex = Step; updatingContents = false; return; } index = Array.FindIndex(OnboardingLessons.All, l => l.Chapter == contents.SelectedIndex); Navigate(); } }; directory.Children.Add(contents); progress = Ui.Text("", 11, Ui.Muted); progress.Name = "GuideProgress"; progress.Margin = new Thickness(0, 6, 0, 0); directory.Children.Add(progress); Grid.SetRow(directory, 1); root.Children.Add(directory);
            var body = new StackPanel(); body.Children.Add(CreateCelebration()); title = Ui.Text("", 21); title.Name = "GuideLessonTitle"; title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 10); body.Children.Add(title); instruction = Ui.Text("", 13, Ui.Muted); instruction.Name = "GuideInstruction"; instruction.LineHeight = 23; instruction.Margin = new Thickness(0, 0, 0, 14); body.Children.Add(instruction); actions = new StackPanel(); body.Children.Add(actions); status = Ui.Text("", 12, Ui.Blue); status.Name = "GuideNotice"; status.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(status); locate = Small("回到当前操作位置", "GuideReturnTarget"); locate.Click += delegate { Locate(); }; body.Children.Add(locate); bodyScroll = Ui.Scroll(body); bodyScroll.Name = "GuideBodyScroll"; Grid.SetRow(bodyScroll, 2); root.Children.Add(bodyScroll);
            var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; var nav = new Grid(); nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); nav.ColumnDefinitions.Add(new ColumnDefinition()); back = Ui.Button("上一步"); back.Name = "GuideBack"; back.Click += delegate { Move(-1); }; nav.Children.Add(back); next = Ui.Button("下一步", true); next.Name = "GuideNext"; next.IsDefault = true; next.Margin = new Thickness(0); next.Click += delegate { Advance(); }; Grid.SetColumn(next, 1); nav.Children.Add(next); footer.Children.Add(nav);
            exitActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; var skip = Small("跳过本步", "GuideLater"); skip.Click += delegate { Skip(false); }; exitActions.Children.Add(skip); var chapter = Small("跳过本章", "GuideSkipChapter"); chapter.Click += delegate { Skip(true); }; exitActions.Children.Add(chapter); var defer = Small("稍后继续", "GuideDefer"); defer.Click += delegate { Finish("deferred"); }; exitActions.Children.Add(defer); var end = Small("结束引导", "GuideSkip"); end.Click += delegate { Finish("skipped"); }; exitActions.Children.Add(end); footer.Children.Add(exitActions); Grid.SetRow(footer, 3); root.Children.Add(footer); DialogUi.Apply(this,root);WindowStartupLocation=WindowStartupLocation.Manual;
            Loaded += delegate { pet.SetGuideLock(true); Navigate(); readiness.Start(); }; readiness.Tick += delegate { RefreshReadiness(); }; pet.GuideInteractionCompleted += InteractionCompleted; settingsHost.Closing += OwnerClosing; settingsHost.LocationChanged += OwnerMoved; settingsHost.SizeChanged += OwnerSized; Closing += ClosingGuide; Closed += ClosedGuide;
        }
        void ShowSettingsHost()
        {
            var main=settingsHost as ChatWindow;
            if(main!=null)main.ShowSettingsPage(settings);
            if(!settingsHost.IsVisible)settingsHost.Show();
            if(settingsHost.WindowState==WindowState.Minimized)settingsHost.WindowState=WindowState.Normal;
        }
        static Button Small(string text, string name) { var b = Ui.Button(text); b.Name = name; b.FontSize = 11; b.MinHeight = 27; b.Padding = new Thickness(2, 3, 7, 3); b.Margin = new Thickness(0); b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; b.Foreground = Ui.Muted; return b; }
        async void ShowApprovalDemo()
        {
            if(approvalDemo!=null)return;var cancel=new CancellationTokenSource();approvalDemo=cancel;
            try{var request=new AgentApprovalRequest{ToolName="审批练习（不会执行）：exec_command",Action="shell",Resource="仅演示：隔离 fixture 目录；不会运行",ProjectId="guide-demo",TaskId="guide-demo",CallId=Guid.NewGuid().ToString("N"),Arguments="{\"command\":\"Write-Output 'DeskPet approval demo'\",\"working_directory\":\"guide-demo\",\"timeout_seconds\":5}"};await new AgentApprovalController(this).ApproveAsync(request,cancel.Token);if(cancel.IsCancellationRequested||finished)return;approvalDemoReviewed=true;status.Text="已完成审批练习；没有执行命令，不保存授权，也不代表终端已验证。";RefreshReadiness();}
            catch(OperationCanceledException){}finally{if(ReferenceEquals(approvalDemo,cancel))approvalDemo=null;cancel.Dispose();}
        }
        void CancelApprovalDemo(){var cancel=approvalDemo;if(cancel!=null)cancel.Cancel();}
        void ActionButton(string caption, string name, Action action) { var b = Ui.Button(caption); b.Name = name; b.Margin = new Thickness(0, 0, 0, 7); b.HorizontalAlignment = HorizontalAlignment.Left; b.Click += delegate { try { action(); } catch (Exception e) { status.Text = "操作未完成：" + e.Message; } }; actions.Children.Add(b); }
        bool Applies(OnboardingLesson lesson) { if (lesson.Branch == "cloud" || lesson.Branch == "system") { var toggle = settings.FindControl("UseCloudVoice") as CheckBox; return (toggle != null ? toggle.IsChecked == true : pet.Settings.UseCloudVoice) == (lesson.Branch == "cloud"); } if (lesson.Branch == "unknown-context") { return pet.Settings.Text == null || pet.Settings.Text.ContextWindowTokens <= 0; } return true; }
        bool CanLeave() { if (pet.LiveActive || pet.DictationActive) { status.Text = "请先挂断通话或停止／取消录音，再离开当前步骤。"; return false; } return true; }
        void Move(int direction) { if (!CanLeave()) return; int candidate = index + direction; while (candidate >= 0 && candidate < OnboardingLessons.All.Length && !Applies(OnboardingLessons.All[candidate])) candidate += direction; if (candidate >= 0 && candidate < OnboardingLessons.All.Length) { index = candidate; Navigate(); } }
        void Skip(bool chapter) { if (!CanLeave() || Current.Action=="complete") return; int before = Step, last=OnboardingLessons.All.Length-1; do { if (!Completed && !pet.Settings.OnboardingSkippedLessons.Contains(CurrentLessonId)) pet.Settings.OnboardingSkippedLessons.Add(CurrentLessonId); index++; } while (chapter && index < last && Step == before); if(index>last)index=last; while (index < last && !Applies(Current)) index++; Navigate(); }
        void InteractionCompleted(string action) { gestureDone = true; RefreshReadiness(); }
        void Advance()
        {
            RefreshReadiness(); if (!next.IsEnabled) return; try
            {
                string a = Current.Action; if (a.StartsWith("ai") && !settings.SaveGuideAiSection(Int32.Parse(a.Substring(2)))) throw new InvalidOperationException("当前服务尚未保存，请检查设置页提示，也可跳过。保存不代表真实连接已验证。");
                if (a == "persona") { Click(settings.FindControl("SavePersona")); if (((TextBox)settings.FindControl("PersonaPrompt")).Text != pet.Settings.SystemPrompt || !String.IsNullOrEmpty(pet.Notice)) throw new InvalidOperationException("人格尚未保存，请查看设置提示。"); }
                if (a == "greeting") { pet.Settings.GreetingMinMinutes = Int32.Parse(((TextBox)settings.FindControl("GreetingMinMinutes")).Text); pet.Settings.GreetingMaxMinutes = Int32.Parse(((TextBox)settings.FindControl("GreetingMaxMinutes")).Text); pet.ConfigureGreetings(); }
                if (a == "hotkey") { string error; if (!pet.UpdateHotkey("quick", ((TextBox)settings.FindControl("Hotkeyquick")).Text, out error)) throw new InvalidOperationException(error); }
                if ((a == "work-save" || a == "prompt-save") && !((WorkSettingsPanel)settings.FindControl("WorkSettingsPanel")).Save()) throw new InvalidOperationException("工作设置尚未保存，请查看设置页提示。");
                if (a == "network" && !settings.SaveGuideNetwork()) throw new InvalidOperationException("网络设置尚未保存，请检查地址。");
                if (a.EndsWith("-test") && !pet.Settings.OnboardingVerifiedLessons.Contains(CurrentLessonId)) pet.Settings.OnboardingVerifiedLessons.Add(CurrentLessonId); if (!Completed) pet.Settings.OnboardingCompletedLessons.Add(CurrentLessonId); pet.Settings.OnboardingSkippedLessons.Remove(CurrentLessonId);
                if (index == OnboardingLessons.All.Length - 1) { Finish("completed"); return; }
                Move(1);
            }
            catch (Exception e) { if (!(e is InvalidOperationException || e is ArgumentException || e is System.IO.IOException || e is UnauthorizedAccessException)) throw; status.Text = e.Message; }
        }
        void RefreshReadiness()
        {
            if (finished || navigating) return; bool ready = true; string reason = ""; string a = Current.Action; var control = a == "nonempty" || a == "url" ? FindTarget(Current.Target) : null;
            if(a=="approval-demo"){ready=approvalDemoReviewed;reason="请主动体验审批练习，或跳过；练习不会执行命令。";}
            if (a == "gesture") { ready = gestureDone; reason = "请完成本步实操，或跳过。"; }
            if (a == "nonempty") { var t = control as TextBox; var c = control as ComboBox; ready = t != null ? !String.IsNullOrWhiteSpace(t.Text) : c != null && !String.IsNullOrWhiteSpace(c.Text); reason = "请填写本项，未准备好可跳过。"; }
            if (a == "url") { Uri uri; ready = control is TextBox && Uri.TryCreate(((TextBox)control).Text, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https"); reason = "请输入有效的 http 或 https 接口地址。"; }
            if (a.StartsWith("ai")) ready = settings.ValidateGuideAiSection(Int32.Parse(a.Substring(2)), out reason);
            if (a == "greeting") { int min, max; ready = Int32.TryParse(((TextBox)settings.FindControl("GreetingMinMinutes")).Text, out min) && Int32.TryParse(((TextBox)settings.FindControl("GreetingMaxMinutes")).Text, out max) && min >= 1 && max <= 1440 && min <= max; reason = "间隔应为 1–1440 分钟，最长不能小于最短。"; }
            if (a == "hotkey") { HotkeyCombination key; string value = ((TextBox)settings.FindControl("Hotkeyquick")).Text; ready = String.IsNullOrWhiteSpace(value) || HotkeyCombination.TryParse(value, out key); reason = "请输入有效组合键，或留空停用。"; }
            if (a == "speech-test") { ready = settings.GuideSpeechTestRevision > speechRevision && settings.GuideSpeechTestSucceeded && heard.IsChecked == true; reason = settings.GuideSpeechTestRevision > speechRevision && settings.GuideSpeechTestSucceeded ? "接口与播放流程已完成；请确认你实际听到了声音。" : "点击测试朗读并等待完成；未试听可跳过，失败可按排错步骤检查。"; }
            if (a == "text-test") { ready = textTestSucceeded; reason = "请显式发送文字测试，或跳过；保存不能代替真实回复验证。"; }
            if (a == "live-test") { ready = pet.Live.SuccessfulConnections > liveRevision && !pet.LiveActive && heard.IsChecked == true; reason = "请显式连接，成功后挂断；未测试可跳过。"; }
            var chat = Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w => w.IsVisible);
            if (a == "dictation-test") { ready = pet.Dictation.SuccessfulRecognitions > dictationRevision && !pet.DictationActive && chat != null && Walk(chat).OfType<MarkdownEditor>().Any(t => t.Name == "MessageInput" && !String.IsNullOrWhiteSpace(t.Text)); reason = "请实际录音并停止，等待识别文字填入草稿，或跳过。"; }
            if (a == "wide") { ready = chat != null && chat.ActualWidth >= 960; reason = "请拉宽或最大化完整对话窗口，直到左侧栏出现。"; }
            if (a == "section") { ready = pet.Projects.Sections.Count > sectionCount; reason = "请从分区 … 新建分区并保存，或跳过。"; }
            if (a == "project") { ready = pet.Projects.Projects.Count > projectCount; reason = "请点击分区 + 新建项目并保存，或跳过。"; }
            if (a == "new-chat") { ready = chat != null && chat.ConversationId != draftId; reason = "请点击左侧栏“新建对话”。"; }
            if (a == "work-mode" || a == "chat-mode") { ready = chat != null && chat.WorkEntry == (a == "work-mode"); reason = "请在顶部选择本步要求的聊天或工作入口。"; }
            if (a == "choose-project") { var w = chat == null ? null : pet.Workspaces.OpenWorkspaces.FirstOrDefault(x => x.Id == chat.ConversationId); ready = w != null && w.EffectiveSettings().AgentEnabled && w.ProjectId != ProjectCatalogStore.UnassignedProjectId && w.ProjectId != ProjectCatalogStore.QuickProjectId; reason = "请打开项目选择器，选择已有项目；不使用项目可跳过。"; }
            if (a == "network") ready = settings.ValidateGuideNetwork(out reason); if (Completed && !new[] { "nonempty", "url", "greeting", "hotkey", "network" }.Contains(a) && !a.StartsWith("ai")) ready = true;
            next.IsEnabled = ready; if (!ready) status.Text = reason; else if (String.IsNullOrEmpty(status.Text) || status.Text.StartsWith("请") || status.Text.StartsWith("点击测试")) status.Text = Completed ? "此步已完成，可以继续。" : "可以继续。";
            if (Step == 14 && target == null) { var found = FindTarget(TargetName); if (found != null && found.IsVisible) { target = found; AddHighlight(); PositionNearTarget(); } }
        }
        void Navigate()
        {
            if (finished || navigating) return; navigating = true; try
            {
                CancelApprovalDemo();approvalDemoReviewed=false;ReleaseScopes(); RestoreNetworkPreview(); RemoveHighlight(); pet.SetGuideInteraction(""); gestureDone = false; status.Text = ""; actions.Children.Clear(); heard = null; textTestSucceeded = false; liveRevision = pet.Live.SuccessfulConnections; dictationRevision = pet.Dictation.SuccessfulRecognitions; if (textTest != null) textTest.Cancel(); pet.GuideWorkspacePractice = Step == 14 || Current.Action == "live-test";
                if (PetLesson) { Owner = pet; settingsHost.Hide(); pet.SetGuideInteraction(new[] { "drag", "menu", "wheel", "click", "double-click" }[Step - 3]); } else if (Step == 14) { Owner = pet; settingsHost.Hide(); pet.ShowChat(); Owner = (Window)Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w => w.IsVisible) ?? pet; } else { ShowSettingsHost(); Owner = settingsHost; if(!Bookend)SelectSettingsPage(); }
                title.Text = Current.Title; instruction.Text = Current.Text; TargetName = Current.Target; var lessons = OnboardingLessons.All.Where(l => l.Chapter == Step && (l == Current || Applies(l))).ToArray(); progress.Text = "第 " + (Step + 1) + " / 17 章 · 小步骤 " + (Array.IndexOf(lessons, Current) + 1) + " / " + lessons.Length; updatingContents = true; contents.SelectedIndex = Step; updatingContents = false; back.IsEnabled = index > 0; next.Content = Current.Action == "complete" ? "完成引导" : Current.Action == "intro" ? "开始引导" : Current.Action.StartsWith("ai") || new[] { "persona", "hotkey", "greeting", "work-save", "prompt-save", "network" }.Contains(Current.Action) ? "保存并继续" : "下一步";
                if (Step == 14) { sectionCount = pet.Projects.Sections.Count; projectCount = pet.Projects.Projects.Count; var practice = Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w => w.IsVisible); draftId = practice == null ? null : practice.ConversationId; if (Current.Action == "wide") ActionButton("最大化完整对话窗口", "GuideMaximizeChat", delegate { var chat = Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w => w.IsVisible); if (chat != null) { chat.WindowState = WindowState.Maximized; chat.Activate(); } }); if (Current.Action == "section") ActionButton("打开分区更多菜单", "GuideOpenSectionMore", delegate { Click(FindTarget("SectionMore_projects")); }); }
                if (Current.Action == "speech-test") { speechRevision = settings.GuideSpeechTestRevision; heard = new CheckBox { Name = "GuideHeardSpeech", Content = "我确实听到了测试声音", Margin = new Thickness(0, 8, 0, 6) }; heard.Checked += delegate { RefreshReadiness(); }; heard.Unchecked += delegate { RefreshReadiness(); }; actions.Children.Add(heard); }
                if (Current.Action == "live-test") { heard = new CheckBox { Name = "GuideConfirmedLive", Content = "我已确认字幕与返回声音", Margin = new Thickness(0, 8, 0, 6) }; heard.Checked += delegate { RefreshReadiness(); }; actions.Children.Add(heard); }
                if(Current.Action=="approval-demo")ActionButton("审批练习（不执行命令）","GuideApprovalDemo",ShowApprovalDemo);if (Current.Action == "text-test") ActionButton("发送文字测试（可能消耗额度）", "GuideTestText", TestText); if (Current.Action == "summary") foreach (var group in OnboardingLessons.All.GroupBy(l => l.Chapter)) { var text = Ui.Text(OnboardingLessons.Chapters[group.Key] + "：完成 " + group.Count(l => pet.Settings.OnboardingCompletedLessons.Contains(l.Id)) + "，跳过 " + group.Count(l => pet.Settings.OnboardingSkippedLessons.Contains(l.Id)) + "，实际验证 " + group.Count(l => pet.Settings.OnboardingVerifiedLessons.Contains(l.Id)), 11, Ui.Muted); text.Margin = new Thickness(0, 0, 0, 5); actions.Children.Add(text); }
                StyleBookend();if(Bookend){PositionNearTarget();}else Locate(); if (Current.Action != "speech-test") { var preview = settings.FindControl("PreviewSpeech"); if (preview != null) otherScopes.Add(new GuideFocusScope(preview)); }
                if (Current.Action != "live-test") { var preview = settings.FindControl("PreviewLive"); if (preview != null) otherScopes.Add(new GuideFocusScope(preview)); }
                foreach (Window w in Application.Current.Windows.Cast<Window>().ToArray()) if (w != this && w != settingsHost && w != pet && !(Step == 14 && w is ChatWindow) && w.Content is UIElement) otherScopes.Add(new GuideFocusScope((UIElement)w.Content)); Persist();
            }
            finally { navigating = false; RefreshReadiness(); }
        }
        void SelectSettingsPage()
        {
            switch (Step)
            {
                case 0: case 1: settings.SelectCompanionSection(0); break;
                case 2: settings.SelectCompanionSection(1); break;
                case 8: case 9: case 10: case 11: settings.SelectAiSection(Step - 8); break;
                case 12: settings.SelectCompanionSection(2); break;
                case 13: settings.SelectPage(7); break;
                case 15:
                    if(Current.Id.StartsWith("mcp.",StringComparison.Ordinal)){bool listStep=Current.Id=="mcp.intro"||Current.Id=="mcp.connect";settings.SelectMcpSettings(!listStep);if(listStep)Click(settings.FindControl("BackMcpServers"));break;}
                    if (settings.FindControl("WorkSettingsPanel") == null) settings.SelectWorkSettings();
                    Click(settings.FindControl(Current.Id.StartsWith("work.prompt") ? "WorkSettingsPromptTab" : "WorkSettingsGeneralTab"));
                    break;
                case 16:
                    settings.SelectPage(6); networkPreview = settings.FindControl("UpdateNetwork") as Expander;
                    if (networkPreview != null) { networkWasExpanded = networkPreview.IsExpanded; networkPreview.IsExpanded = true; }
                    break;
            }
            // An unsaved engine toggle may not survive an application restart.
            // Return to its explicit choice rather than resuming hidden cloud fields.
            if (Step == 10 && (Current.Branch == "cloud" || Current.Branch == "system") && !Applies(Current))
                index = Array.FindIndex(OnboardingLessons.All, l => l.Id == "speech.engine");
        }
        string CurrentScope()
        {
            switch (Step)
            {
                case 0: return "SkinPicker";
                case 1: return "GuidePersonaSection";
                case 2: return "GuideBehaviorSection";
                case 8: return "GuideTextProviderSection";
                case 9: return "GuideAsrProviderSection";
                case 10: return "GuideSpeechProviderSection";
                case 11: return "GuideLiveProviderSection";
                case 12: return "GuideGreetingSection";
                case 13: return TargetName.StartsWith("Hotkey") ? "GuideShortcut" + TargetName.Substring(6) : TargetName;
                case 15: return Current.Id.StartsWith("mcp.",StringComparison.Ordinal)?"McpSettingsPanel":"WorkSettingsPanel";
                default: return "UpdateNetwork";
            }
        }
        void Locate()
        {
            if (finished || Bookend) return; RemoveHighlight(); if (focusScope != null) { focusScope.Dispose(); focusScope = null; }
            if (petScope != null) { petScope.Dispose(); petScope = null; }
            if (Step == 14) { target = FindTarget(TargetName); if (target != null) { target.BringIntoView(); target.Focus(); } }
            else if (PetLesson) target = pet.Content as FrameworkElement;
            else
            {
                settings.ShowGuideTarget(TargetName); string scope = CurrentScope();
                target = settings.FindControl(TargetName); var allowed = settings.FindControl(scope); if (target == null || !target.IsVisible) { target = allowed; status.Text = "先选择或新增本章服务商，再操作本项；未准备好可跳过。"; }
                focusScope = new GuideFocusScope((UIElement)settings.Content, allowed, settings.FindControl("ConnectionNotice"));
            }
            if (!PetLesson) petScope = new GuideFocusScope((UIElement)pet.Content); AddHighlight(); UpdateLayout(); PositionNearTarget(); Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { if (!finished) { if (highlight != null) highlight.InvalidateVisual(); PositionNearTarget(); } }));
        }
        void AddHighlight() { if (target == null) return; target.IsVisibleChanged += TargetVisibilityChanged; layer = AdornerLayer.GetAdornerLayer(target); if (layer != null) { highlight = new GuideAdorner(target); layer.Add(highlight); } }
        FrameworkElement FindTarget(string name) { if (Step == 14) return Application.Current.Windows.OfType<ChatWindow>().Where(w => w.IsVisible).SelectMany(w => Walk(w)).OfType<FrameworkElement>().FirstOrDefault(e => e.Name == name); return settings.FindControl(name); }
        static IEnumerable<DependencyObject> Walk(DependencyObject node) { yield return node; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var x in Walk(VisualTreeHelper.GetChild(node, i))) yield return x; }
        static void Click(FrameworkElement e) { var b = e as Button; if (b != null && b.IsEnabled) b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        async void TestText() { if (textTest != null) return; if (!settings.SaveGuideAiSection(0)) { status.Text = "请先完善并保存文字服务。"; return; } var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25)); textTest = cancel; string requested = CurrentLessonId; status.Text = "正在请求文字模型，不会写入聊天记录。"; try { using (var api = new ApiClient()) { var reply = await api.ChatAsync(pet.Settings.Text.Copy(), new[] { new ChatMessage("user", "请只回复：连接成功") }, cancel.Token); if (finished || CurrentLessonId != requested) return; textTestSucceeded = !String.IsNullOrWhiteSpace(reply); status.Text = textTestSucceeded ? "已收到真实文字回复。" : "没有收到正文，请检查模型与协议。"; } } catch (Exception) { if (!finished && CurrentLessonId == requested) status.Text = "测试未完成，请检查服务与网络，可重试或跳过。"; } finally { if (ReferenceEquals(textTest, cancel)) textTest = null; cancel.Dispose(); if (!finished) RefreshReadiness(); } }
        void ReleaseScopes() { if (focusScope != null) { focusScope.Dispose(); focusScope = null; } if (petScope != null) { petScope.Dispose(); petScope = null; } foreach (var s in otherScopes) s.Dispose(); otherScopes.Clear(); }
        void RestoreNetworkPreview() { if (networkPreview != null) { networkPreview.IsExpanded = networkWasExpanded; networkPreview = null; } }
        void Persist() { pet.Settings.OnboardingLessonId = CurrentLessonId; pet.Settings.OnboardingStep = Step; pet.SaveSettings(); }
        void Finish(string state) { if (finished) return; pet.Settings.OnboardingState = state; Persist(); finished = true; Close(); }
        void ClosingGuide(object sender, CancelEventArgs e) { CancelApprovalDemo();if (!finished) { pet.Settings.OnboardingState = "deferred"; Persist(); finished = true; } }
        void ClosedGuide(object sender, EventArgs args) { readiness.Stop(); if (textTest != null) textTest.Cancel(); pet.GuideWorkspacePractice = false; RestoreNetworkPreview(); ReleaseScopes(); RemoveHighlight(); pet.SetGuideLock(false); pet.SetGuideInteraction(""); pet.GuideInteractionCompleted -= InteractionCompleted; settingsHost.Closing -= OwnerClosing; settingsHost.LocationChanged -= OwnerMoved; settingsHost.SizeChanged -= OwnerSized; if(!ownerClosing && pet.Settings.OnboardingState=="completed")pet.ShowChat();else if (!ownerClosing && !settingsHost.IsVisible && settingsHost.IsLoaded) ShowSettingsHost(); }
    }
}
