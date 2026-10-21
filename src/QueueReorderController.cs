using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu
{
    // A visual preview only: the shared queue changes once, after a valid drop.
    // Refresh cancels this preview when asynchronous queue changes replace rows.
    internal sealed class QueueReorderController
    {
        readonly StackPanel rows;readonly ScrollViewer scroll;readonly Func<string,string,bool> commit;readonly Action<Exception> error;
        readonly DispatcherTimer scrollTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
        List<FrameworkElement> snapshot;FrameworkElement source;Button handle;DragAdorner ghost;AdornerLayer layer;
        Point origin,last;double top,step;int from,target,revision;bool moving,dropping;
        public bool IsDragging{get{return moving;}}
        public QueueReorderController(StackPanel rows,ScrollViewer scroll,Func<string,string,bool> commit,Action<Exception> error)
        {
            this.rows=rows;this.scroll=scroll;this.commit=commit;this.error=error;
            scrollTimer.Tick+=delegate
            {
                if(!moving || dropping)return;double visible=last.Y-scroll.VerticalOffset,delta=visible<20?-10:visible>scroll.ViewportHeight-20?10:0;
                if(delta==0)return;double before=scroll.VerticalOffset;scroll.ScrollToVerticalOffset(before+delta);scroll.UpdateLayout();
                Move(new Point(last.X,last.Y+scroll.VerticalOffset-before));
            };
        }
        public void Attach(Button grip,FrameworkElement row)
        {
            grip.PreviewMouseLeftButtonDown+=(s,e)=>{if(e.ClickCount!=1)return;Begin(grip,row,e.GetPosition(rows));e.Handled=true;};
            grip.PreviewMouseMove+=(s,e)=>{if(handle==grip && e.LeftButton==MouseButtonState.Pressed){Move(e.GetPosition(rows));e.Handled=true;}};
            grip.PreviewMouseLeftButtonUp+=(s,e)=>{if(handle==grip){Drop();e.Handled=true;}};
            grip.LostMouseCapture+=delegate{if(handle==grip && !dropping)Cancel();};
            grip.PreviewKeyDown+=(s,e)=>
            {
                if(e.Key==Key.Escape){Cancel();e.Handled=true;return;}
                if((Keyboard.Modifiers&ModifierKeys.Alt)==0 || (e.Key!=Key.Up && e.Key!=Key.Down))return;
                MoveByKeyboard(row,e.Key==Key.Up?-1:1);e.Handled=true;
            };
        }
        internal void MoveByKeyboard(FrameworkElement row,int direction)
        {
            Cancel();var list=rows.Children.OfType<FrameworkElement>().ToList();int index=list.IndexOf(row),next=index+direction;
            if(index<0 || next<0 || next>=list.Count)return;string id=(string)row.Tag;
            try
            {
                if(!commit(id,next<index?(string)list[next].Tag:next+1<list.Count?(string)list[next+1].Tag:null))return;
                var replacement=rows.Children.OfType<FrameworkElement>().FirstOrDefault(r=>(string)r.Tag==id);var grip=FindGrip(replacement);if(grip!=null)grip.Focus();
            }
            catch(Exception ex){error(ex);}
        }
        static Button FindGrip(DependencyObject node)
        {
            if(node==null)return null;var button=node as Button;if(button!=null && button.Name=="ReorderQueuedMessage")return button;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=FindGrip(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;
        }
        internal void Begin(Button grip,FrameworkElement row,Point point)
        {
            Cancel();snapshot=rows.Children.OfType<FrameworkElement>().ToList();if(snapshot.Count<2 || !snapshot.Contains(row))return;
            handle=grip;source=row;origin=last=point;from=target=snapshot.IndexOf(row);top=row.TranslatePoint(new Point(),rows).Y;step=row.ActualHeight+row.Margin.Top+row.Margin.Bottom;
            grip.Focus();if(!grip.CaptureMouse()){Cancel();return;}
        }
        internal void Move(Point point)
        {
            if(source==null || dropping)return;last=point;
            if(!moving)
            {
                if(Math.Abs(point.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance && Math.Abs(point.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance)return;
                layer=AdornerLayer.GetAdornerLayer(rows);if(layer==null){Cancel();return;}
                var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(source.ActualWidth)),Math.Max(1,(int)Math.Ceiling(source.ActualHeight)),96,96,PixelFormats.Pbgra32);
                var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(source){Stretch=Stretch.Fill},null,new Rect(0,0,source.ActualWidth,source.ActualHeight));bitmap.Render(drawing);bitmap.Freeze();
                ghost=new DragAdorner(rows,bitmap,source.ActualWidth,source.ActualHeight);layer.Add(ghost);source.Opacity=0;moving=true;scrollTimer.Start();
            }
            double center=top+(point.Y-origin.Y)+source.ActualHeight*.5;
            int next=Math.Max(0,Math.Min(snapshot.Count-1,(int)Math.Floor((center-snapshot[0].Margin.Top)/step)));
            if(next!=target)
            {
                target=next;
                for(int i=0;i<snapshot.Count;i++)
                {
                    if(i==from)continue;double shift=i>from && i<=target?-step:i<from && i>=target?step:0;
                    var transform=snapshot[i].RenderTransform as TranslateTransform;if(transform==null){transform=new TranslateTransform();snapshot[i].RenderTransform=transform;}
                    transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(shift,TimeSpan.FromMilliseconds(140)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
                }
            }
            ghost.X=source.Margin.Left+point.X-origin.X;ghost.Y=top+point.Y-origin.Y;ghost.Slot=top+(target-from)*step;ghost.InvalidateVisual();
        }
        internal void Drop()
        {
            if(!moving){Cancel();return;}if(dropping)return;
            if(last.X<0 || last.X>rows.ActualWidth || last.Y<scroll.VerticalOffset || last.Y>scroll.VerticalOffset+scroll.ViewportHeight){Cancel();return;}
            dropping=true;scrollTimer.Stop();if(handle.IsMouseCaptured)handle.ReleaseMouseCapture();
            int ticket=revision;string id=(string)source.Tag;var order=snapshot.Where(r=>r!=source).ToList();string before=target<order.Count?(string)order[target].Tag:null;
            double end=top+(target-from)*step;
            var animation=new DoubleAnimation(end,TimeSpan.FromMilliseconds(140)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            animation.Completed+=delegate{if(ticket!=revision)return;Cancel();try{commit(id,before);}catch(Exception ex){error(ex);}};
            ghost.BeginAnimation(DragAdorner.XProperty,new DoubleAnimation(source.Margin.Left,TimeSpan.FromMilliseconds(140)));
            ghost.BeginAnimation(DragAdorner.YProperty,animation);
        }
        public void Cancel()
        {
            revision++;scrollTimer.Stop();dropping=true;
            if(handle!=null && handle.IsMouseCaptured)handle.ReleaseMouseCapture();
            if(ghost!=null && layer!=null)layer.Remove(ghost);
            if(snapshot!=null)foreach(var row in snapshot){row.Opacity=1;row.RenderTransform=Transform.Identity;}
            handle=null;source=null;snapshot=null;ghost=null;layer=null;moving=false;dropping=false;
        }
        sealed class DragAdorner:Adorner
        {
            readonly BitmapSource bitmap;readonly double width,height;
            public static readonly DependencyProperty XProperty=DependencyProperty.Register("X",typeof(double),typeof(DragAdorner),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
            public static readonly DependencyProperty YProperty=DependencyProperty.Register("Y",typeof(double),typeof(DragAdorner),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
            public double X{get{return (double)GetValue(XProperty);}set{SetValue(XProperty,value);}}
            public double Y{get{return (double)GetValue(YProperty);}set{SetValue(YProperty,value);}}
            public double Slot;
            public DragAdorner(UIElement owner,BitmapSource bitmap,double width,double height):base(owner){this.bitmap=bitmap;this.width=width;this.height=height;IsHitTestVisible=false;}
            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawRoundedRectangle(Ui.Brush("#EDF3FE"),new Pen(Ui.Brush("#BCCFEB"),1),new Rect(7,Slot,Math.Max(1,width+2),height),6,6);
                dc.PushOpacity(.66);dc.DrawRoundedRectangle(ThemeManager.Surface,new Pen(Ui.Brush("#D4DEEB"),1),new Rect(X-2,Y-2,width+4,height+4),7,7);dc.DrawImage(bitmap,new Rect(X,Y,width,height));dc.Pop();
            }
        }
    }
}
