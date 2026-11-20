using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    internal sealed class MediaFileReader : IFileContentReader
    {
        static Func<string, long, CancellationToken, FileReadResult> platformFrame;
        readonly string tools;
        readonly int timeout;

        internal MediaFileReader() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "file-tools"), 15000) { }

        internal MediaFileReader(string tools, int timeout)
        {
            this.tools = Path.GetFullPath(tools);
            this.timeout = timeout;
        }

        internal static void ConfigureFrameExtractor(Func<string, long, CancellationToken, FileReadResult> extract)
        {
            Interlocked.Exchange(ref platformFrame, extract);
        }

        public FileManifest Inspect(string path, CancellationToken token)
        {
            var info = Probe(path, token);
            var sections = new List<FileSection> { new FileSection("info", TextResources.Get("fileCompatibility.mediaInfo")) };
            if (info.Video) sections.Add(new FileSection("frame", TextResources.Get("fileCompatibility.videoFrame")));
            return new FileManifest("media", sections, info.Text);
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            var info = Probe(path, token);
            if (request.Section != "frame" && request.Operation != "frame") return new FileReadResult(info.Text, "");
            if (!info.Video || request.TimestampMs < 0 || (info.DurationMs.HasValue && request.TimestampMs >= info.DurationMs.Value))
                throw new ArgumentOutOfRangeException("timestampMs");
            var extract = Volatile.Read(ref platformFrame);
            if (extract != null) return extract(path, request.TimestampMs, token);
#if !PORTABLE
            try
            {
                byte[] image=WindowsVideoFrame.Extract(path,request.TimestampMs,token);
                return new FileReadResult("",FileContentService.FormatFrameTime(request.TimestampMs,-1),null,image,"image/png");
            }
            catch(NotSupportedException){if(!File.Exists(Executable("ffmpeg")))throw;}
#endif
            string timestamp = (request.TimestampMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            var args = new[] { "-v", "error", "-nostdin", "-threads", "1", "-protocol_whitelist", "file", "-format_whitelist", "mov",
                "-enable_drefs", "0", "-ss", timestamp, "-i", Path.GetFullPath(path), "-map", "0:V:0", "-an", "-frames:v", "1",
                "-vf", "scale=1600:1600:force_original_aspect_ratio=decrease", "-threads", "1", "-c:v", "png", "-f", "image2pipe", "pipe:1" };
            byte[] png = Run("ffmpeg", args, ChatAttachment.MaximumBytes, token);
            if (png.Length < 8 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
                throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
            return new FileReadResult("", TextResources.Format("fileCompatibility.frameAt", timestamp), null, png, "image/png");
        }

        sealed class MediaInformation
        {
            internal bool Video;
            internal long? DurationMs;
            internal string Text;
        }

        MediaInformation Probe(string path, CancellationToken token)
        {
            string extension = FileContentService.Extension(path);
            if (!File.Exists(Executable("ffprobe")))
            {
                if (extension == ".wav") return ProbeWave(path, token);
                var header=MediaHeaderReader.Inspect(path,token);
                return new MediaInformation{Video=header.Video,DurationMs=header.DurationMs,Text=header.Text};
            }
            string format = extension == ".mp4" ? "mov" : extension.TrimStart('.');
            var args = new List<string> { "-v", "error", "-protocol_whitelist", "file", "-format_whitelist", format,
                "-probesize", "4000000", "-analyzeduration", "5000000" };
            if (extension == ".mp4") args.AddRange(new[] { "-enable_drefs", "0" });
            args.AddRange(new[] { "-show_entries", "format=format_name,duration:stream=codec_name,codec_type,width,height,sample_rate,channels:stream_disposition=attached_pic:stream_side_data=rotation",
                "-of", "json", Path.GetFullPath(path) });
            byte[] bytes = Run("ffprobe", args, 64 * 1024, token);
            var data = new JavaScriptSerializer { MaxJsonLength = 64 * 1024, RecursionLimit = 8 }.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
            object value;
            if (data == null || !data.TryGetValue("streams", out value))
                throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
            var streams = value as object[];
            if (streams == null || streams.Length == 0)
                throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
            var info = new MediaInformation();
            var lines = new List<string>();
            if (data.TryGetValue("format", out value))
            {
                var metadata = value as Dictionary<string, object>;
                string duration = Get(metadata, "duration");
                double seconds;
                if (Double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0 && seconds < Int32.MaxValue)
                    info.DurationMs = (long)Math.Round(seconds * 1000);
                lines.Add(Get(metadata, "format_name") + (info.DurationMs.HasValue ? " | " + duration + " s" : ""));
            }
            foreach (var row in streams.OfType<Dictionary<string, object>>().Take(32))
            {
                string type = Get(row, "codec_type");
                string line = type + ": " + Get(row, "codec_name");
                if (type == "video")
                {
                    if (!row.TryGetValue("disposition", out value) || Get(value as Dictionary<string, object>, "attached_pic") != "1") info.Video = true;
                    line += " | " + Get(row, "width") + " × " + Get(row, "height");
                    if (row.TryGetValue("side_data_list", out value) && value is object[])
                        foreach (var entry in ((object[])value).OfType<Dictionary<string, object>>())
                        {
                            string angle = Get(entry, "rotation");
                            if (angle.Length != 0) line += " | " + angle + "°";
                        }
                }
                if (type == "audio") line += " | " + Get(row, "sample_rate") + " Hz | " + Get(row, "channels") + " ch";
                lines.Add(line);
            }
            info.Text = String.Join("\n", lines);
            return info;
        }

        static MediaInformation ProbeWave(string path, CancellationToken token)
        {
            using (var input = new BinaryReader(File.OpenRead(path), Encoding.ASCII))
            {
                if (ReadTag(input) != "RIFF") throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
                long end = input.ReadUInt32() + 8L;
                if (ReadTag(input) != "WAVE" || end > input.BaseStream.Length || end < 12)
                    throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
                uint sampleRate = 0, byteRate = 0, dataBytes = 0;
                ushort format = 0, channels = 0;
                for (int chunks = 0; chunks < 1024 && input.BaseStream.Position + 8 <= end; chunks++)
                {
                    token.ThrowIfCancellationRequested();
                    string type = ReadTag(input);
                    uint length = input.ReadUInt32();
                    long remaining = end - input.BaseStream.Position;
                    if (length > remaining) throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
                    long next = input.BaseStream.Position + length + (length & 1);
                    if (type == "fmt " && length >= 16)
                    {
                        format = input.ReadUInt16();
                        channels = input.ReadUInt16();
                        sampleRate = input.ReadUInt32();
                        byteRate = input.ReadUInt32();
                    }
                    if (type == "data") dataBytes = length;
                    input.BaseStream.Position = next;
                    if (byteRate > 0 && dataBytes > 0) break;
                }
                if (channels == 0 || sampleRate == 0 || byteRate == 0 || dataBytes == 0)
                    throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
                long duration = dataBytes * 1000L / byteRate;
                string codec = format == 1 ? "PCM" : format == 3 ? "PCM float" : "WAVE 0x" + format.ToString("X", CultureInfo.InvariantCulture);
                return new MediaInformation { DurationMs = duration, Text = "WAV | " + (duration / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + " s\n" +
                    codec + " | " + sampleRate.ToString(CultureInfo.InvariantCulture) + " Hz | " + channels.ToString(CultureInfo.InvariantCulture) + " ch" };
            }
        }

        static string ReadTag(BinaryReader input) { return Encoding.ASCII.GetString(input.ReadBytes(4)); }
        static string Get(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
        }

        string Executable(string tool)
        {
            return Path.Combine(tools, tool + (Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : ""));
        }

        byte[] Run(string tool, IEnumerable<string> args, int max, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string executable = Executable(tool);
            CachePaths.Check(executable);
            if (!File.Exists(executable)) throw new NotSupportedException(TextResources.Get("fileCompatibility.componentUnavailable"));
            var options = new ProcessStartInfo(executable) { WorkingDirectory = tools, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
#if PORTABLE
            foreach (string arg in args) options.ArgumentList.Add(arg);
#else
            options.Arguments = String.Join(" ", args.Select(Quote));
#endif
            using (var process = Process.Start(options))
            {
                process.StandardInput.Close();
                var output = Task.Run(() => Capture(process.StandardOutput.BaseStream, max));
                var errors = Task.Run(() => { process.StandardError.BaseStream.CopyTo(Stream.Null); });
                try
                {
                    var timer = Stopwatch.StartNew();
                    while (!process.WaitForExit(50))
                    {
                        token.ThrowIfCancellationRequested();
                        if (timer.ElapsedMilliseconds > timeout) throw new TimeoutException(TextResources.Get("fileCompatibility.timeout"));
                        if (output.IsFaulted) output.GetAwaiter().GetResult();
                    }
                    token.ThrowIfCancellationRequested();
                    byte[] bytes = output.GetAwaiter().GetResult();
                    errors.GetAwaiter().GetResult();
                    if (process.ExitCode != 0) throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
                    return bytes;
                }
                finally
                {
                    if (!process.HasExited)
                    {
#if PORTABLE
                        process.Kill(true);
#else
                        process.Kill();
#endif
                        process.WaitForExit(2000);
                    }
                    try { Task.WaitAll(new Task[] { output, errors }, 500); }
                    catch (AggregateException) { }
                }
            }
        }

        static byte[] Capture(Stream stream, int max)
        {
            using (var buffer = new MemoryStream())
            {
                var block = new byte[4096];
                int size;
                while ((size = stream.Read(block, 0, block.Length)) != 0)
                {
                    if (buffer.Length + size > max) throw new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid"));
                    buffer.Write(block, 0, size);
                }
                return buffer.ToArray();
            }
        }

#if !PORTABLE
        static string Quote(string value)
        {
            return "\"" + System.Text.RegularExpressions.Regex.Replace(value, "(\\\\*)(\"|$)", m =>
                m.Groups[1].Value + m.Groups[1].Value + (m.Groups[2].Value.Length > 0 ? "\\\"" : "")) + "\"";
        }
#endif
    }
}
