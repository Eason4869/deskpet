using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Dafeiyu
{
    internal sealed class RelationshipFeedbackWindow : Window
    {
        readonly DispatcherTimer lifetime=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        readonly System.Diagnostics.Stopwatch age=new System.Diagnostics.Stopwatch();
        double expiresAt,exitStartedAt,restTop;bool exiting,loaded,closed;
        void ExtendLifetime()
        {
            expiresAt=age.Elapsed.TotalMilliseconds+(stages.Count>0||responseArea.Visibility==Visibility.Visible?7000:1200);
            if(!loaded||!exiting)return;
            exiting=false;AnimateTo(1,restTop,120);
        }
        void AnimateTo(double opacity,double top,double milliseconds)
        {
            var duration=new Duration(TimeSpan.FromMilliseconds(milliseconds));
            BeginAnimation(OpacityProperty,new DoubleAnimation(Opacity,opacity,duration));
            BeginAnimation(TopProperty,new DoubleAnimation(Top,top,duration){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}});
        }
        void FadeOut()
        {
            if(exiting||closed)return;exiting=true;exitStartedAt=age.Elapsed.TotalMilliseconds;
            AnimateTo(0,Top-10,250);
        }
        void PositionNear(PetWindow pet)
        {
            if(!loaded||closed)return;
            var handle=new WindowInteropHelper(pet).Handle;var area=DesktopInterop.WorkArea(handle);var bounds=DesktopInterop.Bounds(handle);
            var source=PresentationSource.FromVisual(pet);if(source!=null&&source.CompositionTarget!=null){var transform=source.CompositionTarget.TransformFromDevice;area.Transform(transform);bounds.Transform(transform);}
            double width=ActualWidth,height=ActualHeight,x=Math.Max(area.Left,Math.Min(bounds.Left,area.Right-width)),y;
            // Reserve the full upward animation path. If there is no room above,
            // place beside the companion rather than covering its input surface.
            if(bounds.Top-height-20>=area.Top+10)y=bounds.Top-height-20;
            else if(bounds.Right+12+width<=area.Right){x=bounds.Right+12;y=Math.Max(area.Top+10,Math.Min(bounds.Top,area.Bottom-height-12));}
            else if(bounds.Left-12-width>=area.Left){x=bounds.Left-12-width;y=Math.Max(area.Top+10,Math.Min(bounds.Top,area.Bottom-height-12));}
            else if(bounds.Bottom+20+height+12<=area.Bottom)y=bounds.Bottom+20;
            else {Close();return;}
            Left=x;
            if(Math.Abs(restTop-y)<.1)return;
            double offset=Double.IsNaN(Top)?12:Top-restTop;restTop=y;
            BeginAnimation(TopProperty,null);Top=restTop+offset;
            if(age.IsRunning)BeginAnimation(TopProperty,new DoubleAnimation(Top,restTop+(exiting?-10:0),new Duration(TimeSpan.FromMilliseconds(exiting?250:200))));
        }
        readonly StackPanel details=new StackPanel();readonly TextBlock gain=Ui.Text("",16,AgentUi.Ink),response=Ui.Text("",13,AgentUi.Ink);
        readonly ScrollViewer responseArea;int points;
        readonly System.Collections.Generic.HashSet<string> stages=new System.Collections.Generic.HashSet<string>();
        internal readonly System.Collections.Generic.HashSet<string> EventIds=new System.Collections.Generic.HashSet<string>();
        internal string CompanionId {get;private set;}
        internal void Merge(RelationshipFeedbackNotice notice)
        {
            foreach(var id in notice.EventIds)EventIds.Add(id);points+=notice.AwardedPoints;
            gain.Text=points>0?TextResources.Format("relationship.feedback.gain",points):"";gain.Visibility=points>0?Visibility.Visible:Visibility.Collapsed;
            foreach(var m in notice.Milestones)if(stages.Add(m.StageId)){details.Children.Add(Ui.Text(RelationshipContent.StageName(m.StageId),18,AgentUi.Ink));details.Children.Add(Ui.Text(RelationshipCardData.FormatDate(m.EarnedLocalDate),12,AgentUi.Ink));}
            ExtendLifetime();
        }
        internal void SetInteraction(string text,string eventId)
        {EventIds.Add(eventId);response.Text=text;response.ToolTip=text;System.Windows.Automation.AutomationProperties.SetName(response,text);responseArea.Visibility=Visibility.Visible;ExtendLifetime();}
        internal RelationshipFeedbackWindow(PetWindow pet,RelationshipFeedbackNotice notice)
        {
            CompanionId=notice.CompanionId;
            Width=350;SizeToContent=SizeToContent.Height;MaxHeight=430;ShowActivated=false;ShowInTaskbar=false;Topmost=pet.Settings.Topmost;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;Title=TextResources.Get("relationship.card.title");
            Background=Brushes.Transparent;Foreground=AgentUi.Ink;FontFamily=new FontFamily("Global User Interface");
            var body=new StackPanel();var header=new DockPanel();
            var close=Ui.Button("×");close.Name="DismissRelationshipFeedback";close.ToolTip=TextResources.Get("relationship.feedback.dismiss");close.MinWidth=26;close.Padding=new Thickness(6,0,6,0);close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            close.VerticalAlignment=VerticalAlignment.Center;close.Margin=new Thickness(8,0,0,0);
            var avatar=new Image{Source=SkinCatalog.GetAvatar(pet.Skin),Width=42,Height=42,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(avatar,Dock.Left);header.Children.Add(avatar);
            var name=Ui.InlineText(pet.Skin.DisplayName,14,AgentUi.Ink);name.ToolTip=pet.Skin.DisplayName;header.Children.Add(name);body.Children.Add(header);
            gain.Name="RelationshipGain";body.Children.Add(gain);body.Children.Add(details);response.Name="RelationshipInteraction";
            responseArea=new ScrollViewer{Content=response,MaxHeight=132,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,0)};body.Children.Add(responseArea);Merge(notice);
            Content=new Border{Child=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=420},Padding=new Thickness(18),CornerRadius=new CornerRadius(18),Background=ThemeManager.Surface,BorderThickness=new Thickness(1),BorderBrush=Ui.Line};
            SourceInitialized+=delegate{DesktopInterop.MakeToolWindow(new WindowInteropHelper(this).Handle);};
            Opacity=0;
            Loaded+=delegate{loaded=true;PositionNear(pet);if(closed)return;Top=restTop+12;age.Start();ExtendLifetime();AnimateTo(1,restTop,200);lifetime.Start();};
            SizeChanged+=delegate{PositionNear(pet);};EventHandler petMoved=delegate{PositionNear(pet);};pet.LocationChanged+=petMoved;
            int suppressionVersion=pet.Relationships.FeedbackSuppressionVersion;
            Action savedSettingsChanged=delegate{if(!pet.Relationships.FeedbackEnabled||suppressionVersion!=pet.Relationships.FeedbackSuppressionVersion)Close();};
            pet.Relationships.FeedbackSettingsChanged+=savedSettingsChanged;
            lifetime.Tick+=delegate{if(!pet.RelationshipFeedbackAllowed(notice.CompanionId)||suppressionVersion!=pet.Relationships.FeedbackSuppressionVersion){Close();return;}if(exiting&&age.Elapsed.TotalMilliseconds>=exitStartedAt+250)Close();else if(age.Elapsed.TotalMilliseconds>=expiresAt)FadeOut();};
            Closed+=delegate{closed=true;lifetime.Stop();age.Stop();pet.LocationChanged-=petMoved;pet.Relationships.FeedbackSettingsChanged-=savedSettingsChanged;BeginAnimation(OpacityProperty,null);BeginAnimation(TopProperty,null);};
        }
    }
}
