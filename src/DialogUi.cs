using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Dafeiyu
{
    internal static class DialogUi
    {
        internal static void Apply(Window window,UIElement content)
        {
            window.WindowStyle=WindowStyle.None;window.ResizeMode=ResizeMode.NoResize;window.AllowsTransparency=true;window.Background=Brushes.Transparent;window.ShowInTaskbar=false;
            if(window.Owner==null&&Application.Current!=null){var owner=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w!=window&&w.IsActive&&w.IsVisible);if(owner!=null)window.Owner=owner;}if(window.Owner!=null)window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
            var overlay=new Grid();overlay.Children.Add(content);var close=UiIcons.Button("close",TextResources.Get("common.close"));close.Name="CloseDialog";close.Width=close.Height=28;close.MinHeight=28;close.Padding=new Thickness(6);close.Margin=new Thickness(0,14,14,0);close.VerticalAlignment=VerticalAlignment.Top;close.HorizontalAlignment=HorizontalAlignment.Right;close.Background=Brushes.Transparent;close.BorderBrush=Brushes.Transparent;close.Click+=delegate{window.Close();};overlay.Children.Add(close);
            var frame=new Border{Child=overlay,CornerRadius=new CornerRadius(20),Background=Ui.Brush("#FAFAFB"),BorderBrush=Ui.Brush("#DDDEE1"),BorderThickness=new Thickness(1)};frame.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.GetPosition(frame).Y<48&&e.OriginalSource is TextBlock){try{window.DragMove();}catch(InvalidOperationException){}}};window.Content=frame;
        }
    }
}
