using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    public sealed class QuietPanel:Grid
    {
        readonly PetWindow pet;readonly ThemeModePicker theme;
        readonly CheckBox imageMotion;readonly ComboBox imageConcurrency;
        readonly Slider operationVolume;
        readonly CheckBox themeSound;
        readonly Slider themeVolume;
        readonly ComboBox themeEffect;
        readonly CheckBox quiet,notificationSounds,fullscreen,clickthrough,locked,history;
        readonly SettingsPageHeader header;readonly ContentControl body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        readonly UIElement[] pages;readonly ShortcutSettingsPanel shortcuts;bool refreshing;
        public bool HasUnsavedChanges{get{return shortcuts.HasUnsavedChanges;}}
        internal int SelectedSection{get{return header.SelectedIndex;}}
        internal event System.Action SelectionChanged;
        public QuietPanel(PetWindow owner,System.Action preferencesRestored=null)
        {
            pet=owner;Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());
            header=new SettingsPageHeader(()=>(TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader")),()=>(TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader.alternativeA")),()=>(new[]{TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader.alternativeB"),TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader.alternativeC"),TextResources.Get("dataManagement.title")}),new[]{"GeneralAppearanceTab","GeneralShortcutsTab","GeneralHistoryTab"}){Margin=new Thickness(0,0,0,18)};Children.Add(header);Grid.SetRow(body,1);Children.Add(body);
            var general=new StackPanel();general.Children.Add(LocalizedUi.Text(()=>TextResources.Get("settingsLayout.instantSave"),12,Ui.Muted));var appearance=new StackPanel();
            theme=new ThemeModePicker(pet.Settings.ThemeMode);theme.Name="ThemeModePicker";theme.Changed+=delegate(string mode){if(!refreshing)pet.SetThemeModeFromSettings(theme,mode);};appearance.Children.Add(Ui.SettingRow(()=>TextResources.Get("quietPanel.quietPanel.text"),null,theme,alignRight:true,stackBelow:610));
            themeEffect=new ComboBox{Name="ThemeTransitionEffect",SelectedValuePath="Tag",Width=180,MinHeight=36};
            foreach(var effect in ThemeTransitionMotion.Choices)
            {
                var choice=effect;
                var item=new ComboBoxItem{Tag=choice};
                LocalizedUi.Set(item,"Content",()=>ThemeTransitionMotion.Label(choice));
                themeEffect.Items.Add(item);
            }
            themeEffect.SelectedValue=pet.Settings.ThemeTransitionEffect;
            LocalizedUi.Automation(themeEffect,ThemeTransitionMotion.Caption);
            themeEffect.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs args)
            {
                if(refreshing||args.AddedItems.Count==0)return;
                var item=args.AddedItems[0] as ComboBoxItem;
                var chosen=item==null?null:item.Tag as string;
                if(chosen==null)return;
                pet.Settings.ThemeTransitionEffect=chosen;
                pet.SaveSettings();
            };
            appearance.Children.Add(Ui.SettingRow(ThemeTransitionMotion.Caption,null,themeEffect,alignRight:true));
            themeSound=SettingsSurface.Toggle(()=>TextResources.Get("themeTransition.sound.enabled"),pet.Settings.ThemeTransitionSoundEnabled,enabled=>
            {
                if(refreshing)return;
                pet.Settings.ThemeTransitionSoundEnabled=enabled;
                pet.RefreshNotificationSounds();
                pet.SaveSettings();
            });
            themeSound.Name="ThemeTransitionSoundEnabled";
            appearance.Children.Add(themeSound);
            themeVolume=new Slider{Name="ThemeTransitionSoundVolume",Minimum=0,Maximum=100,TickFrequency=1,
                IsSnapToTickEnabled=true,Value=pet.Settings.ThemeTransitionSoundVolume,Width=180};
            LocalizedUi.Automation(themeVolume,()=>TextResources.Get("themeTransition.sound.volume"));
            themeVolume.ValueChanged+=delegate
            {
                if(refreshing)return;
                pet.Settings.ThemeTransitionSoundVolume=(int)themeVolume.Value;
                pet.RefreshNotificationSounds();
                pet.SaveSettings();
            };
            appearance.Children.Add(Ui.SettingRow(()=>TextResources.Get("themeTransition.sound.volume"),null,themeVolume,alignRight:true));
            var language=LocalizedUi.Button(()=>{var choice=System.Linq.Enumerable.FirstOrDefault(UiLanguages.Choices,c=>c.Code==pet.Settings.UiLanguage);return choice==null?pet.Settings.UiLanguage:choice.NativeName;});language.Name="GeneralLanguage";language.Click+=delegate{var dialog=LanguagePickerWindow.CreateAsync(pet.Settings.UiLanguage,code=>pet.ChangeLanguageAsync(code));dialog.Owner=Window.GetWindow(this);dialog.ShowDialog();};appearance.Children.Add(Ui.SettingRow(()=>TextResources.Get("settingsLayout.language"),null,language));
            imageMotion=SettingsSurface.Toggle(()=>(TextResources.Get("quietPanel.quietPanel.toggle")),pet.Settings.ImageGenerationMotionEnabled,delegate(bool enabled){if(!refreshing){pet.Settings.ImageGenerationMotionEnabled=enabled;pet.SaveSettings();}});imageMotion.Name="ImageGenerationMotionEnabled";appearance.Children.Add(imageMotion);
            imageConcurrency=new ComboBox{Name="ImageGenerationConcurrency",ItemsSource=new[]{1,2,3,4},SelectedItem=pet.Settings.ImageGenerationConcurrency,MinHeight=36};imageConcurrency.SelectionChanged+=delegate{if(!refreshing&&imageConcurrency.SelectedItem is int){pet.Settings.ImageGenerationConcurrency=(int)imageConcurrency.SelectedItem;pet.ImageTasks.Concurrency=pet.Settings.ImageGenerationConcurrency;pet.SaveSettings();}};LocalizedUi.Label(appearance,()=>(TextResources.Get("quietPanel.quietPanel.text.alternativeA")),imageConcurrency);SettingsSurface.Section(general,()=>TextResources.Get("settingsLayout.appearanceGroup"),appearance);
            var behavior=new StackPanel{Margin=new Thickness(0,8,0,0)};
            quiet=SettingsSurface.Toggle(()=>(TextResources.Get("quietPanel.quietPanel.toggle.alternativeA")),pet.Settings.QuietMode,delegate(bool enabled){if(!refreshing)pet.SetQuietMode(enabled);});quiet.Name="QuietMode";behavior.Children.Add(quiet);behavior.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("quietPanel.quietPanel.text.alternativeB")),12,Ui.Muted));
            notificationSounds=SettingsSurface.Toggle(()=>TextResources.Get("notificationSounds.enabled"),pet.Settings.NotificationSoundsEnabled,delegate(bool enabled){if(!refreshing){pet.Settings.NotificationSoundsEnabled=enabled;if(!enabled)pet.StopNotificationSounds();pet.SaveSettings();}});notificationSounds.Name="NotificationSoundsEnabled";behavior.Children.Add(notificationSounds);behavior.Children.Add(LocalizedUi.Text(()=>TextResources.Get("operationSounds.help"),12,Ui.Muted));
            operationVolume=new Slider{Name="CompanionOperationSoundVolume",Minimum=0,Maximum=100,TickFrequency=1,IsSnapToTickEnabled=true,Value=pet.Settings.CompanionOperationSoundVolume,MinWidth=180};LocalizedUi.Automation(operationVolume,()=>TextResources.Get("operationSounds.volume"));operationVolume.ValueChanged+=delegate{if(!refreshing){pet.Settings.CompanionOperationSoundVolume=(int)operationVolume.Value;pet.RefreshCompanionSoundSettings();pet.SaveSettings();}};LocalizedUi.Label(behavior,()=>TextResources.Get("operationSounds.volume"),operationVolume);
            var previews=new WrapPanel();
            AddOperationPreview(previews,CompanionOperationCue.Appear,()=>TextResources.Get("operationSounds.appear"));AddOperationPreview(previews,CompanionOperationCue.Switch,()=>TextResources.Get("operationSounds.switch"));AddOperationPreview(previews,CompanionOperationCue.Hide,()=>TextResources.Get("operationSounds.hide"));behavior.Children.Add(previews);
            fullscreen=SettingsSurface.Toggle(()=>(TextResources.Get("quietPanel.quietPanel.toggle.alternativeB")),pet.Settings.HideOnFullscreen,delegate(bool enabled){if(!refreshing){pet.Settings.HideOnFullscreen=enabled;pet.ApplySettings();}});fullscreen.Name="HideOnFullscreen";behavior.Children.Add(fullscreen);behavior.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("quietPanel.quietPanel.text.alternativeC")),12,Ui.Muted));
            clickthrough=SettingsSurface.Toggle(()=>(TextResources.Get("quietPanel.quietPanel.toggle.alternativeC")),pet.Settings.ClickThrough,delegate(bool enabled){if(!refreshing)pet.SetClickThrough(enabled);});clickthrough.Name="ClickThrough";behavior.Children.Add(clickthrough);behavior.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("quietPanel.quietPanel.text.alternativeD")),12,Ui.Muted));
            locked=SettingsSurface.Toggle(()=>(TextResources.Get("quietPanel.quietPanel.toggle.alternativeD")),pet.Settings.LockPosition,delegate(bool enabled){if(!refreshing){pet.Settings.LockPosition=enabled;pet.ApplySettings();}});locked.Name="LockPosition";behavior.Children.Add(locked);SettingsSurface.Section(general,()=>TextResources.Get("settingsLayout.behaviorGroup"),behavior);
            shortcuts=new ShortcutSettingsPanel(pet);
            var storage=new StackPanel();history=SettingsSurface.Toggle(()=>(TextResources.Get("quietPanel.quietPanel.toggle.alternativeE")),pet.Settings.SaveChatHistory,delegate(bool enabled){if(!refreshing){pet.Settings.SaveChatHistory=enabled;pet.SaveSettings();}});history.Name="SaveChatHistory";
            storage.Children.Add(new DataManagementPanel(pet,delegate{shortcuts.DiscardPreferenceDrafts();if(preferencesRestored!=null)preferencesRestored();else RefreshState();},history));
            var maintenance=new StackPanel();var cache=LocalizedUi.Button(()=>TextResources.Get("cache.title"));cache.Name="GeneralClearCache";cache.HorizontalAlignment=HorizontalAlignment.Right;cache.Click+=delegate{var dialog=new CacheMaintenanceWindow(pet.Store.DirectoryPath){Owner=Window.GetWindow(this)};dialog.ShowDialog();};maintenance.Children.Add(Ui.SettingRow(()=>TextResources.Get("cache.title"),null,cache));SettingsSurface.Section(storage,()=>TextResources.Get("settingsLayout.storageGroup"),maintenance);
            var archived=new ArchivedConversationsPanel(pet){MaxHeight=360};var archiveSection=new Expander{Name="GeneralArchivedConversations",Header=LocalizedUi.Text(()=>TextResources.Get("settingsLayout.archives"),14,Ui.Ink),Content=archived,Margin=new Thickness(0,36,0,0)};SettingsSurface.StyleExpander(archiveSection);archiveSection.Expanded+=delegate{archived.RefreshState();};storage.Children.Add(archiveSection);
            pages=new UIElement[]{SettingsSurface.Scroll(general),shortcuts,SettingsSurface.Scroll(storage)};header.SelectionChanged+=index=>{body.Content=pages[index];if(SelectionChanged!=null)SelectionChanged();};SelectSection(0);
        }
        void AddOperationPreview(Panel panel,CompanionOperationCue cue,System.Func<string> label){var button=LocalizedUi.Button(label);button.Name="PreviewCompanion"+cue;LocalizedUi.Automation(button,label);button.Click+=delegate{pet.PreviewCompanionOperation(cue);};panel.Children.Add(button);}
        public void SelectSection(int index){header.Select(index);}
        internal void ShowTarget(string name){SelectSection(name.StartsWith("Hotkey")||name.StartsWith("GuideShortcut")||name.StartsWith("SaveShortcut")?1:name=="SaveChatHistory"||name=="ClearAllHistory"||name=="RestoreDefaultPreferences"||name=="OpenPreservedAttachments"||name=="GeneralHistoryTab"||name=="GeneralClearCache"||name=="GeneralArchivedConversations"?2:0);}
        public void RefreshState(){refreshing=true;imageMotion.IsChecked=pet.Settings.ImageGenerationMotionEnabled;imageConcurrency.SelectedItem=pet.Settings.ImageGenerationConcurrency;theme.Value=pet.Settings.ThemeMode;themeEffect.SelectedValue=pet.Settings.ThemeTransitionEffect;themeSound.IsChecked=pet.Settings.ThemeTransitionSoundEnabled;themeVolume.Value=pet.Settings.ThemeTransitionSoundVolume;quiet.IsChecked=pet.Settings.QuietMode;notificationSounds.IsChecked=pet.Settings.NotificationSoundsEnabled;operationVolume.Value=pet.Settings.CompanionOperationSoundVolume;fullscreen.IsChecked=pet.Settings.HideOnFullscreen;clickthrough.IsChecked=pet.Settings.ClickThrough;locked.IsChecked=pet.Settings.LockPosition;history.IsChecked=pet.Settings.SaveChatHistory;refreshing=false;shortcuts.RefreshState();}
    }
}
