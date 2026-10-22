using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    // Artwork can decorate a standalone history or the stable conversation surface.
    public sealed class ChatAtmosphere : Grid
    {
        readonly Image artwork;
        SkinDefinition currentSkin;
        double artworkOpacity;
        double previewWidth,previewHeight;

        public ChatAtmosphere(ScrollViewer history):this()
        {
            if(history==null)throw new ArgumentNullException("history");
            if(history.Parent!=null)throw new InvalidOperationException("Detach the history before wrapping it, or use ChatAtmosphere.Attach.");
            history.Background=Brushes.Transparent;Children.Add(history);
        }

        ChatAtmosphere()
        {
            Name="ChatAtmosphere";ClipToBounds=true;
            var fade=new LinearGradientBrush{StartPoint=new Point(.08,0),EndPoint=new Point(.88,.9)};
            fade.GradientStops.Add(new GradientStop(Colors.Transparent,0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(64,255,255,255),.32));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(184,255,255,255),.65));
            fade.GradientStops.Add(new GradientStop(Colors.White,1));fade.Freeze();
            artwork=new Image{Name="ChatAtmosphereArtwork",Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,20,2),IsHitTestVisible=false,Focusable=false,OpacityMask=fade,Visibility=Visibility.Collapsed};
            RenderOptions.SetBitmapScalingMode(artwork,BitmapScalingMode.HighQuality);
            Children.Add(artwork);
            SizeChanged+=delegate{RefreshArtwork();};
        }

        public static ChatAtmosphere AttachSurface(Grid surface,int row,int rowSpan)
        {
            if(surface==null)throw new ArgumentNullException("surface");
            var result=new ChatAtmosphere{IsHitTestVisible=false,Margin=new Thickness(0,0,0,20)};
            // Span history and composer together: approvals and queued drafts resize
            // only their foreground rows, never the background's bottom anchor.
            SetRow(result,row);SetRowSpan(result,rowSpan);Panel.SetZIndex(result,-1);surface.Children.Insert(0,result);return result;
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
            try
            {
                var preview=skin==null?null:SkinCatalog.GetPreview(skin);
                previewWidth=preview==null?0:preview.PixelWidth;previewHeight=preview==null?0:preview.PixelHeight;
                artwork.Source=preview==null?null:CropTransparentPadding(preview);currentSkin=skin;
            }
            catch(Exception error)
            {
                if(!(error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException||error is ArgumentException||error is NotSupportedException))throw;
                // An unavailable optional preview must not prevent reading the conversation.
                artwork.Source=null;currentSkin=null;
            }
            RefreshArtwork();
        }

        static BitmapSource CropTransparentPadding(BitmapSource preview)
        {
            var pixelsSource=new FormatConvertedBitmap(preview,PixelFormats.Bgra32,null,0);
            int width=pixelsSource.PixelWidth,height=pixelsSource.PixelHeight,stride=width*4;
            var pixels=new byte[stride*height];pixelsSource.CopyPixels(pixels,stride,0);
            int left=width,top=height,right=-1,bottom=-1;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                if(pixels[y*stride+x*4+3]!=0){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            if(right<0)return null;
            var crop=new CroppedBitmap(preview,new Int32Rect(left,top,right-left+1,bottom-top+1));crop.Freeze();return crop;
        }

        void RefreshArtwork()
        {
            double width=ActualWidth,height=ActualHeight;
            bool show=artwork.Source!=null&&artworkOpacity>0&&width>=440&&height>=160;
            artwork.Visibility=show?Visibility.Visible:Visibility.Collapsed;
            if(!show)return;
            // Keep the established sprite scale, but size its box to the visible pixels.
            // Aligning the full preview canvas leaves its transparent right/bottom
            // padding between the character and the container's bottom-right corner.
            var bitmap=(BitmapSource)artwork.Source;
            double scale=Math.Min(Math.Min(450,width*.56)/previewWidth,Math.Min(648,height*.92)/previewHeight);
            artwork.Width=bitmap.PixelWidth*scale;artwork.Height=bitmap.PixelHeight*scale;
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
