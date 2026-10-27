using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    // A single frozen, contained static frame; loading never occurs during paint.
    internal sealed class CompanionPortraitSource
    {
        internal readonly BitmapSource Image;
        internal CompanionPortraitSource(BitmapSource image){Image=image;}
        internal static CompanionPortraitSource Load(SkinDefinition skin)
        {
            BitmapSource image=Try(delegate{return SkinCatalog.GetPreview(skin);});
            if(image==null&&skin.Illustrated&&skin.Manifest.Animations!=null){
                IllustratedAnimation idle;
                if(skin.Manifest.Animations.TryGetValue("idle",out idle)&&idle!=null)image=Try(delegate{
                    int width=skin.Manifest.CellWidth,height=skin.Manifest.CellHeight;
                    var atlas=IllustratedPack.ReadImage(IllustratedPack.AssetPath(skin.Source,idle.File),width*idle.Columns,height*(int)Math.Ceiling((double)idle.Frames/idle.Columns));
                    var frame=new CroppedBitmap(atlas,new Int32Rect(0,0,width,height));frame.Freeze();return frame;
                });
            }
            if(image==null)image=Try(delegate{return SkinCatalog.GetAvatar(skin);});
            return new CompanionPortraitSource(image);
        }
        internal static BitmapSource Avatar(SkinDefinition skin)
        {try{return SkinCatalog.GetAvatar(skin);}catch(Exception error){if(!Expected(error))throw;return null;}}
        static BitmapSource Try(Func<BitmapSource> candidate)
        {try{var image=candidate();return Trim(image);}catch(Exception error){if(!Expected(error))throw;return null;}}
        static bool Expected(Exception e){return e is FileFormatException||e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException||e is NotSupportedException||e is System.Security.SecurityException||e is System.Runtime.InteropServices.COMException;}
        internal static BitmapSource Trim(BitmapSource source)
        {
            if(source==null)return null;int width=source.PixelWidth,height=source.PixelHeight;
            var normalized=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var bytes=new byte[checked(width*height*4)];normalized.CopyPixels(bytes,width*4,0);
            int left=width,top=height,right=-1,bottom=-1;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(bytes[(y*width+x)*4+3]!=0){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            if(right<left)return null;int margin=Math.Max(2,(int)Math.Ceiling(Math.Max(right-left+1,bottom-top+1)*.025));
            left=Math.Max(0,left-margin);top=Math.Max(0,top-margin);right=Math.Min(width-1,right+margin);bottom=Math.Min(height-1,bottom+margin);
            var cropped=new CroppedBitmap(source,new Int32Rect(left,top,right-left+1,bottom-top+1));cropped.Freeze();return cropped;
        }
    }
    internal sealed class CompanionPortraitVisual:Border
    {
        internal CompanionPortraitVisual(CompanionPortraitSource source,bool dark)
        {
            Focusable=false;Padding=new Thickness(12);Background=ThemeManager.Brush(dark?"#384B65":"#DCE9F7");CornerRadius=new CornerRadius(12);
            if(source.Image!=null)Child=new Image{Source=source.Image,Stretch=Stretch.Uniform};
            else Child=new Viewbox{Width=64,Height=80,Child=new System.Windows.Shapes.Path{Width=64,Height=80,Stretch=Stretch.Uniform,Fill=ThemeManager.Brush(dark?"#B0C2DA":"#809AB9"),Data=Geometry.Parse("M32,4 A14,14 0 1 1 31.99,4 M8,76 L8,57 C8,34 56,34 56,57 L56,76 Z")}};
        }
    }
}
