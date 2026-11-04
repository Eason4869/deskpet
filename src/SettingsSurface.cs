using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
namespace Dafeiyu
{
    internal static class SettingsSurface
    {
        internal const double GroupSpacing=36;
        static ControlTemplate expanderTemplate;
        internal static void StyleExpander(Expander expander)
        {
            if(expanderTemplate==null)expanderTemplate=(ControlTemplate)ThemeManager.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Expander'>
  <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='10' SnapsToDevicePixels='True'>
    <Grid>
      <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
      <ToggleButton x:Name='HeaderSite' Content='{TemplateBinding Header}' ContentTemplate='{TemplateBinding HeaderTemplate}' ContentTemplateSelector='{TemplateBinding HeaderTemplateSelector}' Foreground='{TemplateBinding Foreground}' IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' HorizontalContentAlignment='Stretch' MinHeight='44' Padding='12,8' FocusVisualStyle='{x:Null}'>
        <ToggleButton.Template>
          <ControlTemplate TargetType='ToggleButton'>
            <Border x:Name='headerSurface' Background='#FAFAFB' BorderBrush='Transparent' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'>
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='14'/></Grid.ColumnDefinitions>
                <ContentPresenter Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' ContentTemplateSelector='{TemplateBinding ContentTemplateSelector}' RecognizesAccessKey='True' VerticalAlignment='Center' Margin='0,0,12,0'/>
                <Path x:Name='chevron' Grid.Column='1' Width='14' Height='14' Data='M 3,5 L 7,9 L 11,5' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' VerticalAlignment='Center' RenderTransformOrigin='.5,.5'><Path.RenderTransform><RotateTransform Angle='0'/></Path.RenderTransform></Path>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsChecked' Value='True'><Setter TargetName='chevron' Property='RenderTransform'><Setter.Value><RotateTransform Angle='180'/></Setter.Value></Setter></Trigger>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='headerSurface' Property='Background' Value='#F1F1F1'/><Setter TargetName='headerSurface' Property='BorderBrush' Value='#ADB0B5'/></Trigger>
              <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='headerSurface' Property='BorderBrush' Value='#6B809E'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='headerSurface' Property='Background' Value='#EAEAEA'/></Trigger>
              <Trigger Property='IsEnabled' Value='False'><Setter TargetName='headerSurface' Property='Opacity' Value='.5'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </ToggleButton.Template>
      </ToggleButton>
      <Border x:Name='body' Grid.Row='1' Padding='12,12,12,12' Visibility='Collapsed'><ContentPresenter ContentSource='Content' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/></Border>
    </Grid>
  </Border>
  <ControlTemplate.Triggers><Trigger Property='IsExpanded' Value='True'><Setter TargetName='body' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>");
            expander.Template=expanderTemplate;expander.FocusVisualStyle=null;expander.Focusable=false;
            expander.Foreground=Ui.Ink;expander.Background=ThemeManager.CardSurface;expander.BorderBrush=ThemeManager.Brush("#D5D9DF","#52637C");
            expander.HorizontalContentAlignment=HorizontalAlignment.Stretch;expander.Padding=new Thickness(0);
        }
        internal static Border Card(UIElement content){return new Border{Child=content,Padding=new Thickness(20,18,20,6),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),BorderBrush=ThemeManager.Brush("#E7E7E9","#3D4148"),Background=ThemeManager.CardSurface};}
        internal static void Section(Panel parent,Func<string> title,UIElement content)
        {
            var label=LocalizedUi.Text(title,14,Ui.Ink);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,parent.Children.Count==0?0:GroupSpacing,0,12);parent.Children.Add(label);parent.Children.Add(Card(content));
        }
        internal static void Reveal(FrameworkElement element)
        {
            if(element==null)return;
            for(DependencyObject parent=element;parent!=null;){var expander=parent as Expander;if(expander!=null)expander.IsExpanded=true;parent=LogicalTreeHelper.GetParent(parent)??(parent is Visual?VisualTreeHelper.GetParent(parent):null);}
            element.BringIntoView();element.UpdateLayout();if(element.Focusable&&element.IsEnabled)element.Focus();
            var previous=element.Effect;var glow=new DropShadowEffect{Color=Color.FromRgb(53,127,245),BlurRadius=12,ShadowDepth=0,Opacity=.55};element.Effect=glow;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1400)};timer.Tick+=delegate{timer.Stop();if(ReferenceEquals(element.Effect,glow))element.Effect=previous;};timer.Start();
        }
    }
}
