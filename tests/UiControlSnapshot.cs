using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    // Control-only capture. Resolve bound brush values from retained drawings:
    // WPF's offscreen VisualBrush channel can retain the previous theme colors.
    internal static class UiControlSnapshot
    {
        internal static RenderTargetBitmap Capture(FrameworkElement view,bool templateRendered=false)
        {
            view.UpdateLayout();
            if(templateRendered)
            {
                // Materialize drawings for the deliberately unattached menu template.
                var primer=new DrawingVisual();using(var draw=primer.RenderOpen())draw.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,1,1));
                new RenderTargetBitmap(1,1,96,96,PixelFormats.Pbgra32).Render(primer);
            }
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())Draw(draw,view,true,templateRendered);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);return bitmap;
        }
        static void Draw(DrawingContext draw,Visual view,bool root,bool templateRendered)
        {
            var element=view as UIElement;
            if(element!=null&&(templateRendered?element.Visibility!=Visibility.Visible:!element.IsVisible))return;
            int pushed=0;
            if(!root)
            {
                var offset=VisualTreeHelper.GetOffset(view);draw.PushTransform(new TranslateTransform(offset.X,offset.Y));pushed++;
                var transform=VisualTreeHelper.GetTransform(view);if(transform!=null){draw.PushTransform(transform);pushed++;}
            }
            var clip=VisualTreeHelper.GetClip(view);if(clip!=null){draw.PushClip(clip);pushed++;}
            draw.PushOpacity(VisualTreeHelper.GetOpacity(view));pushed++;
            var mask=VisualTreeHelper.GetOpacityMask(view);if(mask!=null){draw.PushOpacityMask(mask.CloneCurrentValue());pushed++;}
            var drawing=VisualTreeHelper.GetDrawing(view);if(drawing!=null)draw.DrawDrawing(drawing.CloneCurrentValue());
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(view);i++){var child=VisualTreeHelper.GetChild(view,i)as Visual;if(child!=null)Draw(draw,child,false,templateRendered);}
            while(pushed-->0)draw.Pop();
        }
    }
}
