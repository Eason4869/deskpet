using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    internal sealed class ShortcutDefinition
    {
        internal string Id,Group,Default;internal bool Global;readonly Func<string> title;
        internal string Title {get{return title();}}
        internal string GroupTitle {get{return ShortcutCatalog.GroupTitle(Group);}}
        internal ShortcutDefinition(string id,string group,Func<string> title,string value,bool global=false){Id=id;Group=group;this.title=title;Default=value;Global=global;}
    }
    internal static partial class ShortcutCatalog
    {
        // Group remains a stable routing identity. Only this mapping supplies display copy.
        internal static string GroupTitle(string group)
        {
            if(group=="全局")return TextResources.Get("menus.groups.global");
            if(group=="文件")return TextResources.Get("menus.groups.file");
            if(group=="编辑")return TextResources.Get("menus.groups.edit");
            if(group=="视图")return TextResources.Get("menus.groups.view");
            if(group=="导航")return TextResources.Get("menus.groups.navigation");
            if(group=="帮助")return TextResources.Get("menus.groups.help");
            if(group=="菜单")return TextResources.Get("menus.groups.menu");
            return group;
        }
        internal static readonly ShortcutDefinition[] All={
            new ShortcutDefinition("quick","全局",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition"),"Ctrl+Alt+Space",true),
            new ShortcutDefinition("hide","全局",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeA"),"Ctrl+Alt+H",true),
            new ShortcutDefinition("quiet","全局",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeB"),"Ctrl+Alt+D",true),
            new ShortcutDefinition("passthrough","全局",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeC"),"Ctrl+Alt+P",true),
            new ShortcutDefinition("newchat","文件",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeD"),"Ctrl+N"),
            new ShortcutDefinition("temporary","文件",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeE"),"Ctrl+Shift+N"),
            new ShortcutDefinition("newproject","文件",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeF"),""),
            new ShortcutDefinition("folder","文件",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeG"),"Ctrl+O"),
            new ShortcutDefinition("export","文件",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeH"),""),
            new ShortcutDefinition("close","文件",()=>TextResources.Get("common.close"),"Ctrl+W"),
            new ShortcutDefinition("quit","文件",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeI"),"Ctrl+Q"),
            new ShortcutDefinition("settings","编辑",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeJ"),"Ctrl+OemComma"),
            new ShortcutDefinition("sidebar","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeK"),"Ctrl+Shift+S"),
            new ShortcutDefinition("summary","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeL"),""),
            new ShortcutDefinition("terminal","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeM"),"Ctrl+Oem3"),
            new ShortcutDefinition("tab","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeN"),"Ctrl+Shift+B"),
            new ShortcutDefinition("tools","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeO"),"Ctrl+Alt+B"),
            new ShortcutDefinition("fulltab","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeP"),"Ctrl+Shift+F"),
            new ShortcutDefinition("sidechat","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeQ"),"Ctrl+Alt+S"),
            new ShortcutDefinition("browser","视图",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeR"),"Ctrl+T"),
            new ShortcutDefinition("back","导航",()=>TextResources.Get("common.back"),"Alt+Left"),
            new ShortcutDefinition("forward","导航",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeS"),"Alt+Right"),
            new ShortcutDefinition("focus","导航",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeT"),"Alt+L"),
            new ShortcutDefinition("docs","帮助",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeU"),""),
            new ShortcutDefinition("help","帮助",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeV"),"Ctrl+OemQuestion"),
            new ShortcutDefinition("news","帮助",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeW"),""),
            new ShortcutDefinition("feedback","帮助",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeX"),""),
            new ShortcutDefinition("guide","帮助",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeY"),""),
            new ShortcutDefinition("about","帮助",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeZ"),""),
            new ShortcutDefinition("menufile","菜单",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeAA"),"Alt+F"),
            new ShortcutDefinition("menuedit","菜单",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeAB"),"Alt+E"),
            new ShortcutDefinition("menuview","菜单",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeAC"),"Alt+V"),
            new ShortcutDefinition("menuhelp","菜单",()=>TextResources.Get("shortcutDefinitions.all.allShortcutDefinition.alternativeAD"),"Alt+H")
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
