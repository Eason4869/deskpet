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
    private readonly ComboBox providers = new() { Name = "ImageProviderList", MinWidth = 180, MaxWidth = 280, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox presets = new() { Name = "ImageServicePreset", PlaceholderText = TextResources.Get("imageProviderEditor.presets.placeholderText") };
    private readonly ComboBox protocol = new() { Name = "ImageProviderProtocol" };
    private readonly TextBlock presetNotes = new() { Name = "ImagePresetNotes", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox name = Ui.Input(), address = Ui.Input(), key = Ui.Input(), model = Ui.Input(), proxy = Ui.Input();
    private readonly NumericUpDown timeout = new() { Minimum = 1, Maximum = 3600, Value = 300 };
    private readonly ComboBox size = ImageWorkspacePage.ParameterChoices("ImageProviderSize", new[] { "1024x1024", "1536x1024", "1024x1536", "auto" });
    private readonly ComboBox quality = ImageWorkspacePage.ParameterChoices("ImageProviderQuality", new[] { "auto", "low", "medium", "high", "standard", "hd" });
    private readonly CheckBox useProxy = new() { Content = TextResources.Get("imageProviderEditor.useProxy.content") }, edits = new() { Content = TextResources.Get("imageProviderEditor.useProxy.content.alternativeA") };
    private readonly TextBlock notice = Ui.Text(TextResources.Get("imageProviderEditor.notice.text"));
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
        protocol.SelectionChanged += (_, _) => { if (loading) return; ConfigureParameters(protocol.SelectedItem as string ?? "openai"); presetNotes.Text = TextResources.Get("imageProviderEditor.imageProviderEditor.presetNotes"); };
        providers.SelectionChanged += (_, _) => { if (loading) return; Capture(); selected = providers.SelectedItem as ImageGenerationProvider; Load(); };
        var add = Ui.Button(TextResources.Get("imageProviderEditor.imageProviderEditor.text"), AddProvider); add.Name = "AddImageProvider";
        var remove = Ui.Button(TextResources.Get("common.delete"), RemoveProvider); remove.Name = "DeleteImageProvider";
        Children.Add(SettingsVisual.Wrap(providers, add, remove));
        Children.Add(Ui.Text(TextResources.Get("imageProviderEditor.imageProviderEditor.text.alternativeA")));
        Children.Add(presets); Children.Add(Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field"), name)); Children.Add(Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeA"), address)); Children.Add(Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeB"), key));
        Children.Add(Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeC"), protocol)); Children.Add(SettingsVisual.Wrap(useProxy, edits)); Children.Add(Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeD"), proxy));
        Children.Add(Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeE"), model));
        var parameters = new Expander { Name = "ImageProviderParameters", Header = Ui.Text(TextResources.Get("imageProviderEditor.imageProviderEditor.text.alternativeB")), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = Ui.Column(Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeF"), timeout), Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeG"), size), Ui.Field(TextResources.Get("imageProviderEditor.imageProviderEditor.field.alternativeH"), quality), Ui.Text(TextResources.Get("imageProviderEditor.imageProviderEditor.text.alternativeC"), 13), presetNotes) };
        Children.Add(parameters);
        Children.Add(SettingsVisual.Wrap(Ui.AsyncButton(TextResources.Get("imageProviderEditor.imageProviderEditor.asyncButton"), TestAsync, owner.Error), Ui.Button(TextResources.Get("imageProviderEditor.imageProviderEditor.text.alternativeD"), () => { if(imageJobId != null) _ = owner.ImageTasks.CancelAsync(imageJobId); })));
        Children.Add(notice); Children.Add(result);
        var save = Ui.Button(TextResources.Get("imageProviderEditor.imageProviderEditor.text.alternativeE"), Save); save.Name = "SaveImageProvider"; Children.Add(save); Reload(owner.Settings.ActiveImageProviderId);
    }
    private void AddProvider()
    {
        try { Capture(); if (items.Count >= 32) throw new InvalidOperationException(TextResources.Get("imageProviderEditor.addProvider.text")); var provider = new ImageGenerationProvider(); items.Add(provider); Reload(provider.Id); notice.Text = TextResources.Get("imageProviderEditor.addProvider.notice"); }
        catch (Exception e) { owner.Error(e); }
    }
    private async void RemoveProvider()
    {
        if (selected == null) return; var deleting=selected;var count=owner.ImageTasks.List().Count(t=>t.ProviderId==deleting.Id&&!t.IsTerminal);
        var dialog=Ui.Window(TextResources.Get("imageProviderEditor.removeProvider.window"),Ui.Text(""),450,240);dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        dialog.Content=new Border{Padding=new Avalonia.Thickness(22),Child=Ui.Column(Ui.Text(TextResources.Format("imageProviderEditor.removeProvider.template",count)),SettingsVisual.Wrap(Ui.Button(TextResources.Get("common.cancel"),()=>dialog.Close(false)),Ui.Button(TextResources.Get("imageProviderEditor.removeProvider.text"),()=>dialog.Close(true))))};
        if(!await dialog.ShowDialog<bool>(host))return;await owner.ImageTasks.CancelProviderAsync(deleting.Id);
        items.Remove(deleting); Reload(items.FirstOrDefault()?.Id); notice.Text = TextResources.Get("imageProviderEditor.removeProvider.notice");
    }
    private void ApplyPreset()
    {
        if (loading || presets.SelectedItem is not ImageServicePreset preset) return;
        if (selected == null) { if (items.Count >= 32) { notice.Text = TextResources.Get("imageProviderEditor.applyPreset.notice"); return; } var provider = new ImageGenerationProvider(); items.Add(provider); Reload(provider.Id); }
        loading = true; presets.SelectedItem = preset; loading = false;
        if (!ServicePresets.SameOrigin(address.Text ?? "", preset.BaseUrl)) key.Text = "";
        name.Text = preset.Name; address.Text = preset.BaseUrl; model.Text = preset.Model; protocol.SelectedItem = preset.Protocol; edits.IsChecked = preset.SupportsEdits;
        ConfigureParameters(preset.Protocol); presetNotes.Text = preset.Notes; notice.Text = TextResources.Get("imageProviderEditor.applyPreset.notice.alternativeA");
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
        presets.SelectedItem = null; presetNotes.Text = TextResources.Get("imageProviderEditor.load.presetNotes");
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
        try { Capture(); owner.Settings.ImageProviders = items.Select(p => p.Copy()).ToList(); owner.Settings.ActiveImageProviderId = selected?.Id ?? ""; owner.Save(); notice.Text = TextResources.Get("imageProviderEditor.save.notice"); Reload(selected?.Id); }
        catch (Exception e) { owner.Error(e); }
    }
    private async Task TestAsync()
    {
        if (cancellation != null) return; Capture(); if (selected == null) { notice.Text = TextResources.Get("imageProviderEditor.testAsync.notice"); return; }
        var provider = selected.Copy(); using var cancel = new CancellationTokenSource(); cancellation = cancel; notice.Text = TextResources.Get("imageProviderEditor.testAsync.notice.alternativeA");
        try
        {
            var handle = await owner.ImageTasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest { Provider = provider, Prompt = "A small friendly blue fish on a simple white background", Size = provider.Size, Quality = provider.Quality, Persistent = owner.Settings.SaveChatHistory, TaskId = Guid.NewGuid().ToString("N") },new ImageTaskOrigin{Source=ImageGenerationSource.SettingsTest}) { BeforeStart = id => { imageJobId=id; result.Children.Clear();result.Children.Add(new ImageGenerationPendingCard(owner,host,id)); return Task.FromResult(0); } },CancellationToken.None);var image=await handle.Completion;
            if(disposed)return; foreach (var card in cards) card.Dispose(); cards.Clear(); notice.Text = TextResources.Get("imageProviderEditor.testAsync.notice.alternativeB");
        }
        catch (OperationCanceledException) { notice.Text = TextResources.Get("imageProviderEditor.testAsync.notice.alternativeC"); }
        catch (Exception e) { notice.Text = TextResources.Format("imageProviderEditor.testAsync.noticeEMessage",e.Message); }
        finally { cancellation = null; }
    }
    public void Dispose() { disposed=true;  foreach (var card in cards) card.Dispose(); }
}
