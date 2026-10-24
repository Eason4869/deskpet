using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class RelationshipFeedbackWindow : Window
{
    readonly DispatcherTimer lifetime=new(){Interval=TimeSpan.FromMilliseconds(200)};
    readonly SpriteAssets assets=new();
    internal RelationshipFeedbackWindow(DesktopController owner,RelationshipFeedbackNotice notice)
    {
        Width=350;SizeToContent=SizeToContent.Height;MaxHeight=430;ShowActivated=false;ShowInTaskbar=false;Topmost=owner.Settings.Topmost;
        WindowDecorations=WindowDecorations.None;CanResize=false;Title=TextResources.Get("relationship.card.title");Background=DesktopTheme.Surface;
        var body=Ui.Column();body.Margin=new Thickness(18);var header=new DockPanel();
        var close=Ui.Button("×",Close);close.Name="DismissRelationshipFeedback";ToolTip.SetTip(close,TextResources.Get("relationship.feedback.dismiss"));close.MinWidth=26;close.Padding=new Thickness(6,0,6,0);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        header.Children.Add(new AvatarView{Assets=assets,Skin=owner.Skin,Width=42,Height=42,HorizontalAlignment=HorizontalAlignment.Left});body.Children.Add(header);body.Children.Add(Ui.Text(owner.Skin.DisplayName,16));
        if(notice.AwardedPoints>0)body.Children.Add(Ui.Text(TextResources.Format("relationship.feedback.gain",notice.AwardedPoints),18));
        foreach(var m in notice.Milestones){body.Children.Add(Ui.Text(RelationshipContent.StageName(m.StageId),18));body.Children.Add(Ui.Text(RelationshipCardData.FormatDate(m.EarnedLocalDate),12));}
        Content=new Border{Child=new ScrollViewer{Content=body,MaxHeight=420},BorderThickness=new Thickness(1),BorderBrush=DesktopTheme.Brush("#E5E7EB")};
        var area=owner.Pet.Screens.ScreenFromWindow(owner.Pet)?.WorkingArea??new PixelRect(0,0,1920,1080);Position=new PixelPoint(Math.Clamp(owner.Pet.Position.X,area.X,Math.Max(area.X,area.Right-400)),Math.Max(area.Y,owner.Pet.Position.Y-280));
        var age=System.Diagnostics.Stopwatch.StartNew();lifetime.Tick+=(_,_)=>{if(age.Elapsed>=TimeSpan.FromSeconds(7)||owner.IsExiting||owner.Settings.QuietMode||notice.CompanionId!=owner.RelationshipCompanionId||!owner.Pet.RelationshipFeedbackAllowed)Close();};Closed+=(_,_)=>{lifetime.Stop();assets.Dispose();};lifetime.Start();
    }
}
