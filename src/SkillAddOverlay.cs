using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    // A modal surface inside the plugin page. It does not create a native window.
    internal sealed class SkillAddOverlay : Grid
    {
        readonly TextBlock title=Ui.Text("",20,Ui.PageInk),hint=Ui.Text("",12,Ui.Muted);
        readonly ContentControl surface=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        readonly Button close;
        internal event Action Closed;
        internal bool IsOpen {get{return Visibility==Visibility.Visible;}}
        internal SkillAddOverlay()
        {
            Name="SkillAddOverlay";Visibility=Visibility.Collapsed;Background=new SolidColorBrush(Color.FromArgb(120,0,0,0));
            var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());
            var header=new Grid{Margin=new Thickness(0,0,0,18)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var labels=new StackPanel();title.FontWeight=FontWeights.SemiBold;title.Name="SkillAddTitle";labels.Children.Add(title);hint.Margin=new Thickness(0,3,12,0);labels.Children.Add(hint);header.Children.Add(labels);
            close=UiIcons.Button("close","关闭添加技能");close.Name="CloseSkillAddOverlay";close.Width=close.Height=32;close.Margin=new Thickness(8,0,0,0);close.VerticalAlignment=VerticalAlignment.Top;close.Click+=delegate{Close();};Grid.SetColumn(close,1);header.Children.Add(close);layout.Children.Add(header);
            Grid.SetRow(surface,1);layout.Children.Add(surface);
            var card=new Border{Name="SkillAddCard",Child=layout,Background=ThemeManager.Surface,BorderBrush=ThemeManager.CardBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(22,20,22,22),Margin=new Thickness(24),MaxWidth=860,MaxHeight=740,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=28,ShadowDepth=8,Opacity=.18}};
            Children.Add(card);KeyboardNavigation.SetTabNavigation(card,KeyboardNavigationMode.Cycle);KeyboardNavigation.SetControlTabNavigation(card,KeyboardNavigationMode.Cycle);
            PreviewKeyDown+=delegate(object sender,KeyEventArgs args){if(args.Key==Key.Escape){args.Handled=true;Close();}};
            MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs args){if(Object.ReferenceEquals(args.OriginalSource,this)){args.Handled=true;Close();}};
            System.Windows.Automation.AutomationProperties.SetName(card,"添加技能");
        }
        internal void Open(string heading,string description,UIElement content)
        {
            title.Text=heading;hint.Text=description;surface.Content=content;Visibility=Visibility.Visible;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(delegate{if(IsOpen)MoveFocus(new TraversalRequest(FocusNavigationDirection.First));}));
        }
        internal void Close(){if(!IsOpen)return;Visibility=Visibility.Collapsed;surface.Content=null;var closed=Closed;if(closed!=null)closed();}
    }
}
