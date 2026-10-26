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
    readonly TextBlock summary,status,updates,downloads,versions,protectedCount;
    readonly Button clean,rescan;
    bool busy,closed;
    internal CacheScan? Scan {get;private set;}
    internal CacheCleanResult? LastResult {get;private set;}
    internal Task PendingOperation {get;private set;}
    internal CacheMaintenanceWindow(string dataRoot)
    {
        service=new CacheMaintenanceService(dataRoot);LocalizedUi.Set(this,"Title",()=>(TextResources.Get("cache.title")));Width=500;Height=440;MinWidth=380;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;Background=DesktopTheme.Surface;
        summary=SettingsVisual.Text("",32);summary.FontWeight=Avalonia.Media.FontWeight.SemiBold;summary.Name="CacheSummary";status=SettingsVisual.Text("",12);status.Name="CacheStatus";
        updates=SettingsVisual.Text("",15);updates.Name="CacheUpdates";downloads=SettingsVisual.Text("",15);downloads.Name="CacheDownloads";versions=SettingsVisual.Text("",15);versions.Name="CachePackVersions";protectedCount=SettingsVisual.Text("",12,true);protectedCount.Name="CacheProtectedCount";
        var note=SettingsVisual.Text(()=> (TextResources.Get("cache.preservedPackVersions")),12,true);note.Name="CachePreservedData";
        clean=LocalizedUi.Button(()=>(TextResources.Get("cache.clean")),()=>PendingOperation=CleanAsync());clean.Name="CleanCache";
        rescan=LocalizedUi.Button(()=>(TextResources.Get("cache.rescan")),()=>PendingOperation=RefreshAsync());rescan.Name="RescanCache";
        var cancel=LocalizedUi.Button(()=>(TextResources.Get("common.cancel")),Close);cancel.Name="CancelCache";
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};foreach(var button in new[]{clean,rescan,cancel}){SettingsVisual.StyleButton(button);button.Margin=new Thickness(4,2);actions.Children.Add(button);}
        Control Category(string icon,System.Func<string> title,Control value){var row=new Grid{ColumnDefinitions=new ColumnDefinitions("24,*,Auto"),Margin=new Thickness(0,8)};row.Children.Add(SettingsVisual.Icon(icon,18));var label=SettingsVisual.Text(()=> (title()),13);label.Margin=new Thickness(10,0,12,0);Grid.SetColumn(label,1);row.Children.Add(label);Grid.SetColumn(value,2);row.Children.Add(value);return row;}
        var total=Ui.Column(SettingsVisual.Text(()=> (TextResources.Get("cache.spaceLabel")),12,true),summary);
        var grid=new Grid{RowDefinitions=new RowDefinitions("*,Auto"),Margin=new Thickness(24)};grid.Children.Add(new ScrollViewer{HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Content=Ui.Column(SettingsVisual.Header("storage",()=> (Title??"")),total,SettingsVisual.Card(Ui.Column(Category("file",()=> (TextResources.Get("cache.updateCategory")),updates),Category("download",()=> (TextResources.Get("cache.downloadCategory")),downloads),Category("folder",()=> (TextResources.Get("cache.packVersionCategory")),versions),protectedCount)),note,status)});Grid.SetRow(actions,1);actions.Margin=new Thickness(0,14,0,0);grid.Children.Add(actions);Content=grid;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};Closed+=(_,_)=>closed=true;PendingOperation=RefreshAsync();
    }
    void Render(CacheScan scan)
    {
        summary.Text=CachePresentation.FormatBytes(scan.TotalBytes);LocalizedUi.Automation(summary,()=>(TextResources.Format("cache.total",scan.TotalBytes)));
        updates.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"updates"));downloads.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"downloads"));versions.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"pack-versions"));LocalizedUi.Set(protectedCount,"Text",()=>(TextResources.Format("cache.protected",scan.ProtectedItems,scan.FailedItems)));
    }
    void Busy(bool value){busy=value;clean.IsEnabled=!value&&Scan?.TotalBytes>0;rescan.IsEnabled=!value;}
    async Task RefreshAsync()
    {
        if(busy||closed)return;Busy(true);LocalizedUi.Set(status,"Text",()=>(TextResources.Get("cache.scanning")));
        try{var scan=await service.ScanAsync();if(closed)return;Scan=scan;Render(scan);status.Text=LastResult==null?"":CachePresentation.Describe(LastResult);}
        catch(Exception){if(!closed)LocalizedUi.Set(status,"Text",()=>(TextResources.Get("cache.failed")));Scan=null;}
        finally{if(!closed)Busy(false);}
    }
    async Task CleanAsync()
    {
        if(busy||closed||Scan==null||Scan.TotalBytes==0)return;Busy(true);LocalizedUi.Set(status,"Text",()=>(TextResources.Get("cache.cleaning")));
        try{var result=await service.CleanAsync(Scan);if(closed)return;LastResult=result;status.Text=CachePresentation.Describe(result);Scan=await service.ScanAsync();if(!closed)Render(Scan);}
        catch(Exception){if(!closed)LocalizedUi.Set(status,"Text",()=>(TextResources.Get("cache.failed")));Scan=null;}
        finally{if(!closed)Busy(false);}
    }
}
