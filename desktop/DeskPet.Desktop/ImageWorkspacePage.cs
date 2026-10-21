using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ImageWorkspacePage : TabControl, IDisposable
{
    protected override Type StyleKeyOverride => typeof(TabControl);
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly ComboBox providers = new() { Name = "WorkspaceImageProvider", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox prompt = Ui.Input("", "描述想要生成的图片");
    private readonly ComboBox size = ParameterChoices("WorkspaceImageSize", new[] { "1024x1024", "1536x1024", "1024x1536", "auto" });
    private readonly ComboBox quality = ParameterChoices("WorkspaceImageQuality", new[] { "auto", "low", "medium", "high", "standard", "hd" });
    private readonly TextBlock status = Ui.Text(""), referenceLabel = Ui.Text("未选择参考图"), capability = Ui.Text(""), warning = Ui.Text("");
    private readonly Button generate, cancel, referenceButton;
    private readonly Image referencePreview = new() { Height = 100, Stretch = Stretch.Uniform, IsVisible = false };
    private readonly StackPanel result = Ui.Column();
    private readonly ImageGalleryPanel gallery = new() { Name = "ImageGalleryTiles" };
    private readonly TextBlock galleryEmpty = Ui.Text("");
    private readonly Button masonryLayout, gridLayout, loadMore;
    private int galleryLimit = 40;
    private readonly TextBox search = Ui.Input("", "按提示词或模型搜索图库");
    private readonly List<GeneratedImageCard> resultCards = new(), galleryCards = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly System.Diagnostics.Stopwatch elapsed = new();
    private CancellationTokenSource? cancellation;
    private string? imageJobId;
    private byte[]? referenceBytes;
    private string? referenceMediaType;
    private string? referenceId;
    private bool missingReference;
    private Bitmap? referenceBitmap;
    private bool refreshing, disposed;
    private int galleryRefreshPending;
    private string? providerSignature;
    internal bool IsGenerating => cancellation != null;
    internal int GalleryCount => galleryCards.Count;
    internal ImageGalleryPanel GalleryPanel => gallery;
    internal TextBox Search => search;
    internal ComboBox ProviderChoices => providers;
    internal ComboBox SizeChoices => size;
    internal ComboBox QualityChoices => quality;
    internal Button ReferenceButton => referenceButton;
    internal static ComboBox ParameterChoices(string name, string[] values) => new() { Name = name, ItemsSource = new[] { "服务默认" }.Concat(values).ToArray(), SelectedIndex = 0, MinWidth = 145 };
    internal static void ConfigureParameters(ComboBox sizes, ComboBox qualities, string protocol)
    {
        sizes.ItemsSource = ImageGenerationProtocols.SizeOptions(protocol).Select(value => string.IsNullOrEmpty(value) ? "服务默认" : value).ToArray();
        qualities.ItemsSource = ImageGenerationProtocols.QualityOptions(protocol).Select(value => string.IsNullOrEmpty(value) ? "服务默认" : value).ToArray();
        sizes.SelectedIndex = qualities.SelectedIndex = 0;
    }
    internal static string Parameter(ComboBox box) => box.SelectedIndex <= 0 ? "" : box.SelectedItem as string ?? "";
    internal static void SelectParameter(ComboBox box, string? value) { if (string.IsNullOrEmpty(value)) box.SelectedIndex = 0; else { var choices = box.ItemsSource!.Cast<string>().ToList(); if (!choices.Contains(value)) { choices.Add(value); box.ItemsSource = choices; } box.SelectedItem = value; } }
    public ImageWorkspacePage(DesktopController controller, Window window)
    {
        owner = controller; host = window; Name = "ImageWorkspace"; prompt.Name = "ImagePrompt"; prompt.AcceptsReturn = true; prompt.Height = 140; prompt.MinWidth = 0; prompt.TextWrapping = TextWrapping.Wrap;
        providers.SelectionChanged += (_, _) => { if (refreshing) return; if (providers.SelectedItem is ImageGenerationProvider p) { owner.Settings.ActiveImageProviderId = p.Id; owner.Save(); ConfigureParameters(size, quality, p.Protocol); SelectParameter(size, p.Size); SelectParameter(quality, p.Quality); } UpdateCapability(); };
        generate = Ui.Button("开始生成", () => owner.Run(GenerateAsync)); generate.Name = "StartImageGeneration";
        cancel = Ui.Button("取消生成", () => { if(imageJobId != null) _ = owner.ImageTasks.CancelAsync(imageJobId); }); cancel.Name = "CancelImageGeneration"; cancel.IsEnabled = false;
        referenceButton = Ui.AsyncButton("上传参考图", UploadAsync, owner.Error); referenceButton.Name = "UploadImageReference";
        var remove = Ui.Button("移除参考图", ClearReference); remove.Name = "RemoveImageReference";
        var parameters = new WrapPanel(); parameters.Children.Add(Ui.Field("尺寸", size)); parameters.Children.Add(Ui.Field("质量", quality));
        var generation = Ui.Column(Ui.Text("图片生成", 23), Ui.Text("选择独立配置的图片服务，或上传一张参考图进行编辑。", 13), Ui.Field("图片生成服务", providers), capability,
            Ui.Button("配置图片服务", () => owner.ShowSettingsSection("image")), Ui.Field("提示词", prompt), parameters,
            Ui.Row(referenceButton, remove), referenceLabel, referencePreview, Ui.Row(generate, cancel), status, result);
        masonryLayout = Ui.Button("瀑布流", () => SelectGalleryLayout("masonry")); masonryLayout.Name = "GalleryMasonryLayout";
        gridLayout = Ui.Button("网格", () => SelectGalleryLayout("grid")); gridLayout.Name = "GalleryGridLayout";
        loadMore = Ui.Button("加载更多", () => { galleryLimit += 40; RefreshGallery(); }); loadMore.Name = "LoadMoreGalleryImages";
        var galleryHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; galleryHeader.Children.Add(Ui.Text("图库", 23)); var layouts = Ui.Row(masonryLayout, gridLayout); Grid.SetColumn(layouts, 1); galleryHeader.Children.Add(layouts);
        generation.Margin = new Thickness(16); var galleryPage = Ui.Column(galleryHeader, search, warning, galleryEmpty, gallery, loadMore); galleryPage.Margin = new Thickness(16);
        var recoveryNotice = Ui.Text("");
        var recovery = new Expander { Header = "图库恢复", Content = Ui.Column(Ui.Button("恢复图库索引", () => { try { var count = owner.ImageStore.Recover(); RefreshGallery(); recoveryNotice.Text = "图库索引已恢复，找回 " + count + " 张图片。"; } catch (Exception e) { owner.Error(e); } }), recoveryNotice) };
        galleryPage.Children.Add(recovery);
        ItemsSource = new[] { new TabItem { Header = "生成", Content = new ScrollViewer { Content = generation } }, new TabItem { Header = "图库", Content = new ScrollViewer { Content = galleryPage } } };
        SelectedIndex = 0;
        search.TextChanged += (_, _) => { galleryLimit = 40; RefreshGallery(); }; owner.ImageStore.Changed += StoreChanged; owner.Changed += RefreshProviders;
        timer.Tick += (_, _) => status.Text = "正在生成图片… " + elapsed.Elapsed.TotalSeconds.ToString("0") + " 秒";
        RefreshProviders(); RefreshGallery();
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
        foreach (var button in new[] { masonryLayout, gridLayout }) { var active = ReferenceEquals(button, mode == "grid" ? gridLayout : masonryLayout); button.Background = active ? DesktopTheme.Brush("#357FF5") : Brushes.Transparent; button.Foreground = active ? Brushes.White : DesktopTheme.Brush("#777F89"); }
    }
    private void UpdateCapability()
    {
        var selected = providers.SelectedItem as ImageGenerationProvider; var busy = IsGenerating;
        capability.Text = selected == null ? "尚未配置图片服务。请进入模型配置 → 图片生成。" : selected.SupportsEdits ? "支持生成和单张参考图编辑。" : "此服务未启用参考图编辑；可在图片生成配置中声明服务能力。";
        referenceButton.IsEnabled = !busy && selected?.SupportsEdits == true; generate.IsEnabled = !busy && selected != null; providers.IsEnabled = !busy;
        prompt.IsReadOnly = busy; size.IsEnabled = quality.IsEnabled = !busy; cancel.IsEnabled = busy;
    }
    private async Task UploadAsync()
    {
        if (providers.SelectedItem is not ImageGenerationProvider { SupportsEdits: true }) throw new InvalidOperationException("此服务未启用参考图编辑。");
        var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择单张参考图", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("图片") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } } } });
        var file = files.FirstOrDefault(); if (file == null || disposed || IsGenerating) return;
        await using var input = await file.OpenReadAsync(); using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer)) != 0) { if (output.Length + count > 50L * 1024 * 1024) throw new InvalidOperationException("参考图不能超过 50 MB。"); await output.WriteAsync(buffer.AsMemory(0, count)); }
        SetReference(output.ToArray(), file.Name);
    }
    internal void SetReference(byte[] bytes, string filename)
    {
        if (IsGenerating) return;
        if (bytes.Length > 50L * 1024 * 1024) throw new InvalidOperationException("参考图不能超过 50 MB。");
        var media = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 ? "image/png" : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg" : bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? "image/webp" : throw new InvalidOperationException("参考图仅支持 PNG、JPEG 和 WebP。");
        using var stream = new MemoryStream(bytes); var bitmap = Bitmap.DecodeToWidth(stream, 240); ClearReference(); referenceBitmap = bitmap; referenceBytes = bytes.ToArray(); referenceMediaType = media; referencePreview.Source = bitmap; referencePreview.IsVisible = true; referenceLabel.Text = filename;
    }
    private void ClearReference() { if (IsGenerating) return; referencePreview.Source = null; referenceBitmap?.Dispose(); referenceBitmap = null; referenceBytes = null; referenceMediaType = null; referenceId = null; missingReference = false; referencePreview.IsVisible = false; referenceLabel.Text = "未选择参考图"; }
    internal void Prepare(GeneratedImage image)
    {
        if (IsGenerating) { status.Text = "请先等待或取消当前生成。"; return; } SelectedIndex = 0; prompt.Text = image.Prompt;
        var selected = providers.ItemsSource?.Cast<ImageGenerationProvider>().FirstOrDefault(p => p.Id == image.ProviderId);
        if (selected != null) providers.SelectedItem = selected; SelectParameter(size, image.Size); SelectParameter(quality, image.Quality); ClearReference();
        status.Text = "已填入原提示词与参数；点击开始生成创建新图片。";
        if (!string.IsNullOrEmpty(image.ReferenceImageId))
        {
            try { SetReference(owner.ImageStore.Read(image.ReferenceImageId), "原参考图"); referenceId = image.ReferenceImageId; }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { missingReference = true; referenceLabel.Text = "原参考图不可用，请重新上传或主动移除参考图。"; status.Text = referenceLabel.Text; }
        }
    }
    internal async Task GenerateAsync()
    {
        if (IsGenerating) return;
        var provider = (providers.SelectedItem as ImageGenerationProvider)?.Copy() ?? throw new InvalidOperationException("请先配置图片生成服务。");
        var text = prompt.Text?.Trim(); if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("请输入图片提示词。");
        if (missingReference) throw new InvalidOperationException("原参考图不可用，请重新上传或移除参考图后再生成。");
        if (referenceBytes != null && !provider.SupportsEdits) throw new InvalidOperationException("所选服务不支持参考图编辑。请更换服务或移除参考图。");
        using var cancel = new CancellationTokenSource(); cancellation = cancel; elapsed.Restart(); timer.Start(); UpdateCapability();
        try
        {
            var handle = await owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest { Provider = provider, Prompt = text, Size = Parameter(size), Quality = Parameter(quality), ReferenceImageBytes = referenceBytes?.ToArray(), ReferenceMediaType = referenceMediaType, ReferenceImageId = referenceId, Persistent = owner.Settings.SaveChatHistory, TaskId = Guid.NewGuid().ToString("N") },new ImageTaskOrigin{Source=ImageGenerationSource.Workspace}) { BeforeStart = id => { imageJobId=id; return Task.FromResult(0); } },CancellationToken.None);var image=await handle.Completion;
            if (disposed) return; foreach (var card in resultCards) card.Dispose(); resultCards.Clear(); result.Children.Clear(); var view = new GeneratedImageCard(owner, host, image, Prepare); resultCards.Add(view); result.Children.Add(view); status.Text = "生成完成 · " + elapsed.Elapsed.TotalSeconds.ToString("0.0") + " 秒";
        }
        catch (OperationCanceledException) { status.Text = "已取消生成。"; }
        catch (Exception e) { status.Text = "生成失败：" + e.Message; }
        finally { timer.Stop(); elapsed.Stop(); cancellation = null; if (!disposed) UpdateCapability(); }
    }
    internal void RefreshGallery()
    {
        foreach (var card in galleryCards) card.Dispose(); galleryCards.Clear(); gallery.Children.Clear(); warning.Text = owner.ImageStore.Warning;
        var query = search.Text?.Trim() ?? ""; var found = owner.ImageStore.List().Where(i => query.Length == 0 || (i.Prompt ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) || (i.Model ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).OrderByDescending(i => i.CreatedUtc).ToList();
        galleryEmpty.Text = query.Length == 0 ? "还没有生成图片。" : "没有匹配的图片。"; galleryEmpty.IsVisible = found.Count == 0;
        var visible = found.Take(galleryLimit).ToList();
        foreach (var item in visible) { var card = new GeneratedImageCard(owner, host, item, Prepare, owner.Settings.ImageGalleryLayout); galleryCards.Add(card); gallery.Children.Add(card); }
        gallery.SetImages(visible); loadMore.IsVisible = found.Count > galleryLimit; RefreshGalleryLayout();
    }
    public void Dispose() { disposed = true;  timer.Stop(); owner.ImageStore.Changed -= StoreChanged; owner.Changed -= RefreshProviders; foreach (var card in resultCards.Concat(galleryCards)) card.Dispose(); referenceBitmap?.Dispose(); }
}
