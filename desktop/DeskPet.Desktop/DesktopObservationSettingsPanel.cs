using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class DesktopObservationSettingsPanel : StackPanel,IDisposable
{
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly CheckBox enabled;
    private readonly TextBlock route,model,status,destinations;
    private readonly ListBox exclusions=new(){Name="ObservationExclusions",MinHeight=58,MaxHeight=130};
    private readonly Button pause,permission,confirm,upload;
    private readonly CancellationTokenSource lifetime=new();
    private Window? preview;
    private bool refreshing,busy,disposed;
    private bool? pendingEnabled;
    private string? transientStatus;
    private bool? permissionGranted;
    internal DesktopObservationSettingsPanel(DesktopController owner,Window host)
    {
        this.owner=owner;this.host=host;Name="DesktopObservationSettings";Spacing=10;Margin=new Thickness(0,18,0,0);
        Children.Add(SettingsVisual.Header("model",()=>TextResources.Get("desktopObservation.settings.title")));
        enabled=new CheckBox{Name="DesktopObservationEnabled",Content=SettingsVisual.Text(()=>TextResources.Get("desktopObservation.settings.enabled")),IsChecked=owner.Settings.DesktopObservationEnabled};
        enabled.IsCheckedChanged+=async(_,_)=>
        {
            if(refreshing||busy)return;bool desired=enabled.IsChecked==true;
            pendingEnabled=desired?null:false;
            if(!desired)owner.PauseObservation(true);
            refreshing=true;enabled.IsChecked=desired?owner.Settings.DesktopObservationEnabled:false;refreshing=false;
            busy=true;try
            {
                string? digest=null;
                if(desired){digest=await ConfirmAsync();if(digest==null)return;}
                bool saved=await owner.SaveObservationSettingsAsync(s=>
                {
                    if(desired){var global=s.Copy();global.ImageDescriptionOverride=null;if(DesktopObservationConsent.Destination(global)!=digest)throw new InvalidOperationException();s.DesktopObservationConsentVersion=DesktopObservationConsent.Version;s.DesktopObservationConsentDestination=digest!;}
                    s.DesktopObservationEnabled=desired;
                });
                transientStatus=saved?null:TextResources.Get("desktopObservation.saveFailed");
                if(saved){pendingEnabled=null;if(desired)owner.PauseObservation(false);}
            }
            catch(Exception error) when(error is ApiException or IOException or InvalidOperationException or ArgumentException)
            {transientStatus=DesktopObservationUiText.ReasonText(ObservationReason.InvalidConfiguration);}
            finally{busy=false;Refresh();}
        };
        Children.Add(enabled);Children.Add(SettingsVisual.Text(()=>TextResources.Get("desktopObservation.settings.help"),13,true));
        route=SettingsVisual.Text("",13);route.Name="ObservationRoute";
        model=SettingsVisual.Text("",13,true);model.Name="ObservationModel";model.TextWrapping=TextWrapping.NoWrap;model.TextTrimming=TextTrimming.CharacterEllipsis;
        destinations=SettingsVisual.Text("",13,true);destinations.Name="ObservationDestinations";destinations.TextWrapping=TextWrapping.NoWrap;destinations.TextTrimming=TextTrimming.CharacterEllipsis;
        status=SettingsVisual.Text("",13,true);status.Name="ObservationStatus";status.MinHeight=42;
        Children.Add(Ui.Column(route,model,destinations,status));
        confirm=Action(()=>TextResources.Get("desktopObservation.confirm"),"ObservationConfirm",async()=>
        {
            if(pendingEnabled==false&&owner.Settings.DesktopObservationEnabled)
            {
                bool disabled=await owner.SaveObservationSettingsAsync(s=>s.DesktopObservationEnabled=false);if(disabled)pendingEnabled=null;
                transientStatus=disabled?null:TextResources.Get("desktopObservation.saveFailed");Refresh();return;
            }
            string? digest=await ConfirmAsync();if(digest==null)return;
            bool saved=await owner.SaveObservationSettingsAsync(s=>{var global=s.Copy();global.ImageDescriptionOverride=null;if(DesktopObservationConsent.Destination(global)!=digest)throw new InvalidOperationException();s.DesktopObservationConsentVersion=DesktopObservationConsent.Version;s.DesktopObservationConsentDestination=digest;});
            transientStatus=saved?null:TextResources.Get("desktopObservation.saveFailed");Refresh();
        });
        permission=Action(()=>TextResources.Get("desktopObservation.permission"),"ObservationPermission",async()=>{permissionGranted=await owner.RequestScreenCaptureAccessAsync();Refresh();});
        var configure=SettingsVisual.Button(()=>TextResources.Get("desktopObservation.configure"),()=>owner.ShowSettingsSection("text"));configure.Name="ObservationConfigure";
        configure.BorderThickness=new Thickness(0);configure.Background=Brushes.Transparent;configure.Foreground=SettingsVisual.Muted;if(configure.Content is TextBlock configureText)configureText.Foreground=SettingsVisual.Muted;
        Children.Add(SettingsVisual.Wrap(confirm,permission,configure));
        var local=Action(()=>TextResources.Get("desktopObservation.preview.action"),"ObservationPreview",()=>TestAsync(false));
        upload=Action(()=>TextResources.Get("desktopObservation.test.action"),"ObservationUploadTest",()=>TestAsync(true));
        pause=SettingsVisual.Button(()=>owner.ObservationPaused?TextResources.Get("desktopObservation.resume"):TextResources.Get("desktopObservation.pause"),()=>{owner.PauseObservation(!owner.ObservationPaused);Refresh();});pause.Name="ObservationPause";
        Children.Add(SettingsVisual.Wrap(local,upload,pause));
        var exclusionBody=Ui.Column(SettingsVisual.Text(()=>TextResources.Get("desktopObservation.bundleIdHint"),13,true),exclusions,SettingsVisual.Wrap(Action(()=>TextResources.Get("desktopObservation.addApp"),"ObservationAddExclusion",AddExclusionAsync),Action(()=>TextResources.Get("desktopObservation.removeApp"),"ObservationRemoveExclusion",RemoveExclusionAsync)));
        var disclosure=new Expander{Header=SettingsVisual.Text(()=>TextResources.Get("desktopObservation.exclusions.title"),13),Content=exclusionBody,Name="ObservationExcludedApps"};SettingsVisual.Disclosure(disclosure);Children.Add(disclosure);
        owner.Changed+=Refresh;owner.ObservationChanged+=Refresh;TextResources.LanguageChanged+=LanguageChanged;
        owner.Observation.ImagesCleared+=ClearPreview;
        Refresh();
    }
    private Button Action(Func<string> caption,string name,Func<Task> action)
    {
        var button=SettingsVisual.Button(caption,()=>{});button.Name=name;
        button.Click+=async(_,_)=>{if(busy)return;busy=true;button.IsEnabled=false;try{await action();}catch(OperationCanceledException){}catch(Exception){transientStatus=TextResources.Get("desktopObservation.saveFailed");}finally{busy=false;if(!disposed){button.IsEnabled=true;Refresh();}}};return button;
    }
    private void LanguageChanged(object? sender,EventArgs args){transientStatus=null;Refresh();}
    private void Refresh()
    {
        if(disposed)return;
        refreshing=true;enabled.IsChecked=pendingEnabled??owner.Settings.DesktopObservationEnabled;refreshing=false;
        var settings=owner.GlobalObservationSettings();var path=ImageModelResolver.Resolve(settings,null);
        LocalizedUi.Set(route,"Text",()=>(path.Route==ImageRouteKind.Direct?TextResources.Get("desktopObservation.route.direct"):path.Route==ImageRouteKind.Described?TextResources.Get("desktopObservation.route.described"):TextResources.Get("desktopObservation.routeUnavailable")));
        model.Text=settings.Text.Model;ToolTip.SetTip(model,settings.Text.Model);
        var addresses=DesktopObservationUiText.Destinations(settings);
        LocalizedUi.Set(destinations,"Text",()=>addresses.Length==0?TextResources.Get("desktopObservation.settings.noDestination"):DesktopObservationUiText.ReceivingSummary(settings));
        ToolTip.SetTip(destinations,DesktopObservationUiText.ReceivingSummary(settings));
        var reason=owner.Observation.Policy.TryBegin(new DesktopObservationContext(settings,owner.Skin.Id,owner.Skin.Name,null,null,DateTime.Now,true,true,()=>true),DateTime.UtcNow).Reason;
        string value=transientStatus??(permissionGranted.HasValue?(permissionGranted.Value?TextResources.Get("desktopObservation.permissionReopen"):TextResources.Get("desktopObservation.permissionRequired")):(owner.ObservationPaused?TextResources.Get("desktopObservation.paused"):DesktopObservationUiText.ReasonText(reason)));
        if(transientStatus==null&&!permissionGranted.HasValue&&reason==ObservationReason.Allowed&&!owner.ObservationPaused)
        {
            value=owner.Observation.Capture.GetStatus() switch{DesktopCaptureStatus.UnsupportedPlatform=>TextResources.Get("desktopObservation.unsupportedPlatform"),DesktopCaptureStatus.PermissionRequired=>TextResources.Get("desktopObservation.permissionRequired"),DesktopCaptureStatus.Ready=>DesktopObservationUiText.ReasonText(owner.ObservationReason==ObservationReason.Disabled?ObservationReason.Allowed:owner.ObservationReason),_=>TextResources.Get("desktopObservation.reason.captureUnavailable")};
        }
        status.Text=value;permission.IsEnabled=OperatingSystem.IsMacOS();
        LocalizedUi.Set(pause,"Content",()=>owner.ObservationPaused?TextResources.Get("desktopObservation.resume"):TextResources.Get("desktopObservation.pause"));
        bool required=settings.DesktopObservationEnabled&&settings.GreetingEnabled&&settings.AiGreetingEnabled;
        pause.IsEnabled=!busy&&required&&!(pendingEnabled==false&&owner.Settings.DesktopObservationEnabled);
        upload.IsEnabled=!busy&&required&&path.Route!=ImageRouteKind.Unavailable&&DesktopObservationConsent.IsConfirmed(settings);
        enabled.IsEnabled=!busy;
        LocalizedUi.Set(confirm,"Content",()=>pendingEnabled==false&&owner.Settings.DesktopObservationEnabled?TextResources.Get("common.save"):TextResources.Get("desktopObservation.confirm"));
        var selected=exclusions.SelectedItem as string;var items=owner.Settings.DesktopObservationExcludedApps.ToArray();exclusions.ItemsSource=items;
        if(selected!=null)exclusions.SelectedItem=items.FirstOrDefault(item=>string.Equals(item,selected,StringComparison.OrdinalIgnoreCase));
    }
    private async Task<string?> ConfirmAsync()
    {
        var settings=owner.GlobalObservationSettings();var addresses=DesktopObservationUiText.Destinations(settings);string digest=DesktopObservationConsent.Destination(settings);
        if(addresses.Length==0||digest.Length==0){transientStatus=TextResources.Get("desktopObservation.settings.noDestination");return null;}
        var dialog=LocalizedUi.Window(()=>TextResources.Get("desktopObservation.consentTitle"),new Border(),500,350);dialog.Background=DesktopTheme.Surface;
        dialog.Content=new Border{Padding=new Thickness(22),Child=new ScrollViewer{HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Content=Ui.Column(SettingsVisual.Text(()=>TextResources.Format("desktopObservation.consent.description",DesktopObservationUiText.ReceivingSummary(settings)),14),SettingsVisual.Wrap(SettingsVisual.Button(()=>TextResources.Get("desktopObservation.consentCancel"),()=>dialog.Close(false)),SettingsVisual.Button(()=>TextResources.Get("desktopObservation.consentAccept"),()=>dialog.Close(true))))}};
        return await dialog.ShowDialog<bool>(host)?digest:null;
    }
    private async Task TestAsync(bool upload)
    {
        var settings=owner.GlobalObservationSettings();
        if(!settings.DesktopObservationEnabled){transientStatus=TextResources.Get("desktopObservation.reason.disabled");return;}
        if(upload)
        {
            // Every upload test visibly confirms its exact current destinations.
            string? digest=await ConfirmAsync();if(digest==null)return;
            if(!DesktopObservationConsent.IsConfirmed(settings))
            {
                bool confirmed=await owner.SaveObservationSettingsAsync(s=>
                {
                    var global=s.Copy();global.ImageDescriptionOverride=null;if(DesktopObservationConsent.Destination(global)!=digest)throw new InvalidOperationException();
                    s.DesktopObservationConsentVersion=DesktopObservationConsent.Version;s.DesktopObservationConsentDestination=digest;
                });
                if(!confirmed){transientStatus=TextResources.Get("desktopObservation.saveFailed");return;}
                settings=owner.GlobalObservationSettings();
            }
            if(DesktopObservationConsent.Destination(settings)!=digest||DesktopObservationConsent.Destination(owner.GlobalObservationSettings())!=digest){transientStatus=TextResources.Get("desktopObservation.reason.consentRequired");return;}
            if(!owner.Observation.TryBeginManualTest(DateTime.UtcNow)){transientStatus=TextResources.Get("desktopObservation.reason.busy");return;}
        }
        for(int seconds=3;seconds>0;seconds--){transientStatus=TextResources.Format("desktopObservation.preview.countdown",seconds);Refresh();await Task.Delay(1000,lifetime.Token);}
        transientStatus=null;
        if(disposed||!owner.Pet.ObservationEligible(true))return;
        if(SettingsStore.SnapshotIdentity(settings)!=SettingsStore.SnapshotIdentity(owner.GlobalObservationSettings())){transientStatus=DesktopObservationUiText.ReasonText(ObservationReason.Stale);return;}
        owner.ObserveObservationInput();long epoch=owner.Observation.Epoch;
        if(upload)
        {
            using var decision=await owner.Observation.GenerateAsync(settings,owner.Skin.Id,owner.Skin.Name,null,null,DateTime.Now,true,true,DesktopObservationMode.ManualTest);
            owner.ObserveObservationInput();
            if(!owner.Observation.IsCurrent(epoch)||!owner.Pet.ObservationEligible(true)||!owner.Observation.IsForegroundCurrent())return;
            transientStatus=decision.Message.Length==0?DesktopObservationUiText.ReasonText(decision.Reason):decision.Message;
            return;
        }
        using var frame=await owner.Observation.PreviewAsync(settings,epoch,lifetime.Token);
        owner.ObserveObservationInput();
        if(frame==null){transientStatus=TextResources.Get("desktopObservation.preview.empty");return;}
        if(!owner.Observation.IsCurrent(epoch)||!owner.Pet.ObservationEligible(true)){return;}
        var bitmap=new Bitmap(new MemoryStream(frame.EncodedPng));
        if(!owner.Observation.IsForegroundCurrent()){bitmap.Dispose();return;}
        preview?.Close();preview=LocalizedUi.Window(()=>TextResources.Get("desktopObservation.previewLocal"),Ui.Column(SettingsVisual.Text(()=>TextResources.Get("desktopObservation.previewHelp"),13,true),new Image{Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=480}),700,580);
        preview.Background=DesktopTheme.Surface;preview.Closed+=(_,_)=>{bitmap.Dispose();preview=null;};preview.Show();
    }
    private async Task AddExclusionAsync()
    {
        var input=LocalizedUi.Input("",()=>TextResources.Get("desktopObservation.bundleIdHint"));input.MinWidth=0;var notice=SettingsVisual.Text("",13,true);
        var dialog=LocalizedUi.Window(()=>TextResources.Get("desktopObservation.addApp"),new Border(),450,320);dialog.Background=DesktopTheme.Surface;
        var save=SettingsVisual.Button(()=>TextResources.Get("common.save"),()=>
        {
            string value=(input.Text??"").Trim();
            if(value.Length>200||!System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\z")){notice.Text=TextResources.Get("desktopObservation.bundleIdInvalid");return;}
            dialog.Close(value);
        });
        dialog.Content=Ui.Column(SettingsVisual.Text(()=>TextResources.Get("desktopObservation.bundleIdLabel")),input,notice,SettingsVisual.Wrap(SettingsVisual.Button(()=>TextResources.Get("common.cancel"),()=>dialog.Close(null)),save));
        string? result=await dialog.ShowDialog<string?>(host);if(result==null)return;
        bool saved=await owner.SaveObservationSettingsAsync(s=>{if(!s.DesktopObservationExcludedApps.Contains(result,StringComparer.OrdinalIgnoreCase))s.DesktopObservationExcludedApps.Add(result);});transientStatus=saved?null:TextResources.Get("desktopObservation.saveFailed");
    }
    private async Task RemoveExclusionAsync()
    {
        if(exclusions.SelectedItem is not string item)return;
        bool saved=await owner.SaveObservationSettingsAsync(s=>s.DesktopObservationExcludedApps.RemoveAll(value=>string.Equals(value,item,StringComparison.OrdinalIgnoreCase)));transientStatus=saved?null:TextResources.Get("desktopObservation.saveFailed");
    }
    private void ClearPreview(){if(Dispatcher.UIThread.CheckAccess())preview?.Close();else Dispatcher.UIThread.Post(()=>preview?.Close());}
    public void Dispose(){if(disposed)return;disposed=true;owner.Changed-=Refresh;owner.ObservationChanged-=Refresh;owner.Observation.ImagesCleared-=ClearPreview;TextResources.LanguageChanged-=LanguageChanged;owner.Observation.Invalidate(false);lifetime.Cancel();lifetime.Dispose();preview?.Close();}
}
