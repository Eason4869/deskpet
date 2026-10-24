using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    public sealed class QuietPanel:Grid
    {
        readonly PetWindow pet;readonly ThemeModePicker theme;
        readonly CheckBox imageMotion;readonly ComboBox imageConcurrency;
        readonly CheckBox quiet,fullscreen,clickthrough,locked,history;
        readonly SettingsPageHeader header;readonly ContentControl body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        readonly UIElement[] pages;readonly ShortcutSettingsPanel shortcuts;bool refreshing;
        public bool HasUnsavedChanges{get{return shortcuts.HasUnsavedChanges;}}
        public QuietPanel(PetWindow owner,System.Action preferencesRestored=null)
        {
            pet=owner;Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());
            header=new SettingsPageHeader(TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader"),TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader.alternativeA"),new[]{TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader.alternativeB"),TextResources.Get("quietPanel.quietPanel.headerSettingsPageHeader.alternativeC"),TextResources.Get("dataManagement.title")},new[]{"GeneralAppearanceTab","GeneralShortcutsTab","GeneralHistoryTab"}){Margin=new Thickness(0,0,0,18)};Children.Add(header);Grid.SetRow(body,1);Children.Add(body);
            var general=new StackPanel();var appearance=new StackPanel();appearance.Children.Add(Ui.Text(TextResources.Get("quietPanel.quietPanel.text"),16));
            theme=new ThemeModePicker(pet.Settings.ThemeMode){Margin=new Thickness(0,12,0,16)};theme.Changed+=delegate(string mode){if(refreshing)return;pet.Settings.ThemeMode=mode;ThemeManager.Apply(mode);pet.SaveSettings();};appearance.Children.Add(theme);
            imageMotion=Ui.Toggle(TextResources.Get("quietPanel.quietPanel.toggle"),pet.Settings.ImageGenerationMotionEnabled,delegate(bool enabled){if(!refreshing){pet.Settings.ImageGenerationMotionEnabled=enabled;pet.SaveSettings();}});imageMotion.Name="ImageGenerationMotionEnabled";appearance.Children.Add(imageMotion);
            imageConcurrency=new ComboBox{Name="ImageGenerationConcurrency",ItemsSource=new[]{1,2,3,4},SelectedItem=pet.Settings.ImageGenerationConcurrency,MinHeight=36};imageConcurrency.SelectionChanged+=delegate{if(!refreshing&&imageConcurrency.SelectedItem is int){pet.Settings.ImageGenerationConcurrency=(int)imageConcurrency.SelectedItem;pet.ImageTasks.Concurrency=pet.Settings.ImageGenerationConcurrency;pet.SaveSettings();}};Ui.Label(appearance,TextResources.Get("quietPanel.quietPanel.text.alternativeA"),imageConcurrency);general.Children.Add(appearance);
            var behavior=new StackPanel{Margin=new Thickness(0,8,0,0)};
            quiet=Ui.Toggle(TextResources.Get("quietPanel.quietPanel.toggle.alternativeA"),pet.Settings.QuietMode,delegate(bool enabled){if(!refreshing)pet.SetQuietMode(enabled);});quiet.Name="QuietMode";behavior.Children.Add(quiet);behavior.Children.Add(Ui.Text(TextResources.Get("quietPanel.quietPanel.text.alternativeB"),12,Ui.Muted));
            fullscreen=Ui.Toggle(TextResources.Get("quietPanel.quietPanel.toggle.alternativeB"),pet.Settings.HideOnFullscreen,delegate(bool enabled){if(!refreshing){pet.Settings.HideOnFullscreen=enabled;pet.ApplySettings();}});fullscreen.Name="HideOnFullscreen";behavior.Children.Add(fullscreen);behavior.Children.Add(Ui.Text(TextResources.Get("quietPanel.quietPanel.text.alternativeC"),12,Ui.Muted));
            clickthrough=Ui.Toggle(TextResources.Get("quietPanel.quietPanel.toggle.alternativeC"),pet.Settings.ClickThrough,delegate(bool enabled){if(!refreshing)pet.SetClickThrough(enabled);});clickthrough.Name="ClickThrough";behavior.Children.Add(clickthrough);behavior.Children.Add(Ui.Text(TextResources.Get("quietPanel.quietPanel.text.alternativeD"),12,Ui.Muted));
            locked=Ui.Toggle(TextResources.Get("quietPanel.quietPanel.toggle.alternativeD"),pet.Settings.LockPosition,delegate(bool enabled){if(!refreshing){pet.Settings.LockPosition=enabled;pet.ApplySettings();}});locked.Name="LockPosition";behavior.Children.Add(locked);general.Children.Add(GlassSurface.Card(behavior));
            shortcuts=new ShortcutSettingsPanel(pet);
            var storage=new StackPanel();storage.Children.Add(Ui.Text(TextResources.Get("dataManagement.title"),16));history=Ui.Toggle(TextResources.Get("quietPanel.quietPanel.toggle.alternativeE"),pet.Settings.SaveChatHistory,delegate(bool enabled){if(!refreshing){pet.Settings.SaveChatHistory=enabled;pet.SaveSettings();}});history.Name="SaveChatHistory";storage.Children.Add(history);storage.Children.Add(Ui.Text(TextResources.Get("quietPanel.quietPanel.text.alternativeF"),12,Ui.Muted));
            storage.Children.Add(new DataManagementPanel(pet,delegate{shortcuts.DiscardPreferenceDrafts();if(preferencesRestored!=null)preferencesRestored();else RefreshState();}));
            pages=new UIElement[]{Ui.Scroll(general),shortcuts,Ui.Scroll(GlassSurface.Card(storage))};header.SelectionChanged+=index=>body.Content=pages[index];SelectSection(0);
        }
        public void SelectSection(int index){header.Select(index);}
        internal void ShowTarget(string name){SelectSection(name.StartsWith("Hotkey")||name.StartsWith("GuideShortcut")||name.StartsWith("SaveShortcut")?1:name=="SaveChatHistory"?2:0);}
        public void RefreshState(){refreshing=true;imageMotion.IsChecked=pet.Settings.ImageGenerationMotionEnabled;imageConcurrency.SelectedItem=pet.Settings.ImageGenerationConcurrency;theme.Value=pet.Settings.ThemeMode;quiet.IsChecked=pet.Settings.QuietMode;fullscreen.IsChecked=pet.Settings.HideOnFullscreen;clickthrough.IsChecked=pet.Settings.ClickThrough;locked.IsChecked=pet.Settings.LockPosition;history.IsChecked=pet.Settings.SaveChatHistory;refreshing=false;shortcuts.RefreshState();}
    }
}
