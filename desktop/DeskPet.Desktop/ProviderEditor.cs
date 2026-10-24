using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ProviderEditor : StackPanel, IDisposable
{
    private readonly DesktopController owner;
    private readonly string kind;
    private readonly PetSettings view;
    private readonly ComboBox providers = new() { MinWidth = 180, MaxWidth = 280, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBox name = Ui.Input(), address = Ui.Input(), key = Ui.Input(), model = Ui.Input(), alias = Ui.Input(), proxy = Ui.Input();
    private readonly ComboBox protocol = new() { ItemsSource = new[] { "auto", "openai", "anthropic", "mimo", "gemini-live" } };
    private readonly ComboBox models = new() { MinWidth = 180, MaxWidth = 280, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly CheckBox useProxy = new() { Content = TextResources.Get("providerEditor.useProxy.content") }, stream = new() { Content = TextResources.Get("providerEditor.useProxy.content.alternativeA") };
    private readonly StackPanel found = Ui.Column();
    private readonly TextBlock notice = Ui.Text("");
    private CancellationTokenSource? fetch;
    private TextProviderSettings? selected;
    private bool loading;
    private int revision;
    public ProviderEditor(DesktopController controller, string service)
    {
        owner = controller; kind = service; Spacing = 12;
        view = owner.Settings.Copy().ProviderView(kind); view.EnsureTextProviders();
        key.PasswordChar = '●';
        var presets = new ComboBox { ItemsSource = ServicePresets.For(kind), PlaceholderText = TextResources.Get("providerEditor.providerEditor.placeholderText") };
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedItem is not ServicePreset preset) return;
            CancelFetch();
            if (!ServicePresets.SameOrigin(address.Text ?? "", preset.Url)) key.Text = "";
            name.Text = preset.Name; address.Text = preset.Url; model.Text = preset.Model; protocol.SelectedItem = preset.Protocol;
        };
        providers.SelectionChanged += (_, _) =>
        {
            if (loading) return; Capture(); CancelFetch(); selected = providers.SelectedItem as TextProviderSettings; LoadFields();
        };
        models.SelectionChanged += (_, _) =>
        {
            if (loading || selected == null || models.SelectedItem is not string id) return;
            model.Text = id; alias.Text = selected.ModelDisplayNames.GetValueOrDefault(id, "");
        };
        Children.Add(SettingsVisual.Wrap(providers, Ui.Button(TextResources.Get("providerEditor.providerEditor.text"), AddProvider), Ui.Button(TextResources.Get("common.delete"), RemoveProvider)));
        Children.Add(Ui.Text(TextResources.Get("providerEditor.providerEditor.text.alternativeA")));
        Children.Add(presets); Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field"), name)); Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field.alternativeA"), address)); Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field.alternativeB"), key));
        Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field.alternativeC"), protocol)); Children.Add(SettingsVisual.Wrap(useProxy, stream)); Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field.alternativeD"), proxy));
        Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field.alternativeE"), models)); Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field.alternativeF"), model)); Children.Add(Ui.Field(TextResources.Get("providerEditor.providerEditor.field.alternativeG"), alias));
        Children.Add(SettingsVisual.Wrap(Ui.Button(TextResources.Get("providerEditor.providerEditor.text.alternativeB"), AddModel), Ui.Button(TextResources.Get("providerEditor.providerEditor.text.alternativeC"), RemoveModel)));
        Children.Add(SettingsVisual.Wrap(Ui.AsyncButton(TextResources.Get("providerEditor.providerEditor.asyncButton"), FetchAsync, owner.Error), Ui.Button(TextResources.Get("providerEditor.providerEditor.text.alternativeD"), CancelFetch)));
        Children.Add(new ScrollViewer { Content = found, MaxHeight = 220 }); Children.Add(notice);
        Children.Add(Ui.Button(TextResources.Get("providerEditor.providerEditor.text.alternativeE"), Save));
        ReloadProviders(view.ActiveTextProviderId);
    }
    private void ReloadProviders(string id)
    {
        loading = true; providers.ItemsSource = null; providers.ItemsSource = view.TextProviders; selected = view.TextProviders.FirstOrDefault(p => p.Id == id) ?? view.TextProviders[0]; providers.SelectedItem = selected; loading = false; LoadFields();
    }
    private void LoadFields()
    {
        if (selected == null) return; loading = true;
        name.Text = selected.Name; address.Text = selected.Connection.BaseUrl; key.Text = selected.Connection.ApiKey;
        model.Text = selected.Connection.Model; alias.Text = selected.ModelDisplayNames.GetValueOrDefault(selected.Connection.Model, "");
        protocol.SelectedItem = selected.Connection.Protocol; useProxy.IsChecked = selected.Connection.UseProxy; proxy.Text = selected.Connection.ProxyAddress; stream.IsChecked = selected.Connection.StreamReplies;
        models.ItemsSource = null; models.ItemsSource = selected.Models; models.SelectedItem = selected.Connection.Model; loading = false;
    }
    private void Capture()
    {
        if (selected == null || loading) return;
        selected.Name = name.Text?.Trim() ?? TextResources.Get("providerEditor.capture.nameNameTrim"); selected.Connection.BaseUrl = address.Text?.Trim() ?? "";
        selected.Connection.ApiKey = key.Text ?? ""; selected.Connection.Model = model.Text?.Trim() ?? "";
        selected.Connection.Protocol = protocol.SelectedItem as string ?? "auto"; selected.Connection.UseProxy = useProxy.IsChecked == true;
        selected.Connection.ProxyAddress = proxy.Text?.Trim() ?? ""; selected.Connection.StreamReplies = stream.IsChecked == true;
        var id = selected.Connection.Model;
        if (id.Length > 0) { if (!selected.Models.Contains(id)) selected.Models.Add(id); if (!string.IsNullOrWhiteSpace(alias.Text)) selected.ModelDisplayNames[id] = alias.Text.Trim(); else selected.ModelDisplayNames.Remove(id); }
        selected.Normalize();
    }
    private void AddProvider()
    {
        try { Capture(); CancelFetch(); if (view.TextProviders.Count >= 32) throw new InvalidOperationException(TextResources.Get("providerEditor.addProvider.text")); var item = new TextProviderSettings(); view.TextProviders.Add(item); ReloadProviders(item.Id); } catch (Exception e) { owner.Error(e); }
    }
    private void RemoveProvider()
    {
        if (selected == null) return;
        if (view.TextProviders.Count == 1) { owner.Error(new InvalidOperationException(TextResources.Get("providerEditor.removeProvider.ownerError"))); return; }
        CancelFetch(); view.TextProviders.Remove(selected); ReloadProviders(view.TextProviders[0].Id);
    }
    private void AddModel()
    {
        try { Capture(); LoadFields(); notice.Text = TextResources.Get("providerEditor.addModel.notice"); } catch (Exception e) { owner.Error(e); }
    }
    private void RemoveModel()
    {
        if (selected == null || models.SelectedItem is not string id) return;
        selected.Models.Remove(id); selected.ModelDisplayNames.Remove(id); selected.Connection.Model = selected.Models.FirstOrDefault() ?? ""; LoadFields();
    }
    private void CancelFetch() { revision++; fetch?.Cancel(); found.Children.Clear(); notice.Text = ""; }
    private async Task FetchAsync()
    {
        Capture(); CancelFetch(); if (selected == null) return;
        var providerId = selected.Id; var tokenRevision = revision; var snapshot = selected.Connection.Copy();
        var cancellation = new CancellationTokenSource(); fetch = cancellation;
        notice.Text = TextResources.Get("providerEditor.fetchAsync.notice");
        try
        {
            var result = await owner.Api.ListModelsAsync(snapshot, cancellation.Token);
            if (cancellation.IsCancellationRequested || tokenRevision != revision || selected.Id != providerId) return;
            var checks = new List<CheckBox>();
            foreach (var id in result.Take(500)) { var check = new CheckBox { Content = id, Tag = id, IsChecked = selected.Models.Contains(id) }; checks.Add(check); found.Children.Add(check); }
            found.Children.Add(Ui.Button(TextResources.Get("providerEditor.fetchAsync.text"), () =>
            {
                if (tokenRevision != revision || selected?.Id != providerId) return;
                foreach (var check in checks.Where(c => c.IsChecked == true)) if (selected.Models.Count < TextProviderSettings.MaximumModels && !selected.Models.Contains((string)check.Tag!)) selected.Models.Add((string)check.Tag!);
                LoadFields(); notice.Text = TextResources.Get("providerEditor.fetchAsync.notice.alternativeA");
            }));
            notice.Text = TextResources.Format("providerEditor.fetchAsync.notice.alternativeB",result.Count);
        }
        catch (OperationCanceledException) { if (tokenRevision == revision) notice.Text = TextResources.Get("providerEditor.fetchAsync.notice.alternativeC"); }
        finally { if (ReferenceEquals(fetch, cancellation)) fetch = null; cancellation.Dispose(); }
    }
    private void Save()
    {
        try
        {
            if (owner.Session.IsBusy || owner.Live.IsActive) throw new InvalidOperationException(TextResources.Get("providerEditor.save.text"));
            Capture(); if (selected == null) return;
            view.ActiveTextProviderId = selected.Id; view.Text = selected.Connection.Copy();
            owner.Settings.ApplyProviders(kind, view.TextProviders.Select(p => p.Copy()).ToList(), selected.Id, selected.Connection.Copy());
            owner.Save(); notice.Text = TextResources.Get("providerEditor.save.notice");
        }
        catch (Exception e) { owner.Error(e); }
    }
    public void Dispose() => CancelFetch();
}
