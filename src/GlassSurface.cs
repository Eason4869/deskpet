using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace Dafeiyu
{
    internal static class GlassSurface
    {
        static GradientStop Stop(string color,double offset)
        {
            var stop=new GradientStop{Offset=offset};
            System.Windows.Data.BindingOperations.SetBinding(stop,GradientStop.ColorProperty,new System.Windows.Data.Binding("Color"){Source=Ui.Brush(color)});return stop;
        }
        public static Brush Background()
        {
            var brush=new LinearGradientBrush();brush.StartPoint=new Point(0,0);brush.EndPoint=new Point(1,1);
            brush.GradientStops.Add(Stop("#E2ECF9",0));
            brush.GradientStops.Add(Stop("#F5F7FB",.48));
            brush.GradientStops.Add(Stop("#E1F0ED",1));return brush;
        }
        public static Brush Panel()
        {
            var brush=new LinearGradientBrush();brush.StartPoint=new Point(.2,0);brush.EndPoint=new Point(.8,1);
            brush.GradientStops.Add(Stop("#F2FFFFFF",0));
            brush.GradientStops.Add(Stop("#C3FAFCFF",.52));
            brush.GradientStops.Add(Stop("#DEFFFFFF",1));return brush;
        }
        public static Border Card(UIElement child)
        {
            var card=Ui.Card(child);card.Background=Panel();card.CornerRadius=new CornerRadius(18);
            card.BorderBrush=Ui.Brush("#ECFFFFFF");
            card.Effect=new DropShadowEffect{Color=Color.FromRgb(83,112,145),BlurRadius=15,ShadowDepth=3,Opacity=.085};return card;
        }
    }
}
