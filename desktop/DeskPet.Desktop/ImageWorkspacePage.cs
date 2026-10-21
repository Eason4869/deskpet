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
    private readonly ImageTaskCenterPage taskCenter;
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
    private readonly TextBlock galleryCount = Ui.Text("");
    private readonly TextBlock galleryActionNotice = Ui.Text("");
    private readonly ImageGallerySelection gallerySelection = new();
    private readonly WrapPanel galleryActions = new() { Orientation = Orientation.Horizontal, IsVisible = false };
    private readonly Button multiSelect, trashAction, restoreAction, purgeAction, cancelExport, clearTrash;
    private bool selecting;
    private CancellationTokenSource? exportCancellation;
    private readonly CheckBox favoritesOnly = new() { Content = "只看收藏" }, trashOnly = new() { Content = "回收站" };
    private readonly ComboBox providerFilter = new() { MinWidth = 110 }, modelFilter = new() { MinWidth = 110 }, sourceFilter = new() { MinWidth = 110 }, sortFilter = new() { MinWidth = 110 };
    private readonly TextBox fromDate = Ui.Input("", "起始 YYYY-MM-DD"), toDate = Ui.Input("", "截止 YYYY-MM-DD");
    private IList<GeneratedImage> loadedGallery = new List<GeneratedImage>();
    private int galleryRevision;
    private bool updatingFilters;
    private readonly Button masonryLayout, gridLayout, loadMore;
    private int galleryLimit = 40;
    private readonly TextBox search = Ui.Input("", "按提示词或模型搜索图库");
    private readonly List<GeneratedImageCard> resultCards = new(), galleryCards = new();


    private bool submitting;
    private readonly TabItem taskTab = new() { Header = "任务" };
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
        providerFilter.ItemsSource = new[] { "全部服务" }; providerFilter.SelectedIndex = 0; modelFilter.ItemsSource = new[] { "全部模型" }; modelFilter.SelectedIndex = 0;
        sourceFilter.ItemsSource = new[] { "全部来源", "图像页", "对话", "设置测试", "未知" }; sourceFilter.SelectedIndex = 0; sortFilter.ItemsSource = new[] { "最新优先", "最早优先" }; sortFilter.SelectedIndex = 0;
        var filters = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var control in new Control[] { favoritesOnly, trashOnly, providerFilter, modelFilter, sourceFilter, sortFilter, fromDate, toDate }) { control.Margin = new Thickness(0, 0, 8, 6); filters.Children.Add(control); }
        multiSelect = Ui.Button("多选", () => { selecting = !selecting; if (!selecting) gallerySelection.Clear(); RefreshGallery(); }); multiSelect.Name = "GalleryMultiSelect";
        var bulk = galleryActions;
        var selectLoaded = Ui.Button("全选已加载", () => { gallerySelection.SelectLoaded(loadedGallery); RefreshGallery(); }); selectLoaded.Name = "GallerySelectLoaded"; bulk.Children.Add(selectLoaded);
        bulk.Children.Add(Ui.Button("清除选择", () => { gallerySelection.Clear(); RefreshGallery(); }));
        var favoriteAll = Ui.Button("收藏所选", () => SetSelectedFavorite(true)); favoriteAll.Name = "GalleryFavoriteSelected"; bulk.Children.Add(favoriteAll);
        var unfavoriteAll = Ui.Button("取消收藏", () => SetSelectedFavorite(false)); unfavoriteAll.Name = "GalleryUnfavoriteSelected"; bulk.Children.Add(unfavoriteAll);
        var export = Ui.AsyncButton("导出原图", ExportSelectedAsync, owner.Error); export.Name = "GalleryExportSelected"; bulk.Children.Add(export);
        cancelExport = Ui.Button("取消导出", () => exportCancellation?.Cancel()); cancelExport.Name = "GalleryCancelExport"; cancelExport.IsVisible = false; bulk.Children.Add(cancelExport);
        trashAction = Ui.Button("移入回收站", () => ModifySelected(false)); trashAction.Name = "GalleryTrashSelected"; bulk.Children.Add(trashAction);
        restoreAction = Ui.Button("恢复", () => ModifySelected(true)); restoreAction.Name = "GalleryRestoreSelected"; bulk.Children.Add(restoreAction);
        purgeAction = Ui.AsyncButton("永久删除", PurgeSelectedAsync, owner.Error); purgeAction.Name = "GalleryPurgeSelected"; bulk.Children.Add(purgeAction);
        foreach (var button in bulk.Children.OfType<Button>()) button.Margin = new Thickness(0, 0, 7, 6);
        var cleanup = Ui.AsyncButton("清理未使用参考图", CleanupOrphansAsync, owner.Error); cleanup.Name = "GalleryCleanupOrphans";
        clearTrash = Ui.AsyncButton("清空回收站", ClearTrashAsync, owner.Error); clearTrash.Name = "GalleryClearTrash"; clearTrash.IsVisible = false;
        var managementRow = new WrapPanel { Orientation = Orientation.Horizontal }; multiSelect.Margin = cleanup.Margin = clearTrash.Margin = new Thickness(0, 0, 8, 6); managementRow.Children.Add(multiSelect); managementRow.Children.Add(cleanup); managementRow.Children.Add(clearTrash);
        generation.Margin = new Thickness(16); var galleryPage = Ui.Column(galleryHeader, search, filters, galleryCount, galleryActionNotice, managementRow, bulk, warning, galleryEmpty, gallery, loadMore); galleryPage.Margin = new Thickness(16);
        var recoveryNotice = Ui.Text("");
        var recovery = new Expander { Header = "图库恢复", Content = Ui.Column(Ui.Button("恢复图库索引", () => { try { var count = owner.ImageStore.Recover(); RefreshGallery(); recoveryNotice.Text = "图库索引已恢复，找回 " + count + " 张图片。"; } catch (Exception e) { owner.Error(e); } }), recoveryNotice) };
        galleryPage.Children.Add(recovery);
        taskCenter = new ImageTaskCenterPage(owner, host); taskTab.Content = taskCenter;
        ItemsSource = new[] { new TabItem { Header = "生成", Content = new ScrollViewer { Content = generation } }, new TabItem { Header = "图库", Content = new ScrollViewer { Content = galleryPage } }, taskTab };
        SelectedIndex = 0;
        search.TextChanged += (_, _) => FilterChanged(); favoritesOnly.IsCheckedChanged += (_, _) => FilterChanged(); trashOnly.IsCheckedChanged += (_, _) => FilterChanged(); providerFilter.SelectionChanged += (_, _) => FilterChanged(); modelFilter.SelectionChanged += (_, _) => FilterChanged(); sourceFilter.SelectionChanged += (_, _) => FilterChanged(); sortFilter.SelectionChanged += (_, _) => FilterChanged(); fromDate.TextChanged += (_, _) => FilterChanged(); toDate.TextChanged += (_, _) => FilterChanged(); owner.ImageStore.Changed += StoreChanged; owner.Changed += RefreshProviders;

        RefreshProviders(); RefreshGallery();owner.ImageTasks.Changed+=TaskChanged;TaskChanged(null!);RebindTask();
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
        var selected = providers.SelectedItem as ImageGenerationProvider; var busy = submitting;
        capability.Text = selected == null ? "尚未配置图片服务。请进入模型配置 → 图片生成。" : selected.SupportsEdits ? "支持生成和单张参考图编辑。" : "此服务未启用参考图编辑；可在图片生成配置中声明服务能力。";
        referenceButton.IsEnabled = !busy && selected?.SupportsEdits == true; generate.IsEnabled = !busy && selected != null; providers.IsEnabled = !busy;
        prompt.IsReadOnly = busy; size.IsEnabled = quality.IsEnabled = !busy; cancel.IsEnabled = IsGenerating;
    }
    private async Task UploadAsync()
    {
        if (providers.SelectedItem is not ImageGenerationProvider { SupportsEdits: true }) throw new InvalidOperationException("此服务未启用参考图编辑。");
        var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择单张参考图", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("图片") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } } } });
        var file = files.FirstOrDefault(); if (file == null || disposed || submitting) return;
        await using var input = await file.OpenReadAsync(); using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer)) != 0) { if (output.Length + count > 50L * 1024 * 1024) throw new InvalidOperationException("参考图不能超过 50 MB。"); await output.WriteAsync(buffer.AsMemory(0, count)); }
        SetReference(output.ToArray(), file.Name);
    }
    internal void SetReference(byte[] bytes, string filename)
    {
        if (submitting || disposed) return;
        if (bytes.Length > 50L * 1024 * 1024) throw new InvalidOperationException("参考图不能超过 50 MB。");
        var media = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 ? "image/png" : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg" : bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? "image/webp" : throw new InvalidOperationException("参考图仅支持 PNG、JPEG 和 WebP。");
        using var stream = new MemoryStream(bytes); var bitmap = Bitmap.DecodeToWidth(stream, 240); ClearReference(); referenceBitmap = bitmap; referenceBytes = bytes.ToArray(); referenceMediaType = media; referencePreview.Source = bitmap; referencePreview.IsVisible = true; referenceLabel.Text = filename;
    }
    private void ClearReference() { if (submitting || disposed) return; referencePreview.Source = null; referenceBitmap?.Dispose(); referenceBitmap = null; referenceBytes = null; referenceMediaType = null; referenceId = null; missingReference = false; referencePreview.IsVisible = false; referenceLabel.Text = "未选择参考图"; }
    internal void Prepare(GeneratedImage image)
    {
        if (submitting || disposed) return; SelectedIndex = 0; prompt.Text = image.Prompt;
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
        if (submitting || disposed) return;
        var provider = (providers.SelectedItem as ImageGenerationProvider)?.Copy() ?? throw new InvalidOperationException("请先配置图片生成服务。");
        var text = prompt.Text?.Trim(); if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("请输入图片提示词。");
        if (missingReference) throw new InvalidOperationException("原参考图不可用，请重新上传或移除参考图后再生成。");
        if (referenceBytes != null && !provider.SupportsEdits) throw new InvalidOperationException("所选服务不支持参考图编辑。请更换服务或移除参考图。");
        ImageGenerationTaskHandle? handle=null;submitting=true;UpdateCapability();
        try
        {
            try { handle=await AcceptSubmission(provider,text);if(!disposed)status.Text="任务已提交，可继续提交下一项。"; }
            finally { submitting=false;if(!disposed)UpdateCapability(); }
            await handle.Completion;
            if(!disposed && imageJobId==handle.Id)status.Text="生成完成";
        }
        catch(OperationCanceledException){if(!disposed && (handle==null||imageJobId==handle.Id))status.Text="已取消生成。";}
        catch(Exception e){if(!disposed && (handle==null||imageJobId==handle.Id))status.Text="生成失败："+e.Message;}
        finally { if(!disposed)UpdateCapability(); }
    }
    private Task<ImageGenerationTaskHandle> AcceptSubmission(ImageGenerationProvider provider,string text)
    {
        return owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest { Provider=provider,Prompt=text,Size=Parameter(size),Quality=Parameter(quality),ReferenceImageBytes=referenceBytes,ReferenceMediaType=referenceMediaType,ReferenceImageId=referenceId,Persistent=owner.Settings.SaveChatHistory,TaskId=Guid.NewGuid().ToString("N") },new ImageTaskOrigin{Source=ImageGenerationSource.Workspace}) { BeforeStart=id=>{ if(!disposed){imageJobId=id;result.Children.Clear();result.Children.Add(new ImageGenerationPendingCard(owner,host,id,Prepare));}return Task.CompletedTask;} },CancellationToken.None);
    }
    private void TaskChanged(ImageGenerationTaskSnapshot task)=>Dispatcher.UIThread.Post(()=>{if(!disposed){taskTab.Header="任务 ("+owner.ImageTasks.List().Count(t=>!t.IsTerminal)+")";UpdateCapability();}});
    private void RebindTask(){var task=owner.ImageTasks.List().LastOrDefault(t=>t.Source==ImageGenerationSource.Workspace);if(task==null)return;imageJobId=task.Id;result.Children.Add(new ImageGenerationPendingCard(owner,host,task.Id,Prepare));}
    private void FilterChanged() { if (updatingFilters) return; galleryLimit = 40; gallerySelection.Clear(); RefreshGallery(); }
    internal void RefreshGallery()
    {
        if (disposed) return;
        int revision = Interlocked.Increment(ref galleryRevision);
        if (!ParseDate(fromDate.Text, false, out var from) || !ParseDate(toDate.Text, true, out var to)) { galleryCount.Text = "日期格式应为 YYYY-MM-DD。"; return; }
        var request = new ImageGalleryQuery { Keyword = search.Text, FavoritesOnly = favoritesOnly.IsChecked == true, Trash = trashOnly.IsChecked == true, ProviderId = providerFilter.SelectedIndex > 0 ? (providerFilter.SelectedItem as ImageGalleryProviderChoice)?.Id : null, Model = modelFilter.SelectedIndex > 0 ? modelFilter.SelectedItem as string : null, OldestFirst = sortFilter.SelectedIndex == 1, Limit = galleryLimit, FromUtc = from, ToUtc = to };
        if (sourceFilter.SelectedIndex > 0) request.Source = sourceFilter.SelectedIndex switch { 1 => ImageGenerationSource.Workspace, 2 => ImageGenerationSource.Conversation, 3 => ImageGenerationSource.SettingsTest, _ => ImageGenerationSource.Unknown };
        _ = Task.Run(() => { var options = owner.ImageStore.ListIncludingTrash(); return (Page: new ImageGalleryQueryService(owner.ImageStore).Query(request), Providers: options.Select(i => i.ProviderId).Where(i => !string.IsNullOrEmpty(i)).Distinct().OrderBy(i => i).ToArray(), Models: options.Select(i => i.Model).Where(i => !string.IsNullOrEmpty(i)).Distinct().OrderBy(i => i).ToArray()); }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            if (disposed || revision != galleryRevision) return;
            if (task.IsFaulted) { warning.Text = "图库读取失败，请检查存储。"; return; }
            updatingFilters = true; UpdateProviderOptions(providerFilter, task.Result.Providers, owner.Settings.ImageProviders); UpdateOptions(modelFilter, "全部模型", task.Result.Models); updatingFilters = false;
            foreach (var card in galleryCards) card.Dispose(); galleryCards.Clear(); gallery.Children.Clear(); warning.Text = owner.ImageStore.Warning;
            var result = task.Result.Page; loadedGallery = result.Items; galleryCount.Text = "共 " + result.Total + " 张 · 已加载 " + result.Items.Count + " 张 · 已选 " + gallerySelection.Count + " 张";
            multiSelect.Content = selecting ? "完成多选" : "多选"; galleryActions.IsVisible = selecting; clearTrash.IsVisible = trashOnly.IsChecked == true; trashAction.IsVisible = trashOnly.IsChecked != true; restoreAction.IsVisible = trashOnly.IsChecked == true; purgeAction.IsVisible = trashOnly.IsChecked == true;
            galleryEmpty.Text = string.IsNullOrWhiteSpace(search.Text) ? "还没有生成图片。" : "没有匹配的图片。"; galleryEmpty.IsVisible = result.Total == 0;
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
        var values = new List<ImageGalleryProviderChoice> { new() { Id = "", Label = "全部服务" } };
        foreach (var id in ids) { var provider = configured.FirstOrDefault(p => p.Id == id); values.Add(new ImageGalleryProviderChoice { Id = id, Label = provider == null ? "已移除的服务 · " + id.Substring(0, Math.Min(6, id.Length)) : provider.Name }); }
        box.ItemsSource = values; box.SelectedItem = values.FirstOrDefault(i => i.Id == selected) ?? values[0];
    }
    private void ToggleSelected(string id) { gallerySelection.Toggle(id); RefreshGallery(); }
    private void ToggleFavorite(string id) { try { var image = owner.ImageStore.Get(id); owner.ImageStore.SetFavorite(new[] { id }, !image.IsFavorite); } catch (Exception e) { owner.Error(e); } }
    private void SetSelectedFavorite(bool value) { if (gallerySelection.Count == 0) return; try { owner.ImageStore.SetFavorite(gallerySelection.Ids, value); galleryActionNotice.Text = value ? "已收藏所选图片。" : "已取消所选图片的收藏。"; } catch (Exception e) { owner.Error(e); } }
    private static string ManagementSummary(ImagePurgeResult result) => "已删除 " + result.DeletedIds.Count + " 张；保留 " + result.Blocked.Count + " 张。" + string.Join("；", result.Blocked.Select(i => i.Key + "：" + i.Value)) + (result.CleanupPendingIds.Count > 0 ? "；" + result.CleanupPendingIds.Count + " 张文件待下次清理。" : "");
    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var dialog = Ui.Window(title, Ui.Text(""), 450, 240); dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Content = new Border { Padding = new Thickness(22), Child = Ui.Column(Ui.Text(message), Ui.Row(Ui.Button("取消", () => dialog.Close(false)), Ui.Button(action, () => dialog.Close(true)))) };
        return await dialog.ShowDialog<bool>(host);
    }
    private async void ModifySelected(bool restore)
    {
        if (gallerySelection.Count == 0) return;
        var ids = gallerySelection.Ids.ToArray();
        try { if (restore) owner.ImageStore.Restore(ids); else if (await ConfirmAsync("移入回收站", "将所选 " + ids.Length + " 张图片移入回收站？原聊天仍可查看。", "移入回收站")) owner.ImageStore.MoveToTrash(ids); else return; gallerySelection.Clear(); RefreshGallery(); }
        catch (Exception e) { owner.Error(e); }
    }
    private async Task ExportSelectedAsync()
    {
        if (gallerySelection.Count == 0 || exportCancellation != null) return;
        var folders = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择原图导出目录", AllowMultiple = false }); var folder = folders.FirstOrDefault(); if (folder == null) return;
        var path = folder.TryGetLocalPath(); if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("请选择本地目录。");
        using var source = new CancellationTokenSource(); exportCancellation = source; cancelExport.IsVisible = true; var ids = gallerySelection.Ids.ToArray();
        try
        {
            var progress = new Progress<ImageExportItem>(_ => galleryCount.Text = "正在导出原图…");
            var summary = await new ImageGalleryExport(owner.ImageStore, owner.ImageUsage).ExportAsync(ids, path, progress, source.Token);
            galleryCount.Text = (summary.Canceled ? "已取消；" : "") + "已导出 " + summary.Completed + " 张，失败 " + summary.Failed + " 张。";
            galleryActionNotice.Text = string.Join("\n", summary.Items.Select(i => i.Id + "：" + (i.Path ?? i.Error ?? (i.Canceled ? "已取消" : "未导出"))));
        }
        finally { exportCancellation = null; cancelExport.IsVisible = false; }
    }
    private async Task PurgeSelectedAsync()
    {
        if (gallerySelection.Count == 0) return; var ids = gallerySelection.Ids.ToArray();
        var preview = await owner.ImageManagement.InspectReferencesAsync(ids, CancellationToken.None);
        var assets = owner.ImageStore.ListIncludingTrash().Where(i => ids.Contains(i.Id) && i.TrashedUtc.HasValue).ToList();
        if (!await ConfirmAsync("永久删除图片", "所选 " + assets.Count + " 张，约 " + assets.Sum(i => i.Bytes) / 1024 + " KB；仍被引用 " + preview.Blocked.Count + " 张会保留。", "永久删除")) return;
        var result = await owner.ImageManagement.PurgeAsync(ids, CancellationToken.None); gallerySelection.Clear(); galleryActionNotice.Text = ManagementSummary(result); RefreshGallery();
    }
    private async Task ClearTrashAsync()
    {
        var assets = owner.ImageStore.ListIncludingTrash().Where(i => i.TrashedUtc.HasValue).ToList(); if (assets.Count == 0) { galleryActionNotice.Text = "回收站为空。"; return; }
        var ids = assets.Select(i => i.Id).ToArray(); var preview = await owner.ImageManagement.InspectReferencesAsync(ids, CancellationToken.None);
        if (!await ConfirmAsync("清空回收站", "回收站共 " + assets.Count + " 张，约 " + assets.Sum(i => i.Bytes) / 1024 + " KB；仍被引用 " + preview.Blocked.Count + " 张会保留。", "清空回收站")) return;
        var result = await owner.ImageManagement.PurgeAsync(ids, CancellationToken.None); gallerySelection.Clear(); galleryActionNotice.Text = ManagementSummary(result); RefreshGallery();
    }
    private async Task CleanupOrphansAsync()
    {
        var preview = await owner.ImageManagement.InspectOrphanReferencesAsync(CancellationToken.None);
        if (preview.Count == 0) { galleryActionNotice.Text = "没有未使用的私有参考图。"; return; }
        var ids = preview.Candidates.Select(i => i.Id).ToArray();
        if (!await ConfirmAsync("清理未使用参考图", "候选 " + preview.Count + " 张，约 " + preview.TotalBytes / 1024 + " KB。确认后会再次检查引用。", "清理")) return;
        var result = await owner.ImageManagement.CleanupOrphanReferencesAsync(ids, CancellationToken.None); galleryActionNotice.Text = ManagementSummary(result);
    }
    internal void ShowTasks(string id) { SelectedIndex = 2; taskCenter.SelectTask(id); }
    public void Dispose() { taskCenter.Dispose(); disposed = true; Interlocked.Increment(ref galleryRevision); exportCancellation?.Cancel(); owner.ImageStore.Changed -= StoreChanged; owner.Changed -= RefreshProviders; owner.ImageTasks.Changed-=TaskChanged; referenceBytes=null;referenceId=null;referenceMediaType=null;referencePreview.Source=null;result.Children.Clear(); foreach (var card in resultCards.Concat(galleryCards)) card.Dispose(); referenceBitmap?.Dispose();referenceBitmap=null; }
}
