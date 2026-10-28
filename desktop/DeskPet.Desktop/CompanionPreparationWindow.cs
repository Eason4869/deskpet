using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class CompanionPreparationWindow:Window
{
    internal CompanionPreparationWindow(DesktopController owner)
    {
        var preparation=owner.OfficialPreparation;LocalizedUi.Set(this,"Title",()=>TextResources.Get("onboarding.prepare.progress"));Name="OfficialPreparationProgress";Width=580;Height=540;MinWidth=480;MinHeight=430;
        CanResize=false;CanMaximize=false;CanMinimize=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=DesktopTheme.Surface;
        string Caption()=>preparation.State is OfficialCompanionPreparationState.Failed or OfficialCompanionPreparationState.Cancelled?TextResources.Get("common.retry"):TextResources.Get("onboarding.prepare.start");
        string Status()=>preparation.State==OfficialCompanionPreparationState.Idle?TextResources.Get("onboarding.prepare.packs"):preparation.StatusText;
        string Detail(){
            var value=preparation.Progress;if(value==null)return TextResources.Get("onboarding.prepare.progress");
            var profile=OfficialCompanionProfiles.Find(value.CharacterId);
            string stage=value.Stage=="metadata"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage"):value.Stage=="download"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeA"):value.Stage=="validate"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeB"):value.Stage=="install"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeC"):TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeD");
            return (profile==null?TextResources.Get("officialCompanionProgressWindow.report.nullProfileName"):profile.DisplayName)+" · "+stage;
        }
        var root=new Grid{Margin=new Thickness(24),RowDefinitions=new RowDefinitions("*,Auto")};var body=new StackPanel();
        var heading=new Grid{ColumnDefinitions=new ColumnDefinitions("40,12,*"),Margin=new Thickness(0,0,0,14)};
        heading.Children.Add(new Border{Width=40,Height=40,CornerRadius=new CornerRadius(12),Background=DesktopTheme.Brush("#EDF3FF"),Child=SettingsVisual.Icon("partner",23)});
        var title=SettingsVisual.Text(()=>TextResources.Get("onboarding.prepare.progress"),20);title.FontWeight=FontWeight.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,2);heading.Children.Add(title);body.Children.Add(heading);
        var explanation=SettingsVisual.Text(()=>TextResources.Get("onboarding.prepare.packsDetail"),12,true);explanation.LineHeight=20;explanation.Margin=new Thickness(0,0,0,18);body.Children.Add(explanation);
        var roles=new Dictionary<string,Border>();var roleRow=new WrapPanel{Margin=new Thickness(0,0,0,12)};
        foreach(var profile in OfficialCompanionProfiles.All){var pill=new Border{Child=SettingsVisual.Text(()=>profile.DisplayName,12),CornerRadius=new CornerRadius(9),Padding=new Thickness(10,6),Margin=new Thickness(0,0,6,6),Background=DesktopTheme.Brush("#F7F8FA")};roles.Add(profile.Id,pill);roleRow.Children.Add(pill);}body.Children.Add(roleRow);
        var detail=SettingsVisual.Text(Detail,12,true);detail.Name="OfficialPreparationCurrentRole";
        var status=SettingsVisual.Text(Status,14);status.Name="OfficialPreparationStatus";status.FontWeight=FontWeight.SemiBold;status.Margin=new Thickness(0,8,0,14);
        var bar=new ProgressBar{Name="OfficialPreparationProgressBar",Minimum=0,Maximum=100,Height=7,Foreground=DesktopTheme.Brush("#456DE6"),Background=DesktopTheme.Brush("#EDF3FF"),BorderThickness=new Thickness(0)};
        body.Children.Add(new Border{Child=new StackPanel{Children={detail,status,bar}},CornerRadius=new CornerRadius(14),Background=DesktopTheme.CardSurface,BorderBrush=DesktopTheme.CardBorder,BorderThickness=new Thickness(1),Padding=new Thickness(18),Margin=new Thickness(0,0,0,16)});
        root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        var start=LocalizedUi.Button(Caption,()=>owner.Run(preparation.StartAsync));start.Name="StartOfficialPreparation";start.HorizontalAlignment=HorizontalAlignment.Stretch;start.MinHeight=40;
        var cancel=LocalizedUi.Button(()=>TextResources.Get("onboarding.prepare.cancel"),preparation.Cancel);cancel.Name="CancelOfficialPreparation";
        var close=LocalizedUi.Button(()=>TextResources.Get("common.close"),Close);close.Name="CloseOfficialPreparation";
        var secondary=new DockPanel{LastChildFill=false,Margin=new Thickness(0,9,0,0)};DockPanel.SetDock(cancel,Dock.Left);secondary.Children.Add(cancel);DockPanel.SetDock(close,Dock.Right);secondary.Children.Add(close);
        var actions=new StackPanel{Margin=new Thickness(0,14,0,0),Children={start,secondary}};Grid.SetRow(actions,1);root.Children.Add(actions);Content=root;
        bool closed=false;int refreshQueued=0;
        void Apply(){
            var state=preparation.State;var value=preparation.Progress;bool running=state==OfficialCompanionPreparationState.Running;
            LocalizedUi.Set(status,"Text",Status);LocalizedUi.Set(detail,"Text",Detail);LocalizedUi.Set(start,"Content",Caption);detail.IsVisible=value!=null;
            bar.IsIndeterminate=running&&(value==null||value.TotalBytes<=0&&value.Percent<=0);bar.Value=state==OfficialCompanionPreparationState.Completed?100:value?.Percent??0;
            status.Foreground=state==OfficialCompanionPreparationState.Failed?DesktopTheme.Brush("#AC3E45"):SettingsVisual.Ink;start.IsEnabled=!running;cancel.IsEnabled=running;
            foreach(var role in roles)role.Value.Background=state==OfficialCompanionPreparationState.Completed?DesktopTheme.Brush("#E8F5EF"):value?.CharacterId==role.Key?DesktopTheme.Brush("#EDF3FF"):DesktopTheme.Brush("#F7F8FA");
        }
        void Refresh(){if(Interlocked.Exchange(ref refreshQueued,1)!=0)return;Dispatcher.UIThread.Post(()=>{Interlocked.Exchange(ref refreshQueued,0);if(!closed)Apply();});}
        preparation.Changed+=Refresh;Closed+=(_,_)=>{closed=true;preparation.Changed-=Refresh;};Apply();
    }
}
