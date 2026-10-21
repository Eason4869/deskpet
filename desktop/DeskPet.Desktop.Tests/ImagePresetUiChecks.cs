using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class ImagePresetUiChecks
{
    internal static int Run(DesktopController owner, ChatWindow chat, Func<int> requestCount)
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: image preset " + name); checks++; }
        var savedProviders = owner.Settings.ImageProviders.Select(p => p.Copy()).ToList(); var savedId = owner.Settings.ActiveImageProviderId; var requests = requestCount();
        var host = new Window { Width = 680, Height = 730 }; using var editor = new ImageProviderEditor(owner, host); host.Content = new ScrollViewer { Content = editor }; host.Show(); Dispatcher.UIThread.RunJobs();
        T Field<T>(string name) where T : Control => editor.GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
        var presets = Field<ComboBox>("ImageServicePreset"); var protocol = Field<ComboBox>("ImageProviderProtocol");
        var services = presets.ItemsSource!.Cast<ImageServicePreset>().ToArray();
        Check(services.Count(p => p.Id != "custom") == 6 && new[] { "gemini", "openai", "doubao", "minimax", "qwen", "grok" }.All(id => services.Any(p => p.Protocol == id)), "all six mainstream provider presets and custom gateway are selectable");
        Check(protocol.ItemsSource!.Cast<string>().Order().SequenceEqual(ImageServicePresets.Protocols.Order()), "explicit protocol selector includes backend supported protocols");
        Field<NumericUpDown>("ImageProviderTimeout").Value = 456; Field<CheckBox>("ImageProviderUseProxy").IsChecked = true; Field<TextBox>("ImageProviderProxy").Text = "http://127.0.0.1:7890";
        foreach (var preset in services)
        {
            Field<TextBox>("ImageProviderAddress").Text = "https://unrelated-fixture.example/v1"; Field<TextBox>("ImageProviderKey").Text = "cross-origin-secret"; presets.SelectedItem = preset; Dispatcher.UIThread.RunJobs();
            Check(Field<TextBox>("ImageProviderName").Text == preset.Name && Field<TextBox>("ImageProviderAddress").Text == preset.BaseUrl && Field<TextBox>("ImageProviderModel").Text == preset.Model && protocol.SelectedItem as string == preset.Protocol && Field<CheckBox>("ImageProviderSupportsEdits").IsChecked == preset.SupportsEdits && Field<TextBlock>("ImagePresetNotes").Text == preset.Notes, "preset fills fields and capability notes for " + preset.Protocol);
            Check(Field<TextBox>("ImageProviderKey").Text == "", "cross origin clears secret for " + preset.Protocol);
            Check(Field<ComboBox>("ImageProviderSize").SelectedIndex == 0 && Field<ComboBox>("ImageProviderQuality").SelectedIndex == 0 && Field<ComboBox>("ImageProviderQuality").Items.Count == ImageGenerationProtocols.QualityOptions(preset.Protocol).Length, "preset uses protocol compatible defaults for " + preset.Protocol);
        }
        Check(owner.Settings.ImageProviders.Count == savedProviders.Count && owner.Settings.ActiveImageProviderId == savedId && requestCount() == requests, "preset edits neither save nor call services automatically");
        Check(Field<NumericUpDown>("ImageProviderTimeout").Value == 456 && Field<CheckBox>("ImageProviderUseProxy").IsChecked == true && Field<TextBox>("ImageProviderProxy").Text == "http://127.0.0.1:7890", "presets retain independent proxy and timeout draft");
        var native = services.Single(p => p.Protocol == "gemini"); presets.SelectedItem = native; Field<TextBox>("ImageProviderKey").Text = "same-origin-secret"; presets.SelectedItem = null; presets.SelectedItem = native;
        Check(Field<TextBox>("ImageProviderKey").Text == "same-origin-secret", "same origin preset does not erase the user's key");
        void Save() { editor.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "SaveImageProvider").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        Save(); var loaded = new SettingsStore(Path.Combine(owner.DataDirectory, "settings.json")).Load();
        Check(loaded.SelectedImageProvider()!.Protocol == "gemini" && loaded.SelectedImageProvider()!.Connection.Model == native.Model, "native protocol roundtrips through selected persisted provider");
        Check(chat.Images.SizeChoices.Items.Count == ImageGenerationProtocols.SizeOptions("gemini").Length && chat.Images.QualityChoices.Items.Count == 1, "workspace switches to native Gemini dimensions and service default quality");
        Field<TextBox>("ImageProviderAddress").Text = "https://custom-fixture-gateway.example/v1"; Field<TextBox>("ImageProviderModel").Text = "custom-gemini-image-model"; protocol.SelectedItem = "openai"; Save();
        loaded = new SettingsStore(Path.Combine(owner.DataDirectory, "settings.json")).Load();
        Check(loaded.SelectedImageProvider()!.Protocol == "openai" && loaded.SelectedImageProvider()!.Connection.BaseUrl == "https://custom-fixture-gateway.example/v1" && loaded.SelectedImageProvider()!.Connection.Model == "custom-gemini-image-model", "custom gateway URL and model remain editable and do not infer native protocol from model name");
        Check(loaded.ImageProviders[0].Id == savedProviders[0].Id && loaded.ImageProviders[0].Connection.Model == savedProviders[0].Connection.Model && requestCount() == requests, "editing current service preserves other provider configurations without calling an API");
        host.Close(); owner.Settings.ImageProviders = new(); owner.Settings.ActiveImageProviderId = "";
        var firstHost = new Window(); using var firstEditor = new ImageProviderEditor(owner, firstHost); firstHost.Content = firstEditor; firstHost.Show();
        var firstPresets = firstEditor.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => c.Name == "ImageServicePreset"); firstPresets.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        Check(owner.Settings.ImageProviders.Count == 0 && firstEditor.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => c.Name == "ImageProviderList").Items.Count == 1, "first preset creates an isolated service draft for empty configuration");
        firstHost.Close(); owner.Settings.ImageProviders = savedProviders; owner.Settings.ActiveImageProviderId = savedId; owner.Save(); Dispatcher.UIThread.RunJobs(); return checks;
    }
}
