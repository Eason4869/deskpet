using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Dafeiyu;
using DeskPet.Platform.Mac;

namespace DeskPet.Desktop;

internal sealed class CompanionHomeEntrance : IDisposable
{
    readonly RelationshipHomeMotion motion = new();
    readonly Stopwatch clock = new();
    readonly Control owner;
    bool disposed;
    internal CompanionHomeEntrance(Control owner) { this.owner=owner; }
    internal void Float(Control control,double delay,double distance=20)
    {
        var previous=control.RenderTransform;
        var offset=new TranslateTransform();
        control.RenderTransform=offset;
        motion.Add(delay,RelationshipHomeMotion.CardSeconds,progress=>
        {
            control.Opacity=RelationshipHomeMotion.Ease(progress);
            offset.Y=distance*(1-RelationshipHomeMotion.Ease(progress));
            if(progress>=1)control.RenderTransform=previous;
        });
    }
    internal void Milestone(Control control,double delay)
    {
        var row=(Grid)control;
        var line=row.Children[0];
        var scale=new ScaleTransform(1,0);
        line.RenderTransformOrigin=new RelativePoint(.5,0,RelativeUnit.Relative);line.RenderTransform=scale;
        motion.Add(delay,RelationshipHomeMotion.CardSeconds,progress=>scale.ScaleY=RelationshipHomeMotion.Ease(progress));
        Float(row.Children[1],delay,0);
        Float(row.Children[2],delay,-8);
    }
    internal void Ring(Control ring) { HomeVisual.ScheduleRing(motion,ring,RelationshipHomeMotion.RelationReady); }
    internal void Start()
    {
        if(MacServices.PrefersReducedMotion||TopLevel.GetTopLevel(owner) is not TopLevel top){Dispose();return;}
        clock.Restart();top.RequestAnimationFrame(Frame);
    }
    void Frame(TimeSpan timestamp)
    {
        if(disposed)return;
        if(!owner.IsEffectivelyVisible||MacServices.PrefersReducedMotion||!motion.Advance(clock.Elapsed.TotalSeconds)) { Dispose();return; }
        if(TopLevel.GetTopLevel(owner) is TopLevel top)top.RequestAnimationFrame(Frame);
        else Dispose();
    }
    public void Dispose() { disposed=true;clock.Stop();motion.Finish(); }
}
