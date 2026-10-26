using Avalonia.Controls;
using Avalonia.Layout;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class GitHubRoutingView:StackPanel
{
    internal event Action? Changed;
    internal bool Saved {get;private set;}
    internal GitHubRoutingView(DesktopController owner)
    {
        Name="GitHubRouting";Spacing=12;
        var mode=LocalizedUi.Options(new ComboBox{Name="GitHubRoute",HorizontalAlignment=HorizontalAlignment.Stretch,ItemsSource=new[]{TextResources.Get("aboutPanel.aboutPanel.choice"),TextResources.Get("aboutPanel.aboutPanel.choice.alternativeA"),TextResources.Get("aboutPanel.aboutPanel.choice.alternativeC")},SelectedIndex=owner.Settings.UpdateUseProxy?2:owner.Settings.UpdateUseMirror?1:0},()=>(new[]{TextResources.Get("aboutPanel.aboutPanel.choice"),TextResources.Get("aboutPanel.aboutPanel.choice.alternativeA"),TextResources.Get("aboutPanel.aboutPanel.choice.alternativeC")}));
        var mirror=Ui.Input(owner.Settings.UpdateMirrorAddress);mirror.Name="GitHubMirrorAddress";var proxy=Ui.Input(ProxyConfiguration.DisplayAddress(owner.Settings.UpdateProxyAddress));proxy.Name="GitHubProxyAddress";var notice=Ui.Text("");
        void Save(){mirror.IsVisible=mode.SelectedIndex==1;proxy.IsVisible=mode.SelectedIndex==2;try{
            string mirrorValue=owner.Settings.UpdateMirrorAddress,proxyValue=owner.Settings.UpdateProxyAddress;
            if(mode.SelectedIndex==1)mirrorValue=GitHubUpdates.MirrorPrefix(mirror.Text??"");
            if(mode.SelectedIndex==2){proxyValue=ProxyConfiguration.DisplayAddress(proxy.Text??"");ProxyConfiguration.Endpoint(proxyValue);}
            owner.Settings.UpdateUseMirror=mode.SelectedIndex==1;owner.Settings.UpdateUseProxy=mode.SelectedIndex==2;owner.Settings.UpdateMirrorAddress=mirrorValue;owner.Settings.UpdateProxyAddress=proxyValue;owner.Save();Saved=true;LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("aboutPanel.saveNetwork.networkNotice")));
        }catch(Exception error)when(error is ArgumentException or InvalidOperationException){Saved=false;notice.Text=error.Message;}Changed?.Invoke();}
        Children.Add(LocalizedUi.Text(()=>(TextResources.Get("onboarding.prepare.networkDetail"))));Children.Add(mode);Children.Add(mirror);Children.Add(proxy);Children.Add(notice);
        mode.SelectionChanged+=(_,_)=>Save();mirror.TextChanged+=(_,_)=>{if(mode.SelectedIndex==1)Save();};proxy.TextChanged+=(_,_)=>{if(mode.SelectedIndex==2)Save();};
        mirror.IsVisible=mode.SelectedIndex==1;proxy.IsVisible=mode.SelectedIndex==2;Saved=true;
    }
}
