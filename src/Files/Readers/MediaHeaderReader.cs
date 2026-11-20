using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class MediaHeaderMetadata
    {
        internal MediaHeaderMetadata(bool video, long? durationMs, string text)
        {
            Video = video;
            DurationMs = durationMs;
            Text = text;
        }

        internal bool Video { get; private set; }
        internal long? DurationMs { get; private set; }
        internal string Text { get; private set; }
    }

    internal static class MediaHeaderReader
    {
        static readonly int[] Mpeg1Rates = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
        static readonly int[] Mpeg2Rates = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
        static readonly int[] SampleRates = { 44100, 48000, 32000 };
        static readonly int[] AacRates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };
        static readonly int[] AacChannels = { 0, 1, 2, 3, 4, 5, 6, 8 };

        internal static MediaHeaderMetadata Inspect(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var input = new BinaryReader(stream, Encoding.ASCII))
            {
                if (stream.Length > AttachmentFileStore.AbsoluteFileLimit) throw InvalidMedia();
                try
                {
                    if (Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                        return ReadMp3(input, token);
                    if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                        return new MovieReader(input, token).Read();
                    throw InvalidMedia();
                }
                catch (EndOfStreamException) { throw InvalidMedia(); }
            }
        }

        sealed class MovieReader
        {
            const int MaxBoxes = 16384;
            const int MaxTracks = 32;
            readonly BinaryReader input;
            readonly CancellationToken token;
            readonly HashSet<uint> fragmentTracks = new HashSet<uint>();
            readonly List<Track> tracks = new List<Track>();
            uint timescale;
            ulong duration;
            ulong fragmentDuration;
            int boxes;
            bool movieHeader;
            bool fragmented;

            struct Atom
            {
                internal long Start;
                internal long End;
                internal string Type;
                internal long Size { get { return End - Start; } }
            }

            sealed class Track
            {
                internal uint Id;
                internal string Handler;
                internal string Codec;
                internal bool HasSamples;
                internal int Width, Height, DisplayWidth, DisplayHeight, Rate, Channels, Rotation;
            }

            internal MovieReader(BinaryReader input, CancellationToken token)
            {
                this.input = input;
                this.token = token;
            }

            internal MediaHeaderMetadata Read()
            {
                bool format = false, movie = false, mediaData = false;
                foreach (Atom atom in Children(0, input.BaseStream.Length))
                {
                    if (atom.Type == "ftyp")
                    {
                        if (format || atom.Size < 8 || atom.Size % 4 != 0 || !Printable(FourCcAt(atom.Start))) throw InvalidMedia();
                        format = true;
                    }
                    else if (atom.Type == "moov")
                    {
                        if (movie) throw InvalidMedia();
                        ReadMovie(atom);
                        movie = true;
                    }
                    else if (atom.Type == "moof")
                    {
                        fragmented = true;
                        ReadFragment(atom);
                    }
                    else if (atom.Type == "mdat" && atom.Size > 0) mediaData = true;
                }
                if (!format || !movieHeader || !mediaData || timescale == 0) throw InvalidMedia();
                long? time = Milliseconds(fragmented ? fragmentDuration : duration, timescale);
                var output = new List<string> { Heading("MP4", time, "") };
                bool video = false;
                foreach (Track track in tracks)
                {
                    if (track.Codec == null || (!track.HasSamples && !fragmentTracks.Contains(track.Id))) continue;
                    string codec = CodecLabel(track.Codec);
                    if (track.Handler == "vide")
                    {
                        video |= track.Codec != "encv";
                        int width = track.Width != 0 && track.Height != 0 ? track.Width : track.DisplayWidth;
                        int height = track.Width != 0 && track.Height != 0 ? track.Height : track.DisplayHeight;
                        output.Add(codec + (width != 0 && height != 0 ? " | " + Number(width) + " × " + Number(height) : "") +
                            (track.Rotation == 0 ? "" : " | " + Number(track.Rotation) + "°"));
                    }
                    else if (track.Handler == "soun")
                        output.Add(codec + AudioDetails(track.Rate, track.Channels));
                }
                if (output.Count == 1) throw InvalidMedia();
                return new MediaHeaderMetadata(video, time, string.Join("\n", output));
            }

            void ReadMovie(Atom movie)
            {
                foreach (Atom child in Children(movie.Start, movie.End))
                {
                    switch (child.Type)
                    {
                        case "mvhd":
                            if (movieHeader) throw InvalidMedia();
                            int version = Version(child);
                            Require(child, version == 1 ? 112 : 100);
                            input.BaseStream.Position = child.Start + (version == 1 ? 20 : 12);
                            timescale = Big32(input);
                            duration = TimeField(input, version);
                            movieHeader = true;
                            break;
                        case "trak":
                            if (tracks.Count >= MaxTracks) throw InvalidMedia();
                            tracks.Add(ReadTrack(child));
                            break;
                        case "mvex":
                            fragmented = true;
                            foreach (Atom extension in Children(child.Start, child.End))
                            {
                                if (extension.Type != "mehd") continue;
                                int fragmentVersion = Version(extension);
                                Require(extension, fragmentVersion == 1 ? 12 : 8);
                                input.BaseStream.Position = extension.Start + 4;
                                fragmentDuration = TimeField(input, fragmentVersion);
                            }
                            break;
                    }
                }
            }

            Track ReadTrack(Atom atom)
            {
                var track = new Track();
                Atom samples = new Atom();
                foreach (Atom child in Children(atom.Start, atom.End))
                {
                    if (child.Type == "tkhd") ReadTrackHeader(track, child);
                    if (child.Type != "mdia") continue;
                    foreach (Atom media in Children(child.Start, child.End))
                    {
                        if (media.Type == "hdlr")
                        {
                            Require(media, 24);
                            track.Handler = FourCcAt(media.Start + 8);
                        }
                        else if (media.Type == "minf")
                            foreach (Atom information in Children(media.Start, media.End))
                                if (information.Type == "stbl") samples = information;
                    }
                }
                if ((track.Handler != "vide" && track.Handler != "soun") || samples.End == 0) return track;
                if (track.Id == 0) throw InvalidMedia();
                foreach (Atom child in Children(samples.Start, samples.End))
                {
                    if (child.Type == "stsd") ReadDescription(track, child);
                    else if (child.Type == "stsz" || child.Type == "stz2") track.HasSamples = HasSamples(child);
                }
                return track;
            }

            void ReadTrackHeader(Track track, Atom atom)
            {
                int shift = Version(atom) == 1 ? 12 : 0;
                Require(atom, 84 + shift);
                input.BaseStream.Position = atom.Start + (shift == 12 ? 20 : 12);
                track.Id = Big32(input);
                input.BaseStream.Position = atom.Start + 40 + shift;
                int a = unchecked((int)Big32(input));
                int b = unchecked((int)Big32(input));
                Big32(input);
                int c = unchecked((int)Big32(input));
                int d = unchecked((int)Big32(input));
                if (a == 0 && d == 0)
                {
                    if (b == 65536 && c == -65536) track.Rotation = 90;
                    if (b == -65536 && c == 65536) track.Rotation = 270;
                }
                else if (a == -65536 && d == -65536 && b == 0 && c == 0) track.Rotation = 180;
                input.BaseStream.Position = atom.Start + 76 + shift;
                track.DisplayWidth = (int)(Big32(input) >> 16);
                track.DisplayHeight = (int)(Big32(input) >> 16);
            }

            void ReadDescription(Track track, Atom atom)
            {
                Require(atom, 8);
                input.BaseStream.Position = atom.Start + 4;
                uint entries = Big32(input);
                uint read = 0;
                foreach (Atom description in Children(atom.Start + 8, atom.End))
                {
                    if (++read > entries || !Printable(description.Type)) throw InvalidMedia();
                    if (read > 1) continue;
                    track.Codec = description.Type;
                    if (track.Handler == "vide")
                    {
                        Require(description, 28);
                        input.BaseStream.Position = description.Start + 24;
                        track.Width = Big16(input);
                        track.Height = Big16(input);
                    }
                    else
                    {
                        Require(description, 28);
                        input.BaseStream.Position = description.Start + 16;
                        track.Channels = Big16(input);
                        input.BaseStream.Position = description.Start + 24;
                        track.Rate = (int)(Big32(input) >> 16);
                        if (track.Codec == "mp4a") ReadMpeg4Audio(track, description);
                    }
                }
                if (read != entries) throw InvalidMedia();
            }

            void ReadMpeg4Audio(Track track, Atom sample)
            {
                input.BaseStream.Position = sample.Start + 8;
                int version = Big16(input);
                int fixedBytes = version == 0 ? 28 : version == 1 ? 44 : version == 2 ? 64 : 0;
                if (fixedBytes == 0 || sample.Size < fixedBytes) return;
                foreach (Atom entry in Children(sample.Start + fixedBytes, sample.End))
                {
                    if (entry.Type != "esds") continue;
                    Require(entry, 4);
                    input.BaseStream.Position = entry.Start + 4;
                    long end;
                    if (ReadDescriptor(entry.End, out end) != 3 || end - input.BaseStream.Position < 3) return;
                    Big16(input);
                    int flags = input.ReadByte();
                    if ((flags & 0x80) != 0) input.BaseStream.Position += 2;
                    if ((flags & 0x40) != 0)
                    {
                        if (input.BaseStream.Position >= end) throw InvalidMedia();
                        int length = input.ReadByte();
                        input.BaseStream.Position += length;
                    }
                    if ((flags & 0x20) != 0) input.BaseStream.Position += 2;
                    long decoderEnd;
                    if (ReadDescriptor(end, out decoderEnd) != 4 || decoderEnd - input.BaseStream.Position < 13) return;
                    int objectType = input.ReadByte();
                    input.BaseStream.Position += 12;
                    if (objectType == 0x66 || objectType == 0x67 || objectType == 0x68) track.Codec = "AAC";
                    if (objectType != 0x40) return;
                    long configEnd;
                    if (ReadDescriptor(decoderEnd, out configEnd) == 5)
                        ReadAudioConfiguration(track, input.ReadBytes((int)Math.Min(8, configEnd - input.BaseStream.Position)));
                    return;
                }
            }

            int ReadDescriptor(long parentEnd, out long end)
            {
                end = input.BaseStream.Position;
                if (end == parentEnd) return 0;
                if (end > parentEnd) throw InvalidMedia();
                int type = input.ReadByte();
                int length = 0;
                for (int index = 0; index < 4 && input.BaseStream.Position < parentEnd; index++)
                {
                    int next = input.ReadByte();
                    length = (length << 7) | (next & 0x7f);
                    if ((next & 0x80) != 0) continue;
                    end = input.BaseStream.Position + length;
                    if (end > parentEnd) throw InvalidMedia();
                    return type;
                }
                throw InvalidMedia();
            }

            static void ReadAudioConfiguration(Track track, byte[] bytes)
            {
                int bit = 0;
                int objectType = Bits(bytes, ref bit, 5);
                if (objectType == 31) objectType = 32 + Bits(bytes, ref bit, 6);
                int rate = AudioRate(bytes, ref bit);
                int channels = Bits(bytes, ref bit, 4);
                if (objectType == 5 || objectType == 29) rate = AudioRate(bytes, ref bit);
                if (objectType >= 1 && objectType <= 6 || objectType == 17 || objectType == 19 ||
                    objectType == 20 || objectType == 23 || objectType == 29 || objectType == 39)
                    track.Codec = "AAC";
                else if (objectType == 42) track.Codec = "USAC";
                if (rate > 0) track.Rate = rate;
                if (channels > 0 && channels < AacChannels.Length)
                    track.Channels = objectType == 29 && channels == 1 ? 2 : AacChannels[channels];
            }

            static int AudioRate(byte[] bytes, ref int bit)
            {
                int index = Bits(bytes, ref bit, 4);
                if (index == 15) return Bits(bytes, ref bit, 24);
                return index >= 0 && index < AacRates.Length ? AacRates[index] : 0;
            }

            static int Bits(byte[] bytes, ref int bit, int count)
            {
                if (bit + count > bytes.Length * 8) return -1;
                int value = 0;
                for (int index = 0; index < count; index++, bit++)
                    value = (value << 1) | ((bytes[bit / 8] >> (7 - bit % 8)) & 1);
                return value;
            }

            bool HasSamples(Atom atom)
            {
                Require(atom, 12);
                input.BaseStream.Position = atom.Start + 4;
                uint size = Big32(input);
                uint count = Big32(input);
                long table;
                if (atom.Type == "stsz") table = size == 0 ? 4L * count : 0;
                else
                {
                    int bits = (int)(size & 0xff);
                    if (bits != 4 && bits != 8 && bits != 16) throw InvalidMedia();
                    table = (bits * (long)count + 7) / 8;
                }
                if (table > atom.Size - 12) throw InvalidMedia();
                return count > 0;
            }

            void ReadFragment(Atom atom)
            {
                foreach (Atom group in Children(atom.Start, atom.End))
                {
                    if (group.Type != "traf") continue;
                    uint id = 0;
                    bool samples = false;
                    foreach (Atom child in Children(group.Start, group.End))
                    {
                        if (child.Type != "tfhd" && child.Type != "trun") continue;
                        Require(child, 8);
                        input.BaseStream.Position = child.Start + 4;
                        uint value = Big32(input);
                        if (child.Type == "tfhd") id = value;
                        else samples |= value > 0;
                    }
                    if (id != 0 && samples) fragmentTracks.Add(id);
                }
            }

            int Version(Atom atom)
            {
                Require(atom, 4);
                input.BaseStream.Position = atom.Start;
                int version = input.ReadByte();
                if (version > 1) throw InvalidMedia();
                return version;
            }

            string FourCcAt(long position)
            {
                input.BaseStream.Position = position;
                return ReadTag(input);
            }

            IEnumerable<Atom> Children(long start, long end)
            {
                for (long next = start; next < end;)
                {
                    token.ThrowIfCancellationRequested();
                    if (++boxes > MaxBoxes || end - next < 8) throw InvalidMedia();
                    input.BaseStream.Position = next;
                    ulong size = Big32(input);
                    string type = ReadTag(input);
                    if (size == 1)
                    {
                        if (end - next < 16) throw InvalidMedia();
                        size = Big64(input);
                    }
                    else if (size == 0) size = (ulong)(end - next);
                    long header = input.BaseStream.Position - next + (type == "uuid" ? 16 : 0);
                    if (size < (ulong)header || size > (ulong)(end - next)) throw InvalidMedia();
                    long position = next + header;
                    next += (long)size;
                    yield return new Atom { Start = position, End = next, Type = type };
                }
            }

            static void Require(Atom atom, long minimum)
            {
                if (atom.Size < minimum) throw InvalidMedia();
            }

            static ulong TimeField(BinaryReader input, int version)
            {
                ulong value = version == 1 ? Big64(input) : Big32(input);
                return value == (version == 1 ? ulong.MaxValue : uint.MaxValue) ? 0 : value;
            }
        }

        struct Mp3Frame
        {
            internal int Version, Rate, Channels, Size, Samples;
            internal bool Protected;
        }

        struct Mp3Tag
        {
            internal string Name;
            internal uint Frames;
            internal uint Bytes;
            internal int Padding;
        }

        static MediaHeaderMetadata ReadMp3(BinaryReader input, CancellationToken token)
        {
            long start = LeadingId3(input, token);
            long end = AudioEnd(input, start);
            const int searchLimit = 65536;
            if (end - start < 4) throw InvalidMedia();
            input.BaseStream.Position = start;
            byte[] buffer = input.ReadBytes((int)Math.Min(end - start, searchLimit + 2048));
            Mp3Frame first = new Mp3Frame();
            int sync = -1;
            for (int index = 0; index < searchLimit && index + 4 <= buffer.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                Mp3Frame next;
                if (!TryFrame(Big32(buffer, index), out first)) continue;
                int after = index + first.Size;
                if (start + after == end || (after + 4 <= buffer.Length &&
                    TryFrame(Big32(buffer, after), out next) && Compatible(first, next) && start + after + next.Size <= end))
                {
                    sync = index;
                    break;
                }
            }
            if (sync < 0) throw InvalidMedia();
            start += sync;
            Mp3Tag tag = ReadMp3Tag(input, start, first);
            long position = start;
            const int maxFrames = 100000;
            int frameCount = 0;
            while (position + 4 <= end && frameCount < maxFrames)
            {
                token.ThrowIfCancellationRequested();
                input.BaseStream.Position = position;
                Mp3Frame frame;
                if (!TryFrame(Big32(input), out frame) || !Compatible(first, frame) || position + frame.Size > end) break;
                position += frame.Size;
                frameCount++;
            }
            bool complete = position == end;
            long? duration = null;
            string source = "";
            bool bytesMatch = tag.Bytes == end - start || tag.Bytes == end - start - first.Size;
            if (tag.Name != null && tag.Frames > 0 &&
                ((complete && (tag.Bytes == 0 || bytesMatch) &&
                    (tag.Frames == frameCount || tag.Frames == frameCount - 1)) ||
                (!complete && frameCount == maxFrames && bytesMatch && tag.Frames >= frameCount - 1 &&
                    tag.Frames <= (end - start) / 16)))
            {
                ulong samples = (ulong)tag.Frames * (uint)first.Samples;
                if ((ulong)tag.Padding < samples) samples -= (ulong)tag.Padding;
                duration = Milliseconds(samples, (uint)first.Rate);
                source = tag.Name;
            }
            else if (complete)
                duration = Milliseconds((ulong)frameCount * (uint)first.Samples, (uint)first.Rate);
            return new MediaHeaderMetadata(false, duration, Heading("MP3", duration, source) +
                "\nMPEG Layer III" + AudioDetails(first.Rate, first.Channels));
        }

        static bool TryFrame(uint header, out Mp3Frame frame)
        {
            frame = new Mp3Frame();
            if ((header & 0xffe00000) != 0xffe00000 || ((header >> 17) & 3) != 1 || (header & 3) == 2) return false;
            int version = (int)((header >> 19) & 3);
            int bitRate = (int)((header >> 12) & 15);
            int sampleRate = (int)((header >> 10) & 3);
            if (version == 1 || bitRate == 0 || bitRate == 15 || sampleRate == 3) return false;
            frame.Version = version;
            frame.Rate = SampleRates[sampleRate] / (version == 3 ? 1 : version == 2 ? 2 : 4);
            frame.Channels = ((header >> 6) & 3) == 3 ? 1 : 2;
            frame.Samples = version == 3 ? 1152 : 576;
            frame.Size = (version == 3 ? 144000 * Mpeg1Rates[bitRate] : 72000 * Mpeg2Rates[bitRate]) / frame.Rate +
                (int)((header >> 9) & 1);
            frame.Protected = (header & 0x10000) == 0;
            return frame.Size >= 16;
        }

        static bool Compatible(Mp3Frame left, Mp3Frame right)
        {
            return left.Version == right.Version && left.Rate == right.Rate && left.Channels == right.Channels;
        }

        static Mp3Tag ReadMp3Tag(BinaryReader input, long start, Mp3Frame frame)
        {
            input.BaseStream.Position = start;
            byte[] bytes = input.ReadBytes(frame.Size);
            int offset = 4 + (frame.Version == 3 ? frame.Channels == 1 ? 17 : 32 : frame.Channels == 1 ? 9 : 17);
            Mp3Tag tag = Xing(bytes, offset + (frame.Protected ? 2 : 0));
            if (tag.Name == null && frame.Protected) tag = Xing(bytes, offset);
            if (tag.Name != null) return tag;
            if (bytes.Length >= 54 && Encoding.ASCII.GetString(bytes, 36, 4) == "VBRI" && bytes[40] == 0 && bytes[41] == 1)
                return new Mp3Tag { Name = "VBRI", Bytes = Big32(bytes, 46), Frames = Big32(bytes, 50) };
            return tag;
        }

        static Mp3Tag Xing(byte[] frame, int offset)
        {
            if (offset + 8 > frame.Length) return new Mp3Tag();
            string marker = Encoding.ASCII.GetString(frame, offset, 4);
            if (marker != "Xing" && marker != "Info") return new Mp3Tag();
            uint flags = Big32(frame, offset + 4);
            int cursor = offset + 8;
            var tag = new Mp3Tag { Name = marker };
            if ((flags & 1) != 0)
            {
                if (cursor + 4 > frame.Length) return new Mp3Tag();
                tag.Frames = Big32(frame, cursor);
                cursor += 4;
            }
            if ((flags & 2) != 0)
            {
                if (cursor + 4 > frame.Length) return new Mp3Tag();
                tag.Bytes = Big32(frame, cursor);
                cursor += 4;
            }
            if ((flags & 4) != 0) cursor += 100;
            if ((flags & 8) != 0) cursor += 4;
            if (cursor + 24 > frame.Length) return tag;
            string encoder = Encoding.ASCII.GetString(frame, cursor, 4);
            if (encoder == "LAME" || encoder == "Lavf" || encoder == "Lavc")
            {
                int padding = (frame[cursor + 21] << 16) | (frame[cursor + 22] << 8) | frame[cursor + 23];
                tag.Padding = (padding >> 12) + (padding & 4095);
            }
            return tag;
        }

        static long LeadingId3(BinaryReader input, CancellationToken token)
        {
            long position = 0;
            for (int tags = 0; tags < 4; tags++)
            {
                token.ThrowIfCancellationRequested();
                input.BaseStream.Position = position;
                byte[] header = input.ReadBytes(10);
                if (header.Length < 3 || header[0] != 'I' || header[1] != 'D' || header[2] != '3') break;
                if (header.Length != 10 || header[3] < 2 || header[3] > 4 ||
                    ((header[6] | header[7] | header[8] | header[9]) & 0x80) != 0) throw InvalidMedia();
                int bytes = header[6] << 21 | header[7] << 14 | header[8] << 7 | header[9];
                position += 10L + bytes + (header[3] == 4 && (header[5] & 0x10) != 0 ? 10 : 0);
                if (position > input.BaseStream.Length) throw InvalidMedia();
            }
            return position;
        }

        static long AudioEnd(BinaryReader input, long start)
        {
            long end = input.BaseStream.Length;
            if (end - start >= 128)
            {
                input.BaseStream.Position = end - 128;
                if (Encoding.ASCII.GetString(input.ReadBytes(3)) == "TAG") end -= 128;
            }
            if (end - start >= 32)
            {
                input.BaseStream.Position = end - 32;
                if (Encoding.ASCII.GetString(input.ReadBytes(8)) == "APETAGEX")
                {
                    uint version = input.ReadUInt32();
                    uint bytes = input.ReadUInt32();
                    input.ReadUInt32();
                    uint flags = input.ReadUInt32();
                    long tagBytes = bytes + ((flags & 0x80000000) != 0 ? 32L : 0);
                    if ((version != 1000 && version != 2000) || bytes < 32 || tagBytes > end - start) throw InvalidMedia();
                    end -= tagBytes;
                }
            }
            return end;
        }

        static long? Milliseconds(ulong count, uint rate)
        {
            if (count == 0 || rate == 0 || count > ulong.MaxValue / 1000) return null;
            double millis = 1000.0 * count / rate;
            return millis > long.MaxValue / 2.0 ? null : (long?)Math.Max(1, Math.Round(millis));
        }

        static string Heading(string format, long? milliseconds, string source)
        {
            if (!milliseconds.HasValue) return format;
            return format + " | " + (milliseconds.Value / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + " s" +
                (source.Length == 0 ? "" : " (" + source + ")");
        }

        static string AudioDetails(int rate, int channels)
        {
            return (rate == 0 ? "" : " | " + Number(rate) + " Hz") + (channels == 0 ? "" : " | " + Number(channels) + " ch");
        }

        static string CodecLabel(string value)
        {
            switch (value)
            {
                case "avc1": case "avc3": return "H.264 (" + value + ")";
                case "hvc1": case "hev1": return "HEVC (" + value + ")";
                case "av01": return "AV1";
                case "vp09": return "VP9";
                case "mp4v": return "MPEG-4 Visual";
                case "mp4a": return "MPEG-4 Audio (mp4a)";
                case "alac": return "Apple Lossless";
                case "Opus": return "Opus";
                default: return value;
            }
        }

        static bool Printable(string value)
        {
            foreach (char character in value)
                if (character < 32 || character > 126 || character == '?') return false;
            return true;
        }

        static string Number(int value) { return value.ToString(CultureInfo.InvariantCulture); }
        static string ReadTag(BinaryReader input)
        {
            byte[] bytes = input.ReadBytes(4);
            if (bytes.Length != 4) throw new EndOfStreamException();
            return Encoding.ASCII.GetString(bytes);
        }
        static int Big16(BinaryReader input) { return input.ReadByte() << 8 | input.ReadByte(); }
        static uint Big32(BinaryReader input) { return (uint)Big16(input) << 16 | (uint)Big16(input); }
        static ulong Big64(BinaryReader input) { return (ulong)Big32(input) << 32 | Big32(input); }
        static uint Big32(byte[] data, int offset)
        {
            return (uint)data[offset] << 24 | (uint)data[offset + 1] << 16 | (uint)data[offset + 2] << 8 | data[offset + 3];
        }
        static InvalidDataException InvalidMedia() { return new InvalidDataException(TextResources.Get("fileCompatibility.mediaInvalid")); }
    }
}
