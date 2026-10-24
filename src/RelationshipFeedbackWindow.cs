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
        internal RelationshipFeedbackWindow(PetWindow pet,RelationshipFeedbackNotice notice)
        {
            Width=350;SizeToContent=SizeToContent.Height;MaxHeight=430;ShowActivated=false;ShowInTaskbar=false;Topmost=pet.Settings.Topmost;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;Title=TextResources.Get("relationship.card.title");
            Background=Brushes.Transparent;Foreground=AgentUi.Ink;FontFamily=new FontFamily("Global User Interface");
            var body=new StackPanel();var header=new DockPanel();
            var close=Ui.Button("×");close.Name="DismissRelationshipFeedback";close.ToolTip=TextResources.Get("relationship.feedback.dismiss");close.MinWidth=26;close.Padding=new Thickness(6,0,6,0);close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            close.VerticalAlignment=VerticalAlignment.Center;close.Margin=new Thickness(8,0,0,0);
            var avatar=new Image{Source=SkinCatalog.GetAvatar(pet.Skin),Width=42,Height=42,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(avatar,Dock.Left);header.Children.Add(avatar);
            var name=Ui.InlineText(pet.Skin.DisplayName,14,AgentUi.Ink);name.ToolTip=pet.Skin.DisplayName;header.Children.Add(name);body.Children.Add(header);
            if(notice.AwardedPoints>0)body.Children.Add(Ui.Text(TextResources.Format("relationship.feedback.gain",notice.AwardedPoints),16,AgentUi.Ink));
            foreach(var milestone in notice.Milestones){body.Children.Add(Ui.Text(RelationshipContent.StageName(milestone.StageId),18,AgentUi.Ink));body.Children.Add(Ui.Text(RelationshipCardData.FormatDate(milestone.EarnedLocalDate),12,AgentUi.Ink));}
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
