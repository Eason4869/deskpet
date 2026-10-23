using System;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal static class FileActionsMenu
    {
        internal static ContextMenu Create(Action open,Action openWith,Action reveal,Action copyPath,Action copyContent,Action saveAs,bool available=true,bool text=true)
        {
            var menu=ChatUi.Menu();Add(menu,TextResources.Get("fileActionsMenu.create.text"),"file",open,available);Add(menu,TextResources.Get("fileActionsMenu.create.text.alternativeA"),"more",openWith,available);Add(menu,TextResources.Get("fileActionsMenu.create.text.alternativeB"),"folder-open",reveal,available);Add(menu,TextResources.Get("fileActionsMenu.create.text.alternativeC"),"copy",copyPath,true);Add(menu,TextResources.Get("fileActionsMenu.create.text.alternativeD"),"copy",copyContent,available&&text);Add(menu,TextResources.Get("fileActionsMenu.create.text.alternativeE"),"file",saveAs,available);return menu;
        }
        static void Add(ContextMenu menu,string title,string icon,Action action,bool enabled){var item=new MenuItem{Header=title,Icon=UiIcons.Image(icon,AgentUi.Muted),IsEnabled=enabled};item.Click+=delegate{action();};menu.Items.Add(item);}
    }
}
