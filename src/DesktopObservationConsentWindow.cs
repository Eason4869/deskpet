using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class DesktopObservationConsentWindow : Window
    {
        internal DesktopObservationConsentWindow(PetSettings snapshot,bool test)
        {
            Func<string> title=()=>test?TextResources.Get("desktopObservation.test.action"):TextResources.Get("desktopObservation.consentTitle");LocalizedUi.Set(this,"Title",title);
            Ui.Window(this,title(),520,260);Name="DesktopObservationConsent";MinWidth=380;MinHeight=0;MaxHeight=Math.Min(560,Math.Max(1,SystemParameters.WorkArea.Height-32));SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();var heading=LocalizedUi.Text(title,14);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,4);body.Children.Add(heading);
            var scope=LocalizedUi.Text(()=>TextResources.Get("desktopObservation.captureMode")+": "+(snapshot.DesktopObservationCaptureScope==DesktopObservationCaptureScope.FullScreen?TextResources.Get("desktopObservation.fullScreen"):TextResources.Get("desktopObservation.application")),13);scope.Name="DesktopObservationConsentScope";scope.Margin=new Thickness(0,12,0,0);body.Children.Add(scope);
            var description=LocalizedUi.Text(()=>TextResources.Format("desktopObservation.consent.description",DesktopObservationUiText.ReceivingSummary(snapshot)),13,Ui.Muted);description.Name="DesktopObservationConsentDestinations";description.Margin=new Thickness(0,14,0,8);body.Children.Add(description);root.Children.Add(Ui.Scroll(body));
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};var cancel=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.consentCancel"));cancel.Name="DesktopObservationConsentCancel";cancel.IsCancel=true;cancel.Click+=delegate{DialogResult=false;};var accept=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.consentAccept"),true);accept.Name="DesktopObservationConsentAccept";accept.Click+=delegate{DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(accept);Grid.SetRow(buttons,1);root.Children.Add(buttons);root.Margin=new Thickness(24);heading.FontSize=18;heading.Margin=new Thickness(0,0,32,4);DialogUi.Apply(this,root);Loaded+=delegate{cancel.Focus();PetWindow.PlayApplicationCue(NotificationCue.Permission,"screen-consent:"+Guid.NewGuid().ToString("N"));};
        }
    }
}
