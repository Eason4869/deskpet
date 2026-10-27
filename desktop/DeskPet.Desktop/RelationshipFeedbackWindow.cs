using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dafeiyu;
using System.Diagnostics;

namespace DeskPet.Desktop;

internal sealed class RelationshipFeedbackWindow : Window
{
    static readonly TimeSpan EnterDuration=TimeSpan.FromMilliseconds(RelationshipFeedbackPolicy.EnterMilliseconds),ExitDuration=TimeSpan.FromMilliseconds(RelationshipFeedbackPolicy.ExitMilliseconds),TotalDuration=TimeSpan.FromMilliseconds(RelationshipFeedbackPolicy.LifetimeMilliseconds);
    readonly System.Threading.Timer lifetime;int tickQueued;bool closed;
    readonly Stopwatch age=new();readonly TranslateTransform motion=new(0,12);
    readonly SpriteAssets assets=new();
    readonly StackPanel details=Ui.Column();readonly TextBlock gain=Ui.Text("",16),response=Ui.Text("",13);readonly ScrollViewer responseArea;int points;
    readonly Border card;
    readonly HashSet<string> stages=new();internal readonly HashSet<string> EventIds=new();
    internal string CompanionId{get;}
    void UpdateVisual(TimeSpan now)
    {
        if(now>=TotalDuration){Close();return;}
        double opacity,offset;
        if(now<EnterDuration){double progress=now.TotalMilliseconds/EnterDuration.TotalMilliseconds;opacity=progress;offset=12*(1-progress);}
        else if(now<TotalDuration-ExitDuration){opacity=1;offset=0;}
        else {double progress=(now-(TotalDuration-ExitDuration)).TotalMilliseconds/ExitDuration.TotalMilliseconds;opacity=1-progress;offset=-12*progress;}
        card.Opacity=opacity;motion.Y=offset;
    }
    void PlaceNearPet(DesktopController owner)
    {
        var pet=owner.Pet;var area=pet.Screens.ScreenFromWindow(pet)?.WorkingArea??new PixelRect(0,0,1920,1080);
        double scale=RenderScaling>0?RenderScaling:pet.RenderScaling;int width=(int)Math.Ceiling((Bounds.Width>0?Bounds.Width:Width)*scale);
        int height=(int)Math.Ceiling((Bounds.Height>0?Bounds.Height:160)*scale),petWidth=(int)Math.Ceiling(pet.Bounds.Width*pet.RenderScaling),petHeight=(int)Math.Ceiling(pet.Bounds.Height*pet.RenderScaling);
        int gap=(int)Math.Ceiling(12*scale),centerX=pet.Position.X+(petWidth-width)/2,centerY=pet.Position.Y+(petHeight-height)/2;
        int x=Math.Clamp(centerX,area.X,Math.Max(area.X,area.Right-width)),y=Math.Clamp(centerY,area.Y,Math.Max(area.Y,area.Bottom-height));
        PixelPoint? candidate=null;
        if(width<=area.Width&&height<=area.Height){
            int above=pet.Position.Y-height-gap,right=pet.Position.X+petWidth+gap,left=pet.Position.X-width-gap,below=pet.Position.Y+petHeight+gap;
            if(above>=area.Y)candidate=new PixelPoint(x,above);
            else if(right+width<=area.Right)candidate=new PixelPoint(right,y);
            else if(left>=area.X)candidate=new PixelPoint(left,y);
            else if(below+height<=area.Bottom)candidate=new PixelPoint(x,below);
        }
        if(candidate.HasValue)Position=candidate.Value;
        else if(IsVisible&&Bounds.Height>0)Close();
        else Position=new PixelPoint(x,y);
    }
    internal void Merge(RelationshipFeedbackNotice notice){foreach(var id in notice.EventIds)EventIds.Add(id);points+=notice.AwardedPoints;LocalizedUi.Set(gain,"Text",()=>(points>0?TextResources.Format("relationship.feedback.gain",points):""));gain.IsVisible=points>0;foreach(var m in notice.Milestones)if(stages.Add(m.StageId)){details.Children.Add(Ui.Text(RelationshipContent.StageName(m.StageId),18));details.Children.Add(Ui.Text(RelationshipCardData.FormatDate(m.EarnedLocalDate),12));}}
    internal void SetInteraction(string text,string eventId){EventIds.Add(eventId);response.Text=text;ToolTip.SetTip(response,text);Avalonia.Automation.AutomationProperties.SetName(response,text);responseArea.IsVisible=true;}
    internal RelationshipFeedbackWindow(DesktopController owner,RelationshipFeedbackNotice notice)
    {
        CompanionId=notice.CompanionId;var capturedSkin=owner.Skin;
        Width=350;SizeToContent=SizeToContent.Height;MaxHeight=454;ShowActivated=false;ShowInTaskbar=false;Topmost=owner.Settings.Topmost;
        WindowDecorations=WindowDecorations.None;CanResize=false;LocalizedUi.Set(this,"Title",()=>(TextResources.Get("relationship.card.title")));Background=Brushes.Transparent;
        TransparencyLevelHint=new[]{WindowTransparencyLevel.Transparent};
        var body=Ui.Column();var header=new DockPanel();
        var close=Ui.Button("×",Close);close.Name="DismissRelationshipFeedback";LocalizedUi.Tooltip(close,()=>(TextResources.Get("relationship.feedback.dismiss")));close.MinWidth=26;close.Padding=new Thickness(6,0,6,0);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        close.VerticalAlignment=VerticalAlignment.Center;close.Margin=new Thickness(8,0,0,0);
        var avatar=new AvatarView{Assets=assets,Skin=owner.Skin,Width=42,Height=42,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(avatar,Dock.Left);header.Children.Add(avatar);
        var name=Ui.Text(()=>(owner.Skin.DisplayName),14);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.VerticalAlignment=VerticalAlignment.Center;ToolTip.SetTip(name,owner.Skin.DisplayName);header.Children.Add(name);body.Children.Add(header);
        gain.Name="RelationshipGain";body.Children.Add(gain);body.Children.Add(details);response.Name="RelationshipInteraction";
        responseArea=new ScrollViewer{Content=response,MaxHeight=132,IsVisible=false,Margin=new Thickness(0,8,0,0),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};body.Children.Add(responseArea);Merge(notice);
        card=new Border{Child=new ScrollViewer{Content=body,MaxHeight=420,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled},Padding=new Thickness(18),CornerRadius=new CornerRadius(18),Background=DesktopTheme.Surface,BorderThickness=new Thickness(1),BorderBrush=DesktopTheme.Brush("#E5E7EB"),Opacity=0,RenderTransform=motion};
        Content=new Border{Child=card,Padding=new Thickness(0,12),Background=Brushes.Transparent};
        PlaceNearPet(owner);Opened+=(_,_)=>PlaceNearPet(owner);SizeChanged+=(_,_)=>PlaceNearPet(owner);
        EventHandler<PixelPointEventArgs> moved=(_,_)=>PlaceNearPet(owner);owner.Pet.PositionChanged+=moved;
        int suppressionVersion=owner.Relationships.FeedbackSuppressionVersion;
        Action savedSettingsChanged=()=>{if(!owner.Relationships.FeedbackEnabled||suppressionVersion!=owner.Relationships.FeedbackSuppressionVersion)Close();};
        owner.Relationships.FeedbackSettingsChanged+=savedSettingsChanged;
        Closed+=(_,_)=>{closed=true;lifetime.Dispose();owner.Pet.PositionChanged-=moved;owner.Relationships.FeedbackSettingsChanged-=savedSettingsChanged;assets.Dispose();};
        // A coalesced wall-clock pulse reaches the UI dispatcher after Show/layout,
        // so a slow first frame cannot consume the 200ms entrance before it renders.
        lifetime=new System.Threading.Timer(_=>{
            if(System.Threading.Interlocked.Exchange(ref tickQueued,1)!=0)return;
            Dispatcher.UIThread.Post(()=>{
                System.Threading.Interlocked.Exchange(ref tickQueued,0);
                if(closed||!IsVisible)return;
                if(!ReferenceEquals(capturedSkin,owner.Skin)||owner.IsExiting||owner.Settings.QuietMode||notice.CompanionId!=owner.RelationshipCompanionId||!owner.Pet.RelationshipFeedbackAllowed||suppressionVersion!=owner.Relationships.FeedbackSuppressionVersion){Close();return;}
                if(!age.IsRunning)age.Start();UpdateVisual(age.Elapsed);
            });
        },null,0,16);
    }
}
