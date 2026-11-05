using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class ImportDialogUiChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        void Until(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(10);while(!ready()){if(DateTime.UtcNow>end)throw new TimeoutException("Import dialog lifecycle");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        var host=new Window{Width=300,Height=150};host.Show();
        try{
            foreach(string action in new[]{"CancelCompanionImport","CloseDialog"}){
                using var acknowledged=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                var run=LocalCompanionImportWindow.RunAsync(host,token=>{token.WaitHandle.WaitOne();acknowledged.Set();release.Wait();token.ThrowIfCancellationRequested();return 1;});
                try{
                    Until(()=>host.OwnedWindows.OfType<LocalCompanionImportWindow>().Any());var dialog=host.OwnedWindows.OfType<LocalCompanionImportWindow>().Single();
                    var cancel=dialog.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="CancelCompanionImport");
                    Check(cancel.IsEnabled&&cancel.Content?.ToString()==TextResources.Get("dialog.cancelImport"),"native import initially offers cancel rather than claiming cancellation");
                    dialog.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name==action).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>acknowledged.IsSet);
                    Check(dialog.IsVisible&&!cancel.IsEnabled&&cancel.Content?.ToString()==TextResources.Get("dialog.cancelling")&&!run.IsCompleted,"native "+action+" reports cancellation while worker remains owned");
                    release.Set();Until(()=>run.IsCompleted);bool cancelled=false;try{run.GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled&&!dialog.IsVisible,"native "+action+" closes after worker acknowledges cancellation");
                }finally{
                    foreach(var dialog in host.OwnedWindows.OfType<LocalCompanionImportWindow>().ToArray())dialog.Close();release.Set();Until(()=>run.IsCompleted);
                }
            }
            return checks;
        }finally{host.Close();}
    }
}
