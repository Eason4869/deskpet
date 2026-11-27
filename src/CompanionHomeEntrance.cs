using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class CompanionHomeEntrance : IDisposable
    {
        readonly RelationshipHomeMotion motion = new RelationshipHomeMotion();
        readonly Stopwatch clock = new Stopwatch();
        readonly FrameworkElement owner;
        bool running;
        internal CompanionHomeEntrance(FrameworkElement owner) { this.owner=owner; }
        internal void Float(FrameworkElement control,double delay,double distance=20)
        {
            var previous=control.RenderTransform;
            var offset=new TranslateTransform();
            control.RenderTransform=offset;
            motion.Add(delay,RelationshipHomeMotion.CardSeconds,delegate(double progress)
            {
                control.Opacity=RelationshipHomeMotion.Ease(progress);
                offset.Y=distance*(1-RelationshipHomeMotion.Ease(progress));
                if(progress>=1)control.RenderTransform=previous;
            });
        }
        internal void Milestone(FrameworkElement control,double delay)
        {
            var row=(Grid)control;
            var line=(FrameworkElement)row.Children[0];
            var scale=new ScaleTransform(1,0);
            line.RenderTransformOrigin=new Point(.5,0);line.RenderTransform=scale;
            motion.Add(delay,RelationshipHomeMotion.CardSeconds,delegate(double progress){scale.ScaleY=RelationshipHomeMotion.Ease(progress);});
            Float((FrameworkElement)row.Children[1],delay,0);
            Float((FrameworkElement)row.Children[2],delay,-8);
        }
        internal void Ring(FrameworkElement ring)
        { HomeVisual.ScheduleRing(motion,ring,RelationshipHomeMotion.RelationReady); }
        internal void Start()
        {
            if(!SystemParameters.ClientAreaAnimation){Dispose();return;}
            clock.Restart();running=true;CompositionTarget.Rendering+=Frame;
        }
        void Frame(object sender,EventArgs args)
        {
            if(!owner.IsVisible||!SystemParameters.ClientAreaAnimation||!motion.Advance(clock.Elapsed.TotalSeconds))Dispose();
        }
        public void Dispose()
        {
            if(running)CompositionTarget.Rendering-=Frame;
            running=false;clock.Stop();motion.Finish();
        }
    }
}
