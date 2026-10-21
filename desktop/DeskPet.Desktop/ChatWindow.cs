using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ChatWindow : Window
{
    private readonly DesktopController owner;
    private readonly bool quick;
    private readonly TextBox draft = Ui.Input("", "和伙伴一起工作");
    private readonly TextBlock status = Ui.Text("");
    private readonly StackPanel messages = Ui.Column();
    private readonly StackPanel queue = Ui.Column();
    private readonly StackPanel questions = Ui.Column();
    private readonly ScrollViewer history;
    private readonly ComboBox models = new() { MinWidth = 180 };
    private readonly Dictionary<string, string> queuedEdits = new();
    private readonly Dictionary<string, ChatMessageView> rendered = new();
    private readonly Dictionary<string, List<ChatAttachment>> attachments = new();
    private readonly WrapPanel attachmentChips = new();
    private readonly List<Bitmap> attachmentImages = new();
    private readonly MenuItem planMode;
    private readonly MenuItem goalMenu;
    private readonly Button primary;
    private readonly Button addAttachment;
    private readonly Button modeButton;
    private readonly MenuItem conversationMode;
    private readonly MenuItem workMode;
    private readonly MenuItem snapshotMenu;
    private ApplicationSnapshotTarget? snapshotTarget;
    private Bitmap? snapshotIcon;
    private readonly CancellationTokenSource snapshotCancel = new();
    private readonly SpriteAssets avatarAssets = new();
    private readonly AvatarView partnerAvatar;
    private readonly ContentControl pageHost = new();
    private readonly ImageWorkspacePage imageWorkspace;
    private readonly List<string> routes = new() { "chat" };
    private int routeIndex;
    private Control chatPage = null!;
    private readonly Button routeBack, routeForward;
    private readonly TextBlock partnerName = Ui.Text("", 14);
    private bool nativePaste, closed;
    private int uploadEpoch;
    private readonly record struct UploadContext(string Persona, int Epoch);
    private List<ChatAttachment> Files => attachments.TryGetValue(persona, out var files) ? files : attachments[persona] = new();
    private string persona;
    private sealed record ModelChoice(string Provider, string Model, string Label) { public override string ToString() => Label; }
    public ChatWindow(DesktopController controller, bool isQuick)
    {
        owner = controller; quick = isQuick; persona = owner.Skin.Id;
        draft.Text = owner.Drafts.GetValueOrDefault(persona, "");
        draft.TextChanged += (_, _) => owner.Drafts[persona] = draft.Text ?? "";
        Title = quick ? "DeskPet · 快聊" : "DeskPet · 聊天与队列"; Width = quick ? 470 : 880; Height = quick ? 520 : 730;
        MinWidth = 390; MinHeight = 400;
        Background = DesktopTheme.Surface; Foreground = DesktopTheme.Brush("#000000");
        draft.Name = "ChatDraft"; draft.AcceptsReturn = true; draft.MinHeight = 48; draft.MaxHeight = 160; draft.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        draft.Background = Brushes.Transparent; draft.BorderThickness = new Thickness(0); draft.Padding = new Thickness(4, 6); draft.UseFloatingPlaceholder = false;
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" }) draft.Resources[key] = Brushes.Transparent;
        draft.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        draft.AddHandler(InputElement.KeyDownEvent, DraftKeyDown, RoutingStrategies.Tunnel);
        draft.PastingFromClipboard += (_, e) => { if (nativePaste) return; e.Handled = true; owner.Run(PasteClipboardAsync); };
        ConfigureDraftMenu();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, AttachmentDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, AttachmentDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        history = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        partnerAvatar = new AvatarView { Name = "ChatPartnerAvatar", Assets = avatarAssets, Skin = owner.Skin, Width = 28, Height = 28 }; partnerName.Text = owner.Skin.Name; partnerName.VerticalAlignment = VerticalAlignment.Center;
        questions.Name = "AgentQuestions";
        var header = Ui.Column(Ui.Row(partnerAvatar, partnerName, Ui.Button("设置", owner.ShowSettings), Ui.Button(quick ? "完整聊天" : "快聊", () => owner.ShowChat(!quick))), status, questions);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        primary = Ui.Button("≋", () => { if (!string.IsNullOrWhiteSpace(draft.Text) || Files.Count > 0) Send(); else if (owner.Session.IsBusy) owner.Run(owner.StopAsync); else owner.ShowLive(); });
        primary.Name = "PrimaryChatAction"; primary.Width = primary.Height = 32; primary.Padding = new Thickness(0); primary.HorizontalContentAlignment = HorizontalAlignment.Center; primary.VerticalContentAlignment = VerticalAlignment.Center; primary.CornerRadius = new CornerRadius(16); primary.Background = DesktopTheme.Brush("#357FF5"); primary.Foreground = Avalonia.Media.Brushes.White;
        addAttachment = Ui.Button("+", OpenAttachmentMenu); addAttachment.Name = "AddAttachment"; addAttachment.Width = addAttachment.Height = 32; addAttachment.Padding = new Thickness(0); addAttachment.HorizontalContentAlignment = HorizontalAlignment.Center; addAttachment.VerticalContentAlignment = VerticalAlignment.Center; ToolTip.SetTip(addAttachment, "添加文件等内容");
        var filesMenu = new MenuItem { Name = "AttachmentFilesMenu", Header = "文件和文件夹", Icon = MenuIcon("M 7,5 L 7,12 Q 7,15 10,15 Q 13,15 13,12 L 13,4 Q 13,1 10,1 Q 7,1 7,4 L 7,11 Q 7,13 9,13 Q 11,13 11,11 L 11,5") }; filesMenu.Click += (_, _) => owner.Run(PickAttachmentsAsync);
        snapshotMenu = new MenuItem { Name = "AttachApplicationSnapshot", Header = "附加应用快照", IsEnabled = false }; snapshotMenu.Click += (_, _) => owner.Run(AttachApplicationSnapshotAsync); SetSnapshotTarget(null);
        goalMenu = new MenuItem { Name = "SetAgentGoal", Header = MenuLabel("目标", "设置要持续追求的目标"), Icon = MenuIcon("M 14,9 A 5,5 0 1 1 9,4 M 9,9 L 16,2 M 12,2 L 16,2 L 16,6") }; goalMenu.Click += (_, _) => Try(ShowGoalDialog);
        planMode = new MenuItem { Name = "AgentPlanMode", Header = MenuLabel("计划模式", "只读分析，不修改文件"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = owner.Settings.AgentPlanMode };
        planMode.Click += (_, _) => { owner.Settings.AgentPlanMode = planMode.IsChecked; owner.Save(); };
        var addMenu = new ContextMenu { ItemsSource = new object[] { new MenuItem { Header = "添加", IsEnabled = false }, filesMenu, snapshotMenu, goalMenu, planMode } };
        addMenu.Opening += (_, _) => { planMode.IsChecked = owner.Settings.AgentPlanMode; goalMenu.Header = MenuLabel("目标", string.IsNullOrWhiteSpace(owner.Settings.AgentGoal) ? "设置要持续追求的目标" : owner.Settings.AgentGoal); SetSnapshotTarget(OperatingSystem.IsWindowsVersionAtLeast(6, 1) ? ApplicationSnapshots.Topmost() : null); };
        addAttachment.ContextMenu = addMenu;
        attachmentChips.Name = "AttachmentCards";
        modeButton = Ui.Button("", OpenModeMenu); modeButton.Name = "ChatModeAction"; modeButton.Width = 104; modeButton.Height = 32; modeButton.Padding = new Thickness(4, 0); modeButton.Background = Brushes.Transparent; modeButton.BorderThickness = new Thickness(0); modeButton.Foreground = DesktopTheme.Brush("#9299A3"); modeButton.HorizontalContentAlignment = HorizontalAlignment.Center; modeButton.VerticalContentAlignment = VerticalAlignment.Center;
        conversationMode = new MenuItem { Name = "ConversationMode", Icon = ModeIcon(false), Header = MenuLabel("对话模式", "纯聊天，不调用文件 Agent"), ToggleType = MenuItemToggleType.Radio, GroupName = "ChatMode" }; conversationMode.Click += (_, _) => SelectMode(false);
        workMode = new MenuItem { Name = "WorkMode", Icon = ModeIcon(true), Header = MenuLabel("工作模式", "按工作设置权限使用文件/目录工具"), ToggleType = MenuItemToggleType.Radio, GroupName = "ChatMode", IsEnabled = OperatingSystem.IsWindows() }; workMode.Click += (_, _) => SelectMode(true);
        ToolTip.SetTip(workMode, "文件 Agent 目前仅 Windows 版支持。");
        var modeMenu = new ContextMenu { ItemsSource = new[] { conversationMode, workMode } }; modeMenu.Opening += (_, _) => RefreshMode(); modeButton.ContextMenu = modeMenu;
        var dictate = Ui.AsyncButton("🎙", DictateAsync, owner.Error); dictate.Name = "ChatDictation"; dictate.Width = dictate.Height = 32; dictate.Padding = new Thickness(0); dictate.HorizontalContentAlignment = HorizontalAlignment.Center; dictate.VerticalContentAlignment = VerticalAlignment.Center; ToolTip.SetTip(dictate, "听写 / 完成录音");
        var toolbar = new Grid { Name = "ComposerToolbar", ColumnDefinitions = new ColumnDefinitions("36,104,*,Auto"), Margin = new Thickness(2, 8, 2, 0) };
        toolbar.Children.Add(addAttachment); Grid.SetColumn(modeButton, 1); toolbar.Children.Add(modeButton); models.Name = "ChatModels"; models.MinWidth = 0; models.MaxWidth = 260; models.Margin = new Thickness(0, 0, 4, 0); models.RenderTransform = new TranslateTransform(0, 4); models.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(models, 2); toolbar.Children.Add(models);
        var actions = Ui.Row(dictate, primary); Grid.SetColumn(actions, 3); toolbar.Children.Add(actions);
        var composer = new Border { Name = "ComposerSurface", Background = DesktopTheme.Surface, CornerRadius = new CornerRadius(22), BorderBrush = DesktopTheme.Brush("#E3E7ED"), BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = Ui.Column(attachmentChips, draft, toolbar) };
        draft.TextChanged += (_, _) => RefreshAction();
        DockPanel.SetDock(composer, Dock.Bottom); root.Children.Add(composer);
        if (!quick)
        {
            var sidebar = Ui.Column(Ui.Text("待发送队列", 17), Ui.Row(Ui.Button("继续", () => Try(owner.Queue.Resume)), Ui.Button("暂停", () => Try(owner.Queue.Hold))), new ScrollViewer { Content = queue, MaxHeight = 400 }, Ui.AsyncButton("导出聊天", ExportAsync, owner.Error), Ui.Button("清空记录", ConfirmClear));
            sidebar.Width = 245; sidebar.Margin = new Thickness(18, 8, 0, 8); DockPanel.SetDock(sidebar, Dock.Right); root.Children.Add(sidebar);
        }
        root.Children.Add(history); chatPage = root; imageWorkspace = new ImageWorkspacePage(owner, this);
        var shell = new DockPanel();
        routeBack = Ui.Button("‹", () => NavigateHistory(-1)); routeBack.Name = "PageBack"; ToolTip.SetTip(routeBack, "返回");
        routeForward = Ui.Button("›", () => NavigateHistory(1)); routeForward.Name = "PageForward"; ToolTip.SetTip(routeForward, "前进");
        var imagesNav = Ui.Button("图像", () => Navigate("images")); imagesNav.Name = "ImagesNavigation";
        var nav = Ui.Row(routeBack, routeForward, Ui.Button("对话", () => Navigate("chat")), imagesNav); nav.Margin = new Thickness(16, 8, 16, 0); DockPanel.SetDock(nav, Dock.Top); shell.Children.Add(nav); shell.Children.Add(pageHost); Content = shell; ApplyRoute();
        models.SelectionChanged += (_, _) =>
        {
            if (models.SelectedItem is not ModelChoice item || owner.Settings.ActiveTextProviderId == item.Provider && owner.Settings.Text.Model == item.Model) return;
            if (owner.Session.IsBusy || owner.Live.IsActive) { owner.Error(new InvalidOperationException("请先停止当前对话再切换模型。")); RefreshModels(); return; }
            owner.Settings.SelectTextModel(item.Provider, item.Model); owner.Save();
        };
        owner.RegisterChatHost(this);
        owner.Changed += Refresh; Closed += (_, _) => { closed = true; owner.ChatHostClosed(this); imageWorkspace.Dispose(); foreach (var view in rendered.Values) view.DisposeImages(); uploadEpoch++; snapshotCancel.Cancel(); snapshotCancel.Dispose(); snapshotIcon?.Dispose(); owner.Drafts[persona] = draft.Text ?? ""; owner.Changed -= Refresh; DisposeAttachmentImages(); avatarAssets.Dispose(); if (owner.Dictation.Owns(this)) owner.Run(() => owner.Dictation.CancelAsync(this)); };
        Refresh();
        history.SizeChanged += (_, _) => ResizeBubbles();
    }
    internal string CurrentPage => routes[routeIndex];
    internal string QuestionSessionId => owner.Session.KernelSession?.SessionId ?? owner.Session.PersonaId;
    internal ImageWorkspacePage Images => imageWorkspace;
    internal void ShowImages(GeneratedImage? image = null) { Navigate("images"); if (image != null) imageWorkspace.Prepare(image); }
    private void Navigate(string route) { if (CurrentPage == route) return; routes.RemoveRange(routeIndex + 1, routes.Count - routeIndex - 1); routes.Add(route); routeIndex++; ApplyRoute(); }
    internal void NavigateHistory(int delta) { var next = routeIndex + delta; if (next < 0 || next >= routes.Count) return; routeIndex = next; ApplyRoute(); }
    private void ApplyRoute() { pageHost.Content = null; pageHost.Content = CurrentPage == "images" ? imageWorkspace : chatPage; routeBack.IsEnabled = routeIndex > 0; routeForward.IsEnabled = routeIndex < routes.Count - 1; }
    private void Try(Action action) { try { action(); } catch (Exception e) { owner.Error(e); } }
    private void OpenAttachmentMenu() => addAttachment.ContextMenu!.Open(addAttachment);
    private void OpenModeMenu() => modeButton.ContextMenu!.Open(modeButton);
    internal void SetSnapshotTarget(ApplicationSnapshotTarget? target)
    {
        snapshotTarget = OperatingSystem.IsWindows() ? target : null; snapshotIcon?.Dispose(); snapshotIcon = null;
        snapshotMenu.Header = snapshotTarget == null ? "附加应用快照" : "附加 " + snapshotTarget.AppName;
        snapshotMenu.Icon = MenuIcon("M 3,7 L 3,3 L 7,3 M 11,3 L 15,3 L 15,7 M 15,11 L 15,15 L 11,15 M 7,15 L 3,15 L 3,11");
        if (snapshotTarget?.IconPng is { Length: > 0 } png)
        {
            try { using var stream = new MemoryStream(png); snapshotIcon = Bitmap.DecodeToWidth(stream, 20); snapshotMenu.Icon = new Image { Source = snapshotIcon, Width = 18, Height = 18, Stretch = Stretch.Uniform }; }
            catch (Exception e) when (e is ArgumentException or IOException) { }
        }
        snapshotMenu.IsEnabled = snapshotTarget != null && !owner.Live.IsActive && !owner.Dictation.IsBusy;
        ToolTip.SetTip(snapshotMenu, !OperatingSystem.IsWindows() ? "应用窗口快照目前仅 Windows 版支持。" : snapshotTarget == null ? "没有可附加的其他应用窗口。" : snapshotTarget.Title);
    }
    private async Task AttachApplicationSnapshotAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) throw new PlatformNotSupportedException("应用窗口快照目前仅 Windows 版支持。");
        var context = UploadTarget(); var target = snapshotTarget; if (target == null || !Current(context)) return;
        if (Files.Count >= 4) throw new ApiException("最多添加 4 个附件。");
        var png = await ApplicationSnapshots.CaptureAsync(target, snapshotCancel.Token);
        if (Current(context)) AddAttachments(new[] { ChatAttachment.CreateImage("应用快照-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", png) });
    }
    private void ConfigureDraftMenu()
    {
        MenuItem Item(string name, string title, Key key, Action action)
        {
            var item = new MenuItem { Name = name, Header = title, InputGesture = new KeyGesture(key, KeyModifiers.Control), Padding = new Thickness(10, 6) }; item.Click += (_, _) => action(); return item;
        }
        var cut = Item("DraftCut", "剪切", Key.X, draft.Cut); var copy = Item("DraftCopy", "复制", Key.C, draft.Copy);
        var paste = Item("DraftPaste", "粘贴", Key.V, draft.Paste); var all = Item("DraftSelectAll", "全选", Key.A, draft.SelectAll);
        var menu = new ContextMenu { CornerRadius = new CornerRadius(10), Padding = new Thickness(4), Background = DesktopTheme.Surface, ItemsSource = new object[] { cut, copy, paste, new Separator(), all } };
        menu.Opening += (_, _) => { cut.IsEnabled = !draft.IsReadOnly && draft.SelectionStart != draft.SelectionEnd; copy.IsEnabled = draft.SelectionStart != draft.SelectionEnd; paste.IsEnabled = !draft.IsReadOnly && Clipboard != null; all.IsEnabled = !string.IsNullOrEmpty(draft.Text); }; draft.ContextMenu = menu;
    }
    private UploadContext UploadTarget() => new(persona, uploadEpoch);
    private bool Current(UploadContext context) => !closed && IsVisible && persona == context.Persona && owner.Skin.Id == context.Persona && uploadEpoch == context.Epoch;
    private void PasteNativeText()
    {
        nativePaste = true; try { draft.Paste(); } finally { nativePaste = false; }
    }
    internal async Task PasteClipboardAsync()
    {
        var context = UploadTarget(); if (!Current(context) || draft.IsReadOnly || Clipboard == null) return;
        using var data = await Clipboard.TryGetDataAsync(); if (!Current(context)) return;
        if (data == null || !data.Contains(DataFormat.File) && !data.Contains(DataFormat.Bitmap)) { PasteNativeText(); return; }
        if (data.Contains(DataFormat.File))
        {
            var files = await data.TryGetFilesAsync(); if (!Current(context)) return;
            if (files is { Length: > 0 }) { await LoadAttachmentPathsAsync(files.Select(f => f.TryGetLocalPath() ?? throw new ApiException("请先将文件保存到本机。")), context); return; }
        }
        if (data.Contains(DataFormat.Bitmap))
        {
            var bitmap = await data.TryGetBitmapAsync(); if (!Current(context)) return;
            if (bitmap != null) { AddAttachments(new[] { ImageAttachment(bitmap) }); return; }
        }
        PasteNativeText();
    }
    private static ChatAttachment ImageAttachment(Bitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions());
        return ChatAttachment.CreateImage("粘贴图片-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", stream.ToArray());
    }
    private void AttachmentDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File) && !e.DataTransfer.Contains(DataFormat.Bitmap)) return;
        e.DragEffects = !closed && !owner.Live.IsActive && !owner.Dictation.IsBusy ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
    }
    private void AttachmentDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File) && !e.DataTransfer.Contains(DataFormat.Bitmap)) return;
        e.Handled = true; e.DragEffects = DragDropEffects.Copy;
        // Snapshot synchronous drag data before returning; the platform owns its lifetime.
        try
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files is { Length: > 0 }) { var paths = files.Select(f => f.TryGetLocalPath() ?? throw new ApiException("请先将文件保存到本机。")).ToArray(); owner.Run(() => AddAttachmentPathsAsync(paths)); return; }
            var bitmap = e.DataTransfer.TryGetBitmap(); if (bitmap != null) AddAttachments(new[] { ImageAttachment(bitmap) });
        }
        catch (Exception ex) { owner.Error(ex); }
    }
    private void DraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && OperatingSystem.IsWindows()) { SelectMode(!owner.Settings.AgentEnabled); e.Handled = true; return; }
        if (e.Key == Key.Enter && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))) { Send(); e.Handled = true; }
    }
    private void SelectMode(bool work)
    {
        if (work && !OperatingSystem.IsWindows()) return;
        owner.Settings.AgentEnabled = work; owner.Save(); RefreshMode();
    }
    private void RefreshMode()
    {
        bool work = OperatingSystem.IsWindows() && owner.Settings.AgentEnabled;
        var label = new TextBlock { Text = work ? "工作模式" : "对话模式", FontSize = 12, Foreground = DesktopTheme.Brush("#9299A3"), VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform(0, 4) };
        var content = Ui.Row(ModeIcon(work), label); content.RenderTransform = new TranslateTransform(-4, 0); modeButton.Content = content;
        conversationMode.IsChecked = !work; workMode.IsChecked = work;
        ToolTip.SetTip(modeButton, OperatingSystem.IsWindows() ? (work ? "工作模式：按工作设置权限使用文件/目录工具。" : "对话模式：纯聊天。") + "Tab 切换；仅影响下一次请求。" : "对话模式：纯聊天。工作模式目前仅 Windows 版支持；Windows 版可用 Tab 切换。");
    }
    private static Control ModeIcon(bool work)
    {
        var icon = MenuIcon(work ? "M 3,12 L 12,3 L 15,6 L 6,15 L 2,16 Z M 10,5 L 13,8 M 3,12 L 6,15" : "M 3,3 L 15,3 L 15,12 L 8,12 L 4,16 L 4,12 L 3,12 Z");
        icon.Name = work ? "WorkPencilGlyph" : "ConversationBubbleGlyph"; return icon;
    }
    private static Control MenuIcon(string path) => new ShapePath { Width = 18, Height = 18, Data = Geometry.Parse(path), Stroke = DesktopTheme.Brush("#777F89"), StrokeThickness = 1.3 };
    private static Control MenuLabel(string title, string description)
    {
        var secondary = new TextBlock { Text = description, Foreground = DesktopTheme.Brush("#9CA3AC"), FontSize = 12, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        return Ui.Row(new TextBlock { Text = title }, secondary);
    }
    private void ShowGoalDialog()
    {
        var input = Ui.Input(owner.Settings.AgentGoal ?? "", "输入目标；留空可清除"); input.Name = "AgentGoalInput"; input.AcceptsReturn = true; input.MinHeight = 100; input.TextWrapping = TextWrapping.Wrap;
        var dialog = Ui.Window("设置持续目标", new Border(), 470, 310); dialog.Name = "AgentGoalDialog";
        var save = Ui.Button("保存", () => { owner.Settings.AgentGoal = (input.Text ?? "").Trim(); owner.Save(); dialog.Close(); }); save.Name = "SaveAgentGoal";
        var clear = Ui.Button("清除目标", () => input.Text = ""); clear.Name = "ClearAgentGoal";
        dialog.Content = new Border { Padding = new Thickness(20), Child = Ui.Column(Ui.Text("目标将在后续对话中持续提供给模型。保存后不会自动发送消息。", 12), input, Ui.Row(clear, Ui.Button("取消", dialog.Close), save)) };
        _ = dialog.ShowDialog(this);
    }
    private void RefreshModels()
    {
        var all = owner.Settings.TextProviders.SelectMany(p => p.Models.Select(m => new ModelChoice(p.Id, m, p.Name + " · " + p.DisplayName(m)))).ToList();
        models.ItemsSource = all; models.SelectedItem = all.FirstOrDefault(m => m.Provider == owner.Settings.ActiveTextProviderId && m.Model == owner.Settings.Text.Model);
    }
    private void Send()
    {
        if (owner.Live.IsActive) { owner.Error(new InvalidOperationException("请先结束 Live 语音对话。")); return; }
        if (owner.Dictation.IsBusy) { owner.Error(new InvalidOperationException("请先完成或停止听写。")); return; }
        var text = (draft.Text ?? "").Trim(); if (text.Length == 0 && Files.Count == 0) return;
        try { owner.Queue.Enqueue(text, Files); uploadEpoch++; Files.Clear(); RefreshAttachments(); draft.Text = ""; RefreshAction(); } catch (Exception e) { owner.Error(e); }
    }
    private async Task PickAttachmentsAsync()
    {
        var context = UploadTarget();
        if (OperatingSystem.IsWindows()) { var paths = AttachmentPicker.Show(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero); if (Current(context)) await LoadAttachmentPathsAsync(paths, context); return; }
        if (OperatingSystem.IsMacOS()) { var paths = NativeAttachmentPicker.ShowMac(); if (Current(context)) await LoadAttachmentPathsAsync(paths, context); return; }
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "添加文件或图片", AllowMultiple = true, FileTypeFilter = new[] { new FilePickerFileType("图片、PDF、DOCX、文字与代码") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.pdf", "*.docx", "*.txt", "*.md", "*.csv", "*.tsv", "*.json", "*.xml", "*.yaml", "*.yml", "*.log", "*.cs", "*.py", "*.js", "*.ts", "*.tsx", "*.jsx", "*.html", "*.css", "*.sql", "*.ps1", "*.sh", "*.toml", "*.ini", "*.cfg" } } } });
        if (!Current(context) || picked.Count == 0) return;
        await LoadAttachmentPathsAsync(picked.Select(f => f.TryGetLocalPath() ?? throw new ApiException("请先将文件保存到本机。")), context);
    }
    private static ChatAttachment FolderReference(string path)
    {
        var label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (label.Length > 120) label = label[..120];
        var note = OperatingSystem.IsWindows() ? "此附件仅引用目录路径，未读取目录内容，也不会改变工作模式的访问范围。" : "此附件仅引用目录路径，未读取目录内容。文件 Agent 的目录读写目前仅 Windows 版支持。";
        return ChatAttachment.FromText((string.IsNullOrEmpty(label) ? "文件夹" : label) + "-目录引用.txt", "目录引用：" + path + "\n" + note);
    }
    internal Task AddAttachmentPathsAsync(IEnumerable<string> paths) => LoadAttachmentPathsAsync(paths, UploadTarget());
    private async Task LoadAttachmentPathsAsync(IEnumerable<string> paths, UploadContext context)
    {
        if (!Current(context)) return;
        if (owner.Live.IsActive || owner.Dictation.IsBusy) throw new InvalidOperationException("请先结束 Live 对话或听写。");
        var selected = paths.ToArray(); if (selected.Length == 0) return;
        if (selected.Length + Files.Count > 4) throw new ApiException("最多添加 4 个附件。");
        var folders = selected.Where(Directory.Exists).ToArray();
        if (folders.Length > 1) throw new ApiException("一次只能选择一个工作文件夹；本次没有添加附件。");
        var loaded = await Task.Run(() => selected.Select(p => Directory.Exists(p) ? FolderReference(p) : ChatAttachment.Read(p)).ToList());
        if (!Current(context)) return;
        AddAttachments(loaded);
    }
    internal void AddAttachments(IEnumerable<ChatAttachment> loaded)
    {
        if (closed) return;
        if (owner.Live.IsActive || owner.Dictation.IsBusy) throw new InvalidOperationException("请先结束 Live 对话或听写。");
        var combined = ChatAttachment.Snapshot(Files.Concat(loaded)); Files.Clear(); Files.AddRange(combined); RefreshAttachments(); RefreshAction();
    }
    private void DisposeAttachmentImages() { foreach (var bitmap in attachmentImages) bitmap.Dispose(); attachmentImages.Clear(); }
    private void RefreshAttachments()
    {
        attachmentChips.Children.Clear(); DisposeAttachmentImages(); attachmentChips.IsVisible = Files.Count > 0;
        foreach (var file in Files)
        {
            Control preview = new Border { Height = 72, Background = DesktopTheme.Brush("#F7F8FA"), Child = new ShapePath { Data = Geometry.Parse("M 5,2 L 11,2 L 15,6 L 15,18 L 5,18 Z M 11,2 L 11,6 L 15,6"), Stroke = DesktopTheme.Brush("#818894"), StrokeThickness = 1.2, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            if (file.IsImage)
            {
                try
                {
                    using var data = new MemoryStream(Convert.FromBase64String(file.Data)); var bitmap = new Bitmap(data); attachmentImages.Add(bitmap);
                    preview = new Image { Name = "AttachmentThumbnail", Source = bitmap, Height = 72, Stretch = Stretch.UniformToFill };
                }
                catch (Exception e) when (e is FormatException or ArgumentException or IOException) { }
            }
            var label = new TextBlock { Text = file.Name, FontSize = 11, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 5, 8, 4) };
            var grid = new Grid(); var content = new StackPanel { Spacing = 0 }; content.Children.Add(preview); content.Children.Add(label); grid.Children.Add(content);
            var remove = Ui.Button("×", () => { Files.Remove(file); RefreshAttachments(); RefreshAction(); }); remove.Name = "RemoveAttachment"; remove.Width = remove.Height = 22; remove.Padding = new Thickness(0); remove.Margin = new Thickness(0, 3, 3, 0); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top; remove.HorizontalContentAlignment = HorizontalAlignment.Center; remove.VerticalContentAlignment = VerticalAlignment.Center; remove.CornerRadius = new CornerRadius(11); remove.Background = DesktopTheme.Brush("#F2FFFFFF"); ToolTip.SetTip(remove, "移除 " + file.Name); grid.Children.Add(remove);
            var card = new Border { Name = "AttachmentCard", Width = file.IsImage ? 120 : 150, Height = 100, Margin = new Thickness(0, 0, 8, 8), BorderBrush = DesktopTheme.Brush("#E5E7EB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = grid }; ToolTip.SetTip(card, file.Name); attachmentChips.Children.Add(card);
        }
    }
    private void RefreshAction()
    {
        RefreshMode();
        bool hasDraft = !string.IsNullOrWhiteSpace(draft.Text) || Files.Count > 0;
        primary.Content = hasDraft ? "↑" : owner.Session.IsBusy ? "■" : "≋";
        ToolTip.SetTip(primary, hasDraft ? "发送 / 排队" : owner.Session.IsBusy ? "停止" : "Live 通话");
        addAttachment.IsEnabled = !owner.Live.IsActive && !owner.Dictation.IsBusy;
    }
    private void ResizeBubbles() { foreach (var view in rendered.Values) if (view.IsUser) view.MaxWidth = Math.Max(1, history.Bounds.Width / 2); }
    private async Task DictateAsync()
    {
        var expectedPersona = owner.Skin.Id;
        if (owner.Live.IsActive) throw new InvalidOperationException("请先结束 Live。");
        if (owner.Dictation.Owns(this)) { var text = await owner.Dictation.FinishAsync(this); draft.Text = (draft.Text ?? "") + text; return; }
        if (owner.Session.IsBusy) throw new InvalidOperationException("请先停止当前对话。");
        var granted = await owner.RequestMicrophoneAccessAsync();
        if (!IsVisible || owner.Skin.Id != expectedPersona) return;
        if (!granted) throw new InvalidOperationException("麦克风权限未授予。请在系统设置 → 隐私与安全性 → 麦克风中允许 DeskPet。");
        if (owner.Live.IsActive || owner.Session.IsBusy || owner.Dictation.IsBusy) throw new InvalidOperationException("另一处对话或听写已经开始，请先结束它。");
        owner.Queue.Hold(); owner.Dictation.Start(this);
    }
    private void Refresh()
    {
        RefreshQuestions();
        partnerAvatar.Skin = owner.Skin; partnerName.Text = owner.Skin.Name;
        if (persona != owner.Skin.Id) { uploadEpoch++; owner.Drafts[persona] = draft.Text ?? ""; persona = owner.Skin.Id; draft.Text = owner.Drafts.GetValueOrDefault(persona, ""); foreach (var oldView in rendered.Values) oldView.DisposeImages(); rendered.Clear(); messages.Children.Clear(); RefreshAttachments(); }
        status.Text = owner.Session.Stage + (owner.Dictation.IsBusy ? " · 听写 " + owner.Dictation.Stage : "") + " · " + owner.Session.LastNotice + " " + owner.Dictation.LastNotice + " " + owner.Queue.Notice + " " + owner.Session.HistoryWarning;
        Title = "DeskPet · " + owner.Skin.Name + (quick ? " · 快聊" : " · 完整聊天");
        var followBottom = history.Offset.Y + history.Viewport.Height + 80 >= history.Extent.Height;
        RefreshModels(); RefreshAction();
        var visible = owner.Session.History.Messages.TakeLast(quick ? 20 : 400).ToList();
        foreach (var stale in rendered.Keys.Where(id => !visible.Any(m => m.Id == id)).ToList()) { rendered[stale].DisposeImages(); messages.Children.Remove(rendered[stale]); rendered.Remove(stale); }
        foreach (var message in visible)
        {
            if (rendered.TryGetValue(message.Id, out var view)) view.Update(message, owner.Settings.AgentDetailedProcess);
            else { view = new ChatMessageView(message, owner, this); rendered[message.Id] = view; messages.Children.Add(view); }
        }
        ResizeBubbles();
        if (followBottom) Dispatcher.UIThread.Post(() => history.ScrollToEnd());
        if (quick) return;
        queue.Children.Clear(); var items = owner.Queue.Items.ToList();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i]; var index = i;
            var edit = Ui.Input(queuedEdits.GetValueOrDefault(item.Id, item.Text)); edit.AcceptsReturn = true; edit.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            edit.TextChanged += (_, _) => queuedEdits[item.Id] = edit.Text ?? "";
            queue.Children.Add(Ui.Column(edit,
                Ui.Row(Ui.Button("保存", () => Try(() => { owner.Queue.Edit(item.Id, edit.Text ?? ""); queuedEdits.Remove(item.Id); })), Ui.Button("删除", () => Try(() => { owner.Queue.Remove(item.Id); queuedEdits.Remove(item.Id); })), Ui.Button("↑", () => { if (index > 0) Try(() => owner.Queue.MoveBefore(item.Id, items[index - 1].Id)); })),
                Ui.Row(Ui.Button("↓", () => { if (index < items.Count - 1) Try(() => owner.Queue.MoveBefore(item.Id, index + 2 < items.Count ? items[index + 2].Id : null!)); }), Ui.AsyncButton("立即引导", () => owner.Queue.InterruptAsync(edit.Text ?? "", item.Id), owner.Error))));
        }
    }
    private string questionSignature = "";
    private void RefreshQuestions()
    {
        var currentSession = QuestionSessionId;
        var pending = owner.Questions.Requests.Where(q => q.SessionId == currentSession).ToList();
        var signature = string.Join("|", pending.Select(q => q.SessionId + "/" + q.TaskId + "/" + q.CallId));
        if (signature == questionSignature) return; questionSignature = signature; questions.Children.Clear(); questions.IsVisible = pending.Count > 0;
        foreach (var question in pending)
        {
            void Submit(AgentQuestionAnswer answer) { if (!owner.Questions.Submit(question.SessionId, question.TaskId, question.CallId, answer)) owner.Error(new InvalidOperationException("此问题已结束或答案无效。")); }
            var panel = Ui.Column(Ui.Text("需要你的选择", 15), Ui.Text(question.Question)); var options = new WrapPanel();
            for (int i = 0; i < question.Options.Length; i++) { int index = i; var button = Ui.Button(question.Options[i], () => Submit(new AgentQuestionAnswer { OptionIndex = index })); button.Margin = new Thickness(0, 0, 4, 4); options.Children.Add(button); }
            panel.Children.Add(options);
            if (question.AllowFreeInput) { var answer = Ui.Input("", "输入回复"); answer.MinWidth = 0; panel.Children.Add(answer); panel.Children.Add(Ui.Button("回复", () => { if (!string.IsNullOrWhiteSpace(answer.Text)) Submit(new AgentQuestionAnswer { Text = answer.Text.Trim() }); })); }
            panel.Children.Add(Ui.Button("取消任务", () => { owner.Questions.CancelTask(question.TaskId); owner.Run(owner.Session.StopAsync); }));
            questions.Children.Add(new Border { Name = "AgentQuestionCard", Padding = new Thickness(10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = DesktopTheme.Brush("#D8E5F9"), Child = panel });
        }
    }
    private async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出聊天记录", SuggestedFileName = owner.Skin.Id + "-chat.md", DefaultExtension = "md" });
        if (file == null) return; await using var stream = await file.OpenWriteAsync(); using var writer = new StreamWriter(stream); await writer.WriteAsync(owner.Session.ExportHistory());
    }
    private void ConfirmClear()
    {
        var confirm = Ui.Window("清空当前伙伴聊天记录", Ui.Text("此操作会删除当前伙伴保存的聊天记录。"), 420, 240);
        confirm.Content = new Border { Padding = new Thickness(20), Child = Ui.Column(Ui.Text("确定清空 " + owner.Skin.Name + " 的聊天记录？"), Ui.Button("确认清空", () => { Try(owner.Session.Clear); confirm.Close(); }), Ui.Button("取消", confirm.Close)) }; confirm.Show();
    }
}
