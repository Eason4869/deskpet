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
        [STAThread] public static void Main()
        {
            string suffix = WindowsIdentity.GetCurrent().User.Value;
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
                            MessageBox.Show("桌宠遇到了一个运行问题。可以退出后重新打开，设置仍保存在 data 文件夹。", "桌面伙伴", MessageBoxButton.OK, MessageBoxImage.Information);
                            args.Handled = true; app.Shutdown(1);
                        };
                        var store = new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "settings.json"));
                        var settings = store.Load();
                        var onboarding = OnboardingUpgrade.Prepare(settings, ReleaseInfo.DisplayVersion);
                        if(onboarding.Changed)store.Save(settings);
                        var pet = new PetWindow(null, settings, store, wake);
                        pet.Loaded+=delegate {pet.Updates.StartAutomaticChecks();if(onboarding.ShowGuide)pet.Dispatcher.BeginInvoke(new Action(pet.ShowOnboarding));};
                        pet.Closed += delegate { app.Shutdown(); }; app.Run(pet);
                    }
                    catch (Exception error)
                    {
                        RuntimeDiagnostics.Record(error);
                        MessageBox.Show("桌面伙伴无法启动。请将完整文件夹解压到可写入的位置，并确认 Windows 已安装 .NET Framework 4.8。", ReleaseInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
        }
    }
}
