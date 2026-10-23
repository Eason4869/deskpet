using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class LocalizationUiChecks
{
    internal static int Run(string directory)
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("Localization UI: "+label);checks++;}
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory));
        string artifacts=Path.Combine(AppContext.BaseDirectory,"artifacts","localization");Directory.CreateDirectory(artifacts);
        foreach(string mode in new[]{"light","dark"})
        {
            DesktopTheme.Apply(mode);
            var settings=new SettingsWindow(owner);settings.Show();Dispatcher.UIThread.RunJobs();
            Check(settings.GetLogicalDescendants().OfType<TabItem>().Any(t=>t.Header as string=="工作设置"),mode+" settings independent Chinese tab expectation");
            Check(!settings.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text?.StartsWith("[settings",StringComparison.Ordinal)==true),mode+" no missing resource marker");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using(var image=settings.CaptureRenderedFrame()){Check(image!=null,"settings rendered");image!.Save(Path.Combine(artifacts,"settings-"+mode+".png"),new PngBitmapEncoderOptions());}
            settings.Close();
            var chat=new ChatWindow(owner,false);chat.Show();chat.Width=390;Dispatcher.UIThread.RunJobs();
            Check(chat.GetLogicalDescendants().OfType<Button>().Any(b=>b.Name=="ChatDictation"),mode+" narrow composer retains dictation control");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using(var image=chat.CaptureRenderedFrame()){Check(image!=null,"narrow chat rendered");image!.Save(Path.Combine(artifacts,"chat-narrow-"+mode+".png"),new PngBitmapEncoderOptions());}
            chat.Close();
            var reopened=new SettingsWindow(owner);reopened.Show();Dispatcher.UIThread.RunJobs();Check(reopened.IsVisible,"settings reopen");reopened.Close();
        }
        DesktopTheme.Apply("system");
        owner.Pet.Close();owner.Dispose();
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!owner.ImageServicesShutdown.IsCompleted){if(timer.Elapsed.TotalSeconds>10)throw new TimeoutException("Localization fixture cleanup");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}
        owner.ImageServicesShutdown.GetAwaiter().GetResult();
        return checks;
    }
}
