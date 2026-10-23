using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Automation;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class GeneratedImageCard : Border, IDisposable
{
    private static readonly SemaphoreSlim GalleryDecodeGate = new(3);
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly GeneratedImage metadata;
    private readonly Action<GeneratedImage>? regenerate;
    private Bitmap? thumbnail;
    private Image? tileImage;
    private bool disposed;
    private Button? selectionOverlay, favoriteOverlay;
    private bool selectionActive, selectedAsset;
    internal string GalleryAssetId => metadata.Id;
    internal void UpdateSelection(bool selected, bool selecting) { selectedAsset=selected; selectionActive=selecting; if(selectionOverlay==null||favoriteOverlay==null)return;selectionOverlay.Content="√";selectionOverlay.Background=selected?DesktopTheme.Brush("#357FF5"):new SolidColorBrush(Color.Parse("#B0171B22"));AutomationProperties.SetName(selectionOverlay,selected?TextResources.Get("generatedImageCard.updateSelection.automationPropertiesSetName"):TextResources.Get("generatedImageCard.updateSelection.automationPropertiesSetName.alternativeA"));ToolTip.SetTip(selectionOverlay,selected?TextResources.Get("generatedImageCard.updateSelection.toolTipSetTip"):TextResources.Get("generatedImageCard.updateSelection.toolTipSetTip.alternativeA"));selectionOverlay.Opacity=favoriteOverlay.Opacity=IsPointerOver||IsKeyboardFocusWithin||selected||selecting?1:0; }
    internal bool IsGalleryTile { get; }
    internal void SetGalleryLayout(string layout) { if (tileImage != null) tileImage.Stretch = layout == "grid" ? Stretch.UniformToFill : Stretch.Uniform; }
    public GeneratedImageCard(DesktopController controller, Window window, GeneratedImage image, Action<GeneratedImage>? repeat = null, string? galleryLayout = null, Action<string>? choose = null, Action<string>? favorite = null, bool selected = false, bool selecting = false, bool workspacePreview = false)
    {
        owner = controller; host = window; metadata = image.Copy(); regenerate = repeat; Name = "GeneratedImageCard";
        IsGalleryTile = galleryLayout != null;
        if (workspacePreview) { BuildWorkspacePreview(); return; }
        if (IsGalleryTile) { BuildTile(galleryLayout!, choose, favorite, selected, selecting); return; }
        Padding = new Thickness(12); Margin = new Thickness(0, 0, 8, 8); CornerRadius = new CornerRadius(12); BorderBrush = DesktopTheme.Brush("#E0E4E9"); BorderThickness = new Thickness(1); MaxWidth = 520;
        var body = Ui.Column(); Child = body;
        try
        {
            using var stream = new MemoryStream(owner.ImageStore.Read(image.Id)); thumbnail = Bitmap.DecodeToWidth(stream, 480);
            var preview = Ui.Button("", ShowPreview); preview.Name = "GeneratedImagePreview"; preview.Padding = new Thickness(0); preview.HorizontalAlignment = HorizontalAlignment.Stretch;
            preview.Content = new Image { Source = thumbnail, Height = 220, Stretch = Stretch.Uniform }; ToolTip.SetTip(preview, TextResources.Get("generatedImageCard.generatedImageCard.toolTipSetTip")); body.Children.Add(preview);
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { body.Children.Add(Ui.Text(TextResources.Get("generatedImageCard.generatedImageCard.text"))); }
        body.Children.Add(new TextBlock { Text = metadata.Prompt, TextWrapping = TextWrapping.Wrap, MaxHeight = 58, TextTrimming = TextTrimming.CharacterEllipsis });
        body.Children.Add(Ui.Text(metadata.Model + " · " + metadata.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm") + (metadata.Temporary ? TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeA") : ""), 11));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { Ui.AsyncButton(TextResources.Get("generatedImageCard.generatedImageCard.asyncButton"), SaveAsync, owner.Error), Ui.AsyncButton(TextResources.Get("generatedImageCard.generatedImageCard.asyncButton.alternativeA"), CopyAsync, owner.Error), Ui.Button(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeB"), Details), Ui.AsyncButton(TextResources.Get("generatedImageCard.generatedImageCard.asyncButton.alternativeB"), OpenDirectoryAsync, owner.Error), Ui.Button(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeC"), Regenerate) }) { button.Margin = new Thickness(0, 0, 4, 4); actions.Children.Add(button); }
        body.Children.Add(actions);
    }
    private void BuildWorkspacePreview()
    {
        Name = "WorkspaceGeneratedImage"; CornerRadius = new CornerRadius(14); ClipToBounds = true;
        using var stream = new MemoryStream(owner.ImageStore.Read(metadata.Id)); thumbnail = Bitmap.DecodeToWidth(stream, Math.Min(1400, Math.Max(1, metadata.Width)));
        var surface = new Grid(); Child = surface;
        var preview = Ui.Button("", ShowPreview); preview.Name = "WorkspaceGeneratedImagePreview"; preview.Background = Brushes.Transparent; preview.BorderThickness = new Thickness(0); preview.Padding = new Thickness(0); preview.HorizontalAlignment = HorizontalAlignment.Stretch;
        preview.VerticalAlignment = VerticalAlignment.Stretch; preview.HorizontalContentAlignment = HorizontalAlignment.Stretch; preview.VerticalContentAlignment = VerticalAlignment.Stretch;
        preview.Content = new Image { Source = thumbnail, Stretch = Stretch.Uniform }; surface.Children.Add(preview); AutomationProperties.SetName(preview, TextResources.Get("generatedImageCard.buildWorkspacePreview.automationPropertiesSetName"));
        var menu = new ContextMenu();
        MenuItem Action(string title, Action callback) { var item = new MenuItem { Header = title }; item.Click += (_, _) => callback(); return item; }
        menu.ItemsSource = new object[] { Action(TextResources.Get("generatedImageCard.buildWorkspacePreview.action"), () => owner.Run(SaveAsync)), Action(TextResources.Get("generatedImageCard.buildWorkspacePreview.action.alternativeA"), () => owner.Run(CopyAsync)), Action(TextResources.Get("generatedImageCard.buildWorkspacePreview.action.alternativeB"), () => owner.Run(OpenDirectoryAsync)), Action(TextResources.Get("generatedImageCard.buildWorkspacePreview.action.alternativeC"), Details), Action(TextResources.Get("generatedImageCard.buildWorkspacePreview.action.alternativeD"), Regenerate) };
        ContextMenu = menu;
        var more = Ui.Button("⋯", () => menu.Open(this)); more.HorizontalAlignment = HorizontalAlignment.Right; more.VerticalAlignment = VerticalAlignment.Top; more.Margin = new Thickness(8); more.CornerRadius = new CornerRadius(16); more.Width = more.Height = 32; more.Padding = new Thickness(0); more.FontSize = 22; ToolTip.SetTip(more, TextResources.Get("generatedImageCard.buildWorkspacePreview.toolTipSetTip")); AutomationProperties.SetName(more, TextResources.Get("generatedImageCard.buildWorkspacePreview.automationPropertiesSetName.alternativeA")); surface.Children.Add(more);
    }
    private void BuildTile(string layout, Action<string>? choose, Action<string>? favorite, bool selected, bool selecting)
    {
        Name = "GeneratedImageTile"; CornerRadius = new CornerRadius(14); ClipToBounds = true; Background = DesktopTheme.Brush("#F2F4F7");
        var surface = new Grid(); Child = surface;
        var open = Ui.Button("", () => OpenPreview(true)); open.Name = "GalleryImagePreview"; open.Padding = new Thickness(0); open.BorderThickness = new Thickness(0); open.CornerRadius = CornerRadius; open.Background = Brushes.Transparent; open.HorizontalAlignment = HorizontalAlignment.Stretch; open.VerticalAlignment = VerticalAlignment.Stretch; open.HorizontalContentAlignment = HorizontalAlignment.Stretch; open.VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(open, TextResources.Format("generatedImageCard.buildTile.automationPropertiesSetName",metadata.Prompt));
        tileImage = new Image { Name = "GalleryTileImage" }; SetGalleryLayout(layout); open.Content = tileImage; _ = LoadGalleryThumbnailAsync();
        ToolTip.SetTip(this, metadata.Prompt + "\n" + metadata.Model + " · " + metadata.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")); surface.Children.Add(open);
        var menu = new ContextMenu();
        MenuItem Action(string title, Action callback) { var item = new MenuItem { Header = title }; item.Click += (_, _) => callback(); return item; }
        menu.ItemsSource = new object[] { Action(TextResources.Get("generatedImageCard.buildTile.action"), () => OpenPreview(true)), Action(TextResources.Get("generatedImageCard.buildTile.action.alternativeA"), () => owner.Run(SaveAsync)), Action(TextResources.Get("generatedImageCard.buildTile.action.alternativeB"), () => owner.Run(CopyAsync)), Action(TextResources.Get("generatedImageCard.buildTile.action.alternativeC"), () => owner.Run(OpenDirectoryAsync)), Action(TextResources.Get("generatedImageCard.buildTile.action.alternativeD"), Details), Action(TextResources.Get("generatedImageCard.buildTile.action.alternativeE"), Regenerate) };
        ContextMenu = menu;
        var more = Ui.Button("⋯", () => menu.Open(this)); more.Name = "GalleryTileActions"; more.Width = more.Height = 32; more.FontSize = 22; more.Padding = new Thickness(0); more.CornerRadius = new CornerRadius(16); more.Background = new SolidColorBrush(Color.Parse("#B0171B22")); more.Foreground = Brushes.White; more.HorizontalAlignment = HorizontalAlignment.Right; more.VerticalAlignment = VerticalAlignment.Bottom; more.Margin = new Thickness(8); more.Opacity = 0;
        AutomationProperties.SetName(more, TextResources.Get("generatedImageCard.buildTile.automationPropertiesSetName.alternativeA")); ToolTip.SetTip(more, TextResources.Get("generatedImageCard.buildTile.toolTipSetTip")); surface.Children.Add(more);
        var pick = Ui.Button("√", () => choose?.Invoke(metadata.Id)); pick.Name = "GalleryTileSelect"; pick.Width = pick.Height = 32; pick.Padding = new Thickness(0); pick.HorizontalAlignment = HorizontalAlignment.Left; pick.VerticalAlignment = VerticalAlignment.Top; pick.Margin = new Thickness(8); pick.CornerRadius = new CornerRadius(16); pick.Foreground = Brushes.White; surface.Children.Add(pick);
        var star = Ui.Button("♥", () => favorite?.Invoke(metadata.Id)); star.Name = "GalleryTileFavorite"; star.Width = star.Height = 32; star.Padding = new Thickness(0); star.HorizontalAlignment = HorizontalAlignment.Right; star.VerticalAlignment = VerticalAlignment.Top; star.Margin = new Thickness(8); star.CornerRadius = new CornerRadius(16); star.Background = new SolidColorBrush(Color.Parse("#B0171B22")); star.Foreground = metadata.IsFavorite ? DesktopTheme.Brush("#F46E86") : Brushes.White; AutomationProperties.SetName(star, metadata.IsFavorite ? TextResources.Get("generatedImageCard.buildTile.automationPropertiesSetName.alternativeB") : TextResources.Get("generatedImageCard.buildTile.automationPropertiesSetName.alternativeC")); ToolTip.SetTip(star, metadata.IsFavorite ? TextResources.Get("generatedImageCard.buildTile.toolTipSetTip.alternativeA") : TextResources.Get("generatedImageCard.buildTile.toolTipSetTip.alternativeB")); surface.Children.Add(star);
        selectionOverlay=pick;favoriteOverlay=star;selectionActive=selecting;selectedAsset=selected;
        UpdateSelection(selected, selecting);
        pick.Opacity = star.Opacity = selecting || selected ? 1 : 0;
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty || e.Property == IsKeyboardFocusWithinProperty) { var visible = IsPointerOver || IsKeyboardFocusWithin; more.Opacity = visible ? 1 : 0; pick.Opacity = star.Opacity = visible || selectionActive || selectedAsset ? 1 : 0; } };
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(Bounds.Size), 14, 14);
    }
    private async Task LoadGalleryThumbnailAsync()
    {
        bool entered = false;
        try
        {
            await GalleryDecodeGate.WaitAsync(); entered = true; if (disposed) return;
            var image = await Task.Run(() => { using (owner.ImageUsage.Acquire(metadata.Id, "gallery-thumbnail")) { using var data = new MemoryStream(owner.ImageStore.Read(metadata.Id)); var width = Math.Max(1, (int)(Math.Max(1, metadata.Width) * Math.Min(1, 480d / Math.Max(1, Math.Max(metadata.Width, metadata.Height))))); return Bitmap.DecodeToWidth(data, width); } });
            await Dispatcher.UIThread.InvokeAsync(() => { if (disposed) image.Dispose(); else { thumbnail?.Dispose(); thumbnail = image; if (tileImage != null) tileImage.Source = image; } });
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed) ToolTip.SetTip(this, TextResources.Get("generatedImageCard.loadGalleryThumbnailAsync.toolTipSetTip")); }); }
        finally { if (entered) GalleryDecodeGate.Release(); }
    }
    private void Regenerate()
    {
        if (regenerate != null) regenerate(metadata.Copy()); else owner.ShowImages(metadata.Copy());
    }
    internal async Task CopyAsync()
    {
        if (host.Clipboard == null) throw new InvalidOperationException(TextResources.Get("generatedImageCard.copyAsync.text"));
        var transfer = new ImageClipboardData(owner.ImageStore.Read(metadata.Id));
        try { await host.Clipboard.SetDataAsync(transfer); }
        catch { transfer.Dispose(); throw; }
        await host.Clipboard.FlushAsync();
    }
    private async Task SaveAsync()
    {
        var extension = metadata.MediaType switch { "image/jpeg" => "jpg", "image/webp" => "webp", "image/gif" => "gif", _ => "png" };
        var file = await host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = TextResources.Get("generatedImageCard.saveAsync.title"), SuggestedFileName = "generated-" + metadata.Id + "." + extension, DefaultExtension = extension, FileTypeChoices = new[] { new FilePickerFileType(TextResources.Get("generatedImageCard.saveAsync.fileTypeChoicesFilePickerFileType")) { Patterns = new[] { "*." + extension } } } });
        if (file == null) return;
        var bytes = owner.ImageStore.Read(metadata.Id); await using var output = await file.OpenWriteAsync(); output.SetLength(0); await output.WriteAsync(bytes);
    }
    private async Task OpenDirectoryAsync()
    {
        var path = owner.ImageStore.PathFor(metadata.Id); await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(path)!));
    }
    private void Details()
    {
        var info = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Text = metadata.Prompt + TextResources.Get("generatedImageCard.details.widthMetadataHeight") + metadata.Model + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeA") + metadata.ProviderId + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeB") + metadata.Width + " × " + metadata.Height + "（" + (string.IsNullOrEmpty(metadata.Size) ? TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeC") : metadata.Size) + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeD") + (string.IsNullOrEmpty(metadata.Quality) ? TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeC") : metadata.Quality) + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeE") + metadata.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeF") + metadata.Id + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeG") + metadata.SessionId + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeH") + metadata.TaskId + "\n" + metadata.Bytes + TextResources.Get("generatedImageCard.details.widthMetadataHeight.alternativeI") };
        var dialog = Ui.Window(TextResources.Get("generatedImageCard.details.window"), new ScrollViewer { Content = info }, 520, 490); dialog.Show(host);
    }
    internal void ShowPreview() => OpenPreview(false);
    private void OpenPreview(bool showDetails)
    {
        try
        {
            using var stream = new MemoryStream(owner.ImageStore.Read(metadata.Id)); var bitmap = new Bitmap(stream);
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, Width = bitmap.PixelSize.Width, Height = bitmap.PixelSize.Height };
            var scale = new ScaleTransform(1, 1); var move = new TranslateTransform(); var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(move); image.RenderTransform = transforms; image.RenderTransformOrigin = RelativePoint.Center;
            var viewport = new Border { Name = "ImagePreviewViewport", Background = DesktopTheme.Brush("#F2F4F7"), ClipToBounds = true, Child = image }; double zoom = 1; Point? drag = null; Vector start = default; bool fitted = false;
            void Zoom(double amount) { zoom = Math.Clamp(amount, .05, 12); scale.ScaleX = scale.ScaleY = zoom; }
            void Fit() { if (viewport.Bounds.Width < 1 || viewport.Bounds.Height < 1) return; image.Width = Math.Max(1, viewport.Bounds.Width - 24); image.Height = Math.Max(1, viewport.Bounds.Height - 24); Zoom(1); move.X = move.Y = 0; fitted = true; }
            viewport.SizeChanged += (_, _) => { if (!fitted) Fit(); };
            viewport.PointerWheelChanged += (_, e) => { Zoom(zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15)); e.Handled = true; };
            viewport.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(viewport).Properties.IsLeftButtonPressed) return; drag = e.GetPosition(viewport); start = new Vector(move.X, move.Y); e.Pointer.Capture(viewport); };
            viewport.PointerMoved += (_, e) => { if (drag is not Point origin) return; var delta = e.GetPosition(viewport) - origin; move.X = start.X + delta.X; move.Y = start.Y + delta.Y; };
            viewport.PointerReleased += (_, e) => { drag = null; e.Pointer.Capture(null); };
            var panel = new DockPanel(); var toolbar = new WrapPanel();
            foreach (var button in new[] { Ui.Button("−", () => Zoom(zoom / 1.25)), Ui.Button("+", () => Zoom(zoom * 1.25)), Ui.Button(TextResources.Get("generatedImageCard.openPreview.text"), Fit), Ui.AsyncButton(TextResources.Get("generatedImageCard.openPreview.asyncButton"), SaveAsync, owner.Error), Ui.AsyncButton(TextResources.Get("generatedImageCard.openPreview.asyncButton.alternativeA"), CopyAsync, owner.Error), Ui.AsyncButton(TextResources.Get("generatedImageCard.openPreview.asyncButton.alternativeB"), OpenDirectoryAsync, owner.Error), Ui.Button(TextResources.Get("generatedImageCard.openPreview.text.alternativeA"), Details), Ui.Button(TextResources.Get("generatedImageCard.openPreview.text.alternativeB"), Regenerate) }) toolbar.Children.Add(button);
            DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
            if (showDetails)
            {
                var prompt = new SelectableTextBlock { Name = "GalleryDetailPrompt", Text = metadata.Prompt, TextWrapping = TextWrapping.Wrap }; Ui.ReadOnlyMenu(prompt);
                var details = new Border { Name = "GalleryImageDetails", Padding = new Thickness(12), Child = Ui.Column(Ui.Text(metadata.Model + " · " + metadata.Width + " × " + metadata.Height + " · " + metadata.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 13), new ScrollViewer { Content = prompt, MaxHeight = 145 }) };
                DockPanel.SetDock(details, Dock.Bottom); panel.Children.Add(details);
            }
            panel.Children.Add(viewport);
            var preview = Ui.Window(showDetails ? TextResources.Get("generatedImageCard.openPreview.window") : TextResources.Get("generatedImageCard.openPreview.window.alternativeA"), panel, 860, 700); preview.Name = "GeneratedImagePreviewWindow"; preview.Opened += (_, _) => Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded); preview.Closed += (_, _) => bitmap.Dispose(); preview.Show(host);
        }
        catch (Exception e) { owner.Error(e); }
    }
    public void Dispose() { disposed = true; if (tileImage != null) tileImage.Source = null; thumbnail?.Dispose(); thumbnail = null; }

    // Clipboard data is requested lazily by some platforms. Its bitmap must survive
    // closing the card/preview; the clipboard owns and eventually disposes this object.
    private sealed class ImageClipboardData : IAsyncDataTransfer, IAsyncDataTransferItem
    {
        private readonly Bitmap bitmap;
        internal ImageClipboardData(byte[] bytes) { using var stream = new MemoryStream(bytes); bitmap = new Bitmap(stream); }
        public IReadOnlyList<DataFormat> Formats => new[] { DataFormat.Bitmap };
        public IReadOnlyList<IAsyncDataTransferItem> Items => new[] { this };
        public Task<object?> TryGetRawAsync(DataFormat format) => Task.FromResult<object?>(format == DataFormat.Bitmap ? bitmap : null);
        public void Dispose() => bitmap.Dispose();
    }
}
