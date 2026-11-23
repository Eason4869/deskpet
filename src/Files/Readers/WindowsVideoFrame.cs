#if !PORTABLE
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinRt = Dafeiyu.WindowsMediaRuntime;

namespace Dafeiyu
{
    internal static class WindowsVideoFrame
    {
        const int MaximumSide = 1600;
        const int MaximumPngBytes = 4 * 1024 * 1024;
        const string MediaEditing = "Windows.Media.Editing.";

        internal static byte[] Extract(string path, long timestampMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (timestampMs < 0 || timestampMs > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond)
                throw new ArgumentOutOfRangeException("timestampMs");
            var timestamp = TimeSpan.FromTicks(timestampMs * TimeSpan.TicksPerMillisecond);
            return Task.Run(() => ExtractCore(path, timestamp, token), token).GetAwaiter().GetResult();
        }

        static byte[] ExtractCore(string path, TimeSpan timestamp, CancellationToken token)
        {
            using (var input = new BinaryReader(File.OpenRead(path), Encoding.ASCII))
            {
                int remaining = 65536;
                CheckDataReferences(input, input.BaseStream.Length, 0, ref remaining, token);
            }
            try
            {
                var runtime = new WindowsMediaRuntime(token);
                var storageFileType = WinRt.WinRtType("Windows.Storage.StorageFile");
                var file = runtime.Await(storageFileType, "GetFileFromPathAsync", null, Path.GetFullPath(path));
                var clipType = WinRt.WinRtType(MediaEditing + "MediaClip");
                var clip = runtime.Await(clipType, "CreateFromFileAsync", null, file);
                var duration = (TimeSpan)WinRt.Property(clipType, "OriginalDuration", clip);
                if (timestamp >= duration) throw new ArgumentOutOfRangeException("timestamp");
                var encoding = WinRt.Invoke(WinRt.Method(clipType, "GetVideoEncodingProperties", 0), clip);
                if (encoding == null) throw new NotSupportedException("The file has no decodable video track.");
                Type propertiesType = WinRt.WinRtType("Windows.Media.MediaProperties.VideoEncodingProperties");
                uint width = (uint)WinRt.Property(propertiesType, "Width", encoding);
                uint height = (uint)WinRt.Property(propertiesType, "Height", encoding);
                if (width == 0 || height == 0) throw new InvalidDataException("The video has invalid frame dimensions.");
                Size size = Fit(width, height);

                var compositionType = WinRt.WinRtType(MediaEditing + "MediaComposition");
                var composition = Activator.CreateInstance(compositionType);
                var clips = WinRt.Property(compositionType, "Clips", composition);
                var collection = typeof(ICollection<>).MakeGenericType(clipType);
                WinRt.Invoke(WinRt.Method(collection, "Add", 1), clips, clip);
                var precision = WinRt.EnumValue(MediaEditing + "VideoFramePrecision", "NearestFrame");
                var thumbnail = runtime.Await(compositionType, "GetThumbnailAsync", composition, timestamp, size.Width, size.Height, precision);
                try
                {
                    using (var stream = runtime.OpenForRead(thumbnail))
                    using (var frame = Image.FromStream(stream, false, true))
                        return EncodePng(frame, token);
                }
                finally { WinRt.Dispose(thumbnail); }
            }
            catch (TypeLoadException error) { throw Unavailable(error); }
            catch (MissingMemberException error) { throw Unavailable(error); }
            catch (COMException error) { throw Unavailable(error); }
            catch (FileLoadException error) { throw Unavailable(error); }
            catch (UnauthorizedAccessException error) { throw Unavailable(error); }
        }

        static NotSupportedException Unavailable(Exception error)
        {
            return new NotSupportedException("The native Windows video decoder could not extract a frame.", error);
        }

        static Size Fit(uint width, uint height)
        {
            double scale = Math.Min(1.0, (double)MaximumSide / Math.Max(width, height));
            return new Size(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        }

        static void CheckDataReferences(BinaryReader reader, long end, int level, ref int remaining, CancellationToken token)
        {
            while (reader.BaseStream.Position < end)
            {
                token.ThrowIfCancellationRequested();
                string type;
                long next = ReadBox(reader, end, ref remaining, out type);
                if (level == 0 && type == "moov" || level == 1 && type == "trak" || level == 2 && type == "mdia" ||
                    level == 3 && type == "minf" || level == 4 && type == "dinf")
                    CheckDataReferences(reader, next, level + 1, ref remaining, token);
                else if (level == 5 && type == "dref")
                {
                    if (next - reader.BaseStream.Position < 8 || ReadBigEndian(reader) != 0)
                        throw new InvalidDataException("Invalid MP4 data-reference table.");
                    uint count = ReadBigEndian(reader);
                    for (uint i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        long entryEnd = ReadBox(reader, next, ref remaining, out type);
                        if (type != "url " || entryEnd - reader.BaseStream.Position != 4 || ReadBigEndian(reader) != 1)
                            throw new InvalidDataException("An MP4 track references data outside the selected file.");
                    }
                    if (reader.BaseStream.Position != next) throw new InvalidDataException("Invalid MP4 data-reference table.");
                }
                reader.BaseStream.Position = next;
            }
        }

        static long ReadBox(BinaryReader reader, long end, ref int remaining, out string type)
        {
            long start = reader.BaseStream.Position;
            if (--remaining < 0 || end - start < 8) throw new InvalidDataException("Invalid MP4 box.");
            ulong size = ReadBigEndian(reader);
            type = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (size == 1)
            {
                if (end - start < 16) throw new InvalidDataException("Invalid extended MP4 box.");
                size = ((ulong)ReadBigEndian(reader) << 32) | ReadBigEndian(reader);
            }
            if (size == 0) size = (ulong)(end - start);
            if (size < (ulong)(reader.BaseStream.Position - start) || size > (ulong)(end - start))
                throw new InvalidDataException("MP4 box exceeds its container.");
            return start + (long)size;
        }

        static uint ReadBigEndian(BinaryReader reader)
        {
            return ((uint)reader.ReadByte() << 24) | ((uint)reader.ReadByte() << 16) | ((uint)reader.ReadByte() << 8) | reader.ReadByte();
        }

        static byte[] EncodePng(Image source, CancellationToken token)
        {
            var size = Fit((uint)source.Width, (uint)source.Height);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using (var frame = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
                using (var output = new MemoryStream())
                {
                    using (var graphics = Graphics.FromImage(frame))
                    {
                        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(source, 0, 0, size.Width, size.Height);
                    }
                    frame.Save(output, ImageFormat.Png);
                    token.ThrowIfCancellationRequested();
                    if (output.Length <= MaximumPngBytes) return output.ToArray();
                }
                size = new Size(Math.Max(1, size.Width * 3 / 4), Math.Max(1, size.Height * 3 / 4));
            }
        }
    }
}
#endif
