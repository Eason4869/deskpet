using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu
{
    // Independent of the pet window: reminders must survive hidden/click-through pets.
    public sealed class TrayNotificationWindow : Window
    {
        readonly DispatcherTimer lifetime=new DispatcherTimer();
        readonly Stopwatch elapsed=new Stopwatch();
        readonly ScrollViewer body;
        TimeSpan remaining=TimeSpan.FromSeconds(8);
        Rect workArea;
        bool closed;

        public TrayNotificationWindow(Func<string> title,Func<string> message,Func<string> actionLabel,Action open)
        {
            Name="TrayNotification";LocalizedUi.Set(this,"Title",title);
            Width=360;MinWidth=280;MinHeight=0;SizeToContent=SizeToContent.Height;
            ShowActivated=false;ShowInTaskbar=false;Topmost=true;UseLayoutRounding=true;
            var root=new Grid{Margin=new Thickness(20)};
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid{Margin=new Thickness(0,0,30,0)};header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(22)});header.ColumnDefinitions.Add(new ColumnDefinition());
            var icon=UiIcons.Image("chat",Ui.Blue);icon.Width=icon.Height=20;header.Children.Add(icon);
            var heading=LocalizedUi.Text(title,15,Ui.Ink);heading.Name="TrayNotificationTitle";heading.FontWeight=FontWeights.SemiBold;heading.TextWrapping=TextWrapping.NoWrap;heading.TextTrimming=TextTrimming.CharacterEllipsis;heading.Margin=new Thickness(10,0,0,0);heading.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(heading,1);header.Children.Add(heading);root.Children.Add(header);
            var copy=LocalizedUi.Text(message,13,Ui.Muted);copy.Name="TrayNotificationMessage";copy.TextWrapping=TextWrapping.Wrap;copy.Margin=new Thickness(0);
            body=new ScrollViewer{Content=copy,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,12,0,0),MaxHeight=240};Grid.SetRow(body,1);root.Children.Add(body);
            var action=LocalizedUi.Button(actionLabel,true);action.Name="OpenTrayNotification";action.HorizontalAlignment=HorizontalAlignment.Right;action.Margin=new Thickness(0,16,0,0);action.Click+=delegate{if(closed)return;Close();if(open!=null)open();};Grid.SetRow(action,2);root.Children.Add(action);
            DialogUi.Apply(this,root);Owner=null;WindowStartupLocation=WindowStartupLocation.Manual;
            lifetime.Tick+=Dismiss;
            MouseEnter+=delegate{UpdateLifetime();};MouseLeave+=delegate{UpdateLifetime();};IsKeyboardFocusWithinChanged+=delegate{UpdateLifetime();};
            SizeChanged+=delegate{if(IsVisible)Place();};
            Closed+=delegate{closed=true;lifetime.Stop();lifetime.Tick-=Dismiss;elapsed.Stop();};
        }

        public void ShowNearTray(IntPtr anchor)
        {
            if(closed)return;
            workArea=anchor==IntPtr.Zero?DesktopInterop.WorkArea(DesktopInterop.Cursor()):DesktopInterop.WorkArea(anchor);
            var handle=new WindowInteropHelper(this).EnsureHandle();
            // Move the HWND to the selected monitor before measuring its DPI.
            DesktopInterop.Move(handle,new Point(workArea.Left+12,workArea.Top+12));
            var source=HwndSource.FromHwnd(handle);double scale=source.CompositionTarget.TransformToDevice.M11;
            double width=Math.Max(1,workArea.Width/scale-24),height=Math.Max(1,workArea.Height/scale-24);
            MinWidth=Math.Min(280,width);MaxWidth=width;Width=Math.Min(360,width);MaxHeight=height;body.MaxHeight=Math.Min(240,Math.Max(1,height-140));
            if(!IsVisible)Show();UpdateLayout();Place();UpdateLifetime();
        }

        void Place()
        {
            if(workArea.IsEmpty||workArea.Width<=0)return;
            var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero)return;
            var bounds=DesktopInterop.Bounds(handle);var source=HwndSource.FromHwnd(handle);double gap=12*source.CompositionTarget.TransformToDevice.M11;
            DesktopInterop.Move(handle,new Point(Math.Max(workArea.Left,workArea.Right-bounds.Width-gap),Math.Max(workArea.Top,workArea.Bottom-bounds.Height-gap)));
        }

        void UpdateLifetime()
        {
            if(closed)return;
            lifetime.Stop();if(elapsed.IsRunning){remaining-=elapsed.Elapsed;elapsed.Reset();}
            if(!IsVisible||IsMouseOver||IsKeyboardFocusWithin)return;
            if(remaining<=TimeSpan.Zero){Close();return;}lifetime.Interval=remaining;elapsed.Restart();lifetime.Start();
        }
        void Dismiss(object sender,EventArgs args){lifetime.Stop();if(IsMouseOver||IsKeyboardFocusWithin)UpdateLifetime();else Close();}
    }
}
