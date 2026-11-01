using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class DesktopObservationSettingsPanel : StackPanel,IDisposable
{
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly CheckBox enabled;
    private readonly TextBlock route,status,destinations;
    private readonly ListBox exclusions=new(){Name="ObservationExclusions",MinHeight=58,MaxHeight=130};
    private readonly Button pause,permission;
    private readonly CancellationTokenSource lifetime=new();
    private Window? preview;
    private bool refreshing,busy,disposed;
    private DateTime lastUpload=DateTime.MinValue;
    private string? transientStatus;
    private string? permissionStatus;
    internal DesktopObservationSettingsPanel(DesktopController owner,Window host)
    {
        this.owner=owner;this.host=host;Name="DesktopObservationSettings";Spacing=10;Margin=new Thickness(0,18,0,0);
        Children.Add(SettingsVisual.Header("model",()=>T("title")));
        enabled=new CheckBox{Name="DesktopObservationEnabled",Content=SettingsVisual.Text(()=>T("enabled")),IsChecked=owner.Settings.DesktopObservationEnabled};
        enabled.IsCheckedChanged+=async(_,_)=>
        {
            if(refreshing||busy)return;bool desired=enabled.IsChecked==true;
            refreshing=true;enabled.IsChecked=owner.Settings.DesktopObservationEnabled;refreshing=false;
            busy=true;try
            {
                string? digest=null;
                if(desired){digest=await ConfirmAsync();if(digest==null)return;}
                bool saved=await owner.SaveObservationSettingsAsync(s=>
                {
                    if(desired){var global=s.Copy();global.ImageDescriptionOverride=null;if(DesktopObservationConsent.Destination(global)!=digest)throw new InvalidOperationException();s.DesktopObservationConsentVersion=DesktopObservationConsent.Version;s.DesktopObservationConsentDestination=digest!;}
                    s.DesktopObservationEnabled=desired;
                });
                transientStatus=saved?null:T("saveFailed");
            }
            finally{busy=false;Refresh();}
        };
        Children.Add(enabled);Children.Add(SettingsVisual.Text(()=>T("help"),13,true));
        route=SettingsVisual.Text("",13);route.Name="ObservationRoute";
        destinations=SettingsVisual.Text("",13,true);destinations.Name="ObservationDestinations";
        status=SettingsVisual.Text("",13,true);status.Name="ObservationStatus";status.MinHeight=42;
        Children.Add(Ui.Column(route,destinations,status));
        var confirm=Action("confirm","ObservationConfirm",async()=>
        {
            string? digest=await ConfirmAsync();if(digest==null)return;
            bool saved=await owner.SaveObservationSettingsAsync(s=>{var global=s.Copy();global.ImageDescriptionOverride=null;if(DesktopObservationConsent.Destination(global)!=digest)throw new InvalidOperationException();s.DesktopObservationConsentVersion=DesktopObservationConsent.Version;s.DesktopObservationConsentDestination=digest;});
            transientStatus=saved?null:T("saveFailed");Refresh();
        });
        permission=Action("permission","ObservationPermission",async()=>{bool granted=await owner.RequestScreenCaptureAccessAsync();permissionStatus=T(granted?"permissionReopen":"permissionRequired");Refresh();});
        var configure=SettingsVisual.Button(()=>T("configure"),()=>owner.ShowSettingsSection("text"));configure.Name="ObservationConfigure";
        Children.Add(SettingsVisual.Wrap(confirm,permission,configure));
        var local=Action("preview","ObservationPreview",()=>TestAsync(false));
        var upload=Action("uploadTest","ObservationUploadTest",()=>TestAsync(true));
        pause=SettingsVisual.Button(()=>owner.ObservationPaused?T("resume"):T("pause"),()=>{owner.PauseObservation(!owner.ObservationPaused);Refresh();});pause.Name="ObservationPause";
        Children.Add(SettingsVisual.Wrap(local,upload,pause));
        var exclusionBody=Ui.Column(SettingsVisual.Text(()=>T("bundleIdHint"),13,true),exclusions,SettingsVisual.Wrap(Action("addApp","ObservationAddExclusion",AddExclusionAsync),Action("removeApp","ObservationRemoveExclusion",RemoveExclusionAsync)));
        var disclosure=new Expander{Header=SettingsVisual.Text(()=>T("excludedApps"),13),Content=exclusionBody,Name="ObservationExcludedApps"};SettingsVisual.Disclosure(disclosure);Children.Add(disclosure);
        owner.Changed+=Refresh;owner.ObservationChanged+=Refresh;TextResources.LanguageChanged+=LanguageChanged;
        Refresh();
    }
    private static string T(string key)=>TextResources.Get("desktopObservation."+key);
    private Button Action(string key,string name,Func<Task> action)
    {
        var button=SettingsVisual.Button(()=>T(key),()=>{});button.Name=name;
        button.Click+=async(_,_)=>{if(busy)return;busy=true;button.IsEnabled=false;try{await action();}catch(OperationCanceledException){}catch(Exception){transientStatus=T("saveFailed");}finally{busy=false;if(!disposed){button.IsEnabled=true;Refresh();}}};return button;
    }
    private void LanguageChanged(object? sender,EventArgs args)=>Refresh();
    private void Refresh()
    {
        if(disposed)return;
        refreshing=true;enabled.IsChecked=owner.Settings.DesktopObservationEnabled;refreshing=false;
        var settings=owner.GlobalObservationSettings();var path=ImageModelResolver.Resolve(settings,null);
        LocalizedUi.Set(route,"Text",()=>T(path.Route==ImageRouteKind.Direct?"routeDirect":path.Route==ImageRouteKind.Described?"routeDescribed":"routeUnavailable")+"\n"+settings.Text.Model);
        var addresses=DesktopObservationUiText.Destinations(settings);
        LocalizedUi.Set(destinations,"Text",()=>addresses.Length==0?T("noDestination"):TextResources.Format("desktopObservation.destinations",string.Join("\n",addresses)));
        ToolTip.SetTip(destinations,string.Join("\n",addresses));
        var reason=owner.Observation.Policy.TryBegin(new DesktopObservationContext(settings,owner.Skin.Id,owner.Skin.Name,null,null,DateTime.Now,true,true,()=>true),DateTime.UtcNow).Reason;
        string value=transientStatus??permissionStatus??(owner.ObservationPaused?T("paused"):TextResources.Get("desktopObservation.reason."+reason));
        if(reason==ObservationReason.Allowed&&!owner.ObservationPaused)
        {
            value=owner.Observation.Capture.GetStatus() switch{DesktopCaptureStatus.UnsupportedPlatform=>T("unsupportedPlatform"),DesktopCaptureStatus.PermissionRequired=>T("permissionRequired"),DesktopCaptureStatus.Ready=>TextResources.Get("desktopObservation.reason."+owner.ObservationReason),_=>TextResources.Get("desktopObservation.reason.CaptureUnavailable")};
        }
        status.Text=value;permission.IsEnabled=OperatingSystem.IsMacOS();
        exclusions.ItemsSource=owner.Settings.DesktopObservationExcludedApps.ToArray();
    }
    private async Task<string?> ConfirmAsync()
    {
        var settings=owner.GlobalObservationSettings();var addresses=DesktopObservationUiText.Destinations(settings);string digest=DesktopObservationConsent.Destination(settings);
        if(addresses.Length==0||digest.Length==0){transientStatus=T("noDestination");return null;}
        var dialog=LocalizedUi.Window(()=>T("consentTitle"),new Border(),500,350);dialog.Background=DesktopTheme.Surface;
        dialog.Content=new Border{Padding=new Thickness(22),Child=Ui.Column(SettingsVisual.Text(()=>TextResources.Format("desktopObservation.consentBody",string.Join("\n",addresses)),14),SettingsVisual.Wrap(SettingsVisual.Button(()=>T("consentCancel"),()=>dialog.Close(false)),SettingsVisual.Button(()=>T("consentAccept"),()=>dialog.Close(true))))};
        return await dialog.ShowDialog<bool>(host)?digest:null;
    }
    private async Task TestAsync(bool upload)
    {
        var settings=owner.GlobalObservationSettings();
        if(!settings.DesktopObservationEnabled){transientStatus=TextResources.Get("desktopObservation.reason.Disabled");return;}
        if(upload)
        {
            if(DateTime.UtcNow-lastUpload<TimeSpan.FromSeconds(10)){transientStatus=TextResources.Get("desktopObservation.reason.Busy");return;}
            // Every upload test visibly confirms its exact current destinations.
            string? digest=await ConfirmAsync();if(digest==null)return;
            if(!DesktopObservationConsent.IsConfirmed(settings)||DesktopObservationConsent.Destination(owner.GlobalObservationSettings())!=digest){transientStatus=TextResources.Get("desktopObservation.reason.ConsentRequired");return;}
            lastUpload=DateTime.UtcNow;
        }
        long epoch=owner.Observation.Epoch;
        for(int seconds=3;seconds>0;seconds--){transientStatus=TextResources.Format("desktopObservation.countdown",seconds);Refresh();await Task.Delay(1000,lifetime.Token);}
        transientStatus=null;
        if(disposed||!owner.Observation.IsCurrent(epoch)||!owner.Pet.ObservationEligible(true))return;
        if(upload)
        {
            using var decision=await owner.Observation.GenerateAsync(settings,owner.Skin.Id,owner.Skin.Name,null,null,DateTime.Now,true,true,DesktopObservationMode.ManualTest);
            if(!owner.Observation.IsCurrent(epoch)||!owner.Pet.ObservationEligible(true)||!owner.Observation.IsForegroundCurrent())return;
            transientStatus=decision.Message.Length==0?TextResources.Get("desktopObservation.reason."+decision.Reason):decision.Message;
            return;
        }
        using var frame=await owner.Observation.PreviewAsync(settings,epoch,lifetime.Token);
        if(frame==null){transientStatus=T("previewEmpty");return;}
        if(!owner.Observation.IsCurrent(epoch)||!owner.Pet.ObservationEligible(true)){return;}
        var bitmap=new Bitmap(new MemoryStream(frame.EncodedPng));
        preview?.Close();preview=LocalizedUi.Window(()=>T("previewLocal"),Ui.Column(SettingsVisual.Text(()=>T("previewHelp"),13,true),new Image{Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=480}),700,580);
        preview.Background=DesktopTheme.Surface;preview.Closed+=(_,_)=>{bitmap.Dispose();preview=null;};preview.Show();
    }
    private async Task AddExclusionAsync()
    {
        var input=LocalizedUi.Input("",()=>T("bundleIdHint"));input.MinWidth=0;var notice=SettingsVisual.Text("",13,true);
        var dialog=LocalizedUi.Window(()=>T("addApp"),new Border(),450,320);dialog.Background=DesktopTheme.Surface;
        var save=SettingsVisual.Button(()=>TextResources.Get("common.save"),()=>
        {
            string value=(input.Text??"").Trim();
            if(value.Length>200||!System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\z")){notice.Text=T("bundleIdInvalid");return;}
            dialog.Close(value);
        });
        dialog.Content=Ui.Column(SettingsVisual.Text(()=>T("bundleIdLabel")),input,notice,SettingsVisual.Wrap(SettingsVisual.Button(()=>TextResources.Get("common.cancel"),()=>dialog.Close(null)),save));
        string? result=await dialog.ShowDialog<string?>(host);if(result==null)return;
        bool saved=await owner.SaveObservationSettingsAsync(s=>{if(!s.DesktopObservationExcludedApps.Contains(result,StringComparer.OrdinalIgnoreCase))s.DesktopObservationExcludedApps.Add(result);});transientStatus=saved?null:T("saveFailed");
    }
    private async Task RemoveExclusionAsync()
    {
        if(exclusions.SelectedItem is not string item)return;
        bool saved=await owner.SaveObservationSettingsAsync(s=>s.DesktopObservationExcludedApps.RemoveAll(value=>string.Equals(value,item,StringComparison.OrdinalIgnoreCase)));transientStatus=saved?null:T("saveFailed");
    }
    public void Dispose(){if(disposed)return;disposed=true;owner.Changed-=Refresh;owner.ObservationChanged-=Refresh;TextResources.LanguageChanged-=LanguageChanged;lifetime.Cancel();lifetime.Dispose();preview?.Close();}
}
