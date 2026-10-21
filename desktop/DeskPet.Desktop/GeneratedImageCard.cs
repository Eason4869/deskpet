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
    internal bool IsGalleryTile { get; }
    internal void SetGalleryLayout(string layout) { if (tileImage != null) tileImage.Stretch = layout == "grid" ? Stretch.UniformToFill : Stretch.Uniform; }
    public GeneratedImageCard(DesktopController controller, Window window, GeneratedImage image, Action<GeneratedImage>? repeat = null, string? galleryLayout = null, Action<string>? choose = null, Action<string>? favorite = null, bool selected = false, bool selecting = false)
    {
        owner = controller; host = window; metadata = image.Copy(); regenerate = repeat; Name = "GeneratedImageCard";
        IsGalleryTile = galleryLayout != null;
        if (IsGalleryTile) { BuildTile(galleryLayout!, choose, favorite, selected, selecting); return; }
        Padding = new Thickness(12); Margin = new Thickness(0, 0, 8, 8); CornerRadius = new CornerRadius(12); BorderBrush = DesktopTheme.Brush("#E0E4E9"); BorderThickness = new Thickness(1); MaxWidth = 520;
        var body = Ui.Column(); Child = body;
        try
        {
            using var stream = new MemoryStream(owner.ImageStore.Read(image.Id)); thumbnail = Bitmap.DecodeToWidth(stream, 480);
            var preview = Ui.Button("", ShowPreview); preview.Name = "GeneratedImagePreview"; preview.Padding = new Thickness(0); preview.HorizontalAlignment = HorizontalAlignment.Stretch;
            preview.Content = new Image { Source = thumbnail, Height = 220, Stretch = Stretch.Uniform }; ToolTip.SetTip(preview, "预览图片"); body.Children.Add(preview);
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { body.Children.Add(Ui.Text("图片资产缺失或损坏。可在原会话中重新生成。")); }
        body.Children.Add(new TextBlock { Text = metadata.Prompt, TextWrapping = TextWrapping.Wrap, MaxHeight = 58, TextTrimming = TextTrimming.CharacterEllipsis });
        body.Children.Add(Ui.Text(metadata.Model + " · " + metadata.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm") + (metadata.Temporary ? " · 本次运行临时保存" : ""), 11));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { Ui.AsyncButton("另存为", SaveAsync, owner.Error), Ui.AsyncButton("复制图片", CopyAsync, owner.Error), Ui.Button("详情", Details), Ui.AsyncButton("打开目录", OpenDirectoryAsync, owner.Error), Ui.Button("再生成", Regenerate) }) { button.Margin = new Thickness(0, 0, 4, 4); actions.Children.Add(button); }
        body.Children.Add(actions);
    }
    private void BuildTile(string layout, Action<string>? choose, Action<string>? favorite, bool selected, bool selecting)
    {
        Name = "GeneratedImageTile"; CornerRadius = new CornerRadius(14); ClipToBounds = true; Background = DesktopTheme.Brush("#F2F4F7");
        var surface = new Grid(); Child = surface;
        var open = Ui.Button("", () => OpenPreview(true)); open.Name = "GalleryImagePreview"; open.Padding = new Thickness(0); open.BorderThickness = new Thickness(0); open.CornerRadius = CornerRadius; open.Background = Brushes.Transparent; open.HorizontalAlignment = HorizontalAlignment.Stretch; open.VerticalAlignment = VerticalAlignment.Stretch; open.HorizontalContentAlignment = HorizontalAlignment.Stretch; open.VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(open, "预览图片：" + metadata.Prompt);
        tileImage = new Image { Name = "GalleryTileImage" }; SetGalleryLayout(layout); open.Content = tileImage; _ = LoadGalleryThumbnailAsync();
        ToolTip.SetTip(this, metadata.Prompt + "\n" + metadata.Model + " · " + metadata.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")); surface.Children.Add(open);
        var menu = new ContextMenu();
        MenuItem Action(string title, Action callback) { var item = new MenuItem { Header = title }; item.Click += (_, _) => callback(); return item; }
        menu.ItemsSource = new object[] { Action("查看图片详情", () => OpenPreview(true)), Action("另存为", () => owner.Run(SaveAsync)), Action("复制图片", () => owner.Run(CopyAsync)), Action("打开目录", () => owner.Run(OpenDirectoryAsync)), Action("详情", Details), Action("再生成", Regenerate) };
        ContextMenu = menu;
        var more = Ui.Button("⋯", () => menu.Open(this)); more.Name = "GalleryTileActions"; more.Width = more.Height = 32; more.FontSize = 22; more.Padding = new Thickness(0); more.CornerRadius = new CornerRadius(16); more.Background = new SolidColorBrush(Color.Parse("#B0171B22")); more.Foreground = Brushes.White; more.HorizontalAlignment = HorizontalAlignment.Right; more.VerticalAlignment = VerticalAlignment.Bottom; more.Margin = new Thickness(8); more.Opacity = 0;
        AutomationProperties.SetName(more, "图片操作"); ToolTip.SetTip(more, "图片操作"); surface.Children.Add(more);
        var pick = Ui.Button(selected ? "已" : "选", () => choose?.Invoke(metadata.Id)); pick.Name = "GalleryTileSelect"; pick.Width = pick.Height = 32; pick.Padding = new Thickness(0); pick.HorizontalAlignment = HorizontalAlignment.Left; pick.VerticalAlignment = VerticalAlignment.Top; pick.Margin = new Thickness(8); AutomationProperties.SetName(pick, "选择图片"); surface.Children.Add(pick);
        var star = Ui.Button(metadata.IsFavorite ? "藏" : "收", () => favorite?.Invoke(metadata.Id)); star.Name = "GalleryTileFavorite"; star.Width = star.Height = 32; star.Padding = new Thickness(0); star.HorizontalAlignment = HorizontalAlignment.Right; star.VerticalAlignment = VerticalAlignment.Top; star.Margin = new Thickness(8); AutomationProperties.SetName(star, "收藏图片"); surface.Children.Add(star);
        pick.Opacity = star.Opacity = selecting || selected ? 1 : 0;
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty || e.Property == IsKeyboardFocusWithinProperty) { var visible = IsPointerOver || IsKeyboardFocusWithin; more.Opacity = visible ? 1 : 0; pick.Opacity = star.Opacity = visible || selecting || selected ? 1 : 0; } };
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
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed) ToolTip.SetTip(this, "图片缺失或损坏"); }); }
        finally { if (entered) GalleryDecodeGate.Release(); }
    }
    private void Regenerate()
    {
        if (regenerate != null) regenerate(metadata.Copy()); else owner.ShowImages(metadata.Copy());
    }
    internal async Task CopyAsync()
    {
        if (host.Clipboard == null) throw new InvalidOperationException("系统剪贴板不可用。");
        var transfer = new ImageClipboardData(owner.ImageStore.Read(metadata.Id));
        try { await host.Clipboard.SetDataAsync(transfer); }
        catch { transfer.Dispose(); throw; }
        await host.Clipboard.FlushAsync();
    }
    private async Task SaveAsync()
    {
        var extension = metadata.MediaType switch { "image/jpeg" => "jpg", "image/webp" => "webp", "image/gif" => "gif", _ => "png" };
        var file = await host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "另存生成图片", SuggestedFileName = "generated-" + metadata.Id + "." + extension, DefaultExtension = extension, FileTypeChoices = new[] { new FilePickerFileType("图片") { Patterns = new[] { "*." + extension } } } });
        if (file == null) return;
        var bytes = owner.ImageStore.Read(metadata.Id); await using var output = await file.OpenWriteAsync(); output.SetLength(0); await output.WriteAsync(bytes);
    }
    private async Task OpenDirectoryAsync()
    {
        var path = owner.ImageStore.PathFor(metadata.Id); await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(path)!));
    }
    private void Details()
    {
        var info = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Text = metadata.Prompt + "\n\n模型：" + metadata.Model + "\n服务：" + metadata.ProviderId + "\n尺寸：" + metadata.Width + " × " + metadata.Height + "（" + (string.IsNullOrEmpty(metadata.Size) ? "服务默认" : metadata.Size) + "）\n质量：" + (string.IsNullOrEmpty(metadata.Quality) ? "服务默认" : metadata.Quality) + "\n生成时间：" + metadata.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\n资产：" + metadata.Id + "\n会话：" + metadata.SessionId + "\n任务：" + metadata.TaskId + "\n" + metadata.Bytes + " 字节" };
        var dialog = Ui.Window("图片详情", new ScrollViewer { Content = info }, 520, 490); dialog.Show(host);
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
            foreach (var button in new[] { Ui.Button("−", () => Zoom(zoom / 1.25)), Ui.Button("+", () => Zoom(zoom * 1.25)), Ui.Button("适应窗口", Fit), Ui.AsyncButton("另存为", SaveAsync, owner.Error), Ui.AsyncButton("复制图片", CopyAsync, owner.Error), Ui.AsyncButton("打开目录", OpenDirectoryAsync, owner.Error), Ui.Button("详情", Details), Ui.Button("再生成", Regenerate) }) toolbar.Children.Add(button);
            DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
            if (showDetails)
            {
                var prompt = new SelectableTextBlock { Name = "GalleryDetailPrompt", Text = metadata.Prompt, TextWrapping = TextWrapping.Wrap }; Ui.ReadOnlyMenu(prompt);
                var details = new Border { Name = "GalleryImageDetails", Padding = new Thickness(12), Child = Ui.Column(Ui.Text(metadata.Model + " · " + metadata.Width + " × " + metadata.Height + " · " + metadata.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 13), new ScrollViewer { Content = prompt, MaxHeight = 145 }) };
                DockPanel.SetDock(details, Dock.Bottom); panel.Children.Add(details);
            }
            panel.Children.Add(viewport);
            var preview = Ui.Window(showDetails ? "DeskPet · 图片详情（滚轮缩放，拖动平移）" : "DeskPet · 图片预览（滚轮缩放，拖动平移）", panel, 860, 700); preview.Name = "GeneratedImagePreviewWindow"; preview.Opened += (_, _) => Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded); preview.Closed += (_, _) => bitmap.Dispose(); preview.Show(host);
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
