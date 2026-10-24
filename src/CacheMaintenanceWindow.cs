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
        readonly CacheMaintenanceService service;readonly TextBlock summary,status,updates,downloads,protectedCount;readonly Button clean,rescan;
        bool busy,closed;
        internal CacheScan Scan {get;private set;}
        internal CacheCleanResult LastResult {get;private set;}
        internal Task PendingOperation {get;private set;}
        public CacheMaintenanceWindow(string dataRoot)
        {
            service=new CacheMaintenanceService(dataRoot);Title=TextResources.Get("cache.title");Width=500;Height=440;MinWidth=380;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
            Background=ThemeManager.Surface;Foreground=Ui.Ink;FontFamily=new FontFamily("Global User Interface");
            var grid=new Grid{Margin=new Thickness(20)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();body.Children.Add(DataManagementVisual.Header("folder",Title,18));var label=Ui.Text(TextResources.Get("cache.spaceLabel"),12,Ui.Muted);label.Margin=new Thickness(0,16,0,0);body.Children.Add(label);summary=Ui.Text("",32);summary.FontWeight=FontWeights.SemiBold;summary.Name="CacheSummary";summary.Margin=new Thickness(0,4,0,14);body.Children.Add(summary);
            updates=Ui.Text("",15);updates.Name="CacheUpdates";downloads=Ui.Text("",15);downloads.Name="CacheDownloads";protectedCount=Ui.Text("",12,Ui.Muted);protectedCount.Name="CacheProtectedCount";var categories=new StackPanel();categories.Children.Add(DataManagementVisual.Category("file",TextResources.Get("cache.updateCategory"),updates));categories.Children.Add(DataManagementVisual.Category("download",TextResources.Get("cache.downloadCategory"),downloads));categories.Children.Add(protectedCount);body.Children.Add(DataManagementVisual.Card(categories));
            var preserved=Ui.Text(TextResources.Get("cache.preserved"),12,Ui.Muted);preserved.Name="CachePreservedData";body.Children.Add(preserved);
            status=Ui.Text("",12);status.Name="CacheStatus";status.Margin=new Thickness(0,14,0,14);body.Children.Add(status);grid.Children.Add(Ui.Scroll(body));
            var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};clean=Ui.Button(TextResources.Get("cache.clean"),true);clean.Name="CleanCache";clean.Click+=delegate{PendingOperation=Start(CleanAsync);};
            rescan=Ui.Button(TextResources.Get("cache.rescan"));rescan.Name="RescanCache";rescan.Click+=delegate{PendingOperation=Start(RefreshAsync);};
            var cancel=Ui.Button(TextResources.Get("common.cancel"));cancel.Name="CancelCache";cancel.IsCancel=true;cancel.Click+=delegate{Close();};foreach(var button in new[]{clean,rescan,cancel}){DataManagementVisual.Button(button);button.Margin=new Thickness(4,2,4,2);actions.Children.Add(button);}Grid.SetRow(actions,1);grid.Children.Add(actions);Content=grid;
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};Closed+=delegate{closed=true;};PendingOperation=Start(RefreshAsync);
        }
        void Render(CacheScan scan)
        {
            summary.Text=CachePresentation.FormatBytes(scan.TotalBytes);System.Windows.Automation.AutomationProperties.SetName(summary,TextResources.Format("cache.total",scan.TotalBytes));
            updates.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"updates"));downloads.Text=CachePresentation.FormatBytes(CachePresentation.CategoryBytes(scan,"downloads"));protectedCount.Text=TextResources.Format("cache.protected",scan.ProtectedItems,scan.FailedItems);
        }
        Task Start(Func<Task> action){return Dispatcher.InvokeAsync(action).Task.Unwrap();}
        void Busy(bool value){busy=value;clean.IsEnabled=!value&&Scan!=null&&Scan.TotalBytes>0;rescan.IsEnabled=!value;}
        async Task RefreshAsync()
        {
            if(busy||closed)return;Busy(true);status.Text=TextResources.Get("cache.scanning");
            try{var scan=await service.ScanAsync();if(closed)return;Scan=scan;Render(scan);status.Text=LastResult==null?"":CachePresentation.Describe(LastResult);}
            catch(Exception){if(!closed)status.Text=TextResources.Get("cache.failed");Scan=null;}
            finally{if(!closed)Busy(false);}
        }
        async Task CleanAsync()
        {
            if(busy||closed||Scan==null||Scan.TotalBytes==0)return;Busy(true);status.Text=TextResources.Get("cache.cleaning");
            try{var result=await service.CleanAsync(Scan);if(closed)return;LastResult=result;status.Text=CachePresentation.Describe(result);Scan=await service.ScanAsync();if(!closed)Render(Scan);}
            catch(Exception){if(!closed)status.Text=TextResources.Get("cache.failed");Scan=null;}
            finally{if(!closed)Busy(false);}
        }
    }
}
