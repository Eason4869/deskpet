using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle(Dafeiyu.ReleaseInfo.ProductName)]
[assembly: AssemblyProduct(Dafeiyu.ReleaseInfo.ProductName)]
[assembly: AssemblyDescription("桌面伙伴：E包、大肥鱼、马尔芙；角色互动、图形化 Live 通话、聊天历史与免打扰")]
[assembly: AssemblyCompany(Dafeiyu.ReleaseInfo.Author)]
[assembly: AssemblyCopyright("Copyright © 2026 彧晟Eason")]
[assembly: AssemblyVersion(Dafeiyu.ReleaseInfo.AssemblyVersion)]
[assembly: AssemblyFileVersion(Dafeiyu.ReleaseInfo.AssemblyVersion)]

namespace Dafeiyu
{
    public static class Program
    {
        [STAThread] public static void Main(string[] startupArgs)
        {
            string suffix = WindowsIdentity.GetCurrent().User.Value;
            bool normalExit=false;
            bool first;
            using (var mutex = new Mutex(true, "Local\\Dafeiyu.DeskPet." + suffix, out first))
            {
                if (!first)
                {
                    try { using (EventWaitHandle wake = EventWaitHandle.OpenExisting("Local\\Dafeiyu.DeskPet.Wake." + suffix)) wake.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }
                using (var installation = new Mutex(false, ReleaseInfo.InstallationMutex))
                using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Dafeiyu.DeskPet.Wake." + suffix))
                {
                    try
                    {
                        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
                        {
                            RuntimeDiagnostics.Record(args.Exception);
                            MessageBox.Show(TextResources.StartupMessage("program.main.messageBoxShow"), TextResources.StartupMessage("app.displayName"), MessageBoxButton.OK, MessageBoxImage.Information);
                            args.Handled = true; app.Shutdown(1);
                        };
                        var store = new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "settings.json"));
                        var settings = store.Load();
                        TextResources.ConfigureStartupLanguage(settings.UiLanguage);
                        var onboarding = OnboardingUpgrade.Prepare(settings, ReleaseInfo.DisplayVersion);
                        if(onboarding.Changed)store.Save(settings);
                        var pet = new PetWindow(null, settings, store, wake);
                        bool resume=Array.IndexOf(startupArgs,ApplicationRestart.ResumeArgument)>=0;
                        pet.Loaded+=delegate {pet.Updates.StartAutomaticChecks();if(onboarding.ShowGuide||resume)pet.Dispatcher.BeginInvoke(new Action(pet.ShowOnboarding));};
                        pet.Closed += delegate { app.Shutdown(); }; normalExit=app.Run(pet)==0;
                    }
                    catch (Exception error)
                    {
                        RuntimeDiagnostics.Record(error);
                        MessageBox.Show(TextResources.StartupMessage("program.main.messageBoxShow.alternativeA"), TextResources.StartupMessage("app.displayName"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            if(normalExit&&ApplicationRestart.Requested)ApplicationRestart.Launch();
        }
    }
}
