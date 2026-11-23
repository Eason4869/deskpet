using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Dafeiyu.Tests
{
    public static class MediaCompatibilityChecks
    {
        public static void Run(string directory)
        {
            string wave = Path.Combine(directory, "tone.wav");
            using (var writer = new BinaryWriter(File.Create(wave), Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(16036);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(8000); writer.Write(16000);
                writer.Write((ushort)2); writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(16000); writer.Write(new byte[16000]);
            }
            var reader = new MediaFileReader(directory, 5000);
            var metadata = reader.Inspect(wave, CancellationToken.None);
            if (!metadata.Detail.Contains("1.000 s") || !metadata.Detail.Contains("8000 Hz") || metadata.Sections.Count != 1)
                throw new Exception("The WAV duration, sample rate, or stream capabilities are incorrect.");
            string tools = Environment.GetEnvironmentVariable("DESKPET_MEDIA_TEST_COMPONENT_DIRECTORY");
            string video = Environment.GetEnvironmentVariable("DESKPET_MEDIA_TEST_VIDEO");
            if (!String.IsNullOrWhiteSpace(tools) && !String.IsNullOrWhiteSpace(video))
            {
                reader = new MediaFileReader(tools, 10000);
                var frame = reader.Read(video, new FileReadRequest { Section = "frame", TimestampMs = 500 }, CancellationToken.None);
                if (frame.ImageMediaType != "image/png" || frame.Image == null || frame.Image.Length < 8)
                    throw new Exception("Video extraction did not return a PNG frame.");
                Console.WriteLine("PASS FFprobe and FFmpeg extracted a bounded video frame.");
            }
            Console.WriteLine("PASS WAV metadata is available without a component.");
        }
    }
}
