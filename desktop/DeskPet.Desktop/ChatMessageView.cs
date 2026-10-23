using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Automation;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ChatMessageView : Border
{
    private readonly MarkdownView body = new();
    private readonly SelectableTextBlock reasoning = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = DesktopTheme.Brush("#000000") };
    private readonly TextBlock preview = new() { Name = "ProcessPreview", TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14, Foreground = DesktopTheme.Brush("#000000") };
    private readonly TextBlock thinkingLabel = new() { Name = "ProcessStatus", FontSize = 12, Foreground = DesktopTheme.Brush("#89929F") };
    private readonly Border previewRow;
    private readonly StackPanel processArea = new() { Name = "ThinkingProcess" };
    private string previousReasoning = "", previousTools = "";
    private bool latestIsTool;
    private readonly TextBlock status = new() { FontSize = 11 };
    private readonly TextBlock elapsed = new() { Name = "ReplyElapsedTime", FontSize = 12, Foreground = DesktopTheme.Brush("#969BA3") };
    private readonly Avalonia.Threading.DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Border elapsedRow;
    private readonly Expander thinking;
    private readonly StackPanel tools = new() { Spacing = 3 };
    private readonly Dictionary<string, Expander> toolRows = new();
    private readonly StackPanel replyActions;
    private readonly Button readReply;
    private readonly MenuItem regenerate;
    private readonly MenuItem showReasoning;
    private readonly DesktopController owner;
    private readonly Window window;
    private readonly Border? userBubble;
    private readonly WrapPanel sentAttachments = new() { Name = "SentAttachments", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly List<Bitmap> sentImages = new();
    private List<ChatAttachment> displayedAttachments = new();
    private readonly StackPanel generatedImages = Ui.Column();
    private readonly ContentControl jobHost=new();private DesktopAgentJobsPanel? jobs;
    private GeneratedImageGroupCard? imageGroup;
    private ChatMessage current;
    private bool reading, showHistory;
    private bool? detailedSetting;
    private readonly ConversationSession session;private readonly ConversationQueue queue;private readonly Func<PetSettings> settings;
    public bool IsUser { get; }
    public ChatMessageView(ChatMessage message, DesktopController controller, Window host,ConversationSession? boundSession=null,ConversationQueue? boundQueue=null,Func<PetSettings>? boundSettings=null)
    {
        owner = controller; window = host; current = message;session=boundSession??owner.Session;queue=boundQueue??owner.Queue;settings=boundSettings??(()=>owner.Settings);
        IsUser = message.Role == "user"; Name = IsUser ? "UserMessage" : "AssistantMessage";
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0); CornerRadius = new CornerRadius(18);
        HorizontalAlignment = IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        Padding = IsUser ? new Thickness(0) : new Thickness(2, 8);
        Margin = new Thickness(0, 0, 0, 20);
        previewRow = new Border { BorderBrush = DesktopTheme.Brush("#ECECEC"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0), Margin = new Thickness(0, 5, 0, 0), Child = preview };
        thinking = new Expander { Name = "ReasoningDetails", Header = thinkingLabel, Content = Ui.Column(new ScrollViewer { Content = reasoning, MaxHeight = 240 }, tools), IsExpanded = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ProcessExpander.Apply(thinking);
        thinking.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) previewRow.IsVisible = !thinking.IsExpanded && !string.IsNullOrEmpty(preview.Text); };
        processArea.Children.Add(thinking); processArea.Children.Add(previewRow);
        var panel = Ui.Column(); panel.Spacing = IsUser ? 3 : 10;
        elapsedRow = new Border { Child = elapsed, BorderBrush = DesktopTheme.Brush("#ECECEC"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8) };
        if (!IsUser) { panel.Children.Add(elapsedRow); panel.Children.Add(processArea); }
        clock.Tick += (_, _) => RefreshElapsed(); AttachedToVisualTree += (_, _) => RefreshElapsed(); DetachedFromVisualTree += (_, _) => clock.Stop();
        panel.Children.Add(sentAttachments); RefreshSentAttachments(message.Attachments);
        if (IsUser) { userBubble = new Border { Name = "UserBubble", Background = DesktopTheme.Brush("#E8F2FF"), CornerRadius = new CornerRadius(18), Padding = new Thickness(15, 10), HorizontalAlignment = HorizontalAlignment.Right, Child = body }; panel.Children.Add(userBubble); }
        else panel.Children.Add(body); if (!IsUser) { generatedImages.Name = "MessageGeneratedImages"; panel.Children.Add(generatedImages); panel.Children.Add(jobHost); panel.Children.Add(status); }
        Ui.ReadOnlyMenu(reasoning);
        var copy = IconButton("CopyReply", TextResources.Get("chatMessageView.chatMessageView.iconButton"), "M 6,3 L 14,3 L 14,12 L 6,12 Z M 3,6 L 3,15 L 11,15", () => owner.Run(() => CopyAsync(false)));
        readReply = IconButton("ReadReply", TextResources.Get("chatMessageView.chatMessageView.iconButton.alternativeA"), "M 3,7 L 6,7 L 10,3 L 10,15 L 6,11 L 3,11 Z M 13,5 Q 18,9 13,13", ReadReply);
        var more = IconButton("MoreReply", TextResources.Get("chatMessageView.chatMessageView.iconButton.alternativeB"), "M 3,9 L 4,9 M 8,9 L 9,9 M 13,9 L 14,9", () => { });
        regenerate = new MenuItem { Header = TextResources.Get("chatMessageView.chatMessageView.header") };
        regenerate.Click += (_, _) => owner.Run(async () => { EnsureIdle(); queue.Hold(); await session.RegenerateAsync(current.Id, settings(), owner.Skin.Name); });
        var copyPlain = new MenuItem { Header = TextResources.Get("chatMessageView.chatMessageView.header.alternativeA") }; copyPlain.Click += (_, _) => owner.Run(() => CopyAsync(true));
        var export = new MenuItem { Header = TextResources.Get("chatMessageView.chatMessageView.header.alternativeB") }; export.Click += (_, _) => owner.Run(ExportAsync);
        showReasoning = new MenuItem { Header = TextResources.Get("chatMessageView.chatMessageView.header.alternativeC") };
        showReasoning.Click += (_, _) => { showHistory = !showHistory; thinking.IsExpanded = showHistory; Update(current); };
        var menu = new ContextMenu { ItemsSource = new object[] { regenerate, copyPlain, export, showReasoning } };
        menu.Opening += (_, _) => regenerate.IsEnabled = CanStart();
        more.ContextMenu = menu; more.Click += (_, _) => menu.Open(more);
        replyActions = new StackPanel { Name = "ReplyActions", Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 2) };
        replyActions.Children.Add(copy); replyActions.Children.Add(readReply); replyActions.Children.Add(more);
        if (!IsUser) panel.Children.Add(replyActions);
        else
        {
            var userActions = new StackPanel { Name = "UserMessageActions", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 5, Opacity = 0 };
            var sent = new TextBlock { Name = "UserMessageTime", Text = message.Timestamp.ToLocalTime().ToString("HH:mm"), Foreground = DesktopTheme.Brush("#969BA3"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(sent, message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")); userActions.Children.Add(sent);
            userActions.Children.Add(IconButton("CopyUserMessage", TextResources.Get("chatMessageView.chatMessageView.iconButton.alternativeC"), "M 6,3 L 14,3 L 14,12 L 6,12 Z M 3,6 L 3,15 L 11,15", () => owner.Run(() => CopyAsync(false))));
            panel.Children.Add(userActions);
            PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsPointerOverProperty || e.Property == Avalonia.Input.InputElement.IsKeyboardFocusWithinProperty) userActions.Opacity = IsPointerOver || IsKeyboardFocusWithin ? 1 : 0; };
        }
        Child = panel;
        Update(message);
    }
    private static Button IconButton(string name, string tip, string geometry, Action action)
    {
        var button = new Button { Name = name, Width = 28, Height = 28, Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(5), Foreground = DesktopTheme.Brush("#939BA7"), Content = new ShapePath { Data = Geometry.Parse(geometry), Stroke = DesktopTheme.Brush("#939BA7"), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 18, Height = 18 } };
        AutomationProperties.SetName(button, tip); ToolTip.SetTip(button, tip); button.Click += (_, _) => action(); return button;
    }
    private bool CanStart() => !session.IsBusy && !owner.Live.IsActive && !owner.Dictation.IsBusy;
    private void DisposeSentImages() { foreach (var image in sentImages) image.Dispose(); sentImages.Clear(); }
    internal void DisposeImages() { jobs?.Dispose();jobs=null;jobHost.Content=null; imageGroup?.Dispose(); DisposeSentImages(); }
    private void RefreshGeneratedImages(ChatMessage message)
    {
        if(imageGroup==null){imageGroup=new GeneratedImageGroupCard(owner,window);generatedImages.Children.Add(imageGroup);}imageGroup.Update(message);
    }
    private void RefreshSentAttachments(IList<ChatAttachment>? files)
    {
        files ??= Array.Empty<ChatAttachment>();
        if (files.Count == displayedAttachments.Count && files.Zip(displayedAttachments).All(p => p.First.Name == p.Second.Name && p.First.MediaType == p.Second.MediaType && p.First.Data == p.Second.Data && p.First.Text == p.Second.Text)) { sentAttachments.IsVisible = files.Count > 0; return; }
        sentAttachments.Children.Clear(); DisposeSentImages(); displayedAttachments = files.ToList(); sentAttachments.IsVisible = files.Count > 0;
        foreach (var file in files)
        {
            Control? child = null;
            if (file.IsImage)
            {
                try
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(file.Data)); var bitmap = Bitmap.DecodeToWidth(stream, 160); sentImages.Add(bitmap);
                    child = new Border { Name = "SentImageCard", Width = 80, Height = 80, CornerRadius = new CornerRadius(10), BorderBrush = DesktopTheme.Brush("#E0E4E9"), BorderThickness = new Thickness(1), Background = DesktopTheme.Surface, Clip = new RectangleGeometry(new Rect(0, 0, 80, 80), 10, 10), Child = new Image { Name = "SentImageThumbnail", Source = bitmap, Stretch = Stretch.UniformToFill } };
                }
                catch (Exception e) when (e is FormatException or ArgumentException or IOException) { }
            }
            if (child == null)
            {
                var icon = new ShapePath { Width = 18, Height = 22, Stretch = Stretch.Uniform, Data = Geometry.Parse("M 3,1 L 11,1 L 16,6 L 16,20 L 3,20 Z M 11,1 L 11,6 L 16,6"), Stroke = DesktopTheme.Brush("#858D99"), StrokeThickness = 1.2 };
                var name = new TextBlock { Name = "SentFileName", Text = file.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140, VerticalAlignment = VerticalAlignment.Center };
                var fileRow = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*") }; fileRow.Children.Add(icon); Grid.SetColumn(name, 1); fileRow.Children.Add(name);
                child = new Border { Name = "SentFileCard", MaxWidth = 190, Height = 52, CornerRadius = new CornerRadius(10), BorderBrush = DesktopTheme.Brush("#E0E4E9"), BorderThickness = new Thickness(1), Background = DesktopTheme.Surface, Padding = new Thickness(10), Child = fileRow };
            }
            child.Margin = new Thickness(6, 0, 0, 6); ToolTip.SetTip(child, file.Name); sentAttachments.Children.Add(child);
        }
    }
    private void EnsureIdle() { if (!CanStart()) throw new InvalidOperationException(TextResources.Get("chatMessageView.ensureIdle.text")); }
    private async Task CopyAsync(bool plain)
    {
        if (window.Clipboard is { } clipboard) await clipboard.SetTextAsync(plain ? MarkdownContent.PlainText(current.Content).Trim() : current.Content);
    }
    private void ReadReply()
    {
        if (reading) { session.StopSpeech(); return; }
        owner.Run(async () =>
        {
            EnsureIdle(); queue.Hold(); reading = true; Update(current);
            try { await session.SpeakReplyAsync(current.Id, settings()); }
            finally { reading = false; Update(current); }
        });
    }
    private async Task ExportAsync()
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = TextResources.Get("chatMessageView.exportAsync.title"), SuggestedFileName = "reply-" + current.Id + ".md", DefaultExtension = "md", FileTypeChoices = new[] { new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } } } });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); using var writer = new StreamWriter(stream); await writer.WriteAsync(current.Content);
    }
    public void Update(ChatMessage message) => Update(message, owner.Settings.AgentDetailedProcess);
    private void RefreshElapsed()
    {
        elapsedRow.IsVisible = !IsUser && current.ProcessingStartedAt.HasValue;
        elapsed.Text = current.ProcessingLabel(DateTime.UtcNow);
        if (!IsUser && VisualRoot != null && current.Status == "pending" && current.ProcessingStartedAt.HasValue) clock.Start(); else clock.Stop();
    }
    public void Update(ChatMessage message, bool detailedProcess)
    {
        current = message;
        RefreshElapsed();
        if (message.Status == "pending") showHistory = false;
        body.Update(message.Content ?? ""); body.IsVisible = !string.IsNullOrEmpty(message.Content);
        if (userBubble != null) userBubble.IsVisible = body.IsVisible;
        RefreshSentAttachments(message.Attachments);
        RefreshGeneratedImages(message);
        if(!IsUser&&session.KernelSession!=null&&message.ToolEvents?.Any(tool=>tool.Name=="start_job")==true)
        {if(jobs==null){jobs=new DesktopAgentJobsPanel(owner.WorkBindings.Jobs,session.KernelSession.ProjectId,session.KernelSession.ConversationId,message.Id,true);jobHost.Content=jobs;}}
        else if(jobs!=null){jobs.Dispose();jobs=null;jobHost.Content=null;}
        reasoning.Text = message.Reasoning ?? "";
        bool active = message.Status == "pending" && string.IsNullOrEmpty(message.Content);
        bool detailChanged = detailedSetting != detailedProcess;
        if (active && detailChanged) thinking.IsExpanded = detailedProcess;
        detailedSetting = detailedProcess;
        processArea.IsVisible = active || showHistory && (!string.IsNullOrWhiteSpace(message.Reasoning) || message.ToolEvents?.Count > 0);
        thinking.IsVisible = processArea.IsVisible;
        reasoning.IsVisible = !string.IsNullOrWhiteSpace(message.Reasoning);
        tools.IsVisible = (active || showHistory) && message.ToolEvents != null && message.ToolEvents.Count > 0;
        var events = message.ToolEvents ?? new();
        foreach (var stale in toolRows.Keys.Where(id => !events.Any(t => t != null && t.Id == id)).ToList()) { tools.Children.Remove(toolRows[stale]); toolRows.Remove(stale); }
        foreach (var item in events.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Id)))
        {
            if (!toolRows.TryGetValue(item.Id, out var row))
            {
                row = new Expander { Name = "AgentToolDetails", IsExpanded = active && detailedProcess, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = DesktopTheme.Brush("#89929F") } };
                ProcessExpander.Apply(row);
                toolRows[item.Id] = row; tools.Children.Add(row);
            }
            else if (active && detailChanged) row.IsExpanded = detailedProcess;
            var summary = (item.Summary ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (summary.Length > 100) summary = summary[..100] + "…";
            row.Header = new TextBlock { Text = (item.Status is "pending" or "running" ? "◌ " : item.Status == "error" ? "! " : "✓ ") + item.Name + (string.IsNullOrWhiteSpace(summary) ? "" : " · " + summary) + (item.ElapsedMilliseconds > 0 ? TextResources.Format("chatMessageView.update.template",(item.ElapsedMilliseconds / 1000.0).ToString("0.0")) : ""), FontSize = 11, Foreground = DesktopTheme.Brush("#89929F"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = Math.Max(100, Bounds.Width > 1 ? Bounds.Width - 50 : 500) };
            ((SelectableTextBlock)row.Content!).Text = item.Summary;
        }
        UpdateProcess(message, events, active);
        previewRow.IsVisible = !thinking.IsExpanded && !string.IsNullOrEmpty(preview.Text);
        status.Text = message.Status == "pending" ? active ? "" : TextResources.Get("chatMessageView.update.statusMessageStatus") : message.Status == "cancelled" ? TextResources.Get("chatMessageView.update.statusMessageStatus.alternativeA") : message.Status == "error" ? TextResources.Get("chatMessageView.update.statusMessageStatus.alternativeB") : message.Status == "interrupted" ? TextResources.Get("chatMessageView.update.statusMessageStatus.alternativeC") : "";
        status.IsVisible = !string.IsNullOrEmpty(status.Text);
        replyActions.IsVisible = !IsUser && message.Status == "complete" && !string.IsNullOrWhiteSpace(message.Content);
        regenerate.IsEnabled = CanStart(); readReply.IsEnabled = reading || CanStart();
        ToolTip.SetTip(readReply, reading ? TextResources.Get("chatMessageView.update.toolTipSetTip") : TextResources.Get("chatMessageView.update.toolTipSetTip.alternativeA"));
        showReasoning.IsVisible = !string.IsNullOrWhiteSpace(message.Reasoning) || events.Count > 0;
        showReasoning.Header = showHistory ? TextResources.Get("chatMessageView.update.showReasoningHeaderShowHistory") : TextResources.Get("chatMessageView.update.showReasoningHeaderShowHistory.alternativeA");
    }
    private void UpdateProcess(ChatMessage message, List<AgentToolEvent> events, bool active)
    {
        string text = message.Reasoning ?? "", signature = string.Join("\n", events.Select(t => t.Id + "\t" + t.Status + "\t" + t.Summary));
        if (text != previousReasoning) latestIsTool = false;
        if (signature != previousTools) latestIsTool = events.Count > 0;
        previousReasoning = text; previousTools = signature;
        var running = events.LastOrDefault(t => t.Status is "running" or "pending");
        var latest = running ?? (latestIsTool || string.IsNullOrWhiteSpace(text) ? events.LastOrDefault() : null);
        preview.Text = latest != null ? ((latest.Summary ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? latest.Name) : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        preview.Foreground = latest != null ? DesktopTheme.Brush("#89929F") : DesktopTheme.Brush("#000000");
        string stage = running == null ? (latest != null || string.IsNullOrWhiteSpace(text) && message.WorkMode ? TextResources.Get("chatMessageView.updateProcess.stageRunningNull") : TextResources.Get("chatMessageView.updateProcess.stageRunningNull.alternativeA")) :
            running.Name is "write_file" or "edit_file" or "apply_patch" or "create_directory" ? TextResources.Get("chatMessageView.updateProcess.stageRunningNull.alternativeB") :
            running.Name is "run_command" or "exec_command" or "shell" or "open_directory" ? TextResources.Get("chatMessageView.updateProcess.stageRunningNull.alternativeC") : TextResources.Get("chatMessageView.updateProcess.stageRunningNull");
        thinkingLabel.Text = active ? stage : TextResources.Get("chatMessageView.updateProcess.thinkingLabelActiveStage");
    }
}
