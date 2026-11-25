using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        readonly Action interruptTheme;
        readonly Func<TimeSpan> now;
        readonly DispatcherTimer timeout;
        readonly ScrollChangedEventHandler scrollChanged;
        LiquidThemeReveal.SnapshotLayer layer;
        ThemeRevealPaths paths;
        string targetKey;
        long request;
        int generation;
        bool committed,playing,refreshQueued,disposed;
        TimeSpan started,duration;
        Size bounds;
        DpiScale dpi;

        internal CompanionWindowReveal(Window window,Grid host,FrameworkElement content,Func<bool> eligible,
            Func<string> selectedKey,Func<string> setting,Func<Point> avatar,Func<double> seam,Action interruptTheme,
            Func<bool> reducedMotion=null,Func<TimeSpan> now=null)
        {
            this.window=window;this.host=host;this.content=content;this.eligible=eligible;this.selectedKey=selectedKey;
            this.setting=setting;this.avatar=avatar;this.seam=seam;this.interruptTheme=interruptTheme;
            this.reducedMotion=reducedMotion??(()=>!SystemParameters.ClientAreaAnimation);
            var clock=Stopwatch.StartNew();
            this.now=now??(()=>clock.Elapsed);
            timeout=new DispatcherTimer(DispatcherPriority.Normal,window.Dispatcher){Interval=RefreshLimit};
            timeout.Tick+=Interrupt;
            scrollChanged=Scrolled;
            content.SizeChanged+=Interrupt;
            window.DpiChanged+=OnDpiChanged;
            window.Deactivated+=Interrupt;
            window.StateChanged+=Interrupt;
            window.IsVisibleChanged+=VisibilityChanged;
            window.PreviewMouseDown+=Interrupt;
            window.PreviewMouseWheel+=Interrupt;
            window.PreviewKeyDown+=Interrupt;
            window.AddHandler(ScrollViewer.ScrollChangedEvent,scrollChanged,true);
            window.Closed+=Closed;
            ThemeManager.Changed+=Cancel;
        }

        bool CanShow()
        {
            return !disposed&&eligible()&&window.IsVisible&&content.IsVisible&&window.WindowState!=WindowState.Minimized;
        }

        internal void Prepare(long token,string currentKey,string nextKey)
        {
            Cancel();
            if(currentKey==nextKey||!CanShow())return;
            interruptTheme();
            string effect=CompanionTransitionMotion.Resolve(setting());
            if(effect==CompanionTransitionMotion.Off||reducedMotion()||content.ActualWidth<1||content.ActualHeight<1||
                LiquidThemeReveal.HasUncapturableSurface(content))return;
            var before=LiquidThemeReveal.Capture(content);
            if(before==null)return;
            try
            {
                bounds=content.RenderSize;
                dpi=VisualTreeHelper.GetDpi(content);
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
                if(now()-started>=RefreshLimit||!CanShow()||selectedKey()!=targetKey||reducedMotion())
                {Cancel();return;}
                try
                {
                    content.UpdateLayout();
                    if(pending!=generation)return;
                    if(content.RenderSize!=bounds||LiquidThemeReveal.HasUncapturableSurface(content))
                    {Cancel();return;}
                    var after=LiquidThemeReveal.Capture(content);
                    if(after==null||now()-started>=RefreshLimit||!layer.SetAfter(after)){Cancel();return;}
                }
                catch(InvalidOperationException){Cancel();return;}
                catch(ArgumentException){Cancel();return;}
                catch(OutOfMemoryException){Cancel();return;}
                timeout.Stop();
                playing=true;
                started=now();
                CompositionTarget.Rendering+=Frame;
            }));
        }

        void Frame(object sender,EventArgs args)
        {
            if(!playing)return;
            double progress=(now()-started).TotalMilliseconds/duration.TotalMilliseconds;
            if(progress>=1||!CanShow()||selectedKey()!=targetKey||reducedMotion()){Cancel();return;}
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
        void Scrolled(object sender,ScrollChangedEventArgs args)
        {if(playing&&(args.HorizontalChange!=0||args.VerticalChange!=0))Cancel();}
        void OnDpiChanged(object sender,DpiChangedEventArgs args)
        {
            if(layer!=null&&(args.NewDpi.DpiScaleX!=dpi.DpiScaleX||args.NewDpi.DpiScaleY!=dpi.DpiScaleY))Cancel();
        }
        void Interrupt(object sender,EventArgs args){Cancel();}
        void Closed(object sender,EventArgs args){Dispose();}

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            Cancel();
            timeout.Tick-=Interrupt;
            content.SizeChanged-=Interrupt;
            window.DpiChanged-=OnDpiChanged;
            window.Deactivated-=Interrupt;
            window.StateChanged-=Interrupt;
            window.IsVisibleChanged-=VisibilityChanged;
            window.PreviewMouseDown-=Interrupt;
            window.PreviewMouseWheel-=Interrupt;
            window.PreviewKeyDown-=Interrupt;
            window.RemoveHandler(ScrollViewer.ScrollChangedEvent,scrollChanged);
            window.Closed-=Closed;
            ThemeManager.Changed-=Cancel;
        }
    }
}
