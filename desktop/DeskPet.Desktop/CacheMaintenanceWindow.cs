using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Dafeiyu;
using Dafeiyu.Maintenance;

namespace DeskPet.Desktop;

internal sealed class CacheMaintenanceWindow:Window
{
    readonly CacheMaintenanceService service;
    readonly TextBlock summary,status;
    readonly Button clean,rescan;
    bool busy,closed;
    internal CacheScan? Scan {get;private set;}
    internal CacheCleanResult? LastResult {get;private set;}
    internal Task PendingOperation {get;private set;}
    internal CacheMaintenanceWindow(string dataRoot)
    {
        service=new CacheMaintenanceService(dataRoot);Title=TextResources.Get("cache.title");Width=500;Height=440;MinWidth=380;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;Background=DesktopTheme.Surface;
        summary=Ui.Text("");summary.Name="CacheSummary";status=Ui.Text("",12);status.Name="CacheStatus";
        var note=Ui.Text(TextResources.Get("cache.preserved"),12);note.Name="CachePreservedData";
        clean=Ui.Button(TextResources.Get("cache.clean"),()=>PendingOperation=CleanAsync());clean.Name="CleanCache";
        rescan=Ui.Button(TextResources.Get("cache.rescan"),()=>PendingOperation=RefreshAsync());rescan.Name="RescanCache";
        var cancel=Ui.Button(TextResources.Get("common.cancel"),Close);cancel.Name="CancelCache";
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};foreach(var button in new[]{clean,rescan,cancel}){button.Margin=new Thickness(4,2);actions.Children.Add(button);}
        var grid=new Grid{RowDefinitions=new RowDefinitions("*,Auto"),Margin=new Thickness(20)};grid.Children.Add(new ScrollViewer{Content=Ui.Column(Ui.Text(Title,20),summary,note,status)});Grid.SetRow(actions,1);grid.Children.Add(actions);Content=grid;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};Closed+=(_,_)=>closed=true;PendingOperation=RefreshAsync();
    }
    void Busy(bool value){busy=value;clean.IsEnabled=!value&&Scan?.TotalBytes>0;rescan.IsEnabled=!value;}
    async Task RefreshAsync()
    {
        if(busy||closed)return;Busy(true);status.Text=TextResources.Get("cache.scanning");
        try{var scan=await service.ScanAsync();if(closed)return;Scan=scan;summary.Text=CachePresentation.Describe(scan);status.Text=LastResult==null?"":CachePresentation.Describe(LastResult);}
        catch(Exception){if(!closed)status.Text=TextResources.Get("cache.failed");Scan=null;}
        finally{if(!closed)Busy(false);}
    }
    async Task CleanAsync()
    {
        if(busy||closed||Scan==null||Scan.TotalBytes==0)return;Busy(true);status.Text=TextResources.Get("cache.cleaning");
        try{var result=await service.CleanAsync(Scan);if(closed)return;LastResult=result;status.Text=CachePresentation.Describe(result);Scan=await service.ScanAsync();if(!closed)summary.Text=CachePresentation.Describe(Scan);}
        catch(Exception){if(!closed)status.Text=TextResources.Get("cache.failed");Scan=null;}
        finally{if(!closed)Busy(false);}
    }
}
