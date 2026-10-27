using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class CompanionPreparationWindow:Window
{
    internal CompanionPreparationWindow(DesktopController owner)
    {
        var preparation=owner.OfficialPreparation;LocalizedUi.Set(this,"Title",()=>(TextResources.Get("onboarding.prepare.progress")));Name="OfficialPreparationProgress";Width=520;Height=330;MinWidth=390;MinHeight=260;
        var status=Ui.Text("");var bar=new ProgressBar{Minimum=0,Maximum=100};
        var start=LocalizedUi.Button(()=>(TextResources.Get("onboarding.prepare.start")),()=>owner.Run(preparation.StartAsync));start.Name="StartOfficialPreparation";start.Content=LocalizedUi.Text(()=>(TextResources.Get("onboarding.prepare.start")));start.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;
        var cancel=LocalizedUi.Button(()=>(TextResources.Get("onboarding.prepare.cancel")),preparation.Cancel);
        Content=new Border{Padding=new Avalonia.Thickness(22),Child=new ScrollViewer{Content=Ui.Column(status,bar,start,cancel,LocalizedUi.Button(()=>(TextResources.Get("common.close")),Close)),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled}};
        bool closed=false;void Refresh()=>Dispatcher.UIThread.Post(()=>{if(closed)return;LocalizedUi.Set(status,"Text",()=>(preparation.StatusText));bar.IsIndeterminate=preparation.State==OfficialCompanionPreparationState.Running&&preparation.Progress==null;bar.Value=preparation.Progress?.Percent??0;start.IsEnabled=preparation.State!=OfficialCompanionPreparationState.Running;cancel.IsEnabled=!start.IsEnabled;});
        preparation.Changed+=Refresh;Closed+=(_,_)=>{closed=true;preparation.Changed-=Refresh;};Refresh();
    }
}
