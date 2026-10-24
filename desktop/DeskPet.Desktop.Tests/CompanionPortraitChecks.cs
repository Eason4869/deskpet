using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DeskPet.Desktop;
using System.Runtime.InteropServices;

internal static class CompanionPortraitChecks
{
    internal static int Run(string directory,string language="en_US",bool captureFallback=false)
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        Directory.CreateDirectory(directory);
        using(var sparse=new WriteableBitmap(new PixelSize(40,60),new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul)){
            using(var buffer=sparse.Lock()){var pixels=new byte[buffer.RowBytes*60];pixels[10*buffer.RowBytes+12*4+3]=255;pixels[49*buffer.RowBytes+27*4+3]=255;System.Runtime.InteropServices.Marshal.Copy(pixels,0,buffer.Address,pixels.Length);}
            using var trimmed=CompanionPortraitSource.Copy(sparse,new PixelRect(0,0,40,60));Check(trimmed!=null&&trimmed.PixelSize.Width==20&&trimmed.PixelSize.Height==44,"transparent exterior is removed with safe margin around every opaque pixel");
            using var blank=new WriteableBitmap(new PixelSize(20,20),new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);Check(CompanionPortraitSource.Copy(blank,new PixelRect(0,0,20,20))==null,"fully transparent artwork enters fallback");
            string sourceRoot=Path.Combine(directory,"portrait-source");Directory.CreateDirectory(sourceRoot);sparse.Save(Path.Combine(sourceRoot,"own.png"),new PngBitmapEncoderOptions());File.WriteAllText(Path.Combine(sourceRoot,"bad.png"),"broken");
            var damaged=new PetSkin{Id="portrait-fixture",Name="Imported portrait",Prompt="Fixture",Folder=sourceRoot,Preview="bad.png",Avatar="own.png",Atlas="bad.png"};using var portraitAssets=new SpriteAssets();using var own=CompanionPortraitSource.Load(damaged,portraitAssets);Check(own.Image!=null&&own.Image.PixelSize==trimmed!.PixelSize,"damaged preview and idle use the imported companion own avatar");
            for(int i=0;i<7;i++){sparse.Save(Path.Combine(sourceRoot,"other"+i+".png"),new PngBitmapEncoderOptions());portraitAssets.Get(damaged,"other"+i+".png");}using(var ownedCopy=new MemoryStream()){own.Image!.Save(ownedCopy,new PngBitmapEncoderOptions());Check(ownedCopy.Length>40,"static portrait survives borrowed cache eviction");}
            damaged.Avatar="bad.png";using var neutral=CompanionPortraitSource.Load(damaged,portraitAssets);Check(neutral.Image==null,"unreadable preview idle and avatar produce neutral fallback without borrowing an official character");if(captureFallback)foreach(string fallbackTheme in new[]{"light","dark"}){var fallbackPanel=new StackPanel{Width=640,Height=300,Orientation=Avalonia.Layout.Orientation.Horizontal,Children={new CompanionPortraitVisual(own,fallbackTheme=="dark"){Width=300,Height=280},new CompanionPortraitVisual(neutral,fallbackTheme=="dark"){Width=300,Height=280}}};fallbackPanel.Measure(new Size(640,300));fallbackPanel.Arrange(new Rect(0,0,640,300));using var fallbackImage=new RenderTargetBitmap(new PixelSize(640,300),new Vector(96,96));fallbackImage.Render(fallbackPanel);string fallbackPath=Path.GetFullPath("work/relationship-validation/f3/avalonia/"+language+"-imported-fallback-"+fallbackTheme+".png");Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);fallbackImage.Save(fallbackPath,new PngBitmapEncoderOptions());}
        }

        // Warm the real normalization path before measuring only the requested copy.
        using(var warm=new WriteableBitmap(new PixelSize(8,8),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul))
        using(var ignored=CompanionPortraitSource.Copy(warm,new PixelRect(0,0,8,8))){}
        using(var atlas=new WriteableBitmap(new PixelSize(1024,1024),new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Unpremul)){
            Check(atlas.Format==PixelFormat.Rgba8888,"multi-cell fixture uses non-BGRA source pixels");
            var frame=new PixelRect(37,53,10,8);
            using(var buffer=atlas.Lock()){
                var pixels=new byte[buffer.RowBytes*1024];
                void Pixel(int x,int y,byte r,byte g,byte b,byte a){int i=y*buffer.RowBytes+x*4;pixels[i]=r;pixels[i+1]=g;pixels[i+2]=b;pixels[i+3]=a;}
                Pixel(0,0,0,255,0,255); // An opaque neighboring cell must never enter the copy.
                Pixel(39,54,200,100,50,128);Pixel(42,59,30,150,220,255);
                Marshal.Copy(pixels,0,buffer.Address,pixels.Length);
            }
            long before=GC.GetAllocatedBytesForCurrentThread();
            using var image=CompanionPortraitSource.Copy(atlas,frame);
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Console.WriteLine("FRAME_COPY_MANAGED_BYTES "+allocated+" source=1024x1024 frame=10x8");
            Check(allocated<512*1024,"small portrait frame never allocates a source-sized managed atlas buffer");
            Check(image!=null&&image.PixelSize==new PixelSize(8,8),"nonzero-origin frame trims using only frame-local alpha bounds");
            using var pixelsOut=new WriteableBitmap(image!.PixelSize,new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
            using(var buffer=pixelsOut.Lock()){
                image.CopyPixels(buffer);var pixels=new byte[buffer.RowBytes*8];Marshal.Copy(buffer.Address,pixels,0,pixels.Length);
                int translucent=buffer.RowBytes+2*4,opaque=6*buffer.RowBytes+5*4;
                Check(Math.Abs(pixels[translucent]-25)<=1&&Math.Abs(pixels[translucent+1]-50)<=1&&Math.Abs(pixels[translucent+2]-100)<=1&&pixels[translucent+3]==128,"RGBA subrectangle is normalized to premultiplied BGRA without changing its colors");
                Check(pixels[opaque]==220&&pixels[opaque+1]==150&&pixels[opaque+2]==30&&pixels[opaque+3]==255,"frame copy retains the distant opaque pixel with correct source offset");
            }
            atlas.Dispose();using var encoded=new MemoryStream();image.Save(encoded,new PngBitmapEncoderOptions());Check(encoded.Length>40,"copied subrectangle remains independently owned after source disposal");
        }
        return checks;
    }
}
