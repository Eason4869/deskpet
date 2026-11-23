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
        service=new CacheMaintenanceService(dataRoot);LocalizedUi.Set(this,"Title",()=>TextResources.Get("cache.title"));Width=520;Height=460;SizeToContent=SizeToContent.Height;MinWidth=380;MinHeight=260;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        summary=SettingsVisual.Text("",30);summary.LineHeight=36;summary.FontWeight=Avalonia.Media.FontWeight.SemiBold;summary.Name="CacheSummary";status=SettingsVisual.Text("",12);status.Name="CacheStatus";status.LineHeight=18;status.MinHeight=18;status.Margin=new Thickness(0,9,0,0);
        updates=SettingsVisual.Text("",13);updates.Name="CacheUpdates";downloads=SettingsVisual.Text("",13);downloads.Name="CacheDownloads";versions=SettingsVisual.Text("",13);versions.Name="CachePackVersions";protectedCount=SettingsVisual.Text("",12,true);protectedCount.Name="CacheProtectedCount";protectedCount.LineHeight=18;
        var note=SettingsVisual.Text(()=>TextResources.Get("cache.preservedLatest"),12,true);note.Name="CachePreservedData";note.LineHeight=19;note.Margin=new Thickness(0,13,0,0);
        clean=LocalizedUi.Button(()=>(TextResources.Get("cache.clean")),()=>PendingOperation=CleanAsync());clean.Name="CleanCache";
        rescan=LocalizedUi.Button(()=>(TextResources.Get("cache.rescan")),()=>PendingOperation=RefreshAsync());rescan.Name="RescanCache";
        var cancel=LocalizedUi.Button(()=>(TextResources.Get("common.cancel")),Close);cancel.Name="CancelCache";
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};foreach(var button in new[]{rescan,cancel,clean}){SettingsVisual.StyleButton(button);button.MinHeight=34;button.Padding=new Thickness(14,6);button.Margin=new Thickness(8,0,0,0);actions.Children.Add(button);}clean.Background=DesktopTheme.Brush("#466CFF");clean.BorderBrush=clean.Background;clean.Foreground=Avalonia.Media.Brushes.White;((TextBlock)clean.Content!).Foreground=clean.Foreground;
        Control Category(string icon,Func<string> title,TextBlock value,bool divider){var row=new Grid{ColumnDefinitions=new ColumnDefinitions("28,*,Auto"),MinHeight=36};row.Children.Add(SettingsVisual.Icon(icon,18));var label=SettingsVisual.Text(title,13);label.LineHeight=18;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0,6,12,6);Grid.SetColumn(label,1);row.Children.Add(label);value.LineHeight=18;value.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(value,2);row.Children.Add(value);return new Border{Child=row,BorderBrush=DesktopTheme.CardBorder,BorderThickness=new Thickness(0,divider?1:0,0,0)};}
        var total=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(0,0,0,12)};var label=SettingsVisual.Text(()=>TextResources.Get("cache.spaceLabel"),13,true);label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0,0,12,0);total.Children.Add(label);Grid.SetColumn(summary,1);total.Children.Add(summary);
        var heading=new Grid{ColumnDefinitions=new ColumnDefinitions("30,*"),Margin=new Thickness(0,0,32,16)};heading.Children.Add(SettingsVisual.Icon("folder",22));var title=SettingsVisual.Text(()=>TextResources.Get("cache.title"),18);title.FontWeight=Avalonia.Media.FontWeight.SemiBold;title.LineHeight=26;Grid.SetColumn(title,1);heading.Children.Add(title);
        var rows=new StackPanel{Children={Category("file",()=>TextResources.Get("cache.updateCategory"),updates,false),Category("download",()=>TextResources.Get("cache.downloadCategory"),downloads,true),Category("folder",()=>TextResources.Get("cache.packVersionCategory"),versions,true)}};
        var card=new Border{Child=rows,Padding=new Thickness(14,4),CornerRadius=new CornerRadius(12),Background=DesktopTheme.CardSurface,BorderBrush=DesktopTheme.CardBorder,BorderThickness=new Thickness(1)};
        var protection=new Grid{ColumnDefinitions=new ColumnDefinitions("20,*"),Margin=new Thickness(0,8,0,0)};protection.Children.Add(SettingsVisual.Icon("shield",14));Grid.SetColumn(protectedCount,1);protection.Children.Add(protectedCount);
        var body=new StackPanel{Children={heading,total,card,protection,note,status}};
        var grid=new Grid{RowDefinitions=new RowDefinitions("*,Auto"),Margin=new Thickness(24,22,24,20)};grid.Children.Add(new ScrollViewer{HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Content=body});Grid.SetRow(actions,1);actions.Margin=new Thickness(0,10,0,0);grid.Children.Add(actions);DialogUi.Apply(this,grid);
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
        try{var scan=await service.ScanAsync();if(closed)return;Scan=scan;Render(scan);LocalizedUi.Set(status,"Text",()=>LastResult!=null?CachePresentation.Describe(LastResult):scan.FailedItems>0?TextResources.Get("cache.scanPartial"):scan.TotalBytes==0?TextResources.Get("cache.empty"):TextResources.Get("cache.ready"));}
        catch(Exception){if(!closed)LocalizedUi.Set(status,"Text",()=>(TextResources.Get("cache.failed")));Scan=null;}
        finally{if(!closed)Busy(false);}
    }
    async Task CleanAsync()
    {
        if(busy||closed||Scan==null||Scan.TotalBytes==0)return;Busy(true);LocalizedUi.Set(status,"Text",()=>(TextResources.Get("cache.cleaning")));
        try{var result=await service.CleanAsync(Scan);if(closed)return;LastResult=result;LocalizedUi.Set(status,"Text",()=>CachePresentation.Describe(result));Scan=await service.ScanAsync();if(!closed)Render(Scan);}
        catch(Exception){if(!closed)LocalizedUi.Set(status,"Text",()=>(TextResources.Get("cache.failed")));Scan=null;}
        finally{if(!closed)Busy(false);}
    }
}
