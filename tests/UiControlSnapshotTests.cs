using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
namespace Dafeiyu.Tests
{
    public static class UiControlSnapshotTests
    {
        static int checks;
        static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        static Color Pixel(BitmapSource image){var bytes=new byte[4];image.CopyPixels(new Int32Rect(20,20,1,1),bytes,4,0);return Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);}
        static RenderTargetBitmap Render(Drawing drawing)
        {var visual=new DrawingVisual();using(var context=visual.RenderOpen())context.DrawDrawing(drawing);var image=new RenderTargetBitmap(200,100,96,96,PixelFormats.Pbgra32);image.Render(visual);return image;}
        [STAThread]public static int Main()
        {
            try
            {
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var brush=new SolidColorBrush(Colors.Red);var child=new Border{Width=200,Height=100,Background=brush};var surface=new Border{Width=200,Height=100,Child=child};
                surface.Measure(new Size(200,100));surface.Arrange(new Rect(0,0,200,100));surface.UpdateLayout();
                var image=UiControlSnapshot.Capture(surface,true);
                Check(image.IsFrozen,"captured bitmaps are immutable and detached from future render notifications");
                var method=typeof(UiControlSnapshot).GetMethod("CreateSnapshot",BindingFlags.NonPublic|BindingFlags.Static);
                Check(method!=null,"capture exposes the retained drawing snapshot boundary");
                var snapshot=(DrawingGroup)method.Invoke(null,new object[]{surface,true});
                Check(snapshot.IsFrozen,"retained drawings are frozen before crossing the native render channel");
                Check(Pixel(image)==Colors.Red&&Pixel(Render(snapshot))==Colors.Red,"snapshot preserves the actual source drawing colors");
                brush.Color=Colors.Blue;surface.UpdateLayout();
                Check(!brush.IsFrozen&&Pixel(image)==Colors.Red&&Pixel(Render(snapshot))==Colors.Red,"freezing captures never freezes or mutates live application brushes");
                Check(Pixel(UiControlSnapshot.Capture(surface,true))==Colors.Blue,"next capture observes the new live theme value");
                var moving=new TranslateTransform();child.RenderTransform=moving;
                moving.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,1,new Duration(TimeSpan.FromMinutes(1))));
                var clip=new RectangleGeometry(new Rect(0,0,200,100));child.Clip=clip;
                clip.BeginAnimation(RectangleGeometry.RectProperty,new RectAnimation(new Rect(0,0,200,100),new Rect(0,0,199,100),new Duration(TimeSpan.FromMinutes(1))));
                surface.UpdateLayout();
                var animated=UiControlSnapshot.Capture(surface,true);
                Check(animated.IsFrozen&&Pixel(animated)==Colors.Blue,"capture snapshots current animated transforms and clip geometry");
                Check(moving.HasAnimatedProperties&&clip.HasAnimatedProperties,"capturing never stops or freezes live application animations");
                for(int iteration=0;iteration<150;iteration++)
                {
                    Color expected=iteration%2==0?Colors.Red:Colors.Blue;brush.Color=expected;surface.UpdateLayout();
                    Check(Pixel(UiControlSnapshot.Capture(surface,true))==expected,"capture remains current across color change "+iteration);
                    if(iteration%25==0){GC.Collect();GC.WaitForPendingFinalizers();}
                }
                app.Shutdown();Console.WriteLine("UiControlSnapshotTests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
