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
        owner = controller; Title = "DeskPet · 设置"; Width = 790; Height = 760; MinWidth = 570; MinHeight = 420;
        var pages = new List<TabItem>();
        void Add(string title, Control content) => pages.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = new Border { Padding = new Thickness(20), Child = content } } });
        Add("伙伴", Partner()); Add("通用", Behavior());
        var textProvider = new ProviderEditor(owner, "text"); providers.Add(textProvider); imageProvider = new ImageProviderEditor(owner, this);
        modelTabs.ItemsSource = new[] { new TabItem { Header = "文字对话", Content = textProvider }, new TabItem { Header = "图片生成", Content = imageProvider } }; modelTabs.SelectedIndex = 0; Add("模型配置", modelTabs);
        foreach (var item in new[] { ("听写", "asr"), ("云朗读", "speech"), ("Live", "live") }) { var editor = new ProviderEditor(owner, item.Item2); providers.Add(editor); Add(item.Item1, editor); }
        Add("工作设置", new WorkSettingsView(owner, this)); Add("语音", Voice()); Add("数据", Storage()); tabs.ItemsSource = pages; Content = tabs;
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
            if (prompt.Text != owner.Settings.SystemPrompt) throw new InvalidOperationException("请先保存人格修改，再删除伙伴。");
            if (!await ConfirmRemoveCompanionAsync(target)) return;
            if (owner.ExplainGuideCompanions()) return;
            if (owner.Skin.PickerKey == target.PickerKey) await owner.SelectSkinAsync(owner.Skins.First(s => s.PickerKey != target.PickerKey && !s.IsPlaceholder));
            if (owner.ExplainGuideCompanions()) return;
            if (owner.Skin.PickerKey == target.PickerKey) return;
            SpriteAssets.Remove(target, owner.DataDirectory); owner.CompanionHidden(target); owner.Skins.RemoveAll(s => s.PickerKey == target.PickerKey); RefreshChoices();
        }
        var import = Ui.AsyncButton("导入 / 更新角色包", async () =>
            {
                if (owner.ExplainGuideCompanions()) return;
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入 / 更新角色包", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("角色包 / 外观说明") { Patterns = new[] { "*.zip", "*.json" } } } });
                if (owner.ExplainGuideCompanions()) return;
                var file = files.FirstOrDefault(); if (file == null) return;
                var local = file.TryGetLocalPath() ?? throw new InvalidOperationException("请选择本地外观文件。");
                var skin = await Task.Run(() => SpriteAssets.Import(local, owner.DataDirectory));
                if (owner.ExplainGuideCompanions()) return;
                var existing = owner.Skins.FindIndex(s => s.PickerKey == skin.PickerKey);
                if (existing >= 0) owner.Skins[existing] = skin; else owner.Skins.Add(skin);
                await owner.SelectSkinAsync(skin); RefreshChoices();
            }, owner.Error);
        import.Name = "ImportCompanion"; import.MinHeight = 44;
        var bulk = Ui.AsyncButton("全量下载／更新", owner.DownloadAllOfficialAsync, owner.Error); bulk.Name = "DownloadAllOfficialCompanions"; bulk.MinHeight = 44;
        holidayOutfits = new HolidayOutfitPanel(owner.Holidays, () => owner.Skin, avatarAssets);
        var selectors = new Grid { Name = "PartnerWardrobeSelectors" };
        var companion = new StackPanel { Spacing = 6 }; companion.Children.Add(Ui.Text("选择伙伴")); companion.Children.Add(choices);
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
        return Ui.Column(Ui.Text("当前伙伴", 20), selectors, Ui.Row(import, bulk), description, Ui.Field("人格（此伙伴独立保存，最多 8000 字）", prompt),
            Ui.Button("保存人格", () => { try { PersonaProfiles.Save(owner.Settings, owner.Skin.Id, owner.Skin.Prompt, prompt.Text ?? "", owner.Skin.Voices); owner.Save(); } catch (Exception e) { owner.Error(e); } }),
            Ui.Button("恢复默认人格", () => { PersonaProfiles.Reset(owner.Settings, owner.Skin.Id, owner.Skin.Prompt, owner.Skin.Voices); prompt.Text = owner.Settings.SystemPrompt; owner.Save(); }),
            Ui.AsyncButton("恢复已删除伙伴", async () => {
                if (owner.ExplainGuideCompanions()) return;
                if (prompt.Text != owner.Settings.SystemPrompt) throw new InvalidOperationException("请先保存人格修改，再恢复伙伴。");
                var key = owner.Skin.PickerKey; CompanionPackStorage.RestoreHidden(owner.DataDirectory); var restored = await Task.Run(() => SpriteAssets.LoadSkins(owner.DataDirectory));
                if (owner.ExplainGuideCompanions()) return;
                // Retain the currently active verified source while rebuilding inactive entries.
                var index = restored.FindIndex(s => s.PickerKey == key); if (index >= 0) restored[index] = owner.Skin;
                owner.Skins.Clear(); owner.Skins.AddRange(restored); RefreshChoices();
            }, owner.Error), Ui.Text("同 ID 资源包更新素材并保留个人人格、音色与聊天。无 ID 的旧外观独立保存。"));
    }
    private Control PartnerRow(PetSkin? skin, ComboBox choices, Func<PetSkin, Task> remove)
    {
        if (skin == null) return new TextBlock();
        var name = Ui.Text(skin.Name); name.VerticalAlignment = VerticalAlignment.Center; name.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var button = new Button { Name = "DeleteCompanion", Content = "×", FontSize = 20, Width = 28, Height = 28, Padding = new Thickness(0), Background = Avalonia.Media.Brushes.Transparent, Focusable = false, IsEnabled = owner.Skins.Count > 1 && skin.OfficialCharacterId != "e-bao", IsVisible = false };
        ToolTip.SetTip(button, skin.OfficialCharacterId == "e-bao" ? "默认伙伴用于离线启动，不能删除" : "删除伙伴（保留聊天和配置）");
        button.AttachedToVisualTree += (_, _) => button.IsVisible = button.GetVisualAncestors().OfType<ComboBoxItem>().Any();
        button.Click += (_, e) => { e.Handled = true; choices.IsDropDownOpen = false; owner.Run(() => remove(skin)); };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,32") };
        row.Children.Add(new AvatarView { Assets = avatarAssets, Skin = skin });
        Grid.SetColumn(name, 1); row.Children.Add(name); Grid.SetColumn(button, 2); row.Children.Add(button);
        return row;
    }
    private Task<bool> ConfirmRemoveCompanionAsync(PetSkin target)
    {
        var dialog = Ui.Window("删除伙伴", Ui.Text(""), 450, 260);
        dialog.Name = "RemoveCompanionDialog"; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Content = new Border { Padding = new Thickness(22), Child = Ui.Column(
            Ui.Text("要删除“" + target.Name + "”吗？", 19),
            Ui.Text("将从列表移除。聊天、人格和音色会保留，可通过恢复或重新导入找回。"),
            Ui.Row(Ui.Button("取消", () => dialog.Close(false)), Ui.Button("删除伙伴", () => dialog.Close(true)))) };
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
        var panel = Ui.Column(Ui.Text("桌面行为", 20), Ui.Column(Ui.Text("视觉风格", 16), theme), Ui.Field("伙伴大小", scale),
            Ui.Check("始终置顶", s.Topmost, v => { s.Topmost = v; Apply(); }),
            Ui.Check("锁定位置", s.LockPosition, v => { s.LockPosition = v; Apply(); }),
            Ui.Check("前台应用全屏时隐藏伙伴", s.HideOnFullscreen, v => { s.HideOnFullscreen = v; Apply(); }),
            Ui.Check("鼠标穿透（从菜单栏恢复）", s.ClickThrough, v => { s.ClickThrough = v; Apply(); }),
            Ui.Check("跟随鼠标", s.FollowCursor, v => { s.FollowCursor = v; Apply(); }),
            Ui.Check("偶尔散步（90～180 秒）", s.Wander, v => { s.Wander = v; Apply(); }),
            Ui.Check("落地时可能踉跄（40%，冷却 10 秒）", s.StumbleEnabled, v => { s.StumbleEnabled = v; Apply(); }),
            Ui.Check("空闲时小憩", s.AutoSleepEnabled, v => { s.AutoSleepEnabled = v; Apply(); }),
            Ui.Check("免打扰（关闭回复朗读）", s.QuietMode, v => { s.QuietMode = v; Apply(); }),
            Ui.Button("暂停 / 恢复动画", () => owner.Pet.Paused = !owner.Pet.Paused),
            Ui.Button("显示伙伴并移回屏幕", owner.ShowPet));
        var imageMotion=Ui.Check("图片生成动效",s.ImageGenerationMotionEnabled,v=>{s.ImageGenerationMotionEnabled=v;owner.Save();});imageMotion.Name="ImageGenerationMotionEnabled";panel.Children.Add(imageMotion);
        var imageConcurrency=new NumericUpDown{Name="ImageGenerationConcurrency",Minimum=1,Maximum=4,Increment=1,Value=s.ImageGenerationConcurrency};imageConcurrency.ValueChanged+=(_,_)=>{if(imageConcurrency.Value.HasValue){s.ImageGenerationConcurrency=(int)imageConcurrency.Value.Value;owner.Save();}};panel.Children.Add(Ui.Field("图片生成并发（同一服务最多 1 项）",imageConcurrency));
        panel.Children.Add(Ui.Text("全局快捷键", 18));
        foreach (var entry in new[] { ("chat", "⌘ ⇧ Space · 快聊"), ("show", "⌘ ⇧ D · 显示伙伴"), ("hide", "⌘ ⇧ H · 隐藏伙伴") })
            panel.Children.Add(Ui.Check(entry.Item2, s.Hotkeys.GetValueOrDefault(entry.Item1) == "enabled", v => { s.Hotkeys[entry.Item1] = v ? "enabled" : "disabled"; owner.Save(); owner.RegisterHotkeys(); }));
        panel.Children.Add(Ui.Button("启用登录启动", () => { try { MacServices.SetLoginAtStartup(true); } catch (Exception e) { owner.Error(e); } }));
        panel.Children.Add(Ui.Button("关闭登录启动", () => { try { MacServices.SetLoginAtStartup(false); } catch (Exception e) { owner.Error(e); } }));
        var min = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = s.GreetingMinMinutes };
        var max = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = s.GreetingMaxMinutes };
        panel.Children.Add(Ui.Check("偶尔主动打招呼", s.GreetingEnabled, v => { s.GreetingEnabled = v; owner.Save(); owner.Pet.ConfigureGreetings(); }));
        panel.Children.Add(Ui.Check("使用 AI 生成问候（未配置时使用本地问候）", s.AiGreetingEnabled, v => { s.AiGreetingEnabled = v; owner.Save(); owner.Pet.ConfigureGreetings(); }));
        panel.Children.Add(Ui.Field("问候最短间隔（分钟）", min)); panel.Children.Add(Ui.Field("问候最长间隔（分钟）", max));
        panel.Children.Add(Ui.Button("保存问候间隔", () => { s.GreetingMinMinutes = (int)(min.Value ?? 30); s.GreetingMaxMinutes = Math.Max(s.GreetingMinMinutes, (int)(max.Value ?? 60)); owner.Save(); owner.Pet.ConfigureGreetings(); }));
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
            advice.Text = owner.Skin.Name + " · " + (owner.Skin.Voices?.Description ?? "音色按伙伴与服务分别保存，已有选择优先。");
        };
        refreshVoice();
        voices.SelectionChanged += (_, _) => { if (voices.SelectedItem is string choice) speech.Text = choice; };
        return Ui.Column(Ui.Text("语音与音色", 20), advice, Ui.Check("自动朗读回复", s.ReadReplies, v => { s.ReadReplies = v; owner.Save(); }), Ui.Check("使用云端朗读（关闭时使用 macOS 系统朗读）", s.UseCloudVoice, v => { s.UseCloudVoice = v; owner.Save(); }), Ui.Field("音量", volume), Ui.Field("服务音色", voices), Ui.Field("朗读音色 / 系统语音名称", speech), Ui.Field("Gemini Live 音色", live),
            Ui.Button("保存当前伙伴音色", () => { s.VoiceName = speech.Text?.Trim() ?? ""; s.LiveVoice = live.Text?.Trim() ?? "Aoede"; PersonaProfiles.SaveVoices(s); owner.Save(); }),
            Ui.AsyncButton("试听朗读", () => owner.Session.PreviewSpeechAsync(s, owner.Skin.Name), owner.Error), Ui.Button("停止朗读", owner.Session.StopSpeech), Ui.Text("只有点击聊天听写或 Live 开始时才申请麦克风权限。云语音与 Live 的服务和密钥在对应标签独立配置。"));
    }
    private Control Storage() => Ui.Column(Ui.Text("数据与隐私", 20), Ui.Text(owner.DataDirectory), Ui.Check("在本机加密保存聊天记录", owner.Settings.SaveChatHistory, v => { owner.Settings.SaveChatHistory = v; owner.Save(); }), Ui.Text("升级应用保留此目录；删除应用不会主动清除人格、配置和记录。Windows 加密配置无法在 Mac 直接解密，迁移后请重新录入密钥。"), Ui.Button("打开数据目录", () => owner.Run(async () => { await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(owner.DataDirectory)); })), Ui.Button("重开使用引导", owner.ShowGuide), Ui.Button("关于与检查更新", owner.ShowAbout));
}
