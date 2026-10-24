using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class DataActionConfirmationWindow:Window
{
    readonly Func<Task<string>> execute;readonly Func<Task<string>>? flush;readonly Button confirm,cancel;readonly TextBlock status;bool busy,started;
    internal Task PendingOperation {get;private set;}=Task.CompletedTask;
    internal DataActionConfirmationWindow(string title,string scope,Func<Task<string>> action,Func<Task<string>>? flushPending=null)
    {
        execute=action;flush=flushPending;Name="DataActionConfirmation";Title=title;Width=520;Height=440;MinWidth=380;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        status=Ui.Text("");status.Name="DataActionStatus";var note=Ui.Text(scope);note.Name="DataActionScope";
        confirm=Ui.Button(title,()=>PendingOperation=ExecuteAsync());confirm.Name="ConfirmDataAction";
        cancel=Ui.Button(TextResources.Get("common.cancel"),Close);cancel.Name="CancelDataAction";
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};foreach(var button in new[]{cancel,confirm}){button.Margin=new Thickness(4,2);actions.Children.Add(button);}
        var grid=new Grid{Margin=new Thickness(20),RowDefinitions=new RowDefinitions("*,Auto")};grid.Children.Add(new ScrollViewer{Content=Ui.Column(Ui.Text(title,20),note,status)});Grid.SetRow(actions,1);grid.Children.Add(actions);Content=grid;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;if(!busy)Close();}};Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    async Task ExecuteAsync()
    {
        if(started)return;started=busy=true;confirm.IsEnabled=cancel.IsEnabled=false;status.Text=TextResources.Get("dataManagement.processing");
        try{status.Text=await execute();}
        catch(InvalidOperationException){status.Text=TextResources.Get("dataMaintenance.busy");}
        catch(Exception){status.Text=TextResources.Get("dataManagement.failed");}
        finally{if(flush!=null){string warning=await flush();if(!string.IsNullOrEmpty(warning))status.Text+="\n"+warning;}busy=false;cancel.IsEnabled=true;cancel.Content=TextResources.Get("dataManagement.close");}
    }
}
