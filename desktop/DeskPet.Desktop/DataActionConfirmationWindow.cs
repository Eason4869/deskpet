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
        execute=action;flush=flushPending;Name="DataActionConfirmation";Title=title;Width=520;SizeToContent=SizeToContent.Height;MaxHeight=560;MinWidth=380;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=DesktopTheme.Surface;
        status=SettingsVisual.Text("",13);status.Name="DataActionStatus";var note=SettingsVisual.Text(scope,13,true);note.Name="DataActionScope";
        confirm=Ui.Button(title,()=>PendingOperation=ExecuteAsync());confirm.Name="ConfirmDataAction";SettingsVisual.StyleButton(confirm,title==TextResources.Get("dataManagement.clearHistory"));
        cancel=Ui.Button(TextResources.Get("common.cancel"),Close);cancel.Name="CancelDataAction";SettingsVisual.StyleButton(cancel);
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};foreach(var button in new[]{cancel,confirm}){button.Margin=new Thickness(4,2);actions.Children.Add(button);}
        var heading=SettingsVisual.Text(title,21);heading.FontWeight=Avalonia.Media.FontWeight.SemiBold;
        var header=new Grid{ColumnDefinitions=new ColumnDefinitions("40,*"),Margin=new Thickness(0,0,0,12)};header.Children.Add(new Border{Width=30,Height=30,Background=DesktopTheme.Brush("#FFF3F5"),CornerRadius=new CornerRadius(9),Child=SettingsVisual.Icon("warning",20),VerticalAlignment=VerticalAlignment.Top});Grid.SetColumn(heading,1);header.Children.Add(heading);
        var grid=new Grid{Margin=new Thickness(24),RowDefinitions=new RowDefinitions("*,Auto")};grid.Children.Add(new ScrollViewer{HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Content=Ui.Column(header,note,status)});Grid.SetRow(actions,1);actions.Margin=new Thickness(0,16,0,0);grid.Children.Add(actions);Content=grid;Opened+=(_,_)=>cancel.Focus();
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
