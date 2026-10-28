using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    // The application owns preparation; this window owns presentation only.
    internal sealed class CompanionPreparationWindow:Window
    {
        readonly OfficialCompanionPreparation preparation;
        readonly TextBlock status,detail;
        readonly ProgressBar progress;
        readonly Button start,stop;
        readonly Dictionary<string,Border> roles=new Dictionary<string,Border>();
        bool closed;int refreshQueued;
        internal CompanionPreparationWindow(OfficialCompanionPreparation preparation)
        {
            this.preparation=preparation;
            LocalizedUi.Window(this,()=>TextResources.Get("onboarding.prepare.progress"),580,540);
            Name="OfficialPreparationProgress";ResizeMode=ResizeMode.CanMinimize;Background=ThemeManager.Surface;
            var root=new Grid{Margin=new Thickness(24)};root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();var heading=new DockPanel{Margin=new Thickness(0,0,0,14)};
            var icon=new Border{Width=40,Height=40,CornerRadius=new CornerRadius(12),Background=Ui.Pale,Child=ShellIcons.Image("people",Ui.Blue,23),Margin=new Thickness(0,0,12,0)};DockPanel.SetDock(icon,Dock.Left);heading.Children.Add(icon);
            var title=LocalizedUi.Text(()=>TextResources.Get("onboarding.prepare.progress"),20,Ui.PageInk);title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(title);body.Children.Add(heading);
            var explanation=LocalizedUi.Text(()=>TextResources.Get("onboarding.prepare.packsDetail"),12,Ui.Muted);explanation.LineHeight=20;explanation.Margin=new Thickness(0,0,0,18);body.Children.Add(explanation);
            var roleRow=new WrapPanel{Margin=new Thickness(0,0,0,12)};
            foreach(var profile in OfficialCompanionProfiles.All){var name=LocalizedUi.Text(()=>profile.DisplayName,12,Ui.Ink);var pill=new Border{Child=name,CornerRadius=new CornerRadius(9),Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,6,6),Background=ThemeManager.FieldSurface};roles.Add(profile.Id,pill);roleRow.Children.Add(pill);}body.Children.Add(roleRow);
            var progressBody=new StackPanel();detail=LocalizedUi.Text(CurrentDetail,12,Ui.Muted);detail.Name="OfficialPreparationCurrentRole";progressBody.Children.Add(detail);
            status=LocalizedUi.Text(StatusText,14,Ui.Ink);status.Name="OfficialPreparationStatus";status.FontWeight=FontWeights.SemiBold;status.Margin=new Thickness(0,8,0,14);progressBody.Children.Add(status);
            progress=new ProgressBar{Name="OfficialPreparationProgressBar",Minimum=0,Maximum=100,Height=7,Foreground=Ui.Blue,Background=Ui.Pale,BorderThickness=new Thickness(0)};progressBody.Children.Add(progress);body.Children.Add(Ui.Card(progressBody));
            root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var actions=new StackPanel{Margin=new Thickness(0,14,0,0)};
            start=LocalizedUi.Button(StartCaption,true);start.Name="StartOfficialPreparation";start.HorizontalAlignment=HorizontalAlignment.Stretch;start.MinHeight=40;start.Click+=delegate{var ignored=preparation.StartAsync();};actions.Children.Add(start);
            var secondary=new DockPanel{Margin=new Thickness(0,9,0,0),LastChildFill=false};
            stop=LocalizedUi.Button(()=>TextResources.Get("onboarding.prepare.cancel"));stop.Name="CancelOfficialPreparation";stop.Click+=delegate{preparation.Cancel();};DockPanel.SetDock(stop,Dock.Left);secondary.Children.Add(stop);
            var close=LocalizedUi.Button(()=>TextResources.Get("common.close"));close.Name="CloseOfficialPreparation";close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);secondary.Children.Add(close);actions.Children.Add(secondary);Grid.SetRow(actions,1);root.Children.Add(actions);Content=root;
            preparation.Changed+=RequestRefresh;Closed+=delegate{closed=true;preparation.Changed-=RequestRefresh;};Refresh();
        }
        string StartCaption(){return preparation.State==OfficialCompanionPreparationState.Failed||preparation.State==OfficialCompanionPreparationState.Cancelled?TextResources.Get("common.retry"):TextResources.Get("onboarding.prepare.start");}
        string StatusText(){return preparation.State==OfficialCompanionPreparationState.Idle?TextResources.Get("onboarding.prepare.packs"):preparation.StatusText;}
        string CurrentDetail()
        {
            var value=preparation.Progress;if(value==null)return TextResources.Get("onboarding.prepare.progress");
            var profile=OfficialCompanionProfiles.Find(value.CharacterId);
            string stage=value.Stage=="metadata"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage"):value.Stage=="download"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeA"):value.Stage=="validate"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeB"):value.Stage=="install"?TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeC"):TextResources.Get("officialCompanionProgressWindow.report.stageValueStage.alternativeD");
            return (profile==null?TextResources.Get("officialCompanionProgressWindow.report.nullProfileName"):profile.DisplayName)+" · "+stage;
        }
        void RequestRefresh()
        {
            if(Dispatcher.HasShutdownStarted||Interlocked.Exchange(ref refreshQueued,1)!=0)return;
            Dispatcher.BeginInvoke(new Action(delegate{Interlocked.Exchange(ref refreshQueued,0);if(!closed)Refresh();}));
        }
        void Refresh()
        {
            var state=preparation.State;var value=preparation.Progress;bool running=state==OfficialCompanionPreparationState.Running;
            LocalizedUi.Set(status,"Text",StatusText);LocalizedUi.Set(detail,"Text",CurrentDetail);LocalizedUi.Set(start,"Content",StartCaption);detail.Visibility=value==null?Visibility.Collapsed:Visibility.Visible;
            progress.IsIndeterminate=running&&(value==null||value.TotalBytes<=0&&value.Percent<=0);progress.Value=state==OfficialCompanionPreparationState.Completed?100:value==null?0:value.Percent;
            status.Foreground=state==OfficialCompanionPreparationState.Failed?ThemeManager.Brush("#AC3E45"):Ui.Ink;start.IsEnabled=!running;stop.IsEnabled=running;
            foreach(var role in roles)role.Value.Background=state==OfficialCompanionPreparationState.Completed?Ui.Mint:value!=null&&value.CharacterId==role.Key?Ui.Pale:ThemeManager.FieldSurface;
        }
    }
}
