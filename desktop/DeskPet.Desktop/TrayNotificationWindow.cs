using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DeskPet.Desktop;

internal sealed class TrayNotificationWindow : Window
{
    readonly DispatcherTimer lifetime=new();
    readonly Stopwatch elapsed=new();
    readonly ScrollViewer body;
    TimeSpan remaining=TimeSpan.FromSeconds(8);
    PixelRect workArea;
    double scaling=1;
    bool closed;

    internal TrayNotificationWindow(Func<string> title,Func<string> message,Func<string> actionLabel,Action open)
    {
        Name="TrayNotification";LocalizedUi.With(this,"Title",title);
        Width=360;MinWidth=280;MinHeight=0;SizeToContent=SizeToContent.Height;
        ShowActivated=false;ShowInTaskbar=false;Topmost=true;
        var root=new Grid{Margin=new Thickness(20),RowDefinitions=new RowDefinitions("Auto,Auto,Auto")};
        var header=new Grid{Margin=new Thickness(0,0,30,0),ColumnDefinitions=new ColumnDefinitions("22,*")};header.Children.Add(SettingsVisual.Icon("chat",20));
        var heading=SettingsVisual.Text(title,15);heading.Name="TrayNotificationTitle";heading.FontWeight=FontWeight.SemiBold;heading.TextWrapping=TextWrapping.NoWrap;heading.TextTrimming=TextTrimming.CharacterEllipsis;heading.Margin=new Thickness(10,0,0,0);heading.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(heading,1);header.Children.Add(heading);root.Children.Add(header);
        var copy=SettingsVisual.Text(message,13,true);copy.Name="TrayNotificationMessage";copy.TextWrapping=TextWrapping.Wrap;
        body=new ScrollViewer{Content=copy,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Margin=new Thickness(0,12,0,0),MaxHeight=240};Grid.SetRow(body,1);root.Children.Add(body);
        var action=SettingsVisual.Button(actionLabel,()=>{if(closed)return;Close();open();});action.Name="OpenTrayNotification";action.HorizontalAlignment=HorizontalAlignment.Right;action.Margin=new Thickness(0,16,0,0);Grid.SetRow(action,2);root.Children.Add(action);
        DialogUi.Apply(this,root);WindowStartupLocation=WindowStartupLocation.Manual;
        lifetime.Tick+=Dismiss;
        PropertyChanged+=(_,e)=>{if(e.Property==IsPointerOverProperty||e.Property==IsKeyboardFocusWithinProperty)UpdateLifetime();};
        SizeChanged+=(_,_)=>{if(IsVisible)Place();};
        Closed+=(_,_)=>{closed=true;lifetime.Stop();lifetime.Tick-=Dismiss;elapsed.Stop();};
    }

    internal void ShowNearTray(Window? anchor=null)
    {
        if(closed)return;
        var screen=anchor==null?Screens.Primary:Screens.ScreenFromWindow(anchor)??Screens.Primary;
        if(screen!=null){workArea=screen.WorkingArea;scaling=screen.Scaling;Position=new PixelPoint(workArea.X+12,workArea.Y+12);Fit();}
        if(!IsVisible)Show();
        // DialogUi fits when opened; apply selected-monitor limits afterward.
        Fit();UpdateLayout();Place();UpdateLifetime();
    }

    void Fit()
    {
        if(workArea.Width<=0)return;
        double width=Math.Max(1,workArea.Width/scaling-24),height=Math.Max(1,workArea.Height/scaling-24);
        MinWidth=Math.Min(280,width);MaxWidth=width;Width=Math.Min(360,width);MaxHeight=height;body.MaxHeight=Math.Min(240,Math.Max(1,height-140));
    }
    void Place()
    {
        if(workArea.Width<=0)return;
        int gap=(int)Math.Round(12*scaling),width=(int)Math.Ceiling(Bounds.Width*scaling),height=(int)Math.Ceiling(Bounds.Height*scaling);
        Position=new PixelPoint(Math.Max(workArea.X,workArea.Right-width-gap),Math.Max(workArea.Y,workArea.Bottom-height-gap));
    }
    void UpdateLifetime()
    {
        if(closed)return;
        lifetime.Stop();if(elapsed.IsRunning){remaining-=elapsed.Elapsed;elapsed.Reset();}
        if(!IsVisible||IsPointerOver||IsKeyboardFocusWithin)return;
        if(remaining<=TimeSpan.Zero){Close();return;}lifetime.Interval=remaining;elapsed.Restart();lifetime.Start();
    }
    void Dismiss(object? sender,EventArgs args){lifetime.Stop();if(IsPointerOver||IsKeyboardFocusWithin)UpdateLifetime();else Close();}
}
