using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Dafeiyu;

namespace DeskPet.Desktop;

internal static class DialogUi
{
    static void Prepare(Window window,bool resizable)
    {
        window.WindowDecorations=WindowDecorations.None;window.CanResize=resizable;window.CanMaximize=false;window.CanMinimize=false;window.ShowInTaskbar=false;
        window.TransparencyLevelHint=new[]{WindowTransparencyLevel.Transparent};window.Background=Brushes.Transparent;window.Foreground=SettingsVisual.Ink;
    }

    // This factory is used for auxiliary application windows whose callers may
    // finish constructing Content before Show. Frame that final content once.
    internal static void Install(Window window)
    {
        Prepare(window,true);
        window.Opened+=(_,_)=>{
            if(window.Content is Border {Name:"ApplicationDialogFrame"})return;
            if(window.Content is Control content){window.Content=null;ApplyTitled(window,content,true);}
        };
    }

    internal static void Apply(Window window,Control content,bool resizable=false)
    {
        Prepare(window,resizable);
        var overlay=new Grid();overlay.Children.Add(content);
        var close=LocalizedUi.Button(()=>TextResources.Get("common.close"),window.Close);close.Name="CloseDialog";close.Width=close.Height=close.MinHeight=28;close.Padding=new Thickness(6);close.Margin=new Thickness(0,14,14,0);close.HorizontalAlignment=HorizontalAlignment.Right;close.VerticalAlignment=VerticalAlignment.Top;close.Background=Brushes.Transparent;close.BorderBrush=Brushes.Transparent;close.BorderThickness=new Thickness(0);
        var caption=close.Content;close.Content=new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse("M4,4 L16,16 M16,4 L4,16"),Width=16,Height=16,Stretch=Stretch.Uniform,Stroke=SettingsVisual.Muted,StrokeThickness=1.5};
        LiveTextBindings.TakeRender(close,"Content",caption);LocalizedUi.Automation(close,()=>TextResources.Get("common.close"));LocalizedUi.Tooltip(close,()=>TextResources.Get("common.close"));overlay.Children.Add(close);
        var frame=new Border{Name="ApplicationDialogFrame",Child=overlay,CornerRadius=new CornerRadius(20),Background=DesktopTheme.Brush("#FAFAFB"),BorderBrush=DesktopTheme.Brush("#DDDEE1"),BorderThickness=new Thickness(1)};
        frame.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed&&e.GetPosition(frame).Y<48&&e.Source is TextBlock)window.BeginMoveDrag(e);};window.Content=frame;
        window.KeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!e.Handled){e.Handled=true;window.Close();}};
        void Fit(){var screen=window.Screens.ScreenFromWindow(window)??window.Screens.Primary;if(screen==null)return;double width=Math.Max(1,screen.WorkingArea.Width/screen.Scaling-24),height=Math.Max(1,screen.WorkingArea.Height/screen.Scaling-24);window.MinWidth=Math.Min(window.MinWidth,width);window.MinHeight=Math.Min(window.MinHeight,height);window.MaxWidth=Math.Min(window.MaxWidth,width);window.MaxHeight=Math.Min(window.MaxHeight,height);if(!double.IsNaN(window.Width))window.Width=Math.Min(window.Width,window.MaxWidth);if(!double.IsNaN(window.Height))window.Height=Math.Min(window.Height,window.MaxHeight);}
        if(window.IsVisible)Fit();else window.Opened+=(_,_)=>Fit();
    }

    internal static void ApplyTitled(Window window,Control content,bool resizable=false)
    {
        var root=new Grid{Margin=new Thickness(24),RowDefinitions=new RowDefinitions("Auto,*")};
        var heading=SettingsVisual.Text(()=>window.Title??"",18);heading.FontWeight=FontWeight.SemiBold;heading.LineHeight=26;heading.Margin=new Thickness(0,0,32,16);root.Children.Add(heading);Grid.SetRow(content,1);root.Children.Add(content);Apply(window,root,resizable);
    }
}
