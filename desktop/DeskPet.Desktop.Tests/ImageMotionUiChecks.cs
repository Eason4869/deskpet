using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DeskPet.Desktop;
internal static class ImageMotionUiChecks
{
    internal static int Run(DesktopController owner,Window host,ImageGenerationPendingCard card)
    {
        int count=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL Avalonia motion: "+name);count++;}
        Check(card.ClockRunning,"active frame clock");card.MotionEnabled=false;Check(!card.ClockRunning,"disabled stops clock");card.MotionEnabled=true;Check(card.ClockRunning,"enabled resumes clock");card.ReducedMotion=true;Check(!card.ClockRunning,"reduced motion stops clock");card.ReducedMotion=false;
        card.IsVisible=false;Check(!card.ClockRunning,"hidden stops clock");card.IsVisible=true;Check(card.ClockRunning,"visible resumes clock");host.WindowState=WindowState.Minimized;Check(!card.ClockRunning,"minimized stops clock");host.WindowState=WindowState.Normal;Check(card.ClockRunning,"restored resumes clock");
        string output=Path.GetFullPath("work/image-motion-avalonia");Directory.CreateDirectory(output);
        var window=new Window{Width=260,Height=390};var detached=new ImageGenerationPendingCard(owner,window,owner.ImageTasks.List().First(t=>!t.IsTerminal).Id);window.Content=detached;window.Show();
        foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(output,"motion-card-"+theme+".png"),new PngBitmapEncoderOptions());}
        window.Width=190;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var frame=window.CaptureRenderedFrame())frame!.Save(Path.Combine(output,"motion-card-narrow.png"),new PngBitmapEncoderOptions());window.Width=260;window.Height=270;window.Content=null;Check(!detached.ClockRunning,"unloaded card releases clock");
        foreach(string theme in new[]{"light","dark"})foreach(double phase in new[]{.16,.46,.76}){DesktopTheme.Apply(theme);window.Content=new ImageGenerationDots{Phase=phase};Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();Check(frame!=null,"native deterministic frame");frame!.Save(Path.Combine(output,"motion-"+theme+"-"+(int)(phase*100)+".png"),new PngBitmapEncoderOptions());}
        window.Width=190;window.Content=new ImageGenerationDots();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var frame=window.CaptureRenderedFrame())frame!.Save(Path.Combine(output,"motion-narrow.png"),new PngBitmapEncoderOptions());window.Close();DesktopTheme.Apply("light");return count;
    }
}
