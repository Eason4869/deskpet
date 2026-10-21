using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    internal sealed class ShortcutDefinition
    {
        internal string Id,Group,Title,Default;internal bool Global;
        internal ShortcutDefinition(string id,string group,string title,string value,bool global=false){Id=id;Group=group;Title=title;Default=value;Global=global;}
    }
    internal static partial class ShortcutCatalog
    {
        internal static readonly ShortcutDefinition[] All={
            new ShortcutDefinition("quick","全局","快速对话","Ctrl+Alt+Space",true),
            new ShortcutDefinition("hide","全局","显示 / 隐藏伙伴","Ctrl+Alt+H",true),
            new ShortcutDefinition("quiet","全局","切换免打扰","Ctrl+Alt+D",true),
            new ShortcutDefinition("passthrough","全局","切换鼠标穿透","Ctrl+Alt+P",true),
            new ShortcutDefinition("newchat","文件","新聊天","Ctrl+N"),
            new ShortcutDefinition("temporary","文件","新建临时聊天","Ctrl+Shift+N"),
            new ShortcutDefinition("newproject","文件","新建项目…",""),
            new ShortcutDefinition("folder","文件","打开文件夹…","Ctrl+O"),
            new ShortcutDefinition("export","文件","导出当前会话…",""),
            new ShortcutDefinition("close","文件","关闭","Ctrl+W"),
            new ShortcutDefinition("quit","文件","退出桌宠","Ctrl+Q"),
            new ShortcutDefinition("settings","编辑","设置","Ctrl+OemComma"),
            new ShortcutDefinition("sidebar","视图","显示／隐藏侧边栏","Ctrl+Shift+S"),
            new ShortcutDefinition("summary","视图","显示／隐藏摘要面板",""),
            new ShortcutDefinition("terminal","视图","打开终端","Ctrl+Oem3"),
            new ShortcutDefinition("tab","视图","新建标签页","Ctrl+Shift+B"),
            new ShortcutDefinition("tools","视图","显示／隐藏工具标签","Ctrl+Alt+B"),
            new ShortcutDefinition("fulltab","视图","分屏／完整标签视图","Ctrl+Shift+F"),
            new ShortcutDefinition("sidechat","视图","打开侧边聊天","Ctrl+Alt+S"),
            new ShortcutDefinition("browser","视图","打开浏览器标签页","Ctrl+T"),
            new ShortcutDefinition("back","导航","返回","Alt+Left"),
            new ShortcutDefinition("forward","导航","前进","Alt+Right"),
            new ShortcutDefinition("focus","导航","聚焦主聊天输入框","Alt+L"),
            new ShortcutDefinition("docs","帮助","文档",""),
            new ShortcutDefinition("help","帮助","显示键盘快捷键","Ctrl+OemQuestion"),
            new ShortcutDefinition("news","帮助","新功能",""),
            new ShortcutDefinition("feedback","帮助","反馈",""),
            new ShortcutDefinition("guide","帮助","配置引导",""),
            new ShortcutDefinition("about","帮助","检查更新／关于桌面伙伴",""),
            new ShortcutDefinition("menufile","菜单","文件菜单","Alt+F"),
            new ShortcutDefinition("menuedit","菜单","编辑菜单","Alt+E"),
            new ShortcutDefinition("menuview","菜单","视图菜单","Alt+V"),
            new ShortcutDefinition("menuhelp","菜单","帮助菜单","Alt+H")
        };
        internal static Dictionary<string,string> Defaults(){return All.Where(d=>!d.Global).ToDictionary(d=>d.Id,d=>d.Default);}
        internal static string Value(PetSettings settings,ShortcutDefinition definition){string value;var source=definition.Global?settings.Hotkeys:settings.LocalShortcuts;return source!=null&&source.TryGetValue(definition.Id,out value)?value??"":definition.Default;}
        internal static string Label(string value)
        {
            var parts=(value??"").Split('+');
            for(int i=0;i<parts.Length;i++){string part=parts[i];if(part.Equals("OemComma",StringComparison.OrdinalIgnoreCase))parts[i]=",";else if(part.Equals("OemQuestion",StringComparison.OrdinalIgnoreCase))parts[i]="/";else if(part.Equals("Oem3",StringComparison.OrdinalIgnoreCase))parts[i]="`";else if(part=="Left")parts[i]="←";else if(part=="Right")parts[i]="→";else if(part.Length==2&&part[0]=='D'&&Char.IsDigit(part[1]))parts[i]=part.Substring(1);}
            return String.Join("+",parts);
        }
    }
}
