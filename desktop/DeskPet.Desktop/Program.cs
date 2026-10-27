using Avalonia;
using Dafeiyu;

namespace DeskPet.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try { SmokeRun.Configure(args); int result=BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);if(result==0&&ApplicationRestart.Requested)ApplicationRestart.Launch();return result; }
        catch (Exception error) { if (SmokeRun.Options != null) SmokeRun.Failure(error); Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 1; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
