using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Dafeiyu
{
    public sealed class UpdateNoticeWindow : Window
    {
        readonly PetWindow pet;
        readonly UpdateCoordinator updates;
        readonly TextBlock message;
        readonly Button cancel;
        readonly Polygon tail;
        public UpdateNoticeWindow(PetWindow companion,UpdateCoordinator coordinator)
        {
            pet=companion;updates=coordinator;Title="伙伴更新";Icon=Ui.ApplicationIcon();Width=340;Height=164;
            AllowsTransparency=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            ShowInTaskbar=false;ShowActivated=false;Background=Brushes.Transparent;
            FontFamily=new FontFamily("Microsoft YaHei UI");Foreground=Ui.Ink;UseLayoutRounding=true;
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(14)});
            var body=new StackPanel{Margin=new Thickness(18,14,18,12)};
            var title=Ui.Text("新版已就绪",14,Ui.Blue);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
            message=Ui.Text("",12);message.Name="UpdateCountdown";message.Margin=new Thickness(0,8,0,10);body.Children.Add(message);
            cancel=Ui.Button("取消本次更新");cancel.Name="CancelPendingUpdate";cancel.MinHeight=28;cancel.Padding=new Thickness(12,3,12,3);cancel.Margin=new Thickness(0);cancel.HorizontalAlignment=HorizontalAlignment.Left;cancel.Click+=delegate {updates.Cancel();};body.Children.Add(cancel);
            root.Children.Add(new Border{Child=body,CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),BorderBrush=Ui.Line,Background=ThemeManager.Surface});
            tail=new Polygon{Fill=ThemeManager.Surface,Stroke=Ui.Line,StrokeThickness=1};Grid.SetRow(tail,1);root.Children.Add(tail);Content=root;
            SourceInitialized+=delegate {DesktopInterop.MakeToolWindow(new WindowInteropHelper(this).Handle);};
        }
        public void Refresh()
        {
            if(!updates.HasRestartCountdown && !updates.IsApplying){Hide();return;}
            message.Text=updates.IsApplying?"正在准备安装和重启，设置与外观会保留。":updates.RestartSeconds+" 秒后安装并重启，设置与外观会保留。";
            cancel.IsEnabled=updates.CanCancel;if(!IsVisible)Show();FollowPet();
        }
        public void FollowPet(){BubblePlacement.Follow(this,pet,tail);Topmost=true;}
    }
}
