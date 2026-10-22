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
        public QuietPanel(PetWindow owner)
        {
            pet=owner;Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());
            header=new SettingsPageHeader("通用","设置外观与交互、快捷键，以及本机聊天记录。",new[]{"外观与交互","快捷键","聊天记录"},new[]{"GeneralAppearanceTab","GeneralShortcutsTab","GeneralHistoryTab"}){Margin=new Thickness(0,0,0,18)};Children.Add(header);Grid.SetRow(body,1);Children.Add(body);
            var general=new StackPanel();var appearance=new StackPanel();appearance.Children.Add(Ui.Text("视觉风格",16));
            theme=new ThemeModePicker(pet.Settings.ThemeMode){Margin=new Thickness(0,12,0,16)};theme.Changed+=delegate(string mode){if(refreshing)return;pet.Settings.ThemeMode=mode;ThemeManager.Apply(mode);pet.SaveSettings();};appearance.Children.Add(theme);
            imageMotion=Ui.Toggle("图片生成动效",pet.Settings.ImageGenerationMotionEnabled,delegate(bool enabled){if(!refreshing){pet.Settings.ImageGenerationMotionEnabled=enabled;pet.SaveSettings();}});imageMotion.Name="ImageGenerationMotionEnabled";appearance.Children.Add(imageMotion);
            imageConcurrency=new ComboBox{Name="ImageGenerationConcurrency",ItemsSource=new[]{1,2,3,4},SelectedItem=pet.Settings.ImageGenerationConcurrency,MinHeight=36};imageConcurrency.SelectionChanged+=delegate{if(!refreshing&&imageConcurrency.SelectedItem is int){pet.Settings.ImageGenerationConcurrency=(int)imageConcurrency.SelectedItem;pet.ImageTasks.Concurrency=pet.Settings.ImageGenerationConcurrency;pet.SaveSettings();}};Ui.Label(appearance,"图片生成并发（同一服务最多 1 项）",imageConcurrency);general.Children.Add(appearance);
            var behavior=new StackPanel{Margin=new Thickness(0,8,0,0)};
            quiet=Ui.Toggle("免打扰",pet.Settings.QuietMode,delegate(bool enabled){if(!refreshing)pet.SetQuietMode(enabled);});quiet.Name="QuietMode";behavior.Children.Add(quiet);behavior.Children.Add(Ui.Text("暂停主动问候、散步和回复自动朗读；保留朗读偏好与正在进行的 Live。",12,Ui.Muted));
            fullscreen=Ui.Toggle("全屏应用时自动隐藏",pet.Settings.HideOnFullscreen,delegate(bool enabled){if(!refreshing){pet.Settings.HideOnFullscreen=enabled;pet.ApplySettings();}});fullscreen.Name="HideOnFullscreen";behavior.Children.Add(fullscreen);behavior.Children.Add(Ui.Text("只检测伙伴所在屏幕。退出全屏后恢复；手动隐藏后需手动找回。",12,Ui.Muted));
            clickthrough=Ui.Toggle("鼠标穿透伙伴",pet.Settings.ClickThrough,delegate(bool enabled){if(!refreshing)pet.SetClickThrough(enabled);});clickthrough.Name="ClickThrough";behavior.Children.Add(clickthrough);behavior.Children.Add(Ui.Text("开启后用托盘菜单或快捷键恢复鼠标交互。",12,Ui.Muted));
            locked=Ui.Toggle("锁定伙伴位置",pet.Settings.LockPosition,delegate(bool enabled){if(!refreshing){pet.Settings.LockPosition=enabled;pet.ApplySettings();}});locked.Name="LockPosition";behavior.Children.Add(locked);general.Children.Add(GlassSurface.Card(behavior));
            shortcuts=new ShortcutSettingsPanel(pet);
            var storage=new StackPanel();storage.Children.Add(Ui.Text("聊天记录",16));history=Ui.Toggle("保存聊天记录",pet.Settings.SaveChatHistory,delegate(bool enabled){if(!refreshing){pet.Settings.SaveChatHistory=enabled;pet.SaveSettings();}});history.Name="SaveChatHistory";storage.Children.Add(history);storage.Children.Add(Ui.Text("按伙伴保存到本机，使用当前 Windows 用户加密。关闭后停止保存，已有记录保留；在完整对话窗口导出或清空当前伙伴记录。",12,Ui.Muted));
            pages=new UIElement[]{Ui.Scroll(general),shortcuts,Ui.Scroll(GlassSurface.Card(storage))};header.SelectionChanged+=index=>body.Content=pages[index];SelectSection(0);
        }
        public void SelectSection(int index){header.Select(index);}
        internal void ShowTarget(string name){SelectSection(name.StartsWith("Hotkey")||name.StartsWith("GuideShortcut")||name.StartsWith("SaveShortcut")?1:name=="SaveChatHistory"?2:0);}
        public void RefreshState(){refreshing=true;imageMotion.IsChecked=pet.Settings.ImageGenerationMotionEnabled;imageConcurrency.SelectedItem=pet.Settings.ImageGenerationConcurrency;theme.Value=pet.Settings.ThemeMode;quiet.IsChecked=pet.Settings.QuietMode;fullscreen.IsChecked=pet.Settings.HideOnFullscreen;clickthrough.IsChecked=pet.Settings.ClickThrough;locked.IsChecked=pet.Settings.LockPosition;history.IsChecked=pet.Settings.SaveChatHistory;refreshing=false;shortcuts.RefreshState();}
    }
}
