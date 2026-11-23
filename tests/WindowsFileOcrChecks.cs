#if !PORTABLE
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Dafeiyu.Tests
{
    internal static class WindowsFileOcrChecks
    {
        internal static void Run(string root, string language)
        {
            string label = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "识别测试" : "OCR CHECK";
            string image = Path.Combine(root, "ocr.png"), pdf = Path.Combine(root, "ocr-scan.pdf");
            using (var bitmap = new Bitmap(900, 260))
            using (var font = new Font("Microsoft YaHei", 48, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                DrawLabel(bitmap, font, label, Color.Transparent);
                bitmap.Save(image, ImageFormat.Png);
                DrawLabel(bitmap, font, label, Color.White);
                using (var jpeg = new MemoryStream())
                {
                    bitmap.Save(jpeg, ImageFormat.Jpeg);
                    WriteScan(pdf, jpeg.ToArray(), bitmap.Width, bitmap.Height);
                }
            }
            string png = WindowsFileOcr.Recognize(image, 0, language, CancellationToken.None);
            string scannedPage = WindowsFileOcr.Recognize(pdf, 1, language, CancellationToken.None);
            Require(png.Replace(" ", "").Contains(label.Replace(" ", "")), "transparent PNG OCR identifies fixture text");
            Require(scannedPage.Replace(" ", "").Contains(label.Replace(" ", "")), "raster-only PDF page OCR identifies fixture text");
            try
            {
                WindowsFileOcr.Recognize(pdf, 2, language, CancellationToken.None);
                throw new Exception("The PDF page request exceeded the actual page count.");
            }
            catch (ArgumentOutOfRangeException) { }
            string oversized = Path.Combine(root, "ocr-oversized.png");
            using (var bitmap = new Bitmap(4000, 3001))
                bitmap.Save(oversized, ImageFormat.Png);
            try
            {
                WindowsFileOcr.Recognize(oversized, 0, language, CancellationToken.None);
                throw new Exception("OCR accepted a bitmap larger than 12 MP.");
            }
            catch (InvalidDataException) { }
            try
            {
                WindowsFileOcr.Recognize(image, 0, language, new CancellationToken(true));
                throw new Exception("Pre-canceled OCR succeeded.");
            }
            catch (OperationCanceledException) { }
            try
            {
                WindowsFileOcr.Recognize(image, 0, "unsupported-ocr-language", CancellationToken.None);
                throw new Exception("An unsupported OCR language was accepted.");
            }
            catch (NotSupportedException) { }
        }

        static void DrawLabel(Bitmap bitmap, Font font, string label, Color background)
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(background);
                graphics.DrawString(label, font, Brushes.Black, 35f, 55f);
            }
        }

        static void Require(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine("PASS " + name);
        }

        static void WriteScan(string path, byte[] jpeg, int width, int height)
        {
            string size = width.ToString(CultureInfo.InvariantCulture) + " " + height.ToString(CultureInfo.InvariantCulture);
            string content = "q " + width + " 0 0 " + height + " 0 0 cm /Image Do Q\n";
            using (var output = File.Create(path))
            {
                var offsets = new long[6];
                Write(output, "%PDF-1.4\n");
                offsets[1] = output.Position;
                Write(output, "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
                offsets[2] = output.Position;
                Write(output, "2 0 obj << /Type /Pages /Count 1 /Kids [3 0 R] >> endobj\n");
                offsets[3] = output.Position;
                Write(output, "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 " + size +
                    "] /Resources << /XObject << /Image 4 0 R >> >> /Contents 5 0 R >> endobj\n");
                offsets[4] = output.Position;
                Write(output, "4 0 obj << /Type /XObject /Subtype /Image /Width " + width + " /Height " + height +
                    " /BitsPerComponent 8 /ColorSpace /DeviceRGB /Filter /DCTDecode /Length " + jpeg.Length + " >>\nstream\n");
                output.Write(jpeg, 0, jpeg.Length);
                Write(output, "\nendstream\nendobj\n");
                offsets[5] = output.Position;
                Write(output, "5 0 obj << /Length " + content.Length + " >>\nstream\n" + content + "endstream\nendobj\n");
                long crossReference = output.Position;
                Write(output, "xref\n0 6\n0000000000 65535 f \n");
                for (int i = 1; i < offsets.Length; i++)
                    Write(output, offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                Write(output, "trailer << /Size 6 /Root 1 0 R >>\nstartxref\n" + crossReference + "\n%%EOF\n");
            }
        }

        static void Write(Stream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
#endif
