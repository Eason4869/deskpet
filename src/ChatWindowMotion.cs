using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    internal sealed class QuickChatExpansion
    {
        internal readonly Rect Bounds;
        internal readonly BitmapSource Image;
        readonly QuickChatWindow source;

        QuickChatExpansion(QuickChatWindow source,Rect bounds,BitmapSource image){this.source=source;Bounds=bounds;Image=image;}
        internal void HideSource(){if(source.IsVisible)source.Hide();}

        internal static QuickChatExpansion Capture(QuickChatWindow quick)
        {
            if(quick==null||!quick.IsVisible||!SystemParameters.ClientAreaAnimation)return null;
            var handle=new WindowInteropHelper(quick).Handle;
            var root=quick.Content as FrameworkElement;
            if(handle==IntPtr.Zero||root==null||root.ActualWidth<1||root.ActualHeight<1)return null;
            var image=LiquidThemeReveal.Capture(root);
            return image==null?null:new QuickChatExpansion(quick,DesktopInterop.Bounds(handle),image);
        }

        internal static Rect Interpolate(Rect from,Rect to,double progress)
        {
            double t=progress*progress*(3-2*progress);
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
        Action begin;
        double duration;
        bool expanding,closing;
        bool primed;
        int finishFrames;
        TimeSpan? lastRenderingTime;

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
                    if(snapshot!=null)
                    {
                        var overlay=ShowOverlay(snapshot,DesktopInterop.Bounds(handle),fadeOut?1:.12,"ChatWindowFadeOverlay");
                        if(overlay!=null)
                        {
                            bool cloaked=!fadeOut&&DesktopInterop.SetWindowCloaked(handle,true);
                            bool primed=!fadeOut;
                            opacity=value=>
                            {
                                // Let WPF submit the opaque snapshot before hiding the
                                // real window. Switching the order can expose one blank frame.
                                if(!primed){primed=true;return;}
                                if(fadeOut&&!cloaked)cloaked=DesktopInterop.SetWindowCloaked(handle,true);
                                overlay.Opacity=value;
                            };
                            restore=()=>{if(cloaked&&(!fadeOut||window.IsVisible))DesktopInterop.SetWindowCloaked(handle,false);overlay.Close();};
                        }
                    }
                }
            }
            if(!fadeOut)opacity(.12);
            Start(fadeOut?135:115,t=>opacity(fadeOut?1-t:.12+.88*(1-(1-t)*(1-t))),restore);
        }

        internal void ShowExpanded(QuickChatExpansion source)
        {
            if(source==null||!SystemParameters.ClientAreaAnimation)
            {
                if(source!=null)source.HideSource();
                Show();return;
            }
            Stop();
            expanding=true;
            var handle=new WindowInteropHelper(window).EnsureHandle();
            bool alreadyVisible=window.IsVisible&&window.WindowState!=WindowState.Minimized;
            bool cloaked=!alreadyVisible&&DesktopInterop.SetWindowCloaked(handle,true);
            bool playing=false;
            try
            {
                Show();
                Rect target=DesktopInterop.Bounds(handle);
                var destination=window.Content as FrameworkElement;
                BitmapSource final=null;
                if(destination!=null)
                {
                    destination.UpdateLayout();
                    if(!LiquidThemeReveal.HasUncapturableSurface(destination))final=LiquidThemeReveal.Capture(destination);
                }
                Rect cover=Rect.Union(source.Bounds,target);
                var zoom=new ExpansionVisual(source.Image,final);
                var overlay=ShowOverlay(zoom,cover,1,"QuickChatExpansionOverlay");
                if(overlay==null){source.HideSource();return;}
                var dpi=VisualTreeHelper.GetDpi(overlay);
                Rect first=RelativeBounds(source.Bounds,cover,dpi),last=RelativeBounds(target,cover,dpi);
                zoom.Draw(first,0);
                Start(290,t=>{
                    zoom.Draw(QuickChatExpansion.Interpolate(first,last,t),t);
                    if(t<1)return;
                    if(cloaked){DesktopInterop.SetWindowCloaked(handle,false);cloaked=false;}
                    if(final==null)overlay.Opacity=0;
                },()=>{if(cloaked)DesktopInterop.SetWindowCloaked(handle,false);overlay.Close();},
                    source.HideSource,holdAtEnd:2);
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
        {return ShowOverlay(new Image{Source=bitmap,Stretch=Stretch.Fill,IsHitTestVisible=false},bounds,opacity,name);}

        static Window ShowOverlay(FrameworkElement content,Rect bounds,double opacity,string name)
        {
            var overlay=new Window{Name=name,AllowsTransparency=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
                ShowActivated=false,ShowInTaskbar=false,Topmost=true,Background=Brushes.Transparent,Focusable=false,Opacity=opacity,
                Content=content};
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

        static Rect RelativeBounds(Rect bounds,Rect cover,DpiScale dpi)
        {
            return new Rect((bounds.X-cover.X)/dpi.DpiScaleX,(bounds.Y-cover.Y)/dpi.DpiScaleY,
                bounds.Width/dpi.DpiScaleX,bounds.Height/dpi.DpiScaleY);
        }

        sealed class ExpansionVisual:Canvas
        {
            readonly Grid frame=new Grid{ClipToBounds=true};
            readonly Image from,to;
            readonly BlurEffect blur=new BlurEffect{RenderingBias=RenderingBias.Performance};

            internal ExpansionVisual(BitmapSource source,BitmapSource destination)
            {
                from=new Image{Source=source,Stretch=Stretch.UniformToFill,Effect=blur};
                to=new Image{Source=destination,Stretch=Stretch.UniformToFill,Opacity=0};
                frame.Children.Add(from);frame.Children.Add(to);
                Children.Add(frame);
            }

            internal void Draw(Rect bounds,double progress)
            {
                SetLeft(frame,bounds.X);SetTop(frame,bounds.Y);
                frame.Width=bounds.Width;frame.Height=bounds.Height;
                double mix=Math.Max(0,Math.Min(1,(progress-.22)/.6));
                blur.Radius=5*Math.Sin(Math.PI*mix);
                if(to.Source==null)return;
                to.Opacity=mix;
                // The first image stays underneath until the second is opaque.
                // Dimming both exposes the desktop through the middle of the blend.
            }
        }

        void Start(double milliseconds,Action<double> drawFrame,Action complete,Action afterFirstPaint=null,int holdAtEnd=0)
        {
            duration=milliseconds;draw=drawFrame;cleanup=complete;
            begin=afterFirstPaint;primed=false;finishFrames=holdAtEnd;lastRenderingTime=null;
            clock.Restart();
            CompositionTarget.Rendering+=Frame;
        }

        void Frame(object sender,EventArgs args)
        {
            var render=args as RenderingEventArgs;
            if(render!=null)
            {
                if(lastRenderingTime==render.RenderingTime)return;
                lastRenderingTime=render.RenderingTime;
            }
            if(begin!=null&&CanAnimate)
            {
                if(!primed){primed=true;return;}
                var start=begin;begin=null;
                start();clock.Restart();return;
            }
            double t=clock.Elapsed.TotalMilliseconds/duration;
            if(t<1&&CanAnimate){draw(t);return;}
            if(finishFrames>0&&CanAnimate){finishFrames--;draw(1);return;}
            if(!closing){Stop();return;}
            try{window.Close();}
            finally{Stop();}
        }

        void Stop()
        {
            if(draw==null)return;
            CompositionTarget.Rendering-=Frame;
            var complete=cleanup;
            draw=null;cleanup=null;begin=null;closing=false;
            clock.Stop();
            complete();
        }
    }
}
