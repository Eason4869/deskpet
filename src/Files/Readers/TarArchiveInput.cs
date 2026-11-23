using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class TarArchiveInput
    {
        const int Block = 512;
        const int MaxMetadataBytes = 1024 * 1024;
        readonly Stream stream;
        readonly ArchiveFileReader.ExpansionBudget budget;
        readonly int limit;
        readonly CancellationToken token;
        readonly byte[] header = new byte[Block];
        readonly byte[] data = new byte[32768];
        readonly Metadata global = new Metadata();
        long unread;
        int padding;

        sealed class Metadata
        {
            internal string Path;
            internal long? Size;
            internal bool Sparse;
        }

        internal TarArchiveInput(Stream stream, ArchiveFileReader.ExpansionBudget budget,
            int limit, CancellationToken token)
        {
            this.stream = stream;
            this.budget = budget;
            this.limit = limit;
            this.token = token;
        }

        internal int Position { get; private set; }
        internal string Name { get; private set; }
        internal long Size { get; private set; }
        internal bool Directory { get; private set; }
        internal bool RegularFile { get; private set; }

        internal bool MoveNext()
        {
            DrainEntry();
            var pending = new Metadata { Path = global.Path, Size = global.Size, Sparse = global.Sparse };
            while (true)
            {
                ReadAll(header, header.Length);
                if (IsZero(header, header.Length))
                {
                    ReadAll(header, header.Length);
                    if (!IsZero(header, header.Length)) throw InvalidTar();
                    int read;
                    while ((read = budget.Read(stream, data, 0, data.Length, token)) > 0)
                        if (!IsZero(data, read)) throw InvalidTar();
                    return false;
                }
                if (++Position > limit) throw new ArchiveFileReader.LimitReachedException();
                VerifyChecksum();
                byte type = header[156];
                long rawSize = Number(header, 124, 12);
                if (type == 'L' || type == 'K' || type == 'x' || type == 'g')
                {
                    if (rawSize > MaxMetadataBytes) throw new ArchiveFileReader.LimitReachedException();
                    byte[] payload = new byte[(int)rawSize];
                    ReadAll(payload, payload.Length);
                    SetEntrySize(rawSize);
                    unread = 0;
                    DrainEntry();
                    if (type == 'L') pending.Path = String(payload, 0, payload.Length);
                    else if (type == 'g')
                    {
                        ReadPax(payload, global);
                        pending.Path = global.Path;
                        pending.Size = global.Size;
                        pending.Sparse = global.Sparse;
                    }
                    else if (type == 'x') ReadPax(payload, pending);
                    continue;
                }
                Name = pending.Path ?? HeaderName();
                Size = pending.Size ?? rawSize;
                Directory = type == '5';
                RegularFile = !pending.Sparse && (type == 0 || type == '0');
                SetEntrySize(Size);
                return true;
            }
        }

        internal void Copy(Stream output)
        {
            while (unread > 0)
            {
                int read = budget.Read(stream, data, 0, (int)Math.Min(unread, data.Length), token);
                if (read == 0) throw InvalidTar();
                output.Write(data, 0, read);
                unread -= read;
            }
        }

        void SetEntrySize(long size)
        {
            unread = size;
            padding = (int)((Block - size % Block) % Block);
        }

        void DrainEntry()
        {
            long remaining = unread + padding;
            while (remaining > 0)
            {
                int read = budget.Read(stream, data, 0, (int)Math.Min(remaining, data.Length), token);
                if (read == 0) throw InvalidTar();
                remaining -= read;
            }
            unread = 0;
            padding = 0;
        }

        void ReadAll(byte[] bytes, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = budget.Read(stream, bytes, offset, count - offset, token);
                if (read == 0) throw InvalidTar();
                offset += read;
            }
        }

        string HeaderName()
        {
            string name = String(header, 0, 100);
            if (String(header, 257, 6) != "ustar") return name;
            string prefix = String(header, 345, 155);
            return prefix.Length == 0 ? name : prefix + "/" + name;
        }

        void VerifyChecksum()
        {
            long stored = Number(header, 148, 8);
            int unsigned = 0;
            int signed = 0;
            for (int index = 0; index < header.Length; index++)
            {
                int value = index >= 148 && index < 156 ? 32 : header[index];
                unsigned += value;
                signed += unchecked((sbyte)value);
            }
            if (stored != unsigned && stored != signed) throw InvalidTar();
        }

        static long Number(byte[] bytes, int start, int count)
        {
            long size = 0;
            try
            {
                checked
                {
                    if ((bytes[start] & 0x80) != 0)
                    {
                        if ((bytes[start] & 0x40) != 0) throw InvalidTar();
                        size = bytes[start] & 0x3f;
                        for (int i = 1; i < count; i++) size = size * 256 + bytes[start + i];
                        if (size > long.MaxValue - Block) throw InvalidTar();
                        return size;
                    }
                    int end = start + count;
                    while (start < end && bytes[start] == ' ') start++;
                    while (end > start && (bytes[end - 1] == 0 || bytes[end - 1] == ' ')) end--;
                    for (int i = start; i < end; i++)
                    {
                        if (bytes[i] < '0' || bytes[i] > '7') throw InvalidTar();
                        size = size * 8 + (bytes[i] - '0');
                    }
                    return size;
                }
            }
            catch (OverflowException) { throw InvalidTar(); }
        }

        static void ReadPax(byte[] payload, Metadata metadata)
        {
            int offset = 0;
            while (offset < payload.Length)
            {
                int separator = Array.IndexOf(payload, (byte)' ', offset);
                int length;
                if (separator <= offset || separator - offset > 9 ||
                    !int.TryParse(Encoding.ASCII.GetString(payload, offset, separator - offset),
                        NumberStyles.None, CultureInfo.InvariantCulture, out length) ||
                    length < separator - offset + 4 || length > payload.Length - offset ||
                    payload[offset + length - 1] != '\n') throw InvalidTar();
                int valueLength = offset + length - separator - 2;
                string record = Encoding.UTF8.GetString(payload, separator + 1, valueLength);
                int equals = record.IndexOf('=');
                if (equals < 1) throw InvalidTar();
                string key = record.Substring(0, equals);
                string value = record.Substring(equals + 1);
                if (key == "path") metadata.Path = value.Length == 0 ? null : value;
                else if (key == "size")
                {
                    long size;
                    if (value.Length == 0) metadata.Size = null;
                    else if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out size) &&
                        size <= long.MaxValue - Block) metadata.Size = size;
                    else throw InvalidTar();
                }
                else if (key.StartsWith("GNU.sparse.", StringComparison.Ordinal) || key == "SCHILY.realsize")
                    metadata.Sparse = true;
                offset += length;
            }
        }

        static bool IsZero(byte[] bytes, int count)
        {
            for (int index = 0; index < count; index++)
                if (bytes[index] != 0) return false;
            return true;
        }

        static string String(byte[] bytes, int offset, int count)
        {
            int zero = Array.IndexOf(bytes, (byte)0, offset, count);
            if (zero >= 0) count = zero - offset;
            return Encoding.UTF8.GetString(bytes, offset, count);
        }

        static InvalidDataException InvalidTar() { return new InvalidDataException("TAR.GZ 条目头或数据损坏。"); }
    }
}
