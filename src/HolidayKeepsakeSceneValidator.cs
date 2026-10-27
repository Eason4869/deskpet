using System;
using System.IO;
using System.Windows.Media.Imaging;
namespace Dafeiyu
{
    internal sealed class HolidayKeepsakeSceneValidator:IHolidayKeepsakeSceneValidator
    {
        public void Validate(byte[] png,int expectedWidth,int expectedHeight)
        {
            HolidayKeepsakePng.ValidateStructure(png,expectedWidth,expectedHeight);
            try{
                using(var stream=new MemoryStream(png,false)){
                    var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                    if(decoder.Frames.Count!=1)throw new InvalidDataException("Scene must have one frame.");
                    var frame=decoder.Frames[0];if(frame.PixelWidth!=expectedWidth||frame.PixelHeight!=expectedHeight)throw new InvalidDataException("Decoded scene geometry mismatch.");
                    int stride=(frame.PixelWidth*frame.Format.BitsPerPixel+7)/8;
                    frame.CopyPixels(new byte[checked(stride*frame.PixelHeight)],stride,0);
                }
            }catch(System.Runtime.InteropServices.COMException error){throw new InvalidDataException("Scene pixel decoding failed.",error);}
        }
    }
}
