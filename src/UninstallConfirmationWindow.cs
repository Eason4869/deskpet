using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed class UninstallConfirmationWindow:Window
    {
        readonly PetWindow pet;readonly Button confirm;readonly TextBlock status;readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        bool started;
        internal Task PendingOperation {get;private set;}
        public UninstallConfirmationWindow(PetWindow owner)
        {
            pet=owner;PendingOperation=Task.FromResult(0);Name="UninstallConfirmation";Title=TextResources.Get("uninstall.title");Width=520;MinWidth=380;MinHeight=340;MaxHeight=560;SizeToContent=SizeToContent.Height;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            Background=ThemeManager.Surface;Foreground=Ui.Ink;FontFamily=new FontFamily("Global User Interface");
            var grid=new Grid{Margin=new Thickness(20)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();body.Children.Add(DataManagementVisual.Header("warning",Title,21));var scope=Ui.Text(TextResources.Get("uninstall.scope"),13);scope.Foreground=Ui.Muted;scope.Margin=new Thickness(0,16,0,16);body.Children.Add(scope);status=Ui.Text("",13);status.Name="UninstallStatus";body.Children.Add(status);grid.Children.Add(Ui.Scroll(body));
            var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
            var cancel=Ui.Button(TextResources.Get("common.cancel"));cancel.Name="CancelUninstall";cancel.Click+=delegate{Close();};DataManagementVisual.Button(cancel);actions.Children.Add(cancel);
            confirm=Ui.Button(Title);confirm.Name="ConfirmUninstall";DataManagementVisual.Button(confirm,true);confirm.Click+=delegate{PendingOperation=ExecuteAsync();};actions.Children.Add(confirm);Grid.SetRow(actions,1);grid.Children.Add(actions);Content=grid;
            Loaded+=delegate{cancel.Focus();};KeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};Closed+=delegate{cancellation.Cancel();};
        }
        async Task ExecuteAsync()
        {
            if(started)return;started=true;confirm.IsEnabled=false;status.Text=TextResources.Get("dataManagement.processing");
            try{status.Text=await pet.UninstallAsync(cancellation.Token);}
            catch(OperationCanceledException){}
            catch(Exception error){RuntimeDiagnostics.Record(error);status.Text=TextResources.Get("uninstall.prepareFailed");}
        }
    }
}
