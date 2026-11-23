using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    public sealed class CacheMaintenanceWindow:Window
    {
        readonly CacheMaintenanceService service;readonly TextBlock summary,status,updates,downloads,versions,protectedCount;readonly Button clean,rescan;
        bool busy,closed;
        internal CacheScan Scan {get;private set;}
        internal CacheCleanResult LastResult {get;private set;}
        internal Task PendingOperation {get;private set;}
        public CacheMaintenanceWindow(string dataRoot)
        {
            service=new CacheMaintenanceService(dataRoot);LocalizedUi.Set(this,"Title",()=>(TextResources.Get("cache.title")));Width=520;Height=440;SizeToContent=SizeToContent.Height;MinWidth=380;MinHeight=260;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            var grid=new Grid{Margin=new Thickness(24,22,24,20)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();var heading=DataManagementVisual.Header("folder",()=>TextResources.Get("cache.title"),18);heading.Margin=new Thickness(0,0,32,16);body.Children.Add(heading);
            var total=new Grid{Margin=new Thickness(0,0,0,12)};total.ColumnDefinitions.Add(new ColumnDefinition());total.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var label=LocalizedUi.Text(()=>TextResources.Get("cache.spaceLabel"),13,Ui.Muted);label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0,0,12,0);total.Children.Add(label);
            summary=Ui.Text("",30);summary.LineHeight=36;summary.FontWeight=FontWeights.SemiBold;summary.Name="CacheSummary";summary.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(summary,1);total.Children.Add(summary);body.Children.Add(total);
            updates=Ui.Text("",13);updates.Name="CacheUpdates";downloads=Ui.Text("",13);downloads.Name="CacheDownloads";versions=Ui.Text("",13);versions.Name="CachePackVersions";
            var categories=new StackPanel();categories.Children.Add(Category("file",()=>TextResources.Get("cache.updateCategory"),updates,false));categories.Children.Add(Category("download",()=>TextResources.Get("cache.downloadCategory"),downloads,true));categories.Children.Add(Category("folder",()=>TextResources.Get("cache.packVersionCategory"),versions,true));
            body.Children.Add(new Border{Child=categories,Padding=new Thickness(14,4,14,4),CornerRadius=new CornerRadius(12),Background=ThemeManager.CardSurface,BorderBrush=ThemeManager.CardBorder,BorderThickness=new Thickness(1)});
            protectedCount=Ui.Text("",12,Ui.Muted);protectedCount.Name="CacheProtectedCount";protectedCount.LineHeight=18;var protection=new DockPanel{Margin=new Thickness(0,8,0,0)};var shield=DataManagementVisual.Icon("shield",14);shield.Margin=new Thickness(0,0,6,0);DockPanel.SetDock(shield,Dock.Left);protection.Children.Add(shield);protection.Children.Add(protectedCount);body.Children.Add(protection);
            var preserved=LocalizedUi.Text(()=>TextResources.Get("cache.preservedLatest"),12,Ui.Muted);preserved.Name="CachePreservedData";preserved.LineHeight=19;preserved.Margin=new Thickness(0,13,0,0);body.Children.Add(preserved);
            status=Ui.Text("",12);status.Name="CacheStatus";status.LineHeight=18;status.MinHeight=18;status.Margin=new Thickness(0,9,0,0);body.Children.Add(status);grid.Children.Add(Ui.Scroll(body));
            var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};clean=LocalizedUi.Button(()=>TextResources.Get("cache.clean"),true);clean.Name="CleanCache";clean.Click+=delegate{PendingOperation=Start(CleanAsync);};
            rescan=LocalizedUi.Button(()=>(TextResources.Get("cache.rescan")));rescan.Name="RescanCache";rescan.Click+=delegate{PendingOperation=Start(RefreshAsync);};
            var cancel=LocalizedUi.Button(()=>TextResources.Get("common.cancel"));cancel.Name="CancelCache";cancel.IsCancel=true;cancel.Click+=delegate{Close();};foreach(var button in new[]{rescan,cancel,clean}){button.MinHeight=34;button.Padding=new Thickness(14,6,14,6);button.Margin=new Thickness(8,0,0,0);actions.Children.Add(button);}Grid.SetRow(actions,1);grid.Children.Add(actions);DialogUi.Apply(this,grid);
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};Closed+=delegate{closed=true;};PendingOperation=Start(RefreshAsync);
        }
        static FrameworkElement Category(string icon,Func<string> title,TextBlock value,bool divider)
        {
            var row=new Grid{MinHeight=36};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Children.Add(DataManagementVisual.Icon(icon,18));
            var label=LocalizedUi.Text(title,13);label.LineHeight=18;label.Margin=new Thickness(0,6,12,6);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);value.LineHeight=18;value.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(value,2);row.Children.Add(value);
            return new Border{Child=row,BorderBrush=ThemeManager.CardBorder,BorderThickness=new Thickness(0,divider?1:0,0,0)};
        }
        void Render(CacheScan scan)
        {
            summary.Text=CachePresentation.FormatBytes(scan.TotalBytes);LocalizedUi.Automation(summary,()=>(TextResources.Format("cache.total",scan.TotalBytes)));
            updates.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"updates"));downloads.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"downloads"));versions.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"pack-versions"));LocalizedUi.Set(protectedCount,"Text",()=>(TextResources.Format("cache.protected",scan.ProtectedItems,scan.FailedItems)));
        }
        Task Start(Func<Task> action){return Dispatcher.InvokeAsync(action).Task.Unwrap();}
        void Busy(bool value){busy=value;clean.IsEnabled=!value&&Scan!=null&&Scan.TotalBytes>0;rescan.IsEnabled=!value;}
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
}
