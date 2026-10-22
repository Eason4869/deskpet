using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Desktop;
internal static class ImageMotionUiChecks
{
    internal static bool GeneratingCardReady(Window host,ImageGenerationPendingCard card)=>host.IsVisible&&host.WindowState==WindowState.Normal&&card.GetVisualAncestors().Contains(host)&&card.IsEffectivelyVisible&&card.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="正在生成图片");
    internal static string Diagnostic(DesktopController owner,Window host,ImageGenerationPendingCard card)=>"systemReduced="+ImageGenerationMotion.SystemReducedMotion+" cardReduced="+card.ReducedMotion+" appMotion="+owner.Settings.ImageGenerationMotionEnabled+" cardMotion="+card.MotionEnabled+" attached="+card.GetVisualAncestors().Contains(host)+" visible="+card.IsEffectivelyVisible+" hostVisible="+host.IsVisible+" host="+host.WindowState+" clock="+card.ClockRunning+" labels="+String.Join("|",card.GetLogicalDescendants().OfType<TextBlock>().Select(t=>t.Text));
    internal static int Run(DesktopController owner,Window host,ImageGenerationPendingCard card)
    {
        int count=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL Avalonia motion: "+name+": "+Diagnostic(owner,host,card));count++;}
        void CheckPermittedClock(string name)=>Check(card.ClockRunning==!ImageGenerationMotion.SystemReducedMotion,name+" follows system motion preference");
        Check(GeneratingCardReady(host,card)&&card.MotionEnabled&&owner.Settings.ImageGenerationMotionEnabled&&!card.ReducedMotion,"visible generating card permits application motion");
        CheckPermittedClock("active frame clock");
        card.MotionEnabled=false;Check(!card.ClockRunning,"disabled stops clock");card.MotionEnabled=true;CheckPermittedClock("enabled clock");
        owner.Settings.ImageGenerationMotionEnabled=false;card.MotionEnabled=true;Check(!card.ClockRunning,"application disabled stops clock");owner.Settings.ImageGenerationMotionEnabled=true;card.MotionEnabled=true;CheckPermittedClock("application enabled clock");
        card.ReducedMotion=true;Check(!card.ClockRunning,"reduced motion stops clock");Check(GeneratingCardReady(host,card),"reduced motion preserves generating card readiness");card.ReducedMotion=false;CheckPermittedClock("reduced motion cleared clock");
        card.IsVisible=false;Check(!card.ClockRunning,"hidden stops clock");card.IsVisible=true;CheckPermittedClock("visible clock");host.WindowState=WindowState.Minimized;Check(!card.ClockRunning,"minimized stops clock");host.WindowState=WindowState.Normal;CheckPermittedClock("restored clock");
        string output=Path.GetFullPath("work/image-motion-avalonia");Directory.CreateDirectory(output);
        var window=new Window{Width=260,Height=390};var detached=new ImageGenerationPendingCard(owner,window,owner.ImageTasks.List().First(t=>!t.IsTerminal).Id);window.Content=detached;window.Show();
        foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(output,"motion-card-"+theme+".png"),new PngBitmapEncoderOptions());}
        window.Width=190;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var frame=window.CaptureRenderedFrame())frame!.Save(Path.Combine(output,"motion-card-narrow.png"),new PngBitmapEncoderOptions());window.Width=260;window.Height=270;window.Content=null;Check(!detached.ClockRunning,"unloaded card releases clock");
        foreach(string theme in new[]{"light","dark"})foreach(double phase in new[]{.16,.46,.76}){DesktopTheme.Apply(theme);window.Content=new ImageGenerationDots{Phase=phase};Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();Check(frame!=null,"native deterministic frame");frame!.Save(Path.Combine(output,"motion-"+theme+"-"+(int)(phase*100)+".png"),new PngBitmapEncoderOptions());}
        window.Width=190;window.Content=new ImageGenerationDots();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var frame=window.CaptureRenderedFrame())frame!.Save(Path.Combine(output,"motion-narrow.png"),new PngBitmapEncoderOptions());window.Close();DesktopTheme.Apply("light");return count;
    }
}
