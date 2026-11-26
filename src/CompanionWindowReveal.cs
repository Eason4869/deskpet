using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu
{
    internal sealed class CompanionWindowReveal:IDisposable
    {
        static readonly TimeSpan RefreshLimit=TimeSpan.FromMilliseconds(120);
        readonly Window window;
        readonly Grid host;
        readonly FrameworkElement content;
        readonly Func<bool> eligible,reducedMotion;
        readonly Func<string> selectedKey,setting;
        readonly Func<Point> avatar;
        readonly Func<double> seam;
        readonly Func<FrameworkElement,BitmapSource> takeOverTheme;
        readonly Func<TimeSpan> now;
        readonly DispatcherTimer timeout;
        LiquidThemeReveal.SnapshotLayer layer;
        ThemeRevealPaths paths;
        string targetKey;
        long request;
        int generation;
        bool committed,playing,refreshQueued,disposed;
        TimeSpan started,duration;
        TimeSpan? lastRenderingTime;

        internal CompanionWindowReveal(Window window,Grid host,FrameworkElement content,Func<bool> eligible,
            Func<string> selectedKey,Func<string> setting,Func<Point> avatar,Func<double> seam,Func<FrameworkElement,BitmapSource> takeOverTheme,
            Func<bool> reducedMotion=null,Func<TimeSpan> now=null)
        {
            this.window=window;this.host=host;this.content=content;this.eligible=eligible;this.selectedKey=selectedKey;
            this.setting=setting;this.avatar=avatar;this.seam=seam;this.takeOverTheme=takeOverTheme;
            this.reducedMotion=reducedMotion??(()=>!SystemParameters.ClientAreaAnimation);
            var clock=Stopwatch.StartNew();
            this.now=now??(()=>clock.Elapsed);
            timeout=new DispatcherTimer(DispatcherPriority.Normal,window.Dispatcher){Interval=RefreshLimit};
            timeout.Tick+=Interrupt;
            window.StateChanged+=StateChanged;
            window.IsVisibleChanged+=VisibilityChanged;
            window.Closed+=Closed;
            ThemeManager.Changed+=Cancel;
        }

        bool CanContinue()
        {
            return !disposed&&window.IsVisible&&content.IsVisible&&window.WindowState!=WindowState.Minimized;
        }

        internal void Prepare(long token,string currentKey,string nextKey)
        {
            if(currentKey==nextKey)return;
            string effect=CompanionTransitionMotion.Resolve(setting());
            if(!CanContinue()||!eligible()||effect==CompanionTransitionMotion.Off||reducedMotion()||
                content.ActualWidth<1||content.ActualHeight<1||LiquidThemeReveal.HasUncapturableSurface(content))
            {Cancel();return;}
            // The old mask, decorations and visible live content become one bitmap.
            // A burst of selections never accumulates a stack of overlays.
            var before=takeOverTheme(content)??LiquidThemeReveal.Capture(layer==null?content:host);
            Cancel();
            if(before==null)return;
            try
            {
                Size bounds=content.RenderSize;
                Point origin=avatar();
                var shape=CompanionRevealPaths.Create(effect,bounds.Width,bounds.Height,origin.X,origin.Y,seam());
                paths=ThemeRevealPaths.ForCompanion(shape);
                layer=new LiquidThemeReveal.SnapshotLayer(before,bounds,paths,"CompanionWindowOverlay");
                targetKey=nextKey;
                request=token;
                duration=CompanionTransitionMotion.Duration(effect);
                host.Children.Add(layer);
            }
            catch(InvalidOperationException){Cancel();}
            catch(ArgumentException){Cancel();}
            catch(OutOfMemoryException){Cancel();}
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
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{
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
                catch(InvalidOperationException){Cancel();return;}
                catch(ArgumentException){Cancel();return;}
                catch(OutOfMemoryException){Cancel();return;}
                timeout.Stop();
                playing=true;
                started=now();
                lastRenderingTime=null;
                CompositionTarget.Rendering+=Frame;
            }));
        }

        void Frame(object sender,EventArgs args)
        {
            if(!playing)return;
            var render=args as RenderingEventArgs;
            if(render!=null)
            {
                if(lastRenderingTime==render.RenderingTime)return;
                lastRenderingTime=render.RenderingTime;
            }
            double progress=(now()-started).TotalMilliseconds/duration.TotalMilliseconds;
            if(progress>=1||!CanContinue()||selectedKey()!=targetKey||reducedMotion()){Cancel();return;}
            try{layer.SetFrame(paths,progress);}
            catch(InvalidOperationException){Cancel();}
            catch(ArgumentException){Cancel();}
            catch(OutOfMemoryException){Cancel();}
        }

        internal void AbortPending(long token)
        {if(request==token&&!committed)Cancel();}

        internal void Cancel()
        {
            generation++;
            timeout.Stop();
            if(playing)CompositionTarget.Rendering-=Frame;
            playing=committed=refreshQueued=false;
            if(layer!=null)host.Children.Remove(layer);
            layer=null;paths=null;targetKey=null;
        }

        void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){if(!window.IsVisible)Cancel();}
        void StateChanged(object sender,EventArgs args){if(window.WindowState==WindowState.Minimized)Cancel();}
        void Interrupt(object sender,EventArgs args){Cancel();}
        void Closed(object sender,EventArgs args){Dispose();}

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            Cancel();
            timeout.Tick-=Interrupt;
            window.StateChanged-=StateChanged;
            window.IsVisibleChanged-=VisibilityChanged;
            window.Closed-=Closed;
            ThemeManager.Changed-=Cancel;
        }
    }
}
