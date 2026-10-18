using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed class GreetingBubbleWindow : Window
    {
        readonly PetWindow pet;
        readonly TextBlock name,message;
        readonly Polygon tail;
        readonly DispatcherTimer lifetime;
        public GreetingBubbleWindow(PetWindow companion)
        {
            pet=companion;Title="伙伴的问候";Icon=Ui.ApplicationIcon();Width=320;Height=166;
            AllowsTransparency=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            ShowInTaskbar=false;ShowActivated=false;Topmost=pet.Settings.Topmost;Background=Brushes.Transparent;
            FontFamily=new FontFamily("Microsoft YaHei UI");Foreground=Ui.Ink;
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(14)});
            var body=new Grid{Margin=new Thickness(17,12,17,10)};body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new DockPanel();var close=Ui.Button("×");close.Name="DismissGreeting";close.MinHeight=24;close.Padding=new Thickness(7,0,7,0);close.Margin=new Thickness(0);close.ToolTip="收起问候";close.Click+=delegate {Hide();};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            name=Ui.Text("",12,Ui.Blue);name.FontWeight=FontWeights.SemiBold;header.Children.Add(name);body.Children.Add(header);
            message=Ui.Text("",13);message.Name="GreetingMessage";message.VerticalAlignment=VerticalAlignment.Center;message.Margin=new Thickness(0,3,0,3);Grid.SetRow(message,1);body.Children.Add(message);
            var talk=Ui.Button("聊两句");talk.Name="GreetingQuickChat";talk.MinHeight=25;talk.FontSize=11;talk.Padding=new Thickness(10,2,10,2);talk.Margin=new Thickness(0);talk.HorizontalAlignment=HorizontalAlignment.Left;talk.Click+=delegate {Hide();pet.ShowQuickChat();};Grid.SetRow(talk,2);body.Children.Add(talk);
            root.Children.Add(new Border{Child=body,CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),BorderBrush=Ui.Line,Background=Brushes.White});
            tail=new Polygon{Fill=Brushes.White,Stroke=Ui.Line,StrokeThickness=1};Grid.SetRow(tail,1);root.Children.Add(tail);Content=root;
            lifetime=new DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};lifetime.Tick+=delegate {Hide();};
            SourceInitialized+=delegate {DesktopInterop.MakeToolWindow(new WindowInteropHelper(this).Handle);};
            IsVisibleChanged+=delegate {if(!IsVisible)lifetime.Stop();};Closed+=delegate {lifetime.Stop();};
        }
        public void ShowGreeting(string text)
        {
            name.Text=pet.Skin.Name+" · 打个招呼";message.Text=text;Topmost=pet.Settings.Topmost;
            if(!IsVisible)Show();FollowPet();lifetime.Stop();lifetime.Start();
        }
        public void FollowPet(){BubblePlacement.Follow(this,pet,tail);}
    }

    public static class BubblePlacement
    {
        public static void Follow(Window window,PetWindow pet,Polygon tail)
        {
            if(!window.IsVisible)return;
            var own=new WindowInteropHelper(window).Handle;var other=new WindowInteropHelper(pet).Handle;
            if(own==IntPtr.Zero || other==IntPtr.Zero)return;
            Rect p=DesktopInterop.Bounds(other),b=DesktopInterop.Bounds(own),area=DesktopInterop.WorkArea(other);double anchor=p.Left+p.Width*.5;
            Point position=PetMath.ClampPosition(anchor-b.Width*.5,p.Top+p.Height*.12-b.Height-7,b.Width,b.Height,area);
            if(Math.Abs(b.Left-position.X)>=.5 || Math.Abs(b.Top-position.Y)>=.5)DesktopInterop.Move(own,position);
            if(window.Topmost!=pet.Settings.Topmost)window.Topmost=pet.Settings.Topmost;
            var source=HwndSource.FromHwnd(own);double scale=source.CompositionTarget.TransformToDevice.M11;
            double x=Math.Max(20,Math.Min(window.Width-20,(anchor-position.X)/scale));
            // Follow runs for every pet frame. Stable placement must not rebuild
            // the tail geometry or issue another native window move.
            if(tail.Points.Count!=3 || Math.Abs(tail.Points[1].X-x)>=.01)
                tail.Points=new PointCollection(new[]{new Point(x-12,0),new Point(x,13),new Point(x+12,0)});
        }
    }
}
