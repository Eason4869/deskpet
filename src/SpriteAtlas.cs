using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    public sealed class SpriteAtlas
    {
        private readonly BitmapSource[][] frames = new BitmapSource[11][];
        private readonly byte[][][] alpha = new byte[11][][];
        public int CellWidth { get; private set; }
        public int CellHeight { get; private set; }
        public bool HasGaze { get; private set; }
        public int FrameMultiplier { get; private set; }
        public SpriteAtlas() : this("Dafeiyu.Atlas", true) { }
        public SpriteAtlas(string source, bool embedded)
        {
            if (!embedded) SkinCatalog.ValidatePng(source);
            BitmapSource image;
            using (Stream stream = embedded ? Assembly.GetExecutingAssembly().GetManifestResourceStream(source) : File.OpenRead(source))
            {
                if (stream == null) throw new InvalidOperationException(TextResources.Get("spriteAtlas.spriteAtlas.text"));
                image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            }
            ValidateDimensions(image.PixelWidth, image.PixelHeight);
            FrameMultiplier=1;
            CellWidth = image.PixelWidth / (8*FrameMultiplier); CellHeight = image.PixelHeight / 11; HasGaze = true;
            image.Freeze();
            for (int row = 0; row < 11; row++)
            {
                int count = (row < 9 ? AnimationPlayer.Counts[row] : 8)*FrameMultiplier;
                frames[row] = new BitmapSource[count]; alpha[row] = new byte[count][];
                for (int col = 0; col < count; col++)
                {
                    var cropped = new CroppedBitmap(image, new Int32Rect(col * CellWidth, row * CellHeight, CellWidth, CellHeight));
                    cropped.Freeze();
                    var bgra = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0); bgra.Freeze();
                    byte[] pixels = new byte[CellWidth * CellHeight * 4]; bgra.CopyPixels(pixels, CellWidth * 4, 0);
                    // Detach each small frame so WPF does not retain/upload the
                    // full HD atlas for every animated Image.Source.
                    frames[row][col]=BitmapSource.Create(CellWidth,CellHeight,96,96,PixelFormats.Bgra32,null,pixels,CellWidth*4);
                    frames[row][col].Freeze();
                    alpha[row][col] = new byte[CellWidth * CellHeight];
                    for (int i = 0; i < alpha[row][col].Length; i++) alpha[row][col][i] = pixels[i * 4 + 3];
                    bool visible=false,transparent=false;
                    for(int i=0;i<alpha[row][col].Length;i++) { if(alpha[row][col][i]>16) visible=true; if(alpha[row][col][i]==0) transparent=true; }
                    if(!visible || !transparent) throw new InvalidOperationException(TextResources.Get("spriteAtlas.spriteAtlas.text.alternativeA"));
                }
            }
        }
        internal SpriteAtlas(string folder,SkinManifest manifest)
        {
            CellWidth=480;CellHeight=520;FrameMultiplier=1;HasGaze=true;
            var image=IllustratedPack.ReadImage(IllustratedPack.AssetPath(folder,manifest.Gaze.File),1920,2080);
            for(int row=0;row<11;row++) { int count=row<9?AnimationPlayer.Counts[row]:8;frames[row]=new BitmapSource[count];alpha[row]=new byte[count][]; }
            for(int i=0;i<16;i++) {
                var crop=new CroppedBitmap(image,new Int32Rect(i%4*480,i/4*520,480,520));
                var bgra=new FormatConvertedBitmap(crop,PixelFormats.Bgra32,null,0);
                byte[] pixels=new byte[480*520*4];bgra.CopyPixels(pixels,480*4,0);
                int row=9+i/8,col=i%8;
                frames[row][col]=BitmapSource.Create(480,520,96,96,PixelFormats.Bgra32,null,pixels,480*4);frames[row][col].Freeze();
                alpha[row][col]=new byte[480*520];for(int p=0;p<alpha[row][col].Length;p++)alpha[row][col][p]=pixels[p*4+3];
            }
            // Legacy callers still have a valid fallback; v2 action rendering always
            // uses the independent manifest clips, never these placeholder cells.
            for(int row=0;row<9;row++)for(int col=0;col<frames[row].Length;col++){frames[row][col]=frames[10][0];alpha[row][col]=alpha[10][0];}
        }
        public static void ValidateDimensions(int width, int height)
        {
            if (width % 8 != 0 || height % 11 != 0 || width / 8 < 64 || height / 11 < 64 || width / 8 > 768 || height / 11 > 832 || (long)width*height > 55000000)
                throw new InvalidOperationException(TextResources.Get("spriteAtlas.validateDimensions.text"));
        }
        public BitmapSource GetFrame(int row, int col) { return frames[row][col]; }
        public byte AlphaAt(int row, int col, int x, int y)
        { return x < 0 || y < 0 || x >= CellWidth || y >= CellHeight ? (byte)0 : alpha[row][col][y * CellWidth + x]; }
    }
}
