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

internal sealed partial class ChatWindow : Window
{
    private readonly DesktopController owner;
    private readonly bool quick;
    private readonly TextBox draft = LocalizedUi.Input("", ()=>(TextResources.Get("chatWindow.draft.input")));
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
    private string? activeDraftKey;
    private string LoadDraft(){activeDraftKey=DraftKey;if(owner.Drafts.TryGetValue(activeDraftKey,out var value))return value;value=!quick&&owner.Settings.SaveChatHistory?owner.ComposerDrafts.Get("desktop:"+activeDraftKey):"";owner.Drafts[activeDraftKey]=value;return value;}
    private void SaveDraft(){var key=activeDraftKey??DraftKey;var value=draft.Text??"";owner.Drafts[key]=value;if(!quick&&owner.Settings.SaveChatHistory)owner.ComposerDrafts.Set("desktop:"+key,value);}
    private void SynchronizeDraftScope(){if(activeDraftKey==DraftKey)return;SaveDraft();draft.Text=LoadDraft();uploadEpoch++;RefreshAttachments();}
    private List<ChatAttachment> Files => attachments.TryGetValue(DraftKey, out var files) ? files : attachments[DraftKey] = new();
    private string persona;
    private sealed record ModelChoice(string Provider, string Model, string Label) { public override string ToString() => Label; }
    public ChatWindow(DesktopController controller, bool isQuick)
    {
        owner = controller; quick = isQuick; persona = owner.Skin.Id;
        draft.Text = LoadDraft();
        draft.TextChanged += (_, _) => SaveDraft();
        LocalizedUi.Set(this,"Title",()=>(quick ? TextResources.Get("chatWindow.chatWindow.titleQuick") : TextResources.Get("chatWindow.chatWindow.titleQuick.alternativeA"))); Width = quick ? 470 : 880; Height = quick ? 520 : 730;
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
        partnerAvatar = new AvatarView { Name = "ChatPartnerAvatar", Assets = avatarAssets, Skin = owner.Skin, Width = 28, Height = 28 }; LocalizedUi.Set(partnerName,"Text",()=>(owner.Skin.DisplayName)); partnerName.VerticalAlignment = VerticalAlignment.Center;
        var profileMenu=CreateProfileMenu();
        var profileButton=new Button{Name="ShellAvatar",Content=partnerAvatar,Width=28,Height=28,MinWidth=0,MinHeight=0,Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=Brushes.Transparent,CornerRadius=new CornerRadius(8),ContextMenu=profileMenu};
        profileButton.Click+=(_,_)=>profileMenu.Open(profileButton);
        LocalizedUi.Automation(profileButton,()=>(TextResources.Get("chatAppShell.initializeAppShell.shellIcons.alternativeK")));
        questions.Name = "AgentQuestions";
        var header = Ui.Column(Ui.Row(profileButton, partnerName, LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text")), owner.ShowSettings), LocalizedUi.Button(()=>(quick ? TextResources.Get("chatWindow.chatWindow.text.alternativeA") : TextResources.Get("chatWindow.chatWindow.text.alternativeB")), () => owner.ShowChat(!quick))), status, questions);
        if(!quick)header.Children.Add(CreateWorkBindingActions());
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        primary = Ui.Button("≋", () => { if (!string.IsNullOrWhiteSpace(draft.Text) || Files.Count > 0) Send(); else if (BoundSession.IsBusy) owner.Run(StopBoundAsync); else owner.ShowLive(); });
        primary.Name = "PrimaryChatAction"; primary.Width = primary.Height = 32; primary.Padding = new Thickness(0); primary.HorizontalContentAlignment = HorizontalAlignment.Center; primary.VerticalContentAlignment = VerticalAlignment.Center; primary.CornerRadius = new CornerRadius(16); primary.Background = DesktopTheme.Brush("#357FF5"); primary.Foreground = Avalonia.Media.Brushes.White;
        addAttachment = Ui.Button("+", OpenAttachmentMenu); addAttachment.Name = "AddAttachment"; addAttachment.Width = addAttachment.Height = 32; addAttachment.Padding = new Thickness(0); addAttachment.HorizontalContentAlignment = HorizontalAlignment.Center; addAttachment.VerticalContentAlignment = VerticalAlignment.Center; LocalizedUi.Tooltip(addAttachment,()=>(TextResources.Get("chatWindow.chatWindow.toolTipSetTip")));
        var filesMenu = LocalizedUi.With(new MenuItem { Name = "AttachmentFilesMenu", Header = TextResources.Get("chatWindow.chatWindow.header"), Icon = MenuIcon("M 7,5 L 7,12 Q 7,15 10,15 Q 13,15 13,12 L 13,4 Q 13,1 10,1 Q 7,1 7,4 L 7,11 Q 7,13 9,13 Q 11,13 11,11 L 11,5") },"Header",()=>(TextResources.Get("chatWindow.chatWindow.header"))); filesMenu.Click += (_, _) => owner.Run(PickAttachmentsAsync);
        snapshotMenu = LocalizedUi.With(new MenuItem { Name = "AttachApplicationSnapshot", Header = TextResources.Get("chatWindow.chatWindow.header.alternativeA"), IsEnabled = false },"Header",()=>(TextResources.Get("chatWindow.chatWindow.header.alternativeA"))); snapshotMenu.Click += (_, _) => owner.Run(AttachApplicationSnapshotAsync); SetSnapshotTarget(null);
        goalMenu = new MenuItem { Name = "SetAgentGoal", Header = MenuLabel(()=> (TextResources.Get("chatWindow.chatWindow.menuLabel")), ()=> (TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeA"))), Icon = MenuIcon("M 14,9 A 5,5 0 1 1 9,4 M 9,9 L 16,2 M 12,2 L 16,2 L 16,6") }; goalMenu.Click += (_, _) => Try(ShowGoalDialog);
        planMode = new MenuItem { Name = "AgentPlanMode", Header = MenuLabel(()=> (TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeB")), ()=> (TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeC"))), ToggleType = MenuItemToggleType.CheckBox, IsChecked = EffectiveSettings().AgentPlanMode };
        planMode.Click += (_, _) => { ChangeWorkSetting(settings=>settings.AgentPlanMode=planMode.IsChecked); };
        var addMenu = new ContextMenu { ItemsSource = new object[] { LocalizedUi.With(new MenuItem { Header = TextResources.Get("chatWindow.chatWindow.header.alternativeB"), IsEnabled = false },"Header",()=>(TextResources.Get("chatWindow.chatWindow.header.alternativeB"))), filesMenu, snapshotMenu, goalMenu, planMode } };
        addMenu.Opening += (_, _) => { planMode.IsChecked = EffectiveSettings().AgentPlanMode; goalMenu.Header = MenuLabel(()=> (TextResources.Get("chatWindow.chatWindow.menuLabel")), ()=> (string.IsNullOrWhiteSpace(EffectiveSettings().AgentGoal) ? TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeA") : EffectiveSettings().AgentGoal)); SetSnapshotTarget(OperatingSystem.IsWindowsVersionAtLeast(6, 1) ? ApplicationSnapshots.Topmost() : null); };
        addAttachment.ContextMenu = addMenu;
        attachmentChips.Name = "AttachmentCards";
        modeButton = Ui.Button("", OpenModeMenu); modeButton.Name = "ChatModeAction"; modeButton.Width = 104; modeButton.Height = 32; modeButton.Padding = new Thickness(4, 0); modeButton.Background = Brushes.Transparent; modeButton.BorderThickness = new Thickness(0); modeButton.Foreground = DesktopTheme.Brush("#9299A3"); modeButton.HorizontalContentAlignment = HorizontalAlignment.Center; modeButton.VerticalContentAlignment = VerticalAlignment.Center;
        conversationMode = new MenuItem { Name = "ConversationMode", Icon = ModeIcon(false), Header = MenuLabel(()=> (TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeD")), ()=> (TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeE"))), ToggleType = MenuItemToggleType.Radio, GroupName = "ChatMode" }; conversationMode.Click += (_, _) => SelectMode(false);
        workMode = new MenuItem { Name = "WorkMode", Icon = ModeIcon(true), Header = MenuLabel(()=> (TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeF")), ()=> (TextResources.Get("chatWindow.chatWindow.menuLabel.alternativeG"))), ToggleType = MenuItemToggleType.Radio, GroupName = "ChatMode", IsEnabled = OperatingSystem.IsWindows() }; workMode.Click += (_, _) => SelectMode(true);
        LocalizedUi.Tooltip(workMode,()=>(TextResources.Get("chatWindow.chatWindow.toolTipSetTip.alternativeA")));
        var modeMenu = new ContextMenu { ItemsSource = new[] { conversationMode, workMode } }; modeMenu.Opening += (_, _) => RefreshMode(); modeButton.ContextMenu = modeMenu;
        var dictate = Ui.AsyncButton("🎙", DictateAsync, owner.Error); dictate.Name = "ChatDictation"; dictate.Width = dictate.Height = 32; dictate.Padding = new Thickness(0); dictate.HorizontalContentAlignment = HorizontalAlignment.Center; dictate.VerticalContentAlignment = VerticalAlignment.Center; LocalizedUi.Tooltip(dictate,()=>(TextResources.Get("chatWindow.chatWindow.toolTipSetTip.alternativeB")));
        var toolbar = new Grid { Name = "ComposerToolbar", ColumnDefinitions = new ColumnDefinitions("36,104,*,Auto"), Margin = new Thickness(2, 8, 2, 0) };
        toolbar.Children.Add(addAttachment); Grid.SetColumn(modeButton, 1); toolbar.Children.Add(modeButton); models.Name = "ChatModels"; models.MinWidth = 0; models.MaxWidth = 260; models.Margin = new Thickness(0, 0, 4, 0); models.RenderTransform = new TranslateTransform(0, 4); models.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(models, 2); toolbar.Children.Add(models);
        var actions = Ui.Row(dictate, primary); Grid.SetColumn(actions, 3); toolbar.Children.Add(actions);
        var composer = new Border { Name = "ComposerSurface", Background = DesktopTheme.Surface, CornerRadius = new CornerRadius(22), BorderBrush = DesktopTheme.Brush("#E3E7ED"), BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = Ui.Column(attachmentChips, draft, toolbar) };
        draft.TextChanged += (_, _) => RefreshAction();
        DockPanel.SetDock(composer, Dock.Bottom); root.Children.Add(composer);
        if (!quick)
        {
            var sidebar = Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeC")), 17), Ui.Row(LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeD")), () => Try(BoundQueue.Resume)), LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeE")), () => Try(BoundQueue.Hold))), new ScrollViewer { Content = queue, MaxHeight = 400 }, LocalizedUi.AsyncButton(()=>(TextResources.Get("chatWindow.chatWindow.asyncButton")), ExportAsync, owner.Error), LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeF")), ConfirmClear));
            sidebar.Width = 245; sidebar.Margin = new Thickness(18, 8, 0, 8); DockPanel.SetDock(sidebar, Dock.Right); root.Children.Add(sidebar);
        }
        root.Children.Add(history); chatPage = root; imageWorkspace = new ImageWorkspacePage(owner, this);
        var shell = new DockPanel();
        routeBack = Ui.Button("‹", () => NavigateHistory(-1)); routeBack.Name = "PageBack"; LocalizedUi.Tooltip(routeBack,()=>(TextResources.Get("common.back")));
        routeForward = Ui.Button("›", () => NavigateHistory(1)); routeForward.Name = "PageForward"; LocalizedUi.Tooltip(routeForward,()=>(TextResources.Get("chatWindow.chatWindow.toolTipSetTip.alternativeC")));
        var imagesNav = LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeG")), () => Navigate("images")); imagesNav.Name = "ImagesNavigation";
        var nav = Ui.Row(routeBack, routeForward, LocalizedUi.Button(()=>(TextResources.Get("chatWindow.chatWindow.text.alternativeH")), () => Navigate("chat")), imagesNav); nav.Margin = new Thickness(16, 8, 16, 0); DockPanel.SetDock(nav, Dock.Top); shell.Children.Add(nav); shell.Children.Add(pageHost); Content = shell; ApplyRoute();
        models.SelectionChanged += (_, _) =>
        {
            if (models.SelectedItem is not ModelChoice item || EffectiveSettings().ActiveTextProviderId == item.Provider && EffectiveSettings().Text.Model == item.Model) return;
            if (BoundSession.IsBusy || owner.Live.IsActive) { owner.Error(new InvalidOperationException(TextResources.Get("chatWindow.chatWindow.ownerError"))); RefreshModels(); return; }
            if(binding==null){owner.Settings.SelectTextModel(item.Provider,item.Model);owner.Save();}else owner.WorkBindings.SelectModel(binding.Id,item.Provider,item.Model);
        };
        owner.RegisterChatHost(this);
        owner.Changed += Refresh; Closed += (_, _) => { closed = true; DisposeCompanionRoutes(); owner.ChatHostClosed(this); imageWorkspace.Dispose(); foreach (var view in rendered.Values) view.DisposeImages(); uploadEpoch++; snapshotCancel.Cancel(); snapshotCancel.Dispose(); snapshotIcon?.Dispose(); SaveDraft(); owner.ComposerDrafts.Flush();owner.Changed -= Refresh; DisposeAttachmentImages(); avatarAssets.Dispose(); if (owner.Dictation.Owns(this)) owner.Run(() => owner.Dictation.CancelAsync(this)); };
        Refresh();
        history.SizeChanged += (_, _) => ResizeBubbles();
    }
    internal string CurrentPage => routes[routeIndex];
    internal string QuestionSessionId => BoundSession.KernelSession?.SessionId ?? BoundSession.PersonaId;
    internal ImageWorkspacePage Images => imageWorkspace;
    internal void ShowImageTask(string id) { ShowImages(); imageWorkspace.ShowTasks(id); }
    internal void ShowImages(GeneratedImage? image = null) { Navigate("images"); if (image != null) imageWorkspace.Prepare(image); }
    private void Navigate(string route) { if (CurrentPage == route) return; routes.RemoveRange(routeIndex + 1, routes.Count - routeIndex - 1); routes.Add(route); routeIndex++; ApplyRoute(); }
    internal void NavigateHistory(int delta) { var next = routeIndex + delta; if (next < 0 || next >= routes.Count) return; routeIndex = next; ApplyRoute(); }
    private void ApplyRoute() { pageHost.Content = null; pageHost.Content = CompanionRouteContent(); routeBack.IsEnabled = routeIndex > 0; routeForward.IsEnabled = routeIndex < routes.Count - 1; }
    private void Try(Action action) { try { action(); } catch (Exception e) { owner.Error(e); } }
    private ContextMenu CreateProfileMenu()
    {
        var menu=new ContextMenu{Name="ShellProfileMenu",MinWidth=258};
        var avatar=new AvatarView{Assets=avatarAssets,Skin=owner.Skin,Width=32,Height=32};
        var title=Ui.Text(()=>(owner.Skin.DisplayName),15);var subtitle=LocalizedUi.Text(()=>(TextResources.Get("relationship.home.open")),12);
        var heading=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};heading.Children.Add(Ui.Column(title,subtitle));var arrow=Ui.Text("›",18);Grid.SetColumn(arrow,1);heading.Children.Add(arrow);
        var home=new MenuItem{Name="ProfileCompanionHome",Header=heading,Icon=avatar,IsEnabled=true,Focusable=true};
        LocalizedUi.Automation(home,()=>(owner.Skin.DisplayName+" · "+TextResources.Get("relationship.home.open")));
        home.Click+=(_,_)=>{menu.Close();ShowCompanionPage();};
        var languages=LocalizedUi.With(new MenuItem{Name="ProfileLanguage",Header=TextResources.Get("menus.language")},"Header",()=>(TextResources.Get("menus.language")));
        var settings=LocalizedUi.With(new MenuItem{Name="ProfileSettings",Header=TextResources.Get("chatAppShell.openProfileMenu.addProfile.alternativeC")},"Header",()=>(TextResources.Get("chatAppShell.openProfileMenu.addProfile.alternativeC")));settings.Click+=(_,_)=>{menu.Close();owner.ShowSettings();};
        menu.ItemsSource=new object[]{home,new Separator(),languages,settings};
        menu.Opening+=(_,_)=>{avatar.Skin=owner.Skin;LocalizedUi.Set(title,"Text",()=>(owner.Skin.DisplayName));LocalizedUi.Set(subtitle,"Text",()=>(TextResources.Get("relationship.home.open")));Avalonia.Automation.AutomationProperties.SetName(home,owner.Skin.DisplayName+" · "+subtitle.Text);};
        languages.Click+=async (_,_)=>{menu.Close();var dialog=new LanguagePickerWindow(owner.Settings.UiLanguage,owner.SetUiLanguage);await dialog.ShowDialog(this);};return menu;
    }
    private void OpenAttachmentMenu() => addAttachment.ContextMenu!.Open(addAttachment);
    private void OpenModeMenu() => modeButton.ContextMenu!.Open(modeButton);
    internal void SetSnapshotTarget(ApplicationSnapshotTarget? target)
    {
        snapshotTarget = OperatingSystem.IsWindows() ? target : null; snapshotIcon?.Dispose(); snapshotIcon = null;
        LocalizedUi.Set(snapshotMenu,"Header",()=>(snapshotTarget == null ? TextResources.Get("chatWindow.setSnapshotTarget.nullSnapshotTargetAppName.alternativeA") : TextResources.Format("chatWindow.setSnapshotTarget.nullSnapshotTargetAppName",snapshotTarget.AppName)));
        snapshotMenu.Icon = MenuIcon("M 3,7 L 3,3 L 7,3 M 11,3 L 15,3 L 15,7 M 15,11 L 15,15 L 11,15 M 7,15 L 3,15 L 3,11");
        if (snapshotTarget?.IconPng is { Length: > 0 } png)
        {
            try { using var stream = new MemoryStream(png); snapshotIcon = Bitmap.DecodeToWidth(stream, 20); snapshotMenu.Icon = new Image { Source = snapshotIcon, Width = 18, Height = 18, Stretch = Stretch.Uniform }; }
            catch (Exception e) when (e is ArgumentException or IOException) { }
        }
        snapshotMenu.IsEnabled = snapshotTarget != null && !owner.Live.IsActive && !owner.Dictation.IsBusy;
        LocalizedUi.Tooltip(snapshotMenu,()=>(!OperatingSystem.IsWindows() ? TextResources.Get("chatWindow.setSnapshotTarget.toolTipSetTip") : snapshotTarget == null ? TextResources.Get("chatWindow.setSnapshotTarget.toolTipSetTip.alternativeA") : snapshotTarget.Title));
    }
    private async Task AttachApplicationSnapshotAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) throw new PlatformNotSupportedException(TextResources.Get("chatWindow.attachApplicationSnapshotAsync.text"));
        var context = UploadTarget(); var target = snapshotTarget; if (target == null || !Current(context)) return;
        if (Files.Count >= 4) throw new ApiException(TextResources.Get("chatWindow.attachApplicationSnapshotAsync.text.alternativeA"));
        var png = await ApplicationSnapshots.CaptureAsync(target, snapshotCancel.Token);
        if (Current(context)) AddAttachments(new[] { ChatAttachment.CreateImage(TextResources.Format("chatWindow.attachApplicationSnapshotAsync.chatAttachmentCreateImage",DateTime.Now.ToString("yyyyMMdd-HHmmss")), png) });
    }
    private void ConfigureDraftMenu()
    {
        MenuItem Item(string name, System.Func<string> title, Key key, Action action)
        {
            var item = LocalizedUi.With(new MenuItem { Name = name, Header = title(), InputGesture = new KeyGesture(key, KeyModifiers.Control), Padding = new Thickness(10, 6) },"Header",()=> (title())); item.Click += (_, _) => action(); return item;
        }
        var cut = Item("DraftCut", ()=> (TextResources.Get("chatWindow.configureDraftMenu.item")), Key.X, draft.Cut); var copy = Item("DraftCopy", ()=> (TextResources.Get("common.copy")), Key.C, draft.Copy);
        var paste = Item("DraftPaste", ()=> (TextResources.Get("chatWindow.configureDraftMenu.item.alternativeA")), Key.V, draft.Paste); var all = Item("DraftSelectAll", ()=> (TextResources.Get("chatWindow.configureDraftMenu.item.alternativeB")), Key.A, draft.SelectAll);
        var menu = new ContextMenu { CornerRadius = new CornerRadius(10), Padding = new Thickness(4), Background = DesktopTheme.Surface, ItemsSource = new object[] { cut, copy, paste, new Separator(), all } };
        menu.Opening += (_, _) => { cut.IsEnabled = !draft.IsReadOnly && draft.SelectionStart != draft.SelectionEnd; copy.IsEnabled = draft.SelectionStart != draft.SelectionEnd; paste.IsEnabled = !draft.IsReadOnly && Clipboard != null; all.IsEnabled = !string.IsNullOrEmpty(draft.Text); }; draft.ContextMenu = menu;
    }
    private UploadContext UploadTarget() => new(DraftKey, uploadEpoch);
    private bool Current(UploadContext context) => !closed && IsVisible && DraftKey == context.Persona && (binding!=null||owner.Skin.Id==persona) && uploadEpoch == context.Epoch;
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
            if (files is { Length: > 0 }) { await LoadAttachmentPathsAsync(files.Select(f => f.TryGetLocalPath() ?? throw new ApiException(TextResources.Get("chatWindow.pasteClipboardAsync.filesSelect"))), context); return; }
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
        return ChatAttachment.CreateImage(TextResources.Format("chatWindow.imageAttachment.chatAttachmentCreateImage",DateTime.Now.ToString("yyyyMMdd-HHmmss")), stream.ToArray());
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
            if (files is { Length: > 0 }) { var paths = files.Select(f => f.TryGetLocalPath() ?? throw new ApiException(TextResources.Get("chatWindow.attachmentDrop.filesSelect"))).ToArray(); owner.Run(() => AddAttachmentPathsAsync(paths)); return; }
            var bitmap = e.DataTransfer.TryGetBitmap(); if (bitmap != null) AddAttachments(new[] { ImageAttachment(bitmap) });
        }
        catch (Exception ex) { owner.Error(ex); }
    }
    private void DraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && OperatingSystem.IsWindows()) { SelectMode(!EffectiveSettings().AgentEnabled); e.Handled = true; return; }
        if (e.Key == Key.Enter && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))) { Send(); e.Handled = true; }
    }
    private void SelectMode(bool work)
    {
        if (work && !OperatingSystem.IsWindows()) return;
        if(binding!=null){if(!work){owner.Settings.AgentEnabled=false;owner.Save();owner.Run(()=>BindWorkAsync(null,null));}return;}
        SaveDraft();owner.Settings.AgentEnabled=work;SynchronizeDraftScope();owner.Save();RefreshMode();
    }
    private void RefreshMode()
    {
        bool work = OperatingSystem.IsWindows() && EffectiveSettings().AgentEnabled;
        var label = LocalizedUi.With(new TextBlock { Text = work ? TextResources.Get("chatWindow.refreshMode.work") : TextResources.Get("chatWindow.refreshMode.work.alternativeA"), FontSize = 12, Foreground = DesktopTheme.Brush("#9299A3"), VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform(0, 4) },"Text",()=>(work ? TextResources.Get("chatWindow.refreshMode.work") : TextResources.Get("chatWindow.refreshMode.work.alternativeA")));
        var content = Ui.Row(ModeIcon(work), label); content.RenderTransform = new TranslateTransform(-4, 0); modeButton.Content = content;
        conversationMode.IsChecked = !work; workMode.IsChecked = work;
        LocalizedUi.Tooltip(modeButton,()=>(OperatingSystem.IsWindows() ? (work ? TextResources.Get("chatWindow.refreshMode.toolTipSetTip") : TextResources.Get("chatWindow.refreshMode.toolTipSetTip.alternativeA")) + TextResources.Get("chatWindow.refreshMode.toolTipSetTip.alternativeB") : TextResources.Get("chatWindow.refreshMode.toolTipSetTip.alternativeC")));
    }
    private static Control ModeIcon(bool work)
    {
        var icon = MenuIcon(work ? "M 3,12 L 12,3 L 15,6 L 6,15 L 2,16 Z M 10,5 L 13,8 M 3,12 L 6,15" : "M 3,3 L 15,3 L 15,12 L 8,12 L 4,16 L 4,12 L 3,12 Z");
        icon.Name = work ? "WorkPencilGlyph" : "ConversationBubbleGlyph"; return icon;
    }
    private static Control MenuIcon(string path) => new ShapePath { Width = 18, Height = 18, Data = Geometry.Parse(path), Stroke = DesktopTheme.Brush("#777F89"), StrokeThickness = 1.3 };
    private static Control MenuLabel(System.Func<string> title, System.Func<string> description)
    {
        var secondary = LocalizedUi.With(new TextBlock { Text = description(), Foreground = DesktopTheme.Brush("#9CA3AC"), FontSize = 12, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },"Text",()=> (description()));
        return Ui.Row(LocalizedUi.With(new TextBlock { Text = title() },"Text",()=> (title())), secondary);
    }
    private void ShowGoalDialog()
    {
        var goalProject=binding?.ProjectId;
        var input = LocalizedUi.Input(EffectiveSettings().AgentGoal ?? "", ()=>(TextResources.Get("chatWindow.showGoalDialog.input"))); input.Name = "AgentGoalInput"; input.AcceptsReturn = true; input.MinHeight = 100; input.TextWrapping = TextWrapping.Wrap;
        var dialog = LocalizedUi.Window(()=>(TextResources.Get("chatWindow.showGoalDialog.window")), new Border(), 470, 310); dialog.Name = "AgentGoalDialog";
        var save = LocalizedUi.Button(()=>(TextResources.Get("common.save")), () => { ChangeWorkSettingFor(goalProject,settings=>settings.AgentGoal=(input.Text??"").Trim()); dialog.Close(); }); save.Name = "SaveAgentGoal";
        var clear = LocalizedUi.Button(()=>(TextResources.Get("chatWindow.showGoalDialog.text")), () => input.Text = ""); clear.Name = "ClearAgentGoal";
        dialog.Content = new Border { Padding = new Thickness(20), Child = Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("chatWindow.showGoalDialog.text.alternativeA")), 12), input, Ui.Row(clear, LocalizedUi.Button(()=>(TextResources.Get("common.cancel")), dialog.Close), save)) };
        _ = dialog.ShowDialog(this);
    }
    private void RefreshModels()
    {
        var all = owner.Settings.TextProviders.SelectMany(p => p.Models.Select(m => new ModelChoice(p.Id, m, p.Name + " · " + p.DisplayName(m)))).ToList();
        models.ItemsSource = all; models.SelectedItem = all.FirstOrDefault(m => m.Provider == EffectiveSettings().ActiveTextProviderId && m.Model == EffectiveSettings().Text.Model);
    }
    private void Send()
    {
        if(owner.IsExiting||owner.WorkBindings.IsDraining)return;
        if (owner.Live.IsActive) { owner.Error(new InvalidOperationException(TextResources.Get("chatWindow.send.ownerError"))); return; }
        if (owner.Dictation.IsBusy) { owner.Error(new InvalidOperationException(TextResources.Get("chatWindow.send.ownerError.alternativeA"))); return; }
        var text = (draft.Text ?? "").Trim(); if (text.Length == 0 && Files.Count == 0) return;
        try { BoundQueue.Enqueue(text, Files); uploadEpoch++; Files.Clear(); RefreshAttachments(); draft.Text = ""; RefreshAction(); } catch (Exception e) { owner.Error(e); }
    }
    private async Task PickAttachmentsAsync()
    {
        var context = UploadTarget();
        if (OperatingSystem.IsWindows()) { var paths = AttachmentPicker.Show(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero); if (Current(context)) await LoadAttachmentPathsAsync(paths, context); return; }
        if (OperatingSystem.IsMacOS()) { var paths = NativeAttachmentPicker.ShowMac(); if (Current(context)) await LoadAttachmentPathsAsync(paths, context); return; }
        var picked = await StorageProvider.OpenFilePickerAsync(LocalizedUi.With(new FilePickerOpenOptions { Title = TextResources.Get("chatWindow.pickAttachmentsAsync.title"), AllowMultiple = true, FileTypeFilter = new[] { new FilePickerFileType(TextResources.Get("chatWindow.pickAttachmentsAsync.fileTypeFilterFilePickerFileType")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.pdf", "*.docx", "*.txt", "*.md", "*.csv", "*.tsv", "*.json", "*.xml", "*.yaml", "*.yml", "*.log", "*.cs", "*.py", "*.js", "*.ts", "*.tsx", "*.jsx", "*.html", "*.css", "*.sql", "*.ps1", "*.sh", "*.toml", "*.ini", "*.cfg" } } } },"Title",()=>(TextResources.Get("chatWindow.pickAttachmentsAsync.title"))));
        if (!Current(context) || picked.Count == 0) return;
        await LoadAttachmentPathsAsync(picked.Select(f => f.TryGetLocalPath() ?? throw new ApiException(TextResources.Get("chatWindow.pickAttachmentsAsync.pickedSelect"))), context);
    }
    private static ChatAttachment FolderReference(string path)
    {
        var label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (label.Length > 120) label = label[..120];
        var note = OperatingSystem.IsWindows() ? TextResources.Get("chatWindow.folderReference.noteOperatingSystemIsWindows") : TextResources.Get("chatWindow.folderReference.noteOperatingSystemIsWindows.alternativeA");
        return ChatAttachment.FromText((string.IsNullOrEmpty(label) ? TextResources.Get("chatWindow.folderReference.chatAttachmentFromText.alternativeA") : label) + TextResources.Get("chatWindow.folderReference.chatAttachmentFromText.alternativeB"), TextResources.Format("chatWindow.folderReference.chatAttachmentFromText",path,note));
    }
    internal Task AddAttachmentPathsAsync(IEnumerable<string> paths) => LoadAttachmentPathsAsync(paths, UploadTarget());
    private async Task LoadAttachmentPathsAsync(IEnumerable<string> paths, UploadContext context)
    {
        if (!Current(context)) return;
        if (owner.Live.IsActive || owner.Dictation.IsBusy) throw new InvalidOperationException(TextResources.Get("chatWindow.loadAttachmentPathsAsync.text"));
        var selected = paths.ToArray(); if (selected.Length == 0) return;
        if (selected.Length + Files.Count > 4) throw new ApiException(TextResources.Get("chatWindow.loadAttachmentPathsAsync.text.alternativeA"));
        var folders = selected.Where(Directory.Exists).ToArray();
        if (folders.Length > 1) throw new ApiException(TextResources.Get("chatWindow.loadAttachmentPathsAsync.text.alternativeB"));
        var loaded = await Task.Run(() => selected.Select(p => Directory.Exists(p) ? FolderReference(p) : ChatAttachment.Read(p)).ToList());
        if (!Current(context)) return;
        AddAttachments(loaded);
    }
    internal void AddAttachments(IEnumerable<ChatAttachment> loaded)
    {
        if (closed) return;
        if (owner.Live.IsActive || owner.Dictation.IsBusy) throw new InvalidOperationException(TextResources.Get("chatWindow.addAttachments.text"));
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
            var remove = Ui.Button("×", () => { Files.Remove(file); RefreshAttachments(); RefreshAction(); }); remove.Name = "RemoveAttachment"; remove.Width = remove.Height = 22; remove.Padding = new Thickness(0); remove.Margin = new Thickness(0, 3, 3, 0); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top; remove.HorizontalContentAlignment = HorizontalAlignment.Center; remove.VerticalContentAlignment = VerticalAlignment.Center; remove.CornerRadius = new CornerRadius(11); remove.Background = DesktopTheme.Brush("#F2FFFFFF"); LocalizedUi.Tooltip(remove,()=>(TextResources.Format("chatWindow.refreshAttachments.toolTipSetTip",file.Name))); grid.Children.Add(remove);
            var card = new Border { Name = "AttachmentCard", Width = file.IsImage ? 120 : 150, Height = 100, Margin = new Thickness(0, 0, 8, 8), BorderBrush = DesktopTheme.Brush("#E5E7EB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = grid }; ToolTip.SetTip(card, file.Name); attachmentChips.Children.Add(card);
        }
    }
    private void RefreshAction()
    {
        RefreshMode();
        bool hasDraft = !string.IsNullOrWhiteSpace(draft.Text) || Files.Count > 0;
        primary.Content = hasDraft ? "↑" : BoundSession.IsBusy ? "■" : "≋";
        LocalizedUi.Tooltip(primary,()=>(hasDraft ? TextResources.Get("chatWindow.refreshAction.toolTipSetTip") : BoundSession.IsBusy ? TextResources.Get("chatWindow.refreshAction.toolTipSetTip.alternativeA") : TextResources.Get("chatWindow.refreshAction.toolTipSetTip.alternativeB")));
        addAttachment.IsEnabled = !owner.Live.IsActive && !owner.Dictation.IsBusy;
    }
    private void ResizeBubbles() { foreach (var view in rendered.Values) if (view.IsUser) view.MaxWidth = Math.Max(1, history.Bounds.Width / 2); }
    private async Task DictateAsync()
    {
        var expectedPersona = owner.Skin.Id;var expectedBinding=binding;
        if (owner.Live.IsActive) throw new InvalidOperationException(TextResources.Get("chatWindow.dictateAsync.text"));
        if (owner.Dictation.Owns(this)) { var text = await owner.Dictation.FinishAsync(this); if(ReferenceEquals(binding,expectedBinding))draft.Text = (draft.Text ?? "") + text; return; }
        if (BoundSession.IsBusy) throw new InvalidOperationException(TextResources.Get("chatWindow.dictateAsync.text.alternativeA"));
        var granted = await owner.RequestMicrophoneAccessAsync();
        if (!IsVisible || owner.Skin.Id != expectedPersona || !ReferenceEquals(binding,expectedBinding)) return;
        if (!granted) throw new InvalidOperationException(TextResources.Get("chatWindow.dictateAsync.text.alternativeB"));
        if (owner.Live.IsActive || BoundSession.IsBusy || owner.Dictation.IsBusy) throw new InvalidOperationException(TextResources.Get("chatWindow.dictateAsync.text.alternativeC"));
        BoundQueue.Hold(); owner.Dictation.Start(this);
    }
    private void Refresh()
    {
        if(owner.WorkBindings.IsDraining)return;
        RefreshQuestions();
        if(workJobsButton!=null)workJobsButton.IsEnabled=binding!=null&&!owner.IsExiting;
        draft.IsEnabled=!owner.IsExiting;models.IsEnabled=!owner.IsExiting;
        partnerAvatar.Skin = owner.Skin; LocalizedUi.Set(partnerName,"Text",()=>(owner.Skin.DisplayName));
        if (binding==null && persona != owner.Skin.Id) { uploadEpoch++; SaveDraft(); persona = owner.Skin.Id; draft.Text = LoadDraft(); foreach (var oldView in rendered.Values) oldView.DisposeImages(); rendered.Clear(); messages.Children.Clear(); RefreshAttachments(); }
        SynchronizeDraftScope();
        LocalizedUi.Set(status,"Text",()=>(BoundSession.Stage + (owner.Dictation.IsBusy ? TextResources.Format("chatWindow.refresh.ownerSessionStage",owner.Dictation.Stage) : "") + " · " + BoundSession.LastNotice + " " + owner.Dictation.LastNotice + " " + BoundQueue.Notice + " " + BoundSession.HistoryWarning+" "+owner.ComposerDrafts.Warning));
        LocalizedUi.Set(this,"Title",()=>(TextResources.Get("chatWindow.refresh.ownerSkinName") + owner.Skin.DisplayName + (quick ? TextResources.Get("chatWindow.refresh.ownerSkinName.alternativeA") : TextResources.Get("chatWindow.refresh.ownerSkinName.alternativeB"))));
        var followBottom = history.Offset.Y + history.Viewport.Height + 80 >= history.Extent.Height;
        RefreshModels(); RefreshAction();
        var visible = BoundSession.History.Messages.TakeLast(quick ? 20 : 400).ToList();
        foreach (var stale in rendered.Keys.Where(id => !visible.Any(m => m.Id == id)).ToList()) { rendered[stale].DisposeImages(); messages.Children.Remove(rendered[stale]); rendered.Remove(stale); }
        foreach (var message in visible)
        {
            if (rendered.TryGetValue(message.Id, out var view)) view.Update(message, owner.Settings.AgentDetailedProcess);
            else { view = new ChatMessageView(message, owner, this, BoundSession, BoundQueue, CaptureSettings()); rendered[message.Id] = view; messages.Children.Add(view); }
        }
        ResizeBubbles();
        if (followBottom) Dispatcher.UIThread.Post(() => history.ScrollToEnd());
        if (quick) return;
        queue.Children.Clear(); var items = BoundQueue.Items.ToList();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i]; var index = i;
            var edit = Ui.Input(queuedEdits.GetValueOrDefault(item.Id, item.Text)); edit.AcceptsReturn = true; edit.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            edit.TextChanged += (_, _) => queuedEdits[item.Id] = edit.Text ?? "";
            queue.Children.Add(Ui.Column(edit,
                Ui.Row(LocalizedUi.Button(()=>(TextResources.Get("common.save")), () => Try(() => { BoundQueue.Edit(item.Id, edit.Text ?? ""); queuedEdits.Remove(item.Id); })), LocalizedUi.Button(()=>(TextResources.Get("common.delete")), () => Try(() => { BoundQueue.Remove(item.Id); queuedEdits.Remove(item.Id); })), Ui.Button("↑", () => { if (index > 0) Try(() => BoundQueue.MoveBefore(item.Id, items[index - 1].Id)); })),
                Ui.Row(Ui.Button("↓", () => { if (index < items.Count - 1) Try(() => BoundQueue.MoveBefore(item.Id, index + 2 < items.Count ? items[index + 2].Id : null!)); }), LocalizedUi.AsyncButton(()=>(TextResources.Get("chatWindow.refresh.asyncButton")), () => BoundQueue.InterruptAsync(edit.Text ?? "", item.Id), owner.Error))));
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
            void Submit(AgentQuestionAnswer answer) { if (!owner.Questions.Submit(question.SessionId, question.TaskId, question.CallId, answer)) owner.Error(new InvalidOperationException(TextResources.Get("chatWindow.refreshQuestions.ownerError"))); }
            var panel = Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("chatWindow.refreshQuestions.text")), 15), Ui.Text(question.Question)); var options = new WrapPanel();
            for (int i = 0; i < question.Options.Length; i++) { int index = i; var button = Ui.Button(question.Options[i], () => Submit(new AgentQuestionAnswer { OptionIndex = index })); button.Margin = new Thickness(0, 0, 4, 4); options.Children.Add(button); }
            panel.Children.Add(options);
            if (question.AllowFreeInput) { var answer = LocalizedUi.Input("", ()=>(TextResources.Get("chatWindow.refreshQuestions.input"))); answer.MinWidth = 0; panel.Children.Add(answer); panel.Children.Add(LocalizedUi.Button(()=>(TextResources.Get("chatWindow.refreshQuestions.text.alternativeA")), () => { if (!string.IsNullOrWhiteSpace(answer.Text)) Submit(new AgentQuestionAnswer { Text = answer.Text.Trim() }); })); }
            panel.Children.Add(LocalizedUi.Button(()=>(TextResources.Get("chatWindow.refreshQuestions.text.alternativeB")), () => { owner.Questions.CancelTask(question.TaskId); owner.Run(BoundSession.StopAsync); }));
            questions.Children.Add(new Border { Name = "AgentQuestionCard", Padding = new Thickness(10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = DesktopTheme.Brush("#D8E5F9"), Child = panel });
        }
    }
    private async Task ExportAsync()
    {
        var target=BoundSession;var file = await StorageProvider.SaveFilePickerAsync(LocalizedUi.With(new FilePickerSaveOptions { Title = TextResources.Get("chatWindow.exportAsync.title"), SuggestedFileName = (binding?.Id??owner.Skin.Id) + "-chat.md", DefaultExtension = "md" },"Title",()=>(TextResources.Get("chatWindow.exportAsync.title"))));
        if (file == null) return; await using var stream = await file.OpenWriteAsync(); using var writer = new StreamWriter(stream); await writer.WriteAsync(target.ExportHistory());
    }
    private void ConfirmClear()
    {
        var target=BoundSession;var confirm = LocalizedUi.Window(()=>(TextResources.Get("chatWindow.confirmClear.window")), LocalizedUi.Text(()=>(TextResources.Get("chatWindow.confirmClear.text"))), 420, 240);
        confirm.Content = new Border { Padding = new Thickness(20), Child = Ui.Column(LocalizedUi.Text(()=>(TextResources.Format("chatWindow.confirmClear.template",owner.Skin.DisplayName))), LocalizedUi.Button(()=>(TextResources.Get("chatWindow.confirmClear.text.alternativeA")), () => { Try(target.Clear); confirm.Close(); }), LocalizedUi.Button(()=>(TextResources.Get("common.cancel")), confirm.Close)) }; confirm.Show();
    }
}
