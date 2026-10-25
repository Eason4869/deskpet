using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Dafeiyu
{
    // A shared 24-unit grid gives navigation and menu symbols identical optical scale.
    internal static class ShellIcons
    {
        static readonly ControlTemplate buttonTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#E4EAF0'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#D5E1ED'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.32'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        internal static FrameworkElement Image(string name,Brush ink=null,double size=18,bool solid=false)
        {
            string shape;
            switch(name){
                case "image":shape="M3 4h18v16H3z M3 16l5-5 5 5 3-3 5 5 M16 8h.1";break;
                case "home":shape="M3 11 12 3 21 11 M5 10v10h5v-6h4v6h5V10";break;
                case "settings":shape="M9.13 5.07L10.19 4.72 10.21 1.86 13.79 1.86 13.81 4.72 14.87 5.07 15.86 5.57 17.91 3.56 20.44 6.09 18.43 8.14 18.93 9.13 19.28 10.19 22.14 10.21 22.14 13.79 19.28 13.81 18.93 14.87 18.43 15.86 20.44 17.91 17.91 20.44 15.86 18.43 14.87 18.93 13.81 19.28 13.79 22.14 10.21 22.14 10.19 19.28 9.13 18.93 8.14 18.43 6.09 20.44 3.56 17.91 5.57 15.86 5.07 14.87 4.72 13.81 1.86 13.79 1.86 10.21 4.72 10.19 5.07 9.13 5.57 8.14 3.56 6.09 6.09 3.56 8.14 5.57z M15.3 12a3.3 3.3 0 1 1-6.6 0a3.3 3.3 0 1 1 6.6 0";break;
                case "back":shape="M19 12H5 M11 6l-6 6 6 6";break;
                case "forward":shape="M5 12h14 M13 6l6 6-6 6";break;
                case "sidebar":shape="M5 3h14q2 0 2 2v14q0 2-2 2H5q-2 0-2-2V5q0-2 2-2 M9 3v18";break;
                case "usage":shape="M3 15h4v6H3z M10 9h4v12h-4z M17 3h4v18h-4z";break;
                case "mcp":shape="M4 6h5V4a2 2 0 0 1 4 0v2h5v5h2a2 2 0 0 1 0 4h-2v5h-5v-2a2 2 0 0 0-4 0v2H4v-5h2a2 2 0 0 0 0-4H4Z";break;
                case "pet":shape="M2 12q10-14 20 0-10 14-20 0 M15 12a3 3 0 1 1-6 0a3 3 0 1 1 6 0";break;
                case "people":shape="M3 21v-3q0-5 7-5t7 5v3 M14 6a4 4 0 1 1-8 0a4 4 0 1 1 8 0 M18 3q6 3 0 7 M20 14q3 1 3 6";break;
                case "language":shape="M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0 M3 12h18 M4.2 7.5h15.6 M4.2 16.5h15.6 M12 3C6 8 6 16 12 21 M12 3C18 8 18 16 12 21";break;
                case "help":shape="M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0 M9.5 8.5C9.5 5.4 14.8 5.4 14.8 8.4C14.8 10.3 12 10.6 12 12.8 M12 16.5v.15";break;
                case "about":shape="M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0 M12 7v6 M12 16.5v.15";break;
                case "keys":shape="M4 5h16q2 0 2 2v10q0 2-2 2H4q-2 0-2-2V7q0-2 2-2 M6 9h1 M11 9h1 M16 9h1 M6 13h1 M11 13h1 M16 13h1 M8 16h8";break;
                case "book":shape="M12 5q-5-3-10 0v15q5-3 10 0 5-3 10 0V5q-5-3-10 0v15";break;
                case "news":shape="M5 3h14q2 0 2 2v14q0 2-2 2H5q-2 0-2-2V5q0-2 2-2 M7 7h10 M7 11h10 M7 15h6";break;
                case "feedback":shape="M5 3h14q2 0 2 2v11q0 2-2 2h-9l-5 4v-4q-2 0-2-2V5q0-2 2-2 M7 8h10 M7 12h7";break;
                case "terminal":shape="M5 3h14q2 0 2 2v14q0 2-2 2H5q-2 0-2-2V5q0-2 2-2 M7 8l4 4-4 4 M14 16h3";break;
                case "folder":shape="M3 8V5q0-2 2-2h5l3 4h6q2 0 2 2v11H3z M3 11h18";break;
                case "file":shape="M5 3h9l5 5v13H5z M14 3v5h5 M8 12h8 M8 16h6";break;
                case "new":shape="M5 4h9l5 5v12H5z M14 4v5h5 M8 15h8 M12 11v8";break;
                case "new-chat":shape="M5 4h14q2 0 2 2v10q0 2-2 2h-8l-6 4v-4q-2 0-2-2V6q0-2 2-2 M8 11h8 M12 7v8";break;
                case "tabs":shape="M3 6h18v15H3z M6 3h12 M14 6v15";break;
                case "split-panels":shape="M3 5h7v14H3z M14 5h7v14h-7z";break;
                case "expand-tabs":shape="M8 3H3v5 M16 3h5v5 M21 16v5h-5 M8 21H3v-5";break;
                case "collapse-tools":shape="M17 3h4v18h-4 M3 12h11 M9 7l5 5-5 5";break;
                case "summary":shape="M3 5h3v3H3z M3 11h3v3H3z M3 17h3v3H3z M10 6h11 M10 12h11 M10 18h11";break;
                case "close":shape="M6 6l12 12 M18 6 6 18";break;
                case "minimize":shape="M5 12h14";break;
                case "maximize":shape="M5 5h14v14H5z";break;
                case "restore":shape="M8 8h11v11H8z M5 15H3V3h12v2";break;
                default:shape="M12 3v12 M6 9a9 9 0 1 0 12 0";break;
            }
            if(solid&&name=="home")shape="M3 10.5L12 3l9 7.5-2 2V21h-5v-6h-4v6H5v-8.5z";
            var path=new Path{Data=Geometry.Parse(shape),Fill=solid?(ink??AgentUi.Ink):Brushes.Transparent,Stroke=ink??AgentUi.Muted,StrokeThickness=1.65,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
            var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(path);return new Viewbox{Child=canvas,Width=size,Height=size,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false};
        }
        internal static Button Button(string icon,string label,Action click)
        {
            var b=new Button{Content=Image(icon),Template=buttonTemplate,Width=32,Height=32,MinHeight=0,Padding=new Thickness(0),Margin=new Thickness(2),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,Focusable=true,Cursor=Cursors.Hand,ToolTip=label,FocusVisualStyle=null};
            System.Windows.Automation.AutomationProperties.SetName(b,label);if(click!=null)b.Click+=delegate{click();};return b;
        }
internal static Button Button(System.Func<string> icon,string label,Action click){return LocalizedUi.With(Button(icon(),label,click),"Content",icon);}    }
}
