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
    readonly StackPanel details=Ui.Column();readonly TextBlock gain=Ui.Text("",16),response=Ui.Text("",13);readonly ScrollViewer responseArea;int points;
    readonly HashSet<string> stages=new();internal readonly HashSet<string> EventIds=new();
    internal string CompanionId{get;}
    internal void Merge(RelationshipFeedbackNotice notice){foreach(var id in notice.EventIds)EventIds.Add(id);points+=notice.AwardedPoints;gain.Text=points>0?TextResources.Format("relationship.feedback.gain",points):"";gain.IsVisible=points>0;foreach(var m in notice.Milestones)if(stages.Add(m.StageId)){details.Children.Add(Ui.Text(RelationshipContent.StageName(m.StageId),18));details.Children.Add(Ui.Text(RelationshipCardData.FormatDate(m.EarnedLocalDate),12));}}
    internal void SetInteraction(string text,string eventId){EventIds.Add(eventId);response.Text=text;ToolTip.SetTip(response,text);Avalonia.Automation.AutomationProperties.SetName(response,text);responseArea.IsVisible=true;}
    internal RelationshipFeedbackWindow(DesktopController owner,RelationshipFeedbackNotice notice)
    {
        CompanionId=notice.CompanionId;var capturedSkin=owner.Skin;
        Width=350;SizeToContent=SizeToContent.Height;MaxHeight=430;ShowActivated=false;ShowInTaskbar=false;Topmost=owner.Settings.Topmost;
        WindowDecorations=WindowDecorations.None;CanResize=false;Title=TextResources.Get("relationship.card.title");Background=Brushes.Transparent;
        TransparencyLevelHint=new[]{WindowTransparencyLevel.Transparent};
        var body=Ui.Column();var header=new DockPanel();
        var close=Ui.Button("×",Close);close.Name="DismissRelationshipFeedback";ToolTip.SetTip(close,TextResources.Get("relationship.feedback.dismiss"));close.MinWidth=26;close.Padding=new Thickness(6,0,6,0);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        close.VerticalAlignment=VerticalAlignment.Center;close.Margin=new Thickness(8,0,0,0);
        var avatar=new AvatarView{Assets=assets,Skin=owner.Skin,Width=42,Height=42,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(avatar,Dock.Left);header.Children.Add(avatar);
        var name=Ui.Text(owner.Skin.DisplayName,14);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.VerticalAlignment=VerticalAlignment.Center;ToolTip.SetTip(name,owner.Skin.DisplayName);header.Children.Add(name);body.Children.Add(header);
        gain.Name="RelationshipGain";body.Children.Add(gain);body.Children.Add(details);response.Name="RelationshipInteraction";
        responseArea=new ScrollViewer{Content=response,MaxHeight=132,IsVisible=false,Margin=new Thickness(0,8,0,0),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};body.Children.Add(responseArea);Merge(notice);
        Content=new Border{Child=new ScrollViewer{Content=body,MaxHeight=420,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled},Padding=new Thickness(18),CornerRadius=new CornerRadius(18),Background=DesktopTheme.Surface,BorderThickness=new Thickness(1),BorderBrush=DesktopTheme.Brush("#E5E7EB")};
        var area=owner.Pet.Screens.ScreenFromWindow(owner.Pet)?.WorkingArea??new PixelRect(0,0,1920,1080);Position=new PixelPoint(Math.Clamp(owner.Pet.Position.X,area.X,Math.Max(area.X,area.Right-400)),Math.Max(area.Y,owner.Pet.Position.Y-280));
        int suppressionVersion=owner.Relationships.FeedbackSuppressionVersion;
        Action savedSettingsChanged=()=>{if(!owner.Relationships.FeedbackEnabled||suppressionVersion!=owner.Relationships.FeedbackSuppressionVersion)Close();};
        owner.Relationships.FeedbackSettingsChanged+=savedSettingsChanged;
        var age=System.Diagnostics.Stopwatch.StartNew();lifetime.Tick+=(_,_)=>{if(age.Elapsed>=TimeSpan.FromSeconds(7)||!ReferenceEquals(capturedSkin,owner.Skin)||owner.IsExiting||owner.Settings.QuietMode||notice.CompanionId!=owner.RelationshipCompanionId||!owner.Pet.RelationshipFeedbackAllowed||suppressionVersion!=owner.Relationships.FeedbackSuppressionVersion)Close();};Closed+=(_,_)=>{lifetime.Stop();owner.Relationships.FeedbackSettingsChanged-=savedSettingsChanged;assets.Dispose();};lifetime.Start();
    }
}
