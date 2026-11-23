using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Dafeiyu
{
    internal sealed class CompanionInstallationBubble:Window
    {
        readonly PetWindow pet;
        readonly CompanionInstallationStatus status=new CompanionInstallationStatus();
        readonly TextBlock message;
        readonly Button cancel,retry;
        readonly Polygon tail;
        readonly DispatcherTimer timer;
        string previousText;
        bool shuttingDown;
        internal CompanionInstallationStatus Status {get{return status;}}
        internal CompanionInstallationBubble(PetWindow owner)
        {
            pet=owner;Name="CompanionInstallationBubble";Width=340;Height=130;MinHeight=110;MaxHeight=230;SizeToContent=SizeToContent.Height;AllowsTransparency=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            ShowInTaskbar=false;ShowActivated=false;Background=Brushes.Transparent;Topmost=pet.Settings.Topmost;
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(14)});
            var body=new StackPanel{Margin=new Thickness(16,10,16,10)};
            var close=Ui.Button("×");close.Name="DismissCompanionInstallation";close.HorizontalAlignment=HorizontalAlignment.Right;close.Padding=new Thickness(7,0,7,0);close.MinHeight=22;LocalizedUi.Set(close,"ToolTip",()=>TextResources.Get("common.close"));close.Click+=delegate{status.Dismiss();Hide();};body.Children.Add(close);
            message=Ui.Text("",13);message.Name="CompanionInstallationMessage";message.TextWrapping=TextWrapping.Wrap;body.Children.Add(message);
            var actions=new StackPanel{Orientation=Orientation.Horizontal};cancel=LocalizedUi.Button(()=>TextResources.Get("dialog.cancelDownload"));cancel.Name="CancelCompanionInstallation";cancel.Click+=delegate{pet.CancelCompanionSelection(status.Request);};actions.Children.Add(cancel);
            retry=LocalizedUi.Button(()=>TextResources.Get("common.retry"));retry.Name="RetryCompanionInstallation";retry.Click+=async delegate{var skin=pet.Catalog.FindPicker(status.PickerKey);if(skin!=null)await pet.RequestSkinAsync(skin,(CompanionSelectionOrigin)Enum.Parse(typeof(CompanionSelectionOrigin),status.Origin),null);};actions.Children.Add(retry);body.Children.Add(actions);
            root.Children.Add(new Border{Child=body,CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),BorderBrush=Ui.Line,Background=ThemeManager.Surface});tail=new Polygon{Fill=ThemeManager.Surface,Stroke=Ui.Line,StrokeThickness=1};Grid.SetRow(tail,1);root.Children.Add(tail);Content=root;
            SourceInitialized+=delegate{DesktopInterop.MakeToolWindow(new WindowInteropHelper(this).Handle);};
            timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};timer.Tick+=delegate{RenderStatus();};Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs args){if(!shuttingDown){args.Cancel=true;status.Dismiss();Hide();}};Closed+=delegate{timer.Stop();};
        }
        internal void Shutdown(){shuttingDown=true;Close();}
        internal void Accept(CompanionSelectionUpdate update)
        {
            bool wasActive=status.Active;string previousStage=status.Stage;status.Accept(update);
            // Lifecycle feedback is immediate; only continuous progress waits for the timer.
            if(status.Active!=wasActive||(status.Stage!=previousStage&&status.Stage!="progress"))RenderStatus();
            if(status.Active&&!timer.IsEnabled)timer.Start();
        }
        internal void RenderStatus()
        {
            var text=status.Text;if(text!=previousText){previousText=text;message.Text=text;pet.ShowCompanionInstallationStatus(status.Origin,text);}
            cancel.Visibility=status.CanCancel?Visibility.Visible:Visibility.Collapsed;retry.Visibility=status.CanRetry?Visibility.Visible:Visibility.Collapsed;
            if(status.Visible(pet.IsPetVisible)){if(!IsVisible)Show();BubblePlacement.Follow(this,pet,tail);}else if(IsVisible)Hide();
            if(!status.Active)timer.Stop();
        }
    }
    public sealed partial class PetWindow
    {
        CompanionInstallationBubble companionInstallationBubble;
        void PresentCompanionInstallation(CompanionSelectionUpdate update){if(companionInstallationBubble==null)companionInstallationBubble=new CompanionInstallationBubble(this);companionInstallationBubble.Accept(update);}
        internal void ShowCompanionInstallationStatus(string origin,string text){if(origin=="Settings"||origin=="Onboarding"){Notice=text;if(preferences!=null)preferences.ShowNotice(text);}}
    }
}
