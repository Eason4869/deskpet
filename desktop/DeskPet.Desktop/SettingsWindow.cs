using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed class SettingsWindow : Window
{
    private readonly DesktopController owner;
    private readonly List<ProviderEditor> providers = new();
    private readonly ImageProviderEditor imageProvider;
    private readonly TabControl modelTabs = new();
    private readonly TabControl tabs = new();
    private readonly SpriteAssets avatarAssets;
    private Action? refreshVoice;
    private HolidayOutfitPanel? holidayOutfits;
    public SettingsWindow(DesktopController controller, SpriteAssets? avatarSource = null)
    {
        avatarAssets = avatarSource ?? new SpriteAssets();
        owner = controller; Title = TextResources.Get("settingsWindow.settingsWindow.title"); Width = 790; Height = 760; MinWidth = 570; MinHeight = 420;
        var pages = new List<TabItem>();
        void Add(string title, Control content) => pages.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = new Border { Padding = new Thickness(20), Child = content } } });
        Add(TextResources.Get("settingsWindow.settingsWindow.text"), Partner()); Add(TextResources.Get("settingsWindow.settingsWindow.text.alternativeA"), Behavior());
        var textProvider = new ProviderEditor(owner, "text"); providers.Add(textProvider); imageProvider = new ImageProviderEditor(owner, this);
        modelTabs.ItemsSource = new[] { new TabItem { Header = TextResources.Get("settingsWindow.settingsWindow.header"), Content = textProvider }, new TabItem { Header = TextResources.Get("settingsWindow.settingsWindow.header.alternativeA"), Content = imageProvider } }; modelTabs.SelectedIndex = 0; Add(TextResources.Get("settingsWindow.settingsWindow.text.alternativeB"), modelTabs);
        foreach (var item in new[] { (TextResources.Get("settingsWindow.settingsWindow.text.alternativeC"), "asr"), (TextResources.Get("settingsWindow.settingsWindow.text.alternativeD"), "speech"), ("Live", "live") }) { var editor = new ProviderEditor(owner, item.Item2); providers.Add(editor); Add(item.Item1, editor); }
        Add(TextResources.Get("settingsWindow.settingsWindow.text.alternativeE"), new WorkSettingsView(owner, this)); Add(TextResources.Get("settingsWindow.settingsWindow.text.alternativeF"), Voice()); Add(TextResources.Get("settingsWindow.settingsWindow.text.alternativeG"), Storage()); tabs.ItemsSource = pages; Content = tabs;
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs) && tabs.SelectedIndex == 7) refreshVoice?.Invoke(); };
        Closed += (_, _) => { foreach (var provider in providers) provider.Dispose(); imageProvider.Dispose(); avatarAssets.Dispose(); holidayOutfits?.Dispose(); };
    }
    public void SelectSection(string section) { tabs.SelectedIndex = section switch { "partner" => 0, "behavior" => 1, "text" or "image" => 2, "asr" => 3, "speech" => 4, "live" => 5, "work" => 6, "voice" => 7, "storage" => 8, _ => 0 }; if (section is "text" or "image") modelTabs.SelectedIndex = section == "image" ? 1 : 0; }
    private Control Partner()
    {
        var prompt = Ui.Input(owner.Settings.SystemPrompt); prompt.AcceptsReturn = true; prompt.Height = 250; prompt.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var description = Ui.Text(owner.Skin.Description);
        bool refreshingPartners = false;
        var choices = new ComboBox { Name = "PartnerPicker", MinWidth = 0, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = owner.Skins, SelectedItem = owner.Skin, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        choices.Styles.Add(new Style(s => s.OfType<ComboBoxItem>()) { Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) } });
        choices.ItemTemplate = new FuncDataTemplate<PetSkin>((skin, _) => PartnerRow(skin, choices, RemoveAsync));
        choices.SelectionChanged += (_, _) => { if (!refreshingPartners && choices.SelectedItem is PetSkin skin) owner.Run(async () => { await owner.SelectSkinAsync(skin); refreshingPartners = true; choices.SelectedItem = owner.Skin; refreshingPartners = false; description.Text = owner.Skin.Description; refreshVoice?.Invoke(); }); };
        void RefreshChoices()
        {
            refreshingPartners = true; choices.ItemsSource = null; choices.ItemsSource = owner.Skins; choices.SelectedItem = owner.Skin; refreshingPartners = false;
            prompt.Text = owner.Settings.SystemPrompt; description.Text = owner.Skin.Description; refreshVoice?.Invoke();
        }
        async Task RemoveAsync(PetSkin target)
        {
            if (owner.ExplainGuideCompanions()) return;
            if (owner.Skins.Count <= 1) return;
            if (prompt.Text != owner.Settings.SystemPrompt) throw new InvalidOperationException(TextResources.Get("settingsWindow.partner.text"));
            if (!await ConfirmRemoveCompanionAsync(target)) return;
            if (owner.ExplainGuideCompanions()) return;
            if (owner.Skin.PickerKey == target.PickerKey) await owner.SelectSkinAsync(owner.Skins.First(s => s.PickerKey != target.PickerKey && !s.IsPlaceholder));
            if (owner.ExplainGuideCompanions()) return;
            if (owner.Skin.PickerKey == target.PickerKey) return;
            SpriteAssets.Remove(target, owner.DataDirectory); owner.CompanionHidden(target); owner.Skins.RemoveAll(s => s.PickerKey == target.PickerKey); RefreshChoices();
        }
        var import = Ui.AsyncButton(TextResources.Get("settingsWindow.partner.asyncButton"), async () =>
            {
                if (owner.ExplainGuideCompanions()) return;
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = TextResources.Get("settingsWindow.partner.title"), AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType(TextResources.Get("settingsWindow.partner.fileTypeFilterFilePickerFileType")) { Patterns = new[] { "*.zip", "*.json" } } } });
                if (owner.ExplainGuideCompanions()) return;
                var file = files.FirstOrDefault(); if (file == null) return;
                var local = file.TryGetLocalPath() ?? throw new InvalidOperationException(TextResources.Get("settingsWindow.partner.localFileTryGetLocalPath"));
                var skin = await Task.Run(() => SpriteAssets.Import(local, owner.DataDirectory));
                if (owner.ExplainGuideCompanions()) return;
                var existing = owner.Skins.FindIndex(s => s.PickerKey == skin.PickerKey);
                if (existing >= 0) owner.Skins[existing] = skin; else owner.Skins.Add(skin);
                await owner.SelectSkinAsync(skin); RefreshChoices();
            }, owner.Error);
        import.Name = "ImportCompanion"; import.MinHeight = 44;
        var bulk = Ui.AsyncButton(TextResources.Get("settingsWindow.partner.asyncButton.alternativeA"), owner.DownloadAllOfficialAsync, owner.Error); bulk.Name = "DownloadAllOfficialCompanions"; bulk.MinHeight = 44;
        holidayOutfits = new HolidayOutfitPanel(owner.Holidays, () => owner.Skin, avatarAssets);
        var selectors = new Grid { Name = "PartnerWardrobeSelectors" };
        var companion = new StackPanel { Spacing = 6 }; companion.Children.Add(Ui.Text(TextResources.Get("settingsWindow.partner.text.alternativeA"))); companion.Children.Add(choices);
        selectors.Children.Add(companion); Grid.SetColumn(holidayOutfits, 2); selectors.Children.Add(holidayOutfits);
        void LayoutSelectors() => selectors.ColumnDefinitions = new ColumnDefinitions(holidayOutfits.IsVisible ? "*,12,*" : "*,0,0");
        holidayOutfits.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) LayoutSelectors(); };
        LayoutSelectors();
        var shownPersona = owner.Skin.Id;
        string CatalogKey() => string.Join("|", owner.Skins.Select(s => s.PickerKey + ":" + s.SourceKey));
        var shownCatalog = CatalogKey();
        void CatalogChanged()
        {
            var next = CatalogKey();
            if (shownCatalog == next && shownPersona == owner.Skin.Id && ReferenceEquals(choices.SelectedItem, owner.Skin)) return;
            shownCatalog = next;
            refreshingPartners = true; choices.ItemsSource = null; choices.ItemsSource = owner.Skins; choices.SelectedItem = owner.Skin; refreshingPartners = false;
            if (shownPersona != owner.Skin.Id) { shownPersona = owner.Skin.Id; prompt.Text = owner.Settings.SystemPrompt; refreshVoice?.Invoke(); }
            description.Text = owner.Skin.Description;
        }
        owner.Changed += CatalogChanged; Closed += (_, _) => owner.Changed -= CatalogChanged;
        return Ui.Column(Ui.Text(TextResources.Get("settingsWindow.partner.text.alternativeB"), 20), selectors, Ui.Row(import, bulk), description, Ui.Field(TextResources.Get("settingsWindow.partner.field"), prompt),
            Ui.Button(TextResources.Get("settingsWindow.partner.text.alternativeC"), () => { try { PersonaProfiles.Save(owner.Settings, owner.Skin.Id, owner.Skin.Prompt, prompt.Text ?? "", owner.Skin.Voices); owner.Save(); } catch (Exception e) { owner.Error(e); } }),
            Ui.Button(TextResources.Get("settingsWindow.partner.text.alternativeD"), () => { PersonaProfiles.Reset(owner.Settings, owner.Skin.Id, owner.Skin.Prompt, owner.Skin.Voices); prompt.Text = owner.Settings.SystemPrompt; owner.Save(); }),
            Ui.AsyncButton(TextResources.Get("settingsWindow.partner.asyncButton.alternativeB"), async () => {
                if (owner.ExplainGuideCompanions()) return;
                if (prompt.Text != owner.Settings.SystemPrompt) throw new InvalidOperationException(TextResources.Get("settings.companions.savePersonaFirst"));
                var key = owner.Skin.PickerKey; CompanionPackStorage.RestoreHidden(owner.DataDirectory); var restored = await Task.Run(() => SpriteAssets.LoadSkins(owner.DataDirectory));
                if (owner.ExplainGuideCompanions()) return;
                // Retain the currently active verified source while rebuilding inactive entries.
                var index = restored.FindIndex(s => s.PickerKey == key); if (index >= 0) restored[index] = owner.Skin;
                owner.Skins.Clear(); owner.Skins.AddRange(restored); RefreshChoices();
            }, owner.Error), Ui.Text(TextResources.Get("settingsWindow.partner.text.alternativeE")));
    }
    private Control PartnerRow(PetSkin? skin, ComboBox choices, Func<PetSkin, Task> remove)
    {
        if (skin == null) return new TextBlock();
        var name = Ui.Text(skin.Name); name.VerticalAlignment = VerticalAlignment.Center; name.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var button = new Button { Name = "DeleteCompanion", Content = "×", FontSize = 20, Width = 28, Height = 28, Padding = new Thickness(0), Background = Avalonia.Media.Brushes.Transparent, Focusable = false, IsEnabled = owner.Skins.Count > 1 && skin.OfficialCharacterId != "e-bao", IsVisible = false };
        ToolTip.SetTip(button, skin.OfficialCharacterId == "e-bao" ? TextResources.Get("settingsWindow.partnerRow.toolTipSetTip") : TextResources.Get("settingsWindow.partnerRow.toolTipSetTip.alternativeA"));
        button.AttachedToVisualTree += (_, _) => button.IsVisible = button.GetVisualAncestors().OfType<ComboBoxItem>().Any();
        button.Click += (_, e) => { e.Handled = true; choices.IsDropDownOpen = false; owner.Run(() => remove(skin)); };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,32") };
        row.Children.Add(new AvatarView { Assets = avatarAssets, Skin = skin });
        Grid.SetColumn(name, 1); row.Children.Add(name); Grid.SetColumn(button, 2); row.Children.Add(button);
        return row;
    }
    private Task<bool> ConfirmRemoveCompanionAsync(PetSkin target)
    {
        var dialog = Ui.Window(TextResources.Get("settingsWindow.confirmRemoveCompanionAsync.window"), Ui.Text(""), 450, 260);
        dialog.Name = "RemoveCompanionDialog"; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Content = new Border { Padding = new Thickness(22), Child = Ui.Column(
            Ui.Text(TextResources.Format("settingsWindow.confirmRemoveCompanionAsync.template",target.Name), 19),
            Ui.Text(TextResources.Get("settingsWindow.confirmRemoveCompanionAsync.text")),
            Ui.Row(Ui.Button(TextResources.Get("common.cancel"), () => dialog.Close(false)), Ui.Button(TextResources.Get("settingsWindow.confirmRemoveCompanionAsync.text.alternativeA"), () => dialog.Close(true)))) };
        return dialog.ShowDialog<bool>(this);
    }
    private Control Behavior()
    {
        var s = owner.Settings;
        var theme = new ThemeModePicker(s.ThemeMode);
        theme.Changed += mode => { s.ThemeMode = mode; DesktopTheme.Apply(mode); owner.Save(); };
        void Apply() { owner.Pet.ApplySettings(); owner.Save(); }
        var scale = new Slider { Minimum = .5, Maximum = 2.5, Value = s.Scale, TickFrequency = .1 };
        scale.ValueChanged += (_, _) => { s.Scale = scale.Value; Apply(); };
        var panel = Ui.Column(Ui.Text(TextResources.Get("settingsWindow.behavior.text"), 20), Ui.Column(Ui.Text(TextResources.Get("settingsWindow.behavior.text.alternativeA"), 16), theme), Ui.Field(TextResources.Get("settingsWindow.behavior.field"), scale),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check"), s.Topmost, v => { s.Topmost = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeA"), s.LockPosition, v => { s.LockPosition = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeB"), s.HideOnFullscreen, v => { s.HideOnFullscreen = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeC"), s.ClickThrough, v => { s.ClickThrough = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeD"), s.FollowCursor, v => { s.FollowCursor = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeE"), s.Wander, v => { s.Wander = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeF"), s.StumbleEnabled, v => { s.StumbleEnabled = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeG"), s.AutoSleepEnabled, v => { s.AutoSleepEnabled = v; Apply(); }),
            Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeH"), s.QuietMode, v => { s.QuietMode = v; Apply(); }),
            Ui.Button(TextResources.Get("settingsWindow.behavior.text.alternativeB"), () => owner.Pet.Paused = !owner.Pet.Paused),
            Ui.Button(TextResources.Get("settingsWindow.behavior.text.alternativeC"), owner.ShowPet));
        var imageMotion=Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeI"),s.ImageGenerationMotionEnabled,v=>{s.ImageGenerationMotionEnabled=v;owner.Save();});imageMotion.Name="ImageGenerationMotionEnabled";panel.Children.Add(imageMotion);
        var imageConcurrency=new NumericUpDown{Name="ImageGenerationConcurrency",Minimum=1,Maximum=4,Increment=1,Value=s.ImageGenerationConcurrency};imageConcurrency.ValueChanged+=(_,_)=>{if(imageConcurrency.Value.HasValue){s.ImageGenerationConcurrency=(int)imageConcurrency.Value.Value;owner.Save();}};panel.Children.Add(Ui.Field(TextResources.Get("settingsWindow.behavior.field.alternativeA"),imageConcurrency));
        panel.Children.Add(Ui.Text(TextResources.Get("settingsWindow.behavior.text.alternativeD"), 18));
        foreach (var entry in new[] { ("chat", TextResources.Get("settingsWindow.behavior.text.alternativeE")), ("show", TextResources.Get("settingsWindow.behavior.text.alternativeF")), ("hide", TextResources.Get("settingsWindow.behavior.text.alternativeG")) })
            panel.Children.Add(Ui.Check(entry.Item2, s.Hotkeys.GetValueOrDefault(entry.Item1) == "enabled", v => { s.Hotkeys[entry.Item1] = v ? "enabled" : "disabled"; owner.Save(); owner.RegisterHotkeys(); }));
        panel.Children.Add(Ui.Button(TextResources.Get("settingsWindow.behavior.text.alternativeH"), () => { try { MacServices.SetLoginAtStartup(true); } catch (Exception e) { owner.Error(e); } }));
        panel.Children.Add(Ui.Button(TextResources.Get("settingsWindow.behavior.text.alternativeI"), () => { try { MacServices.SetLoginAtStartup(false); } catch (Exception e) { owner.Error(e); } }));
        var min = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = s.GreetingMinMinutes };
        var max = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = s.GreetingMaxMinutes };
        panel.Children.Add(Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeJ"), s.GreetingEnabled, v => { s.GreetingEnabled = v; owner.Save(); owner.Pet.ConfigureGreetings(); }));
        panel.Children.Add(Ui.Check(TextResources.Get("settingsWindow.behavior.check.alternativeK"), s.AiGreetingEnabled, v => { s.AiGreetingEnabled = v; owner.Save(); owner.Pet.ConfigureGreetings(); }));
        panel.Children.Add(Ui.Field(TextResources.Get("settingsWindow.behavior.field.alternativeB"), min)); panel.Children.Add(Ui.Field(TextResources.Get("settingsWindow.behavior.field.alternativeC"), max));
        panel.Children.Add(Ui.Button(TextResources.Get("settingsWindow.behavior.text.alternativeJ"), () => { s.GreetingMinMinutes = (int)(min.Value ?? 30); s.GreetingMaxMinutes = Math.Max(s.GreetingMinMinutes, (int)(max.Value ?? 60)); owner.Save(); owner.Pet.ConfigureGreetings(); }));
        return panel;
    }
    private Control Voice()
    {
        var s = owner.Settings;
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = s.Volume };
        volume.ValueChanged += (_, _) => { s.Volume = (int)volume.Value; owner.Session.SetVolume(s.Volume); owner.Live.SetVolume(s.Volume); owner.Save(); };
        var speech = Ui.Input(s.VoiceName); var live = Ui.Input(s.LiveVoice);
        var voices = new ComboBox { ItemsSource = VoicePresets.For("speech", s.SpeechConnection()).Select(v => v.Id).ToList() };
        var advice = Ui.Text("");
        refreshVoice = () => {
            speech.Text = s.VoiceName; live.Text = s.LiveVoice;
            voices.ItemsSource = VoicePresets.For("speech", s.SpeechConnection()).Select(v => v.Id).ToList();
            advice.Text = owner.Skin.Name + " · " + (owner.Skin.Voices?.Description ?? TextResources.Get("settingsWindow.voice.ownerSkinName"));
        };
        refreshVoice();
        voices.SelectionChanged += (_, _) => { if (voices.SelectedItem is string choice) speech.Text = choice; };
        return Ui.Column(Ui.Text(TextResources.Get("settingsWindow.voice.text"), 20), advice, Ui.Check(TextResources.Get("settingsWindow.voice.check"), s.ReadReplies, v => { s.ReadReplies = v; owner.Save(); }), Ui.Check(TextResources.Get("settingsWindow.voice.check.alternativeA"), s.UseCloudVoice, v => { s.UseCloudVoice = v; owner.Save(); }), Ui.Field(TextResources.Get("settingsWindow.voice.field"), volume), Ui.Field(TextResources.Get("settingsWindow.voice.field.alternativeA"), voices), Ui.Field(TextResources.Get("settingsWindow.voice.field.alternativeB"), speech), Ui.Field(TextResources.Get("settingsWindow.voice.field.alternativeC"), live),
            Ui.Button(TextResources.Get("settingsWindow.voice.text.alternativeA"), () => { s.VoiceName = speech.Text?.Trim() ?? ""; s.LiveVoice = live.Text?.Trim() ?? "Aoede"; PersonaProfiles.SaveVoices(s); owner.Save(); }),
            Ui.AsyncButton(TextResources.Get("settingsWindow.voice.asyncButton"), () => owner.Session.PreviewSpeechAsync(s, owner.Skin.Name), owner.Error), Ui.Button(TextResources.Get("settingsWindow.voice.text.alternativeB"), owner.Session.StopSpeech), Ui.Text(TextResources.Get("settingsWindow.voice.text.alternativeC")));
    }
    private Control Storage() => Ui.Column(Ui.Text(TextResources.Get("settingsWindow.storage.text"), 20), Ui.Text(owner.DataDirectory), Ui.Check(TextResources.Get("settingsWindow.storage.check"), owner.Settings.SaveChatHistory, v => { owner.Settings.SaveChatHistory = v; owner.Save(); }), Ui.Text(TextResources.Get("settingsWindow.storage.text.alternativeA")), Ui.Button(TextResources.Get("settingsWindow.storage.text.alternativeB"), () => owner.Run(async () => { await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(owner.DataDirectory)); })), Ui.Button(TextResources.Get("settingsWindow.storage.text.alternativeC"), owner.ShowGuide), Ui.Button(TextResources.Get("settingsWindow.storage.text.alternativeD"), owner.ShowAbout));
}
