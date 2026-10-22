using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    public sealed class InteractionAtlas
    {
        private readonly BitmapSource[,] frames = new BitmapSource[8, 4];
        private readonly byte[,][] alpha = new byte[8, 4][];
        public int FrameWidth { get { return 480; } }
        public int FrameHeight { get { return 520; } }

        private InteractionAtlas(Stream stream)
        {
            var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (image.PixelWidth != FrameWidth * 4 || image.PixelHeight != FrameHeight * 8)
                throw new InvalidOperationException("Interaction artwork must contain 4 columns and 8 rows of 480 x 520 frames.");
            image.Freeze();
            for (int row = 0; row < 8; row++) for (int col = 0; col < 4; col++)
            {
                var crop = new CroppedBitmap(image, new Int32Rect(col * FrameWidth, row * FrameHeight, FrameWidth, FrameHeight));
                var bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                byte[] pixels = new byte[FrameWidth * FrameHeight * 4];
                bgra.CopyPixels(pixels, FrameWidth * 4, 0);
                // Each frame owns only its small pixel buffer: retaining a crop
                // would also retain/upload the full atlas in WPF.
                frames[row, col] = BitmapSource.Create(FrameWidth, FrameHeight, 96, 96, PixelFormats.Bgra32, null, pixels, FrameWidth * 4);
                frames[row, col].Freeze();
                byte[] mask = new byte[FrameWidth * FrameHeight];
                bool visible = false, transparent = false;
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = pixels[i * 4 + 3];
                    visible |= mask[i] > 16;
                    transparent |= mask[i] == 0;
                }
                if (!visible || !transparent) throw new InvalidOperationException("Interaction frames require visible artwork and a transparent background.");
                alpha[row, col] = mask;
            }
        }

        public static InteractionAtlas LoadForSkin(SkinDefinition skin)
        {
            return skin == null || !skin.Embedded ? null : LoadForSkin(skin.BuiltinCharacterId??skin.Id);
        }

        public static InteractionAtlas LoadForSkin(string skinId)
        {
            string resource;
            switch (skinId)
            {
                case "e-bao": resource = "Dafeiyu.EbaoInteractions"; break;
                case "dafeiyu": resource = "Dafeiyu.DafeiyuInteractions"; break;
                case "marfu": resource = "Dafeiyu.MarfuInteractions"; break;
                default: return null;
            }
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                return stream == null ? null : new InteractionAtlas(stream);
        }

        public BitmapSource GetFrame(int row, int col) { return frames[row, col]; }

        public byte AlphaAt(int row, int col, int x, int y)
        {
            return x < 0 || y < 0 || x >= FrameWidth || y >= FrameHeight ? (byte)0 : alpha[row, col][y * FrameWidth + x];
        }
    }
}
