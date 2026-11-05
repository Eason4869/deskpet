using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class LocalImportUiTests
    {
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
            var host=new Window{Width=300,Height=150};
            host.Loaded+=async delegate{
                try{
                    foreach(string button in new[]{"CancelCompanionImport","CloseDialog"}){
                        bool waitingForWorker=false;
                        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
                        timer.Tick+=delegate{
                            var progress=app.Windows.OfType<LocalCompanionImportWindow>().FirstOrDefault();if(progress==null)return;timer.Stop();
                            if(progress.WindowStyle!=WindowStyle.None)throw new Exception("Import uses application chrome");
                            Find(progress).Single(b=>b.Name==button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            waitingForWorker=progress.IsVisible;
                        };
                        timer.Start();bool cancelled=false;
                        try{await LocalCompanionImportWindow.RunAsync<int>(host,token=>{token.WaitHandle.WaitOne(5000);Thread.Sleep(150);token.ThrowIfCancellationRequested();return 1;});}
                        catch(OperationCanceledException){cancelled=true;}
                        finally{timer.Stop();}
                        if(!cancelled||!waitingForWorker||app.Windows.OfType<LocalCompanionImportWindow>().Any())throw new Exception(button+" must wait for worker cancellation before closing");
                        Console.WriteLine("PASS import "+button+" waits for cancellation acknowledgement");
                    }
                    result=0;
                }catch(Exception error){Console.Error.WriteLine(error);}
                finally{host.Close();app.Shutdown();}
            };
            host.Show();app.Run();return result;
        }
        static System.Collections.Generic.IEnumerable<Button> Find(DependencyObject root)
        {
            if(root is Button)yield return (Button)root;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var button in Find(VisualTreeHelper.GetChild(root,i)))yield return button;
        }
    }
}
