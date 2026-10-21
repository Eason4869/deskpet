using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ImageProviderEditor : StackPanel, IDisposable
{
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly List<ImageGenerationProvider> items;
    private readonly ComboBox providers = new() { Name = "ImageProviderList", MinWidth = 220 };
    private readonly ComboBox presets = new() { Name = "ImageServicePreset", PlaceholderText = "选择服务预设" };
    private readonly ComboBox protocol = new() { Name = "ImageProviderProtocol" };
    private readonly TextBlock presetNotes = new() { Name = "ImagePresetNotes", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox name = Ui.Input(), address = Ui.Input(), key = Ui.Input(), model = Ui.Input(), proxy = Ui.Input();
    private readonly NumericUpDown timeout = new() { Minimum = 1, Maximum = 3600, Value = 300 };
    private readonly ComboBox size = ImageWorkspacePage.ParameterChoices("ImageProviderSize", new[] { "1024x1024", "1536x1024", "1024x1536", "auto" });
    private readonly ComboBox quality = ImageWorkspacePage.ParameterChoices("ImageProviderQuality", new[] { "auto", "low", "medium", "high", "standard", "hd" });
    private readonly CheckBox useProxy = new() { Content = "此厂商使用独立代理" }, edits = new() { Content = "支持参考图编辑" };
    private readonly TextBlock notice = Ui.Text("尚未测试连接。");
    private readonly StackPanel result = Ui.Column();
    private ImageGenerationProvider? selected;
    private CancellationTokenSource? cancellation;
    private string? imageJobId;
    private bool disposed;
    private bool loading;
    private readonly List<GeneratedImageCard> cards = new();
    public ImageProviderEditor(DesktopController controller, Window window)
    {
        owner = controller; host = window; Spacing = 12; Name = "ImageProviderEditor";
        owner.Settings.EnsureImageProviders(); items = owner.Settings.ImageProviders.Select(p => p.Copy()).ToList(); key.PasswordChar = '●';
        name.Name = "ImageProviderName"; address.Name = "ImageProviderAddress"; key.Name = "ImageProviderKey"; model.Name = "ImageProviderModel"; proxy.Name = "ImageProviderProxy"; timeout.Name = "ImageProviderTimeout"; useProxy.Name = "ImageProviderUseProxy"; edits.Name = "ImageProviderSupportsEdits";
        presets.ItemsSource = ImageServicePresets.All; presets.ItemTemplate = new FuncDataTemplate<ImageServicePreset>((preset, _) => Ui.Text(preset?.Name ?? ""));
        protocol.ItemsSource = ImageServicePresets.Protocols;
        presets.SelectionChanged += (_, _) => ApplyPreset();
        protocol.SelectionChanged += (_, _) => { if (loading) return; ConfigureParameters(protocol.SelectedItem as string ?? "openai"); presetNotes.Text = "兼容网关请选择 openai；协议独立于模型名称。"; };
        providers.SelectionChanged += (_, _) => { if (loading) return; Capture(); selected = providers.SelectedItem as ImageGenerationProvider; Load(); };
        var add = Ui.Button("新增厂商", AddProvider); add.Name = "AddImageProvider";
        var remove = Ui.Button("删除", RemoveProvider); remove.Name = "DeleteImageProvider";
        Children.Add(Ui.Row(providers, add, remove));
        Children.Add(Ui.Text("每个厂商独立保存地址、密钥、代理和模型；点击保存并启用后生效。"));
        Children.Add(presets); Children.Add(Ui.Field("厂商名称", name)); Children.Add(Ui.Field("服务地址", address)); Children.Add(Ui.Field("API Key（保存在系统密钥保护下）", key));
        Children.Add(Ui.Field("协议", protocol)); Children.Add(Ui.Row(useProxy, edits)); Children.Add(Ui.Field("代理地址", proxy));
        Children.Add(Ui.Field("模型 ID（可手填）", model));
        var parameters = new Expander { Name = "ImageProviderParameters", Header = Ui.Text("图片生成设置"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = Ui.Column(Ui.Field("超时（秒）", timeout), Ui.Field("默认尺寸", size), Ui.Field("默认质量", quality), Ui.Text("图片服务与文字对话独立配置。尺寸与质量可保持服务默认。", 13), presetNotes) };
        Children.Add(parameters);
        Children.Add(Ui.Row(Ui.AsyncButton("测试生成（会调用服务并计费）", TestAsync, owner.Error), Ui.Button("取消测试", () => { if(imageJobId != null) _ = owner.ImageTasks.CancelAsync(imageJobId); })));
        Children.Add(notice); Children.Add(result);
        var save = Ui.Button("保存并启用此厂商 / 模型", Save); save.Name = "SaveImageProvider"; Children.Add(save); Reload(owner.Settings.ActiveImageProviderId);
    }
    private void AddProvider()
    {
        try { Capture(); if (items.Count >= 32) throw new InvalidOperationException("最多添加 32 个厂商。"); var provider = new ImageGenerationProvider(); items.Add(provider); Reload(provider.Id); notice.Text = "已新增草稿；点击保存并启用后生效。"; }
        catch (Exception e) { owner.Error(e); }
    }
    private async void RemoveProvider()
    {
        if (selected == null) return; var deleting=selected;var count=owner.ImageTasks.List().Count(t=>t.ProviderId==deleting.Id&&!t.IsTerminal);
        var dialog=Ui.Window("删除生图服务",Ui.Text(""),450,240);dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        dialog.Content=new Border{Padding=new Avalonia.Thickness(22),Child=Ui.Column(Ui.Text("此服务有 "+count+" 个未完成图片任务。取消任务并删除服务？"),Ui.Row(Ui.Button("取消",()=>dialog.Close(false)),Ui.Button("取消任务并删除",()=>dialog.Close(true))))};
        if(!await dialog.ShowDialog<bool>(host))return;await owner.ImageTasks.CancelProviderAsync(deleting.Id);
        items.Remove(deleting); Reload(items.FirstOrDefault()?.Id); notice.Text = "已从草稿删除；点击保存并启用后生效。";
    }
    private void ApplyPreset()
    {
        if (loading || presets.SelectedItem is not ImageServicePreset preset) return;
        if (selected == null) { if (items.Count >= 32) { notice.Text = "最多添加 32 个厂商。"; return; } var provider = new ImageGenerationProvider(); items.Add(provider); Reload(provider.Id); }
        loading = true; presets.SelectedItem = preset; loading = false;
        if (!ServicePresets.SameOrigin(address.Text ?? "", preset.BaseUrl)) key.Text = "";
        name.Text = preset.Name; address.Text = preset.BaseUrl; model.Text = preset.Model; protocol.SelectedItem = preset.Protocol; edits.IsChecked = preset.SupportsEdits;
        ConfigureParameters(preset.Protocol); presetNotes.Text = preset.Notes; notice.Text = "已应用预设；点击保存并启用后生效。";
    }
    private void ConfigureParameters(string selectedProtocol)
    {
        ImageWorkspacePage.ConfigureParameters(size, quality, selectedProtocol);
    }
    private void Reload(string? id)
    {
        loading = true; providers.ItemsSource = null; providers.ItemsSource = items; selected = items.FirstOrDefault(p => p.Id == id) ?? items.FirstOrDefault(); providers.SelectedItem = selected; loading = false; Load();
    }
    private void Load()
    {
        loading = true;
        presets.SelectedItem = null; presetNotes.Text = "兼容网关请选择 openai；协议独立于模型名称。";
        name.Text = selected?.Name ?? ""; address.Text = selected?.Connection.BaseUrl ?? ""; key.Text = selected?.Connection.ApiKey ?? ""; model.Text = selected?.Connection.Model ?? "";
        proxy.Text = selected?.Connection.ProxyAddress ?? ""; useProxy.IsChecked = selected?.Connection.UseProxy == true; edits.IsChecked = selected?.SupportsEdits == true;
        protocol.SelectedItem = selected?.Protocol ?? "openai"; ConfigureParameters(selected?.Protocol ?? "openai");
        timeout.Value = selected?.TimeoutSeconds ?? 300; ImageWorkspacePage.SelectParameter(size, selected?.Size); ImageWorkspacePage.SelectParameter(quality, selected?.Quality); loading = false;
    }
    private void Capture()
    {
        if (loading || selected == null) return;
        selected.Name = name.Text?.Trim() ?? ""; selected.Connection.BaseUrl = address.Text?.Trim() ?? ""; selected.Connection.ApiKey = key.Text ?? ""; selected.Connection.Model = model.Text?.Trim() ?? "";
        selected.Connection.ProxyAddress = proxy.Text?.Trim() ?? ""; selected.Connection.UseProxy = useProxy.IsChecked == true; selected.SupportsEdits = edits.IsChecked == true;
        selected.Protocol = protocol.SelectedItem as string ?? "openai";
        selected.TimeoutSeconds = (int)(timeout.Value ?? 300); selected.Size = ImageWorkspacePage.Parameter(size); selected.Quality = ImageWorkspacePage.Parameter(quality); selected.Normalize();
    }
    private void Save()
    {
        try { Capture(); owner.Settings.ImageProviders = items.Select(p => p.Copy()).ToList(); owner.Settings.ActiveImageProviderId = selected?.Id ?? ""; owner.Save(); notice.Text = "已保存并启用。"; Reload(selected?.Id); }
        catch (Exception e) { owner.Error(e); }
    }
    private async Task TestAsync()
    {
        if (cancellation != null) return; Capture(); if (selected == null) { notice.Text = "请先新增厂商。"; return; }
        var provider = selected.Copy(); using var cancel = new CancellationTokenSource(); cancellation = cancel; notice.Text = "正在测试生成…";
        try
        {
            var handle = await owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest { Provider = provider, Prompt = "A small friendly blue fish on a simple white background", Size = provider.Size, Quality = provider.Quality, Persistent = owner.Settings.SaveChatHistory, TaskId = Guid.NewGuid().ToString("N") },new ImageTaskOrigin{Source=ImageGenerationSource.SettingsTest}) { BeforeStart = id => { imageJobId=id; return Task.FromResult(0); } },CancellationToken.None);var image=await handle.Completion;
            if(disposed)return; foreach (var card in cards) card.Dispose(); cards.Clear(); result.Children.Clear();
            var view = new GeneratedImageCard(owner, host, image); cards.Add(view); result.Children.Add(view); notice.Text = "连接成功，测试图片已加入图库。";
        }
        catch (OperationCanceledException) { notice.Text = "测试已取消。"; }
        catch (Exception e) { notice.Text = "测试失败：" + e.Message; }
        finally { cancellation = null; }
    }
    public void Dispose() { disposed=true;  foreach (var card in cards) card.Dispose(); }
}
