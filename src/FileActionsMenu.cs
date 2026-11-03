using System;
using System.Collections.Generic;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal static class FileActionsMenu
    {
        internal static ContextMenu Create(Action open,Action openWith,Action reveal,Action copyPath,Action copyContent,Action saveAs,bool available=true,bool text=true,Func<IList<OpenWithApplication>> applications=null,Action<OpenWithApplication> openApplication=null)
        {
            var menu=ChatUi.Menu();Add(menu,()=>TextResources.Get("fileActionsMenu.create.text"),"file",open,available);
            var with=LocalizedUi.With(new MenuItem{IsEnabled=available,Icon=UiIcons.Image("more",AgentUi.Muted)},"Header",()=>TextResources.Get("fileActionsMenu.create.text.alternativeA"));
            Action populate=delegate{
                with.Items.Clear();
                if(applications!=null&&openApplication!=null)foreach(var application in applications())
                {
                    var selected=application;var choice=new MenuItem{Header=selected.Name};
                    choice.Click+=delegate(object sender,System.Windows.RoutedEventArgs e){e.Handled=true;openApplication(selected);};with.Items.Add(choice);
                }
                if(with.Items.Count>0)with.Items.Add(new Separator());
                var other=LocalizedUi.With(new MenuItem(),"Header",()=>TextResources.Get("fileActionsMenu.chooseApplication"));
                other.Click+=delegate(object sender,System.Windows.RoutedEventArgs e){e.Handled=true;openWith();};with.Items.Add(other);
            };
            // Seed one item so WPF displays the submenu arrow before discovery.
            var initial=LocalizedUi.With(new MenuItem(),"Header",()=>TextResources.Get("fileActionsMenu.chooseApplication"));initial.Click+=delegate{openWith();};with.Items.Add(initial);
            with.SubmenuOpened+=delegate(object sender,System.Windows.RoutedEventArgs e){if(Object.ReferenceEquals(e.OriginalSource,with))populate();};menu.Items.Add(with);
            Add(menu,()=>TextResources.Get("fileActionsMenu.create.text.alternativeB"),"folder-open",reveal,available);Add(menu,()=>TextResources.Get("fileActionsMenu.create.text.alternativeC"),"copy",copyPath,true);Add(menu,()=>TextResources.Get("fileActionsMenu.create.text.alternativeD"),"copy",copyContent,available&&text);Add(menu,()=>TextResources.Get("fileActionsMenu.create.text.alternativeE"),"file",saveAs,available);return menu;
        }
        static void Add(ContextMenu menu,Func<string> title,string icon,Action action,bool enabled){var item=LocalizedUi.With(new MenuItem{Header=title(),Icon=UiIcons.Image(icon,AgentUi.Muted),IsEnabled=enabled},"Header",title);item.Click+=delegate{action();};menu.Items.Add(item);}
    }
}
