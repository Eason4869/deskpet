using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Dafeiyu
{
    internal static class MarkdownLinkMenu
    {
        internal static ContextMenu Create(Hyperlink link)
        {
            Uri uri=link.NavigateUri;bool web=uri.Scheme=="http"||uri.Scheme=="https";
            var menu=ChatUi.Menu();
            var side=new MenuItem{Name="OpenLinkInSideTab",Header=TextResources.Get("markdownLinkMenu.create.header"),Icon=UiIcons.Image("globe",Ui.Ink),IsEnabled=web};
            side.Click+=delegate
            {
                var host=Window.GetWindow(link);var chat=host as ChatWindow;var quick=host as QuickChatWindow;
                if(chat!=null)chat.OpenBrowserLink(uri);else if(quick!=null)quick.OpenBrowserLink(uri);
            };
            var external=new MenuItem{Name="OpenLinkExternally",Header=web?TextResources.Get("markdownLinkMenu.create.headerWeb"):TextResources.Get("markdownLinkMenu.create.headerWeb.alternativeA"),Icon=UiIcons.Image("expand",Ui.Ink)};
            external.Click+=delegate{try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}};
            var copy=new MenuItem{Name="CopyLinkAddress",Header=TextResources.Get("markdownLinkMenu.create.header.alternativeA"),Icon=UiIcons.Image("copy",Ui.Ink)};
            copy.Click+=delegate{try{Clipboard.SetText(uri.AbsoluteUri);}catch(System.Runtime.InteropServices.COMException){}};
            menu.Items.Add(side);menu.Items.Add(external);menu.Items.Add(new Separator());menu.Items.Add(copy);
            menu.Opened+=delegate{var host=Window.GetWindow(link);side.IsEnabled=web&&(host is ChatWindow||host is QuickChatWindow);};
            return menu;
        }
    }
}
