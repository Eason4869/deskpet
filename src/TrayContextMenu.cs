using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Dafeiyu
{
    // A tray popup has its own HWND, so the companion can remain hidden or click-through.
    internal sealed class TrayContextMenu:ContextMenu,IDisposable
    {
        bool disposed;
        internal event EventHandler Opening;
        internal bool Visible{get{return IsOpen;}}
        internal bool Enabled{get{return IsEnabled;}set{IsEnabled=value;if(!value)IsOpen=false;}}
        internal TrayContextMenu()
        {
            var style=ChatUi.Menu();Name="TrayContextMenu";Template=style.Template;Resources=style.Resources;Background=style.Background;Foreground=style.Foreground;FontFamily=style.FontFamily;FontSize=style.FontSize;Padding=style.Padding;MinWidth=220;StaysOpen=false;
            Opened+=delegate
            {
                var opening=Opening;if(opening!=null)opening(this,EventArgs.Empty);
                Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate{if(disposed||!IsOpen)return;DesktopInterop.ForegroundMenu(this);Focus();}));
            };
        }
        internal void ShowMenu(Point? position=null)
        {
            if(disposed||!Enabled)return;
            Placement=position.HasValue?PlacementMode.AbsolutePoint:PlacementMode.MousePoint;
            if(position.HasValue){HorizontalOffset=position.Value.X;VerticalOffset=position.Value.Y;}else{HorizontalOffset=VerticalOffset=0;}
            IsOpen=true;
        }
        public void Dispose(){if(disposed)return;disposed=true;IsOpen=false;Opening=null;Items.Clear();}
    }
}
