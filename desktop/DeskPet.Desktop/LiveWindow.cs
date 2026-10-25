using Dafeiyu;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed class LiveWindow : Window
{
    private readonly DesktopController owner;
    private readonly TextBlock status = Ui.Text("");
    private readonly TextBlock user = Ui.Text(""), reply = Ui.Text("");
    private readonly ProgressBar input = new() { Minimum = 0, Maximum = 1 }, output = new() { Minimum = 0, Maximum = 1 };
    private readonly DispatcherTimer meter;
    private readonly SpriteAssets avatarAssets = new();
    private readonly AvatarView avatar;
    private readonly TextBlock partnerName = Ui.Text("");
    private bool closing;
    public LiveWindow(DesktopController controller)
    {
        owner = controller; LocalizedUi.Set(this,"Title",()=>(TextResources.Get("liveWindow.liveWindow.title"))); Width = 560; Height = 490;
        avatar = new AvatarView { Assets = avatarAssets, Skin = owner.Skin, Width = 36, Height = 36 };
        partnerName.VerticalAlignment = VerticalAlignment.Center;
        Content = new Border { Padding = new Thickness(20), Child = Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("liveWindow.liveWindow.text")), 22), Ui.Row(avatar, partnerName), status,
            LocalizedUi.Text(()=>(TextResources.Get("liveWindow.liveWindow.text.alternativeA"))), input, LocalizedUi.Text(()=>(TextResources.Get("liveWindow.liveWindow.text.alternativeB"))), output,
            new ScrollViewer { Content = Ui.Column(LocalizedUi.Text(()=>(TextResources.Get("liveWindow.liveWindow.text.alternativeC")), 12), user, LocalizedUi.Text(()=>(TextResources.Get("liveWindow.liveWindow.text.alternativeD")), 12), reply), MaxHeight = 200 },
            Ui.Row(LocalizedUi.AsyncButton(()=>(TextResources.Get("liveWindow.liveWindow.asyncButton")), StartAsync, owner.Error), LocalizedUi.AsyncButton(()=>(TextResources.Get("liveWindow.liveWindow.asyncButton.alternativeA")), owner.Live.StopAsync, owner.Error), LocalizedUi.Button(()=>(TextResources.Get("liveWindow.liveWindow.text.alternativeE")), () => owner.ShowSettingsSection("live"))),
            LocalizedUi.Text(()=>(TextResources.Get("liveWindow.liveWindow.text.alternativeF")))) };
        owner.Live.StateChanged += Refresh; owner.Live.PartialTranscript += Transcript; owner.Changed += RefreshPartner;
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true;
            try { await owner.Live.StopAsync(); Close(); } catch (Exception error) { closing = false; owner.Error(error); }
        };
        meter = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => { input.Value = owner.Live.InputLevel; output.Value = owner.Live.OutputLevel; });
        Closed += (_, _) => { meter.Stop(); owner.Live.StateChanged -= Refresh; owner.Live.PartialTranscript -= Transcript; owner.Changed -= RefreshPartner; avatarAssets.Dispose(); };
        Refresh(); RefreshPartner();
    }
    private async Task StartAsync()
    {
        var expectedPersona = owner.Skin.Id;
        if (owner.Live.IsActive) return;
        await owner.Queue.PauseAsync(); await owner.Dictation.CancelAsync(); await owner.Session.StopAsync();
        if (!IsVisible || closing || owner.Skin.Id != expectedPersona) return;
        var granted = await owner.RequestMicrophoneAccessAsync();
        if (!IsVisible || closing || owner.Skin.Id != expectedPersona) return;
        if (!granted) throw new InvalidOperationException(TextResources.Get("liveWindow.startAsync.text"));
        if (owner.Live.IsActive) return;
        if (owner.Dictation.IsBusy || owner.Session.IsBusy) throw new InvalidOperationException(TextResources.Get("liveWindow.startAsync.text.alternativeA"));
        var companion=owner.RelationshipCompanionId;
        var context=await Task.Run(()=>owner.Relationships.StageContext(companion));
        if (!IsVisible || closing || owner.Skin.Id != expectedPersona) return;
        await owner.Live.StartAsync(owner.Settings, owner.Settings.SystemPrompt+"\n"+context);
    }
    private void Refresh() => Dispatcher.UIThread.Post(() => status.Text = owner.Live.Status);
    private void RefreshPartner()
    {
        if (!ReferenceEquals(avatar.Skin, owner.Skin)) avatar.Skin = owner.Skin;
        LocalizedUi.Set(partnerName,"Text",()=>(owner.Skin.DisplayName));
    }
    private void Transcript(string role, string text) => Dispatcher.UIThread.Post(() => { if (role == "user") user.Text = text; else reply.Text = text; });
}
