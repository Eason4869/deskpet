using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpZip = SharpCompress.Archives.Zip.ZipArchive;

namespace Dafeiyu
{
    internal sealed class ArchiveFileReader : IFileContentReader
    {
        const int MaxEntries = 10000;
        const int MaxDepth = 2;
        const long MaxEntryBytes = 64L * 1024 * 1024;
        const long MaxArchiveBytes = 128L * 1024 * 1024;
        const long MaxExpandedBytes = 256L * 1024 * 1024;
        static readonly Regex SafeSuffix = new Regex(@"\A\.[a-z0-9]{1,12}(?:\.[a-z0-9]{1,12})?\z");
        static readonly uint[] CrcTable = Crc32Table();

        internal sealed class LimitReachedException : IOException { }

        internal sealed class ExpansionBudget
        {
            long remaining = MaxExpandedBytes;
            internal int Read(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                int maximum = (int)Math.Min(remaining, count);
                if (maximum == 0) throw new LimitReachedException();
                int read = stream.Read(buffer, offset, maximum);
                remaining -= read;
                return read;
            }
        }

        sealed class Entry
        {
            internal string Id;
            internal string Name;
            internal Action<Stream> Copy;
        }

        sealed class Scan
        {
            internal int Skipped;
            internal bool Limited;
            internal string Detail
            {
                get
                {
                    string skipped = Skipped == 0 ? "" : "已跳过 " + Num(Skipped) + " 项。";
                    return Limited ? "已达到归档清单或展开上限。" + skipped : skipped;
                }
            }
        }

        public FileManifest Inspect(string path, CancellationToken token)
        {
            return Manifest(path, new ExpansionBudget(), token);
        }

        static FileManifest Manifest(string path, ExpansionBudget budget, CancellationToken token)
        {
            var sections = new List<FileSection>();
            Scan scan = Visit(path, delegate(Entry entry)
            {
                sections.Add(new FileSection(entry.Id, entry.Name));
                return false;
            }, budget, token);
            return new FileManifest("archive", sections, scan.Detail);
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            return Read(path, request, new ExpansionBudget(), 1, token);
        }

        static FileReadResult Read(string path, FileReadRequest request, ExpansionBudget budget,
            int depth, CancellationToken token)
        {
            if (string.IsNullOrEmpty(request.Section))
                return List(Manifest(path, budget, token), request.Start, request.Count);
            FileReadResult result = null;
            Scan scan = Visit(path, delegate(Entry entry)
            {
                if (!string.Equals(entry.Id, request.Section, StringComparison.Ordinal)) return false;
                string suffix = FileContentService.Extension(entry.Name);
                if (!SafeSuffix.IsMatch(suffix) || !FileContentService.CanRead(suffix))
                    throw new InvalidDataException("归档条目的文件类型不支持读取。");
                bool isArchive = suffix == ".zip" || suffix == ".tar.gz";
                if (isArchive && depth >= MaxDepth) throw new InvalidDataException("归档最多读取两层。");
                string scratch = CreateScratch();
                string extracted = Path.Combine(scratch, "entry" + suffix);
                try
                {
                    using (var output = new FileStream(extracted, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        entry.Copy(output);
                    var childRequest = ChildRequest(request);
                    var child = isArchive
                        ? Read(extracted, childRequest, budget, depth + 1, token)
                        : FileContentService.ReadPath(extracted, childRequest, token);
                    result = new FileReadResult(child.Text, entry.Name + " [" + entry.Id + "]" +
                        (child.Range.Length == 0 ? "" : " / " + child.Range), child.NextStart, child.Image, child.ImageMediaType);
                    return true;
                }
                finally
                {
                    try { File.Delete(extracted); }
                    finally { Directory.Delete(scratch); }
                }
            }, budget, token);
            if (result != null) return result;
            if (scan.Limited) throw new InvalidDataException("所选条目超出归档清单或展开上限。");
            throw new InvalidDataException("所选归档条目不可用。");
        }

        static string CreateScratch()
        {
#if NET10_0
            return Directory.CreateTempSubdirectory("DeskPet-entry-").FullName;
#else
            string directory = Path.Combine(Path.GetTempPath(), "DeskPet-entry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
#endif
        }

        static FileReadRequest ChildRequest(FileReadRequest parent)
        {
            return new FileReadRequest
            {
                Section = parent.NestedSection, Start = parent.Start, Count = parent.Count,
                StartColumn = parent.StartColumn, ColumnCount = parent.ColumnCount, Operation = parent.Operation,
                EncodingName = parent.EncodingName, Language = parent.Language, Query = parent.Query,
                Column = parent.Column, FilterColumn = parent.FilterColumn, FilterValue = parent.FilterValue,
                FilterOperator = parent.FilterOperator, SortColumn = parent.SortColumn, TimestampMs = parent.TimestampMs
            };
        }

        static FileReadResult List(FileManifest manifest, int start, int count)
        {
            start = Math.Max(1, start);
            count = Math.Max(1, Math.Min(1000, count));
            var output = new StringBuilder();
            int bytes = 0;
            int listed = 0;
            foreach (var section in manifest.Sections.Skip(start - 1).Take(count))
            {
                string line = section.Id + "\t" + section.Label + "\n";
                bytes += Encoding.UTF8.GetByteCount(line);
                if (bytes > 32 * 1024 - 512) break;
                output.Append(line);
                listed++;
            }
            if (!string.IsNullOrEmpty(manifest.Detail)) output.Append(manifest.Detail);
            string range = listed == 0 ? "归档没有可列出的条目" : "条目 " + Num(start) + "–" + Num(start + listed - 1);
            return new FileReadResult(output.ToString(), range, start + listed <= manifest.Sections.Count ? (int?)(start + listed) : null);
        }

        static Scan Visit(string path, Func<Entry, bool> visitor, ExpansionBudget budget, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var scan = new Scan();
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > MaxArchiveBytes) throw new InvalidDataException("归档文件超过 128 MiB。");
                string digest = Digest(file, token);
                try
                {
                    if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                        VisitTar(file, digest, visitor, scan, budget, token);
                    else VisitZip(file, digest, visitor, scan, budget, token);
                }
                catch (LimitReachedException) { scan.Limited = true; }
                catch (SharpCompressException) { throw new InvalidDataException("ZIP 文件损坏或存在不支持的条目。"); }
            }
            return scan;
        }

        static void VisitZip(Stream source, string digest, Func<Entry, bool> visitor, Scan scan,
            ExpansionBudget budget, CancellationToken token)
        {
            if (!SharpZip.IsZipFile(source)) throw new InvalidDataException("ZIP 文件结构无效。");
            source.Position = 0;
            using (var archive = SharpZip.OpenArchive(source))
            {
                int position = 0;
                foreach (IArchiveEntry item in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (++position > MaxEntries) throw new LimitReachedException();
                    if (item.IsDirectory) continue;
                    int attrib = item.Attrib.GetValueOrDefault();
                    int mode = (attrib >> 16) & 0xf000;
                    if (item.IsEncrypted || (attrib & 0x400) != 0 || (mode != 0 && mode != 0x8000) ||
                        !SafeName(item.Key) || item.Size < 0 || item.Size > MaxEntryBytes ||
                        (item.CompressionType != CompressionType.None && item.CompressionType != CompressionType.Deflate &&
                            item.CompressionType != CompressionType.Deflate64))
                    {
                        scan.Skipped++;
                        continue;
                    }
                    var selected = item;
                    if (visitor(new Entry
                    {
                        Id = digest + "-" + Num(position), Name = selected.Key,
                        Copy = output => CopyZip(selected, output, budget, token)
                    })) return;
                }
            }
        }

        static void CopyZip(IArchiveEntry entry, Stream output, ExpansionBudget budget, CancellationToken token)
        {
            using (var input = entry.OpenEntryStream())
            {
                var buffer = new byte[32768];
                long total = 0;
                uint crc = uint.MaxValue;
                int read;
                while ((read = budget.Read(input, buffer, 0, buffer.Length, token)) != 0)
                {
                    total += read;
                    if (total > MaxEntryBytes || total > entry.Size)
                        throw new InvalidDataException("ZIP 条目大小超过限制或与目录记录不一致。");
                    for (int index = 0; index < read; index++)
                        crc = CrcTable[(crc ^ buffer[index]) & 0xff] ^ (crc >> 8);
                    output.Write(buffer, 0, read);
                }
                if (total != entry.Size || ~crc != unchecked((uint)entry.Crc))
                    throw new InvalidDataException("ZIP 条目大小或校验码不匹配。");
            }
        }

        static void VisitTar(Stream source, string digest, Func<Entry, bool> visitor, Scan scan,
            ExpansionBudget budget, CancellationToken token)
        {
            if (source.ReadByte() != 0x1f || source.ReadByte() != 0x8b)
                throw new InvalidDataException("TAR.GZ 文件头无效。");
            source.Position = 0;
            using (var gzip = new GZipStream(source, CompressionMode.Decompress, true))
            {
                var tar = new TarArchiveInput(gzip, budget, MaxEntries, token);
                while (tar.MoveNext())
                {
                    if (tar.Directory) continue;
                    if (!tar.RegularFile || !SafeName(tar.Name) || tar.Size > MaxEntryBytes)
                    {
                        scan.Skipped++;
                        continue;
                    }
                    if (visitor(new Entry
                    {
                        Id = digest + "-" + Num(tar.Position), Name = tar.Name, Copy = tar.Copy
                    })) return;
                }
            }
        }

        static bool SafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 1024 || name.IndexOf(':') >= 0 || name.Any(char.IsControl))
                return false;
            var parts = name.Split('/', '\\');
            if (parts.Length == 0 || parts[0].Length == 0 || parts[parts.Length - 1].Length == 0) return false;
            return !parts.Any(part => part == "..");
        }

        static string Digest(Stream source, CancellationToken token)
        {
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[65536];
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, count, buffer, 0);
                }
                sha.TransformFinalBlock(buffer, 0, 0);
                source.Position = 0;
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        static string Num(int number) { return number.ToString(CultureInfo.InvariantCulture); }

        static uint[] Crc32Table()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                table[index] = value;
            }
            return table;
        }
    }
}
