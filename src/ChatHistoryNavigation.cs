using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        readonly Grid historyOverlay=new Grid{VerticalAlignment=VerticalAlignment.Bottom,MaxWidth=920,Margin=new Thickness(0,0,18,12)};
        readonly Button jumpToLatest=UiIcons.Button("arrow-down",TextResources.Get("chatHistoryNavigation.jumpToLatest.uiIcons"));
        void InitializeHistoryNavigation()
        {
            jumpToLatest.Name="JumpToLatestMessage";jumpToLatest.Width=jumpToLatest.Height=jumpToLatest.MinHeight=34;jumpToLatest.Padding=new Thickness(8);jumpToLatest.Margin=new Thickness(0);jumpToLatest.HorizontalAlignment=HorizontalAlignment.Center;jumpToLatest.ToolTip=TextResources.Get("chatHistoryNavigation.initializeHistoryNavigation.jumpToLatestToolTip");jumpToLatest.Visibility=Visibility.Collapsed;
            jumpToLatest.Background=ThemeManager.Surface;jumpToLatest.BorderBrush=Ui.Brush("#E2E4E8");jumpToLatest.BorderThickness=new Thickness(1);jumpToLatest.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=12,ShadowDepth=2,Opacity=.10,Color=Colors.Black};
            jumpToLatest.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='ring' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='17' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ring' Property='Background' Value='#F2F4F7'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            jumpToLatest.Click+=delegate{scroll.ScrollToEnd();};var floating=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};floating.Children.Add(jumpToLatest);liveChanges=new LiveChangeSummary(OpenTaskChanges){HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=400};floating.Children.Add(liveChanges);historyOverlay.Children.Add(floating);Grid.SetRow(historyOverlay,2);Panel.SetZIndex(historyOverlay,5);messageArea.Children.Add(historyOverlay);
            scroll.ScrollChanged+=delegate{RefreshHistoryNavigation();};scroll.SizeChanged+=delegate{RefreshHistoryNavigation();};
        }
        // A queued automatic follow must not override a newer reader scroll or session.
        void QueueHistoryFollow()
        {
            double expectedOffset=scroll.VerticalOffset;var expectedSession=session;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(delegate{
                if(closed||!ReferenceEquals(expectedSession,session))return;
                if(Math.Abs(scroll.VerticalOffset-expectedOffset)<1||scroll.ScrollableHeight-scroll.VerticalOffset<32)scroll.ScrollToEnd();
            }));
        }
        void RefreshHistoryNavigation(){jumpToLatest.Visibility=scroll.ScrollableHeight-scroll.VerticalOffset>120?Visibility.Visible:Visibility.Collapsed;}
    }
}
