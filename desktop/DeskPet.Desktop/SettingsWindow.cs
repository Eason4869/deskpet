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
    private readonly ListBox navigation = new() { Name = "SettingsNavigation", Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly ContentControl pageHost = new() { Name = "SettingsPageHost", HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly List<Control> pages = new();
    internal string SelectedSection => navigation.SelectedIndex switch { 1 => "behavior", 2 => modelTabs.SelectedIndex == 1 ? "image" : "text", 3 => "asr", 4 => "speech", 5 => "live", 6 => "work", 7 => "voice", 8 => "storage", _ => "partner" };
    private readonly SpriteAssets avatarAssets;
    private Action? refreshVoice;
    private Action? refreshVolume;
    private bool refreshingPreferences;
    private HolidayOutfitPanel? holidayOutfits;
    public SettingsWindow(DesktopController controller, SpriteAssets? avatarSource = null)
    {
        avatarAssets = avatarSource ?? new SpriteAssets();
        owner = controller; LocalizedUi.Set(this,"Title",()=>(TextResources.Get("settingsWindow.settingsWindow.title"))); Width = 790; Height = 760; MinWidth = 570; MinHeight = 420;
        Background = DesktopTheme.Surface;
        var entries = new List<ListBoxItem>();
        void Add(Func<string> title, string icon, Control content) {
            pages.Add(WrapPage(content));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), Margin = new Thickness(0,2) };
            row.Children.Add(SettingsVisual.Icon(icon,18)); var label=SettingsVisual.Text(title,13);label.Margin=new Thickness(9,0,0,0);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);
            var item=new ListBoxItem{Content=row,Padding=new Thickness(10,11),MinHeight=44,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            SettingsVisual.NavigationItem(item);entries.Add(item);
        }
        Add(()=>TextResources.Get("settingsWindow.settingsWindow.text"),"partner",Partner()); Add(()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeA"),"settings",Behavior());
        var textProvider = new ProviderEditor(owner, "text"); providers.Add(textProvider); imageProvider = new ImageProviderEditor(owner, this);
        modelTabs.ItemsSource = new[] { LocalizedUi.With(new TabItem { Header = TextResources.Get("settingsWindow.settingsWindow.header"), Content = textProvider },"Header",()=>(TextResources.Get("settingsWindow.settingsWindow.header"))), LocalizedUi.With(new TabItem { Header = TextResources.Get("settingsWindow.settingsWindow.header.alternativeA"), Content = imageProvider },"Header",()=>(TextResources.Get("settingsWindow.settingsWindow.header.alternativeA"))) }; modelTabs.SelectedIndex = 0; Add(()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeB"),"model",modelTabs);
        foreach (var item in new[] { ((Func<string>)(()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeC")), "asr"), ((Func<string>)(()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeD")), "speech"), ((Func<string>)(()=>"Live"), "live") }) { var editor = new ProviderEditor(owner, item.Item2); providers.Add(editor); Add(item.Item1,item.Item2,editor); }
        Add(()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeE"),"work",new WorkSettingsView(owner, this)); Add(()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeF"),"voice",Voice()); Add(()=>TextResources.Get("dataManagement.title"),"storage",Storage());
        navigation.ItemsSource=entries;
        navigation.SelectionChanged += (_, e) => { if (!ReferenceEquals(e.Source,navigation)) return; int index=navigation.SelectedIndex;if(index<0)return;pageHost.Content=pages[index];if(index==7)refreshVoice?.Invoke(); };
        var sideTitle=SettingsVisual.Text(()=>Title??"",21);sideTitle.FontWeight=Avalonia.Media.FontWeight.SemiBold;sideTitle.Margin=new Thickness(16,24,8,18);
        var sidebar=new Border{Background=DesktopTheme.Brush("#F7F8FA"),BorderBrush=DesktopTheme.Brush("#D5E1ED"),BorderThickness=new Thickness(0,0,1,0),Child=new Grid{RowDefinitions=new RowDefinitions("Auto,*"),Children={sideTitle}}};
        var sideGrid=(Grid)sidebar.Child!;Grid.SetRow(navigation,1);sideGrid.Children.Add(navigation);navigation.Margin=new Thickness(8,0,8,16);
        var layout=new Grid{ColumnDefinitions=new ColumnDefinitions("180,*")};layout.Children.Add(sidebar);Grid.SetColumn(pageHost,1);layout.Children.Add(pageHost);Content=layout;
        SizeChanged+=(_,_)=>{layout.ColumnDefinitions[0].Width=new GridLength(Bounds.Width<680?160:180);foreach(var page in pages.OfType<ScrollViewer>())if(page.Content is Border border)border.Padding=new Thickness(Bounds.Width<680?14:26,24);};
        navigation.SelectedIndex=0;
        Closed += (_, _) => { foreach (var provider in providers) provider.Dispose(); imageProvider.Dispose(); avatarAssets.Dispose(); holidayOutfits?.Dispose(); };
    }
    private Control WrapPage(Control content) => new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = new Border { Padding = new Thickness(Bounds.Width>0&&Bounds.Width<680?14:26,24), Child = content } };
    public void SelectSection(string section) { navigation.SelectedIndex = section switch { "partner" => 0, "behavior" => 1, "text" or "image" => 2, "asr" => 3, "speech" => 4, "live" => 5, "work" => 6, "voice" => 7, "storage" => 8, _ => 0 }; if (section is "text" or "image") modelTabs.SelectedIndex = section == "image" ? 1 : 0; }
    private Control Partner()
    {
        var prompt = Ui.Input(owner.Settings.SystemPrompt); prompt.AcceptsReturn = true; prompt.Height = 250; prompt.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var description = Ui.Text(()=>(owner.Skin.DisplayDescription));
        bool refreshingPartners = false;
        var choices = new ComboBox { Name = "PartnerPicker", MinWidth = 0, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = owner.Skins, SelectedItem = owner.Skin, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        choices.Styles.Add(new Style(s => s.OfType<ComboBoxItem>()) { Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) } });
        choices.ItemTemplate = new FuncDataTemplate<PetSkin>((skin, _) => PartnerRow(skin, choices, RemoveAsync));
        choices.SelectionChanged += (_, _) => { if (!refreshingPartners && choices.SelectedItem is PetSkin skin) owner.Run(async () => { await owner.SelectSkinAsync(skin); refreshingPartners = true; choices.SelectedItem = owner.Skin; refreshingPartners = false; LocalizedUi.Set(description,"Text",()=>(owner.Skin.DisplayDescription)); refreshVoice?.Invoke(); }); };
        void RefreshChoices()
        {
            refreshingPartners = true; choices.ItemsSource = null; choices.ItemsSource = owner.Skins; choices.SelectedItem = owner.Skin; refreshingPartners = false;
            prompt.Text = owner.Settings.SystemPrompt; LocalizedUi.Set(description,"Text",()=>(owner.Skin.DisplayDescription)); refreshVoice?.Invoke();
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
        var import = LocalizedUi.AsyncButton(()=>(TextResources.Get("settingsWindow.partner.asyncButton")), async () =>
            {
                if (owner.ExplainGuideCompanions()) return;
                var files = await StorageProvider.OpenFilePickerAsync(LocalizedUi.With(new FilePickerOpenOptions { Title = TextResources.Get("settingsWindow.partner.title"), AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType(TextResources.Get("settingsWindow.partner.fileTypeFilterFilePickerFileType")) { Patterns = new[] { "*.zip", "*.json" } } } },"Title",()=>(TextResources.Get("settingsWindow.partner.title"))));
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
        var bulk = LocalizedUi.AsyncButton(()=>(TextResources.Get("settingsWindow.partner.asyncButton.alternativeA")), owner.DownloadAllOfficialAsync, owner.Error); bulk.Name = "DownloadAllOfficialCompanions"; bulk.MinHeight = 44;
        holidayOutfits = new HolidayOutfitPanel(owner.Holidays, () => owner.Skin, avatarAssets);
        var selectors = new Grid { Name = "PartnerWardrobeSelectors" };
        var companion = new StackPanel { Spacing = 6 }; companion.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.partner.text.alternativeA")))); companion.Children.Add(choices);
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
            LocalizedUi.Set(description,"Text",()=>(owner.Skin.DisplayDescription));
        }
        owner.Changed += CatalogChanged; Closed += (_, _) => owner.Changed -= CatalogChanged;
        return Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.partner.text.alternativeB")), 20), selectors, SettingsVisual.Wrap(import, bulk), description, LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.partner.field")), prompt),
            LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.partner.text.alternativeC")), () => { try { PersonaProfiles.Save(owner.Settings, owner.Skin.Id, owner.Skin.Prompt, prompt.Text ?? "", owner.Skin.Voices, owner.Skin.OfficialCharacterId ?? owner.Skin.BuiltinCharacterId); owner.Save(); } catch (Exception e) { owner.Error(e); } }),
            LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.partner.text.alternativeD")), () => { PersonaProfiles.Reset(owner.Settings, owner.Skin.Id, owner.Skin.Prompt, owner.Skin.Voices, owner.Skin.OfficialCharacterId ?? owner.Skin.BuiltinCharacterId); prompt.Text = owner.Settings.SystemPrompt; owner.Save(); }),
            LocalizedUi.AsyncButton(()=>(TextResources.Get("settingsWindow.partner.asyncButton.alternativeB")), async () => {
                if (owner.ExplainGuideCompanions()) return;
                if (prompt.Text != owner.Settings.SystemPrompt) throw new InvalidOperationException(TextResources.Get("settings.companions.savePersonaFirst"));
                var key = owner.Skin.PickerKey; CompanionPackStorage.RestoreHidden(owner.DataDirectory); var restored = await Task.Run(() => SpriteAssets.LoadSkins(owner.DataDirectory));
                if (owner.ExplainGuideCompanions()) return;
                // Retain the currently active verified source while rebuilding inactive entries.
                var index = restored.FindIndex(s => s.PickerKey == key); if (index >= 0) restored[index] = owner.Skin;
                owner.Skins.Clear(); owner.Skins.AddRange(restored); RefreshChoices();
            }, owner.Error), LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.partner.text.alternativeE"))));
    }
    private Control PartnerRow(PetSkin? skin, ComboBox choices, Func<PetSkin, Task> remove)
    {
        if (skin == null) return new TextBlock();
        var name = Ui.Text(()=>(skin.DisplayName)); name.VerticalAlignment = VerticalAlignment.Center; name.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var button = new Button { Name = "DeleteCompanion", Content = "×", FontSize = 20, Width = 28, Height = 28, Padding = new Thickness(0), Background = Avalonia.Media.Brushes.Transparent, Focusable = false, IsEnabled = owner.Skins.Count > 1 && skin.OfficialCharacterId != "e-bao", IsVisible = false };
        LocalizedUi.Tooltip(button,()=>(skin.OfficialCharacterId == "e-bao" ? TextResources.Get("settingsWindow.partnerRow.toolTipSetTip") : TextResources.Get("settingsWindow.partnerRow.toolTipSetTip.alternativeA")));
        button.AttachedToVisualTree += (_, _) => button.IsVisible = button.GetVisualAncestors().OfType<ComboBoxItem>().Any();
        button.Click += (_, e) => { e.Handled = true; choices.IsDropDownOpen = false; owner.Run(() => remove(skin)); };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,32") };
        row.Children.Add(new AvatarView { Assets = avatarAssets, Skin = skin });
        Grid.SetColumn(name, 1); row.Children.Add(name); Grid.SetColumn(button, 2); row.Children.Add(button);
        return row;
    }
    private Task<bool> ConfirmRemoveCompanionAsync(PetSkin target)
    {
        var dialog = LocalizedUi.Window(()=>(TextResources.Get("settingsWindow.confirmRemoveCompanionAsync.window")), Ui.Text(""), 450, 260);
        dialog.Name = "RemoveCompanionDialog"; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Content = new Border { Padding = new Thickness(22), Child = Ui.Column(
            LocalizedUi.Text(()=>(TextResources.Format("settingsWindow.confirmRemoveCompanionAsync.template",target.DisplayName)), 19),
            LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.confirmRemoveCompanionAsync.text"))),
            Ui.Row(LocalizedUi.Button(()=>(TextResources.Get("common.cancel")), () => dialog.Close(false)), LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.confirmRemoveCompanionAsync.text.alternativeA")), () => dialog.Close(true)))) };
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
        var panel = Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.behavior.text")), 20), Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.behavior.text.alternativeA")), 16), theme), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.behavior.field")), scale),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check")), s.Topmost, v => { s.Topmost = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeA")), s.LockPosition, v => { s.LockPosition = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeB")), s.HideOnFullscreen, v => { s.HideOnFullscreen = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeC")), s.ClickThrough, v => { s.ClickThrough = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeD")), s.FollowCursor, v => { s.FollowCursor = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeE")), s.Wander, v => { s.Wander = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeF")), s.StumbleEnabled, v => { s.StumbleEnabled = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeG")), s.AutoSleepEnabled, v => { s.AutoSleepEnabled = v; Apply(); }),
            LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeH")), s.QuietMode, v => { s.QuietMode = v; Apply(); }),
            LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.behavior.text.alternativeB")), () => owner.Pet.Paused = !owner.Pet.Paused),
            LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.behavior.text.alternativeC")), owner.ShowPet));
        var imageMotion=LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeI")),s.ImageGenerationMotionEnabled,v=>{s.ImageGenerationMotionEnabled=v;owner.Save();});imageMotion.Name="ImageGenerationMotionEnabled";panel.Children.Add(imageMotion);
        var imageConcurrency=new NumericUpDown{Name="ImageGenerationConcurrency",Minimum=1,Maximum=4,Increment=1,Value=s.ImageGenerationConcurrency};imageConcurrency.ValueChanged+=(_,_)=>{if(imageConcurrency.Value.HasValue){s.ImageGenerationConcurrency=(int)imageConcurrency.Value.Value;owner.Save();}};panel.Children.Add(LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.behavior.field.alternativeA")),imageConcurrency));
        panel.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.behavior.text.alternativeD")), 18));
        foreach (var entry in new[] { ("chat", TextResources.Get("settingsWindow.behavior.text.alternativeE")), ("show", TextResources.Get("settingsWindow.behavior.text.alternativeF")), ("hide", TextResources.Get("settingsWindow.behavior.text.alternativeG")) })
            panel.Children.Add(Ui.Check(entry.Item2, s.Hotkeys.GetValueOrDefault(entry.Item1) == "enabled", v => { s.Hotkeys[entry.Item1] = v ? "enabled" : "disabled"; owner.Save(); owner.RegisterHotkeys(); }));
        panel.Children.Add(LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.behavior.text.alternativeH")), () => { try { MacServices.SetLoginAtStartup(true); } catch (Exception e) { owner.Error(e); } }));
        panel.Children.Add(LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.behavior.text.alternativeI")), () => { try { MacServices.SetLoginAtStartup(false); } catch (Exception e) { owner.Error(e); } }));
        var min = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = s.GreetingMinMinutes };
        var max = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = s.GreetingMaxMinutes };
        panel.Children.Add(LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeJ")), s.GreetingEnabled, v => { s.GreetingEnabled = v; owner.Save(); owner.Pet.ConfigureGreetings(); }));
        panel.Children.Add(LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.behavior.check.alternativeK")), s.AiGreetingEnabled, v => { s.AiGreetingEnabled = v; owner.Save(); owner.Pet.ConfigureGreetings(); }));
        panel.Children.Add(LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.behavior.field.alternativeB")), min)); panel.Children.Add(LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.behavior.field.alternativeC")), max));
        panel.Children.Add(LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.behavior.text.alternativeJ")), () => { s.GreetingMinMinutes = (int)(min.Value ?? 30); s.GreetingMaxMinutes = Math.Max(s.GreetingMinMinutes, (int)(max.Value ?? 60)); owner.Save(); owner.Pet.ConfigureGreetings(); }));
        var data=LocalizedUi.Button(()=>(TextResources.Get("dataManagement.title")),()=>SelectSection("storage"));data.Name="GeneralDataManagement";panel.Children.Add(data);
        return panel;
    }
    private Control Voice()
    {
        var s = owner.Settings;
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = s.Volume };
        volume.ValueChanged += (_, _) => { if(refreshingPreferences)return;s.Volume = (int)volume.Value; owner.Session.SetVolume(s.Volume); owner.Live.SetVolume(s.Volume); owner.Save(); };
        refreshVolume=()=>volume.Value=s.Volume;
        var speech = Ui.Input(s.VoiceName); var live = Ui.Input(s.LiveVoice);
        var voices = new ComboBox { ItemsSource = VoicePresets.For("speech", s.SpeechConnection()).Select(v => v.Id).ToList() };
        var advice = Ui.Text("");
        refreshVoice = () => {
            speech.Text = s.VoiceName; live.Text = s.LiveVoice;
            voices.ItemsSource = VoicePresets.For("speech", s.SpeechConnection()).Select(v => v.Id).ToList();
            LocalizedUi.Set(advice,"Text",()=>(owner.Skin.DisplayName + " · " + (owner.Skin.Voices?.Description ?? TextResources.Get("settingsWindow.voice.ownerSkinName"))));
        };
        refreshVoice();
        voices.SelectionChanged += (_, _) => { if (voices.SelectedItem is string choice) speech.Text = choice; };
        return Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.voice.text")), 20), advice, LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.voice.check")), s.ReadReplies, v => { s.ReadReplies = v; owner.Save(); }), LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.voice.check.alternativeA")), s.UseCloudVoice, v => { s.UseCloudVoice = v; owner.Save(); }), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field")), volume), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field.alternativeA")), voices), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field.alternativeB")), speech), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field.alternativeC")), live),
            LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.voice.text.alternativeA")), () => { s.VoiceName = speech.Text?.Trim() ?? ""; s.LiveVoice = live.Text?.Trim() ?? "Aoede"; PersonaProfiles.SaveVoices(s); owner.Save(); }),
            LocalizedUi.AsyncButton(()=>(TextResources.Get("settingsWindow.voice.asyncButton")), () => owner.Session.PreviewSpeechAsync(s, owner.Skin.Name), owner.Error), LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.voice.text.alternativeB")), owner.Session.StopSpeech), LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.voice.text.alternativeC"))));
    }
    private void RefreshPreferences()
    {
        refreshingPreferences=true;
        try{
            pages[1]=WrapPage(Behavior()); pages[8]=WrapPage(Storage());
            if(navigation.SelectedIndex is 1 or 8)pageHost.Content=pages[navigation.SelectedIndex];
            refreshVolume?.Invoke();
        }finally{refreshingPreferences=false;}
    }
    private Control Storage()
    {
        var path=new SelectableTextBlock{Name="LocalDataPath",Text=owner.DataDirectory,TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=12,Foreground=DesktopTheme.Brush("#61738B")};Ui.ReadOnlyMenu(path);
        var folder=SettingsVisual.Button(()=> (TextResources.Get("settingsWindow.storage.text.alternativeB")),()=>owner.Run(async()=>{await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(owner.DataDirectory));}));folder.Name="OpenLocalDataDirectory";
        var location=new Expander{Name="LocalDataDirectory",Header=SettingsVisual.Header("folder",()=> (TextResources.Get("dataManagement.localDirectory"))),Content=path,HorizontalContentAlignment=HorizontalAlignment.Stretch};SettingsVisual.Disclosure(location,folder);
        var tools=new Expander{Name="ApplicationTools",Header=SettingsVisual.Header("settings",()=> (TextResources.Get("dataManagement.tools"))),HorizontalContentAlignment=HorizontalAlignment.Stretch,Content=Ui.Column(
            SettingsVisual.Text(()=> (TextResources.Get("dataManagement.toolsNote")),12,true),
            SettingsVisual.Button(()=> (TextResources.Get("onboarding.prepare.network")),()=>{var window=LocalizedUi.Window(()=>(TextResources.Get("onboarding.prepare.network")),new GitHubRoutingView(owner),520,340);window.Show(this);}),
            SettingsVisual.Button(()=> (TextResources.Get("onboarding.prepare.progress")),owner.ShowOfficialPreparation),
            SettingsVisual.Button(()=> (TextResources.Get("settingsWindow.storage.text.alternativeC")),owner.ShowGuide),
            SettingsVisual.Button(()=> (TextResources.Get("settingsWindow.storage.text.alternativeD")),owner.ShowAbout))};SettingsVisual.Disclosure(tools);
        return Ui.Column(new DataManagementView(owner,this,RefreshPreferences),location,tools);
    }
}
