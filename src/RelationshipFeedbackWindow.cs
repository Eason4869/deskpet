using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu
{
    internal sealed class RelationshipFeedbackWindow : Window
    {
        readonly DispatcherTimer lifetime=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
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
        }
        internal void SetInteraction(string text,string eventId)
        {EventIds.Add(eventId);response.Text=text;response.ToolTip=text;System.Windows.Automation.AutomationProperties.SetName(response,text);responseArea.Visibility=Visibility.Visible;}
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
            Loaded+=delegate{var area=DesktopInterop.WorkArea(new WindowInteropHelper(pet).Handle);Left=Math.Max(area.Left,Math.Min(pet.Left,area.Right-Width));Top=Math.Max(area.Top,pet.Top-ActualHeight-8);};
            int suppressionVersion=pet.Relationships.FeedbackSuppressionVersion;
            Action savedSettingsChanged=delegate{if(!pet.Relationships.FeedbackEnabled||suppressionVersion!=pet.Relationships.FeedbackSuppressionVersion)Close();};
            pet.Relationships.FeedbackSettingsChanged+=savedSettingsChanged;
            var age=System.Diagnostics.Stopwatch.StartNew();lifetime.Tick+=delegate{if(age.Elapsed>=TimeSpan.FromSeconds(7)||!pet.RelationshipFeedbackAllowed(notice.CompanionId)||suppressionVersion!=pet.Relationships.FeedbackSuppressionVersion)Close();};Closed+=delegate{lifetime.Stop();pet.Relationships.FeedbackSettingsChanged-=savedSettingsChanged;};lifetime.Start();
        }
    }
}
