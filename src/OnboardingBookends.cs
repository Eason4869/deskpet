using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace Dafeiyu {
 public sealed partial class OnboardingWindow {
  Canvas celebration;bool celebrationPlayed;
  Canvas CreateCelebration(){
   celebration=new Canvas{Name="GuideCelebration",Width=96,Height=96,Margin=new Thickness(0,28,0,24),HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false,Focusable=false,Visibility=Visibility.Collapsed};
   celebration.Children.Add(new Ellipse{Width=84,Height=84,Fill=Ui.Brush("#F3F6FE"),Margin=new Thickness(6)});
   celebration.Children.Add(new Polygon{Points=new PointCollection(new[]{new Point(25,76),new Point(39,39),new Point(65,62)}),Fill=Ui.Brush("#EDB447")});
   celebration.Children.Add(new Path{Data=Geometry.Parse("M30 63L54 69"),Stroke=Ui.Brush("#E58C79"),StrokeThickness=6});
   celebration.Children.Add(new Path{Data=Geometry.Parse("M34 33C30 20 57 19 58 35"),Stroke=Ui.Blue,StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
   celebration.Children.Add(new Path{Data=Geometry.Parse("M51 39C56 25 76 31 76 43"),Stroke=Ui.Brush("#68B8A4"),StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
   return celebration;
  }
  void StyleBookend(){
   bool complete=Current.Action=="complete";directory.Visibility=Bookend?Visibility.Collapsed:Visibility.Visible;locate.Visibility=Bookend?Visibility.Collapsed:Visibility.Visible;exitActions.Visibility=Bookend?Visibility.Collapsed:Visibility.Visible;explore.Visibility=Current.Action=="intro"?Visibility.Visible:Visibility.Collapsed;
   LocalizedUi.Set(guideHeading,"Text",()=>(Bookend?TextResources.Get("onboardingBookends.styleBookend.petSkinName.alternativeA"):TextResources.Format("onboardingBookends.styleBookend.petSkinName",pet.Skin.DisplayName)));guideHeading.FontSize=Bookend?12:18;guideHeading.Foreground=Bookend?Ui.Muted:Ui.Ink;guideHeading.FontWeight=Bookend?FontWeights.Normal:FontWeights.SemiBold;
   title.FontSize=complete?26:21;title.FontWeight=complete?FontWeights.Bold:FontWeights.SemiBold;title.TextAlignment=complete?TextAlignment.Center:TextAlignment.Left;title.Margin=new Thickness(0,0,0,complete?22:10);
   instruction.FontSize=complete?15:13;instruction.LineHeight=complete?27:23;instruction.TextAlignment=complete?TextAlignment.Center:TextAlignment.Left;instruction.Foreground=complete?Ui.Ink:Ui.Muted;
   bodyScroll.VerticalContentAlignment=complete?VerticalAlignment.Center:VerticalAlignment.Top;celebration.Visibility=complete?Visibility.Visible:Visibility.Collapsed;status.Visibility=Bookend?Visibility.Collapsed:Visibility.Visible;
   if(Bookend){focusScope=new GuideFocusScope((UIElement)settings.Content);petScope=new GuideFocusScope((UIElement)pet.Content);}
   if(complete){var hint=LocalizedUi.Text(()=>(TextResources.Get("onboardingBookends.styleBookend.text")),12,Ui.Muted);hint.TextAlignment=TextAlignment.Center;hint.Margin=new Thickness(0,16,0,12);actions.Children.Add(hint);Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(PlayCelebration));}
  }
  void PlayCelebration(){
   if(finished||Current.Action!="complete"||celebrationPlayed)return;celebrationPlayed=true;if(!SystemParameters.ClientAreaAnimation)return;
   string[] colors={"#456DE6","#68B8A4","#EDB447","#EB8A9D"};
   for(int i=0;i<12;i++){
    double angle=Math.PI*(1.05+i/12.0*1.05);var motion=new TranslateTransform();var paper=new Rectangle{Width=i%2==0?4:6,Height=i%2==0?4:6,Fill=Ui.Brush(colors[i%4]),Opacity=0,RenderTransform=motion,IsHitTestVisible=false};Canvas.SetLeft(paper,46);Canvas.SetTop(paper,46);celebration.Children.Add(paper);
    var delay=TimeSpan.FromMilliseconds(i%4*35);var duration=TimeSpan.FromMilliseconds(850);var easing=new QuadraticEase{EasingMode=EasingMode.EaseOut};
    motion.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,Math.Cos(angle)*70,duration){BeginTime=delay,EasingFunction=easing});motion.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,Math.Sin(angle)*70+20,duration){BeginTime=delay,EasingFunction=easing});
    var opacity=new DoubleAnimationUsingKeyFrames{BeginTime=delay,FillBehavior=FillBehavior.Stop};opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));opacity.KeyFrames.Add(new LinearDoubleKeyFrame(.7,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(580))));opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(duration)));paper.BeginAnimation(UIElement.OpacityProperty,opacity);
   }
  }
 }
}
