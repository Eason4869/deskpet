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
        readonly CacheMaintenanceService service;readonly TextBlock summary,status;readonly Button clean,rescan;
        bool busy,closed;
        internal CacheScan Scan {get;private set;}
        internal CacheCleanResult LastResult {get;private set;}
        internal Task PendingOperation {get;private set;}
        public CacheMaintenanceWindow(string dataRoot)
        {
            service=new CacheMaintenanceService(dataRoot);Title=TextResources.Get("cache.title");Width=500;Height=440;MinWidth=380;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
            Background=ThemeManager.Surface;Foreground=Ui.Ink;FontFamily=new FontFamily("Global User Interface");
            var grid=new Grid{Margin=new Thickness(20)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();body.Children.Add(Ui.Text(Title,20));summary=Ui.Text("",13);summary.Name="CacheSummary";summary.Margin=new Thickness(0,14,0,14);body.Children.Add(summary);
            var preserved=Ui.Text(TextResources.Get("cache.preserved"),12,Ui.Muted);preserved.Name="CachePreservedData";body.Children.Add(preserved);
            status=Ui.Text("",12);status.Name="CacheStatus";status.Margin=new Thickness(0,14,0,14);body.Children.Add(status);grid.Children.Add(Ui.Scroll(body));
            var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};clean=Ui.Button(TextResources.Get("cache.clean"),true);clean.Name="CleanCache";clean.Click+=delegate{PendingOperation=Start(CleanAsync);};
            rescan=Ui.Button(TextResources.Get("cache.rescan"));rescan.Name="RescanCache";rescan.Click+=delegate{PendingOperation=Start(RefreshAsync);};
            var cancel=Ui.Button(TextResources.Get("common.cancel"));cancel.Name="CancelCache";cancel.IsCancel=true;cancel.Click+=delegate{Close();};actions.Children.Add(clean);actions.Children.Add(rescan);actions.Children.Add(cancel);Grid.SetRow(actions,1);grid.Children.Add(actions);Content=grid;
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};Closed+=delegate{closed=true;};PendingOperation=Start(RefreshAsync);
        }
        Task Start(Func<Task> action){return Dispatcher.InvokeAsync(action).Task.Unwrap();}
        void Busy(bool value){busy=value;clean.IsEnabled=!value&&Scan!=null&&Scan.TotalBytes>0;rescan.IsEnabled=!value;}
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
}
