using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
namespace Dafeiyu
{
    internal static class SettingsSurface
    {
        internal const double GroupSpacing=36;
        internal const double BodyWidth=684;
        static ControlTemplate expanderTemplate,sliderTemplate;
        internal static ScrollViewer Scroll(UIElement body,double horizontalInset=0)
        {
            var scroll=Ui.Scroll(body);
            scroll.MaxWidth=BodyWidth;
            scroll.Margin=new Thickness(horizontalInset,0,horizontalInset,0);
            return scroll;
        }
        internal static CheckBox Toggle(Func<string> label,bool value,Action<bool> changed)
        {return LocalizedUi.With(Ui.Toggle(label(),value,changed,true),"Content",label);}
        internal static Slider StyleSlider(Slider slider,int precision=0)
        {
            if(sliderTemplate==null)sliderTemplate=(ControlTemplate)ThemeManager.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
  <ControlTemplate.Resources>
    <Style x:Key='RailButton' TargetType='RepeatButton'>
      <Setter Property='Focusable' Value='False'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='RepeatButton'>
            <Grid Background='Transparent'>
              <Border Background='{TemplateBinding Background}' CornerRadius='2' Height='4' VerticalAlignment='Center'/>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </ControlTemplate.Resources>
  <Grid x:Name='SliderSurface' Background='Transparent' SnapsToDevicePixels='True'>
    <Track x:Name='PART_Track'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='{x:Static Slider.DecreaseLarge}' Style='{StaticResource RailButton}' Background='{TemplateBinding Foreground}'/>
      </Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='{x:Static Slider.IncreaseLarge}' Style='{StaticResource RailButton}' Background='{TemplateBinding Background}'/>
      </Track.IncreaseRepeatButton>
      <Track.Thumb>
        <Thumb Width='16' x:Name='SliderThumb' Background='{TemplateBinding Foreground}' BorderBrush='{TemplateBinding BorderBrush}'>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'>
              <Grid Background='Transparent'>
                <Ellipse Width='16' Height='16' Fill='{TemplateBinding Background}' Stroke='{TemplateBinding BorderBrush}' StrokeThickness='2'/>
              </Grid>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsKeyboardFocusWithin' Value='True'>
      <Setter TargetName='SliderThumb' Property='Effect'>
        <Setter.Value><DropShadowEffect Color='#357FF5' BlurRadius='5' ShadowDepth='0' Opacity='.8'/></Setter.Value>
      </Setter>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='SliderSurface' Property='Opacity' Value='.5'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");
            slider.Template=sliderTemplate;
            slider.MinHeight=26;
            slider.Background=ThemeManager.Brush("#85858A","#65758C");
            slider.Foreground=ThemeManager.Brush("#126BC4","#73AAFF");
            slider.BorderBrush=ThemeManager.Surface;
            slider.AutoToolTipPlacement=AutoToolTipPlacement.TopLeft;
            slider.AutoToolTipPrecision=precision;
            slider.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler(delegate(object sender,DragStartedEventArgs args)
            {
                var thumb=args.OriginalSource as Thumb;
                var tooltip=thumb==null?null:thumb.ToolTip as ToolTip;
                if(tooltip==null)return;
                tooltip.Background=ThemeManager.Surface;
                tooltip.Foreground=Ui.Ink;
                tooltip.BorderBrush=ThemeManager.CardBorder;
                tooltip.Padding=new Thickness(8,4,8,4);
            }),true);
            return slider;
        }
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
        internal static Border Card(UIElement content){return new Border{Child=content,Padding=new Thickness(20,18,20,6),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),BorderBrush=ThemeManager.CardBorder,Background=ThemeManager.CardSurface};}
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
