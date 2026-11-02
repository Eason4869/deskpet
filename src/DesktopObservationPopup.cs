using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Dafeiyu
{
    internal sealed class DesktopObservationPopup : Window
    {
        internal static Point ScreenCenter
        {
            get{var area=SystemParameters.WorkArea;return new Point(area.Left+area.Width/2,area.Top+area.Height/2);}
        }
        internal DesktopObservationPopup(string title,FrameworkElement content,double width,double height,Point center,bool passive)
        {
            var area=SystemParameters.WorkArea;
            width=Math.Min(width,Math.Max(1,area.Width-32));height=Math.Min(height,Math.Max(1,area.Height-32));
            Ui.Window(this,title,width,height);
            Name=passive?"DesktopObservationCountdown":"DesktopObservationPreviewWindow";
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=!passive;Topmost=passive;
            WindowStartupLocation=WindowStartupLocation.Manual;Left=center.X-width/2;Top=center.Y-height/2;
            var body=new DockPanel{LastChildFill=true};var header=new DockPanel{LastChildFill=true};
            var close=LocalizedUi.Button(()=>TextResources.Get("common.close"));close.Margin=new Thickness(12,0,0,0);close.Padding=new Thickness(12,5,12,5);close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            var caption=Ui.InlineText(title,14);caption.FontWeight=FontWeights.SemiBold;header.Children.Add(caption);
            header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs args){if(!passive && args.ChangedButton==MouseButton.Left)DragMove();};
            DockPanel.SetDock(header,Dock.Top);body.Children.Add(header);content.Margin=new Thickness(0,12,0,0);body.Children.Add(content);
            Content=Frame(body);
            KeyDown+=delegate(object sender,KeyEventArgs args){if(args.Key==Key.Escape)Close();};
        }
        internal static Border Frame(UIElement content)
        {
            return new Border{Margin=new Thickness(10),Padding=new Thickness(18),CornerRadius=new CornerRadius(16),Background=ThemeManager.Surface,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Child=content,Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=16,ShadowDepth=3,Opacity=.12}};
        }
    }
}
