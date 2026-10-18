using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    // Wrap only the history ScrollViewer. The composer remains a separate sibling.
    public sealed class ChatAtmosphere : Grid
    {
        readonly Image artwork;
        SkinDefinition currentSkin;
        double artworkOpacity;

        public ChatAtmosphere(ScrollViewer history)
        {
            if(history==null)throw new ArgumentNullException("history");
            if(history.Parent!=null)throw new InvalidOperationException("Detach the history before wrapping it, or use ChatAtmosphere.Attach.");
            Name="ChatAtmosphere";ClipToBounds=true;
            var fade=new LinearGradientBrush{StartPoint=new Point(.08,0),EndPoint=new Point(.88,.9)};
            fade.GradientStops.Add(new GradientStop(Colors.Transparent,0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(64,255,255,255),.32));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(184,255,255,255),.65));
            fade.GradientStops.Add(new GradientStop(Colors.White,1));fade.Freeze();
            artwork=new Image{Name="ChatAtmosphereArtwork",Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,20,2),IsHitTestVisible=false,Focusable=false,OpacityMask=fade,Visibility=Visibility.Collapsed};
            RenderOptions.SetBitmapScalingMode(artwork,BitmapScalingMode.HighQuality);
            Children.Add(artwork);history.Background=Brushes.Transparent;Children.Add(history);
            SizeChanged+=delegate{RefreshArtwork();};
        }

        // The reusable layer starts disabled; ChatWindow applies the chosen 20% level.
        public double ArtworkOpacity
        {
            get{return artworkOpacity;}
            set
            {
                if(Double.IsNaN(value)||Double.IsInfinity(value)||value<0||value>1)throw new ArgumentOutOfRangeException("value");
                artworkOpacity=value;RefreshArtwork();
            }
        }

        public void SetSkin(SkinDefinition skin)
        {
            if(ReferenceEquals(currentSkin,skin))return;
            try{artwork.Source=skin==null?null:SkinCatalog.GetPreview(skin);currentSkin=skin;}
            catch(Exception error)
            {
                if(!(error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException||error is ArgumentException||error is NotSupportedException))throw;
                // An unavailable optional preview must not prevent reading the conversation.
                artwork.Source=null;currentSkin=null;
            }
            RefreshArtwork();
        }

        void RefreshArtwork()
        {
            double width=ActualWidth,height=ActualHeight;
            bool show=artwork.Source!=null&&artworkOpacity>0&&width>=440&&height>=160;
            artwork.Visibility=show?Visibility.Visible:Visibility.Collapsed;
            if(!show)return;
            artwork.Width=Math.Min(450,width*.56);artwork.Height=Math.Min(648,height*.92);
            double compact=Math.Min(1,.30+.70*Math.Max(0,width-440)/180);
            artwork.Opacity=artworkOpacity*compact;
        }

        public static ChatAtmosphere Attach(ScrollViewer history)
        {
            if(history==null)throw new ArgumentNullException("history");
            var existing=history.Parent as ChatAtmosphere;if(existing!=null)return existing;
            var parent=history.Parent as Grid;
            if(parent==null)throw new InvalidOperationException("History must belong to a Grid before attaching its atmosphere.");
            int index=parent.Children.IndexOf(history),row=GetRow(history),column=GetColumn(history),rowSpan=GetRowSpan(history),columnSpan=GetColumnSpan(history),z=Panel.GetZIndex(history);
            parent.Children.Remove(history);var result=new ChatAtmosphere(history);
            SetRow(result,row);SetColumn(result,column);SetRowSpan(result,rowSpan);SetColumnSpan(result,columnSpan);Panel.SetZIndex(result,z);
            // Grid positions now belong to the wrapper; its internal layers share one cell.
            SetRow(history,0);SetColumn(history,0);SetRowSpan(history,1);SetColumnSpan(history,1);
            parent.Children.Insert(index,result);return result;
        }
    }
}
