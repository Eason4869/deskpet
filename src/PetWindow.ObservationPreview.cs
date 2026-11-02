using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        // This fallback is local-preview only. Automatic observation still excludes our windows.
        DesktopObservationFrame CaptureOwnObservationPreview(DesktopCaptureOptions options)
        {
            if(closing || sessionLocked || !ApplicationSnapshots.InteractiveDesktop())return null;
            int processId;long processStarted;string applicationId;
            using(var process=Process.GetCurrentProcess()){processId=process.Id;processStarted=process.StartTime.ToUniversalTime().Ticks;applicationId=process.MainModule.FileName;}
            if(options.IsExcluded(applicationId))return null;
            var target=Application.Current.Windows.OfType<Window>().Where(w=>w!=this && !(w is DesktopObservationPopup) && w.IsVisible && w.WindowState!=WindowState.Minimized && w.ActualWidth>0 && w.ActualHeight>0).OrderByDescending(w=>w.IsActive).FirstOrDefault();
            if(target==null && IsPetVisible)target=this;
            if(target==null || target.ActualWidth<1 || target.ActualHeight<1)return null;
            double width=target.ActualWidth,height=target.ActualHeight;
            double scale=Math.Min(1,Math.Min(DesktopCaptureOptions.MaximumDimension/Math.Max(width,height),Math.Sqrt(DesktopCaptureOptions.MaximumPixels/(width*height))));
            for(int attempt=0;attempt<6;attempt++,scale*=.75){
                int pixelsWide=Math.Max(1,(int)(width*scale)),pixelsHigh=Math.Max(1,(int)(height*scale));
                var visual=new DrawingVisual();
                using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(target){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,width,height),Stretch=Stretch.Fill},null,new Rect(0,0,pixelsWide,pixelsHigh));
                var bitmap=new RenderTargetBitmap(pixelsWide,pixelsHigh,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));byte[] png;
                using(var stream=new MemoryStream()){encoder.Save(stream);if(stream.Length>DesktopCaptureOptions.MaximumPngBytes)continue;png=stream.ToArray();}
                if(closing || sessionLocked || !target.IsVisible || !ApplicationSnapshots.InteractiveDesktop()){Array.Clear(png,0,png.Length);return null;}
                try{
                    var identity=new DesktopObservationIdentity(new WindowInteropHelper(target).Handle.ToInt64(),processId,processStarted,0,applicationId,target.Title??"","local-preview",(int)target.Left,(int)target.Top,pixelsWide,pixelsHigh);
                    return new DesktopObservationFrame(png,DateTimeOffset.UtcNow,identity);
                }catch{Array.Clear(png,0,png.Length);throw;}
            }
            return null;
        }
        internal Window ShowObservationMessage(string message,string title)
        {
            var text=Ui.Text(message,14,Ui.Blue);text.HorizontalAlignment=HorizontalAlignment.Center;text.VerticalAlignment=VerticalAlignment.Center;
            var popup=new DesktopObservationPopup(title,text,460,180,DesktopObservationPopup.ScreenCenter,false);popup.Name="DesktopObservationMessage";
            observationPreviews.Add(popup);popup.Closed+=delegate{observationPreviews.Remove(popup);};popup.Show();return popup;
        }
    }
}
