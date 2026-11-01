using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class DesktopObservationConsentWindow : Window
    {
        internal DesktopObservationConsentWindow(PetSettings snapshot,bool test)
        {
            Name="DesktopObservationConsent";Width=520;MinWidth=380;MaxHeight=560;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;ResizeMode=ResizeMode.CanMinimize;Background=ThemeManager.Surface;Foreground=Ui.Ink;FontFamily=new FontFamily("Microsoft YaHei UI");
            Func<string> title=()=>test?TextResources.Get("desktopObservation.uploadTest"):TextResources.Get("desktopObservation.consentTitle");LocalizedUi.Set(this,"Title",title);
            var root=new Grid{Margin=new Thickness(22)};root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();var heading=LocalizedUi.Text(title,18);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
            var description=LocalizedUi.Text(()=>TextResources.Format("desktopObservation.consentBody",DesktopObservationUiText.ReceivingSummary(snapshot)),13,Ui.Muted);description.Name="DesktopObservationConsentDestinations";description.Margin=new Thickness(0,14,0,8);body.Children.Add(description);root.Children.Add(Ui.Scroll(body));
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};var cancel=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.consentCancel"));cancel.Name="DesktopObservationConsentCancel";cancel.Click+=delegate{DialogResult=false;};var accept=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.consentAccept"),true);accept.Name="DesktopObservationConsentAccept";accept.Click+=delegate{DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(accept);Grid.SetRow(buttons,1);root.Children.Add(buttons);Content=root;Loaded+=delegate{cancel.Focus();};
        }
    }
}
