using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class CompanionInstallationBubble : Window
{
    private readonly DesktopController owner;
    internal CompanionInstallationStatus Status {get;}=new();
    private readonly TextBlock message=Ui.Text("");
    private readonly Button cancel,retry;
    private readonly DispatcherTimer timer;
    private string? previousText;
    private bool shuttingDown;
    internal CompanionInstallationBubble(DesktopController controller)
    {
        owner=controller;Name="CompanionInstallationBubble";Width=340;Height=145;MinHeight=110;MaxHeight=260;SizeToContent=SizeToContent.Height;CanResize=false;ShowInTaskbar=false;ShowActivated=false;
        WindowDecorations=WindowDecorations.None;TransparencyLevelHint=new[]{WindowTransparencyLevel.Transparent};Background=Brushes.Transparent;
        var close=LocalizedUi.Button(()=>TextResources.Get("common.close"),()=>{Status.Dismiss();Hide();});close.Name="DismissCompanionInstallation";close.HorizontalAlignment=HorizontalAlignment.Right;
        cancel=LocalizedUi.Button(()=>TextResources.Get("dialog.cancelDownload"),()=>owner.CancelCompanionSelection(Status.Request));cancel.Name="CancelCompanionInstallation";
        retry=LocalizedUi.Button(()=>TextResources.Get("common.retry"),()=>{var skin=owner.Skins.FirstOrDefault(s=>s.PickerKey==Status.PickerKey);if(skin!=null)owner.Run(()=>owner.SelectSkinAsync(skin,Enum.Parse<CompanionSelectionOrigin>(Status.Origin)));});retry.Name="RetryCompanionInstallation";
        message.Name="CompanionInstallationMessage";message.TextWrapping=TextWrapping.Wrap;
        var body=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(16,10),Background=DesktopTheme.Brush("#FAFAFB"),BorderBrush=DesktopTheme.Brush("#DDDEE1"),BorderThickness=new Thickness(1),Child=Ui.Column(close,message,Ui.Row(cancel,retry))};
        var root=new Grid{RowDefinitions=new RowDefinitions("Auto,14")};root.Children.Add(body);
        var tail=new Avalonia.Controls.Shapes.Polygon{Points=new Avalonia.Collections.AvaloniaList<Point>{new(158,0),new(170,13),new(182,0)},Fill=DesktopTheme.Brush("#FAFAFB")};Grid.SetRow(tail,1);root.Children.Add(tail);Content=root;
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};timer.Tick+=(_,_)=>RenderStatus();Closing+=(_,args)=>{if(!shuttingDown){args.Cancel=true;Status.Dismiss();Hide();}};Closed+=(_,_)=>timer.Stop();
    }
    internal void Shutdown(){shuttingDown=true;Close();}
    internal void Accept(CompanionSelectionUpdate update)
    {
        bool wasActive=Status.Active;string? previousStage=Status.Stage;Status.Accept(update);
        // Lifecycle feedback is immediate; only continuous progress waits for the timer.
        if(Status.Active!=wasActive||(Status.Stage!=previousStage&&Status.Stage!="progress"))RenderStatus();
        if(Status.Active&&!timer.IsEnabled)timer.Start();
    }
    internal void RenderStatus()
    {
        string text=Status.Text;if(text!=previousText){previousText=text;message.Text=text;owner.ShowCompanionInstallationStatus(Status.Origin,text);}
        cancel.IsVisible=Status.CanCancel;retry.IsVisible=Status.CanRetry;
        if(Status.Visible(owner.Pet.IsPetVisible)){
            Topmost=owner.Settings.Topmost;if(!IsVisible)Show();
            var pet=owner.Pet;double scale=pet.RenderScaling;var work=pet.Screens.ScreenFromWindow(pet)?.WorkingArea;
            int x=pet.Position.X+(int)((pet.Bounds.Width-Bounds.Width)*scale/2),y=pet.Position.Y-(int)((Bounds.Height+8)*scale);
            if(work is { } area){x=Math.Clamp(x,area.X,Math.Max(area.X,area.Right-(int)(Bounds.Width*scale)));y=Math.Clamp(y,area.Y,Math.Max(area.Y,area.Bottom-(int)(Bounds.Height*scale)));}
            Position=new PixelPoint(x,y);
        }else if(IsVisible)Hide();
        if(!Status.Active)timer.Stop();
    }
}

internal sealed partial class DesktopController
{
    private CompanionInstallationBubble? companionInstallationBubble;
    private void PresentCompanionInstallation(CompanionSelectionUpdate update){companionInstallationBubble??=new(this);companionInstallationBubble.Accept(update);}
    internal void ShowCompanionInstallationStatus(string origin,string text){if(origin is "Settings" or "Onboarding"){CompanionSelectionNotice=text;Changed?.Invoke();}}
}
