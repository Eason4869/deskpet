using Avalonia;
using Avalonia.Media.Imaging;
using Dafeiyu;
using System.Runtime.InteropServices;
namespace DeskPet.Desktop;
internal sealed class HolidayKeepsakeSceneValidator:IHolidayKeepsakeSceneValidator
{
    public void Validate(byte[] png,int expectedWidth,int expectedHeight)
    {
        HolidayKeepsakePng.ValidateStructure(png,expectedWidth,expectedHeight);
        using var stream=new MemoryStream(png,false);
        using var bitmap=new Bitmap(stream);
        if(bitmap.PixelSize.Width!=expectedWidth||bitmap.PixelSize.Height!=expectedHeight)throw new InvalidDataException("Decoded scene geometry mismatch.");
        // Explicit full original pixel access; never DecodeToWidth or a page cache.
        int stride=checked(expectedWidth*8),size=checked(stride*expectedHeight);
        IntPtr pixels=Marshal.AllocHGlobal(size);
        try{bitmap.CopyPixels(new PixelRect(0,0,expectedWidth,expectedHeight),pixels,size,stride);}
        finally{Marshal.FreeHGlobal(pixels);}
    }
}
