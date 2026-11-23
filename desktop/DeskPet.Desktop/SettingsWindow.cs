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
    private ImageProviderEditor? imageProvider;
    private ImChannelSettingsView? imChannels;
    private readonly TabControl modelTabs = new() { Name = "ModelSettingsTabs" };
    private readonly TabControl partnerTabs = new(), generalTabs = new(), toolsTabs = new();
    private int requestedModelIndex, requestedPartnerIndex, requestedGeneralIndex, requestedToolsIndex;
    private string selectedSection = "partner";
    private readonly ListBox navigation = new() { Name = "SettingsNavigation", Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly ContentControl pageHost = new() { Name = "SettingsPageHost", HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly List<Control?> pages = new();
    private readonly List<Func<Control>> pageFactories = new();
    internal bool HasUnsavedChanges=>providers.Any(p=>p.HasUnsavedChanges)||imageProvider?.HasUnsavedChanges==true||imChannels?.HasUnsavedChanges==true||conversation?.HasUnsavedChanges==true||this.GetVisualDescendants().OfType<WebSearchSettingsView>().Any(v=>v.HasUnsavedChanges);
    internal string SelectedSection => selectedSection;
    private readonly SpriteAssets avatarAssets;
    private Action? refreshVoice;
    private Control? voiceSettings;
    private Action? refreshVolume;
    private bool refreshingPreferences;
    private HolidayOutfitPanel? holidayOutfits;
    private DesktopObservationSettingsPanel? observation;
    private ConversationSettingsView? conversation;
    public SettingsWindow(DesktopController controller, SpriteAssets? avatarSource = null)
    {
        avatarAssets = avatarSource ?? new SpriteAssets();
        owner = controller; LocalizedUi.Set(this,"Title",()=>TextResources.Get("settingsWindow.settingsWindow.title")); Width = 960; Height = 760; MinWidth = 570; MinHeight = 420;
        Background = DesktopTheme.Surface;
        var entries = new List<ListBoxItem>();
        void Add(Func<string> title, string icon, Func<Control> create) {
            pageFactories.Add(create); pages.Add(null);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), Margin = new Thickness(0,2) };
            row.Children.Add(SettingsVisual.Icon(icon,18)); var label=SettingsVisual.Text(title,13);label.Margin=new Thickness(9,0,0,0);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);
            var item=new ListBoxItem{Content=row,Padding=new Thickness(10,11),MinHeight=44,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            SettingsVisual.NavigationItem(item);entries.Add(item);
        }
        Add(()=>TextResources.Get("settingsWindow.settingsWindow.text"),"partner",()=>Tabs(partnerTabs,requestedPartnerIndex,
            (()=>TextResources.Get("settingsWindow.settingsWindow.text"),"partner",Partner),
            (()=>TextResources.Get("settingsWindow.behavior.text"),"companion-behavior",CompanionBehavior),
            (()=>TextResources.Get("settingsLayout.greetings"),"greetings",Greetings)));
        Add(()=>TextResources.Get("settingsLayout.modelTitle"),"model",()=>Tabs(modelTabs,requestedModelIndex,
            (()=>TextResources.Get("settingsWindow.settingsWindow.header"),"text",()=>ModelEditor("text")),
            (()=>TextResources.Get("settingsWindow.settingsWindow.header.alternativeA"),"image",()=>SettingsVisual.Group(()=>TextResources.Get("settingsWindow.settingsWindow.header.alternativeA"),imageProvider=new ImageProviderEditor(owner,this))),
            (()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeC"),"asr",()=>ModelEditor("asr")),
            (()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeD"),"speech",()=>SettingsVisual.Page(ModelEditor("speech"),Voice())),
            (()=>TextResources.Get("settingsLayout.liveVoice"),"live",()=>ModelEditor("live"))));
        Add(()=>TextResources.Get("settingsLayout.conversationTitle"),"chat",()=>conversation=new ConversationSettingsView(owner));
        Add(()=>TextResources.Get("settingsLayout.workTitle"),"work",()=>new WorkSettingsView(owner,this));
        Add(()=>TextResources.Get("settingsLayout.toolsTitle"),"settings",()=>Tabs(toolsTabs,requestedToolsIndex,
            (()=>TextResources.Get("webSearch.title"),"tools",()=>new WebSearchSettingsView(owner)),
            (()=>TextResources.Get("im.title"),"channels",()=>WrapPage(SettingsVisual.Card(imChannels=new ImChannelSettingsView(owner))))));
        Add(()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeA"),"settings",()=>Tabs(generalTabs,requestedGeneralIndex,
            (()=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeA"),"behavior",General),
            (()=>TextResources.Get("dataManagement.title"),"storage",Storage)));
        navigation.ItemsSource=entries;
        navigation.SelectionChanged += (_, e) => {
            if (!ReferenceEquals(e.Source,navigation)) return;
            int index=navigation.SelectedIndex;if(index<0)return;
            selectedSection=new[]{"partner","text","conversation","work","tools","behavior"}[index];
            pageHost.Content=GetPage(index);
            selectedSection=index switch{0=>partnerTabs.SelectedIndex switch{1=>"companion-behavior",2=>"greetings",_=>"partner"},1=>modelTabs.SelectedIndex switch{1=>"image",2=>"asr",3=>"speech",4=>"live",_=>"text"},4=>toolsTabs.SelectedIndex==1?"channels":"tools",5=>generalTabs.SelectedIndex==1?"storage":"behavior",_=>selectedSection};
        };
        var sideTitle=SettingsVisual.Text(()=>Title??"",21);sideTitle.FontWeight=Avalonia.Media.FontWeight.SemiBold;sideTitle.Margin=new Thickness(16,24,8,18);
        var about=SettingsVisual.Button(()=>TextResources.Get("settingsLayout.aboutTitle"),owner.ShowAbout);about.Name="SettingsAboutAndUpdates";about.HorizontalAlignment=HorizontalAlignment.Stretch;about.Padding=new Thickness(8);if(about.Content is TextBlock aboutLabel)aboutLabel.FontSize=12;
        var guide=SettingsVisual.Button(()=>TextResources.Get("settingsPage.settingsPage.guideButtonContentCompact"),owner.ShowGuide);guide.Name="SettingsGuide";guide.HorizontalAlignment=HorizontalAlignment.Stretch;guide.Padding=new Thickness(8);if(guide.Content is TextBlock guideLabel)guideLabel.FontSize=12;
        var bottomActions=new Grid{ColumnDefinitions=new ColumnDefinitions("*,8,*"),Margin=new Thickness(12,12,12,16)};bottomActions.Children.Add(about);Grid.SetColumn(guide,2);bottomActions.Children.Add(guide);
        var sidebar=new Border{Background=DesktopTheme.Brush("#F7F8FA"),BorderBrush=DesktopTheme.Brush("#D5E1ED"),BorderThickness=new Thickness(0,0,1,0),Child=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Children={sideTitle}}};
        var sideGrid=(Grid)sidebar.Child!;Grid.SetRow(navigation,1);sideGrid.Children.Add(navigation);navigation.Margin=new Thickness(8,0,8,0);Grid.SetRow(bottomActions,2);sideGrid.Children.Add(bottomActions);
        var layout=new Grid{ColumnDefinitions=new ColumnDefinitions("190,*")};layout.Children.Add(sidebar);Grid.SetColumn(pageHost,1);layout.Children.Add(pageHost);Content=layout;
        SizeChanged+=(_,_)=>{sideTitle.FontSize=Bounds.Width<680?18:21;layout.ColumnDefinitions[0].Width=new GridLength(Bounds.Width<680?150:190);foreach(var page in pages.OfType<ScrollViewer>())if(page.Content is Border border)border.Padding=new Thickness(Bounds.Width<680?14:26,24);};
        navigation.SelectedIndex=0;
        Closed += (_, _) => { foreach (var provider in providers) provider.Dispose(); imageProvider?.Dispose(); imChannels?.Dispose(); avatarAssets.Dispose(); holidayOutfits?.Dispose();observation?.Dispose(); };
    }
    private Control ModelEditor(string kind)
    {
        var editor=new ProviderEditor(owner,kind);providers.Add(editor);
        return SettingsVisual.Group(()=>kind switch{"asr"=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeC"),"speech"=>TextResources.Get("settingsWindow.settingsWindow.text.alternativeD"),"live"=>TextResources.Get("settingsLayout.liveVoice"),_=>TextResources.Get("settingsWindow.settingsWindow.header")},editor);
    }
    private Control Tabs(TabControl tabs,int selected,params (Func<string> Title,string Route,Func<Control> Create)[] sections)
    {
        var items=sections.Select(section=>LocalizedUi.With(new TabItem{Padding=new Thickness(8,10),FontSize=12},"Header",section.Title)).ToArray();
        void EnsurePage(){int index=tabs.SelectedIndex;if(index<0||index>=items.Length)return;items[index].Content??=sections[index].Create();selectedSection=sections[index].Route;}
        tabs.HorizontalContentAlignment=HorizontalAlignment.Stretch;tabs.VerticalContentAlignment=VerticalAlignment.Stretch;
        tabs.ItemsPanel=new FuncTemplate<Panel?>(()=>new WrapPanel{Orientation=Orientation.Horizontal});
        tabs.ItemsSource=items;tabs.SelectedIndex=selected;
        tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,tabs))EnsurePage();};EnsurePage();return tabs;
    }
    private Control GetPage(int index) => pages[index] ??= WrapPage(pageFactories[index]());
    private Control WrapPage(Control content) => content is ConversationSettingsView || ReferenceEquals(content,toolsTabs) ? content : new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = new Border { MaxWidth=900,HorizontalAlignment=HorizontalAlignment.Stretch,Padding = new Thickness(Bounds.Width>0&&Bounds.Width<680?14:26,24), Child = content } };
    public void SelectSection(string section)
    {
        requestedModelIndex=section switch{"image"=>1,"asr"=>2,"speech" or "voice"=>3,"live"=>4,_=>0};
        requestedPartnerIndex=section switch{"companion-behavior"=>1,"greetings"=>2,_=>0};
        requestedGeneralIndex=section=="storage"?1:0;requestedToolsIndex=section=="channels"?1:0;
        navigation.SelectedIndex=section switch{"text" or "image" or "asr" or "speech" or "voice" or "live"=>1,"conversation"=>2,"work"=>3,"tools" or "websearch" or "channels"=>4,"behavior" or "general" or "storage"=>5,_=>0};
        switch(navigation.SelectedIndex){case 0:partnerTabs.SelectedIndex=requestedPartnerIndex;break;case 1:modelTabs.SelectedIndex=requestedModelIndex;break;case 4:toolsTabs.SelectedIndex=requestedToolsIndex;break;case 5:generalTabs.SelectedIndex=requestedGeneralIndex;break;}
        selectedSection=section is "partner" or "companion-behavior" or "greetings" or "text" or "image" or "asr" or "speech" or "voice" or "live" or "conversation" or "work" or "tools" or "websearch" or "channels" or "behavior" or "general" or "storage"?section:"partner";
        if(section=="voice")Avalonia.Threading.Dispatcher.UIThread.Post(()=>voiceSettings?.BringIntoView());
    }
    private Control Partner()
    {
        var prompt = Ui.Input(owner.Settings.SystemPrompt); prompt.AcceptsReturn = true; prompt.Height = 250; prompt.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var description = Ui.Text(()=>(owner.Skin.DisplayDescription));
        var installationStatus=Ui.Text(owner.CompanionSelectionNotice);installationStatus.Name="CompanionInstallationStatus";
        void InstallationChanged(){installationStatus.Text=owner.CompanionSelectionNotice;}
        owner.Changed+=InstallationChanged;Closed+=(_,_)=>owner.Changed-=InstallationChanged;
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
                PetSkin skin;try{skin=await LocalCompanionImportWindow.RunAsync(this,cancel=>SpriteAssets.Import(local,owner.DataDirectory,cancel));owner.PlayNotificationCue(NotificationCue.Complete,"import:"+Guid.NewGuid().ToString("N"));}catch(OperationCanceledException){return;}catch{owner.PlayNotificationCue(NotificationCue.Failure,"import:"+Guid.NewGuid().ToString("N"));throw;}
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
        return SettingsVisual.Page(SettingsVisual.Group(()=>TextResources.Get("settingsWindow.partner.text.alternativeB"),selectors,SettingsVisual.Wrap(import,bulk),description,installationStatus),
            SettingsVisual.Group(()=>TextResources.Get("settingsWindow.partner.field"),prompt,
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
            }, owner.Error), LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.partner.text.alternativeE")))));
    }
    private Control PartnerRow(PetSkin? skin, ComboBox choices, Func<PetSkin, Task> remove)
    {
        if (skin == null) return new TextBlock();
        var name = Ui.Text(()=>(skin.DisplayName)); name.VerticalAlignment = VerticalAlignment.Center; name.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var button = new Button { Name = "DeleteCompanion", Content = "×", FontSize = 20, Width = 28, Height = 28, Padding = new Thickness(0), Background = Avalonia.Media.Brushes.Transparent, Focusable = false, IsEnabled = owner.Skins.Count > 1 && skin.OfficialCharacterId != "e-bao", IsVisible = false };
        LocalizedUi.Tooltip(button,()=>(skin.OfficialCharacterId == "e-bao" ? TextResources.Get("settingsWindow.partnerRow.toolTipSetTip") : TextResources.Get("companions.removeFromList")));
        button.AttachedToVisualTree += (_, _) => button.IsVisible = button.GetVisualAncestors().OfType<ComboBoxItem>().Any();
        button.Click += (_, e) => { e.Handled = true; choices.IsDropDownOpen = false; owner.Run(() => remove(skin)); };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,32") };
        row.Children.Add(new AvatarView { Assets = avatarAssets, Skin = skin });
        Grid.SetColumn(name, 1); row.Children.Add(name); Grid.SetColumn(button, 2); row.Children.Add(button);
        return row;
    }
    private Task<bool> ConfirmRemoveCompanionAsync(PetSkin target)
    {
        var dialog = LocalizedUi.Window(()=>(TextResources.Get("companions.removeFromList")), Ui.Text(""), 450, 260);
        dialog.Name = "RemoveCompanionDialog"; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Content = new Border { Padding = new Thickness(22), Child = Ui.Column(
            LocalizedUi.Text(()=>(TextResources.Format("settingsWindow.confirmRemoveCompanionAsync.template",target.DisplayName)), 19),
            LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.confirmRemoveCompanionAsync.text"))),
            Ui.Row(LocalizedUi.Button(()=>(TextResources.Get("common.cancel")), () => dialog.Close(false)), LocalizedUi.Button(()=>(TextResources.Get("companions.removeFromList")), () => dialog.Close(true)))) };
        return dialog.ShowDialog<bool>(this);
    }
    private Control General()
    {
        var s=owner.Settings;
        var theme=new ThemeModePicker(s.ThemeMode);theme.Changed+=mode=>{s.ThemeMode=mode;DesktopTheme.Apply(mode);owner.Save();};
        var language=LocalizedUi.AsyncButton(()=>TextResources.Get("menus.language"),async()=>{await new LanguagePickerWindow(s.UiLanguage,owner.SetUiLanguage).ShowDialog(this);},owner.Error);language.Name="SettingsLanguage";
        var general=SettingsVisual.Group(()=>TextResources.Get("settingsLayout.appearanceGroup"),SettingsVisual.Text(()=>TextResources.Get("settingsLayout.instantSave"),12,true),theme,language);
        var imageMotion=LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeI"),s.ImageGenerationMotionEnabled,v=>{s.ImageGenerationMotionEnabled=v;owner.Save();});imageMotion.Name="ImageGenerationMotionEnabled";
        var notificationSounds=LocalizedUi.Check(()=>TextResources.Get("notificationSounds.enabled"),s.NotificationSoundsEnabled,v=>{s.NotificationSoundsEnabled=v;if(!v)owner.StopNotificationSounds();owner.Save();});notificationSounds.Name="NotificationSoundsEnabled";
        var operationVolume=new Slider{Name="CompanionOperationSoundVolume",Minimum=0,Maximum=100,TickFrequency=1,IsSnapToTickEnabled=true,Value=s.CompanionOperationSoundVolume};LocalizedUi.Automation(operationVolume,()=>TextResources.Get("operationSounds.volume"));operationVolume.ValueChanged+=(_,_)=>{s.CompanionOperationSoundVolume=(int)operationVolume.Value;owner.RefreshCompanionSoundSettings();owner.Save();};
        Button Preview(CompanionOperationCue cue,Func<string> label){var button=LocalizedUi.Button(label,()=>owner.PreviewCompanionOperation(cue));button.Name="PreviewCompanion"+cue;LocalizedUi.Automation(button,label);return button;}
        var operationPreviews=SettingsVisual.Wrap(Preview(CompanionOperationCue.Appear,()=>TextResources.Get("operationSounds.appear")),Preview(CompanionOperationCue.Switch,()=>TextResources.Get("operationSounds.switch")),Preview(CompanionOperationCue.Hide,()=>TextResources.Get("operationSounds.hide")));
        var concurrency=new NumericUpDown{Name="ImageGenerationConcurrency",Minimum=1,Maximum=4,Increment=1,Value=s.ImageGenerationConcurrency};concurrency.ValueChanged+=(_,_)=>{if(concurrency.Value.HasValue){s.ImageGenerationConcurrency=(int)concurrency.Value.Value;owner.Save();}};
        var shortcuts=Ui.Column();
        foreach(var entry in new (string Id,Func<string> Title)[]{("chat",()=>TextResources.Get("settingsWindow.behavior.text.alternativeE")),("show",()=>TextResources.Get("settingsWindow.behavior.text.alternativeF")),("hide",()=>TextResources.Get("settingsWindow.behavior.text.alternativeG"))})
            shortcuts.Children.Add(LocalizedUi.Check(entry.Title,s.Hotkeys.GetValueOrDefault(entry.Id)=="enabled",v=>{s.Hotkeys[entry.Id]=v?"enabled":"disabled";owner.Save();owner.RegisterHotkeys();}));
        var data=LocalizedUi.Button(()=>TextResources.Get("dataManagement.title"),()=>SelectSection("storage"));data.Name="GeneralDataManagement";
        return SettingsVisual.Page(general,
            SettingsVisual.Group(()=>TextResources.Get("settingsLayout.behaviorGroup"),notificationSounds,SettingsVisual.Text(()=>TextResources.Get("operationSounds.help"),12,true),LocalizedUi.Field(()=>TextResources.Get("operationSounds.volume"),operationVolume),operationPreviews),
            SettingsVisual.Group(()=>TextResources.Get("settingsWindow.settingsWindow.header.alternativeA"),imageMotion,LocalizedUi.Field(()=>TextResources.Get("settingsWindow.behavior.field.alternativeA"),concurrency)),
            SettingsVisual.Group(()=>TextResources.Get("settingsWindow.behavior.text.alternativeD"),shortcuts,
                LocalizedUi.Button(()=>TextResources.Get("settingsWindow.behavior.text.alternativeH"),()=>{try{MacServices.SetLoginAtStartup(true);}catch(Exception e){owner.Error(e);}}),
                LocalizedUi.Button(()=>TextResources.Get("settingsWindow.behavior.text.alternativeI"),()=>{try{MacServices.SetLoginAtStartup(false);}catch(Exception e){owner.Error(e);}})),
            SettingsVisual.Group(()=>TextResources.Get("dataManagement.title"),data));
    }
    private Control CompanionBehavior()
    {
        var s=owner.Settings;void Apply(){owner.Pet.ApplySettings();owner.Save();}
        var scale=new Slider{Minimum=.5,Maximum=2.5,Value=s.Scale,TickFrequency=.1};scale.ValueChanged+=(_,_)=>{s.Scale=scale.Value;Apply();};
        return SettingsVisual.Page(SettingsVisual.Text(()=>TextResources.Get("settingsLayout.instantSave"),12,true),
            SettingsVisual.Group(()=>TextResources.Get("settingsWindow.behavior.text"),LocalizedUi.Field(()=>TextResources.Get("settingsWindow.behavior.field"),scale),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check"),s.Topmost,v=>{s.Topmost=v;Apply();}),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeA"),s.LockPosition,v=>{s.LockPosition=v;Apply();}),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeB"),s.HideOnFullscreen,v=>{s.HideOnFullscreen=v;Apply();}),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeC"),s.ClickThrough,v=>{s.ClickThrough=v;Apply();})),
            SettingsVisual.Group(()=>TextResources.Get("settingsLayout.companionBehavior"),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeD"),s.FollowCursor,v=>{s.FollowCursor=v;Apply();}),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeE"),s.Wander,v=>{s.Wander=v;Apply();}),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeF"),s.StumbleEnabled,v=>{s.StumbleEnabled=v;Apply();}),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeG"),s.AutoSleepEnabled,v=>{s.AutoSleepEnabled=v;Apply();}),
                LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeH"),s.QuietMode,v=>{s.QuietMode=v;if(v){owner.StopNotificationSounds();owner.StopProactiveGreetingSpeech();}Apply();}),
                SettingsVisual.Wrap(LocalizedUi.Button(()=>TextResources.Get("settingsWindow.behavior.text.alternativeB"),()=>owner.Pet.Paused=!owner.Pet.Paused),LocalizedUi.Button(()=>TextResources.Get("settingsWindow.behavior.text.alternativeC"),owner.ShowPet))));
    }
    private Control Greetings()
    {
        var s=owner.Settings;
        var min=new NumericUpDown{Minimum=1,Maximum=1440,Value=s.GreetingMinMinutes};var max=new NumericUpDown{Minimum=1,Maximum=1440,Value=s.GreetingMaxMinutes};
        observation??=new DesktopObservationSettingsPanel(owner,this);
        return SettingsVisual.Page(SettingsVisual.Group(()=>TextResources.Get("settingsLayout.greetings"),
            SettingsVisual.Text(()=>TextResources.Get("settingsLayout.greetingSaveHint"),12,true),
            LocalizedUi.Check(()=>TextResources.Get("greeting.proactive"),s.GreetingEnabled,v=>{s.GreetingEnabled=v;owner.Save();owner.Pet.ConfigureGreetings();}),
            LocalizedUi.Check(()=>TextResources.Get("greeting.autoRead"),s.GreetingAutoReadEnabled,v=>{s.GreetingAutoReadEnabled=v;owner.RefreshProactiveGreetingSpeech();owner.Save();}),
            SettingsVisual.Text(()=>TextResources.Get("greeting.autoReadHelp"),12,true),
            LocalizedUi.Check(()=>TextResources.Get("settingsWindow.behavior.check.alternativeK"),s.AiGreetingEnabled,v=>{s.AiGreetingEnabled=v;owner.Save();owner.Pet.ConfigureGreetings();}),
            LocalizedUi.Field(()=>TextResources.Get("settingsWindow.behavior.field.alternativeB"),min),LocalizedUi.Field(()=>TextResources.Get("settingsWindow.behavior.field.alternativeC"),max),
            LocalizedUi.Button(()=>TextResources.Get("settingsWindow.behavior.text.alternativeJ"),()=>{s.GreetingMinMinutes=(int)(min.Value??30);s.GreetingMaxMinutes=Math.Max(s.GreetingMinMinutes,(int)(max.Value??60));owner.Save();owner.Pet.ConfigureGreetings();})),observation);
    }
    private Control Voice()
    {
        var s = owner.Settings;
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = s.Volume };
        volume.ValueChanged += (_, _) => { if(refreshingPreferences)return;s.Volume = (int)volume.Value; owner.Session.SetVolume(s.Volume); owner.Live.SetVolume(s.Volume);owner.RefreshProactiveGreetingSpeech(); owner.Save(); };
        refreshVolume=()=>volume.Value=s.Volume;
        var speech = Ui.Input(s.VoiceName);speech.Name="VoiceName"; var live = Ui.Input(s.LiveVoice);live.Name="LiveVoiceName";
        var voices = new ComboBox { ItemsSource = VoicePresets.For("speech", s.SpeechConnection()).Select(v => v.Id).ToList() };
        var advice = Ui.Text("");
        refreshVoice = () => {
            speech.Text = s.VoiceName; live.Text = s.LiveVoice;
            voices.ItemsSource = VoicePresets.For("speech", s.SpeechConnection()).Select(v => v.Id).ToList();
            LocalizedUi.Set(advice,"Text",()=>(owner.Skin.DisplayName + " · " + (owner.Skin.Voices?.Description ?? TextResources.Get("settingsWindow.voice.ownerSkinName"))));
        };
        refreshVoice();
        voices.SelectionChanged += (_, _) => { if (voices.SelectedItem is string choice) speech.Text = choice; };
        return voiceSettings=SettingsVisual.Group(()=>TextResources.Get("settingsWindow.voice.text"), SettingsVisual.Text(()=>TextResources.Get("settingsLayout.voiceSaveHint"),12,true), advice, LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.voice.check.alternativeA")), s.UseCloudVoice, v => { s.UseCloudVoice = v; owner.Save(); }), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field")), volume), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field.alternativeA")), voices), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field.alternativeB")), speech), LocalizedUi.Field(()=>(TextResources.Get("settingsWindow.voice.field.alternativeC")), live),
            LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.voice.text.alternativeA")), () => { s.VoiceName = speech.Text?.Trim() ?? ""; s.LiveVoice = live.Text?.Trim() ?? "Aoede"; PersonaProfiles.SaveVoices(s); owner.Save(); }),
            LocalizedUi.AsyncButton(()=>(TextResources.Get("settingsWindow.voice.asyncButton")), () => owner.Session.PreviewSpeechAsync(s, owner.Skin.Name), owner.Error), LocalizedUi.Button(()=>(TextResources.Get("settingsWindow.voice.text.alternativeB")), owner.Session.StopSpeech), LocalizedUi.Text(()=>(TextResources.Get("settingsWindow.voice.text.alternativeC"))));
    }
    private void RefreshPreferences()
    {
        refreshingPreferences=true;
        try{
            if(generalTabs.ItemsSource is TabItem[] general){if(general[0].Content!=null)general[0].Content=General();if(general[1].Content!=null)general[1].Content=Storage();}
            if(partnerTabs.ItemsSource is TabItem[] partner){
                if(partner[1].Content!=null)partner[1].Content=CompanionBehavior();
                if(partner[2].Content!=null){observation?.Dispose();observation=null;partner[2].Content=Greetings();}
            }
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
        var cache=LocalizedUi.Button(()=>TextResources.Get("cache.title"),()=>{_ = new CacheMaintenanceWindow(owner.DataDirectory).ShowDialog(this);});cache.Name="ClearCache";
        return SettingsVisual.Page(new DataManagementView(owner,this,RefreshPreferences),SettingsVisual.Group(()=>TextResources.Get("cache.title"),cache),SettingsVisual.Group(()=>TextResources.Get("dataManagement.localDirectory"),location),SettingsVisual.Group(()=>TextResources.Get("dataManagement.tools"),tools));
    }
}
