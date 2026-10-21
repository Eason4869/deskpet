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
            var side=new MenuItem{Name="OpenLinkInSideTab",Header="在侧边标签页打开",Icon=UiIcons.Image("globe",Ui.Ink),IsEnabled=web};
            side.Click+=delegate
            {
                var host=Window.GetWindow(link);var chat=host as ChatWindow;var quick=host as QuickChatWindow;
                if(chat!=null)chat.OpenBrowserLink(uri);else if(quick!=null)quick.OpenBrowserLink(uri);
            };
            var external=new MenuItem{Name="OpenLinkExternally",Header=web?"在默认浏览器打开":"使用默认应用打开",Icon=UiIcons.Image("expand",Ui.Ink)};
            external.Click+=delegate{try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}};
            var copy=new MenuItem{Name="CopyLinkAddress",Header="复制链接",Icon=UiIcons.Image("copy",Ui.Ink)};
            copy.Click+=delegate{try{Clipboard.SetText(uri.AbsoluteUri);}catch(System.Runtime.InteropServices.COMException){}};
            menu.Items.Add(side);menu.Items.Add(external);menu.Items.Add(new Separator());menu.Items.Add(copy);
            menu.Opened+=delegate{var host=Window.GetWindow(link);side.IsEnabled=web&&(host is ChatWindow||host is QuickChatWindow);};
            return menu;
        }
    }
}
