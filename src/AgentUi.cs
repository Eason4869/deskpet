using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Dafeiyu
{
    internal static class AgentUi
    {
        internal static readonly Brush Ink=Ui.Brush("#282A2F"),Muted=Ui.Brush("#7A7E86"),Line=Ui.Brush("#E9EAED"),Soft=Ui.Brush("#F7F8FA");
        static readonly ControlTemplate navigation=NavigationTemplate("#EDEEF1"),strongNavigation=NavigationTemplate("#DEE0E3");
        static ControlTemplate NavigationTemplate(string hoverColor){return (ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='"+hoverColor+"'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");}
        internal static Button Nav(string text,string name,bool strongHover=false)
        {
            return new Button{Name=name,Content=text,Foreground=Ink,Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,Template=strongHover?strongNavigation:navigation,Padding=new Thickness(10,8,10,8),MinHeight=35,FontSize=13,Margin=new Thickness(0,2,0,2),FocusVisualStyle=null,HorizontalContentAlignment=HorizontalAlignment.Left};
        }
        internal static Border Card(UIElement child)
        {return new Border{Child=child,Background=ThemeManager.CardSurface,BorderBrush=ThemeManager.Brush("#E9EAED","#52637C"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(18),Margin=new Thickness(0,0,0,14)};}
internal static Button Nav(System.Func<string> text,string name,bool strongHover=false){return LocalizedUi.With(Nav(text(),name,strongHover),"Content",text);}    }
}
