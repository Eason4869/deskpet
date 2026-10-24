using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DeskPet.Desktop;

// Own the static copy. SpriteAssets may evict and dispose any borrowed bitmap.
internal sealed class CompanionPortraitSource:IDisposable
{
    internal Bitmap? Image {get;private set;}
    internal CompanionPortraitSource(Bitmap? image){Image=image;}
    internal static CompanionPortraitSource Load(PetSkin skin,SpriteAssets assets)
    {
        Bitmap? Try(Func<Bitmap?> candidate,Func<PixelRect?>? frame=null)
        {
            try{var image=candidate();return image==null?null:Copy(image,frame?.Invoke()??new PixelRect(image.PixelSize));}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException or KeyNotFoundException){return null;}
        }
        var image=skin.Preview==null?null:Try(()=>assets.Get(skin,skin.Preview));
        if(image==null){ClipSpec? clip=null;image=Try(()=>{clip=assets.Resolve(skin,"idle",false);return assets.Get(skin,clip);},()=>SpriteAssets.Rect(clip!,0));}
        image??=Try(()=>assets.GetAvatar(skin));
        return new(image);
    }
    internal static Bitmap? Avatar(PetSkin skin,SpriteAssets assets)
    {
        try{var image=assets.GetAvatar(skin);return image==null?null:Copy(image,new PixelRect(image.PixelSize));}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException){return null;}
    }
    internal static Bitmap? Copy(Bitmap source,PixelRect frame)
    {
        if(frame.X<0||frame.Y<0||frame.Right>source.PixelSize.Width||frame.Bottom>source.PixelSize.Height||frame.Width<=0||frame.Height<=0)throw new InvalidDataException("Invalid static portrait frame");
        using var normalized=new WriteableBitmap(source.PixelSize,new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
        int left=frame.Right,top=frame.Bottom,right=-1,bottom=-1;byte[] bytes;
        using(var buffer=normalized.Lock()){
            source.CopyPixels(buffer);bytes=new byte[buffer.RowBytes*source.PixelSize.Height];Marshal.Copy(buffer.Address,bytes,0,bytes.Length);
            for(int y=frame.Y;y<frame.Bottom;y++)for(int x=frame.X;x<frame.Right;x++)if(bytes[y*buffer.RowBytes+x*4+3]!=0){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            if(right<left)return null;int margin=Math.Max(2,(int)Math.Ceiling(Math.Max(right-left+1,bottom-top+1)*.025));
            left=Math.Max(frame.X,left-margin);top=Math.Max(frame.Y,top-margin);right=Math.Min(frame.Right-1,right+margin);bottom=Math.Min(frame.Bottom-1,bottom+margin);
            var copy=new WriteableBitmap(new PixelSize(right-left+1,bottom-top+1),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
            using(var target=copy.Lock())for(int y=0;y<copy.PixelSize.Height;y++)Marshal.Copy(bytes,(top+y)*buffer.RowBytes+left*4,IntPtr.Add(target.Address,y*target.RowBytes),copy.PixelSize.Width*4);
            return copy;
        }
    }
    public void Dispose(){Image?.Dispose();Image=null;}
}
internal sealed class CompanionPortraitVisual:Border
{
    internal PetSkin? Skin {get;set;}
    internal CompanionPortraitVisual(CompanionPortraitSource source,bool dark)
    {
        Focusable=false;Padding=new Thickness(12);Background=new SolidColorBrush(Color.Parse(dark?"#384B65":"#DCE9F7"));CornerRadius=new CornerRadius(12);
        if(source.Image!=null)Child=new Image{Source=source.Image,Stretch=Stretch.Uniform};
        else Child=new Viewbox{Width=64,Height=80,Child=new Avalonia.Controls.Shapes.Path{Width=64,Height=80,Stretch=Stretch.Uniform,Fill=new SolidColorBrush(Color.Parse(dark?"#B0C2DA":"#809AB9")),Data=Geometry.Parse("M32,4 A14,14 0 1 1 31.99,4 M8,76 L8,57 C8,34 56,34 56,57 L56,76 Z")}};
    }
}
