using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ImageWorkspacePage : TabControl, IDisposable
{
    protected override Type StyleKeyOverride => typeof(TabControl);
    private readonly ImageTaskCenterPage taskCenter;
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly ComboBox providers = new() { Name = "WorkspaceImageProvider", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox prompt = Ui.Input("", TextResources.Get("imageWorkspacePage.prompt.input"));
    private readonly ComboBox size = ParameterChoices("WorkspaceImageSize", new[] { "1024x1024", "1536x1024", "1024x1536", "auto" });
    private readonly ComboBox quality = ParameterChoices("WorkspaceImageQuality", new[] { "auto", "low", "medium", "high", "standard", "hd" });
    private readonly TextBlock status = Ui.Text(""), referenceLabel = Ui.Text(TextResources.Get("imageWorkspacePage.status.text")), capability = Ui.Text(""), warning = Ui.Text("");
    private readonly Button generate, cancel, referenceButton;
    private readonly Image referencePreview = new() { Height = 100, Stretch = Stretch.Uniform, IsVisible = false };
    private readonly StackPanel referenceChip;
    private readonly ImageWorkspacePreview result;
    private readonly TextBlock model = Ui.Text("", 13);
    private readonly ImageGalleryPanel gallery = new() { Name = "ImageGalleryTiles" };
    private readonly TextBlock galleryEmpty = Ui.Text("");
    private readonly TextBlock galleryCount = Ui.Text("");
    private readonly TextBlock galleryActionNotice = Ui.Text("");
    private readonly ImageGallerySelection gallerySelection = new();
    private readonly MenuItem multiSelect, restoreAction, purgeAction, cancelExport, clearTrash;
    private readonly Button trashAction;
    private readonly ContextMenu filterMenu = new();
    private readonly MenuItem providerMenu = new() { Header = TextResources.Get("imageWorkspacePage.providerMenu.header") }, modelMenu = new() { Header = TextResources.Get("imageWorkspacePage.providerMenu.header.alternativeA") }, sourceMenu = new() { Header = TextResources.Get("imageWorkspacePage.providerMenu.header.alternativeB") };
    private bool selecting;
    private CancellationTokenSource? exportCancellation;
    private readonly CheckBox favoritesOnly = new() { Content = TextResources.Get("imageWorkspacePage.favoritesOnly.content") }, trashOnly = new() { Content = TextResources.Get("imageWorkspacePage.favoritesOnly.content.alternativeA") };
    private readonly ComboBox providerFilter = new() { MinWidth = 110 }, modelFilter = new() { MinWidth = 110 }, sourceFilter = new() { MinWidth = 110 }, sortFilter = new() { MinWidth = 110 };
    private readonly TextBox fromDate = Ui.Input("", TextResources.Get("imageWorkspacePage.fromDate.input")), toDate = Ui.Input("", TextResources.Get("imageWorkspacePage.fromDate.input.alternativeA"));
    private int galleryTotal;
    private IList<GeneratedImage> loadedGallery = new List<GeneratedImage>();
    private int galleryRevision;
    private bool updatingFilters;
    private readonly Button loadMore;
    private readonly ComboBox layoutFilter = new() { Name = "GalleryLayout", Width = 94 };
    private int galleryLimit = 40;
    private readonly TextBox search = Ui.Input("", TextResources.Get("imageWorkspacePage.search.input"));
    private readonly List<GeneratedImageCard> galleryCards = new();


    private bool submitting;
    private readonly TabItem taskTab = new() { Header = TextResources.Get("imageWorkspacePage.taskTab.header") };
    private string? imageJobId;
    private byte[]? referenceBytes;
    private string? referenceMediaType;
    private string? referenceId;
    private bool missingReference;
    private Bitmap? referenceBitmap;
    private bool refreshing, disposed;
    private int galleryRefreshPending;
    private string? providerSignature;
    internal bool IsGenerating => imageJobId != null && owner.ImageTasks.Get(imageJobId) is { IsTerminal: false };
    internal int GalleryCount => galleryCards.Count;
    internal int GallerySelectedCount => gallerySelection.Count;
    internal ImageGalleryPanel GalleryPanel => gallery;
    internal TextBox Search => search;
    internal ComboBox ProviderChoices => providers;
    internal ComboBox SizeChoices => size;
    internal ComboBox QualityChoices => quality;
    internal Button ReferenceButton => referenceButton;
    internal static ComboBox ParameterChoices(string name, string[] values) => new() { Name = name, ItemsSource = new[] { TextResources.Get("imageWorkspacePage.parameterChoices.concat") }.Concat(values).ToArray(), SelectedIndex = 0, MinWidth = 145 };
    internal static void ConfigureParameters(ComboBox sizes, ComboBox qualities, string protocol)
    {
        sizes.ItemsSource = ImageGenerationProtocols.SizeOptions(protocol).Select(value => string.IsNullOrEmpty(value) ? TextResources.Get("imageWorkspacePage.configureParameters.imageGenerationProtocolsSizeOptions") : value).ToArray();
        qualities.ItemsSource = ImageGenerationProtocols.QualityOptions(protocol).Select(value => string.IsNullOrEmpty(value) ? TextResources.Get("imageWorkspacePage.configureParameters.imageGenerationProtocolsQualityOptions") : value).ToArray();
        sizes.SelectedIndex = qualities.SelectedIndex = 0;
    }
    internal static string Parameter(ComboBox box) => box.SelectedIndex <= 0 ? "" : box.SelectedItem as string ?? "";
    internal static void SelectParameter(ComboBox box, string? value) { if (string.IsNullOrEmpty(value)) box.SelectedIndex = 0; else { var choices = box.ItemsSource!.Cast<string>().ToList(); if (!choices.Contains(value)) { choices.Add(value); box.ItemsSource = choices; } box.SelectedItem = value; } }
    public ImageWorkspacePage(DesktopController controller, Window window)
    {
        owner = controller; host = window; Name = "ImageWorkspace";
        result = new ImageWorkspacePreview(owner, host, Prepare);
        prompt.Name = "ImagePrompt"; prompt.AcceptsReturn = true; prompt.Height = 70; prompt.MinWidth = 0; prompt.TextWrapping = TextWrapping.Wrap;
        prompt.Background = Brushes.Transparent; prompt.BorderThickness = new Thickness(0); prompt.Padding = new Thickness(4); prompt.UseFloatingPlaceholder = false;
        providers.SelectionChanged += (_, _) => { if (refreshing) return; if (providers.SelectedItem is ImageGenerationProvider p) { owner.Settings.ActiveImageProviderId = p.Id; owner.Save(); ConfigureParameters(size, quality, p.Protocol); SelectParameter(size, p.Size); SelectParameter(quality, p.Quality); } UpdateCapability(); };
        generate = Ui.Button(TextResources.Get("imageWorkspacePage.imageWorkspacePage.text"), () => owner.Run(GenerateAsync)); generate.Name = "StartImageGeneration"; generate.CornerRadius = new CornerRadius(18); generate.Background = DesktopTheme.Brush("#357FF5"); generate.Foreground = Brushes.White;
        cancel = Ui.Button(TextResources.Get("common.cancel"), () => { if (imageJobId != null) _ = owner.ImageTasks.CancelAsync(imageJobId); }); cancel.Name = "CancelImageGeneration"; cancel.IsEnabled = false;
        referenceButton = Ui.AsyncButton("+", UploadAsync, owner.Error); referenceButton.Name = "UploadImageReference"; referenceButton.Width = referenceButton.Height = 34; referenceButton.Padding = new Thickness(0); referenceButton.FontSize = 23; referenceButton.CornerRadius = new CornerRadius(17); ToolTip.SetTip(referenceButton, TextResources.Get("imageWorkspacePage.imageWorkspacePage.toolTipSetTip")); AutomationProperties.SetName(referenceButton, TextResources.Get("imageWorkspacePage.imageWorkspacePage.automationPropertiesSetName"));
        var remove = Ui.Button(TextResources.Get("imageWorkspacePage.imageWorkspacePage.text.alternativeA"), ClearReference); remove.Name = "RemoveImageReference";
        referenceLabel.FontSize = 11; referenceLabel.MaxWidth = 140; referenceLabel.MaxHeight = 28; referencePreview.Height = 44;
        referenceChip = Ui.Row(referencePreview, referenceLabel, remove); referenceChip.IsVisible = false;
        referencePreview.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) referenceChip.IsVisible = referencePreview.IsVisible || missingReference; };
        var parameters = Ui.Column(Ui.Text(TextResources.Get("imageWorkspacePage.imageWorkspacePage.text.alternativeB"), 20), Ui.Field(TextResources.Get("imageWorkspacePage.imageWorkspacePage.field"), providers), Ui.Field(TextResources.Get("imageWorkspacePage.imageWorkspacePage.field.alternativeA"), model), Ui.Field(TextResources.Get("imageWorkspacePage.imageWorkspacePage.field.alternativeB"), size), Ui.Field(TextResources.Get("imageWorkspacePage.imageWorkspacePage.field.alternativeC"), quality), capability, Ui.Button(TextResources.Get("imageWorkspacePage.imageWorkspacePage.text.alternativeC"), () => owner.ShowSettingsSection("image")), cancel);
        capability.FontSize = 11; parameters.Spacing = 14;
        var parameterSurface = new Border { Name = "ImageGenerationParameters", Padding = new Thickness(16), Background = DesktopTheme.Surface, CornerRadius = new CornerRadius(16), Child = parameters };
        var stage = new Grid { Name = "ImageGenerationStage", ColumnDefinitions = new ColumnDefinitions("208,*"), RowDefinitions = new RowDefinitions("*"), ColumnSpacing = 16 };
        stage.Children.Add(parameterSurface); Grid.SetColumn(result, 1); stage.Children.Add(result);
        var composerActions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 }; composerActions.Children.Add(referenceButton); Grid.SetColumn(referenceChip, 1); composerActions.Children.Add(referenceChip); Grid.SetColumn(generate, 2); composerActions.Children.Add(generate);
        var composer = new Border { Name = "ImagePromptComposer", CornerRadius = new CornerRadius(24), Padding = new Thickness(14, 10), Background = DesktopTheme.Surface, BorderBrush = DesktopTheme.Brush("#E0E4E9"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 12, 0, 0), Child = Ui.Column(prompt, composerActions) };
        var generation = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), Margin = new Thickness(14) }; generation.Children.Add(stage); Grid.SetRow(composer, 1); generation.Children.Add(composer); Grid.SetRow(status, 2); status.FontSize = 11; status.Margin = new Thickness(6, 4, 0, 0); generation.Children.Add(status);
        generation.SizeChanged += (_, _) =>
        {
            bool narrow = generation.Bounds.Width < 610;
            stage.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "208,*"); stage.RowDefinitions = new RowDefinitions(narrow ? "Auto,*" : "*");
            Grid.SetColumn(result, narrow ? 0 : 1); Grid.SetRow(result, narrow ? 1 : 0);
            parameterSurface.Margin = new Thickness(0, 0, 0, narrow ? 10 : 0); parameterSurface.Padding = new Thickness(narrow ? 10 : 16);
            parameters.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical; parameters.Spacing = narrow ? 8 : 14;
            for (int i = 0; i < parameters.Children.Count; i++) parameters.Children[i].IsVisible = !narrow || i == 1 || i == 3 || i == 4;
            parameters.Children[1].Width = narrow ? Math.Max(86, generation.Bounds.Width - 254) : double.NaN;
            parameters.Children[3].Width = parameters.Children[4].Width = narrow ? 100 : double.NaN;
            providers.MinWidth = size.MinWidth = quality.MinWidth = 0;
        };
        loadMore = Ui.Button(TextResources.Get("imageWorkspacePage.imageWorkspacePage.text.alternativeD"), () => { galleryLimit += 40; RefreshGallery(); }); loadMore.Name = "LoadMoreGalleryImages";
        providerFilter.ItemsSource = new[] { TextResources.Get("imageWorkspacePage.imageWorkspacePage.providerFilterItemsSource") }; providerFilter.SelectedIndex = 0; modelFilter.ItemsSource = new[] { TextResources.Get("imageWorkspacePage.imageWorkspacePage.modelFilterItemsSource") }; modelFilter.SelectedIndex = 0;
        sourceFilter.ItemsSource = new[] { TextResources.Get("imageWorkspacePage.imageWorkspacePage.sourceFilterItemsSource"), TextResources.Get("imageWorkspacePage.imageWorkspacePage.sourceFilterItemsSource.alternativeA"), TextResources.Get("imageWorkspacePage.imageWorkspacePage.sourceFilterItemsSource.alternativeB"), TextResources.Get("imageWorkspacePage.imageWorkspacePage.sourceFilterItemsSource.alternativeC"), TextResources.Get("imageWorkspacePage.imageWorkspacePage.sourceFilterItemsSource.alternativeD") }; sourceFilter.SelectedIndex = 0;
        sortFilter.Name = "GallerySort"; sortFilter.ItemsSource = new[] { TextResources.Get("imageWorkspacePage.imageWorkspacePage.sortFilterItemsSource"), TextResources.Get("imageWorkspacePage.imageWorkspacePage.sortFilterItemsSource.alternativeA") }; sortFilter.SelectedIndex = 0; sortFilter.Width = 94; sortFilter.MinWidth = 0;
        layoutFilter.ItemsSource = new[] { TextResources.Get("imageWorkspacePage.imageWorkspacePage.layoutFilterItemsSource"), TextResources.Get("imageWorkspacePage.imageWorkspacePage.layoutFilterItemsSource.alternativeA") }; layoutFilter.SelectionChanged += (_, _) => { if (!updatingFilters) SelectGalleryLayout(layoutFilter.SelectedIndex == 1 ? "grid" : "masonry"); };
        search.Name = "GallerySearch"; search.PlaceholderText = TextResources.Get("imageWorkspacePage.imageWorkspacePage.searchPlaceholderText"); search.Width = 90; search.MinWidth = 0; search.Height = 32; search.FontSize = 12; ToolTip.SetTip(search, TextResources.Get("imageWorkspacePage.imageWorkspacePage.toolTipSetTip.alternativeA"));
        var filterArrow = new PathIcon { Name = "GalleryFilterArrow", Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        PathIcon? sortArrow = null, layoutArrow = null;
        void UseGalleryArrow(PathIcon target)
        {
            if (sortArrow == null) return;
            target.Bind(PathIcon.DataProperty, sortArrow.GetObservable(PathIcon.DataProperty));
            target.Bind(TemplatedControl.ForegroundProperty, sortArrow.GetObservable(TemplatedControl.ForegroundProperty));
        }
        // Copy the actual Fluent ComboBox vector and observe its foreground so
        // all three arrows stay identical when the application theme changes.
        sortFilter.TemplateApplied += (_, e) =>
        {
            sortArrow = e.NameScope.Find<PathIcon>("DropDownGlyph"); UseGalleryArrow(filterArrow); if (layoutArrow != null) UseGalleryArrow(layoutArrow);
        };
        layoutFilter.TemplateApplied += (_, e) => { layoutArrow = e.NameScope.Find<PathIcon>("DropDownGlyph"); if (layoutArrow != null) UseGalleryArrow(layoutArrow); };
        var filterContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12") }; filterContent.Children.Add(new TextBlock { Text = TextResources.Get("imageWorkspacePage.imageWorkspacePage.text.alternativeE"), VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(filterArrow, 1); filterContent.Children.Add(filterArrow);
        var filter = new Button { Content = filterContent, HorizontalContentAlignment = HorizontalAlignment.Stretch }; filter.Click += (_, _) => { RefreshFilterMenu(); filterMenu.Open(filter); }; filter.Name = "GalleryFilter"; filter.ContextMenu = filterMenu; filter.Width = 66; filter.Padding = new Thickness(10, 0); AutomationProperties.SetName(filter, TextResources.Get("imageWorkspacePage.imageWorkspacePage.automationPropertiesSetName.alternativeA"));
        multiSelect = MenuAction("GalleryMultiSelect", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction"), () => { selecting = !selecting; if (!selecting) gallerySelection.Clear(); RefreshSelection(); });
        var selectLoaded = MenuAction("GallerySelectLoaded", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeA"), () => { selecting = true; gallerySelection.SelectLoaded(loadedGallery); RefreshSelection(); });
        var favoriteAll = MenuAction("GalleryFavoriteSelected", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeB"), () => SetSelectedFavorite(true));
        var unfavoriteAll = MenuAction("GalleryUnfavoriteSelected", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeC"), () => SetSelectedFavorite(false));
        var export = MenuAction("GalleryExportSelected", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeD"), () => owner.Run(ExportSelectedAsync));
        cancelExport = MenuAction("GalleryCancelExport", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeE"), () => exportCancellation?.Cancel()); cancelExport.IsVisible = false;
        trashAction = Ui.Button("", () => ModifySelected(false)); trashAction.Name = "GalleryTrashSelected"; trashAction.Width = trashAction.Height = 32; trashAction.Padding = new Thickness(7); trashAction.IsEnabled = false; ToolTip.SetTip(trashAction, TextResources.Get("imageWorkspacePage.imageWorkspacePage.toolTipSetTip.alternativeB")); AutomationProperties.SetName(trashAction, TextResources.Get("imageWorkspacePage.imageWorkspacePage.automationPropertiesSetName.alternativeB"));
        trashAction.Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 3,5 L 15,5 M 6,5 L 6,2 L 12,2 L 12,5 M 5,5 L 6,17 L 12,17 L 13,5 M 8,8 L 8,14 M 10,8 L 10,14"), StrokeThickness = 1.5, Stroke = DesktopTheme.Brush("#A0A8B3"), Width = 18, Height = 18, Stretch = Stretch.Uniform };
        restoreAction = MenuAction("GalleryRestoreSelected", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeF"), () => ModifySelected(true));
        purgeAction = MenuAction("GalleryPurgeSelected", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeG"), () => owner.Run(PurgeSelectedAsync));
        clearTrash = MenuAction("GalleryClearTrash", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeH"), () => owner.Run(ClearTrashAsync));
        var cleanup = MenuAction("GalleryCleanupOrphans", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeI"), () => owner.Run(CleanupOrphansAsync));
        var recovery = MenuAction("GalleryRecover", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeJ"), () => { try { var count = owner.ImageStore.Recover(); RefreshGallery(); galleryActionNotice.Text = TextResources.Format("imageWorkspacePage.imageWorkspacePage.galleryActionNoticeCount",count); } catch (Exception e) { owner.Error(e); } });
        var favorites = new MenuItem { Name = "GalleryFavoritesOnly", Header = TextResources.Get("imageWorkspacePage.imageWorkspacePage.header"), ToggleType = MenuItemToggleType.CheckBox }; favorites.Click += (_, _) => favoritesOnly.IsChecked = favorites.IsChecked;
        var trash = new MenuItem { Name = "GalleryTrashView", Header = TextResources.Get("imageWorkspacePage.imageWorkspacePage.header.alternativeA"), ToggleType = MenuItemToggleType.CheckBox }; trash.Click += (_, _) => trashOnly.IsChecked = trash.IsChecked;
        var dates = new MenuItem { Header = TextResources.Get("imageWorkspacePage.imageWorkspacePage.header.alternativeB"), ItemsSource = new object[] { new MenuItem { Header = Ui.Field(TextResources.Get("imageWorkspacePage.imageWorkspacePage.field.alternativeD"), fromDate) }, new MenuItem { Header = Ui.Field(TextResources.Get("imageWorkspacePage.imageWorkspacePage.field.alternativeE"), toDate) } } };
        var management = new MenuItem { Header = TextResources.Get("imageWorkspacePage.imageWorkspacePage.header.alternativeC"), ItemsSource = new object[] { multiSelect, selectLoaded, MenuAction("GalleryClearSelection", TextResources.Get("imageWorkspacePage.imageWorkspacePage.menuAction.alternativeK"), () => { gallerySelection.Clear(); RefreshSelection(); }), favoriteAll, unfavoriteAll, export, cancelExport, new Separator(), trash, restoreAction, purgeAction, clearTrash, new Separator(), cleanup, recovery } };
        filterMenu.ItemsSource = new object[] { favorites, modelMenu, sourceMenu, providerMenu, dates, new Separator(), management }; filterMenu.Opening += (_, _) => { RefreshFilterMenu(); favorites.IsChecked = favoritesOnly.IsChecked == true; trash.IsChecked = trashOnly.IsChecked == true; };
        var toolbar = Ui.Row(search, filter, sortFilter, layoutFilter, trashAction); toolbar.Name = "GalleryToolbar"; toolbar.Spacing = 5; toolbar.HorizontalAlignment = HorizontalAlignment.Right; toolbar.Margin = new Thickness(0, 0, 0, 12);
        foreach (var control in new Control[] { filter, sortFilter, layoutFilter }) control.Height = 32;
        filter.FontSize = sortFilter.FontSize = layoutFilter.FontSize = 12;
        var galleryBody = Ui.Column(galleryActionNotice, warning, galleryEmpty, gallery, loadMore); galleryBody.Spacing = 8;
        galleryActionNotice.IsVisible = warning.IsVisible = false;
        galleryActionNotice.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) galleryActionNotice.IsVisible = !string.IsNullOrEmpty(galleryActionNotice.Text); };
        warning.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) warning.IsVisible = !string.IsNullOrEmpty(warning.Text); };
        var galleryPage = new DockPanel { Margin = new Thickness(14), LastChildFill = true }; DockPanel.SetDock(toolbar, Dock.Top); galleryPage.Children.Add(toolbar); galleryPage.Children.Add(new ScrollViewer { Content = galleryBody });
        galleryPage.SizeChanged += (_, _) => search.Width = Math.Clamp(galleryPage.Bounds.Width - 306, 30, 140);
        taskCenter = new ImageTaskCenterPage(owner, host); taskTab.Content = taskCenter;
        ItemsSource = new[] { new TabItem { Header = TextResources.Get("imageWorkspacePage.imageWorkspacePage.header.alternativeD"), Content = generation }, new TabItem { Header = TextResources.Get("imageWorkspacePage.imageWorkspacePage.header.alternativeE"), Content = galleryPage }, taskTab };
        SelectedIndex = 0;
        search.TextChanged += (_, _) => { ToolTip.SetTip(search, string.IsNullOrEmpty(search.Text) ? TextResources.Get("imageWorkspacePage.imageWorkspacePage.toolTipSetTip.alternativeA") : search.Text); FilterChanged(); }; favoritesOnly.IsCheckedChanged += (_, _) => FilterChanged(); trashOnly.IsCheckedChanged += (_, _) => FilterChanged(); providerFilter.SelectionChanged += (_, _) => FilterChanged(); modelFilter.SelectionChanged += (_, _) => FilterChanged(); sourceFilter.SelectionChanged += (_, _) => FilterChanged(); sortFilter.SelectionChanged += (_, _) => FilterChanged(); fromDate.TextChanged += (_, _) => FilterChanged(); toDate.TextChanged += (_, _) => FilterChanged(); owner.ImageStore.Changed += StoreChanged; owner.Changed += RefreshProviders;
        RefreshProviders(); RefreshGallery(); owner.ImageTasks.Changed += TaskChanged; TaskChanged(null!); RebindTask();
    }
    private static MenuItem MenuAction(string name, string title, Action action)
    {
        var item = new MenuItem { Name = name, Header = title }; item.Click += (_, _) => action(); return item;
    }
    private void RefreshFilterMenu()
    {
        void Populate(MenuItem menu, ComboBox box)
        {
            menu.ItemsSource = box.Items.Cast<object>().Select((value, index) => { var item = new MenuItem { Header = value.ToString(), IsChecked = box.SelectedIndex == index, ToggleType = MenuItemToggleType.Radio, GroupName = menu.Header!.ToString() }; item.Click += (_, _) => box.SelectedIndex = index; return item; }).ToArray();
        }
        Populate(providerMenu, providerFilter); Populate(modelMenu, modelFilter); Populate(sourceMenu, sourceFilter);
        restoreAction.IsVisible = purgeAction.IsVisible = clearTrash.IsVisible = trashOnly.IsChecked == true;
        restoreAction.IsEnabled = purgeAction.IsEnabled = gallerySelection.Count > 0;
    }
    private void StoreChanged()
    {
        if (Interlocked.Exchange(ref galleryRefreshPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref galleryRefreshPending, 0); if (!disposed) RefreshGallery(); });
    }
    private void RefreshProviders()
    {
        if (disposed) return; RefreshGalleryLayout(); owner.Settings.EnsureImageProviders();
        var signature = string.Join("|", owner.Settings.ImageProviders.Select(p => p.Id + p.Name + p.Protocol + p.Connection.Model + p.Connection.BaseUrl + p.Connection.EncryptedKey + p.Connection.UseProxy + p.Connection.ProxyAddress + p.TimeoutSeconds + p.SupportsEdits + p.Size + p.Quality)) + owner.Settings.ActiveImageProviderId;
        if (signature == providerSignature) return; providerSignature = signature; refreshing = true;
        providers.ItemsSource = owner.Settings.ImageProviders.Select(p => p.Copy()).ToList(); providers.SelectedItem = providers.ItemsSource.Cast<ImageGenerationProvider>().FirstOrDefault(p => p.Id == owner.Settings.ActiveImageProviderId) ?? providers.ItemsSource.Cast<ImageGenerationProvider>().FirstOrDefault(); refreshing = false;
        if (providers.SelectedItem is ImageGenerationProvider selected) { ConfigureParameters(size, quality, selected.Protocol); SelectParameter(size, selected.Size); SelectParameter(quality, selected.Quality); }
        UpdateCapability();
    }
    private void SelectGalleryLayout(string layout)
    {
        if (owner.Settings.ImageGalleryLayout == layout) return;
        var previous = owner.Settings.ImageGalleryLayout;
        try { owner.Settings.ImageGalleryLayout = layout; owner.Save(); RefreshGalleryLayout(); }
        catch (Exception e) { owner.Settings.ImageGalleryLayout = previous; RefreshGalleryLayout(); owner.Error(e); }
    }
    private void RefreshGalleryLayout()
    {
        var mode = owner.Settings.ImageGalleryLayout == "grid" ? "grid" : "masonry";
        gallery.LayoutMode = mode;
        foreach (var card in galleryCards) card.SetGalleryLayout(mode);
        updatingFilters = true; layoutFilter.SelectedIndex = mode == "grid" ? 1 : 0; updatingFilters = false;
    }
    private void UpdateCapability()
    {
        var selected = providers.SelectedItem as ImageGenerationProvider; var busy = submitting;
        model.Text = selected?.Connection.Model ?? TextResources.Get("imageWorkspacePage.updateCapability.selectedConnectionModel"); model.MaxHeight = 48; model.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(model, model.Text); ToolTip.SetTip(providers, selected == null ? TextResources.Get("imageWorkspacePage.updateCapability.toolTipSetTip") : selected.Name + " · " + selected.Connection.Model);
        capability.Text = selected == null ? TextResources.Get("imageWorkspacePage.updateCapability.nullSelectedSupportsEdits") : selected.SupportsEdits ? TextResources.Get("imageWorkspacePage.updateCapability.nullSelectedSupportsEdits.alternativeA") : TextResources.Get("imageWorkspacePage.updateCapability.nullSelectedSupportsEdits.alternativeB");
        referenceButton.IsEnabled = !busy && selected?.SupportsEdits == true; generate.IsEnabled = !busy && selected != null; providers.IsEnabled = !busy;
        prompt.IsReadOnly = busy; size.IsEnabled = quality.IsEnabled = !busy; cancel.IsEnabled = IsGenerating;
    }
    private async Task UploadAsync()
    {
        if (providers.SelectedItem is not ImageGenerationProvider { SupportsEdits: true }) throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.uploadAsync.text"));
        var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = TextResources.Get("imageWorkspacePage.uploadAsync.title"), AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType(TextResources.Get("imageWorkspacePage.uploadAsync.fileTypeFilterFilePickerFileType")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } } } });
        var file = files.FirstOrDefault(); if (file == null || disposed || submitting) return;
        await using var input = await file.OpenReadAsync(); using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer)) != 0) { if (output.Length + count > 50L * 1024 * 1024) throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.uploadAsync.text.alternativeA")); await output.WriteAsync(buffer.AsMemory(0, count)); }
        SetReference(output.ToArray(), file.Name);
    }
    internal void SetReference(byte[] bytes, string filename)
    {
        if (submitting || disposed) return;
        if (bytes.Length > 50L * 1024 * 1024) throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.setReference.text"));
        var media = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 ? "image/png" : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg" : bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? "image/webp" : throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.setReference.encodingASCIIGetString"));
        using var stream = new MemoryStream(bytes); var bitmap = Bitmap.DecodeToWidth(stream, 240); ClearReference(); referenceBitmap = bitmap; referenceBytes = bytes.ToArray(); referenceMediaType = media; referencePreview.Source = bitmap; referencePreview.IsVisible = true; referenceLabel.Text = filename;
    }
    private void ClearReference() { if (submitting || disposed) return; if (missingReference) status.Text = TextResources.Get("imageWorkspacePage.clearReference.status"); referencePreview.Source = null; referenceBitmap?.Dispose(); referenceBitmap = null; referenceBytes = null; referenceMediaType = null; referenceId = null; missingReference = false; referencePreview.IsVisible = false; referenceChip.IsVisible = false; referenceLabel.Text = TextResources.Get("imageWorkspacePage.clearReference.referenceLabel"); }
    internal void Prepare(GeneratedImage image)
    {
        if (submitting || disposed) return; SelectedIndex = 0; prompt.Text = image.Prompt;
        var selected = providers.ItemsSource?.Cast<ImageGenerationProvider>().FirstOrDefault(p => p.Id == image.ProviderId);
        if (selected != null) providers.SelectedItem = selected; SelectParameter(size, image.Size); SelectParameter(quality, image.Quality); ClearReference();
        status.Text = TextResources.Get("imageWorkspacePage.prepare.status");
        if (!string.IsNullOrEmpty(image.ReferenceImageId))
        {
            try { SetReference(owner.ImageStore.Read(image.ReferenceImageId), TextResources.Get("imageWorkspacePage.prepare.setReference")); referenceId = image.ReferenceImageId; }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { missingReference = true; referenceChip.IsVisible = true; referenceLabel.Text = TextResources.Get("imageWorkspacePage.prepare.referenceLabel"); status.Text = referenceLabel.Text; }
        }
    }
    internal async Task GenerateAsync()
    {
        if (submitting || disposed) return;
        var provider = (providers.SelectedItem as ImageGenerationProvider)?.Copy() ?? throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.generateAsync.provider"));
        var text = prompt.Text?.Trim(); if (string.IsNullOrEmpty(text)) throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.generateAsync.text"));
        if (missingReference) throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.generateAsync.text.alternativeA"));
        if (referenceBytes != null && !provider.SupportsEdits) throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.generateAsync.text.alternativeB"));
        ImageGenerationTaskHandle? handle=null;submitting=true;UpdateCapability();
        try
        {
            try { handle=await AcceptSubmission(provider,text);if(!disposed)status.Text=TextResources.Get("imageWorkspacePage.generateAsync.status"); }
            finally { submitting=false;if(!disposed)UpdateCapability(); }
            await handle.Completion;
            if(!disposed && imageJobId==handle.Id)status.Text=TextResources.Get("imageWorkspacePage.generateAsync.status.alternativeA");
        }
        catch(OperationCanceledException){if(!disposed && (handle==null||imageJobId==handle.Id))status.Text=TextResources.Get("imageWorkspacePage.generateAsync.status.alternativeB");}
        catch(Exception e){if(!disposed && (handle==null||imageJobId==handle.Id))status.Text=TextResources.Format("imageWorkspacePage.generateAsync.statusEMessage",e.Message);}
        finally { if(!disposed)UpdateCapability(); }
    }
    private Task<ImageGenerationTaskHandle> AcceptSubmission(ImageGenerationProvider provider,string text)
    {
        return owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest { Provider=provider,Prompt=text,Size=Parameter(size),Quality=Parameter(quality),ReferenceImageBytes=referenceBytes,ReferenceMediaType=referenceMediaType,ReferenceImageId=referenceId,Persistent=owner.Settings.SaveChatHistory,TaskId=Guid.NewGuid().ToString("N") },new ImageTaskOrigin{Source=ImageGenerationSource.Workspace}) { BeforeStart=id=>{ if(!disposed){imageJobId=id;result.ShowTask(id);}return Task.CompletedTask;} },CancellationToken.None);
    }
    private void TaskChanged(ImageGenerationTaskSnapshot task)=>Dispatcher.UIThread.Post(()=>{if(!disposed){taskTab.Header=TextResources.Format("imageWorkspacePage.taskChanged.ownerImageTasksList",owner.ImageTasks.List().Count(t=>!t.IsTerminal));UpdateCapability();}});
    private void RebindTask(){var task=owner.ImageTasks.List().LastOrDefault(t=>t.Source==ImageGenerationSource.Workspace);if(task==null)return;imageJobId=task.Id;result.ShowTask(task.Id);}
    private void FilterChanged() { if (updatingFilters) return; galleryLimit = 40; gallerySelection.Clear(); RefreshSelection(); RefreshGallery(); }
    internal void RefreshGallery()
    {
        if (disposed) return;
        int revision = Interlocked.Increment(ref galleryRevision);
        if (!ParseDate(fromDate.Text, false, out var from) || !ParseDate(toDate.Text, true, out var to)) { warning.Text = galleryCount.Text = TextResources.Get("imageWorkspacePage.refreshGallery.galleryCount"); return; }
        var request = new ImageGalleryQuery { Keyword = search.Text, FavoritesOnly = favoritesOnly.IsChecked == true, Trash = trashOnly.IsChecked == true, ProviderId = providerFilter.SelectedIndex > 0 ? (providerFilter.SelectedItem as ImageGalleryProviderChoice)?.Id : null, Model = modelFilter.SelectedIndex > 0 ? modelFilter.SelectedItem as string : null, OldestFirst = sortFilter.SelectedIndex == 1, Limit = galleryLimit, FromUtc = from, ToUtc = to };
        if (sourceFilter.SelectedIndex > 0) request.Source = sourceFilter.SelectedIndex switch { 1 => ImageGenerationSource.Workspace, 2 => ImageGenerationSource.Conversation, 3 => ImageGenerationSource.SettingsTest, _ => ImageGenerationSource.Unknown };
        _ = Task.Run(() => { var options = owner.ImageStore.ListIncludingTrash(); return (Page: new ImageGalleryQueryService(owner.ImageStore).Query(request), Providers: options.Select(i => i.ProviderId).Where(i => !string.IsNullOrEmpty(i)).Distinct().OrderBy(i => i).ToArray(), Models: options.Select(i => i.Model).Where(i => !string.IsNullOrEmpty(i)).Distinct().OrderBy(i => i).ToArray()); }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            if (disposed || revision != galleryRevision) return;
            if (task.IsFaulted) { warning.Text = TextResources.Get("imageWorkspacePage.refreshGallery.warning"); return; }
            updatingFilters = true; UpdateProviderOptions(providerFilter, task.Result.Providers, owner.Settings.ImageProviders); UpdateOptions(modelFilter, TextResources.Get("imageWorkspacePage.refreshGallery.updateOptions"), task.Result.Models); updatingFilters = false;
            var effectiveProvider=providerFilter.SelectedIndex>0?(providerFilter.SelectedItem as ImageGalleryProviderChoice)?.Id:null;var effectiveModel=modelFilter.SelectedIndex>0?modelFilter.SelectedItem as string:null;
            // Reconciliation can remove the selected option. Never render its obsolete page.
            if(request.ProviderId!=effectiveProvider||request.Model!=effectiveModel){FilterChanged();return;}
            foreach (var card in galleryCards) card.Dispose(); galleryCards.Clear(); gallery.Children.Clear(); warning.Text = owner.ImageStore.Warning;
            var result = task.Result.Page; loadedGallery = result.Items; galleryTotal=result.Total; galleryCount.Text = TextResources.Format("imageWorkspacePage.refreshGallery.countGallerySelectionCount",result.Total,result.Items.Count,gallerySelection.Count);
            RefreshSelection(); RefreshFilterMenu();
            galleryEmpty.Text = string.IsNullOrWhiteSpace(search.Text) ? TextResources.Get("imageWorkspacePage.refreshGallery.galleryEmptyIsNullOrWhiteSpace") : TextResources.Get("imageWorkspacePage.refreshGallery.galleryEmptyIsNullOrWhiteSpace.alternativeA"); galleryEmpty.IsVisible = result.Total == 0;
            foreach (var item in result.Items) { var card = new GeneratedImageCard(owner, host, item, Prepare, owner.Settings.ImageGalleryLayout, ToggleSelected, ToggleFavorite, gallerySelection.Contains(item.Id), selecting); galleryCards.Add(card); gallery.Children.Add(card); }
            gallery.SetImages(result.Items); loadMore.IsVisible = result.Total > result.Items.Count; RefreshGalleryLayout();
        }));
    }
    private static bool ParseDate(string? input, bool end, out DateTime? value)
    {
        return ImageGalleryDateFilter.TryParseDate(input, end, out value);
    }
    private static void UpdateOptions(ComboBox box, string all, string[] choices)
    {
        var previous = box.SelectedItem as string; var values = new[] { all }.Concat(choices).ToArray(); box.ItemsSource = values; box.SelectedItem = values.Contains(previous) ? previous : all;
    }
    private static void UpdateProviderOptions(ComboBox box, string[] ids, IEnumerable<ImageGenerationProvider> configured)
    {
        var selected = (box.SelectedItem as ImageGalleryProviderChoice)?.Id;
        var values = new List<ImageGalleryProviderChoice> { new() { Id = "", Label = TextResources.Get("imageWorkspacePage.updateProviderOptions.text") } };
        foreach (var id in ids) { var provider = configured.FirstOrDefault(p => p.Id == id); values.Add(new ImageGalleryProviderChoice { Id = id, Label = provider == null ? TextResources.Format("imageWorkspacePage.updateProviderOptions.nullIdSubstring",id.Substring(0, Math.Min(6, id.Length))) : provider.Name }); }
        box.ItemsSource = values; box.SelectedItem = values.FirstOrDefault(i => i.Id == selected) ?? values[0];
    }
    private void ToggleSelected(string id) { gallerySelection.Toggle(id); RefreshSelection(); }
    private void RefreshSelection() { multiSelect.Header=selecting?TextResources.Get("imageWorkspacePage.refreshSelection.multiSelectHeaderSelecting"):TextResources.Get("imageWorkspacePage.refreshSelection.multiSelectHeaderSelecting.alternativeA");trashAction.IsEnabled=gallerySelection.Count>0&&trashOnly.IsChecked!=true;if(trashAction.Content is Avalonia.Controls.Shapes.Path icon)icon.Stroke=DesktopTheme.Brush(trashAction.IsEnabled?"#D64B55":"#A0A8B3");galleryCount.Text=TextResources.Format("imageWorkspacePage.refreshSelection.countGallerySelectionCount",galleryTotal,loadedGallery.Count,gallerySelection.Count);foreach(var card in galleryCards)card.UpdateSelection(gallerySelection.Contains(card.GalleryAssetId),selecting); }
    private void ToggleFavorite(string id) { try { var image = owner.ImageStore.Get(id); owner.ImageStore.SetFavorite(new[] { id }, !image.IsFavorite); } catch (Exception e) { owner.Error(e); } }
    private void SetSelectedFavorite(bool value) { if (gallerySelection.Count == 0) return; try { owner.ImageStore.SetFavorite(gallerySelection.Ids, value); galleryActionNotice.Text = value ? TextResources.Get("imageWorkspacePage.setSelectedFavorite.galleryActionNoticeValue") : TextResources.Get("imageWorkspacePage.setSelectedFavorite.galleryActionNoticeValue.alternativeA"); } catch (Exception e) { owner.Error(e); } }
    private static string ManagementSummary(ImagePurgeResult result) => TextResources.Get("imageWorkspacePage.managementSummary.text") + result.DeletedIds.Count + TextResources.Get("imageWorkspacePage.managementSummary.text.alternativeA") + result.Blocked.Count + TextResources.Get("imageWorkspacePage.managementSummary.text.alternativeB") + string.Join("；", result.Blocked.Select(i => i.Key + "：" + i.Value)) + (result.CleanupPendingIds.Count > 0 ? TextResources.Format("imageWorkspacePage.managementSummary.template",result.CleanupPendingIds.Count) : "");
    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var dialog = Ui.Window(title, Ui.Text(""), 450, 240); dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Content = new Border { Padding = new Thickness(22), Child = Ui.Column(Ui.Text(message), Ui.Row(Ui.Button(TextResources.Get("common.cancel"), () => dialog.Close(false)), Ui.Button(action, () => dialog.Close(true)))) };
        return await dialog.ShowDialog<bool>(host);
    }
    private async void ModifySelected(bool restore)
    {
        if (gallerySelection.Count == 0) return;
        var ids = gallerySelection.Ids.ToArray();
        try { if (restore) owner.ImageStore.Restore(ids); else if (await ConfirmAsync(TextResources.Get("imageWorkspacePage.modifySelected.confirmAsync.alternativeA"), TextResources.Format("imageWorkspacePage.modifySelected.confirmAsync",ids.Length), TextResources.Get("imageWorkspacePage.modifySelected.confirmAsync.alternativeA"))) owner.ImageStore.MoveToTrash(ids); else return; gallerySelection.Clear(); RefreshGallery(); }
        catch (Exception e) { owner.Error(e); }
    }
    private async Task ExportSelectedAsync()
    {
        if (gallerySelection.Count == 0 || exportCancellation != null) return;
        var folders = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = TextResources.Get("imageWorkspacePage.exportSelectedAsync.title"), AllowMultiple = false }); var folder = folders.FirstOrDefault(); if (folder == null) return;
        var path = folder.TryGetLocalPath(); if (string.IsNullOrEmpty(path)) throw new InvalidOperationException(TextResources.Get("imageWorkspacePage.exportSelectedAsync.text"));
        using var source = new CancellationTokenSource(); exportCancellation = source; cancelExport.IsVisible = true; var ids = gallerySelection.Ids.ToArray();
        galleryActionNotice.Text = TextResources.Get("imageWorkspacePage.exportSelectedAsync.galleryActionNotice");
        try
        {
            var progress = new Progress<ImageExportItem>(_ => galleryCount.Text = TextResources.Get("imageWorkspacePage.exportSelectedAsync.galleryCount"));
            var summary = await new ImageGalleryExport(owner.ImageStore, owner.ImageUsage).ExportAsync(ids, path, progress, source.Token);
            galleryCount.Text = (summary.Canceled ? TextResources.Get("imageWorkspacePage.exportSelectedAsync.galleryCount.alternativeA") : "") + TextResources.Get("imageWorkspacePage.exportSelectedAsync.galleryCount.alternativeB") + summary.Completed + TextResources.Get("imageWorkspacePage.exportSelectedAsync.galleryCount.alternativeC") + summary.Failed + TextResources.Get("imageWorkspacePage.exportSelectedAsync.galleryCount.alternativeD");
            galleryActionNotice.Text = string.Join("\n", summary.Items.Select(i => i.Id + "：" + (i.Path ?? i.Error ?? (i.Canceled ? TextResources.Get("imageWorkspacePage.exportSelectedAsync.summaryItemsSelect") : TextResources.Get("imageWorkspacePage.exportSelectedAsync.summaryItemsSelect.alternativeA")))));
        }
        finally { exportCancellation = null; cancelExport.IsVisible = false; }
    }
    private async Task PurgeSelectedAsync()
    {
        if (gallerySelection.Count == 0) return; var ids = gallerySelection.Ids.ToArray();
        var preview = await owner.ImageManagement.InspectReferencesAsync(ids, CancellationToken.None);
        var assets = owner.ImageStore.ListIncludingTrash().Where(i => ids.Contains(i.Id) && i.TrashedUtc.HasValue).ToList();
        if (!await ConfirmAsync(TextResources.Get("imageWorkspacePage.purgeSelectedAsync.confirmAsync.alternativeA"), TextResources.Format("imageWorkspacePage.purgeSelectedAsync.confirmAsync",assets.Count,assets.Sum(i => i.Bytes) / 1024,preview.Blocked.Count), TextResources.Get("imageWorkspacePage.purgeSelectedAsync.confirmAsync.alternativeB"))) return;
        var result = await owner.ImageManagement.PurgeAsync(ids, CancellationToken.None); gallerySelection.Clear(); galleryActionNotice.Text = ManagementSummary(result); RefreshGallery();
    }
    private async Task ClearTrashAsync()
    {
        var assets = owner.ImageStore.ListIncludingTrash().Where(i => i.TrashedUtc.HasValue).ToList(); if (assets.Count == 0) { galleryActionNotice.Text = TextResources.Get("imageWorkspacePage.clearTrashAsync.galleryActionNotice"); return; }
        var ids = assets.Select(i => i.Id).ToArray(); var preview = await owner.ImageManagement.InspectReferencesAsync(ids, CancellationToken.None);
        if (!await ConfirmAsync(TextResources.Get("imageWorkspacePage.clearTrashAsync.confirmAsync.alternativeA"), TextResources.Format("imageWorkspacePage.clearTrashAsync.confirmAsync",assets.Count,assets.Sum(i => i.Bytes) / 1024,preview.Blocked.Count), TextResources.Get("imageWorkspacePage.clearTrashAsync.confirmAsync.alternativeA"))) return;
        var result = await owner.ImageManagement.PurgeAsync(ids, CancellationToken.None); gallerySelection.Clear(); galleryActionNotice.Text = ManagementSummary(result); RefreshGallery();
    }
    private async Task CleanupOrphansAsync()
    {
        var preview = await owner.ImageManagement.InspectOrphanReferencesAsync(CancellationToken.None);
        if (preview.Count == 0) { galleryActionNotice.Text = TextResources.Get("imageWorkspacePage.cleanupOrphansAsync.galleryActionNotice"); return; }
        var ids = preview.Candidates.Select(i => i.Id).ToArray();
        if (!await ConfirmAsync(TextResources.Get("imageWorkspacePage.cleanupOrphansAsync.confirmAsync.alternativeA"), TextResources.Format("imageWorkspacePage.cleanupOrphansAsync.confirmAsync",preview.Count,preview.TotalBytes / 1024), TextResources.Get("imageWorkspacePage.cleanupOrphansAsync.confirmAsync.alternativeB"))) return;
        var result = await owner.ImageManagement.CleanupOrphanReferencesAsync(ids, CancellationToken.None); galleryActionNotice.Text = ManagementSummary(result);
    }
    internal void ShowTasks(string id) { SelectedIndex = 2; taskCenter.SelectTask(id); }
    public void Dispose() { taskCenter.Dispose(); disposed = true; Interlocked.Increment(ref galleryRevision); exportCancellation?.Cancel(); owner.ImageStore.Changed -= StoreChanged; owner.Changed -= RefreshProviders; owner.ImageTasks.Changed-=TaskChanged; referenceBytes=null;referenceId=null;referenceMediaType=null;referencePreview.Source=null;result.Dispose(); foreach (var card in galleryCards) card.Dispose(); referenceBitmap?.Dispose();referenceBitmap=null; }
}
