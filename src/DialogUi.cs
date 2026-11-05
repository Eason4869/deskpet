using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
namespace Dafeiyu
{
    internal static class DialogUi
    {
        internal static void Apply(Window window,UIElement content,bool resizable=false)
        {
            window.WindowStyle=WindowStyle.None;window.ResizeMode=resizable?ResizeMode.CanResizeWithGrip:ResizeMode.NoResize;window.AllowsTransparency=true;window.Background=Brushes.Transparent;window.ShowInTaskbar=false;
            window.FontFamily=new FontFamily("Microsoft YaHei UI");window.FontSize=13;window.Foreground=Ui.Ink;SlimScrollBars.Install(window);
            if(window.Owner==null&&Application.Current!=null){var owner=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w!=window&&w.IsActive&&w.IsVisible);if(owner!=null)window.Owner=owner;}if(window.Owner!=null)window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
            window.SourceInitialized+=delegate{FitToMonitor(window,window.Owner??window);};window.Loaded+=delegate{FitToMonitor(window,window);};
            var overlay=new Grid();overlay.Children.Add(content);var close=UiIcons.Button("close",()=>(TextResources.Get("common.close")));close.Name="CloseDialog";close.Width=close.Height=28;close.MinHeight=28;close.Padding=new Thickness(6);close.Margin=new Thickness(0,14,14,0);close.VerticalAlignment=VerticalAlignment.Top;close.HorizontalAlignment=HorizontalAlignment.Right;close.Background=Brushes.Transparent;close.BorderBrush=Brushes.Transparent;close.Click+=delegate{window.Close();};overlay.Children.Add(close);
            var frame=new Border{Child=overlay,CornerRadius=new CornerRadius(20),Background=Ui.Brush("#FAFAFB"),BorderBrush=Ui.Brush("#DDDEE1"),BorderThickness=new Thickness(1)};frame.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.GetPosition(frame).Y<48&&e.OriginalSource is TextBlock){try{window.DragMove();}catch(InvalidOperationException){}}};window.Content=frame;
            window.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&!e.Handled){e.Handled=true;window.Close();}};
        }
        static void FitToMonitor(Window window,Window target)
        {
            var handle=new WindowInteropHelper(target).Handle;if(handle==IntPtr.Zero)return;
            var source=PresentationSource.FromVisual(target);double scale=source==null||source.CompositionTarget==null?1:source.CompositionTarget.TransformToDevice.M11;
            FitToWorkArea(window,DesktopInterop.WorkArea(handle),scale);
        }
        internal static void FitToWorkArea(Window window,Rect physicalWorkArea,double scale)
        {
            if(physicalWorkArea.IsEmpty||scale<=0)return;
            double width=Math.Max(1,physicalWorkArea.Width/scale-24),height=Math.Max(1,physicalWorkArea.Height/scale-24);
            window.MinWidth=Math.Min(window.MinWidth,width);window.MinHeight=Math.Min(window.MinHeight,height);window.MaxWidth=Math.Min(window.MaxWidth,width);window.MaxHeight=Math.Min(window.MaxHeight,height);
            if(!Double.IsNaN(window.Width))window.Width=Math.Min(window.Width,window.MaxWidth);if(!Double.IsNaN(window.Height))window.Height=Math.Min(window.Height,window.MaxHeight);
        }
        internal static void ApplyTitled(Window window,UIElement content,bool resizable=false)
        {
            var root=new Grid{Margin=new Thickness(24)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
            var heading=LocalizedUi.Text(()=>window.Title,18,Ui.Ink);heading.FontWeight=FontWeights.SemiBold;heading.LineHeight=26;heading.Margin=new Thickness(0,0,32,16);root.Children.Add(heading);
            Grid.SetRow(content,1);root.Children.Add(content);Apply(window,root,resizable);
        }
        internal static void Alert(string message,Func<string> title)
        {
            var window=new Window{Name="ApplicationAlert",SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterScreen};LocalizedUi.Window(window,title,520,230);window.MinHeight=0;
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.Children.Add(Ui.Scroll(Ui.Text(message,13,Ui.Muted)));var close=LocalizedUi.Button(()=>TextResources.Get("common.close"));close.Name="CloseApplicationAlert";close.IsCancel=close.IsDefault=true;close.HorizontalAlignment=HorizontalAlignment.Right;close.Margin=new Thickness(0,18,0,0);close.Click+=delegate{window.Close();};Grid.SetRow(close,1);root.Children.Add(close);
            ApplyTitled(window,root);window.Loaded+=delegate{close.Focus();};window.ShowDialog();
        }
    }
}
