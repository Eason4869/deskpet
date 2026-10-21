using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    public sealed class QuietPanel : ScrollViewer
    {
        readonly PetWindow pet;
        readonly ThemeModePicker theme;
        readonly CheckBox quiet,fullscreen,clickthrough,locked,history;
        readonly Dictionary<string,TextBox> fields=new Dictionary<string,TextBox>();
        readonly TextBlock notice;
        bool refreshing;
        public bool HasUnsavedChanges {get{foreach(var pair in fields)if(pair.Value.Text!=pet.Settings.Hotkeys[pair.Key])return true;return false;}}
        public QuietPanel(PetWindow owner)
        {
            pet=owner;VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
            var root=new StackPanel{Margin=new Thickness(24,22,24,24)};root.Children.Add(Ui.Text("通用",25));
            root.Children.Add(Ui.Text("设置伙伴的显示、交互、全局快捷键与本地记录。",12,Ui.Muted));
            var appearance=new StackPanel{Margin=new Thickness(0,18,0,12)};appearance.Children.Add(Ui.Text("视觉风格",16));
            theme=new ThemeModePicker(pet.Settings.ThemeMode){Margin=new Thickness(0,12,0,0)};
            theme.Changed+=delegate(string mode){if(refreshing)return;pet.Settings.ThemeMode=mode;ThemeManager.Apply(mode);pet.SaveSettings();};
            appearance.Children.Add(theme);root.Children.Add(appearance);
            var behavior=new StackPanel{Margin=new Thickness(0,14,0,0)};
            quiet=Ui.Toggle("免打扰",pet.Settings.QuietMode,delegate(bool enabled){if(!refreshing)pet.SetQuietMode(enabled);});quiet.Name="QuietMode";behavior.Children.Add(quiet);
            behavior.Children.Add(Ui.Text("暂停主动问候、散步和回复自动朗读；保留朗读偏好与正在进行的 Live。",12,Ui.Muted));
            fullscreen=Ui.Toggle("全屏应用时自动隐藏",pet.Settings.HideOnFullscreen,delegate(bool enabled){if(!refreshing){pet.Settings.HideOnFullscreen=enabled;pet.ApplySettings();}});fullscreen.Name="HideOnFullscreen";behavior.Children.Add(fullscreen);
            behavior.Children.Add(Ui.Text("只检测伙伴所在屏幕。退出全屏后恢复；手动隐藏后需手动找回。",12,Ui.Muted));
            clickthrough=Ui.Toggle("鼠标穿透伙伴",pet.Settings.ClickThrough,delegate(bool enabled){if(!refreshing)pet.SetClickThrough(enabled);});clickthrough.Name="ClickThrough";behavior.Children.Add(clickthrough);
            behavior.Children.Add(Ui.Text("开启后用托盘菜单或快捷键恢复鼠标交互。",12,Ui.Muted));
            locked=Ui.Toggle("锁定伙伴位置",pet.Settings.LockPosition,delegate(bool enabled){if(!refreshing){pet.Settings.LockPosition=enabled;pet.ApplySettings();}});locked.Name="LockPosition";behavior.Children.Add(locked);
            root.Children.Add(GlassSurface.Card(behavior));
            var shortcuts=new StackPanel();shortcuts.Children.Add(Ui.Text("全局快捷键",16));shortcuts.Children.Add(Ui.Text("可编辑组合键；留空并保存可停用。冲突时保留原快捷键。",12,Ui.Muted));
            AddShortcut(shortcuts,"quick","快速对话");AddShortcut(shortcuts,"hide","显示 / 隐藏伙伴");AddShortcut(shortcuts,"quiet","切换免打扰");AddShortcut(shortcuts,"passthrough","切换鼠标穿透");root.Children.Add(GlassSurface.Card(shortcuts));
            var storage=new StackPanel();storage.Children.Add(Ui.Text("聊天记录",16));history=Ui.Toggle("保存聊天记录",pet.Settings.SaveChatHistory,delegate(bool enabled){if(!refreshing){pet.Settings.SaveChatHistory=enabled;pet.SaveSettings();}});history.Name="SaveChatHistory";storage.Children.Add(history);
            storage.Children.Add(Ui.Text("按伙伴保存到本机，使用当前 Windows 用户加密。关闭后停止保存，已有记录保留；在完整对话窗口导出或清空当前伙伴记录。",12,Ui.Muted));root.Children.Add(GlassSurface.Card(storage));
            notice=Ui.Text(pet.HotkeyWarning ?? "",12,Ui.Muted);root.Children.Add(notice);Content=root;
        }
        void AddShortcut(Panel panel,string action,string label)
        {
            var row=new DockPanel{Name="GuideShortcut"+action,Margin=new Thickness(0,12,0,0)};var save=Ui.Button("保存");save.Name="SaveShortcut"+action;DockPanel.SetDock(save,Dock.Right);row.Children.Add(save);
            var caption=Ui.Text(label,12);caption.Width=135;caption.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(caption,Dock.Left);row.Children.Add(caption);
            var field=Ui.Field(pet.Settings.Hotkeys[action]);field.Name="Hotkey"+action;field.MaxLength=64;fields[action]=field;row.Children.Add(field);
            save.Click+=delegate {string error;if(pet.UpdateHotkey(action,field.Text,out error)){field.Text=pet.Settings.Hotkeys[action];notice.Text=String.IsNullOrWhiteSpace(field.Text)?"已停用该快捷键。":"快捷键已保存。";}else{notice.Text=error;}};panel.Children.Add(row);
        }
        public void RefreshState()
        {
            refreshing=true;theme.Value=pet.Settings.ThemeMode;quiet.IsChecked=pet.Settings.QuietMode;fullscreen.IsChecked=pet.Settings.HideOnFullscreen;clickthrough.IsChecked=pet.Settings.ClickThrough;locked.IsChecked=pet.Settings.LockPosition;history.IsChecked=pet.Settings.SaveChatHistory;refreshing=false;
        }
    }
}
