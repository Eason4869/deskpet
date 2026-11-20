using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Dafeiyu.Tests
{
    public static class MediaHeaderCompatibilityChecks
    {
        public static int Run(string directory)
        {
            string mp4 = Path.Combine(directory, "metadata.mp4");
            File.WriteAllBytes(mp4, Mp4(true, 1500, 40, 30));
            var video = MediaHeaderReader.Inspect(mp4, CancellationToken.None);
            Require(video.Video && video.DurationMs == 1500 && video.Text.Contains("H.264") && video.Text.Contains("40 × 30"),
                "MP4 metadata uses the video track and movie timescale.");

            File.WriteAllBytes(mp4, Mp4(false, 1000, 0, 0));
            var audio = MediaHeaderReader.Inspect(mp4, CancellationToken.None);
            Require(!audio.Video && audio.DurationMs == 1000 && audio.Text.Contains("AAC") && audio.Text.Contains("44100 Hz"),
                "Audio-only MP4 exposes audio metadata without a video frame section.");

            byte[] fragmented = Mp4(true, 0, 40, 30, true);
            File.WriteAllBytes(mp4, fragmented);
            var fragment = MediaHeaderReader.Inspect(mp4, CancellationToken.None);
            Require(fragment.Video && !fragment.DurationMs.HasValue,
                "Fragmented video does not represent a zero movie duration as an exact duration.");
            File.WriteAllBytes(mp4, fragmented.Take(fragmented.Length - 1).ToArray());
            Expect<InvalidDataException>(() => MediaHeaderReader.Inspect(mp4, CancellationToken.None),
                "MP4 refuses a box extending past EOF.");
            Expect<OperationCanceledException>(() => MediaHeaderReader.Inspect(mp4, new CancellationToken(true)),
                "Metadata inspection checks cancellation before touching the file.");

            string mp3 = Path.Combine(directory, "metadata.mp3");
            File.WriteAllBytes(mp3, Mp3(12, true));
            var xing = MediaHeaderReader.Inspect(mp3, CancellationToken.None);
            Require(!xing.Video && xing.DurationMs == 287 && xing.Text.Contains("44100 Hz") && xing.Text.Contains("Xing"),
                "MP3 verifies the Xing header against the measured frame sequence.");
            byte[] gapless = Mp3(12, true);
            const int id3Bytes = 18;
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("Info"), 0, gapless, id3Bytes + 36, 4);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("LAME"), 0, gapless, id3Bytes + 52, 4);
            gapless[id3Bytes + 52 + 22] = 2; // 512 samples of end padding.
            File.WriteAllBytes(mp3, gapless);
            var info = MediaHeaderReader.Inspect(mp3, CancellationToken.None);
            Require(info.DurationMs == 276 && info.Text.Contains("Info"), "MP3 Info applies encoder padding to the duration.");
            byte[] vbri = Mp3(12, false);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("VBRI"), 0, vbri, id3Bytes + 36, 4);
            Set16(vbri, id3Bytes + 40, 1);
            Set32(vbri, id3Bytes + 46, vbri.Length - id3Bytes);
            Set32(vbri, id3Bytes + 50, 11);
            File.WriteAllBytes(mp3, vbri);
            var variable = MediaHeaderReader.Inspect(mp3, CancellationToken.None);
            Require(variable.DurationMs == 287 && variable.Text.Contains("VBRI"), "MP3 recognizes VBRI frame and byte counts.");
            byte[] untagged = Mp3(3, false);
            File.WriteAllBytes(mp3, untagged);
            var frames = MediaHeaderReader.Inspect(mp3, CancellationToken.None);
            Require(frames.DurationMs == 78 && frames.Text.Contains("2 ch"),
                "MP3 derives duration by counting a complete header sequence without Xing.");
            File.WriteAllBytes(mp3, Bytes(untagged, new byte[] { 1, 2, 3, 4, 5, 6 }));
            Require(!MediaHeaderReader.Inspect(mp3, CancellationToken.None).DurationMs.HasValue,
                "MP3 does not claim an exact duration when unverified trailing data remains.");
            File.WriteAllBytes(mp3, Encoding.ASCII.GetBytes("ID3"));
            Expect<InvalidDataException>(() => MediaHeaderReader.Inspect(mp3, CancellationToken.None),
                "A truncated MP3 tag does not qualify as audio.");
            return 11;
        }

        static byte[] Mp4(bool video, int duration, int width, int height, bool fragmented = false)
        {
            byte[] movie = new byte[100];
            Set32(movie, 12, 1000);
            Set32(movie, 16, duration);
            byte[] track = new byte[84];
            Set32(track, 12, 1);
            byte[] hdlr = new byte[24];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(video ? "vide" : "soun"), 0, hdlr, 8, 4);
            byte[] sample = new byte[video ? 78 : 28];
            if (video)
            {
                Set16(sample, 24, width);
                Set16(sample, 26, height);
            }
            else
            {
                Set16(sample, 16, 2);
                Set16(sample, 24, 44100);
            }
            byte[] counts = new byte[12];
            Set32(counts, 4, 4);
            Set32(counts, 8, fragmented ? 0 : 1);
            byte[] esds = video ? new byte[0] : Atom("esds", new byte[4],
                new byte[] { 3, 22, 0, 1, 0, 4, 17, 0x40, 0x15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 2, 0x12, 0x10 });
            byte[] description = Bytes(new byte[7], new byte[] { 1 }, Atom(video ? "avc1" : "mp4a", sample, esds));
            byte[] media = Atom("mdia", Atom("hdlr", hdlr),
                Atom("minf", Atom("stbl", Atom("stsd", description), Atom("stsz", counts))));
            byte[] moov = Atom("moov", Atom("mvhd", movie), Atom("trak", Atom("tkhd", track), media),
                fragmented ? Atom("mvex") : new byte[0]);
            byte[] fragment = fragmented ? Atom("moof", Atom("traf",
                Atom("tfhd", new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }),
                Atom("trun", new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }))) : new byte[0];
            return Bytes(Atom("ftyp", Encoding.ASCII.GetBytes("isom\0\0\0\0")), moov, fragment, Atom("mdat", new byte[4]));
        }

        static byte[] Mp3(int frames, bool xing)
        {
            const int frameSize = 417;
            byte[] audio = new byte[frames * frameSize];
            for (int frame = 0; frame < frames; frame++)
                Buffer.BlockCopy(new byte[] { 0xff, 0xfb, 0x90, 0 }, 0, audio, frame * frameSize, 4);
            if (xing)
            {
                Buffer.BlockCopy(Encoding.ASCII.GetBytes("Xing"), 0, audio, 36, 4);
                Set32(audio, 40, 3);
                Set32(audio, 44, frames - 1);
                Set32(audio, 48, audio.Length);
            }
            return Bytes(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 8 }, new byte[8], audio);
        }

        static byte[] Atom(string type, params byte[][] contents)
        {
            byte[] header = new byte[8];
            Set32(header, 0, 8 + contents.Sum(part => part.Length));
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(type), 0, header, 4, 4);
            return Bytes(new[] { header }.Concat(contents).ToArray());
        }

        static byte[] Bytes(params byte[][] chunks)
        {
            using (var output = new MemoryStream())
            {
                foreach (var chunk in chunks) output.Write(chunk, 0, chunk.Length);
                return output.ToArray();
            }
        }

        static void Set32(byte[] data, int index, int value)
        {
            Set16(data, index, value >> 16);
            Set16(data, index + 2, value);
        }

        static void Set16(byte[] data, int index, int value)
        {
            data[index] = (byte)(value >> 8);
            data[index + 1] = (byte)value;
        }

        static void Expect<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException(message);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
