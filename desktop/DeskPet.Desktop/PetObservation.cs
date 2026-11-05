using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class PetWindow
{
    internal bool ObservationEligible(bool manual=false)=>sessionInteractive&&IsVisible&&!fullscreenHidden&&!owner.ObservationFullscreen&&!Paused&&!dragging&&!holidayPlayback.Active&&!holidayTransition.Active&&action=="idle"&&!owner.Settings.QuietMode&&!owner.ObservationPaused&&!owner.ObservationBlockingPanel(manual)&&!owner.HolidayBusinessBusy&&!owner.DataMaintenance.IsMaintenanceActive&&!owner.RelationshipNoticeVisible&&!owner.IsExiting;
    private async Task ShowObservationAsync()
    {
        if(!ObservationEligible())return;
        owner.ObserveObservationInput();
        var host=owner.Observation;long epoch=host.Epoch;var settings=owner.GlobalObservationSettings();
        var skin=owner.Skin;var relationship=owner.RelationshipCompanionId;int revision=owner.Relationships.SettingsVersion;
        IList<ChatMessage>? memory=null;try{memory=new CompanionMemory(()=>owner.Session).Capture(skin.Id);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException){}
        using var decision=await host.GenerateAsync(settings,skin.Id,skin.Name,memory,recentGreetings.ToArray(),HolidayLocalNow(),true,true,current:()=>revision==owner.Relationships.SettingsVersion);
        owner.ObserveObservationInput();
        owner.ObservationReason=decision.Reason;owner.RefreshObservationStatus();
        if(decision.Kind==ProactiveDecisionKind.Skip||!host.IsCurrent(epoch)||!ObservationEligible()||!ReferenceEquals(skin,owner.Skin)||relationship!=owner.RelationshipCompanionId||revision!=owner.Relationships.SettingsVersion||!host.IsForegroundCurrent())return;
        var item=decision.CandidateEvent??new ProactiveEvent(Guid.NewGuid().ToString("N"),skin.Id,decision.Message,null,DateTime.UtcNow,"","");
        string eventId=item.Id;var message=Ui.Text(decision.Message);message.Name="GreetingMessage";
        var bubble=LocalizedUi.Window(()=>TextResources.Format("petWindow.showGreetingAsync.titleName",skin.DisplayName),Ui.Column(message,LocalizedUi.Button(()=>TextResources.Get("desktopObservation.reply"),()=>{DismissGreeting();owner.ReplyToProactive(eventId);})),320,130);
        bubble.ShowActivated=false;bubble.ShowInTaskbar=false;bubble.Topmost=owner.Settings.Topmost;bubble.CanResize=false;bubble.WindowDecorations=WindowDecorations.None;bubble.Background=DesktopTheme.Surface;
        var area=WorkArea();bubble.Position=new PixelPoint(Math.Clamp(Position.X-50,area.X,Math.Max(area.X,area.Right-(int)(320*RenderScaling))),Math.Max(area.Y,Position.Y-(int)(135*RenderScaling)));
        greetingBubble=bubble;bubble.Show();
        if(bubble.IsVisible&&host.IsCurrent(epoch)&&ObservationEligible()&&host.IsForegroundCurrent())
        {owner.RegisterShownProactive(item,settings);recentGreetings.Add(decision.Message);if(recentGreetings.Count>6)recentGreetings.RemoveAt(0);owner.SpeakProactiveGreeting(decision.Message);}
        else{greetingBubble=null;bubble.Close();return;}
        var dismiss=new DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};dismiss.Tick+=(_,_)=>{if(!ReferenceEquals(greetingBubble,bubble)){dismiss.Stop();return;}if(owner.ProactiveGreetingSpeechActive){dismiss.Interval=TimeSpan.FromMilliseconds(250);return;}dismiss.Stop();DismissGreeting();};bubble.Closed+=(_,_)=>{dismiss.Stop();if(ReferenceEquals(greetingBubble,bubble)){greetingBubble=null;owner.StopProactiveGreetingSpeech();}};dismiss.Start();
    }
}
