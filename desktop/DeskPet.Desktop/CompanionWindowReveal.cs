using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed class CompanionWindowReveal:IDisposable
{
    private static readonly TimeSpan RefreshLimit=TimeSpan.FromMilliseconds(120);
    private readonly Window window;
    private readonly Grid host;
    private readonly Control content;
    private readonly Func<bool> eligible,reducedMotion;
    private readonly Func<string> selectedKey,setting;
    private readonly Func<Point> avatar;
    private readonly Func<TimeSpan> now;
    private readonly Action? interruptTheme;
    private readonly DispatcherTimer timeout;
    private LiquidThemeReveal.SnapshotLayer? layer;
    private ThemeRevealPaths? paths;
    private string? targetKey;
    private long request;
    private int generation;
    private bool committed,playing,refreshQueued,disposed;
    private TimeSpan started,duration;

    internal CompanionWindowReveal(Window window,Grid host,Control content,Func<bool> eligible,
        Func<string> selectedKey,Func<string> setting,Func<Point> avatar,Func<bool>? reducedMotion=null,
        Func<TimeSpan>? now=null,Action? interruptTheme=null)
    {
        this.window=window;this.host=host;this.content=content;this.eligible=eligible;this.selectedKey=selectedKey;
        this.setting=setting;this.avatar=avatar;this.interruptTheme=interruptTheme;
        this.reducedMotion=reducedMotion??(()=>MacServices.PrefersReducedMotion);
        var clock=Stopwatch.StartNew();
        this.now=now??(()=>clock.Elapsed);
        timeout=new DispatcherTimer{Interval=RefreshLimit};
        timeout.Tick+=Interrupt;
        window.PropertyChanged+=WindowChanged;
        window.Closed+=Closed;
        DesktopTheme.AppearanceChanged+=Cancel;
    }

    private bool CanContinue()=>!disposed&&window.IsVisible&&content.IsEffectivelyVisible&&window.WindowState!=WindowState.Minimized;

    internal void Prepare(long token,string currentKey,string nextKey)
    {
        if(currentKey==nextKey)return;
        string effect=CompanionTransitionMotion.Resolve(setting());
        if(!CanContinue()||!eligible()||effect==CompanionTransitionMotion.Off||reducedMotion()||
            content.Bounds.Width<1||content.Bounds.Height<1||LiquidThemeReveal.HasUncapturableSurface(content))
        {Cancel();return;}
        // Capture the composition before replacing its overlay.
        var before=LiquidThemeReveal.Capture(window,host);
        interruptTheme?.Invoke();
        Cancel();
        if(before==null)return;
        try
        {
            Size bounds=content.Bounds.Size;
            Point origin=avatar();
            var shape=CompanionRevealPaths.Create(effect,bounds.Width,bounds.Height,origin.X,origin.Y,.23*bounds.Width);
            paths=ThemeRevealPaths.ForCompanion(shape);
            layer=new LiquidThemeReveal.SnapshotLayer(before,bounds,paths,"CompanionWindowOverlay");
            request=token;targetKey=nextKey;duration=CompanionTransitionMotion.Duration(effect);
            host.Children.Add(layer);
        }
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            if(layer==null)before.Dispose();
            Cancel();
        }
    }

    internal void MarkCommitted(long token,string actualKey)
    {
        if(layer==null||request!=token)return;
        if(actualKey!=targetKey){Cancel();return;}
        committed=true;
        started=now();
        timeout.Start();
    }

    internal void OnContentRefreshed()
    {
        if(layer==null||!committed||playing||refreshQueued)return;
        refreshQueued=true;
        int pending=generation;
        Dispatcher.UIThread.Post(()=>{
            if(pending!=generation)return;
            if(now()-started>=RefreshLimit||!CanContinue()||selectedKey()!=targetKey||reducedMotion())
            {Cancel();return;}
            try
            {
                content.UpdateLayout();
                if(pending!=generation)return;
                if(LiquidThemeReveal.HasUncapturableSurface(content))
                {Cancel();return;}
            }
            catch(Exception error) when(error is InvalidOperationException or ArgumentException or OutOfMemoryException)
            {Cancel();return;}
            timeout.Stop();
            playing=true;
            started=now();
            window.RequestAnimationFrame(Frame);
            void Frame(TimeSpan _)
            {
                if(pending==generation&&RenderFrame())window.RequestAnimationFrame(Frame);
            }
        },DispatcherPriority.Loaded);
    }

    private bool RenderFrame()
    {
        double progress=(now()-started).TotalMilliseconds/duration.TotalMilliseconds;
        if(progress>=1||!CanContinue()||selectedKey()!=targetKey||reducedMotion()){Cancel();return false;}
        try{layer!.SetFrame(paths!,progress);return true;}
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or OutOfMemoryException){Cancel();return false;}
    }

    internal void AbortPending(long token){if(request==token&&!committed)Cancel();}

    internal void Cancel()
    {
        generation++;
        timeout.Stop();
        playing=committed=refreshQueued=false;
        if(layer!=null){host.Children.Remove(layer);layer.Dispose();}
        layer=null;paths=null;targetKey=null;
    }

    private void WindowChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
    {
        if(args.Property==Visual.IsVisibleProperty&&!window.IsVisible||
            args.Property==Window.WindowStateProperty&&window.WindowState==WindowState.Minimized)Cancel();
    }
    private void Interrupt(object? sender,EventArgs args)=>Cancel();
    private void Closed(object? sender,EventArgs args)=>Dispose();

    public void Dispose()
    {
        if(disposed)return;
        disposed=true;Cancel();
        timeout.Tick-=Interrupt;
        window.PropertyChanged-=WindowChanged;
        window.Closed-=Closed;
        DesktopTheme.AppearanceChanged-=Cancel;
    }
}
