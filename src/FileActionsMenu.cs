using System;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal static class FileActionsMenu
    {
        internal static ContextMenu Create(Action open,Action openWith,Action reveal,Action copyPath,Action copyContent,Action saveAs,bool available=true,bool text=true)
        {
            var menu=ChatUi.Menu();Add(menu,"打开文件","file",open,available);Add(menu,"打开方式…","more",openWith,available);Add(menu,"在资源管理器中打开","folder-open",reveal,available);Add(menu,"复制路径","copy",copyPath,true);Add(menu,"复制文件内容","copy",copyContent,available&&text);Add(menu,"另存为…","file",saveAs,available);return menu;
        }
        static void Add(ContextMenu menu,string title,string icon,Action action,bool enabled){var item=new MenuItem{Header=title,Icon=UiIcons.Image(icon,AgentUi.Muted),IsEnabled=enabled};item.Click+=delegate{action();};menu.Items.Add(item);}
    }
}
