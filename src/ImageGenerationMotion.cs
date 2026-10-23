using System;
namespace Dafeiyu
{
    public struct ImageMotionSample
    {
        public double X,Y,Radius,Opacity;
    }
    public static class ImageGenerationMotion
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)]
        static extern bool SystemParametersInfo(uint action,uint parameter,out int value,uint flags);
        public static bool SystemReducedMotion {get{if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return false;int value;return SystemParametersInfo(0x1042,0,out value,0)&&value==0;}}
        public static ImageMotionSample Sample(double phase)
        {
            if(Double.IsNaN(phase)||Double.IsInfinity(phase))phase=0;
            phase-=Math.Floor(phase);double angle=phase*Math.PI*2;
            return new ImageMotionSample{X=.5+.29*Math.Cos(angle),Y=.5+.29*Math.Sin(angle),Radius=.42,Opacity=.72};
        }
        public static double DotOpacity(ImageMotionSample sample,double x,double y)
        {
            double dx=x-sample.X,dy=y-sample.Y;
            return .035+.5*sample.Opacity*Math.Exp(-(dx*dx+dy*dy)/(2*sample.Radius*sample.Radius*.22));
        }
        public static bool ShouldAnimate(ImageGenerationTaskState state,bool enabled,bool visible,bool minimized,bool reducedMotion)
        {
            return enabled&&visible&&!minimized&&!reducedMotion&&(state==ImageGenerationTaskState.Generating||state==ImageGenerationTaskState.Downloading||state==ImageGenerationTaskState.Saving);
        }
        public static int ElapsedSeconds(ImageGenerationTaskRecord task,DateTime now){return task==null||!task.StartedUtc.HasValue?0:Math.Max(0,(int)((task.CompletedUtc??now)-task.StartedUtc.Value).TotalSeconds);}
        public static string Label(ImageGenerationTaskRecord task)
        {
            if(task==null)return TextResources.Get("imageGenerationMotion.label.text");
            switch(task.State){case ImageGenerationTaskState.Queued:return TextResources.Get("imageGenerationMotion.label.text.alternativeA");case ImageGenerationTaskState.Generating:return TextResources.Get("imageGenerationMotion.label.text.alternativeB");case ImageGenerationTaskState.Downloading:return TextResources.Get("imageGenerationMotion.label.text.alternativeC");case ImageGenerationTaskState.Saving:return TextResources.Get("imageGenerationMotion.label.text.alternativeD");case ImageGenerationTaskState.Succeeded:return TextResources.Get("imageGenerationMotion.label.text.alternativeE");case ImageGenerationTaskState.Canceled:return TextResources.Get("imageGenerationMotion.label.text.alternativeF");case ImageGenerationTaskState.Interrupted:return TextResources.Get("imageGenerationMotion.label.text.alternativeG");default:return TextResources.Get("imageGenerationMotion.label.text.alternativeH");}
        }
    }
}
