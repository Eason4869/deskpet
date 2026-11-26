using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    internal sealed class QuickChatExpansion
    {
        internal readonly Rect Bounds;
        internal readonly BitmapSource Image;

        QuickChatExpansion(Rect bounds,BitmapSource image){Bounds=bounds;Image=image;}

        internal static QuickChatExpansion Capture(QuickChatWindow quick)
        {
            if(quick==null||!quick.IsVisible||!SystemParameters.ClientAreaAnimation)return null;
            var handle=new WindowInteropHelper(quick).Handle;
            var root=quick.Content as FrameworkElement;
            if(handle==IntPtr.Zero||root==null||root.ActualWidth<1||root.ActualHeight<1)return null;
            var image=LiquidThemeReveal.Capture(root);
            return image==null?null:new QuickChatExpansion(DesktopInterop.Bounds(handle),image);
        }

        internal static Rect Interpolate(Rect from,Rect to,double progress)
        {
            double t=1-Math.Pow(1-progress,3);
            return new Rect(from.X+(to.X-from.X)*t,from.Y+(to.Y-from.Y)*t,
                from.Width+(to.Width-from.Width)*t,from.Height+(to.Height-from.Height)*t);
        }
    }

    internal sealed class ChatWindowMotion
    {
        readonly Window window;
        readonly Stopwatch clock=new Stopwatch();
        Action<double> draw;
        Action cleanup;
        double duration;
        bool expanding,closing;

        internal ChatWindowMotion(Window window)
        {
            this.window=window;
            window.IsVisibleChanged+=VisibilityChanged;
            window.StateChanged+=delegate{if(window.WindowState==WindowState.Minimized)Stop();};
            window.Closed+=delegate{Stop();};
            window.PreviewKeyDown+=delegate(object sender,KeyEventArgs args)
            {
                if(args.SystemKey!=Key.F4||(Keyboard.Modifiers&ModifierKeys.Alt)==0)return;
                args.Handled=true;Close();
            };
        }

        bool CanAnimate {get{return SystemParameters.ClientAreaAnimation&&window.WindowState!=WindowState.Minimized;}}

        void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
        {
            if(!window.IsVisible){Stop();return;}
            if(!expanding&&CanAnimate)Fade(false);
        }

        internal void Close()
        {
            if(closing)return;
            if(!window.IsVisible||!CanAnimate){window.Close();return;}
            Stop();
            closing=true;
            Fade(true);
        }

        void Fade(bool fadeOut)
        {
            Action<double> opacity=value=>window.Opacity=value;
            Action restore=()=>opacity(1);
            var handle=new WindowInteropHelper(window).Handle;
            var content=window.Content as FrameworkElement;
            if(!window.AllowsTransparency&&content!=null&&handle!=IntPtr.Zero)
            {
                content.UpdateLayout();
                if(content.ActualWidth>0&&content.ActualHeight>0&&!LiquidThemeReveal.HasUncapturableSurface(content))
                {
                    var snapshot=LiquidThemeReveal.Capture(content);
                    if(snapshot!=null&&DesktopInterop.SetWindowCloaked(handle,true))
                    {
                        var overlay=ShowOverlay(snapshot,DesktopInterop.Bounds(handle),fadeOut?1:.12,"ChatWindowFadeOverlay");
                        if(overlay!=null)
                        {
                            opacity=value=>overlay.Opacity=value;
                            restore=()=>{DesktopInterop.SetWindowCloaked(handle,false);overlay.Close();};
                        }
                        else DesktopInterop.SetWindowCloaked(handle,false);
                    }
                }
            }
            opacity(fadeOut?1:.12);
            Start(fadeOut?95:115,t=>opacity(fadeOut?1-t:.12+.88*(1-(1-t)*(1-t))),restore);
        }

        internal void ShowExpanded(QuickChatExpansion source)
        {
            if(source==null||!SystemParameters.ClientAreaAnimation){Show();return;}
            Stop();
            expanding=true;
            var handle=new WindowInteropHelper(window).EnsureHandle();
            bool cloaked=DesktopInterop.SetWindowCloaked(handle,true);
            bool playing=false;
            try
            {
                Show();
                Rect target=DesktopInterop.Bounds(handle);
                var overlay=ShowOverlay(source.Image,source.Bounds,1,"QuickChatExpansionOverlay");
                if(overlay==null)return;
                var overlayHandle=new WindowInteropHelper(overlay).Handle;
                Start(235,t=>{
                    DesktopInterop.MoveAndResize(overlayHandle,QuickChatExpansion.Interpolate(source.Bounds,target,t));
                    if(t<.68)return;
                    if(cloaked){DesktopInterop.SetWindowCloaked(handle,false);cloaked=false;}
                    overlay.Opacity=1-(t-.68)/.32;
                },()=>{if(cloaked)DesktopInterop.SetWindowCloaked(handle,false);overlay.Close();});
                playing=true;
            }
            finally
            {
                expanding=false;
                if(!playing&&cloaked)DesktopInterop.SetWindowCloaked(handle,false);
            }
        }

        void Show()
        {
            // A handle can already exist on a hidden or minimized main window.
            if(!window.IsVisible)window.Show();
            if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
            window.Activate();
        }

        static Window ShowOverlay(BitmapSource bitmap,Rect bounds,double opacity,string name)
        {
            var overlay=new Window{Name=name,AllowsTransparency=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
                ShowActivated=false,ShowInTaskbar=false,Topmost=true,Background=Brushes.Transparent,Focusable=false,Opacity=opacity,
                Content=new Image{Source=bitmap,Stretch=Stretch.Fill,IsHitTestVisible=false}};
            try
            {
                var handle=new WindowInteropHelper(overlay).EnsureHandle();
                DesktopInterop.MakeToolWindow(handle);
                DesktopInterop.SetClickThrough(handle,true);
                DesktopInterop.MoveAndResize(handle,bounds);
                overlay.Show();
                DesktopInterop.MoveAndResize(handle,bounds);
                return overlay;
            }
            catch(InvalidOperationException){overlay.Close();return null;}
            catch(ArgumentException){overlay.Close();return null;}
        }

        void Start(double milliseconds,Action<double> drawFrame,Action complete)
        {
            duration=milliseconds;draw=drawFrame;cleanup=complete;
            clock.Restart();
            CompositionTarget.Rendering+=Frame;
        }

        void Frame(object sender,EventArgs args)
        {
            double t=clock.Elapsed.TotalMilliseconds/duration;
            if(t<1&&CanAnimate){draw(t);return;}
            if(!closing){Stop();return;}
            try{window.Close();}
            finally{Stop();}
        }

        void Stop()
        {
            if(draw==null)return;
            CompositionTarget.Rendering-=Frame;
            var complete=cleanup;
            draw=null;cleanup=null;closing=false;
            clock.Stop();
            complete();
        }
    }
}
