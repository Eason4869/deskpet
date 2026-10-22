using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace Dafeiyu
{
    internal static class SlimScrollBars
    {
        static Style style;
        public static void Install(FrameworkElement owner)
        {
            if(style==null)style=(Style)ThemeManager.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
<Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='Focusable' Value='False'/><Setter Property='Background' Value='Transparent'/><Setter Property='Width' Value='12'/><Setter Property='MinWidth' Value='12'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
<Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
<Track.DecreaseRepeatButton><RepeatButton x:Name='before' Focusable='False' Command='ScrollBar.PageUpCommand'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
<Track.Thumb><Thumb x:Name='thumb' MinHeight='28' Background='#DFDFDF'><Thumb.Template><ControlTemplate TargetType='Thumb'><Grid Background='Transparent'><Border x:Name='pill' Background='{TemplateBinding Background}' CornerRadius='3' Margin='3,1'/></Grid><ControlTemplate.Triggers>
<DataTrigger Binding='{Binding Orientation, RelativeSource={RelativeSource AncestorType=ScrollBar}}' Value='Horizontal'><Setter TargetName='pill' Property='Margin' Value='1,3'/></DataTrigger>
<DataTrigger Binding='{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ScrollBar}}' Value='True'><Setter TargetName='pill' Property='Background' Value='#555555'/></DataTrigger>
<Trigger Property='IsDragging' Value='True'><Setter TargetName='pill' Property='Background' Value='#4A4A4A'/></Trigger>
</ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
<Track.IncreaseRepeatButton><RepeatButton x:Name='after' Focusable='False' Command='ScrollBar.PageDownCommand'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
</Track></Grid><ControlTemplate.Triggers>
<Trigger Property='Orientation' Value='Horizontal'><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/><Setter TargetName='before' Property='Command' Value='ScrollBar.PageLeftCommand'/><Setter TargetName='after' Property='Command' Value='ScrollBar.PageRightCommand'/><Setter TargetName='thumb' Property='MinHeight' Value='0'/><Setter TargetName='thumb' Property='MinWidth' Value='28'/></Trigger>
<Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
<Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='12'/><Setter Property='MinHeight' Value='12'/></Trigger></Style.Triggers>
</Style>");
            owner.Resources[typeof(ScrollBar)]=style;
        }
    }
}
